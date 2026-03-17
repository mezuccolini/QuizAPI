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
Jwt__Issuer=QuizAPI
Jwt__Audience=QuizAPIUsers
Cors__AllowedOrigins__0=https://your-production-site.example
```

Optional production settings:

```text
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft=Warning
```

## Build And Publish

From the project root:

```powershell
dotnet build
dotnet publish -c Release -o .\publish
```

Deploy the contents of the `publish` folder to the target server.

## Database

The application automatically migrates the database only in development.

For production:

1. Publish the app
2. Set production environment variables
3. Apply the database migration deliberately before or during deployment

Recommended manual migration command:

```powershell
dotnet ef database update
```

Run that against the production connection string in a controlled deployment step.

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

## Pre-Deployment Checklist

- production connection string is set
- JWT key is set and strong
- CORS origins are correct
- SMTP settings are configured if email features are needed
- database migration has been applied
- sample/dev credentials are not relied on in production

## Post-Deployment Smoke Test

1. Open the landing page
2. Verify `/health` returns success
2. Verify `/swapi.html` loads
3. Test admin login
4. Test quiz list retrieval
5. Upload and import a sample quiz package
6. Open a quiz and confirm images render
