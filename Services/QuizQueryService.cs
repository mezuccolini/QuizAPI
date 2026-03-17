using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;

namespace QuizAPI.Services
{
    public class QuizQueryService
    {
        private readonly QuizDbContext _db;
        private readonly Random _rng = new();

        public QuizQueryService(QuizDbContext db) => _db = db;

        public async Task<RandomizedQuizDto?> GetRandomizedAsync(Guid quizId)
        {
            var quiz = await _db.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Answers)
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Images)
                .FirstOrDefaultAsync(q => q.Id == quizId);

            if (quiz is null) return null;

            var qShuffled = quiz.Questions.OrderBy(_ => _rng.Next()).ToList();

            return new RandomizedQuizDto
            {
                QuizId = quiz.Id,
                Title = quiz.Title,
                Category = string.IsNullOrWhiteSpace(quiz.Category) ? "Uncategorized" : quiz.Category,
                Questions = qShuffled.Select(q => new QuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    AllowMultiple = q.AllowMultiple,
                    Answers = q.Answers
                        .OrderBy(_ => _rng.Next())
                        .Select(a => new AnswerDto { AnswerId = a.Id, Text = a.Text })
                        .ToList(),
                    Images = q.Images
                        .Select(i => new ImageDto
                        {
                            ImageId = i.Id,
                            FileName = i.FileName,
                            ContentType = i.ContentType,
                            Url = i.Url
                        })
                        .ToList()
                }).ToList()
            };
        }
    }
}
