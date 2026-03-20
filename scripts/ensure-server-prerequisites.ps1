#requires -Version 5.1
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$DeploymentRoot = 'C:\Deployment',
    [string]$DotNetChannel = '9.0',
    [string]$DotNetEfVersion = '9.*',
    [switch]$ForceReinstall
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Add-MachinePathEntry {
    param([string]$PathToAdd)

    if ([string]::IsNullOrWhiteSpace($PathToAdd) -or -not (Test-Path -LiteralPath $PathToAdd)) {
        return
    }

    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $parts = @()
    if (-not [string]::IsNullOrWhiteSpace($machinePath)) {
        $parts = $machinePath.Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    if ($parts -contains $PathToAdd) {
        return
    }

    $updatedPath = (($parts + $PathToAdd) | Select-Object -Unique) -join ';'
    [Environment]::SetEnvironmentVariable('Path', $updatedPath, 'Machine')

    if (-not ($env:Path.Split(';') -contains $PathToAdd)) {
        $env:Path = "$env:Path;$PathToAdd"
    }
}

function Get-DotNetReleaseMetadata {
    param([string]$Channel)

    $metadataUrl = "https://dotnetcli.azureedge.net/dotnet/release-metadata/$Channel/releases.json"
    return Invoke-RestMethod -Uri $metadataUrl -UseBasicParsing
}

function Get-InstallerRecord {
    param(
        [Parameter(Mandatory = $true)]$Files,
        [Parameter(Mandatory = $true)][string]$Name
    )

    foreach ($file in $Files) {
        if ($file.name -eq $Name) {
            return $file
        }
    }

    throw "Installer record '$Name' could not be found in the .NET release metadata."
}

function Download-File {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [string]$ExpectedSha512
    )

    Write-Host "Downloading $Url" -ForegroundColor Yellow
    Invoke-WebRequest -Uri $Url -OutFile $DestinationPath -UseBasicParsing

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha512)) {
        $actualHash = (Get-FileHash -Algorithm SHA512 -Path $DestinationPath).Hash.ToLowerInvariant()
        if ($actualHash -ne $ExpectedSha512.ToLowerInvariant()) {
            throw "Hash validation failed for $DestinationPath."
        }
    }
}

function Install-ExeSilently {
    param(
        [Parameter(Mandatory = $true)][string]$InstallerPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $process = Start-Process -FilePath $InstallerPath -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Installer failed: $InstallerPath exited with code $($process.ExitCode)."
    }
}

function Test-DotNetSdkInstalled {
    param([string]$Channel)

    $dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetExe)) {
        return $false
    }

    $sdks = & $dotnetExe --list-sdks 2>$null
    return [bool]($sdks | Where-Object { $_ -match "^$([regex]::Escape($Channel))\." })
}

function Test-HostingBundleInstalled {
    param([string]$Channel)

    $dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    $ancmPath = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'

    if (-not (Test-Path -LiteralPath $dotnetExe) -or -not (Test-Path -LiteralPath $ancmPath)) {
        return $false
    }

    $runtimes = & $dotnetExe --list-runtimes 2>$null
    return [bool]($runtimes | Where-Object { $_ -match "^Microsoft\.AspNetCore\.App $([regex]::Escape($Channel))\." })
}

function Ensure-WindowsFeatures {
    $featureNames = @(
        'Web-Server',
        'Web-Common-Http',
        'Web-Default-Doc',
        'Web-Static-Content',
        'Web-Http-Errors',
        'Web-Health',
        'Web-Http-Logging',
        'Web-Performance',
        'Web-Stat-Compression',
        'Web-Security',
        'Web-Filtering',
        'Web-App-Dev',
        'Web-Net-Ext45',
        'Web-Asp-Net45',
        'Web-ISAPI-Ext',
        'Web-ISAPI-Filter',
        'Web-Mgmt-Console',
        'Web-Scripting-Tools',
        'NET-Framework-45-Core'
    )

    $missingFeatures = @()
    foreach ($featureName in $featureNames) {
        $feature = Get-WindowsFeature -Name $featureName
        if (-not $feature.Installed) {
            $missingFeatures += $featureName
        }
    }

    if ($missingFeatures.Count -eq 0) {
        Write-Host 'IIS and required Windows features are already installed.' -ForegroundColor Green
        return
    }

    Write-Host ('Installing Windows features: ' + ($missingFeatures -join ', ')) -ForegroundColor Yellow
    $result = Install-WindowsFeature -Name $missingFeatures -IncludeManagementTools
    if (-not $result.Success) {
        throw 'One or more Windows features failed to install.'
    }
}

Write-Step 'Preparing folders'
$installersRoot = Join-Path $DeploymentRoot 'installers'
$toolRoot = Join-Path $DeploymentRoot 'tools'
Ensure-Directory -Path $DeploymentRoot
Ensure-Directory -Path $installersRoot
Ensure-Directory -Path $toolRoot

Write-Step 'Ensuring IIS and required Windows features are installed'
Import-Module ServerManager
Ensure-WindowsFeatures

Write-Step 'Resolving current .NET 9 installers from official Microsoft release metadata'
$releaseMetadata = Get-DotNetReleaseMetadata -Channel $DotNetChannel
$latestRelease = $releaseMetadata.releases | Select-Object -First 1
$sdkInstaller = Get-InstallerRecord -Files $latestRelease.sdk.files -Name 'dotnet-sdk-win-x64.exe'
$hostingInstaller = Get-InstallerRecord -Files $latestRelease.'aspnetcore-runtime'.files -Name 'dotnet-hosting-win.exe'

Write-Step 'Ensuring .NET 9 SDK is installed'
if ($ForceReinstall -or -not (Test-DotNetSdkInstalled -Channel $DotNetChannel)) {
    $sdkInstallerPath = Join-Path $installersRoot $sdkInstaller.name
    Download-File -Url $sdkInstaller.url -DestinationPath $sdkInstallerPath -ExpectedSha512 $sdkInstaller.hash
    Install-ExeSilently -InstallerPath $sdkInstallerPath -Arguments @('/install', '/quiet', '/norestart')
    Write-Host "Installed .NET SDK $($latestRelease.sdk.version)." -ForegroundColor Green
}
else {
    Write-Host '.NET 9 SDK is already installed.' -ForegroundColor Green
}

Write-Step 'Ensuring .NET 9 IIS Hosting Bundle is installed'
if ($ForceReinstall -or -not (Test-HostingBundleInstalled -Channel $DotNetChannel)) {
    $hostingInstallerPath = Join-Path $installersRoot $hostingInstaller.name
    Download-File -Url $hostingInstaller.url -DestinationPath $hostingInstallerPath -ExpectedSha512 $hostingInstaller.hash
    Install-ExeSilently -InstallerPath $hostingInstallerPath -Arguments @('/install', '/quiet', '/norestart', 'OPT_NO_ANCM=0')
    Write-Host 'Installed ASP.NET Core Hosting Bundle.' -ForegroundColor Green
}
else {
    Write-Host '.NET 9 Hosting Bundle is already installed.' -ForegroundColor Green
}

$dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    throw "dotnet.exe was not found after installation."
}

Write-Step 'Ensuring dotnet-ef is installed into the Deployment tools folder'
$dotnetEfExe = Join-Path $toolRoot 'dotnet-ef.exe'
if (Test-Path -LiteralPath $dotnetEfExe) {
    & $dotnetExe tool update dotnet-ef --tool-path $toolRoot --version $DotNetEfVersion | Out-Host
}
else {
    & $dotnetExe tool install dotnet-ef --tool-path $toolRoot --version $DotNetEfVersion | Out-Host
}

Write-Step 'Ensuring PATH entries are available for this server'
Add-MachinePathEntry -PathToAdd (Join-Path $env:ProgramFiles 'dotnet')
Add-MachinePathEntry -PathToAdd $toolRoot

Write-Step 'Restarting IIS services so the hosting bundle is active'
iisreset | Out-Host

Write-Step 'Prerequisite summary'
Write-Host "Deployment root: $DeploymentRoot" -ForegroundColor Green
Write-Host "dotnet path: $dotnetExe" -ForegroundColor Green
Write-Host "dotnet-ef path: $dotnetEfExe" -ForegroundColor Green
Write-Host "Latest .NET release used: $($releaseMetadata.'latest-release')" -ForegroundColor Green
Write-Host "Latest SDK used: $($releaseMetadata.'latest-sdk')" -ForegroundColor Green

Write-Host ''
Write-Host 'Prerequisites are ready. Next run the installation script from the Deployment package.' -ForegroundColor Green
