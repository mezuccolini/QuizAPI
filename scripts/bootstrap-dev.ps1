param(
    [string]$ProjectPath = ".",
    [switch]$RunApp
)

$ErrorActionPreference = "Stop"

$resolvedProjectPath = Resolve-Path $ProjectPath
Set-Location $resolvedProjectPath

Write-Host "== The Cert Master dev bootstrap ==" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK is not installed or not available on PATH."
}

Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore

Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build QuizAPI.sln -c Debug

Write-Host "Applying development database migration..." -ForegroundColor Yellow
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update

Write-Host ""
Write-Host "Development setup is ready." -ForegroundColor Green
Write-Host "Default local admin:" -ForegroundColor Green
Write-Host "  Email:    admin@quizapi.local"
Write-Host "  Password: Admin@123"
Write-Host ""
Write-Host "Available local entry points:" -ForegroundColor Green
Write-Host "  Landing:  http://localhost:5185/"
Write-Host "  Register: http://localhost:5185/register.html"
Write-Host "  Quiz:     http://localhost:5185/quiz.html"
Write-Host "  Admin:    http://localhost:5185/manage.html"
Write-Host "  Swagger:  http://localhost:5185/swapi.html"

if ($RunApp) {
    Write-Host ""
    Write-Host "Starting the app in Development..." -ForegroundColor Yellow
    dotnet run
}
