using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;
using QuizAPI.Services;
using System.Security.Claims;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuizController : ControllerBase
    {
        private const double PassingScorePercent = 70.0;
        private const int GuestQuestionLimit = 5;
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
        [EnableRateLimiting("QuizLoad")]
        [HttpGet("{quizId:guid}/random")]
        public async Task<IActionResult> GetRandomized(Guid quizId, [FromQuery] int? questionCount)
        {
            var dto = await _query.GetRandomizedAsync(quizId, NormalizeRequestedQuestionCount(questionCount));
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpGet("custom/availability")]
        public async Task<IActionResult> GetCustomAvailability([FromQuery] List<string> categories)
        {
            if (categories == null || categories.Count == 0)
                return BadRequest("At least one category is required.");

            var dto = await _query.GetAvailabilityForCategoriesAsync(categories);
            return Ok(dto);
        }

        [EnableRateLimiting("QuizLoad")]
        [HttpGet("custom/random")]
        public async Task<IActionResult> GetRandomizedCustom([FromQuery] List<string> categories, [FromQuery] int? questionCount)
        {
            if (categories == null || categories.Count == 0)
                return BadRequest("At least one category is required.");

            var dto = await _query.GetRandomizedByCategoriesAsync(categories, NormalizeRequestedQuestionCount(questionCount));
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

            var deliveredQuestionIds = (attempt.DeliveredQuestionIds ?? new List<Guid>())
                .Distinct()
                .ToHashSet();

            var gradedQuestions = deliveredQuestionIds.Count == 0
                ? quiz.Questions.ToList()
                : quiz.Questions.Where(q => deliveredQuestionIds.Contains(q.Id)).ToList();

            if (IsGuestUser() && gradedQuestions.Count > GuestQuestionLimit)
                return BadRequest($"Guest users can submit up to {GuestQuestionLimit} questions per quiz.");

            var result = new QuizAttemptResultDto
            {
                QuizId = quizId,
                TotalQuestions = gradedQuestions.Count
            };

            var correct = 0;

            foreach (var q in gradedQuestions)
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
            result.Passed = result.ScorePercent >= PassingScorePercent;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                _db.UserQuizAttempts.Add(new UserQuizAttempt
                {
                    UserId = userId,
                    QuizId = quiz.Id,
                    QuizTitle = quiz.Title,
                    QuizCategory = string.IsNullOrWhiteSpace(quiz.Category) ? "Uncategorized" : quiz.Category,
                    TotalQuestions = result.TotalQuestions,
                    CorrectCount = result.CorrectCount,
                    ScorePercent = result.ScorePercent,
                    Passed = result.Passed,
                    SubmittedUtc = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }

            return Ok(result);
        }

        [HttpPost("custom/submit")]
        public async Task<IActionResult> SubmitCustom([FromBody] QuizAttemptSubmitDto attempt)
        {
            if (attempt is null)
                return BadRequest("Missing request body.");

            var deliveredQuestionIds = (attempt.DeliveredQuestionIds ?? new List<Guid>())
                .Distinct()
                .ToList();

            if (deliveredQuestionIds.Count == 0)
                return BadRequest("DeliveredQuestionIds is required for custom category quizzes.");

            var questions = await _db.Questions
                .Include(q => q.Answers)
                .Include(q => q.Quiz)
                .AsNoTracking()
                .Where(q => deliveredQuestionIds.Contains(q.Id))
                .ToListAsync();

            if (questions.Count == 0)
                return NotFound();

            if (IsGuestUser())
            {
                var guestCustomQuestionLimit = Math.Max(1, (attempt.SelectedCategories ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count()) * GuestQuestionLimit;

                if (questions.Count > guestCustomQuestionLimit)
                    return BadRequest($"Guest users can submit up to {guestCustomQuestionLimit} questions for this category selection.");
            }

            var incoming = (attempt.Answers ?? new List<QuestionAttemptDto>())
                .GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new QuizAttemptResultDto
            {
                QuizId = Guid.Empty,
                TotalQuestions = questions.Count
            };

            var correct = 0;
            foreach (var q in questions)
            {
                incoming.TryGetValue(q.Id, out var a);
                var selected = (a?.SelectedAnswerIds ?? new List<Guid>())
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                var correctIds = q.Answers
                    .Where(x => x.IsCorrect)
                    .Select(x => x.Id)
                    .OrderBy(x => x)
                    .ToList();

                var isCorrect = !q.AllowMultiple
                    ? selected.Count == 1 && correctIds.Count == 1 && selected[0] == correctIds[0]
                    : selected.SequenceEqual(correctIds);

                if (isCorrect)
                    correct++;

                result.Questions.Add(new QuestionResultDto
                {
                    QuestionId = q.Id,
                    IsCorrect = isCorrect,
                    SelectedAnswerIds = selected
                });
            }

            result.CorrectCount = correct;
            result.ScorePercent = result.TotalQuestions == 0
                ? 0
                : Math.Round((double)result.CorrectCount / result.TotalQuestions * 100.0, 2);
            result.Passed = result.ScorePercent >= PassingScorePercent;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var historyTitle = string.IsNullOrWhiteSpace(attempt.QuizTitle) ? "Custom Category Quiz" : attempt.QuizTitle;
                var selectedCategories = (attempt.SelectedCategories ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                var historyCategory = selectedCategories.Count == 0
                    ? "Mixed Categories"
                    : string.Join(", ", selectedCategories);

                _db.UserQuizAttempts.Add(new UserQuizAttempt
                {
                    UserId = userId,
                    QuizId = Guid.Empty,
                    QuizTitle = historyTitle,
                    QuizCategory = historyCategory,
                    TotalQuestions = result.TotalQuestions,
                    CorrectCount = result.CorrectCount,
                    ScorePercent = result.ScorePercent,
                    Passed = result.Passed,
                    SubmittedUtc = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }

            return Ok(result);
        }

        private int? NormalizeRequestedQuestionCount(int? requestedQuestionCount)
        {
            if (!IsGuestUser())
                return requestedQuestionCount;

            return GuestQuestionLimit;
        }

        private bool IsGuestUser()
        {
            return User?.Identity?.IsAuthenticated != true;
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
