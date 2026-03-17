using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuizController : ControllerBase
    {
        private readonly QuizQueryService _query;
        private readonly QuizDbContext _db;

        public QuizController(QuizQueryService query, QuizDbContext db)
        {
            _query = query;
            _db = db;
        }

        // Lightweight quiz listing for browser pickers / admin UIs
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? category)
        {
            var query = _db.Quizzes.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(category) &&
                !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedCategory = NormalizeCategory(category);

                query = query.Where(q =>
                    (q.Category ?? "Uncategorized") == normalizedCategory);
            }

            var quizzes = await query
                .OrderByDescending(q => q.CreatedUtc)
                .Select(q => new
                {
                    q.Id,
                    q.Title,
                    Category = string.IsNullOrWhiteSpace(q.Category) ? "Uncategorized" : q.Category,
                    q.CreatedUtc
                })
                .ToListAsync();

            return Ok(quizzes);
        }

        // Returns randomized questions & answers on each call
        [HttpGet("{quizId:guid}/random")]
        public async Task<IActionResult> GetRandomized(Guid quizId)
        {
            var dto = await _query.GetRandomizedAsync(quizId);
            return dto is null ? NotFound() : Ok(dto);
        }

        // Submits an attempt and returns a score. Correct answers never leave the server.
        [HttpPost("{quizId:guid}/submit")]
        public async Task<IActionResult> Submit(Guid quizId, [FromBody] QuizAttemptSubmitDto attempt)
        {
            if (attempt is null)
                return BadRequest("Missing request body.");

            var quiz = await _db.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Answers)
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == quizId);

            if (quiz is null)
                return NotFound();

            // Index incoming answers by QuestionId for quick lookup
            var incoming = (attempt.Answers ?? new List<QuestionAttemptDto>())
                .GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new QuizAttemptResultDto
            {
                QuizId = quizId,
                TotalQuestions = quiz.Questions.Count
            };

            var correct = 0;

            foreach (var q in quiz.Questions)
            {
                incoming.TryGetValue(q.Id, out var a);
                var selected = (a?.SelectedAnswerIds ?? new List<Guid>())
                    .Distinct()
                    .ToList();

                // Determine the correct set from DB
                var correctIds = q.Answers
                    .Where(x => x.IsCorrect)
                    .Select(x => x.Id)
                    .OrderBy(x => x)
                    .ToList();

                var selectedIds = selected
                    .OrderBy(x => x)
                    .ToList();

                bool isCorrect;

                if (!q.AllowMultiple)
                {
                    // single-answer question: exactly one selected, and it must be correct
                    isCorrect = selectedIds.Count == 1 && correctIds.Count == 1 && selectedIds[0] == correctIds[0];
                }
                else
                {
                    // multi-answer question: exact set match (order independent)
                    isCorrect = selectedIds.SequenceEqual(correctIds);
                }

                if (isCorrect)
                    correct++;

                result.Questions.Add(new QuestionResultDto
                {
                    QuestionId = q.Id,
                    IsCorrect = isCorrect,
                    SelectedAnswerIds = selectedIds
                });
            }

            result.CorrectCount = correct;
            result.ScorePercent = result.TotalQuestions == 0
                ? 0
                : Math.Round((double)result.CorrectCount / result.TotalQuestions * 100.0, 2);

            return Ok(result);
        }


        [Authorize(Roles = "Admin")]
        [HttpDelete("{quizId:guid}")]
        public async Task<IActionResult> Delete(Guid quizId)
        {
            var quiz = await _db.Quizzes
             .Include(q => q.Questions)
                .FirstOrDefaultAsync(q => q.Id == quizId);

            if (quiz == null)
             return NotFound($"Quiz not found: {quizId}");

            _db.Quizzes.Remove(quiz);
            await _db.SaveChangesAsync();

            return Ok($"Deleted quiz {quizId}");
        }

        private static string NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "Uncategorized";

            var collapsed = System.Text.RegularExpressions.Regex.Replace(category.Trim(), @"\s+", " ");
            var lower = collapsed.ToLowerInvariant();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
        }
    }
}