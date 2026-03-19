param(
    [string]$BaseUrl = "http://localhost:5185"
)

$ErrorActionPreference = "Stop"

function Invoke-SmokeCheck {
    param(
        [string]$Name,
        [string]$Path,
        [int[]]$ExpectedStatusCodes = @(200)
    )

    $url = "$($BaseUrl.TrimEnd('/'))$Path"
    Write-Host "Checking $Name -> $url" -ForegroundColor Yellow

    $response = Invoke-WebRequest -Uri $url -Method Get -SkipHttpErrorCheck
    if ($ExpectedStatusCodes -notcontains [int]$response.StatusCode) {
        throw "$Name failed with HTTP $($response.StatusCode)."
    }

    Write-Host "  OK ($($response.StatusCode))" -ForegroundColor Green
}

Invoke-SmokeCheck -Name "Landing page" -Path "/"
Invoke-SmokeCheck -Name "Liveness health" -Path "/health/live"
Invoke-SmokeCheck -Name "Readiness health" -Path "/health/ready"
Invoke-SmokeCheck -Name "Version endpoint" -Path "/version"

Write-Host ""
Write-Host "Smoke test completed successfully." -ForegroundColor Green
