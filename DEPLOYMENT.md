# Deployment Guide

This document describes a practical deployment path for `DevQuizAPI`.

## Deployment Model

Recommended production shape:

- ASP.NET Core application hosted on Windows with IIS or as a Kestrel-backed service
- SQL Server database
- production secrets supplied through environment variables or secure host configuration

This repository now also includes two PowerShell 5.1 server-installation scripts intended for a bare Windows Server where IIS, SMTP, and SQL Server Express 2019 are present but the application itself is not yet configured.

Current deployment intent for this environment is to serve the app over both `http` and `https` during the transition from dev/test to production.

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
Swagger__Enabled=false
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

## Bare Server Installation Scripts

These scripts are written for PowerShell `5.1.20348.4294` compatibility and assume Administrator elevation.

Place the deployment package under a root folder named `Deployment`, then run:

1. [ensure-server-prerequisites.ps1](scripts/ensure-server-prerequisites.ps1)
2. [install-production-application.ps1](scripts/install-production-application.ps1)
3. [configure-smtp-test.ps1](scripts/configure-smtp-test.ps1) if you want local SMTP testing wired up immediately
4. Copy and edit [production-settings.template.psd1](scripts/production-settings.template.psd1)

Recommended package layout on the target server:

```text
C:\Deployment\
  scripts\
  publish\               optional if you plan to publish on the server
  source\                optional if the full source is copied here
```

The installation script can also publish directly from a copied source tree if `QuizAPI.csproj` is present under the deployment root or under `C:\Deployment\source`.

### Script 1: Ensure Server Prerequisites

[ensure-server-prerequisites.ps1](scripts/ensure-server-prerequisites.ps1) will:

- install required IIS role services and management tools if missing
- download the current .NET 9 SDK from official Microsoft release metadata
- download the current .NET 9 IIS Hosting Bundle from official Microsoft release metadata
- install `dotnet-ef` into `C:\Deployment\tools`
- add `C:\Program Files\dotnet` and `C:\Deployment\tools` to the machine `PATH`
- restart IIS so the hosting bundle is active

Example:

```powershell
powershell.exe -ExecutionPolicy Bypass -File C:\Deployment\scripts\ensure-server-prerequisites.ps1
```

### Script 2: Install The Application And Run Migrations

[install-production-application.ps1](scripts/install-production-application.ps1) will:

- publish the application into `C:\Deployment\publish`
- create/configure the IIS application pool using `No Managed Code`
- point the IIS site root at the deployed publish folder
- bind the IIS site on both HTTP and HTTPS
- write production environment variables into the published `web.config`
- create required runtime folders and assign app pool permissions
- run `dotnet ef database update`
- grant the IIS app pool identity database access for local SQL Express

If no certificate thumbprint is supplied, the installer can create a self-signed certificate for the host name derived from `PublicBaseUrl`.

Recommended settings-file workflow:

```powershell
Copy-Item C:\Deployment\scripts\production-settings.template.psd1 C:\Deployment\scripts\production-settings.psd1
notepad C:\Deployment\scripts\production-settings.psd1
powershell.exe -ExecutionPolicy Bypass -File C:\Deployment\scripts\install-production-application.ps1 `
  -SettingsFile C:\Deployment\scripts\production-settings.psd1
```

The installer reads the PowerShell data file and uses those values as defaults. Any directly passed installer parameters still win if you need to override a value for one run.

Template file:

- [production-settings.template.psd1](scripts/production-settings.template.psd1)

The checked-in template now defaults to a local server profile:

- `PublicBaseUrl = 'http://quizapi.local'`
- `BootstrapAdminEmail = 'admin@quizapi.local'`
- `CorsOrigins = @('http://quizapi.local', 'https://quizapi.local')`

That gives you a consistent starting point for an internal server or first production-style install.

Minimal required values to edit before production use:

- `PublicBaseUrl`
- `JwtKey`
- `BootstrapAdminEmail`
- `BootstrapAdminPassword`
- `DatabaseName` if you do not want the default
- `ConnectionString` if you are not using the default local SQL Express pattern
- `CorsOrigins` if more than the public base URL should be allowed
- `CertificateThumbprint` if you want to use a real server certificate instead of a generated self-signed certificate

HTTP and HTTPS notes:

- the installer configures IIS for both `http` and `https`
- the application-level `HttpsRedirection` setting is left disabled by default so both schemes stay usable during this phase
- once you are ready to force secure traffic, set `EnableHttpsRedirection = $true` in the settings file and rerun the installer

Bootstrap admin notes:

- production does not create the development admin account
- the installer now creates the first admin account intentionally during installation
- `BootstrapAdminEmail` is not automatically derived from `PublicBaseUrl`
- you should usually make them match the same host naming convention for clarity

Recommended example:

```powershell
PublicBaseUrl = 'http://quizapi.local'
BootstrapAdminEmail = 'admin@quizapi.local'
CorsOrigins = @(
    'http://quizapi.local',
    'https://quizapi.local'
)
```

What each setting means:

- `PublicBaseUrl` is the canonical URL the app uses for generated links, deployment identity, and HTTPS host inference
- `BootstrapAdminEmail` is simply the email address of the first admin account the installer creates
- `CorsOrigins` should include every browser origin that is allowed to call the API

If you use `quizapi.local`, make sure that host name resolves on the server and on any client machine that will browse the site. That usually means a DNS entry or a local `hosts` file entry.

If you leave `PublicBaseUrl = 'http://localhost'` and also set `BootstrapAdminEmail = 'admin@localhost'`, the installer will create `admin@localhost`. If you want the login to be `admin@quizapi.local`, set that email explicitly before running the installer.

Default installation assumptions:

- IIS site name: `Default Web Site`
- application pool name: `QuizAPI`
- SQL instance: `.\SQLEXPRESS`
- database name: `QuizAPI`
- deployment root: `C:\Deployment`

If your production host needs different names or paths, override the script parameters.

### Script 3: Configure SMTP For Application Testing

[configure-smtp-test.ps1](scripts/configure-smtp-test.ps1) will:

- verify the Windows SMTP service is installed
- start `IISADMIN` and `SMTPSVC` if needed
- write `C:\Deployment\publish\App_Data\smtp_settings.json` for QuizAPI
- default the sender address from `PublicBaseUrl` when a settings file is present
- optionally send a direct SMTP test email

The script is meant for local application SMTP testing after the install completes. It does not replace real production relay hardening, but it gives the app a working SMTP profile quickly.

Example:

```powershell
powershell.exe -ExecutionPolicy Bypass -File C:\Deployment\scripts\configure-smtp-test.ps1 `
  -DeploymentRoot C:\Deployment `
  -TestRecipientEmail you@example.com
```

How sender defaults are chosen:

- if `production-settings.psd1` exists and `PublicBaseUrl = 'http://quizapi.local'`, the script defaults `FromEmail` to `no-reply@quizapi.local`
- if no host can be derived, it falls back to `BootstrapAdminEmail`
- if neither is available, it falls back to `no-reply@localhost`

This keeps SMTP testing aligned with the same hostname pattern used by `PublicBaseUrl` and `BootstrapAdminEmail`.

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

The production installation script now automates this same migration step by calling `dotnet ef database update` with production settings.

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

Monitoring surfaces now available:

- `/health/live`: process liveness
- `/health/ready`: app + database readiness
- `/health`: combined health payload
- `/version`: app/environment metadata for quick verification

Unhandled API failures return a trace id in the response body. That trace id should be included in any support or operator notes so the matching log entry can be found quickly.

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
- `/health/live` and `/health/ready` are reachable from the deployment target
- runtime folders for `App_Data` and `wwwroot/uploads/images` are writable
- the release notes and changelog match the code being deployed
- the deployment package includes the source tree if the installation server will run `dotnet ef` directly

## Release Checklist

- run a release build
- run the integration tests
- publish to a clean output folder
- apply production migration in a controlled step
- smoke test `/`, `/swapi.html`, and `/health`
- smoke test `/health/live`, `/health/ready`, and `/version`
- verify admin login and a sample quiz import
- verify public login / reset / resend flows return sensible `429` messages under repeated requests
- publish the matching Git tag and GitHub release notes

Recommended automated smoke test:

```powershell
pwsh .\scripts\post-deploy-smoke-test.ps1 -BaseUrl https://your-production-site.example
```

## Post-Deployment Smoke Test

1. Open the landing page
2. Verify `/health/live` returns success
3. Verify `/health/ready` returns success
4. Verify `/version` returns the expected environment
5. Verify `/swapi.html` loads only if Swagger was intentionally enabled
6. Test admin login
7. Test quiz list retrieval
8. Upload and import a sample quiz package
9. Open a quiz and confirm images render
