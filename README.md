# DevQuizAPI

`DevQuizAPI` is a .NET 9 quiz platform for building, importing, managing, and taking quizzes through both API endpoints and simple browser-based pages.

This repository is the active source-of-truth application for the project.

## What It Does

- JWT-based authentication for admin and user access
- quiz listing and quiz-taking APIs
- CSV/TXT quiz import workflow
- uploaded file management
- admin user management
- SMTP configuration and email test endpoints
- browser-based utility pages for auth testing, quiz running, and admin upload/import work

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
- base URL: `http://localhost:5185`
- HTTPS URL: `https://localhost:7193`

### First Run

From your local project directory or Git-cloned repository folder:

```powershell
dotnet build
dotnet run
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

1. Open [upload.html](wwwroot/upload.html)
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
- [upload.html](wwwroot/upload.html): admin dashboard for login, file upload, imports, user management, SMTP settings
- [quiz.html](wwwroot/quiz.html): quiz runner UI

In development, Swagger is available at `/swapi.html`.

## Main API Areas

- `POST /api/auth/login`
- `POST /api/auth/register` admin-only user creation endpoint
- `GET /api/categories`
- `GET /api/quiz`
- `GET /api/quiz/{quizId}/random`
- `POST /api/quiz/{quizId}/submit`
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
- `DevAdmin:Email`
- `DevAdmin:Password`
- `SampleData:Enabled`

For production-like deployments, prefer environment variables or secret storage for:

- JWT signing keys
- SMTP credentials
- connection strings

The checked-in [appsettings.json](appsettings.json) intentionally does not contain a usable production database connection string or JWT signing key.

Recommended production environment variables:

```text
ConnectionStrings__DefaultConnection=Server=YOURSERVER;Database=YOURDB;User Id=YOURUSER;Password=YOURPASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=True;
Jwt__Key=YOUR-LONG-RANDOM-SECRET-AT-LEAST-32-CHARS
Jwt__Issuer=QuizAPI
Jwt__Audience=QuizAPIUsers
Cors__AllowedOrigins__0=https://your-production-site.example
```

Startup will now fail fast if required production configuration is missing.

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

- `DevQuizAPI` is the maintained application going forward.
- A public sample quiz package is included for demo and onboarding purposes.
- The repository is designed for local development, technical preview use, and continued hardening toward beta.
