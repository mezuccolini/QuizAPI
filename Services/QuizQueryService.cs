using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuizAPI.Services
{
    public class QuizQueryService
    {
        private readonly QuizDbContext _db;
        private readonly Random _rng = new();

        public QuizQueryService(QuizDbContext db) => _db = db;

        public async Task<RandomizedQuizDto?> GetRandomizedAsync(Guid quizId, int? questionCount = null)
        {
            var quiz = await _db.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Answers)
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Images)
                .FirstOrDefaultAsync(q => q.Id == quizId);

            if (quiz is null) return null;

            var qShuffled = quiz.Questions.OrderBy(_ => _rng.Next()).ToList();
            var totalAvailableQuestions = qShuffled.Count;

            if (questionCount.HasValue)
            {
                var normalizedCount = Math.Max(1, questionCount.Value);
                qShuffled = qShuffled.Take(normalizedCount).ToList();
            }

            return new RandomizedQuizDto
            {
                QuizId = quiz.Id,
                Title = quiz.Title,
                Category = string.IsNullOrWhiteSpace(quiz.Category) ? "Uncategorized" : quiz.Category,
                TotalAvailableQuestions = totalAvailableQuestions,
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

        public async Task<CustomQuizAvailabilityDto> GetAvailabilityForCategoriesAsync(IEnumerable<string> categories)
        {
            var normalizedCategories = NormalizeCategories(categories);

            var questions = await _db.Questions
                .AsNoTracking()
                .Include(q => q.Quiz)
                .ToListAsync();

            questions = questions
                .Where(q => normalizedCategories.Contains(NormalizeCategory(q.Quiz?.Category), StringComparer.OrdinalIgnoreCase))
                .ToList();

            var counts = questions
                .GroupBy(q => NormalizeCategory(q.Quiz?.Category))
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            return new CustomQuizAvailabilityDto
            {
                TotalAvailableQuestions = questions.Count,
                CategoryQuestionCounts = counts
            };
        }

        public async Task<RandomizedQuizDto?> GetRandomizedByCategoriesAsync(IEnumerable<string> categories, int? questionCount = null)
        {
            var normalizedCategories = NormalizeCategories(categories);
            if (normalizedCategories.Count == 0)
            {
                return null;
            }

            var questions = await _db.Questions
                .Include(q => q.Answers)
                .Include(q => q.Images)
                .Include(q => q.Quiz)
                .ToListAsync();

            questions = questions
                .Where(q => normalizedCategories.Contains(NormalizeCategory(q.Quiz?.Category), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (questions.Count == 0)
            {
                return null;
            }

            var byCategory = normalizedCategories
                .ToDictionary(
                    category => category,
                    category => questions
                        .Where(q => string.Equals(NormalizeCategory(q.Quiz?.Category), category, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(_ => _rng.Next())
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var pickedQuestions = PickQuestionsForCategories(byCategory, questionCount);

            return new RandomizedQuizDto
            {
                QuizId = Guid.Empty,
                Title = normalizedCategories.Count == 1
                    ? $"{normalizedCategories[0]} Question Pool"
                    : $"Mixed Category Pool ({string.Join(", ", normalizedCategories)})",
                Category = normalizedCategories.Count == 1
                    ? normalizedCategories[0]
                    : string.Join(", ", normalizedCategories),
                TotalAvailableQuestions = questions.Count,
                Questions = pickedQuestions.Select(q => new QuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    AllowMultiple = q.AllowMultiple,
                    Answers = q.Answers
                        .OrderBy(_ => _rng.Next())
                        .Select(a => new AnswerDto
                        {
                            AnswerId = a.Id,
                            Text = a.Text
                        })
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

        private List<Question> PickQuestionsForCategories(Dictionary<string, List<Question>> byCategory, int? questionCount)
        {
            var categoryCount = byCategory.Count;
            var allQuestions = byCategory.Values.SelectMany(v => v).ToList();

            if (categoryCount == 1)
            {
                var onlyCategoryQuestions = byCategory.Values.Single();
                if (!questionCount.HasValue)
                {
                    return onlyCategoryQuestions;
                }

                return onlyCategoryQuestions.Take(Math.Min(questionCount.Value, onlyCategoryQuestions.Count)).ToList();
            }

            if (!questionCount.HasValue)
            {
                return allQuestions;
            }

            var perCategoryTarget = Math.Max(1, questionCount.Value);
            var perCategoryPicked = new List<Question>();
            foreach (var bucket in byCategory.Values)
            {
                perCategoryPicked.AddRange(bucket.Take(Math.Min(perCategoryTarget, bucket.Count)));
            }

            return perCategoryPicked
                .OrderBy(_ => _rng.Next())
                .ToList();
        }

        private static List<string> NormalizeCategories(IEnumerable<string> categories)
        {
            return categories
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(NormalizeCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        }

        private static string NormalizeCategory(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Uncategorized";

            var s = Regex.Replace(input.Trim(), "\\s+", " ");
            var ti = CultureInfo.InvariantCulture.TextInfo;
            return ti.ToTitleCase(s.ToLowerInvariant());
        }
    }
}
