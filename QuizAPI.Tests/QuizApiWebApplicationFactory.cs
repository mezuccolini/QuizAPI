using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuizAPI.Data;
using QuizAPI.Models;
using QuizAPI.Services;
using Xunit;

namespace QuizAPI.Tests;

public class QuizApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly FakeEmailService _fakeEmailService = new();
    private readonly string _databaseName = $"QuizApiTests_{Guid.NewGuid():N}";
    private readonly IDictionary<string, string?>? _overrides;
    private string ConnectionString
    {
        get
        {
            var baseConnection = Environment.GetEnvironmentVariable("TEST_SQLSERVER_BASE_CONNECTION");
            if (string.IsNullOrWhiteSpace(baseConnection))
            {
                return $"Server=.\\SQLEXPRESS;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True;";
            }

            var builder = new SqlConnectionStringBuilder(baseConnection)
            {
                InitialCatalog = _databaseName
            };

            builder.TrustServerCertificate = true;

            builder.Encrypt = SqlConnectionEncryptOption.Mandatory;

            return builder.ConnectionString;
        }
    }

    public QuizApiWebApplicationFactory()
        : this(null)
    {
    }

    private QuizApiWebApplicationFactory(IDictionary<string, string?>? overrides)
    {
        _overrides = overrides;
        Environment.SetEnvironmentVariable("Jwt__Key", "Integration_Test_Jwt_Key_123456789012345");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "QuizAPI-Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "QuizAPI-Tests-Users");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
        Environment.SetEnvironmentVariable("SampleData__Enabled", "false");
    }

    public static QuizApiWebApplicationFactory CreateWithOverrides(IDictionary<string, string?> overrides)
        => new(overrides);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        if (_overrides is not null && _overrides.Count > 0)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(_overrides);
            });
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton(_fakeEmailService);
            services.AddSingleton<IEmailService>(_fakeEmailService);
        });
    }

    public async Task InitializeAsync()
    {
        using (var cleanupContext = CreateSqlServerContext())
        {
            await cleanupContext.Database.EnsureDeletedAsync();
        }

        await EnsureDatabaseSeededAsync();
    }

    public new async Task DisposeAsync()
    {
        await using (var cleanupContext = CreateSqlServerContext())
        {
            await cleanupContext.Database.EnsureDeletedAsync();
        }

        Environment.SetEnvironmentVariable("Jwt__Key", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("SampleData__Enabled", null);
    }

    public async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "admin@quizapi.local",
            ["password"] = "Admin@123"
        });

        using var response = await client.PostAsync("/api/auth/login", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString() ?? throw new InvalidOperationException("Missing JWT token.");
    }

    public FakeEmailService EmailService => _fakeEmailService;

    public async Task<string> CreateImportFileAsync(string contents, string fileName)
    {
        var path = Path.Combine(GetUploadsDirectory(), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents, Encoding.UTF8);
        return fileName;
    }

    public string GetUploadsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "App_Data", "uploads");
    }

    public async Task EnsureDatabaseSeededAsync()
    {
        _ = Services;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('dbo.Quizzes', 'Category') IS NULL ALTER TABLE dbo.Quizzes ADD Category nvarchar(128) NULL;");

        foreach (var roleName in new[] { "Admin", "User" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        var adminEmail = "admin@quizapi.local";
        var existingUser = await userManager.FindByEmailAsync(adminEmail);
        if (existingUser == null)
        {
            existingUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(existingUser, "Admin@123");
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(existingUser, "Admin"))
        {
            await userManager.AddToRoleAsync(existingUser, "Admin");
        }

        if (!await db.Quizzes.AnyAsync(q => q.Title == "Seed Quiz"))
        {
            db.Quizzes.Add(new Quiz
            {
                Title = "Seed Quiz",
                Category = "Networking",
                Questions =
                {
                    new Question
                    {
                        Text = "Which protocol secures web traffic?",
                        OrderIndex = 1,
                        AllowMultiple = false,
                        Answers =
                        {
                            new Answer { Text = "HTTPS", IsCorrect = true, OrderIndex = 1 },
                            new Answer { Text = "FTP", IsCorrect = false, OrderIndex = 2 }
                        },
                        Images =
                        {
                            new Image
                            {
                                FileName = "seed-diagram.png",
                                ContentType = "image/png",
                                Url = "/uploads/images/test/seed-diagram.png"
                            }
                        }
                    }
                }
            });
        }

        await db.SaveChangesAsync();
    }

    private QuizDbContext CreateSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<QuizDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new QuizDbContext(options);
    }
}

public sealed class FakeEmailService : IEmailService
{
    private readonly List<FakeEmailMessage> _messages = new();

    public IReadOnlyList<FakeEmailMessage> Messages => _messages;

    public Task SendAsync(string toEmail, string subject, string bodyText)
    {
        _messages.Add(new FakeEmailMessage(toEmail, subject, bodyText));
        return Task.CompletedTask;
    }

    public void Clear() => _messages.Clear();
}

public sealed record FakeEmailMessage(string ToEmail, string Subject, string BodyText);
