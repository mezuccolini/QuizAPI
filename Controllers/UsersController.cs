using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize(Roles = "Admin")]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // Returns basic user info plus current role for admin UI.
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
                    role = roles.FirstOrDefault() ?? "User"
                });
            }

            return Ok(result);
        }

        // POST: api/users/{email}/reset-password
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
                return BadRequest(result.Errors);

            return Ok($"Password updated for {email}.");
        }

        // POST: api/users
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Email and password are required.");

            if (!await _roleManager.RoleExistsAsync(req.Role))
                return BadRequest($"Role '{req.Role}' does not exist.");

            var existing = await _userManager.FindByEmailAsync(req.Email);
            if (existing != null)
                return BadRequest("User already exists.");

            var user = new IdentityUser { UserName = req.Email, Email = req.Email };
            var result = await _userManager.CreateAsync(user, req.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            await _userManager.AddToRoleAsync(user, req.Role);
            return Ok($"User {req.Email} created with role {req.Role}");
        }

        // PUT: api/users/{email}/role
        [HttpPut("{email}/role")]
        public async Task<IActionResult> UpdateRole(string email, [FromBody] UpdateRoleRequest req)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return NotFound("User not found.");

            if (!await _roleManager.RoleExistsAsync(req.Role))
                return BadRequest($"Role '{req.Role}' does not exist.");

            var oldRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, oldRoles);
            await _userManager.AddToRoleAsync(user, req.Role);

            return Ok($"User {email} role updated to {req.Role}");
        }

        // DELETE: api/users/{email}
        [HttpDelete("{email}")]
        public async Task<IActionResult> DeleteUser(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return NotFound("User not found.");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok($"User {email} deleted.");
        }
    }

    // Request DTOs
    public class CreateUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "User"; // default role
    }

    public class UpdateRoleRequest
    {
        public string Role { get; set; } = "User";
    }

    public class ResetPasswordRequest
    {
        public string NewPassword { get; set; } = string.Empty;
    }
}