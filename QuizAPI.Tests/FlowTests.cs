using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using QuizAPI.Data;
using Xunit;

namespace QuizAPI.Tests;

[CollectionDefinition("Integration", DisableParallelization = true)]
public class IntegrationCollection : ICollectionFixture<QuizApiWebApplicationFactory>
{
}

[Collection("Integration")]
public class FlowTests
{
    private readonly QuizApiWebApplicationFactory _factory;

    public FlowTests(QuizApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithSeededAdmin_ReturnsJwtToken()
    {
        var client = _factory.CreateClient();

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "admin@quizapi.local",
            ["password"] = "Admin@123"
        });

        using var response = await client.PostAsync("/api/auth/login", content);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);

        Assert.True(doc.RootElement.TryGetProperty("token", out var tokenElement));
        Assert.False(string.IsNullOrWhiteSpace(tokenElement.GetString()));
    }

    [Fact]
    public async Task GetQuizList_ReturnsSeededQuiz()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/quiz");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("Seed Quiz", payload);
    }

    [Fact]
    public async Task GetRandomizedQuiz_ReturnsSeededImageUrl()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/quiz");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(payload);
        var quizId = listDoc.RootElement.EnumerateArray()
            .First(e => string.Equals(e.GetProperty("Title").GetString(), "Seed Quiz", StringComparison.Ordinal))
            .GetProperty("Id")
            .GetGuid();

        using var randomizedResponse = await client.GetAsync($"/api/quiz/{quizId}/random");
        randomizedResponse.EnsureSuccessStatusCode();

        var randomizedPayload = await randomizedResponse.Content.ReadAsStringAsync();
        Assert.Contains("/uploads/images/test/seed-diagram.png", randomizedPayload);
    }

    [Fact]
    public async Task UploadAndProcessCsv_ImportsQuizIntoDatabase()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var csv = string.Join(Environment.NewLine, new[]
        {
            "QuizTitle,Category,QuestionText,AnswerText,IsCorrect,QuestionImgKey",
            "Integration Import Quiz,Security,What improves account security?,Multi-factor authentication,true,",
            "Integration Import Quiz,Security,What improves account security?,Reused passwords,false,",
            "Integration Import Quiz,Security,Which item is a credential?,API token,true,",
            "Integration Import Quiz,Security,Which item is a credential?,Public logo,false,"
        });

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "File", "integration-import.csv");

        using var uploadResponse = await client.PostAsync("/api/import/upload", multipart);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadMessage = await uploadResponse.Content.ReadAsStringAsync();
        Assert.Contains("Saved import file:", uploadMessage);

        var savedFileName = uploadMessage.Split(':', 2)[1].Trim();

        using var processResponse = await client.PostAsync($"/api/import/process/{savedFileName}", content: null);
        processResponse.EnsureSuccessStatusCode();

        using var verifyResponse = await client.GetAsync("/api/quiz?category=Security");
        verifyResponse.EnsureSuccessStatusCode();

        var verifyPayload = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains("Integration Import Quiz", verifyPayload);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        Assert.True(db.Quizzes.Any(q => q.Title == "Integration Import Quiz"));
    }

    [Fact]
    public async Task PublicRegistration_RequiresEmailVerification_BeforeLoginSucceeds()
    {
        _factory.EmailService.Clear();
        var client = _factory.CreateClient();

        var registerPayload = JsonSerializer.Serialize(new
        {
            email = "newuser@example.com",
            password = "Password!123",
            confirmPassword = "Password!123"
        });

        using var registerResponse = await client.PostAsync(
            "/api/auth/public-register",
            new StringContent(registerPayload, Encoding.UTF8, "application/json"));

        registerResponse.EnsureSuccessStatusCode();
        Assert.Single(_factory.EmailService.Messages);

        using var unverifiedLoginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "newuser@example.com",
            ["password"] = "Password!123"
        });

        using var unverifiedLoginResponse = await client.PostAsync("/api/auth/login", unverifiedLoginContent);
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiedLoginResponse.StatusCode);

        var email = _factory.EmailService.Messages[0];
        var verificationLink = email.BodyText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .First(line => line.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        var verificationPath = new Uri(verificationLink).PathAndQuery;
        using var verificationResponse = await client.GetAsync(verificationPath.Replace("/verify-email.html", "/api/auth/confirm-email", StringComparison.OrdinalIgnoreCase));
        verificationResponse.EnsureSuccessStatusCode();

        using var verifiedLoginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "newuser@example.com",
            ["password"] = "Password!123"
        });

        using var verifiedLoginResponse = await client.PostAsync("/api/auth/login", verifiedLoginContent);
        verifiedLoginResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await userManager.FindByEmailAsync("newuser@example.com");
        Assert.NotNull(user);
        Assert.True(user!.EmailConfirmed);
    }
}
