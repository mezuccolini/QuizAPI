using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using QuizAPI.Models;
using QuizAPI.Services;
using QuizAPI.Validation;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration config,
            IEmailService emailService,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _emailService = emailService;
            _logger = logger;
        }

        public sealed class RegisterRequest
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public sealed class PublicRegisterRequest
        {
            public string FirstName { get; set; } = string.Empty;

            public string LastName { get; set; } = string.Empty;

            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public sealed class ResendVerificationRequest
        {
            [EmailAddress]
            public string Email { get; set; } = string.Empty;
        }

        public sealed class CompletePasswordResetRequest
        {
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            public string Code { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            public string NewPassword { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public class LoginRequest
        {
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            var password = request?.Password ?? string.Empty;

            if (!PlainTextInputValidator.TryNormalizePersonName(request?.FirstName, "First name", required: true, out var firstName, out var firstNameError))
                return BadRequest(firstNameError);

            if (!PlainTextInputValidator.TryNormalizePersonName(request?.LastName, "Last name", required: true, out var lastName, out var lastNameError))
                return BadRequest(lastNameError);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return BadRequest("Email and password are required.");
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = true
            };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "User creation failed."));
            }

            await _userManager.AddToRoleAsync(user, "User");
            _logger.LogInformation("Admin-created user {Email}", email);
            return Ok($"User {email} created with role User");
        }

        [AllowAnonymous]
        [EnableRateLimiting("AuthSensitive")]
        [HttpPost("public-register")]
        public async Task<IActionResult> PublicRegister([FromBody] PublicRegisterRequest? request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            var password = request?.Password ?? string.Empty;
            var confirmPassword = request?.ConfirmPassword ?? string.Empty;

            if (!PlainTextInputValidator.TryNormalizePersonName(request?.FirstName, "First name", required: true, out var firstName, out var firstNameError))
                return BadRequest(firstNameError);

            if (!PlainTextInputValidator.TryNormalizePersonName(request?.LastName, "Last name", required: true, out var lastName, out var lastNameError))
                return BadRequest(lastNameError);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                return BadRequest("Email, password, and password confirmation are required.");
            }

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
                return BadRequest("Password and confirmation password must match.");

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                if (!existingUser.EmailConfirmed)
                {
                    await SendVerificationEmailAsync(existingUser);
                    _logger.LogInformation("Re-sent verification during registration attempt for existing unverified account {Email}", email);
                    return Ok(new { message = "Account already exists but is not yet verified. A fresh verification email has been sent." });
                }

                _logger.LogWarning("Public registration attempted with existing verified email {Email}", email);
                return BadRequest("An account with that email address already exists.");
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = false
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "Registration failed."));
            }

            await _userManager.AddToRoleAsync(user, "User");
            await SendVerificationEmailAsync(user);
            _logger.LogInformation("Public user registered and verification email queued for {Email}", email);

            return Ok(new
            {
                message = "Registration successful. Please check your email and confirm your address before signing in."
            });
        }

        [AllowAnonymous]
        [EnableRateLimiting("AuthSensitive")]
        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest? request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email is required.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && !user.EmailConfirmed)
            {
                await SendVerificationEmailAsync(user);
                _logger.LogInformation("Verification email resent for {Email}", email);
            }
            else
            {
                _logger.LogInformation("Verification email resend requested for non-actionable email {Email}", email);
            }

            return Ok(new
            {
                message = "If an unverified account exists for that email address, a verification email has been sent."
            });
        }

        [AllowAnonymous]
        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] string? userId, [FromQuery] string? code)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
                return BadRequest("A valid userId and code are required.");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return BadRequest("User could not be found.");

            string decodedCode;
            try
            {
                decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch
            {
                return BadRequest("Verification code is invalid.");
            }

            var result = await _userManager.ConfirmEmailAsync(user, decodedCode);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Email confirmation failed for user {UserId}", userId);
                return BadRequest("Email verification failed. The code may be invalid or expired.");
            }

            _logger.LogInformation("Email confirmed for user {UserId}", userId);
            return Ok(new { message = "Email address verified successfully." });
        }

        [AllowAnonymous]
        [EnableRateLimiting("AuthSensitive")]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] CompletePasswordResetRequest? request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            var code = request?.Code?.Trim() ?? string.Empty;
            var newPassword = request?.NewPassword ?? string.Empty;
            var confirmPassword = request?.ConfirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                return BadRequest("Email, code, new password, and confirmation password are required.");
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
                return BadRequest("New password and confirmation password must match.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return BadRequest("Password reset link is invalid or expired.");

            string decodedCode;
            try
            {
                decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch
            {
                return BadRequest("Password reset code is invalid.");
            }

            var result = await _userManager.ResetPasswordAsync(user, decodedCode, newPassword);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Password reset failed for {Email}", email);
                return BadRequest(ApiErrorFormatter.SummarizeIdentityErrors(result.Errors, "Password reset failed."));
            }

            _logger.LogInformation("Password reset completed for {Email}", email);
            return Ok(new { message = "Password reset successfully." });
        }

        [AllowAnonymous]
        [EnableRateLimiting("AuthSensitive")]
        [HttpPost("login")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Login([FromForm] LoginRequest? request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            var password = request?.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return BadRequest("Email and password are required.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Login failed for unknown email {Email}", email);
                return Unauthorized("Invalid email or password");
            }

            if (!user.EmailConfirmed)
            {
                _logger.LogWarning("Login blocked for unverified email {Email}", email);
                return Unauthorized("Please verify your email address before signing in.");
            }

            var valid = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
            if (!valid.Succeeded)
            {
                _logger.LogWarning("Login failed for {Email}. LockedOut={LockedOut}", email, valid.IsLockedOut);
                return Unauthorized("Invalid email or password");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles);
            _logger.LogInformation("Login succeeded for {Email}", email);

            return Ok(new { token });
        }

        private string GenerateJwtToken(ApplicationUser user, IList<string> roles)
        {
            var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }
                .Where(v => !string.IsNullOrWhiteSpace(v)));

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? (user.UserName ?? user.Email ?? string.Empty) : displayName),
                new Claim("given_name", user.FirstName ?? string.Empty),
                new Claim("family_name", user.LastName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            foreach (var role in roles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, role));
            }

            var signingKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)
            );

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                expires: DateTime.Now.AddHours(3),
                claims: authClaims,
                signingCredentials: new SigningCredentials(
                    signingKey, SecurityAlgorithms.HmacSha256
                )
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task SendVerificationEmailAsync(ApplicationUser user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var configuredBaseUrl = _config["PublicApp:BaseUrl"]?.Trim().TrimEnd('/');
            var baseUrl = !string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? configuredBaseUrl
                : $"{Request.Scheme}://{Request.Host}";

            var verificationUrl =
                $"{baseUrl}/verify-email.html?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(encodedToken)}";

            var body =
                $"Welcome to The Cert Master.{Environment.NewLine}{Environment.NewLine}" +
                $"Please verify your email address by opening this link:{Environment.NewLine}{verificationUrl}{Environment.NewLine}{Environment.NewLine}" +
                $"If you did not create this account, you can ignore this email.";

            await _emailService.SendAsync(user.Email ?? string.Empty, "Verify your The Cert Master account", body);
        }
    }
}
