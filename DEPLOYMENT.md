# Deployment Guide

This document describes a practical deployment path for `DevQuizAPI`.

## Deployment Model

Recommended production shape:

- ASP.NET Core application hosted on Windows with IIS or as a Kestrel-backed service
- SQL Server database
- production secrets supplied through environment variables or secure host configuration

## Required Production Configuration

Do not rely on checked-in `appsettings.json` values for production.

Set these values in the target environment:

```text
ConnectionStrings__DefaultConnection=Server=YOURSERVER;Database=YOURDB;User Id=YOURUSER;Password=YOURPASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=True;
Jwt__Key=YOUR-LONG-RANDOM-SECRET-AT-LEAST-32-CHARS
Jwt__Issuer=TheCertMaster
Jwt__Audience=TheCertMasterUsers
Cors__AllowedOrigins__0=https://your-production-site.example
PublicApp__BaseUrl=https://your-production-site.example
```

Optional production settings:

```text
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft=Warning
RateLimiting__AuthAttemptsPerMinute=8
RateLimiting__GuestQuizLoadsPerMinute=20
RateLimiting__AuthenticatedQuizLoadsPerMinute=60
```

## Build And Publish

From the project root:

```powershell
dotnet build
dotnet publish -c Release -o .\publish
```

Deploy the contents of the `publish` folder to the target server.

You can automate the release publish plus environment-template generation with:

```powershell
pwsh .\scripts\prepare-production.ps1
```

That script:

- generates a `deploy\production.env.example` file
- creates a random JWT secret placeholder
- publishes a `Release` build to `deploy\publish`

## Database

The application automatically migrates the database only in development.

For production:

1. Publish the app
2. Set production environment variables
3. Apply the database migration deliberately before switching traffic to the new app build

Recommended manual migration command:

```powershell
dotnet ef database update
```

Run that against the production connection string in a controlled deployment step.

Migration expectations:

- do not depend on startup to mutate the production database
- run migrations as a deliberate release step
- confirm the target environment is pointing at the intended database before applying migrations
- if the release contains schema changes, do not mark the deployment complete until the migration succeeds

## ASPNETCORE_ENVIRONMENT

Set:

```text
ASPNETCORE_ENVIRONMENT=Production
```

Do not run production as `Development`.

## Public Files And Uploads

Be aware of runtime storage:

- imported CSV/ZIP files are stored under `App_Data/uploads`
- extracted package images are served from `wwwroot/uploads/images`
- data protection keys are stored under `App_Data/keys`

These runtime folders should be persisted appropriately on the host and not treated as source-controlled assets.

## Security Notes

Production behavior now includes:

- rate limits on public authentication-sensitive endpoints
- separate quiz-load rate limits for guests and authenticated users
- automatic admin-session rejection in the browser when a token expires or lacks the `Admin` role
- structured logs for auth failures, rate-limit rejections, password-reset activity, SMTP changes, and admin management actions

If you tighten or relax rate limits in production, record those settings alongside the deployment so operators know the expected thresholds.

## Pre-Deployment Checklist

- production connection string is set
- JWT key is set and strong
- CORS origins are correct
- SMTP settings are configured if email features are needed
- rate-limiting values are confirmed for the target environment
- `PublicApp__BaseUrl` is set correctly for email verification links
- database migration has been applied
- sample/dev credentials are not relied on in production
- `ASPNETCORE_ENVIRONMENT` is set to `Production`
- `/health` is reachable from the deployment target
- runtime folders for `App_Data` and `wwwroot/uploads/images` are writable
- the release notes and changelog match the code being deployed

## Release Checklist

- run a release build
- run the integration tests
- publish to a clean output folder
- apply production migration in a controlled step
- smoke test `/`, `/swapi.html`, and `/health`
- verify admin login and a sample quiz import
- verify public login / reset / resend flows return sensible `429` messages under repeated requests
- publish the matching Git tag and GitHub release notes

## Post-Deployment Smoke Test

1. Open the landing page
2. Verify `/health` returns success
3. Verify `/swapi.html` loads
4. Test admin login
5. Test quiz list retrieval
6. Upload and import a sample quiz package
7. Open a quiz and confirm images render
