[CmdletBinding()]
param(
    [string]$DeploymentRoot = 'C:\Deployment',
    [string]$PublishedSitePath = '',
    [string]$SettingsFile = '',
    [string]$SmtpHost = 'localhost',
    [int]$SmtpPort = 25,
    [bool]$UseStartTls = $false,
    [bool]$UseSsl = $false,
    [string]$Username = '',
    [string]$Password = '',
    [string]$FromEmail = '',
    [string]$FromName = 'QuizAPI',
    [string]$TestRecipientEmail = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-TcpPort {
    param(
        [string]$HostName,
        [int]$Port
    )

    $client = $null
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $async = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne(5000, $false)) {
            return $false
        }

        $client.EndConnect($async)
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($client) {
            $client.Dispose()
        }
    }
}

function Resolve-DefaultValuesFromSettings {
    param([string]$SettingsPath)

    if ([string]::IsNullOrWhiteSpace($SettingsPath) -or -not (Test-Path -LiteralPath $SettingsPath)) {
        return $null
    }

    try {
        return Import-PowerShellDataFile -Path $SettingsPath
    }
    catch {
        throw "Unable to parse settings file '$SettingsPath'. $($_.Exception.Message)"
    }
}

function Ensure-ServiceIsRunning {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $service) {
        return
    }

    $serviceConfig = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
    if ($serviceConfig -and $serviceConfig.StartMode -ne 'Auto') {
        Set-Service -Name $Name -StartupType Automatic
    }

    if ($service.Status -ne 'Running') {
        Start-Service -Name $Name
    }
}

if ([string]::IsNullOrWhiteSpace($PublishedSitePath)) {
    $PublishedSitePath = Join-Path $DeploymentRoot 'publish'
}

if ([string]::IsNullOrWhiteSpace($SettingsFile)) {
    $SettingsFile = Join-Path $DeploymentRoot 'scripts\production-settings.psd1'
}

$settings = Resolve-DefaultValuesFromSettings -SettingsPath $SettingsFile

if ($settings) {
    if ([string]::IsNullOrWhiteSpace($FromEmail)) {
        $resolvedHost = ''

        if ($settings.ContainsKey('PublicBaseUrl') -and -not [string]::IsNullOrWhiteSpace($settings.PublicBaseUrl)) {
            try {
                $resolvedHost = ([Uri]$settings.PublicBaseUrl).Host
            }
            catch {
                $resolvedHost = ''
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($resolvedHost)) {
            $FromEmail = "no-reply@$resolvedHost"
        }
        elseif ($settings.ContainsKey('BootstrapAdminEmail') -and -not [string]::IsNullOrWhiteSpace($settings.BootstrapAdminEmail)) {
            $FromEmail = $settings.BootstrapAdminEmail
        }
    }

    if ([string]::IsNullOrWhiteSpace($FromName) -and $settings.ContainsKey('BootstrapAdminFirstName')) {
        $FromName = "$($settings.BootstrapAdminFirstName) QuizAPI".Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($FromEmail)) {
    $FromEmail = 'no-reply@localhost'
}

if (-not (Test-Path -LiteralPath $PublishedSitePath)) {
    throw "Published site path '$PublishedSitePath' does not exist."
}

$appDataPath = Join-Path $PublishedSitePath 'App_Data'
$smtpSettingsPath = Join-Path $appDataPath 'smtp_settings.json'

Write-Step "Checking SMTP service installation"
$smtpService = Get-Service -Name 'SMTPSVC' -ErrorAction SilentlyContinue
if (-not $smtpService) {
    $featureName = 'SMTP-Server'
    $featureStatus = $null

    if (Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue) {
        $featureStatus = Get-WindowsFeature -Name $featureName -ErrorAction SilentlyContinue
    }

    if ($featureStatus -and -not $featureStatus.Installed) {
        throw "The Windows SMTP Server feature is not installed. Install it first or rerun the prerequisite workflow with SMTP enabled."
    }

    throw "The SMTP service 'SMTPSVC' was not found on this server."
}

Write-Step "Starting SMTP services"
Ensure-ServiceIsRunning -Name 'IISADMIN'
Ensure-ServiceIsRunning -Name 'SMTPSVC'

Write-Step "Writing QuizAPI SMTP settings for application testing"
New-Item -ItemType Directory -Path $appDataPath -Force | Out-Null

$payload = [ordered]@{
    Host = $SmtpHost
    Port = $SmtpPort
    UseStartTls = $UseStartTls
    UseSsl = $UseSsl
    Username = $Username
    Password = $Password
    ProtectedPassword = ''
    FromEmail = $FromEmail
    FromName = $FromName
}

$payload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $smtpSettingsPath -Encoding UTF8

Write-Step "Testing SMTP endpoint reachability"
$portReachable = Test-TcpPort -HostName $SmtpHost -Port $SmtpPort
if (-not $portReachable) {
    Write-Warning "Could not connect to $SmtpHost`:$SmtpPort. The application settings were written, but the SMTP endpoint did not accept a local TCP connection."
}

if (-not [string]::IsNullOrWhiteSpace($TestRecipientEmail)) {
    Write-Step "Sending SMTP test message"
    $mailMessage = New-Object System.Net.Mail.MailMessage
    $mailMessage.From = New-Object System.Net.Mail.MailAddress($FromEmail, $FromName)
    [void]$mailMessage.To.Add($TestRecipientEmail)
    $mailMessage.Subject = 'QuizAPI SMTP Test'
    $mailMessage.Body = 'This is a test email from the QuizAPI deployment SMTP test script.'

    $smtpClient = New-Object System.Net.Mail.SmtpClient($SmtpHost, $SmtpPort)
    $smtpClient.EnableSsl = $UseSsl
    $smtpClient.DeliveryMethod = [System.Net.Mail.SmtpDeliveryMethod]::Network
    $smtpClient.UseDefaultCredentials = $false

    if (-not [string]::IsNullOrWhiteSpace($Username)) {
        $smtpClient.Credentials = New-Object System.Net.NetworkCredential($Username, $Password)
    }

    try {
        $smtpClient.Send($mailMessage)
        Write-Host "SMTP test email sent to $TestRecipientEmail" -ForegroundColor Green
    }
    finally {
        $mailMessage.Dispose()
        $smtpClient.Dispose()
    }
}

Write-Host ""
Write-Host "SMTP test configuration is complete." -ForegroundColor Green
Write-Host "Published site path: $PublishedSitePath"
Write-Host "SMTP settings file: $smtpSettingsPath"
Write-Host "SMTP host: $SmtpHost"
Write-Host "SMTP port: $SmtpPort"
Write-Host "From email: $FromEmail"
