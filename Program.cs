using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using QuizAPI.Data;
using QuizAPI.Models;
using QuizAPI.Services;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:Key"];
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
var publicBaseUrl = builder.Configuration["PublicApp:BaseUrl"];
var swaggerEnabled = builder.Configuration.GetValue<bool?>("Swagger:Enabled")
    ?? builder.Environment.IsDevelopment();
var httpsRedirectionEnabled = builder.Configuration.GetValue<bool?>("HttpsRedirection:Enabled")
    ?? builder.Environment.IsDevelopment();
var authAttemptsPerMinute = builder.Configuration.GetValue<int?>("RateLimiting:AuthAttemptsPerMinute")
    ?? (builder.Environment.IsDevelopment() ? 30 : 8);
var guestQuizLoadsPerMinute = builder.Configuration.GetValue<int?>("RateLimiting:GuestQuizLoadsPerMinute")
    ?? (builder.Environment.IsDevelopment() ? 60 : 20);
var authenticatedQuizLoadsPerMinute = builder.Configuration.GetValue<int?>("RateLimiting:AuthenticatedQuizLoadsPerMinute")
    ?? (builder.Environment.IsDevelopment() ? 120 : 60);

if (string.IsNullOrWhiteSpace(jwtIssuer))
    throw new InvalidOperationException("Jwt:Issuer is required. Set it in configuration or with the Jwt__Issuer environment variable.");

if (string.IsNullOrWhiteSpace(jwtAudience))
    throw new InvalidOperationException("Jwt:Audience is required. Set it in configuration or with the Jwt__Audience environment variable.");

if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key is required and must be at least 32 characters long. Set it in configuration or with the Jwt__Key environment variable.");

if (string.IsNullOrWhiteSpace(defaultConnection))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required. Set it in configuration or with the ConnectionStrings__DefaultConnection environment variable.");

if (!builder.Environment.IsDevelopment() &&
    string.Equals(defaultConnection, "SET-IN-ENVIRONMENT", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Production requires a real ConnectionStrings:DefaultConnection value. Replace the placeholder with an environment-specific secret.");
}

if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(publicBaseUrl))
{
    throw new InvalidOperationException("Production requires PublicApp:BaseUrl so email links and browser flows point to the correct public host.");
}

var jwtSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

// Persist DataProtection keys so cookies/auth survive IIS recycles/reboots
var keysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keysPath);

var dataProtection = builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("QuizAPI");

if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

// Controllers & JSON
builder.Services.AddControllers()
    .AddJsonOptions(o => { o.JsonSerializerOptions.PropertyNamingPolicy = null; });
builder.Services.AddProblemDetails();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields =
        HttpLoggingFields.RequestMethod |
        HttpLoggingFields.RequestPath |
        HttpLoggingFields.RequestQuery |
        HttpLoggingFields.ResponseStatusCode |
        HttpLoggingFields.Duration;
});
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Application is running."), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting");
        logger.LogWarning(
            "Rate limit rejected request for {Method} {Path} from {RemoteIp}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"message\":\"Too many requests. Please wait a moment and try again.\"}",
            cancellationToken);
    };

    options.AddPolicy("AuthSensitive", httpContext =>
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"auth:{remoteIp}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authAttemptsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("QuizLoad", httpContext =>
    {
        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.Identity?.Name
                ?? "authenticated";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"quiz-auth:{userId}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authenticatedQuizLoadsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        }

        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"quiz-guest:{remoteIp}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = guestQuizLoadsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Quiz API",
        Version = "v1",
        Description = "Quiz API with JWT Authentication"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token in the format: Bearer {your token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

const string CorsPolicyName = "ConfiguredCors";
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (configuredOrigins.Length > 0)
        {
            policy.WithOrigins(configuredOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    return false;

                return uri.IsLoopback;
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
            return;
        }

        throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one origin outside Development.");
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<QuizDbContext>()
    .AddDefaultTokenProviders();

// Disable cookie auth for API calls; use JWT only
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = PathString.Empty;
    options.AccessDeniedPath = PathString.Empty;
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = jwtSigningKey,
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Name
    };
});

// Call to appsettings.json for SQL Express connection string
builder.Services.AddDbContext<QuizDbContext>(options =>
    options.UseSqlServer(defaultConnection));

// App services
builder.Services.AddScoped<QuizQueryService>();
builder.Services.AddScoped<QuizImportService>();
builder.Services.AddScoped<SampleDataSeeder>();
builder.Services.AddScoped<DatabaseHealthCheck>();

// SMTP settings + email service (supports authenticated and unauthenticated relay)
builder.Services.AddScoped<ISmtpSettingsStore, FileSmtpSettingsStore>();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");

        if (exceptionFeature?.Error != null)
        {
            logger.LogError(
                exceptionFeature.Error,
                "Unhandled exception for {Method} {Path} TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                message = "Something went wrong while processing the request.",
                traceId = context.TraceIdentifier
            }));
            return;
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"Something went wrong. TraceId: {context.TraceIdentifier}");
    });
});

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Quiz API v1");
        c.RoutePrefix = "swapi.html";
    });
}

app.UseForwardedHeaders();
if (httpsRedirectionEnabled)
{
    app.UseHttpsRedirection();
}
app.UseHttpLogging();
app.UseCors(CorsPolicyName);
app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (Activity.Current?.Id is { Length: > 0 } traceId)
    {
        context.Response.Headers["X-Trace-Id"] = traceId;
    }

    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync
});
app.MapGet("/version", (IHostEnvironment environment) =>
{
    var assembly = typeof(Program).Assembly.GetName();
    var informationalVersion = assembly.Version?.ToString() ?? "unknown";

    return Results.Ok(new
    {
        application = assembly.Name ?? "QuizAPI",
        version = informationalVersion,
        environment = environment.EnvironmentName,
        utc = DateTime.UtcNow
    });
})
.AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
    var sampleDataSeeder = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    if (app.Environment.IsDevelopment())
    {
        await db.Database.MigrateAsync();
        await sampleDataSeeder.SeedAsync();
    }

    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    if (!await roleManager.RoleExistsAsync("User"))
        await roleManager.CreateAsync(new IdentityRole("User"));

    var bootstrapAdminEmail = configuration["BootstrapAdmin:Email"]?.Trim();
    var bootstrapAdminPassword = configuration["BootstrapAdmin:Password"];
    var bootstrapAdminFirstName = configuration["BootstrapAdmin:FirstName"]?.Trim();
    var bootstrapAdminLastName = configuration["BootstrapAdmin:LastName"]?.Trim();

    if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail))
    {
        if (string.IsNullOrWhiteSpace(bootstrapAdminPassword))
        {
            throw new InvalidOperationException("BootstrapAdmin:Password is required when BootstrapAdmin:Email is set.");
        }

        var bootstrapAdminUser = await userManager.FindByEmailAsync(bootstrapAdminEmail);
        if (bootstrapAdminUser == null)
        {
            bootstrapAdminUser = new ApplicationUser
            {
                UserName = bootstrapAdminEmail,
                Email = bootstrapAdminEmail,
                FirstName = string.IsNullOrWhiteSpace(bootstrapAdminFirstName) ? "Bootstrap" : bootstrapAdminFirstName,
                LastName = string.IsNullOrWhiteSpace(bootstrapAdminLastName) ? "Admin" : bootstrapAdminLastName,
                EmailConfirmed = true
            };

            var createBootstrapAdminResult = await userManager.CreateAsync(bootstrapAdminUser, bootstrapAdminPassword);
            if (!createBootstrapAdminResult.Succeeded)
            {
                var errors = string.Join("; ", createBootstrapAdminResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to create bootstrap admin user: {errors}");
            }

            startupLogger.LogInformation("Created bootstrap admin account {Email}", bootstrapAdminEmail);
        }
        else
        {
            var updated = false;

            if (!bootstrapAdminUser.EmailConfirmed)
            {
                bootstrapAdminUser.EmailConfirmed = true;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(bootstrapAdminFirstName) &&
                !string.Equals(bootstrapAdminUser.FirstName, bootstrapAdminFirstName, StringComparison.Ordinal))
            {
                bootstrapAdminUser.FirstName = bootstrapAdminFirstName;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(bootstrapAdminLastName) &&
                !string.Equals(bootstrapAdminUser.LastName, bootstrapAdminLastName, StringComparison.Ordinal))
            {
                bootstrapAdminUser.LastName = bootstrapAdminLastName;
                updated = true;
            }

            if (updated)
            {
                var updateBootstrapAdminResult = await userManager.UpdateAsync(bootstrapAdminUser);
                if (!updateBootstrapAdminResult.Succeeded)
                {
                    var errors = string.Join("; ", updateBootstrapAdminResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Failed to update bootstrap admin user: {errors}");
                }
            }

            startupLogger.LogInformation("Bootstrap admin account already exists for {Email}", bootstrapAdminEmail);
        }

        if (!await userManager.IsInRoleAsync(bootstrapAdminUser, "Admin"))
        {
            var addBootstrapAdminRoleResult = await userManager.AddToRoleAsync(bootstrapAdminUser, "Admin");
            if (!addBootstrapAdminRoleResult.Succeeded)
            {
                var errors = string.Join("; ", addBootstrapAdminRoleResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to assign Admin role to bootstrap admin user: {errors}");
            }
        }

        if (!await userManager.IsInRoleAsync(bootstrapAdminUser, "User"))
        {
            await userManager.AddToRoleAsync(bootstrapAdminUser, "User");
        }
    }

    if (app.Environment.IsDevelopment())
    {
        var devAdminEmail = configuration["DevAdmin:Email"] ?? "admin@quizapi.local";
        var devAdminPassword = configuration["DevAdmin:Password"] ?? "Admin@123";

        var devAdminUser = await userManager.FindByEmailAsync(devAdminEmail);
        if (devAdminUser == null)
        {
            devAdminUser = new ApplicationUser
            {
                UserName = devAdminEmail,
                Email = devAdminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(devAdminUser, devAdminPassword);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to create development admin user: {errors}");
            }
        }

        var devAdminRoles = await userManager.GetRolesAsync(devAdminUser);
        if (!devAdminRoles.Contains("Admin"))
        {
            if (devAdminRoles.Count > 0)
                await userManager.RemoveFromRolesAsync(devAdminUser, devAdminRoles);

            var addRoleResult = await userManager.AddToRoleAsync(devAdminUser, "Admin");
            if (!addRoleResult.Succeeded)
            {
                var errors = string.Join("; ", addRoleResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to assign Admin role to development user: {errors}");
            }
        }
    }
}

startupLogger.LogInformation(
    "QuizAPI started in {Environment}. SwaggerEnabled={SwaggerEnabled}. HttpsRedirectionEnabled={HttpsRedirectionEnabled}. BaseUrl={BaseUrl}. Health endpoints: /health/live, /health/ready, /health",
    app.Environment.EnvironmentName,
    swaggerEnabled,
    httpsRedirectionEnabled,
    string.IsNullOrWhiteSpace(publicBaseUrl) ? "not-set" : publicBaseUrl);

app.Run();

public partial class Program { }
