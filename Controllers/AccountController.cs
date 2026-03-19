using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;
using QuizAPI.Validation;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/account")]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private readonly QuizDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AccountController> _logger;

        public AccountController(QuizDbContext db, UserManager<ApplicationUser> userManager, ILogger<AccountController> logger)
        {
            _db = db;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var roles = await _userManager.GetRolesAsync(user);
            var attempts = await _db.UserQuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == user.Id)
                .ToListAsync();

            return Ok(new AccountProfileDto
            {
                Email = user.Email ?? user.UserName ?? string.Empty,
                FirstName = user.FirstName,
                LastName = user.LastName,
                EmailConfirmed = user.EmailConfirmed,
                Role = roles.FirstOrDefault() ?? "User",
                TotalAttempts = attempts.Count,
                PassedAttempts = attempts.Count(a => a.Passed),
                FailedAttempts = attempts.Count(a => !a.Passed)
            });
        }

        [HttpGet("attempts")]
        public async Task<IActionResult> GetAttempts()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var attempts = await _db.UserQuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.SubmittedUtc)
                .Select(a => new AccountQuizAttemptDto
                {
                    AttemptId = a.Id,
                    QuizId = a.QuizId,
                    QuizTitle = a.QuizTitle,
                    QuizCategory = a.QuizCategory,
                    TotalQuestions = a.TotalQuestions,
                    CorrectCount = a.CorrectCount,
                    ScorePercent = a.ScorePercent,
                    Passed = a.Passed,
                    SubmittedUtc = a.SubmittedUtc
                })
                .ToListAsync();

            return Ok(attempts);
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest? request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var currentPassword = request?.CurrentPassword ?? string.Empty;
            var newPassword = request?.NewPassword ?? string.Empty;
            var confirmPassword = request?.ConfirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                return BadRequest("Current password, new password, and confirmation password are required.");
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
                return BadRequest("New password and confirmation password must match.");

            var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Password change failed for user {UserId}", user.Id);
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "Password update failed."));
            }

            _logger.LogInformation("Password changed for user {UserId}", user.Id);
            return Ok(new { message = "Password updated successfully." });
        }

        private Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Task.FromResult<ApplicationUser?>(null);

            return _userManager.FindByIdAsync(userId);
        }
    }
}
