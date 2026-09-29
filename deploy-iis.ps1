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
    Package, Install, or Diagnose (read-only prerequisite report for a server).

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
    # On the server: check prerequisites without changing anything
    .\deploy-iis.ps1 -Action Diagnose

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
    [ValidateSet('Package', 'Install', 'Diagnose')]
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

# Whether the operator explicitly asked for a port / host name (vs. defaults).
# Explicit values are applied to an existing site's HTTP binding on reinstall.
$PortSpecified = $PSBoundParameters.ContainsKey('Port')
$HostHeaderSpecified = $PSBoundParameters.ContainsKey('HostHeader')

# Folder containing this script. Resolved in the script body, not in the
# param block, because Windows PowerShell 5.1 can leave $PSScriptRoot empty
# when evaluating parameter defaults.
$ScriptDirectory = $PSScriptRoot
if (-not $ScriptDirectory) { $ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $SourcePath) { $SourcePath = $ScriptDirectory }

# Files that must never be copied over, deleted or overwritten on the server.
$ProtectedDataPatterns = @('*.db', '*.db-wal', '*.db-shm', '*.db-journal')
$MinimumApiKeyLength = 16

# Official SemVer 2.0 pattern (semver.org) without build metadata; the build
# appends "+<git commit>" to the assembly's informational version itself.
$SemVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?$'

function Get-QATrackVersion([string]$Folder) {
    <# SemVer of the KanbanBoard.Api.dll in a folder (ProductVersion = "1.1.0+commit"), or $null. #>
    $dll = Join-Path $Folder 'KanbanBoard.Api.dll'
    if (-not (Test-Path $dll)) { return $null }
    $product = (Get-Item $dll).VersionInfo.ProductVersion
    if (-not $product) { return $null }
    return $product.Trim()
}

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
    if ($Version -notmatch $SemVerPattern) {
        throw "Version '$Version' is not a valid Semantic Version (MAJOR.MINOR.PATCH[-prerelease]). See https://semver.org"
    }

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

function Get-ProgramFiles64 {
    <# 64-bit Program Files, even when this script runs in 32-bit (x86) PowerShell. #>
    if ($env:ProgramW6432) { return $env:ProgramW6432 }
    return $env:ProgramFiles
}

function Find-AspNetCoreModuleV2 {
    <#
      Locates ASP.NET Core Module V2 (ANCM). Returns an object with:
        Installed  - aspnetcorev2.dll exists on disk (Hosting Bundle ran)
        Registered - IIS lists AspNetCoreModuleV2 as a global module (IIS will load it)
        Checked    - $false when IIS registration could not be queried (not elevated)
        Path, Version
      The Hosting Bundle installs the module to
      "%ProgramFiles%\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll" (NOT System32\inetsrv,
      which only holds the legacy V1 aspnetcore.dll).
    #>
    $result = [pscustomobject]@{ Installed = $false; Registered = $false; Checked = $false; Path = $null; Version = $null }

    $candidates = @(
        (Join-Path (Get-ProgramFiles64) 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'),
        (Join-Path $env:windir 'System32\inetsrv\aspnetcorev2.dll')
    )
    $regKey = 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2'
    if (Test-Path $regKey) {
        $reg = Get-ItemProperty $regKey -ErrorAction SilentlyContinue
        if ($reg -and $reg.PSObject.Properties['Version']) { $result.Version = [string]$reg.Version }
        if ($reg -and $reg.PSObject.Properties['InstallDir'] -and $reg.InstallDir) {
            $candidates = @((Join-Path ([string]$reg.InstallDir) 'aspnetcorev2.dll')) + $candidates
        }
    }

    # Authoritative: what IIS itself will load (needs elevation to read applicationHost.config).
    if ((Test-IsAdministrator) -and (Get-Module -ListAvailable -Name WebAdministration)) {
        try {
            Import-Module WebAdministration -ErrorAction Stop
            $module = Get-WebGlobalModule -Name 'AspNetCoreModuleV2' -ErrorAction Stop
            $result.Checked = $true
            if ($module) {
                $result.Registered = $true
                $image = [Environment]::ExpandEnvironmentVariables([string]$module.Image)
                $candidates = @($image) + $candidates
            }
        }
        catch {
            $result.Checked = $false
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            $result.Installed = $true
            $result.Path = $candidate
            if (-not $result.Version) { $result.Version = (Get-Item $candidate).VersionInfo.ProductVersion }
            break
        }
    }
    return $result
}

function Find-AspNetCore8Runtime {
    <# Returns the installed Microsoft.AspNetCore.App 8.x versions (may be empty). #>
    $versions = @()
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetPath = $null
    if ($dotnet) { $dotnetPath = $dotnet.Source }
    elseif (Test-Path (Join-Path (Get-ProgramFiles64) 'dotnet\dotnet.exe')) { $dotnetPath = Join-Path (Get-ProgramFiles64) 'dotnet\dotnet.exe' }

    if ($dotnetPath) {
        foreach ($line in (& $dotnetPath --list-runtimes 2>$null)) {
            if ($line -match '^Microsoft\.AspNetCore\.App (8\.\S+)') { $versions += $Matches[1] }
        }
        $global:LASTEXITCODE = 0
    }
    if ($versions.Count -eq 0) {
        $shared = Join-Path (Get-ProgramFiles64) 'dotnet\shared\Microsoft.AspNetCore.App'
        if (Test-Path $shared) {
            $versions = @(Get-ChildItem $shared -Directory | Where-Object { $_.Name -like '8.*' } | ForEach-Object { $_.Name })
        }
    }
    return ,$versions
}

function Get-PrerequisiteReport {
    <# Evaluates every server prerequisite without changing anything. #>
    $checks = @()
    $isAdmin = Test-IsAdministrator
    $checks += [pscustomobject]@{
        Name = 'Elevated (Run as Administrator)'; Ok = $isAdmin
        Detail = $(if ($isAdmin) { 'yes' } else { 'no' })
        Fix = 'Right-click PowerShell > Run as Administrator.'
    }

    $webAdmin = [bool](Get-Module -ListAvailable -Name WebAdministration)
    $checks += [pscustomobject]@{
        Name = 'IIS + WebAdministration module'; Ok = $webAdmin
        Detail = $(if ($webAdmin) { 'available' } else { 'missing' })
        Fix = 'Server Manager > Add Roles and Features > Web Server (IIS), including Management Tools > IIS Management Scripts and Tools.'
    }

    $ancm = Find-AspNetCoreModuleV2
    if (-not $ancm.Installed) {
        $ancmOk = $false; $ancmDetail = 'not found'
        $ancmFix = 'Install the .NET 8 Hosting Bundle (dotnet-hosting-8.x-win.exe), then run iisreset.'
    }
    elseif ($ancm.Checked -and -not $ancm.Registered) {
        $ancmOk = $false; $ancmDetail = "installed at $($ancm.Path) but NOT registered with IIS"
        $ancmFix = 'This happens when the Hosting Bundle was installed before IIS. Run the Hosting Bundle installer again, choose Repair, then run iisreset.'
    }
    else {
        $ancmOk = $true
        $state = $(if ($ancm.Registered) { 'registered with IIS' } elseif (-not $ancm.Checked) { 'IIS registration not checked (run elevated)' } else { '' })
        $ancmDetail = "$($ancm.Version) at $($ancm.Path); $state"
        $ancmFix = ''
    }
    $checks += [pscustomobject]@{ Name = 'ASP.NET Core Module V2'; Ok = $ancmOk; Detail = $ancmDetail; Fix = $ancmFix }

    $runtimes = Find-AspNetCore8Runtime
    $checks += [pscustomobject]@{
        Name = 'ASP.NET Core 8 runtime'; Ok = ($runtimes.Count -gt 0)
        Detail = $(if ($runtimes.Count -gt 0) { $runtimes -join ', ' } else { 'not found' })
        Fix = 'Install the .NET 8 Hosting Bundle (it includes the runtime).'
    }
    return $checks
}

function Write-PrerequisiteReport($Checks) {
    foreach ($check in $Checks) {
        if ($check.Ok) {
            Write-Host ("    [ OK ] {0}: {1}" -f $check.Name, $check.Detail) -ForegroundColor Green
        }
        else {
            Write-Host ("    [FAIL] {0}: {1}" -f $check.Name, $check.Detail) -ForegroundColor Red
            if ($check.Fix) { Write-Host ("           Fix: {0}" -f $check.Fix) -ForegroundColor Yellow }
        }
    }
}

function Assert-Prerequisites {
    $checks = Get-PrerequisiteReport
    Write-PrerequisiteReport $checks
    $failed = @($checks | Where-Object { -not $_.Ok })
    if ($failed.Count -gt 0) {
        $lines = $failed | ForEach-Object { "$($_.Name): $($_.Detail). $($_.Fix)" }
        throw ("Prerequisites not met:`n - " + ($lines -join "`n - ") + "`nRun '.\deploy-iis.ps1 -Action Diagnose' for a full report.")
    }
}

function Invoke-Diagnose {
    <# Read-only report of the server prerequisites and any existing QATrack install. #>
    Write-Step 'QATrack server prerequisites (read-only, nothing is changed)'
    $checks = Get-PrerequisiteReport
    $conflict = Get-PortConflict -CheckPort $Port -CheckHost $HostHeader -OwnSite $SiteName
    $checks += [pscustomobject]@{
        Name = "Port $Port"; Ok = (-not $conflict)
        Detail = $(if ($conflict) { $conflict } else { "free (or used by site '$SiteName')" })
        Fix = 'Pick another port with -Port, or stop the program using it.'
    }
    Write-PrerequisiteReport $checks

    $packageVersion = Get-QATrackVersion $SourcePath
    if ($packageVersion) { Write-Host "    Package in this folder: QATrack $packageVersion" }
    $installed = Get-QATrackVersion $PhysicalPath
    if ($installed) { Write-Host "    Installed at ${PhysicalPath}: QATrack $installed" }

    $failed = @($checks | Where-Object { -not $_.Ok })
    Write-Host ''
    if ($failed.Count -eq 0) {
        Write-Host 'All prerequisites met. You can run -Action Install.' -ForegroundColor Green
    }
    else {
        Write-Host "$($failed.Count) prerequisite(s) missing - see Fix lines above." -ForegroundColor Yellow
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

function Get-SiteHttpPort([string]$Name) {
    <# First HTTP port bound to an IIS site, or $null. #>
    $binding = Get-WebBinding -Name $Name -Protocol http -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $binding) { return $null }
    return [int](($binding.bindingInformation -split ':')[1])
}

function Get-PortConflict([int]$CheckPort, [string]$CheckHost, [string]$OwnSite) {
    <#
      Returns a human readable description of whatever already uses
      $CheckPort (another started IIS site with the same host name, or any
      other listening process), or $null when the port is free for $OwnSite.
      HRESULT 0x80070020 from Start-Website is exactly this situation.
    #>
    $ownSiteListening = $false

    # Other IIS sites (needs WebAdministration + elevation; skipped otherwise).
    if ((Test-IsAdministrator) -and (Get-Module -ListAvailable -Name WebAdministration)) {
        try {
            Import-Module WebAdministration -ErrorAction Stop
            foreach ($site in Get-Website) {
                foreach ($binding in $site.bindings.Collection) {
                    if ($binding.protocol -notin @('http', 'https')) { continue }
                    $parts = ([string]$binding.bindingInformation) -split ':'
                    if ($parts.Count -lt 2 -or [int]$parts[1] -ne $CheckPort) { continue }
                    $bindingHost = ''
                    if ($parts.Count -ge 3) { $bindingHost = $parts[2] }
                    if ($site.Name -ieq $OwnSite) {
                        if ($site.State -eq 'Started') { $ownSiteListening = $true }
                        continue
                    }
                    if ($bindingHost -ieq $CheckHost -and $site.State -eq 'Started') {
                        return "IIS site '$($site.Name)' is already bound to port $CheckPort" +
                            $(if ($bindingHost) { " for host '$bindingHost'" } else { '' }) + ' and is running'
                    }
                }
            }
        }
        catch {
            # Could not query IIS; fall through to the TCP check.
        }
    }

    # Any other process listening on the port.
    try {
        $listeners = @(Get-NetTCPConnection -LocalPort $CheckPort -State Listen -ErrorAction Stop)
    }
    catch {
        $listeners = @()
    }
    foreach ($listener in $listeners) {
        $processId = [int]$listener.OwningProcess
        if ($processId -eq 4) {
            # PID 4 = the kernel HTTP.sys listener shared by IIS and other HTTP.sys apps.
            if ($ownSiteListening -or $CheckHost) { continue }
            return "port $CheckPort is held by the Windows HTTP service (HTTP.sys, PID 4) for something other than the '$OwnSite' site - see 'netsh http show servicestate'"
        }
        $processName = 'unknown'
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($process) { $processName = $process.ProcessName }
        return "port $CheckPort is in use by process '$processName' (PID $processId)"
    }
    return $null
}

function Set-IisSite([string]$SiteRoot) {
    <# Creates/updates the app pool and site. Does NOT start the site (see Start-QATrackSite). #>
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
        # New-Website may auto-start the site; start it explicitly later, after ACLs.
        if ((Get-WebsiteState -Name $SiteName).Value -eq 'Started') { Stop-Website -Name $SiteName -ErrorAction SilentlyContinue }
    }
    else {
        Set-ItemProperty $sitePath -Name physicalPath -Value $SiteRoot
        Set-ItemProperty $sitePath -Name applicationPool -Value $AppPoolName
        if ($PortSpecified -or $HostHeaderSpecified) {
            $currentPort = Get-SiteHttpPort $SiteName
            Write-Host "    Updating HTTP binding of '$SiteName' to port $Port$(if ($HostHeader) { " (host $HostHeader)" }) (was $currentPort). HTTPS bindings are kept."
            Get-WebBinding -Name $SiteName -Protocol http -ErrorAction SilentlyContinue | Remove-WebBinding
            New-WebBinding -Name $SiteName -Protocol http -Port $Port -HostHeader $HostHeader | Out-Null
        }
    }
}

function Start-QATrackSite {
    <# Starts pool and site, translating the common "port in use" failure into plain language. #>
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Started') { Start-WebAppPool -Name $AppPoolName }
    if ((Get-WebsiteState -Name $SiteName).Value -eq 'Started') { return }
    try {
        Start-Website -Name $SiteName -ErrorAction Stop
    }
    catch {
        $sitePort = Get-SiteHttpPort $SiteName
        if ($_.Exception.Message -match '0x80070020|being used by another process') {
            $conflict = Get-PortConflict -CheckPort $sitePort -CheckHost $HostHeader -OwnSite $SiteName
            if (-not $conflict) { $conflict = "port $sitePort is already in use" }
            throw "IIS could not start site '$SiteName': $conflict. Re-run Install with a free port, e.g. -Port $($sitePort + 1) (the existing site's binding is updated), or stop whatever holds port $sitePort. Files, data and permissions are already in place."
        }
        throw
    }
}

function Get-HttpErrorSummary($ErrorRecord) {
    <#
      Turns a failed Invoke-WebRequest into something actionable: the IIS /
      ASP.NET Core Module error title (e.g. "HTTP Error 500.19 - ...", "HTTP
      Error 500.30 - ASP.NET Core app failed to start"), the "Config Error"
      row of IIS detailed error pages, and a hint for well-known codes.
    #>
    $summary = $ErrorRecord.Exception.Message
    # Windows PowerShell 5.1 has already consumed the response stream and puts
    # the body in ErrorDetails; fall back to rewinding the stream otherwise.
    $body = $null
    if ($ErrorRecord.ErrorDetails -and $ErrorRecord.ErrorDetails.Message) {
        $body = [string]$ErrorRecord.ErrorDetails.Message
    }
    if (-not $body) {
        try {
            $response = $ErrorRecord.Exception.Response
            if ($response) {
                $stream = $response.GetResponseStream()
                if ($stream.CanSeek) { $stream.Position = 0 }
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                $reader.Dispose()
            }
        }
        catch {
            $body = $null
        }
    }
    if (-not $body) { return $summary }

    # Normalize to plain text: PS 5.1 hands over an already tag-stripped page,
    # PS 7 / the raw stream give HTML. Tags become line breaks.
    $text = [System.Net.WebUtility]::HtmlDecode(($body -replace '<[^>]+>', "`n"))

    $lines = @()
    $title = [regex]::Match($text, 'HTTP Error \d{3}(?:\.\d+)?[^\r\n]*', 'IgnoreCase')
    if ($title.Success) { $lines += $title.Value.Trim() }
    $configError = [regex]::Match($text, 'Config Error\s*([^\r\n]+)', 'IgnoreCase')
    if ($configError.Success) { $lines += 'Config Error: ' + $configError.Groups[1].Value.Trim() }

    $joined = $lines -join ' | '
    $hint = ''
    if ($joined -match '500\.19') { $hint = 'IIS cannot read web.config (see Config Error). Make sure the web.config from this package was copied and the Hosting Bundle is installed.' }
    elseif ($joined -match '500\.3[0-9]') { $hint = "The app failed to start. Set stdoutLogEnabled=""true"" in web.config and check the logs folder, or Event Viewer > Windows Logs > Application (source 'IIS AspNetCore Module V2')." }
    elseif ($joined -match '503') { $hint = "The app pool '$AppPoolName' is stopped. Check Event Viewer (WAS / IIS-W3SVC-WP) for why it crashed." }
    if ($hint) { $joined = "$joined`n    Hint: $hint" }
    if ($joined) { return $joined }
    return $summary
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

        # Port pre-flight, before anything on the server is changed. For an
        # existing site without an explicit -Port, its current binding is kept.
        Import-Module WebAdministration
        $checkPort = $Port
        if ((Test-Path "IIS:\Sites\$SiteName") -and -not $PortSpecified) {
            $existingPort = Get-SiteHttpPort $SiteName
            if ($existingPort) { $checkPort = $existingPort }
        }
        $conflict = Get-PortConflict -CheckPort $checkPort -CheckHost $HostHeader -OwnSite $SiteName
        if ($conflict) {
            throw "Cannot use port ${checkPort}: $conflict. Choose a free port with -Port (for example -Port $($checkPort + 1)) or stop the other program. Nothing has been changed."
        }
        Write-Host "    [ OK ] Port ${checkPort} is available for '$SiteName'." -ForegroundColor Green
    }

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $offline = Join-Path $target 'app_offline.htm'
    $isUpdate = Test-Path (Join-Path $target 'KanbanBoard.Api.dll')

    $newVersion = Get-QATrackVersion $source
    if (-not $newVersion) { $newVersion = 'unknown' }
    if ($isUpdate) {
        $oldVersion = Get-QATrackVersion $target
        if (-not $oldVersion) { $oldVersion = 'unknown' }
        Write-Step "Upgrading QATrack $oldVersion -> $newVersion"
    }
    else {
        Write-Step "Installing QATrack $newVersion"
    }

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

            # Permissions BEFORE starting, so the app can create its database on first request.
            Write-Step 'Granting App_Data permissions'
            Grant-AppDataPermissions $target

            Write-Step "Starting site '$SiteName'"
            Start-QATrackSite
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
        $sitePort = Get-SiteHttpPort $SiteName
        if (-not $sitePort) { $sitePort = $Port }
        $url = "http://${hostName}:$sitePort/api/ui/board"
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 60
            Write-Host "    $url -> HTTP $($response.StatusCode)" -ForegroundColor Green
        }
        catch {
            Write-Warning "Warm-up request to $url failed:`n    $(Get-HttpErrorSummary $_)"
        }
        try {
            $running = Invoke-RestMethod -Uri "http://${hostName}:$sitePort/api/version" -TimeoutSec 30
            Write-Host "    Running version reported by the site: $($running.version)" -ForegroundColor Green
        }
        catch {
            Write-Warning "Could not read /api/version: $($_.Exception.Message)"
        }
    }

    Write-Host ''
    Write-Host "QATrack $newVersion deployed to $target" -ForegroundColor Green
}

switch ($Action) {
    'Package' { Invoke-Package | Out-Null }
    'Install' { Invoke-Install }
    'Diagnose' { Invoke-Diagnose }
}
