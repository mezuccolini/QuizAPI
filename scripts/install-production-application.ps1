#requires -Version 5.1
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$SettingsFile = '',
    [string]$DeploymentRoot = 'C:\Deployment',
    [string]$SiteName = 'Default Web Site',
    [string]$AppPoolName = 'QuizAPI',
    [int]$HttpPort = 80,
    [int]$HttpsPort = 443,
    [string]$HostName = '',
    [string]$CertificateThumbprint = '',
    [switch]$CreateSelfSignedCertificate,
    [string]$SqlInstance = '.\SQLEXPRESS',
    [string]$DatabaseName = 'QuizAPI',
    [string]$ConnectionString = '',
    [string]$PublicBaseUrl = '',
    [string]$JwtKey = '',
    [string]$JwtIssuer = 'TheCertMaster',
    [string]$JwtAudience = 'TheCertMasterUsers',
    [string]$BootstrapAdminEmail = '',
    [string]$BootstrapAdminPassword = '',
    [string]$BootstrapAdminFirstName = '',
    [string]$BootstrapAdminLastName = '',
    [string[]]$CorsOrigins = @(),
    [int]$AuthAttemptsPerMinute = 8,
    [int]$GuestQuizLoadsPerMinute = 20,
    [int]$AuthenticatedQuizLoadsPerMinute = 60,
    [switch]$EnableSwagger,
    [switch]$EnableHttpsRedirection
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (-not [string]::IsNullOrWhiteSpace($SettingsFile)) {
    if (-not (Test-Path -LiteralPath $SettingsFile)) {
        throw "Settings file was not found: $SettingsFile"
    }

    $resolvedSettingsFile = (Resolve-Path -LiteralPath $SettingsFile).Path
    $settings = Import-PowerShellDataFile -Path $resolvedSettingsFile

    foreach ($entry in $settings.GetEnumerator()) {
        if ($PSBoundParameters.ContainsKey($entry.Key)) {
            continue
        }

        Set-Variable -Name $entry.Key -Value $entry.Value -Scope Script
    }
}

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

function Get-SourceRoot {
    param([string]$DeploymentRootPath)

    $candidates = @(
        (Split-Path -Path $PSScriptRoot -Parent),
        (Join-Path $DeploymentRootPath 'source'),
        $DeploymentRootPath
    )

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $projectPath = Join-Path $candidate 'QuizAPI.csproj'
        if (Test-Path -LiteralPath $projectPath) {
            return $candidate
        }
    }

    throw "Could not locate QuizAPI.csproj. Copy the application source into the Deployment package before running this installer."
}

function Ensure-DotNetInstalled {
    $dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetExe)) {
        throw '.NET was not found. Run ensure-server-prerequisites.ps1 first.'
    }

    return $dotnetExe
}

function Ensure-DotNetEfInstalled {
    param([string]$DeploymentRootPath)

    $dotnetEfExe = Join-Path (Join-Path $DeploymentRootPath 'tools') 'dotnet-ef.exe'
    if (-not (Test-Path -LiteralPath $dotnetEfExe)) {
        throw "dotnet-ef was not found at $dotnetEfExe. Run ensure-server-prerequisites.ps1 first."
    }

    return $dotnetEfExe
}

function Invoke-BootstrapAdminTool {
    param(
        [string]$DotNetExe,
        [string]$SourceRoot,
        [string]$ConnectionStringToUse,
        [string]$Email,
        [string]$Password,
        [string]$FirstName,
        [string]$LastName
    )

    if ([string]::IsNullOrWhiteSpace($Email)) {
        return
    }

    & $DotNetExe run --project (Join-Path $SourceRoot 'QuizAPI.Bootstrapper\QuizAPI.Bootstrapper.csproj') -- `
        --connection $ConnectionStringToUse `
        --email $Email `
        --password $Password `
        --first-name $FirstName `
        --last-name $LastName

    if ($LASTEXITCODE -ne 0) {
        throw 'Bootstrap admin creation failed.'
    }
}

function Ensure-IisModule {
    Import-Module WebAdministration

    $appCmdExe = Join-Path $env:SystemRoot 'System32\inetsrv\appcmd.exe'
    if (-not (Test-Path -LiteralPath $appCmdExe)) {
        throw 'IIS appcmd.exe is not available. Verify IIS was installed correctly.'
    }

    return $appCmdExe
}

function Publish-Application {
    param(
        [string]$DotNetExe,
        [string]$SourceRoot,
        [string]$PublishPath
    )

    Ensure-Directory -Path $PublishPath
    & $DotNetExe publish (Join-Path $SourceRoot 'QuizAPI.csproj') -c Release -o $PublishPath
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed.'
    }
}

function Ensure-AppPool {
    param(
        [string]$AppCmdExe,
        [string]$PoolName
    )

    Import-Module WebAdministration

    if (-not (Test-Path -LiteralPath ("IIS:\AppPools\" + $PoolName))) {
        New-WebAppPool -Name $PoolName | Out-Null
    }

    & $AppCmdExe set apppool "/apppool.name:$PoolName" "/managedRuntimeVersion:" "/managedPipelineMode:Integrated" "/processModel.identityType:ApplicationPoolIdentity" "/processModel.loadUserProfile:true" "/autoStart:true" "/startMode:AlwaysRunning" | Out-Host
    & $AppCmdExe set config -section:system.applicationHost/applicationPools "/[name='$PoolName'].processModel.idleTimeout:00:00:00" /commit:apphost | Out-Host
}

function Ensure-SiteConfiguration {
    param(
        [string]$SiteNameToUse,
        [string]$PublishPath,
        [string]$PoolName,
        [int]$HttpBindingPort
    )

    Import-Module WebAdministration

    if (Test-Path -LiteralPath ("IIS:\Sites\" + $SiteNameToUse)) {
        Set-ItemProperty ("IIS:\Sites\" + $SiteNameToUse) -Name physicalPath -Value $PublishPath
        Set-ItemProperty ("IIS:\Sites\" + $SiteNameToUse) -Name applicationPool -Value $PoolName
    }
    else {
        New-Website -Name $SiteNameToUse -Port $HttpBindingPort -PhysicalPath $PublishPath -ApplicationPool $PoolName | Out-Null
    }

    Start-WebSite -Name $SiteNameToUse | Out-Null
}

function Get-TargetHostName {
    param(
        [string]$ConfiguredHostName,
        [string]$BaseUrl
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredHostName)) {
        return $ConfiguredHostName.Trim()
    }

    $uri = [Uri]$BaseUrl
    return $uri.Host
}

function Ensure-HttpsCertificate {
    param(
        [string]$RequestedThumbprint,
        [string]$DnsName,
        [switch]$AllowCreateSelfSigned
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedThumbprint)) {
        $normalizedThumbprint = $RequestedThumbprint.Replace(' ', '').Trim()
        $existing = Get-Item -Path ("Cert:\LocalMachine\My\" + $normalizedThumbprint) -ErrorAction SilentlyContinue
        if (-not $existing) {
            throw "Certificate thumbprint '$RequestedThumbprint' was not found in Cert:\LocalMachine\My."
        }

        return $normalizedThumbprint
    }

    if (-not $AllowCreateSelfSigned) {
        throw 'No HTTPS certificate thumbprint was supplied and self-signed certificate creation is disabled.'
    }

    $existingByDns = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq "CN=$DnsName" } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if ($existingByDns) {
        return $existingByDns.Thumbprint
    }

    $cert = New-SelfSignedCertificate `
        -DnsName $DnsName `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -FriendlyName "QuizAPI HTTPS ($DnsName)" `
        -NotAfter (Get-Date).AddYears(2)

    return $cert.Thumbprint
}

function Ensure-SiteBindings {
    param(
        [string]$SiteNameToUse,
        [int]$HttpBindingPort,
        [int]$HttpsBindingPort,
        [string]$DnsName,
        [string]$HttpsCertificateThumbprint
    )

    Import-Module WebAdministration

    $httpBindingInfo = "*:${HttpBindingPort}:"
    $existingHttpBinding = Get-WebBinding -Name $SiteNameToUse -Protocol 'http' -ErrorAction SilentlyContinue |
        Where-Object { $_.bindingInformation -eq $httpBindingInfo }

    if (-not $existingHttpBinding) {
        New-WebBinding -Name $SiteNameToUse -Protocol 'http' -Port $HttpBindingPort -IPAddress '*' | Out-Null
    }

    $httpsBindingInfo = "*:${HttpsBindingPort}:"
    $existingHttpsBinding = Get-WebBinding -Name $SiteNameToUse -Protocol 'https' -ErrorAction SilentlyContinue |
        Where-Object { $_.bindingInformation -eq $httpsBindingInfo }

    if (-not $existingHttpsBinding) {
        New-WebBinding -Name $SiteNameToUse -Protocol 'https' -Port $HttpsBindingPort -IPAddress '*' | Out-Null
    }

    $sslBindingPath = "IIS:\SslBindings\0.0.0.0!$HttpsBindingPort"
    if (Test-Path -LiteralPath $sslBindingPath) {
        Remove-Item -LiteralPath $sslBindingPath -Force
    }

    Get-Item -LiteralPath ("Cert:\LocalMachine\My\" + $HttpsCertificateThumbprint) | New-Item -Path $sslBindingPath -Force | Out-Null
}

function Stop-SiteAndPoolIfPresent {
    param(
        [string]$SiteNameToUse,
        [string]$PoolName
    )

    Import-Module WebAdministration

    if (Test-Path -LiteralPath ("IIS:\Sites\" + $SiteNameToUse)) {
        $site = Get-Website -Name $SiteNameToUse
        if ($site -and $site.State -ne 'Stopped') {
            Stop-WebSite -Name $SiteNameToUse | Out-Null
        }
    }

    if (Test-Path -LiteralPath ("IIS:\AppPools\" + $PoolName)) {
        $appPoolState = Get-WebAppPoolState -Name $PoolName
        if ($appPoolState -and $appPoolState.Value -ne 'Stopped') {
            Stop-WebAppPool -Name $PoolName | Out-Null
        }
    }
}

function Start-SiteAndPool {
    param(
        [string]$SiteNameToUse,
        [string]$PoolName
    )

    Import-Module WebAdministration
    Start-WebAppPool -Name $PoolName | Out-Null
    Start-WebSite -Name $SiteNameToUse | Out-Null
}

function Set-WebConfigEnvironmentVariables {
    param(
        [string]$WebConfigPath,
        [hashtable]$EnvironmentVariables
    )

    [xml]$webConfig = Get-Content -LiteralPath $WebConfigPath
    $configurationNode = $webConfig.SelectSingleNode('/configuration')
    if (-not $configurationNode) {
        throw "Invalid web.config at $WebConfigPath"
    }

    $systemWebServerNode = $webConfig.SelectSingleNode('/configuration/system.webServer')
    if (-not $systemWebServerNode) {
        $systemWebServerNode = $webConfig.SelectSingleNode('/configuration/location/system.webServer')
    }
    if (-not $systemWebServerNode) {
        $systemWebServerNode = $webConfig.CreateElement('system.webServer')
        [void]$configurationNode.AppendChild($systemWebServerNode)
    }

    $aspNetCoreNode = $webConfig.SelectSingleNode('//aspNetCore')
    if (-not $aspNetCoreNode) {
        throw 'The published web.config does not contain a system.webServer/aspNetCore node.'
    }

    $environmentVariablesNode = $aspNetCoreNode.SelectSingleNode('./environmentVariables')
    if (-not $environmentVariablesNode) {
        $environmentVariablesNode = $webConfig.CreateElement('environmentVariables')
        [void]$aspNetCoreNode.AppendChild($environmentVariablesNode)
    }

    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value

        $existingNode = $null
        foreach ($envNode in $environmentVariablesNode.environmentVariable) {
            if ($envNode.name -eq $name) {
                $existingNode = $envNode
                break
            }
        }

        if ($existingNode -eq $null) {
            $existingNode = $webConfig.CreateElement('environmentVariable')
            [void]$existingNode.SetAttribute('name', $name)
            [void]$environmentVariablesNode.AppendChild($existingNode)
        }

        [void]$existingNode.SetAttribute('value', $value)
    }

    $webConfig.Save($WebConfigPath)
}

function Grant-DirectoryRights {
    param(
        [string]$Path,
        [string]$Identity,
        [System.Security.AccessControl.FileSystemRights]$Rights
    )

    $acl = Get-Acl -LiteralPath $Path
    $inheritanceFlags = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagationFlags = [System.Security.AccessControl.PropagationFlags]::None
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($Identity, $Rights, $inheritanceFlags, $propagationFlags, 'Allow')
    $acl.SetAccessRule($accessRule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Grant-ApplicationAccess {
    param(
        [string]$Path,
        [string]$AppPoolName,
        [System.Security.AccessControl.FileSystemRights]$Rights
    )

    $preferredIdentity = "IIS APPPOOL\$AppPoolName"

    try {
        Grant-DirectoryRights -Path $Path -Identity $preferredIdentity -Rights $Rights
        Write-Host "Granted $Rights to $preferredIdentity on $Path" -ForegroundColor Green
        return
    }
    catch [System.Management.Automation.MethodInvocationException] {
        Write-Warning "Could not grant rights to $preferredIdentity on $Path. Falling back to IIS_IUSRS."
    }
    catch {
        Write-Warning "Could not grant rights to $preferredIdentity on $Path. Falling back to IIS_IUSRS."
    }

    Grant-DirectoryRights -Path $Path -Identity 'IIS_IUSRS' -Rights $Rights
    Write-Host "Granted $Rights to IIS_IUSRS on $Path" -ForegroundColor Yellow
}

function Invoke-NonQuerySql {
    param(
        [string]$ConnectionStringToUse,
        [string]$Sql
    )

    Add-Type -AssemblyName System.Data
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionStringToUse
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

function Ensure-AppPoolSqlAccess {
    param(
        [string]$SqlServerInstance,
        [string]$TargetDatabaseName,
        [string]$PoolName
    )

    $principal = "IIS APPPOOL\$PoolName"
    $escapedPrincipal = $principal.Replace("'", "''")
    $masterConnectionString = "Server=$SqlServerInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;"

    $loginSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$escapedPrincipal')
BEGIN
    CREATE LOGIN [$principal] FROM WINDOWS;
END
"@
    Invoke-NonQuerySql -ConnectionStringToUse $masterConnectionString -Sql $loginSql

    $dbConnectionString = "Server=$SqlServerInstance;Database=$TargetDatabaseName;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;"
    $userSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$escapedPrincipal')
BEGIN
    CREATE USER [$principal] FOR LOGIN [$principal];
END

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    INNER JOIN sys.database_principals role_principal ON drm.role_principal_id = role_principal.principal_id
    INNER JOIN sys.database_principals member_principal ON drm.member_principal_id = member_principal.principal_id
    WHERE role_principal.name = N'db_owner'
      AND member_principal.name = N'$escapedPrincipal'
)
BEGIN
    ALTER ROLE [db_owner] ADD MEMBER [$principal];
END
"@
    Invoke-NonQuerySql -ConnectionStringToUse $dbConnectionString -Sql $userSql
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = "Server=$SqlInstance;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True;"
}

if ([string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
    throw 'PublicBaseUrl is required. Set it in the settings file or pass it directly to the installer.'
}

if ([string]::IsNullOrWhiteSpace($JwtKey) -or $JwtKey.Length -lt 32) {
    throw 'JwtKey is required and must be at least 32 characters long.'
}

if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminEmail) -and [string]::IsNullOrWhiteSpace($BootstrapAdminPassword)) {
    throw 'BootstrapAdminPassword is required when BootstrapAdminEmail is set.'
}

if ($CorsOrigins.Count -eq 0) {
    $CorsOrigins = @($PublicBaseUrl.TrimEnd('/'))
}

$effectiveHostName = Get-TargetHostName -ConfiguredHostName $HostName -BaseUrl $PublicBaseUrl
$effectiveCertificateThumbprint = Ensure-HttpsCertificate -RequestedThumbprint $CertificateThumbprint -DnsName $effectiveHostName -AllowCreateSelfSigned:$CreateSelfSignedCertificate

Write-Step 'Resolving source and tool locations'
$sourceRoot = Get-SourceRoot -DeploymentRootPath $DeploymentRoot
$publishPath = Join-Path $DeploymentRoot 'publish'
$dotnetExe = Ensure-DotNetInstalled
$dotnetEfExe = Ensure-DotNetEfInstalled -DeploymentRootPath $DeploymentRoot
$appCmdExe = Ensure-IisModule

Write-Step 'Stopping IIS site and app pool if they already exist'
Stop-SiteAndPoolIfPresent -SiteNameToUse $SiteName -PoolName $AppPoolName

Write-Step 'Publishing the application into the Deployment root'
Publish-Application -DotNetExe $dotnetExe -SourceRoot $sourceRoot -PublishPath $publishPath

Write-Step 'Creating and configuring the IIS application pool'
Ensure-AppPool -AppCmdExe $appCmdExe -PoolName $AppPoolName

Write-Step 'Preparing runtime folders and permissions'
$appDataPath = Join-Path $publishPath 'App_Data'
$uploadsPath = Join-Path $appDataPath 'uploads'
$keysPath = Join-Path $appDataPath 'keys'
$imageUploadPath = Join-Path $publishPath 'wwwroot\uploads\images'

Ensure-Directory -Path $appDataPath
Ensure-Directory -Path $uploadsPath
Ensure-Directory -Path $keysPath
Ensure-Directory -Path $imageUploadPath

Grant-ApplicationAccess -Path $publishPath -AppPoolName $AppPoolName -Rights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
Grant-ApplicationAccess -Path $appDataPath -AppPoolName $AppPoolName -Rights ([System.Security.AccessControl.FileSystemRights]::Modify)
Grant-ApplicationAccess -Path $imageUploadPath -AppPoolName $AppPoolName -Rights ([System.Security.AccessControl.FileSystemRights]::Modify)

Write-Step 'Writing production environment variables into the published web.config'
$environmentVariables = @{
    'ASPNETCORE_ENVIRONMENT' = 'Production'
    'ConnectionStrings__DefaultConnection' = $ConnectionString
    'Jwt__Key' = $JwtKey
    'Jwt__Issuer' = $JwtIssuer
    'Jwt__Audience' = $JwtAudience
    'PublicApp__BaseUrl' = $PublicBaseUrl.TrimEnd('/')
    'Swagger__Enabled' = $(if ($EnableSwagger) { 'true' } else { 'false' })
    'HttpsRedirection__Enabled' = $(if ($EnableHttpsRedirection) { 'true' } else { 'false' })
    'RateLimiting__AuthAttemptsPerMinute' = [string]$AuthAttemptsPerMinute
    'RateLimiting__GuestQuizLoadsPerMinute' = [string]$GuestQuizLoadsPerMinute
    'RateLimiting__AuthenticatedQuizLoadsPerMinute' = [string]$AuthenticatedQuizLoadsPerMinute
}

if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminEmail)) {
    $environmentVariables['BootstrapAdmin__Email'] = $BootstrapAdminEmail.Trim()
    $environmentVariables['BootstrapAdmin__Password'] = $BootstrapAdminPassword

    if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminFirstName)) {
        $environmentVariables['BootstrapAdmin__FirstName'] = $BootstrapAdminFirstName.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($BootstrapAdminLastName)) {
        $environmentVariables['BootstrapAdmin__LastName'] = $BootstrapAdminLastName.Trim()
    }
}

for ($i = 0; $i -lt $CorsOrigins.Count; $i++) {
    $environmentVariables["Cors__AllowedOrigins__$i"] = $CorsOrigins[$i].TrimEnd('/')
}

Set-WebConfigEnvironmentVariables -WebConfigPath (Join-Path $publishPath 'web.config') -EnvironmentVariables $environmentVariables

Write-Step 'Pointing the IIS site root at the Deployment publish folder'
Ensure-SiteConfiguration -SiteNameToUse $SiteName -PublishPath $publishPath -PoolName $AppPoolName -HttpBindingPort $HttpPort

Write-Step 'Ensuring IIS serves the application over both HTTP and HTTPS'
Ensure-SiteBindings -SiteNameToUse $SiteName -HttpBindingPort $HttpPort -HttpsBindingPort $HttpsPort -DnsName $effectiveHostName -HttpsCertificateThumbprint $effectiveCertificateThumbprint

Write-Step 'Running EF Core database migrations against SQL Express'
Push-Location $sourceRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:ConnectionStrings__DefaultConnection = $ConnectionString
    $env:Jwt__Key = $JwtKey
    $env:Jwt__Issuer = $JwtIssuer
    $env:Jwt__Audience = $JwtAudience
    $env:PublicApp__BaseUrl = $PublicBaseUrl.TrimEnd('/')
    & $dotnetEfExe database update --project (Join-Path $sourceRoot 'QuizAPI.csproj') --startup-project (Join-Path $sourceRoot 'QuizAPI.csproj')
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet ef database update failed.'
    }
}
finally {
    Pop-Location
}

Write-Step 'Ensuring the bootstrap admin account exists'
Invoke-BootstrapAdminTool `
    -DotNetExe $dotnetExe `
    -SourceRoot $sourceRoot `
    -ConnectionStringToUse $ConnectionString `
    -Email $BootstrapAdminEmail `
    -Password $BootstrapAdminPassword `
    -FirstName $BootstrapAdminFirstName `
    -LastName $BootstrapAdminLastName

Write-Step 'Granting the IIS application pool identity access to the database'
Ensure-AppPoolSqlAccess -SqlServerInstance $SqlInstance -TargetDatabaseName $DatabaseName -PoolName $AppPoolName

Write-Step 'Starting the IIS site and application pool'
Start-SiteAndPool -SiteNameToUse $SiteName -PoolName $AppPoolName
iisreset | Out-Host

Write-Step 'Installation summary'
Write-Host "Deployment root: $DeploymentRoot" -ForegroundColor Green
Write-Host "Published site path: $publishPath" -ForegroundColor Green
Write-Host "IIS site: $SiteName" -ForegroundColor Green
Write-Host "Application pool: $AppPoolName" -ForegroundColor Green
Write-Host "SQL connection: $ConnectionString" -ForegroundColor Green
Write-Host "Public base URL: $($PublicBaseUrl.TrimEnd('/'))" -ForegroundColor Green
Write-Host "HTTP binding: http://$effectiveHostName`:$HttpPort" -ForegroundColor Green
Write-Host "HTTPS binding: https://$effectiveHostName`:$HttpsPort" -ForegroundColor Green
Write-Host "HTTPS certificate thumbprint: $effectiveCertificateThumbprint" -ForegroundColor Green

Write-Host ''
Write-Host 'Production installation is complete. Run post-deploy smoke checks next.' -ForegroundColor Green
