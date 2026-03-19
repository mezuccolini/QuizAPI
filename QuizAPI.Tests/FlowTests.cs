using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using QuizAPI.Data;
using QuizAPI.Models;
using Xunit;

namespace QuizAPI.Tests;

public class FlowTests : IAsyncLifetime
{
    private readonly QuizApiWebApplicationFactory _factory;

    public FlowTests()
    {
        _factory = new QuizApiWebApplicationFactory();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync() => _factory.DisposeAsync();

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
    public async Task GuestRandomizedQuiz_IsClampedToFiveQuestions()
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

        using var randomizedResponse = await client.GetAsync($"/api/quiz/{quizId}/random?questionCount=25");
        randomizedResponse.EnsureSuccessStatusCode();

        var randomizedPayload = await randomizedResponse.Content.ReadAsStringAsync();
        using var randomizedDoc = JsonDocument.Parse(randomizedPayload);
        Assert.InRange(randomizedDoc.RootElement.GetProperty("Questions").GetArrayLength(), 1, 5);
    }

    [Fact]
    public async Task GuestCustomQuiz_ReturnsFiveQuestionsPerSelectedCategory()
    {
        var client = _factory.CreateClient();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();

            foreach (var category in new[] { "Routing", "Security" })
            {
                var quiz = new Quiz
                {
                    Title = $"{category} Guest Clamp Quiz",
                    Category = category
                };

                for (var i = 1; i <= 10; i++)
                {
                    var question = new Question
                    {
                        Text = $"{category} Guest Question {i}?",
                        OrderIndex = i,
                        AllowMultiple = false
                    };

                    question.Answers.Add(new Answer { Text = $"Correct {i}", IsCorrect = true, OrderIndex = 1 });
                    question.Answers.Add(new Answer { Text = $"Incorrect {i}", IsCorrect = false, OrderIndex = 2 });
                    quiz.Questions.Add(question);
                }

                db.Quizzes.Add(quiz);
            }

            await db.SaveChangesAsync();
        }

        using var randomizedResponse = await client.GetAsync("/api/quiz/custom/random?categories=Routing&categories=Security&questionCount=25");
        randomizedResponse.EnsureSuccessStatusCode();

        var randomizedBody = await randomizedResponse.Content.ReadAsStringAsync();
        using var randomizedDoc = JsonDocument.Parse(randomizedBody);
        Assert.Equal(10, randomizedDoc.RootElement.GetProperty("Questions").GetArrayLength());
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
            firstName = "New",
            lastName = "User",
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
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("newuser@example.com");
        Assert.NotNull(user);
        Assert.True(user!.EmailConfirmed);
        Assert.Equal("New", user.FirstName);
        Assert.Equal("User", user.LastName);
    }

    [Fact]
    public async Task PublicRegistration_RejectsSpecialCharactersInProfileNames()
    {
        var client = _factory.CreateClient();

        var registerPayload = JsonSerializer.Serialize(new
        {
            firstName = "N3w!",
            lastName = "User",
            email = "invalidname@example.com",
            password = "Password!123",
            confirmPassword = "Password!123"
        });

        using var registerResponse = await client.PostAsync(
            "/api/auth/public-register",
            new StringContent(registerPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, registerResponse.StatusCode);
        var body = await registerResponse.Content.ReadAsStringAsync();
        Assert.Contains("First name can only contain letters and spaces.", body);
    }

    [Fact]
    public async Task AuthenticatedQuizSubmission_IsRecordedInAccountHistory()
    {
        _factory.EmailService.Clear();
        var client = _factory.CreateClient();

        var email = "historyuser@example.com";
        var password = "Password!123";

        var registerPayload = JsonSerializer.Serialize(new
        {
            firstName = "History",
            lastName = "User",
            email,
            password,
            confirmPassword = password
        });

        using var registerResponse = await client.PostAsync(
            "/api/auth/public-register",
            new StringContent(registerPayload, Encoding.UTF8, "application/json"));
        registerResponse.EnsureSuccessStatusCode();

        var verificationLink = _factory.EmailService.Messages[0].BodyText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .First(line => line.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        using var verificationResponse = await client.GetAsync(new Uri(verificationLink).PathAndQuery
            .Replace("/verify-email.html", "/api/auth/confirm-email", StringComparison.OrdinalIgnoreCase));
        verificationResponse.EnsureSuccessStatusCode();

        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password
        });

        using var loginResponse = await client.PostAsync("/api/auth/login", loginContent);
        loginResponse.EnsureSuccessStatusCode();

        var loginPayload = await loginResponse.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginPayload);
        var token = loginDoc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var quizListResponse = await client.GetAsync("/api/quiz");
        quizListResponse.EnsureSuccessStatusCode();
        var quizListPayload = await quizListResponse.Content.ReadAsStringAsync();
        using var quizListDoc = JsonDocument.Parse(quizListPayload);
        var quizId = quizListDoc.RootElement.EnumerateArray()
            .First(e => string.Equals(e.GetProperty("Title").GetString(), "Seed Quiz", StringComparison.Ordinal))
            .GetProperty("Id")
            .GetGuid();

        using var randomizedResponse = await client.GetAsync($"/api/quiz/{quizId}/random");
        randomizedResponse.EnsureSuccessStatusCode();
        var randomizedPayload = await randomizedResponse.Content.ReadAsStringAsync();
        using var randomizedDoc = JsonDocument.Parse(randomizedPayload);

        var question = randomizedDoc.RootElement.GetProperty("Questions")[0];
        var questionId = question.GetProperty("QuestionId").GetGuid();
        var correctAnswerId = question.GetProperty("Answers").EnumerateArray()
            .First(a => string.Equals(a.GetProperty("Text").GetString(), "HTTPS", StringComparison.Ordinal))
            .GetProperty("AnswerId")
            .GetGuid();

        var submitPayload = JsonSerializer.Serialize(new
        {
            Answers = new[]
            {
                new
                {
                    QuestionId = questionId,
                    SelectedAnswerIds = new[] { correctAnswerId }
                }
            }
        });

        using var submitResponse = await client.PostAsync(
            $"/api/quiz/{quizId}/submit",
            new StringContent(submitPayload, Encoding.UTF8, "application/json"));
        submitResponse.EnsureSuccessStatusCode();

        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"Passed\":true", submitBody, StringComparison.OrdinalIgnoreCase);

        using var profileResponse = await client.GetAsync("/api/account/me");
        profileResponse.EnsureSuccessStatusCode();
        var profilePayload = await profileResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"TotalAttempts\":1", profilePayload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"FirstName\":\"History\"", profilePayload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"LastName\":\"User\"", profilePayload, StringComparison.OrdinalIgnoreCase);

        using var attemptsResponse = await client.GetAsync("/api/account/attempts");
        attemptsResponse.EnsureSuccessStatusCode();
        var attemptsPayload = await attemptsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Seed Quiz", attemptsPayload);
        Assert.Contains("\"Passed\":true", attemptsPayload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminUserManagement_CanUpdateNames_And_SendPasswordResetLink()
    {
        _factory.EmailService.Clear();
        var client = _factory.CreateClient();
        var adminToken = await _factory.LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createPayload = JsonSerializer.Serialize(new
        {
            firstName = "Taylor",
            lastName = "Original",
            email = "manageduser@example.com",
            password = "Password!123",
            role = "User"
        });

        using var createResponse = await client.PostAsync(
            "/api/users",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));
        createResponse.EnsureSuccessStatusCode();

        var updatePayload = JsonSerializer.Serialize(new
        {
            email = "manageduser@example.com",
            firstName = "Taylor",
            lastName = "Updated"
        });

        using var updateResponse = await client.PutAsync(
            "/api/users/manageduser@example.com/profile",
            new StringContent(updatePayload, Encoding.UTF8, "application/json"));
        updateResponse.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("manageduser@example.com");
            Assert.NotNull(user);
            Assert.Equal("Taylor", user!.FirstName);
            Assert.Equal("Updated", user.LastName);
            Assert.True(user.EmailConfirmed);
        }

        using var sendLinkResponse = await client.PostAsync("/api/users/manageduser@example.com/send-reset-link", content: null);
        sendLinkResponse.EnsureSuccessStatusCode();

        var message = Assert.Single(_factory.EmailService.Messages);
        Assert.Equal("manageduser@example.com", message.ToEmail);
        Assert.Contains("/reset-password.html?", message.BodyText, StringComparison.OrdinalIgnoreCase);

        var resetLink = message.BodyText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .First(line => line.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        var uri = new Uri(resetLink);
        var query = QueryHelpers.ParseQuery(uri.Query);
        var email = query["email"].ToString();
        var code = query["code"].ToString();

        Assert.Equal("manageduser@example.com", email);
        Assert.False(string.IsNullOrWhiteSpace(code));

        var resetPayload = JsonSerializer.Serialize(new
        {
            email,
            code,
            newPassword = "Password!456",
            confirmPassword = "Password!456"
        });

        using var resetResponse = await client.PostAsync(
            "/api/auth/reset-password",
            new StringContent(resetPayload, Encoding.UTF8, "application/json"));
        resetResponse.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = null;

        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "manageduser@example.com",
            ["password"] = "Password!456"
        });

        using var loginResponse = await client.PostAsync("/api/auth/login", loginContent);
        loginResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminUserManagement_RejectsSpecialCharactersInProfileNames()
    {
        var client = _factory.CreateClient();
        var adminToken = await _factory.LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createPayload = JsonSerializer.Serialize(new
        {
            firstName = "Taylor",
            lastName = "User",
            email = "plainnamesonly@example.com",
            password = "Password!123",
            role = "User"
        });

        using var createResponse = await client.PostAsync(
            "/api/users",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));
        createResponse.EnsureSuccessStatusCode();

        var updatePayload = JsonSerializer.Serialize(new
        {
            email = "plainnamesonly@example.com",
            firstName = "Taylor@",
            lastName = "User"
        });

        using var updateResponse = await client.PutAsync(
            "/api/users/plainnamesonly@example.com/profile",
            new StringContent(updatePayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        var body = await updateResponse.Content.ReadAsStringAsync();
        Assert.Contains("First name can only contain letters and spaces.", body);
    }

    [Fact]
    public async Task RandomizedQuiz_CanLimitQuestionCount_And_SubmitScoresOnlyDeliveredSubset()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Guid quizId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();

            var quiz = new Quiz
            {
                Title = "Question Count Quiz",
                Category = "Security"
            };

            for (var i = 1; i <= 25; i++)
            {
                var question = new Question
                {
                    Text = $"Question {i}?",
                    OrderIndex = i,
                    AllowMultiple = false
                };

                var correctAnswer = new Answer { Text = $"Correct {i}", IsCorrect = true, OrderIndex = 1 };
                question.Answers.Add(correctAnswer);
                question.Answers.Add(new Answer { Text = $"Incorrect {i}", IsCorrect = false, OrderIndex = 2 });
                quiz.Questions.Add(question);
            }

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();

            quizId = quiz.Id;
        }

        using var randomizedResponse = await client.GetAsync($"/api/quiz/{quizId}/random?questionCount=20");
        randomizedResponse.EnsureSuccessStatusCode();

        var randomizedPayload = await randomizedResponse.Content.ReadAsStringAsync();
        using var randomizedDoc = JsonDocument.Parse(randomizedPayload);
        var questions = randomizedDoc.RootElement.GetProperty("Questions").EnumerateArray().ToList();

        Assert.Equal(20, questions.Count);
        Assert.Equal(25, randomizedDoc.RootElement.GetProperty("TotalAvailableQuestions").GetInt32());

        var deliveredQuestionIds = questions
            .Select(q => q.GetProperty("QuestionId").GetGuid())
            .ToArray();

        var answeredQuestionId = deliveredQuestionIds[0];
        var selectedAnswerId = questions[0]
            .GetProperty("Answers")
            .EnumerateArray()
            .First()
            .GetProperty("AnswerId")
            .GetGuid();

        var submitPayload = JsonSerializer.Serialize(new
        {
            deliveredQuestionIds = deliveredQuestionIds,
            Answers = new[]
            {
                new
                {
                    QuestionId = answeredQuestionId,
                    SelectedAnswerIds = new[] { selectedAnswerId }
                }
            }
        });

        using var submitResponse = await client.PostAsync(
            $"/api/quiz/{quizId}/submit",
            new StringContent(submitPayload, Encoding.UTF8, "application/json"));
        submitResponse.EnsureSuccessStatusCode();

        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        using var submitDoc = JsonDocument.Parse(submitBody);

        Assert.Equal(20, submitDoc.RootElement.GetProperty("TotalQuestions").GetInt32());
        Assert.Equal(20, submitDoc.RootElement.GetProperty("Questions").GetArrayLength());
    }

    [Fact]
    public async Task CustomCategoryQuiz_DefaultTwentyPerCategory_ReturnsTwentyFromEachSelectedCategory()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();

            foreach (var category in new[] { "Routing", "Security", "Cloud" })
            {
                var quiz = new Quiz
                {
                    Title = $"{category} Pool Quiz",
                    Category = category
                };

                for (var i = 1; i <= 30; i++)
                {
                    var question = new Question
                    {
                        Text = $"{category} Question {i}?",
                        OrderIndex = i,
                        AllowMultiple = false
                    };

                    question.Answers.Add(new Answer { Text = $"Correct {i}", IsCorrect = true, OrderIndex = 1 });
                    question.Answers.Add(new Answer { Text = $"Incorrect {i}", IsCorrect = false, OrderIndex = 2 });
                    quiz.Questions.Add(question);
                }

                db.Quizzes.Add(quiz);
            }

            await db.SaveChangesAsync();
        }

        using var availabilityResponse = await client.GetAsync("/api/quiz/custom/availability?categories=Routing&categories=Security&categories=Cloud");
        availabilityResponse.EnsureSuccessStatusCode();

        var availabilityBody = await availabilityResponse.Content.ReadAsStringAsync();
        using var availabilityDoc = JsonDocument.Parse(availabilityBody);
        Assert.True(availabilityDoc.RootElement.GetProperty("TotalAvailableQuestions").GetInt32() >= 90);

        using var randomizedResponse = await client.GetAsync("/api/quiz/custom/random?categories=Routing&categories=Security&categories=Cloud&questionCount=20");
        randomizedResponse.EnsureSuccessStatusCode();

        var randomizedBody = await randomizedResponse.Content.ReadAsStringAsync();
        using var randomizedDoc = JsonDocument.Parse(randomizedBody);
        var deliveredQuestions = randomizedDoc.RootElement.GetProperty("Questions").EnumerateArray().ToList();

        Assert.Equal(60, deliveredQuestions.Count);

        var deliveredQuestionIds = deliveredQuestions
            .Select(q => q.GetProperty("QuestionId").GetGuid())
            .ToArray();

        var submitPayload = JsonSerializer.Serialize(new
        {
            quizTitle = "Mixed Category Pool",
            selectedCategories = new[] { "Routing", "Security", "Cloud" },
            deliveredQuestionIds,
            Answers = Array.Empty<object>()
        });

        using var submitResponse = await client.PostAsync(
            "/api/quiz/custom/submit",
            new StringContent(submitPayload, Encoding.UTF8, "application/json"));
        submitResponse.EnsureSuccessStatusCode();

        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        using var submitDoc = JsonDocument.Parse(submitBody);
        Assert.Equal(60, submitDoc.RootElement.GetProperty("TotalQuestions").GetInt32());
    }

    [Fact]
    public async Task CustomCategoryQuiz_RequestedTwentyFiveAcrossTwoCategories_ReturnsFiftyTotalQuestions()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();

            foreach (var category in new[] { "Networking", "Security" })
            {
                var quiz = new Quiz
                {
                    Title = $"{category} Mixed Pool Quiz",
                    Category = category
                };

                for (var i = 1; i <= 30; i++)
                {
                    var question = new Question
                    {
                        Text = $"{category} Mixed Question {i}?",
                        OrderIndex = i,
                        AllowMultiple = false
                    };

                    question.Answers.Add(new Answer { Text = $"Correct {i}", IsCorrect = true, OrderIndex = 1 });
                    question.Answers.Add(new Answer { Text = $"Incorrect {i}", IsCorrect = false, OrderIndex = 2 });
                    quiz.Questions.Add(question);
                }

                db.Quizzes.Add(quiz);
            }

            await db.SaveChangesAsync();
        }

        using var randomizedResponse = await client.GetAsync("/api/quiz/custom/random?categories=Networking&categories=Security&questionCount=25");
        randomizedResponse.EnsureSuccessStatusCode();

        var randomizedBody = await randomizedResponse.Content.ReadAsStringAsync();
        using var randomizedDoc = JsonDocument.Parse(randomizedBody);
        var deliveredQuestions = randomizedDoc.RootElement.GetProperty("Questions").EnumerateArray().ToList();

        Assert.Equal(50, deliveredQuestions.Count);
    }

    [Fact]
    public async Task CustomCategoryQuiz_RequestedThirtySixAcrossFourCategories_ReturnsOneHundredFortyFourTotalQuestions()
    {
        var client = _factory.CreateClient();
        var token = await _factory.LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();

            foreach (var category in new[] { "Data Center Virtualization", "Networking", "Security", "Systems" })
            {
                var quiz = new Quiz
                {
                    Title = $"{category} Large Pool Quiz",
                    Category = category
                };

                for (var i = 1; i <= 40; i++)
                {
                    var question = new Question
                    {
                        Text = $"{category} Large Question {i}?",
                        OrderIndex = i,
                        AllowMultiple = false
                    };

                    question.Answers.Add(new Answer { Text = $"Correct {i}", IsCorrect = true, OrderIndex = 1 });
                    question.Answers.Add(new Answer { Text = $"Incorrect {i}", IsCorrect = false, OrderIndex = 2 });
                    quiz.Questions.Add(question);
                }

                db.Quizzes.Add(quiz);
            }

            await db.SaveChangesAsync();
        }

        using var randomizedResponse = await client.GetAsync("/api/quiz/custom/random?categories=Data%20Center%20Virtualization&categories=Networking&categories=Security&categories=Systems&questionCount=36");
        randomizedResponse.EnsureSuccessStatusCode();

        var randomizedBody = await randomizedResponse.Content.ReadAsStringAsync();
        using var randomizedDoc = JsonDocument.Parse(randomizedBody);
        var deliveredQuestions = randomizedDoc.RootElement.GetProperty("Questions").EnumerateArray().ToList();

        Assert.Equal(144, deliveredQuestions.Count);
    }
}
