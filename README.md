# QuizAPI

`QuizAPI` is a .NET 9 quiz platform for building, importing, managing, and taking quizzes through both API endpoints and simple browser-based pages.

This repository is the active source-of-truth application for the project.

## What It Does

- JWT-based authentication for admin and user access
- public self-registration with email verification
- quiz listing and quiz-taking APIs
- persisted signed-in quiz history with pass/fail tracking
- CSV/TXT quiz import workflow
- uploaded file management
- admin user management
- SMTP configuration and email test endpoints
- rate limiting for public auth flows and quiz loading
- browser-based pages for public access, account management, quiz running, and admin upload/import work
- structured health, readiness, and version endpoints for deployment monitoring

## Tech Stack

- ASP.NET Core 9
- Entity Framework Core 9
- SQL Server / SQL Server Express
- ASP.NET Core Identity
- JWT bearer authentication
- Swagger / OpenAPI

## Project Structure

- [Program.cs](Program.cs): app startup, auth, CORS, Swagger, migrations, dev seeding
- [Controllers](Controllers): API endpoints
- [Data](Data): EF Core `DbContext`
- [Models](Models): entities
- [DTO](DTO): request/response models
- [Services](Services): quiz import, query, SMTP, sample data seeding
- [Migrations](Migrations): database schema history
- [wwwroot](wwwroot): static browser pages

## Local Development

### Requirements

- .NET 9 SDK
- SQL Server Express or another reachable SQL Server instance

### Default Development Configuration

Development settings live in [appsettings.Development.json](appsettings.Development.json).

By default, development uses:

- environment: `Development`
- database: `DevQuizDB`
- SQL Server connection: local `.\SQLEXPRESS` with `Encrypt=True` and `TrustServerCertificate=True`
- base URL: `http://localhost:5185`
- HTTPS URL: `https://localhost:7193`

### First Run

From your local project directory or Git-cloned repository folder:

```powershell
dotnet build
dotnet run
```

Or use the bootstrap script to automate the first-time local setup:

```powershell
pwsh .\scripts\bootstrap-dev.ps1
```

On startup in `Development`, the app will:

- apply EF Core migrations automatically
- create the development roles if needed
- create the development admin account if needed
- seed fake sample quiz data if the quiz tables are empty

### Default Development Admin

- email: `admin@quizapi.local`
- password: `Admin@123`

You should change these for any shared or non-local environment.

## Sample Data

This repository is set up to be safe for public use.

- real database files are not committed
- real user data is not committed
- fake demo quiz data can be seeded automatically for local development

Sample data behavior:

- controlled by `SampleData:Enabled`
- defaults to `true` in development
- only seeds when there are no quizzes in the database

## Sample Import Package

A public sample quiz package is included in [samples](samples):

- [sample-quiz-package-1V0-21.20.zip](samples/sample-quiz-package-1V0-21.20.zip)

This package is intended as a demo/template import example and includes:

- one CSV quiz file
- question-linked image assets
- a structure compatible with the ZIP package upload flow in the admin panel

Typical usage:

1. Open [manage.html](wwwroot/manage.html)
2. Log in as an admin
3. Upload the ZIP package
4. Import the extracted CSV file shown by the upload result
5. Open [quiz.html](wwwroot/quiz.html) and take the imported quiz

## Import Format

Detailed import guidance lives in [import-format.md](docs/import-format.md).

Quick summary:

- CSV/TXT imports use rows with `QuizTitle`, `QuestionText`, `AnswerText`, and `IsCorrect`
- `Category` is optional
- `QuestionImgKey` links a question row to an image inside a ZIP package
- ZIP packages should contain one CSV plus an optional `images/` folder

## Browser Entry Points

- [index.html](wwwroot/index.html): public landing page for the application
- [register.html](wwwroot/register.html): public user registration page with email verification flow
- [verify-email.html](wwwroot/verify-email.html): email confirmation landing page
- [account.html](wwwroot/account.html): signed-in user account page with password change and quiz history
- [manage.html](wwwroot/manage.html): admin dashboard for login, file upload, imports, user management, SMTP settings
- [quiz.html](wwwroot/quiz.html): quiz runner UI
- [result-home.html](wwwroot/result-home.html): most recent submitted quiz result page with pass/fail summary and a direct path back to quiz selection

In development, Swagger is available at `/swapi.html`.

## Public User Flow

The application now supports a full public user path:

1. Open [register.html](wwwroot/register.html) and create an account
2. Confirm the email verification link
3. Sign in on [account.html](wwwroot/account.html)
4. Take quizzes on [quiz.html](wwwroot/quiz.html)
5. Submit the quiz to record the result on your account
6. Use the `Home` button after submit to open [result-home.html](wwwroot/result-home.html)

Signed-in quiz attempts are stored on the user account. Anonymous quiz sessions still work, but they are not added to account history.

Current scoring behavior:

- quiz attempts with a score of `70%` or higher are marked as `Pass`
- quiz attempts below `70%` are marked as `Fail`

## Main API Areas

- `POST /api/auth/login`
- `POST /api/auth/register` admin-only user creation endpoint
- `POST /api/auth/public-register`
- `POST /api/auth/resend-verification`
- `GET /api/auth/confirm-email`
- `GET /api/categories`
- `GET /api/quiz`
- `GET /api/quiz/{quizId}/random`
- `POST /api/quiz/{quizId}/submit`
- `GET /api/account/me`
- `GET /api/account/attempts`
- `POST /api/account/change-password`
- `GET /api/files`
- `POST /api/import/upload`
- `POST /api/import/process/{fileName}`
- `GET /api/users`
- `POST /api/users`
- `GET /api/admin/smtp`
- `POST /api/admin/smtp`
- `POST /api/admin/smtp/test`

## Configuration Notes

Important configuration keys:

- `ConnectionStrings:DefaultConnection`
- `Jwt:Key`
- `Jwt:Issuer`
- `Jwt:Audience`
- `Cors:AllowedOrigins`
- `PublicApp:BaseUrl`
- `DevAdmin:Email`
- `DevAdmin:Password`
- `SampleData:Enabled`
- `RateLimiting:AuthAttemptsPerMinute`
- `RateLimiting:GuestQuizLoadsPerMinute`
- `RateLimiting:AuthenticatedQuizLoadsPerMinute`
- `Swagger:Enabled`

## Security Hardening

The current application includes a few intentional hardening layers:

- public registration, login, resend-verification, and password reset are rate-limited
- quiz loading is rate-limited, with stricter limits for guest users than signed-in users
- the admin dashboard restores only verified admin sessions and clears stale or unauthorized tokens automatically
- user profile name fields accept letters and spaces only

Text-input validation notes:

- first name, last name, and SMTP `From Name` are normalized to plain letters and spaces
- passwords and email addresses keep their normal required character flexibility
- technical configuration fields such as SMTP hostnames/usernames remain unrestricted where punctuation is required
- imported quiz content is intentionally not reduced to plain-text-only because question and answer content often needs punctuation and symbols

For production-like deployments, prefer environment variables or secret storage for:

- JWT signing keys
- SMTP credentials
- connection strings

The checked-in [appsettings.json](appsettings.json) intentionally does not contain a usable production database connection string or JWT signing key.

Recommended production environment variables:

```text
ConnectionStrings__DefaultConnection=Server=YOURSERVER;Database=YOURDB;User Id=YOURUSER;Password=YOURPASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=True;
Jwt__Key=YOUR-LONG-RANDOM-SECRET-AT-LEAST-32-CHARS
Jwt__Issuer=TheCertMaster
Jwt__Audience=TheCertMasterUsers
Cors__AllowedOrigins__0=https://your-production-site.example
PublicApp__BaseUrl=https://your-production-site.example
```

Startup will now fail fast if required production configuration is missing.

## Monitoring And Runtime Checks

Operational endpoints:

- `/health/live`: confirms the app process is running
- `/health/ready`: confirms the app can reach the configured database
- `/health`: combined status payload
- `/version`: returns app name, version, environment, and current UTC time

Runtime visibility:

- structured request logging is enabled through ASP.NET Core HTTP logging
- unhandled API errors return a trace id so logs can be matched to user-reported failures
- key auth, admin, SMTP, import, and rate-limit events are logged through `ILogger`

For post-deploy smoke checks, run:

```powershell
pwsh .\scripts\post-deploy-smoke-test.ps1 -BaseUrl https://your-production-site.example
```

## Build

```powershell
dotnet build
```

## Release Workflow

- [CHANGELOG.md](CHANGELOG.md): release history
- [v0.1.0-alpha release notes](docs/releases/v0.1.0-alpha.md)
- [release plan](docs/release-plan.md): criteria for `v0.2.0-beta`
- [release checklist](docs/release-checklist.md): pre-push and release verification steps
- [DEPLOYMENT.md](DEPLOYMENT.md): production deployment guide
- [FAQ.md](docs/FAQ.md): common setup and troubleshooting questions
- [external user testing guide](docs/external-user-testing.md): structured outside-tester workflow for beta readiness

## Automation Helpers

- [bootstrap-dev.ps1](scripts/bootstrap-dev.ps1): restore, build, and initialize a local development setup
- [prepare-production.ps1](scripts/prepare-production.ps1): publish a release build and generate a production env template
- [post-deploy-smoke-test.ps1](scripts/post-deploy-smoke-test.ps1): verify landing page, health endpoints, and version endpoint after deployment
- [appsettings.Production.template.json](appsettings.Production.template.json): production configuration reference

## Health Check

- `GET /health`: lightweight application health endpoint for deployment checks and monitoring

## CI

GitHub Actions CI is configured at [ci.yml](.github/workflows/ci.yml).

The workflow:

- restores dependencies
- builds the solution
- runs the integration test suite

## Git

This project is tracked in Git and uses `main` as the default branch.

Remote:

- [https://github.com/mezuccolini/QuizAPI.git](https://github.com/mezuccolini/QuizAPI.git)

## Notes

- `QuizAPI` is the maintained application going forward.
- A public sample quiz package is included for demo and onboarding purposes.
- The repository is designed for local development, technical preview use, and continued hardening toward beta.
