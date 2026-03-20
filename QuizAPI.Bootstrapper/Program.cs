using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuizAPI.Data;
using QuizAPI.Models;

static string? GetArgument(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

var connectionString = GetArgument(args, "--connection");
var email = GetArgument(args, "--email");
var password = GetArgument(args, "--password");
var firstName = GetArgument(args, "--first-name") ?? "Server";
var lastName = GetArgument(args, "--last-name") ?? "Admin";

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("--connection is required.");
    return 1;
}

if (string.IsNullOrWhiteSpace(email))
{
    Console.Error.WriteLine("--email is required.");
    return 1;
}

if (string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("--password is required.");
    return 1;
}

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddDataProtection();
services.AddDbContext<QuizDbContext>(options => options.UseSqlServer(connectionString));
services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<QuizDbContext>()
    .AddDefaultTokenProviders();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

if (!await roleManager.RoleExistsAsync("Admin"))
{
    var createAdminRoleResult = await roleManager.CreateAsync(new IdentityRole("Admin"));
    if (!createAdminRoleResult.Succeeded)
    {
        Console.Error.WriteLine("Failed to create Admin role: " + string.Join("; ", createAdminRoleResult.Errors.Select(e => e.Description)));
        return 1;
    }
}

if (!await roleManager.RoleExistsAsync("User"))
{
    var createUserRoleResult = await roleManager.CreateAsync(new IdentityRole("User"));
    if (!createUserRoleResult.Succeeded)
    {
        Console.Error.WriteLine("Failed to create User role: " + string.Join("; ", createUserRoleResult.Errors.Select(e => e.Description)));
        return 1;
    }
}

var user = await userManager.FindByEmailAsync(email);
if (user == null)
{
    user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        FirstName = firstName,
        LastName = lastName,
        EmailConfirmed = true
    };

    var createUserResult = await userManager.CreateAsync(user, password);
    if (!createUserResult.Succeeded)
    {
        Console.Error.WriteLine("Failed to create bootstrap admin user: " + string.Join("; ", createUserResult.Errors.Select(e => e.Description)));
        return 1;
    }
}
else
{
    var requiresUpdate = false;

    if (!user.EmailConfirmed)
    {
        user.EmailConfirmed = true;
        requiresUpdate = true;
    }

    if (!string.Equals(user.FirstName, firstName, StringComparison.Ordinal))
    {
        user.FirstName = firstName;
        requiresUpdate = true;
    }

    if (!string.Equals(user.LastName, lastName, StringComparison.Ordinal))
    {
        user.LastName = lastName;
        requiresUpdate = true;
    }

    if (requiresUpdate)
    {
        var updateUserResult = await userManager.UpdateAsync(user);
        if (!updateUserResult.Succeeded)
        {
            Console.Error.WriteLine("Failed to update bootstrap admin user: " + string.Join("; ", updateUserResult.Errors.Select(e => e.Description)));
            return 1;
        }
    }
}

if (!await userManager.IsInRoleAsync(user, "Admin"))
{
    var addAdminRoleResult = await userManager.AddToRoleAsync(user, "Admin");
    if (!addAdminRoleResult.Succeeded)
    {
        Console.Error.WriteLine("Failed to assign Admin role: " + string.Join("; ", addAdminRoleResult.Errors.Select(e => e.Description)));
        return 1;
    }
}

if (!await userManager.IsInRoleAsync(user, "User"))
{
    var addUserRoleResult = await userManager.AddToRoleAsync(user, "User");
    if (!addUserRoleResult.Succeeded)
    {
        Console.Error.WriteLine("Failed to assign User role: " + string.Join("; ", addUserRoleResult.Errors.Select(e => e.Description)));
        return 1;
    }
}

Console.WriteLine($"Bootstrap admin is ready: {email}");
return 0;
