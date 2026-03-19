param(
    [string]$OutputDirectory = ".\deploy",
    [string]$PublicBaseUrl = "https://your-production-site.example",
    [string]$ConnectionString = "Server=YOURSERVER;Database=YOURDB;User Id=YOURUSER;Password=YOURPASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=True;",
    [string]$JwtIssuer = "TheCertMaster",
    [string]$JwtAudience = "TheCertMasterUsers",
    [string]$CorsOrigin = "https://your-production-site.example"
)

$ErrorActionPreference = "Stop"

function New-RandomSecret {
    param([int]$Bytes = 48)

    $buffer = New-Object byte[] $Bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    return [Convert]::ToBase64String($buffer)
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK is not installed or not available on PATH."
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$publishPath = Join-Path $resolvedOutput "publish"
$envTemplatePath = Join-Path $resolvedOutput "production.env.example"

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$jwtKey = New-RandomSecret

$envTemplate = @"
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=$ConnectionString
Jwt__Key=$jwtKey
Jwt__Issuer=$JwtIssuer
Jwt__Audience=$JwtAudience
Cors__AllowedOrigins__0=$CorsOrigin
PublicApp__BaseUrl=$PublicBaseUrl
Swagger__Enabled=false
RateLimiting__AuthAttemptsPerMinute=8
RateLimiting__GuestQuizLoadsPerMinute=20
RateLimiting__AuthenticatedQuizLoadsPerMinute=60
"@

Set-Content -Path $envTemplatePath -Value $envTemplate -Encoding UTF8

Write-Host "Publishing release build..." -ForegroundColor Yellow
dotnet publish -c Release -o $publishPath

Write-Host ""
Write-Host "Production prep is complete." -ForegroundColor Green
Write-Host "Publish output: $publishPath"
Write-Host "Environment template: $envTemplatePath"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Green
Write-Host "  1. Copy production.env.example to your deployment secret store or host config."
Write-Host "  2. Replace placeholder values with real production values."
Write-Host "  3. Run: dotnet ef database update"
Write-Host "  4. Deploy the publish folder to your production host."
