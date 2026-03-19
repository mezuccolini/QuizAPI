using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using QuizAPI.Models;
using QuizAPI.Services;
using System.Text;
using QuizAPI.Validation;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize(Roles = "Admin")]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration config,
            IEmailService emailService,
            ILogger<UsersController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _config = config;
            _emailService = emailService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = _userManager.Users.ToList();

            var result = new List<object>(users.Count);
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                result.Add(new
                {
                    id = user.Id,
                    email = user.Email ?? user.UserName ?? string.Empty,
                    userName = user.UserName ?? user.Email ?? string.Empty,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    role = roles.FirstOrDefault() ?? "User"
                });
            }

            return Ok(result);
        }

        [HttpPost("{email}/reset-password")]
        public async Task<IActionResult> ResetPassword(string email, [FromBody] ResetPasswordRequest req)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email is required.");
            if (req == null || string.IsNullOrWhiteSpace(req.NewPassword))
                return BadRequest("NewPassword is required.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return NotFound("User not found.");

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, req.NewPassword);
            if (!result.Succeeded)
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "Password update failed."));

            _logger.LogInformation("Admin reset password for user {Email}", email);
            return Ok($"Password updated for {email}.");
        }

        [HttpPost("{email}/send-reset-link")]
        public async Task<IActionResult> SendResetLink(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email is required.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return NotFound("User not found.");

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var configuredBaseUrl = _config["PublicApp:BaseUrl"]?.Trim().TrimEnd('/');
            var baseUrl = !string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? configuredBaseUrl
                : $"{Request.Scheme}://{Request.Host}";

            var resetUrl =
                $"{baseUrl}/reset-password.html?email={Uri.EscapeDataString(user.Email ?? string.Empty)}&code={Uri.EscapeDataString(encodedToken)}";

            var body =
                $"A password reset has been requested for your The Cert Master account.{Environment.NewLine}{Environment.NewLine}" +
                $"Open this link to set a new password:{Environment.NewLine}{resetUrl}{Environment.NewLine}{Environment.NewLine}" +
                $"If you did not expect this email, you can ignore it.";

            await _emailService.SendAsync(user.Email ?? string.Empty, "Reset your The Cert Master password", body);
            _logger.LogInformation("Admin sent password reset link to {Email}", email);

            return Ok($"Password reset link sent to {email}.");
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Email and password are required.");

            if (!PlainTextInputValidator.TryNormalizePersonName(req.FirstName, "First name", required: false, out var firstName, out var firstNameError))
                return BadRequest(firstNameError);

            if (!PlainTextInputValidator.TryNormalizePersonName(req.LastName, "Last name", required: false, out var lastName, out var lastNameError))
                return BadRequest(lastNameError);

            if (!await _roleManager.RoleExistsAsync(req.Role))
                return BadRequest($"Role '{req.Role}' does not exist.");

            var existing = await _userManager.FindByEmailAsync(req.Email);
            if (existing != null)
                return BadRequest("User already exists.");

            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "User creation failed."));

            await _userManager.AddToRoleAsync(user, req.Role);
            _logger.LogInformation("Admin created user {Email} with role {Role}", req.Email, req.Role);
            return Ok($"User {req.Email} created with role {req.Role}");
        }

        [HttpPut("{email}/profile")]
        public async Task<IActionResult> UpdateProfile(string email, [FromBody] UpdateUserProfileRequest req)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return NotFound("User not found.");

            if (!PlainTextInputValidator.TryNormalizePersonName(req.FirstName, "First name", required: false, out var firstName, out var firstNameError))
                return BadRequest(firstNameError);

            if (!PlainTextInputValidator.TryNormalizePersonName(req.LastName, "Last name", required: false, out var lastName, out var lastNameError))
                return BadRequest(lastNameError);

            var targetEmail = req.Email?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(targetEmail) &&
                !string.Equals(targetEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                var existing = await _userManager.FindByEmailAsync(targetEmail);
                if (existing != null && !string.Equals(existing.Id, user.Id, StringComparison.Ordinal))
                    return BadRequest("Another user already uses that email address.");

                user.Email = targetEmail;
                user.UserName = targetEmail;
            }

            user.FirstName = firstName;
            user.LastName = lastName;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "Profile update failed."));

            _logger.LogInformation("Admin updated profile for user {Email}", user.Email);
            return Ok($"User {user.Email} profile updated.");
        }

        [HttpPut("{email}/role")]
        public async Task<IActionResult> UpdateRole(string email, [FromBody] UpdateRoleRequest req)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return NotFound("User not found.");

            if (!await _roleManager.RoleExistsAsync(req.Role))
                return BadRequest($"Role '{req.Role}' does not exist.");

            var oldRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, oldRoles);
            await _userManager.AddToRoleAsync(user, req.Role);
            _logger.LogInformation("Admin updated role for {Email} to {Role}", email, req.Role);

            return Ok($"User {email} role updated to {req.Role}");
        }

        [HttpDelete("{email}")]
        public async Task<IActionResult> DeleteUser(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return NotFound("User not found.");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "User deletion failed."));

            _logger.LogInformation("Admin deleted user {Email}", email);
            return Ok($"User {email} deleted.");
        }
    }

    public class CreateUserRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
    }

    public class UpdateRoleRequest
    {
        public string Role { get; set; } = "User";
    }

    public class ResetPasswordRequest
    {
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UpdateUserProfileRequest
    {
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
}
