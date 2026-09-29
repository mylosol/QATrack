<#
.SYNOPSIS
    Builds, packages and deploys QATrack (DevOps Kanban Board) to IIS.

.DESCRIPTION
    Two actions:

    -Action Package   (developer / build machine, run from the repo root)
        1. Runs the full verification gate (npm run verify) unless -SkipTests.
        2. Builds the TypeScript SPA with Vite straight into wwwroot.
        3. Publishes the ASP.NET Core 8 API (framework-dependent, Release).
        4. Guarantees App_Data ships EMPTY (README only) - never a database.
        5. Copies this script into the package and zips everything to
           artifacts\QATrack-<version>.zip.

    -Action Install   (Windows Server, elevated, run from the extracted zip)
        1. Verifies prerequisites (IIS, ASP.NET Core Module V2, .NET 8 runtime).
        2. Creates the App Pool ("No Managed Code") and site if missing.
        3. Takes the running app offline gracefully (app_offline.htm) so the
           SQLite database is closed cleanly, then backs it up.
        4. Copies the new build WITHOUT ever touching App_Data\*.db* files or
           the server's own appsettings.Production.json (no /MIR, no purge).
        5. Generates a strong AI agent API key on first install.
        6. Grants the App Pool identity and IUSR Read & Execute, Write and
           Modify on App_Data (and the logs folder).
        7. Brings the app back online; EF migrations run forward-only on the
           first request and a warm-up request verifies health.

    Data safety contract: no step in this script deletes, replaces or
    recreates App_Data\kanban.db. Redeploys only ever add/replace binaries.

.PARAMETER Action
    Package or Install.

.PARAMETER Version
    Package version label (default: version from package.json).

.PARAMETER SkipTests
    Package only: skip 'npm run verify' (not recommended).

.PARAMETER SiteName
    Install only: IIS site name (default QATrack).

.PARAMETER AppPoolName
    Install only: IIS application pool name (default QATrack).

.PARAMETER PhysicalPath
    Install only: target folder (default C:\inetpub\QATrack).

.PARAMETER Port
    Install only: HTTP port for a newly created site (default 8080).

.PARAMETER HostHeader
    Install only: optional host name binding for a newly created site.

.PARAMETER SourcePath
    Install only: folder containing the extracted package (default: this script's folder).

.PARAMETER ApiKey
    Install only: explicit AI agent API key (min 16 chars). By default an
    existing key is preserved and a new random key is generated only if none exists.

.PARAMETER SkipIisConfiguration
    Install only: copy files / protect data / configure the key but skip all
    IIS and ACL steps. Used by the automated tests; also handy for staging a
    folder before an IIS administrator wires it up.

.EXAMPLE
    # On the build machine
    .\deploy-iis.ps1 -Action Package

.EXAMPLE
    # On the server, from the extracted package, in an elevated PowerShell
    .\deploy-iis.ps1 -Action Install -SiteName QATrack -PhysicalPath D:\Sites\QATrack -Port 8080
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Package', 'Install')]
    [string]$Action,

    [string]$Version,
    [switch]$SkipTests,

    [ValidatePattern('^[A-Za-z0-9 ._-]+$')]
    [string]$SiteName = 'QATrack',

    [ValidatePattern('^[A-Za-z0-9 ._-]+$')]
    [string]$AppPoolName = 'QATrack',

    [string]$PhysicalPath = 'C:\inetpub\QATrack',

    [ValidateRange(1, 65535)]
    [int]$Port = 8080,

    [string]$HostHeader = '',

    # Defaults to this script's folder (resolved below: $PSScriptRoot can be
    # empty while param defaults are evaluated under Windows PowerShell 5.1).
    [string]$SourcePath,

    [string]$ApiKey,

    [switch]$SkipIisConfiguration
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Folder containing this script. Resolved in the script body, not in the
# param block, because Windows PowerShell 5.1 can leave $PSScriptRoot empty
# when evaluating parameter defaults.
$ScriptDirectory = $PSScriptRoot
if (-not $ScriptDirectory) { $ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $SourcePath) { $SourcePath = $ScriptDirectory }

# Files that must never be copied over, deleted or overwritten on the server.
$ProtectedDataPatterns = @('*.db', '*.db-wal', '*.db-shm', '*.db-journal')
$MinimumApiKeyLength = 16

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory) {
    <# Runs an external command and throws if it fails (external tools don't honour $ErrorActionPreference). #>
    Write-Host "    > $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function New-RandomApiKey {
    <# 48 bytes of CSPRNG output, URL-safe base64 (64 chars). #>
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([Convert]::ToBase64String($bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# =============================================================================
# PACKAGE
# =============================================================================
function New-ZipArchive([string]$SourceDirectory, [string]$DestinationPath) {
    <#
      Zips a folder with standard '/' entry separators. Windows PowerShell
      5.1's Compress-Archive (and .NET Framework's ZipFile for legacy hosts)
      write '\' separators, which non-Windows unzip tools mishandle.
    #>
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = (Resolve-Path $SourceDirectory).Path.TrimEnd('\', '/')
    $zip = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -Path $root -Recurse -File) {
            $entryName = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Invoke-Package {
    $repoRoot = $ScriptDirectory
    $frontend = Join-Path $repoRoot 'src\Frontend'
    $apiProject = Join-Path $repoRoot 'src\Backend\KanbanBoard.Api\KanbanBoard.Api.csproj'
    $artifacts = Join-Path $repoRoot 'artifacts'
    $publishDir = Join-Path $artifacts 'publish'

    if (-not (Test-Path $apiProject)) {
        throw "Package must be run from the repository root (could not find $apiProject)."
    }

    if (-not $Version) {
        $Version = (Get-Content (Join-Path $repoRoot 'package.json') -Raw | ConvertFrom-Json).version
    }
    if ($Version -notmatch '^[0-9A-Za-z.\-]+$') { throw "Invalid version '$Version'." }

    Write-Step 'Checking toolchain'
    foreach ($tool in @('node', 'npm', 'dotnet')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not on PATH." }
    }

    if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
        Write-Step 'Installing frontend dependencies (npm ci)'
        Invoke-Checked 'npm' @('ci') $frontend
    }

    if ($SkipTests) {
        Write-Warning 'Skipping verification gate (-SkipTests). The package is NOT verified.'
    }
    else {
        Write-Step 'Running verification gate (typecheck, unit, backend, build, E2E)'
        Invoke-Checked 'npm' @('run', 'verify') $repoRoot
    }

    Write-Step 'Building frontend into wwwroot (Vite production build)'
    Invoke-Checked 'npm' @('run', 'build') $frontend

    Write-Step "Publishing API (Release, framework-dependent net8.0, win-x64) to $publishDir"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    # win-x64 keeps only the Windows SQLite native library (smaller package);
    # --self-contained false still uses the server's .NET 8 Hosting Bundle.
    Invoke-Checked 'dotnet' @('publish', $apiProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-o', $publishDir, '--nologo') $repoRoot

    Write-Step 'Validating package contents'
    $required = @('KanbanBoard.Api.dll', 'web.config', 'appsettings.json', 'appsettings.Production.json', 'wwwroot\index.html', 'wwwroot\theme-init.js')
    foreach ($file in $required) {
        if (-not (Test-Path (Join-Path $publishDir $file))) { throw "Package is missing required file: $file" }
    }
    [xml]$webConfig = Get-Content (Join-Path $publishDir 'web.config') -Raw
    if ($webConfig.OuterXml -notmatch 'AspNetCoreModuleV2' -or $webConfig.OuterXml -notmatch 'inprocess' -or $webConfig.OuterXml -notmatch 'App_Data') {
        throw 'web.config does not contain the expected in-process handler and App_Data hidden segment.'
    }
    if (Test-Path (Join-Path $publishDir 'appsettings.Development.json')) {
        throw 'appsettings.Development.json must not be shipped (it contains a development API key).'
    }

    # App_Data ships empty (README only). Remove any database that slipped in
    # from a local run - a package must never be able to overwrite live data.
    $appData = Join-Path $publishDir 'App_Data'
    New-Item -ItemType Directory -Force -Path $appData | Out-Null
    foreach ($pattern in $ProtectedDataPatterns) {
        Get-ChildItem -Path $appData -Filter $pattern -File -ErrorAction SilentlyContinue | Remove-Item -Force
    }
    if (-not (Test-Path (Join-Path $appData 'README.txt'))) {
        Set-Content -Path (Join-Path $appData 'README.txt') -Value 'SQLite data directory. kanban.db is created on first start.' -Encoding ASCII
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $publishDir 'logs') | Out-Null
    Set-Content -Path (Join-Path $publishDir 'logs\README.txt') -Value 'ASP.NET Core Module stdout logs (disabled by default in web.config).' -Encoding ASCII

    Copy-Item -Path (Join-Path $repoRoot 'deploy-iis.ps1') -Destination $publishDir -Force

    $zipPath = Join-Path $artifacts "QATrack-$Version.zip"
    Write-Step "Creating $zipPath"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    New-ZipArchive -SourceDirectory $publishDir -DestinationPath $zipPath

    $sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host ''
    Write-Host "Package ready: $zipPath ($sizeMb MB)" -ForegroundColor Green
    Write-Host 'Copy it to the server, extract it, and run in an elevated PowerShell:'
    Write-Host '    .\deploy-iis.ps1 -Action Install -SiteName QATrack -PhysicalPath C:\inetpub\QATrack -Port 8080'
    return $zipPath
}

# =============================================================================
# INSTALL
# =============================================================================
function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Prerequisites {
    if (-not (Test-IsAdministrator)) {
        throw 'Install must run in an elevated (Run as Administrator) PowerShell session.'
    }
    if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
        throw 'The IIS WebAdministration module is not available. Install IIS (Web-Server) with the Management Tools.'
    }
    $ancm = Join-Path $env:windir 'System32\inetsrv\aspnetcorev2.dll'
    if (-not (Test-Path $ancm)) {
        throw 'ASP.NET Core Module V2 is not installed. Install the .NET 8 Hosting Bundle, then run iisreset.'
    }
    $runtimes = & dotnet --list-runtimes 2>$null
    if (-not ($runtimes -match '^Microsoft\.AspNetCore\.App 8\.')) {
        throw 'The ASP.NET Core 8 runtime was not found. Install the .NET 8 Hosting Bundle.'
    }
}

function Copy-PackageFiles([string]$From, [string]$To) {
    <#
      Copies the build into the site folder. Deliberately NOT a mirror:
      nothing in the destination is deleted, App_Data database files are
      excluded, and the server's appsettings.Production.json is kept.
    #>
    $excludeFiles = @($ProtectedDataPatterns) + @('app_offline.htm')
    if (Test-Path (Join-Path $To 'appsettings.Production.json')) {
        $excludeFiles += 'appsettings.Production.json'
        Write-Host '    Preserving existing appsettings.Production.json (server configuration).'
    }
    $excludeDirs = @((Join-Path $From 'logs'), (Join-Path $From 'App_Data\backups'))

    $robocopyArgs = @($From, $To, '/E', '/R:2', '/W:2', '/NP', '/NFL', '/NDL', '/XF') + $excludeFiles + @('/XD') + $excludeDirs
    & robocopy @robocopyArgs | Out-Host
    # Robocopy exit codes 0-7 are success variants; 8+ means failure.
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }
    $global:LASTEXITCODE = 0

    foreach ($dir in @('App_Data', 'logs')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $To $dir) | Out-Null
    }
}

function Backup-Database([string]$SiteRoot) {
    <# Copies kanban.db (+ WAL/SHM) to App_Data\backups\<timestamp>\ and keeps the 10 newest. #>
    $appData = Join-Path $SiteRoot 'App_Data'
    $db = Join-Path $appData 'kanban.db'
    if (-not (Test-Path $db)) {
        Write-Host '    No existing database - fresh install.'
        return $null
    }
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
    $target = Join-Path $appData "backups\$stamp"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    foreach ($pattern in @('kanban.db', 'kanban.db-wal', 'kanban.db-shm')) {
        $file = Join-Path $appData $pattern
        if (Test-Path $file) { Copy-Item $file -Destination $target -Force }
    }
    Write-Host "    Database backed up to $target"

    Get-ChildItem (Join-Path $appData 'backups') -Directory |
        Sort-Object Name -Descending | Select-Object -Skip 10 |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
    return $target
}

function Set-AgentApiKey([string]$SiteRoot) {
    <# Ensures appsettings.Production.json holds a usable AI agent API key. #>
    $path = Join-Path $SiteRoot 'appsettings.Production.json'
    if (Test-Path $path) {
        $json = Get-Content $path -Raw | ConvertFrom-Json
    }
    else {
        $json = New-Object psobject
    }
    # Properties['x'] returns $null when missing; '.Properties.Name' throws
    # under Set-StrictMode on an object with no properties.
    if ($null -eq $json.PSObject.Properties['AiAgentApi']) {
        $json | Add-Member -NotePropertyName AiAgentApi -NotePropertyValue (New-Object psobject)
    }
    if ($null -eq $json.AiAgentApi.PSObject.Properties['ApiKey']) {
        $json.AiAgentApi | Add-Member -NotePropertyName ApiKey -NotePropertyValue ''
    }

    $existing = [string]$json.AiAgentApi.ApiKey
    if ($ApiKey) {
        if ($ApiKey.Length -lt $MinimumApiKeyLength) { throw "-ApiKey must be at least $MinimumApiKeyLength characters." }
        $json.AiAgentApi.ApiKey = $ApiKey
        Write-Host '    AI agent API key set from -ApiKey.'
    }
    elseif ($existing.Trim().Length -ge $MinimumApiKeyLength) {
        Write-Host '    Keeping the existing AI agent API key.'
        return
    }
    else {
        $json.AiAgentApi.ApiKey = New-RandomApiKey
        Write-Host '    Generated a new AI agent API key (share it only with trusted agents):' -ForegroundColor Yellow
        Write-Host "    X-API-Key: $($json.AiAgentApi.ApiKey)" -ForegroundColor Yellow
    }
    $json | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding UTF8
}

function Grant-AppDataPermissions([string]$SiteRoot) {
    <# Spec 6.2: Read & Execute, Write and Modify for the App Pool identity and IUSR on App_Data. #>
    $identities = @("IIS AppPool\$AppPoolName", 'IUSR')
    foreach ($dir in @('App_Data', 'logs')) {
        $path = Join-Path $SiteRoot $dir
        foreach ($identity in $identities) {
            # (OI)(CI) = inherit to files and subfolders; M = Modify, which includes Read & Execute and Write.
            & icacls $path /grant "${identity}:(OI)(CI)M" /T /C /Q | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "icacls failed granting $identity on $path." }
        }
    }
    # The pool only needs read access to the binaries.
    & icacls $SiteRoot /grant "IIS AppPool\${AppPoolName}:(OI)(CI)RX" /C /Q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "icacls failed granting read access on $SiteRoot." }
}

function Set-IisSite([string]$SiteRoot) {
    Import-Module WebAdministration

    $poolPath = "IIS:\AppPools\$AppPoolName"
    if (-not (Test-Path $poolPath)) {
        Write-Host "    Creating application pool '$AppPoolName' (No Managed Code, ApplicationPoolIdentity)."
        New-WebAppPool -Name $AppPoolName | Out-Null
    }
    # ASP.NET Core in-process hosting requires "No Managed Code"; the package
    # is built for win-x64, so the worker process must be 64-bit.
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $poolPath -Name enable32BitAppOnWin64 -Value $false
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty $poolPath -Name startMode -Value 'AlwaysRunning'

    $sitePath = "IIS:\Sites\$SiteName"
    if (-not (Test-Path $sitePath)) {
        Write-Host "    Creating site '$SiteName' on port $Port."
        $siteArgs = @{ Name = $SiteName; PhysicalPath = $SiteRoot; ApplicationPool = $AppPoolName; Port = $Port }
        if ($HostHeader) { $siteArgs.HostHeader = $HostHeader }
        New-Website @siteArgs | Out-Null
    }
    else {
        Set-ItemProperty $sitePath -Name physicalPath -Value $SiteRoot
        Set-ItemProperty $sitePath -Name applicationPool -Value $AppPoolName
    }

    if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Started') { Start-WebAppPool -Name $AppPoolName }
    if ((Get-WebsiteState -Name $SiteName).Value -ne 'Started') { Start-Website -Name $SiteName }
}

function Invoke-Install {
    # Validate every input before touching the server.
    if ($ApiKey -and $ApiKey.Length -lt $MinimumApiKeyLength) {
        throw "-ApiKey must be at least $MinimumApiKeyLength characters."
    }
    $source = (Resolve-Path $SourcePath).Path
    if (-not (Test-Path (Join-Path $source 'KanbanBoard.Api.dll'))) {
        throw "SourcePath '$source' does not contain KanbanBoard.Api.dll. Run Install from the extracted package folder."
    }
    $target = [System.IO.Path]::GetFullPath($PhysicalPath)
    if ($source.TrimEnd('\') -ieq $target.TrimEnd('\')) {
        throw 'SourcePath and PhysicalPath must differ: extract the package somewhere else first.'
    }

    if ($SkipIisConfiguration) {
        Write-Warning 'Skipping IIS configuration and ACLs (-SkipIisConfiguration).'
    }
    else {
        Write-Step 'Checking prerequisites'
        Assert-Prerequisites
    }

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $offline = Join-Path $target 'app_offline.htm'
    $isUpdate = Test-Path (Join-Path $target 'KanbanBoard.Api.dll')

    try {
        if ($isUpdate) {
            Write-Step 'Taking the running app offline (app_offline.htm)'
            # ANCM stops the app, releasing DLL and SQLite handles, and serves this page meanwhile.
            Set-Content -Path $offline -Encoding ASCII -Value '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>QATrack - updating</title></head><body><h1>QATrack is being updated</h1><p>Please try again in a minute.</p></body></html>'
            if (-not $SkipIisConfiguration) { Start-Sleep -Seconds 5 }

            Write-Step 'Backing up the database'
            Backup-Database $target | Out-Null
        }

        Write-Step "Copying application files to $target (database and server config are never overwritten)"
        if ($PSCmdlet.ShouldProcess($target, 'Copy application files')) {
            Copy-PackageFiles -From $source -To $target
        }

        Write-Step 'Configuring the AI agent API key'
        Set-AgentApiKey $target

        if (-not $SkipIisConfiguration) {
            Write-Step "Configuring IIS app pool '$AppPoolName' and site '$SiteName'"
            Set-IisSite $target

            Write-Step 'Granting App_Data permissions'
            Grant-AppDataPermissions $target
        }
    }
    finally {
        if (Test-Path $offline) {
            Write-Step 'Bringing the app back online'
            Remove-Item $offline -Force
        }
    }

    if (-not $SkipIisConfiguration) {
        Write-Step 'Warm-up request (runs pending migrations)'
        $hostName = 'localhost'
        if ($HostHeader) { $hostName = $HostHeader }
        $url = "http://${hostName}:$Port/api/ui/board"
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 60
            Write-Host "    $url -> HTTP $($response.StatusCode)" -ForegroundColor Green
        }
        catch {
            Write-Warning "Warm-up request to $url failed: $($_.Exception.Message). Check Event Viewer or enable stdout logging in web.config."
        }
    }

    Write-Host ''
    Write-Host "QATrack deployed to $target" -ForegroundColor Green
}

switch ($Action) {
    'Package' { Invoke-Package | Out-Null }
    'Install' { Invoke-Install }
}
