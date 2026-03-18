using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using QuizAPI.Services;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;

        public AuthController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IConfiguration config,
            IEmailService emailService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _emailService = emailService;
        }

        public sealed class RegisterRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public sealed class PublicRegisterRequest
        {
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

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return BadRequest("Email and password are required.");

            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            await _userManager.AddToRoleAsync(user, "User");
            return Ok($"User {email} created with role User");
        }

        [AllowAnonymous]
        [HttpPost("public-register")]
        public async Task<IActionResult> PublicRegister([FromBody] PublicRegisterRequest? request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            var password = request?.Password ?? string.Empty;
            var confirmPassword = request?.ConfirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
                return BadRequest("Email, password, and password confirmation are required.");

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
                return BadRequest("Password and confirmation password must match.");

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                if (!existingUser.EmailConfirmed)
                {
                    await SendVerificationEmailAsync(existingUser);
                    return Ok(new { message = "Account already exists but is not yet verified. A fresh verification email has been sent." });
                }

                return BadRequest("An account with that email address already exists.");
            }

            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = false
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            await _userManager.AddToRoleAsync(user, "User");
            await SendVerificationEmailAsync(user);

            return Ok(new
            {
                message = "Registration successful. Please check your email and confirm your address before signing in."
            });
        }

        [AllowAnonymous]
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
                return BadRequest("Email verification failed. The code may be invalid or expired.");

            return Ok(new { message = "Email address verified successfully." });
        }

        [AllowAnonymous]
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
                return Unauthorized("Invalid email or password");

            if (!user.EmailConfirmed)
                return Unauthorized("Please verify your email address before signing in.");

            var valid = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
            if (!valid.Succeeded)
                return Unauthorized("Invalid email or password");

            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles);

            return Ok(new { token });
        }

        private string GenerateJwtToken(IdentityUser user, IList<string> roles)
        {
            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
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

        private async Task SendVerificationEmailAsync(IdentityUser user)
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
