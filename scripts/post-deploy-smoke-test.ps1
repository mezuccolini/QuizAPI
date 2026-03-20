#requires -Version 5.1

param(
    [string]$BaseUrl = "http://localhost",
    [switch]$AllowInvalidCertificate
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$originalCertificateValidationCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
if ($AllowInvalidCertificate) {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
}

function Invoke-SmokeCheck {
    param(
        [string]$Name,
        [string]$Path,
        [int[]]$ExpectedStatusCodes = @(200)
    )

    $url = "$($BaseUrl.TrimEnd('/'))$Path"
    Write-Host "Checking $Name -> $url" -ForegroundColor Yellow

    try {
        $response = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing
        $statusCode = [int]$response.StatusCode
    }
    catch [System.Net.WebException] {
        $webResponse = $_.Exception.Response
        if (-not $webResponse) {
            throw
        }

        $statusCode = [int]$webResponse.StatusCode
    }

    if ($ExpectedStatusCodes -notcontains $statusCode) {
        throw "$Name failed with HTTP $statusCode."
    }

    Write-Host "  OK ($statusCode)" -ForegroundColor Green
}

Invoke-SmokeCheck -Name "Landing page" -Path "/"
Invoke-SmokeCheck -Name "Liveness health" -Path "/health/live"
Invoke-SmokeCheck -Name "Readiness health" -Path "/health/ready"
Invoke-SmokeCheck -Name "Version endpoint" -Path "/version"

Write-Host ""
Write-Host "Smoke test completed successfully." -ForegroundColor Green

if ($AllowInvalidCertificate) {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $originalCertificateValidationCallback
}
