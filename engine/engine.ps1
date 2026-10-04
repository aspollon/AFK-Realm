#requires -Version 5.1
<#
.SYNOPSIS
    Builds, updates and configures a Conquest of Azeroth (AzerothCore + Playerbots)
    server on Windows 10/11 from source.

.DESCRIPTION
    This is the engine behind the AFK Realm app. It can also run on its own:

        powershell -ExecutionPolicy Bypass -File engine.ps1                  (interactive)
        powershell -ExecutionPolicy Bypass -File engine.ps1 -NonInteractive -Mode Install

    Modes
        Install   first installation (a clean rebuild if a server already exists)
        Update    fetch new commits of the core, Playerbots and custom modules;
                  rebuild incrementally only when something changed
        Rebuild   clean rebuild of the current sources
        Setup     database, world data and configuration only, no compiling
        Backup    snapshot of the server: programs, configs, databases and versions
        Restore   roll the server back to a snapshot (-Snapshot <folder name>)
        Modules   add modules (-AddModules "<git url>;...") and/or remove them
                  (-RemoveModules "<folder name>;..."), rebuild, record or undo
                  their database changes

    In -NonInteractive mode the database password is read from the environment
    variable AC_DB_PASSWORD and progress is reported as machine-readable lines:
        ##AC|PHASE|<id>|<text>    ##AC|CHANGE|<text>    ##AC|SOURCES|<count>
        ##AC|NOTE|<text>          (module results worth showing at the end)
        ##AC|DONE|ok|uptodate     ##AC|FAIL|<message>
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = '',
    [ValidateRange(1024, 65535)][int]$DatabasePort = 3307,
    [ValidateSet('', 'Install', 'Update', 'Rebuild', 'Setup', 'Backup', 'Restore', 'Modules')][string]$Mode = '',
    [string]$Snapshot = '',
    [string]$AddModules = '',
    [string]$RemoveModules = '',
    [switch]$NonInteractive
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if ($NonInteractive) { try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { } }
# Text piped into mysql.exe (Invoke-Sql) is sent as UTF-8; Windows PowerShell 5.1 would send ASCII.
$OutputEncoding = New-Object Text.UTF8Encoding($false)

# =============================================================================
#  Server profile - everything specific to the Conquest of Azeroth project
# =============================================================================
$Project = @{
    Name       = 'Conquest of Azeroth'
    # Every build uses the newest commit of these branches. Revision is filled in at
    # run time and recorded in Dependencies\revisions.txt, so the log and later
    # repairs know exactly which code was built.
    Core       = @{ Repo = 'https://github.com/jealous-sound/azerothcore-wotlk-coa.git'; Branch = 'main'; Revision = '' }
    Playerbots = @{ Repo = 'https://github.com/Zyth45/mod-playerbots.git'; Branch = 'coa'; Revision = ''; Folder = 'mod-playerbots' }
    # The CoA fork ships its world content as a versioned package with its own importer.
    WorldImporter = 'apps\coa-world\world_data.py'
}

# Third-party downloads, pinned to exact files and checksums.
$Downloads = @{
    Boost   = @{ Version = '1.87.0'; Folder = 'boost_1_87_0'; MinBytes = 200MB
                 Url = 'https://archives.boost.io/release/1.87.0/binaries/boost_1_87_0-msvc-14.3-64.exe'
                 Sha256 = '7b204c1cfa1a41f771361d23a99d3b4d5d677d7b52064eb73f37ba47b2d238bb' }
    MySql   = @{ Version = '8.4.9'; MinBytes = 200MB
                 Url = 'https://cdn.mysql.com/archives/mysql-8.4/mysql-8.4.9-winx64.zip'
                 Sha256 = '5795ba250e89290f7507ed3bcc6a655be373616abb58b877acdea71e1b8f4e8c' }
    OpenSsl = @{ Version = '3.5.7'; MinBytes = 30MB
                 Url = 'https://download.firedaemon.com/FireDaemon-OpenSSL/openssl-3.5.7.zip'
                 Sha256 = '2591459A06A6DF2D2E2B23B02A28D7C180B95C02FB4965099A708B7365A74014' }
    Python  = @{ Version = '3.13.15'; MinBytes = 5MB
                 Url = 'https://www.python.org/ftp/python/3.13.15/python-3.13.15-embed-amd64.zip'
                 Sha256 = 'd1f04d990aee1253d8569e8e5104e30fa9f5fa830899f14843448872d936a2cf' }
    # Reads the CoA client's MPQ archives for apps\coa-dbc\client_dbc.py.
    MpqCli  = @{ Version = '0.9.9'; MinBytes = 500KB
                 Url = 'https://github.com/thegraydot/mpqcli/releases/download/v0.9.9/mpqcli-windows-amd64.exe'
                 Sha256 = 'c4b1d04ad84f18c90157389c1bfa69a85e45dc153c2f390d32e814c713d5350d' }
    VsBootstrapper = 'https://aka.ms/vs/17/release/vs_BuildTools.exe'
    MinimumCMake = [version]'3.27.0'
}

# =============================================================================
#  Paths
# =============================================================================
if (-not $InstallRoot) { $InstallRoot = $PSScriptRoot }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$Paths = @{
    Root        = $InstallRoot
    Deps        = Join-Path $InstallRoot 'Dependencies'
    Downloads   = Join-Path $InstallRoot 'Dependencies\Downloads'
    Source      = Join-Path $InstallRoot 'Dependencies\Source'
    Build       = Join-Path $InstallRoot 'Dependencies\Build'
    Server      = Join-Path $InstallRoot 'Server'
    Configs     = Join-Path $InstallRoot 'Server\configs'
    Data        = Join-Path $InstallRoot 'Server\Data'
    Db          = Join-Path $InstallRoot 'DB'
    MySql       = Join-Path $InstallRoot 'DB\mysql'
    DbData      = Join-Path $InstallRoot 'DB\data'
    MyIni       = Join-Path $InstallRoot 'DB\my.ini'
    Logs        = Join-Path $InstallRoot 'logs'
    Log         = Join-Path $InstallRoot 'logs\install.log'
    Revisions   = Join-Path $InstallRoot 'Dependencies\revisions.txt'
    BuildMarker = Join-Path $InstallRoot 'Dependencies\last-successful-build.txt'
    Backups     = Join-Path $InstallRoot 'Backups'
    ModuleList  = Join-Path $InstallRoot 'Dependencies\modules.txt'
    ModuleTrash = Join-Path $InstallRoot 'Dependencies\ModulesRemoved'
    ModuleSettings = Join-Path $InstallRoot 'Dependencies\module-settings.txt'
}
$Paths.Modules = Join-Path $Paths.Source 'modules'
$Paths.Playerbots = Join-Path $Paths.Modules $Project.Playerbots.Folder
# SQL procedures that record and undo the database changes of modules (extracted next to this script).
$JournalSql = Join-Path $PSScriptRoot 'module-journal.sql'

# =============================================================================
#  Output and logging
# =============================================================================
function Write-Log {
    param([string]$Text, [ConsoleColor]$Color = [ConsoleColor]::Gray)
    try { [IO.File]::AppendAllText($Paths.Log, ('{0:yyyy-MM-dd HH:mm:ss} {1}{2}' -f (Get-Date), $Text, [Environment]::NewLine), (New-Object Text.UTF8Encoding($false))) } catch { }
    Write-Host $Text -ForegroundColor $Color
}
function Write-Section([string]$Text) { Write-Log ''; Write-Log "=== $Text ===" Cyan }
function Send-Event([string]$Kind, [string]$Text = '') {
    Write-Host ('##AC|{0}|{1}' -f $Kind, ($Text -replace '[\r\n]+', ' '))
}
function Enter-Phase([string]$Id, [string]$Text) { Send-Event 'PHASE' "$Id|$Text"; Write-Section $Text }

# =============================================================================
#  Running programs
# =============================================================================
# Runs a console program, streams every output line to the screen and the log
# (as plain UTF-8 text) and returns its exit code. Windows PowerShell 5.1 turns
# stderr lines into error records, so they are unwrapped to their message.
function Invoke-Program {
    param([Parameter(Mandatory)][string]$Path, [string[]]$Arguments = @(), [string]$WorkingDirectory = '', [switch]$AllowFailure)
    Write-Log ('> {0} {1}' -f $Path, ($Arguments -join ' ')) DarkGray
    $previousLocation = Get-Location
    $previousPreference = $ErrorActionPreference
    $writer = New-Object IO.StreamWriter($Paths.Log, $true, (New-Object Text.UTF8Encoding($false)))
    try {
        if ($WorkingDirectory) { Set-Location $WorkingDirectory }
        $ErrorActionPreference = 'Continue'
        & $Path @Arguments 2>&1 | ForEach-Object {
            $line = if ($_ -is [Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" }
            $writer.WriteLine($line)
            Write-Host $line
        }
        $code = $LASTEXITCODE
    } finally {
        $writer.Dispose()
        $ErrorActionPreference = $previousPreference
        Set-Location $previousLocation
    }
    if ($code -ne 0 -and -not $AllowFailure) { throw ('{0} failed with exit code {1}.' -f (Split-Path $Path -Leaf), $code) }
    return $code
}

# Runs a console program quietly and returns its standard output as text ($null on failure).
function Get-ProgramOutput {
    param([Parameter(Mandatory)][string]$Path, [string[]]$Arguments = @())
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $out = & $Path @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
    } finally { $ErrorActionPreference = $previousPreference }
    return ((@($out) | ForEach-Object { "$_" }) -join "`n").Trim()
}

# Starts an installer and waits for that process only. Start-Process -Wait would
# also wait for every helper the installer leaves running, which used to freeze
# the console after a successful install.
function Start-Installer {
    param([Parameter(Mandatory)][string]$Path, [string]$ArgumentLine = '')
    Write-Log ('> {0} {1}' -f $Path, $ArgumentLine) DarkGray
    $process = Start-Process -FilePath $Path -ArgumentList $ArgumentLine -WorkingDirectory $env:SystemRoot -PassThru
    $process.WaitForExit()
    Write-Log ('  exit code {0}' -f $process.ExitCode) DarkGray
    return $process.ExitCode
}

function Quote-Argument([string]$Value) {
    if ($Value -match '[\s"]') { return '"' + $Value.Replace('"', '\"') + '"' }
    return $Value
}

# =============================================================================
#  Downloads
# =============================================================================
function Test-FileHash([string]$File, [string]$Sha256) {
    if (-not $Sha256) { return $true }
    return ((Get-FileHash $File -Algorithm SHA256).Hash -eq $Sha256.ToUpperInvariant())
}

# Downloads a file into Dependencies\Downloads, verifies size and checksum and
# reuses a previous download when it is still valid. HTTP is tried first with a
# progress readout, then BITS, then curl.exe.
function Save-Download {
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$FileName, [string]$Sha256 = '', [long]$MinBytes = 1KB)
    New-Item -ItemType Directory -Force -Path $Paths.Downloads | Out-Null
    $target = Join-Path $Paths.Downloads $FileName
    if ((Test-Path $target) -and (Get-Item $target).Length -ge $MinBytes -and (Test-FileHash $target $Sha256)) {
        Write-Log "Using the cached download $FileName"
        return $target
    }
    $partial = "$target.part"
    $failures = @()
    foreach ($method in 'HTTP', 'BITS', 'curl') {
        Remove-Item $partial -Force -ErrorAction SilentlyContinue
        Write-Log "Downloading $FileName ($method)"
        try {
            switch ($method) {
                'HTTP' { Receive-HttpFile $Url $partial $FileName }
                'BITS' { Start-BitsTransfer -Source $Url -Destination $partial -ErrorAction Stop }
                'curl' {
                    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
                    if (-not (Test-Path $curl)) { throw 'curl.exe is not available.' }
                    [void](Invoke-Program $curl @('-L', '--fail', '--retry', '3', '-o', $partial, $Url))
                }
            }
            if (-not (Test-Path $partial) -or (Get-Item $partial).Length -lt $MinBytes) { throw 'The download is incomplete.' }
            if (-not (Test-FileHash $partial $Sha256)) { throw 'The checksum does not match the expected file.' }
            Move-Item $partial $target -Force
            Write-Log ('Downloaded {0} ({1:N1} MB)' -f $FileName, ((Get-Item $target).Length / 1MB))
            return $target
        } catch {
            $failures += "$method`: $($_.Exception.Message)"
            Write-Log "  $method failed: $($_.Exception.Message)" Yellow
        }
    }
    throw ("Could not download $FileName from $Url. " + ($failures -join ' | '))
}

function Receive-HttpFile([string]$Url, [string]$Destination, [string]$Label) {
    $request = [Net.HttpWebRequest]::Create($Url)
    $request.UserAgent = 'AFK-Realm'
    $request.AllowAutoRedirect = $true
    $response = $request.GetResponse()
    try {
        $total = $response.ContentLength
        $stream = $response.GetResponseStream()
        $output = [IO.File]::Create($Destination)
        try {
            $buffer = New-Object byte[] 1048576
            $done = 0L; $lastReported = -1
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $output.Write($buffer, 0, $read)
                $done += $read
                if ($total -gt 0) {
                    $percent = [int][math]::Floor($done * 100 / $total)
                    if ($percent -ge $lastReported + 5) {
                        $lastReported = $percent
                        Write-Host ('Downloading {0}: {1}% ({2:N0} of {3:N0} MB)' -f $Label, $percent, ($done / 1MB), ($total / 1MB))
                    }
                }
            }
        } finally { $output.Dispose(); $stream.Dispose() }
    } finally { $response.Dispose() }
}

function Expand-Download([string]$Zip, [string]$Destination) {
    Remove-Item $Destination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Expand-Archive -Path $Zip -DestinationPath $Destination -Force
}

# Returns the only top-level folder of an extracted archive (most ZIPs wrap their content in one).
function Get-SingleFolder([string]$Path) {
    $folders = @(Get-ChildItem $Path -Directory)
    if ($folders.Count -ne 1) { throw "Unexpected archive layout in $Path." }
    return $folders[0].FullName
}

# =============================================================================
#  Build tools: Git and CMake
# =============================================================================
function Find-Program([string]$Name, [string[]]$KnownLocations = @()) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    foreach ($location in $KnownLocations) { if (Test-Path $location) { return $location } }
    return $null
}

# Asks the GitHub API for the newest release asset whose name matches a pattern.
function Get-LatestReleaseAsset([string]$Repository, [string]$Pattern) {
    $release = Invoke-RestMethod -UseBasicParsing -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers @{ 'User-Agent' = 'AFK-Realm' }
    $asset = @($release.assets | Where-Object { $_.name -match $Pattern }) | Select-Object -First 1
    if (-not $asset) { throw "No download matching '$Pattern' was found in the latest $Repository release." }
    return $asset
}

# Git may be found in a known folder without being on PATH (or be the portable copy).
# CMake looks for git on PATH only, so the folder is added for this process and all
# programs it starts, and Invoke-Configure also passes the path explicitly.
function Use-Git([string]$Git) {
    $folder = Split-Path $Git -Parent
    if (-not (@($env:PATH -split ';') -contains $folder)) { $env:PATH = "$folder;$env:PATH" }
    $script:GitExe = $Git
    Write-Log "Git: $Git"
    return $Git
}

function Resolve-Git {
    $git = Find-Program 'git.exe' @("$env:ProgramFiles\Git\cmd\git.exe", "${env:ProgramFiles(x86)}\Git\cmd\git.exe", "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe")
    if ($git) { return Use-Git $git }
    $portable = Join-Path $Paths.Deps 'Git\cmd\git.exe'
    if (-not (Test-Path $portable)) {
        Write-Log 'Git is not installed; setting up a portable copy (MinGit).'
        $asset = Get-LatestReleaseAsset 'git-for-windows/git' '^MinGit-[\d.]+-64-bit\.zip$'
        $zip = Save-Download $asset.browser_download_url 'MinGit-64-bit.zip' '' 20MB
        Expand-Download $zip (Join-Path $Paths.Deps 'Git')
        if (-not (Test-Path $portable)) { throw 'git.exe is missing after extracting MinGit.' }
    }
    return Use-Git $portable
}

function Resolve-CMake {
    $cmake = Find-Program 'cmake.exe' @("$env:ProgramFiles\CMake\bin\cmake.exe")
    if ($cmake) {
        $versionText = Get-ProgramOutput $cmake @('--version')
        if ($versionText -match '(\d+\.\d+\.\d+)' -and [version]$Matches[1] -ge $Downloads.MinimumCMake) { Write-Log "CMake: $cmake"; return $cmake }
        Write-Log "The installed CMake is older than $($Downloads.MinimumCMake); a portable copy is used instead."
    }
    $folder = Join-Path $Paths.Deps 'CMake'
    $portable = Join-Path $folder 'bin\cmake.exe'
    if (-not (Test-Path $portable)) {
        $asset = Get-LatestReleaseAsset 'Kitware/CMake' 'windows-x86_64\.zip$'
        $zip = Save-Download $asset.browser_download_url 'cmake-windows-x86_64.zip' '' 20MB
        $temp = Join-Path $Paths.Deps '_cmake'
        Expand-Download $zip $temp
        Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
        Move-Item (Get-SingleFolder $temp) $folder
        Remove-Item $temp -Recurse -Force
        if (-not (Test-Path $portable)) { throw 'cmake.exe is missing after extracting CMake.' }
    }
    Write-Log "CMake: $portable"
    return $portable
}

# =============================================================================
#  Visual Studio 2022 C++ Build Tools
# =============================================================================
$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$VsWorkload = 'Microsoft.VisualStudio.Workload.VCTools'
$VsToolset = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'

# All registered VS 2022 instances, as objects with path, version and state.
function Get-VisualStudioInstances {
    if (-not (Test-Path $VsWhere)) { return @() }
    $json = Get-ProgramOutput $VsWhere @('-all', '-prerelease', '-products', '*', '-version', '[17.0,18.0)', '-format', 'json', '-utf8')
    if (-not $json) { return @() }
    # Windows PowerShell 5.1 hands a JSON list on as one object; going through a variable
    # splits it into its instances (with two installations the fields were mixed up otherwise).
    $parsed = $json | ConvertFrom-Json
    return @($parsed | ForEach-Object { $_ })
}

# The newest VS 2022 instance that has the x64 C++ compiler, or $null.
function Find-VisualStudio {
    if (-not (Test-Path $VsWhere)) { return $null }
    $path = Get-ProgramOutput $VsWhere @('-latest', '-products', '*', '-version', '[17.0,18.0)', '-requires', $VsToolset, '-property', 'installationPath')
    if (-not $path) { return $null }
    $path = ($path -split "`n")[0].Trim()
    $instance = @(Get-VisualStudioInstances) | Where-Object { $_.installationPath -eq $path } | Select-Object -First 1
    # CMake only accepts a version of exactly four numbers; anything else is left out.
    $version = if ($instance) { [string]$instance.installationVersion } else { '' }
    if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { $version = '' }
    return [pscustomobject]@{
        Path           = $path
        Version        = $version
        RebootRequired = [bool]($instance -and $instance.PSObject.Properties['isRebootRequired'] -and $instance.isRebootRequired)
    }
}

function Test-RebootPending {
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    foreach ($key in $keys) { if (Test-Path $key) { return $true } }
    $session = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue
    return [bool]($session -and $session.PendingFileRenameOperations)
}

function Wait-VisualStudioInstallerIdle([int]$Seconds = 900) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Name 'vs_installer', 'vs_installershell', 'setup', 'vs_bootstrapper' -ErrorAction SilentlyContinue |
                  Where-Object { try { $_.Path -like '*Visual Studio*' -or $_.Path -like '*vs_*' } catch { $false } })) { return }
        Start-Sleep -Seconds 5
    }
}

function Get-VisualStudioExitMessage([int]$Code) {
    switch ($Code) {
        0     { 'success' }
        3010  { 'success, Windows needs a restart' }
        1641  { 'success, a restart was started' }
        5007  { 'blocked by a policy or security software' }
        8001  { 'the installer could not be updated' }
        8004  { 'the target folder was rejected (network drive, too long, special characters or not empty)' }
        8006  { 'another Visual Studio installation is running' }
        default { 'installation error (common causes: too little disk space, a pending Windows restart, a proxy or antivirus blocking the download)' }
    }
}

function Resolve-VisualStudio {
    $vs = Find-VisualStudio
    if ($vs) { Write-Log "Visual Studio C++ toolset: $($vs.Path)"; return $vs }

    if (Test-RebootPending) {
        Write-Log 'Windows is waiting for a restart. If the Visual Studio installation fails, restart Windows and run this again.' Yellow
    }
    $bootstrapper = Save-Download $Downloads.VsBootstrapper 'vs_buildtools.exe' '' 1MB
    if ((Get-AuthenticodeSignature $bootstrapper).Status -ne 'Valid') { throw 'The Visual Studio installer does not carry a valid Microsoft signature.' }

    $components = "--add $VsWorkload --includeRecommended --wait --norestart"
    $localFolder = Join-Path $Paths.Deps 'VSBuildTools'
    $attempts = New-Object Collections.Generic.List[object]
    # An existing VS 2022 without the C++ workload is extended instead of installing a second copy.
    $existing = @(Get-VisualStudioInstances) | Select-Object -First 1
    $systemSetup = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe"
    if ($existing -and (Test-Path $systemSetup)) {
        $attempts.Add(@{ Text = "add C++ to the Visual Studio in $($existing.installationPath)"; Exe = $systemSetup
                         Args = ('modify --installPath {0} --passive {1}' -f (Quote-Argument $existing.installationPath), $components) })
    }
    $attempts.Add(@{ Text = "install into $localFolder"; Exe = $bootstrapper; Reset = $localFolder
                     Args = ('--installPath {0} --passive {1}' -f (Quote-Argument $localFolder), $components) })
    $attempts.Add(@{ Text = 'install into the default location'; Exe = $bootstrapper
                     Args = ('--passive --nocache {0}' -f $components) })

    $results = @()
    foreach ($attempt in $attempts) {
        Write-Log "Visual Studio: $($attempt.Text) (the Visual Studio installer window opens; please do not close it)" Cyan
        if ($attempt.ContainsKey('Reset') -and (Test-Path $attempt.Reset) -and -not (Get-VisualStudioInstances | Where-Object { $_.installationPath -eq $attempt.Reset })) {
            Remove-Item $attempt.Reset -Recurse -Force -ErrorAction SilentlyContinue   # leftover of an earlier failed attempt
        }
        Wait-VisualStudioInstallerIdle
        $code = Start-Installer $attempt.Exe $attempt.Args
        Wait-VisualStudioInstallerIdle 300
        $vs = Find-VisualStudio
        if ($vs) {
            Write-Log "Visual Studio C++ toolset installed: $($vs.Path)"
            if ($code -eq 3010 -or $vs.RebootRequired) { Write-Log 'Visual Studio asks for a Windows restart. If the build fails, restart and run this again.' Yellow }
            return $vs
        }
        $message = Get-VisualStudioExitMessage $code
        Write-Log "  failed: exit code $code - $message" Yellow
        $results += "$($attempt.Text): $code ($message)"
        if ($code -eq 5007) { break }
    }
    throw ("Visual Studio 2022 Build Tools could not be installed. " + ($results -join ' | ') +
           ' Install "Build Tools for Visual Studio 2022" with the workload "Desktop development with C++" manually, then run this again.')
}

# =============================================================================
#  Libraries: OpenSSL 3 and Boost
# =============================================================================
function Test-OpenSslFolder([string]$Path) {
    if (-not $Path -or -not (Test-Path (Join-Path $Path 'include\openssl\opensslv.h'))) { return $false }
    return ((Get-Content (Join-Path $Path 'include\openssl\opensslv.h') -Raw) -match '(?m)^\s*#\s*define\s+OPENSSL_VERSION_MAJOR\s+3\b')
}

function Resolve-OpenSsl {
    foreach ($candidate in @($env:OPENSSL_ROOT_DIR, "$env:ProgramFiles\OpenSSL-Win64", "$env:ProgramFiles\OpenSSL")) {
        if (Test-OpenSslFolder $candidate) { Write-Log "OpenSSL: $candidate"; return $candidate }
    }
    $folder = Join-Path $Paths.Deps ('openssl-{0}-x64' -f $Downloads.OpenSsl.Version)
    if (-not (Test-OpenSslFolder $folder)) {
        # FireDaemon publishes the Windows builds that openssl.org links to.
        $zip = Save-Download $Downloads.OpenSsl.Url ('openssl-{0}.zip' -f $Downloads.OpenSsl.Version) $Downloads.OpenSsl.Sha256 $Downloads.OpenSsl.MinBytes
        $temp = Join-Path $Paths.Deps '_openssl'
        Expand-Download $zip $temp
        $x64 = Get-ChildItem $temp -Directory -Recurse | Where-Object { $_.Name -eq 'x64' -and (Test-Path (Join-Path $_.FullName 'include\openssl\ssl.h')) } | Select-Object -First 1
        if (-not $x64) { throw 'The OpenSSL archive has an unexpected layout.' }
        Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item $x64.FullName $folder -Recurse
        Remove-Item $temp -Recurse -Force
        if (-not (Test-OpenSslFolder $folder)) { throw 'OpenSSL is incomplete after extraction.' }
    }
    Write-Log "OpenSSL: $folder"
    return $folder
}

function Test-BoostFolder([string]$Path) {
    return ($Path -and (Test-Path (Join-Path $Path 'boost\version.hpp')) -and (Test-Path (Join-Path $Path 'lib64-msvc-14.3')))
}

function Resolve-Boost {
    if (Test-BoostFolder $env:BOOST_ROOT) { Write-Log "Boost: $env:BOOST_ROOT"; return $env:BOOST_ROOT }
    $folder = Join-Path $Paths.Deps $Downloads.Boost.Folder
    if (-not (Test-BoostFolder $folder)) {
        $installer = Save-Download $Downloads.Boost.Url 'boost-installer.exe' $Downloads.Boost.Sha256 $Downloads.Boost.MinBytes
        $setupLog = Join-Path $Paths.Logs 'boost-install.log'
        Write-Log 'Installing Boost (unpacks thousands of files; this can take a while without further output) ...'
        Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
        # One pre-quoted argument line: Start-Process does not quote array elements, so a path with spaces would break.
        $code = Start-Installer $installer ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="{0}" /LOG="{1}"' -f $folder, $setupLog)
        $deadline = (Get-Date).AddSeconds(30)
        while (-not (Test-BoostFolder $folder) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        if (-not (Test-BoostFolder $folder)) { throw "Boost could not be installed (installer exit code $code). Details: $setupLog" }
    }
    Write-Log "Boost: $folder"
    return $folder
}

# =============================================================================
#  Portable MySQL database
# =============================================================================
function Get-MySqlTool([string]$Name) { return (Join-Path $Paths.MySql "bin\$Name") }

function Install-MySql {
    if ((Test-Path (Join-Path $Paths.MySql 'bin\mysqld.exe')) -and (Test-Path (Join-Path $Paths.MySql 'lib\mysqlclient.lib'))) { return }
    $zip = Save-Download $Downloads.MySql.Url ('mysql-{0}-winx64.zip' -f $Downloads.MySql.Version) $Downloads.MySql.Sha256 $Downloads.MySql.MinBytes
    Write-Log 'Unpacking MySQL (about 1 GB; this can take a while) ...'
    $temp = Join-Path $Paths.Db '_mysql'
    Expand-Download $zip $temp
    Remove-Item $Paths.MySql -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item (Get-SingleFolder $temp) $Paths.MySql
    Remove-Item $temp -Recurse -Force
    foreach ($file in 'bin\mysqld.exe', 'bin\mysql.exe', 'bin\mysqladmin.exe', 'include\mysql.h', 'lib\mysqlclient.lib', 'lib\libmysql.dll') {
        if (-not (Test-Path (Join-Path $Paths.MySql $file))) { throw "The MySQL package is missing $file." }
    }
}

function Write-MySqlConfig {
    $forward = { param($p) $p.Replace('\', '/') }
    $lines = @(
        '[mysqld]'
        "basedir=$(& $forward $Paths.MySql)"
        "datadir=$(& $forward $Paths.DbData)"
        "port=$DatabasePort"
        'bind-address=127.0.0.1'
        'mysqlx=0'
        'character-set-server=utf8mb4'
        'collation-server=utf8mb4_unicode_ci'
        'max_allowed_packet=128M'
        "log-error=$(& $forward (Join-Path $Paths.Logs 'mysql-error.log'))"
        ''
        '[client]'
        "port=$DatabasePort"
        'host=127.0.0.1'
        'protocol=tcp'
    )
    [IO.File]::WriteAllLines($Paths.MyIni, $lines)
}

function Test-Port([int]$Port) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        $pending = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        return ($pending.AsyncWaitHandle.WaitOne(500) -and $client.Connected)
    } catch { return $false } finally { $client.Close() }
}

function Get-DatabaseProcess {
    return @(Get-CimInstance Win32_Process -Filter "Name='mysqld.exe'" -ErrorAction SilentlyContinue |
             Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Paths.MySql, [StringComparison]::OrdinalIgnoreCase) })
}

function Start-Database {
    Write-MySqlConfig
    if (-not (Test-Path (Join-Path $Paths.DbData 'mysql'))) {
        Write-Log 'Creating a new database directory.'
        New-Item -ItemType Directory -Force -Path $Paths.DbData | Out-Null
        [void](Invoke-Program (Join-Path $Paths.MySql 'bin\mysqld.exe') @("--defaults-file=$($Paths.MyIni)", '--initialize-insecure', '--console'))
    }
    if (@(Get-DatabaseProcess).Count -eq 0) {
        if (Test-Port $DatabasePort) { throw "Port $DatabasePort is already used by another program (another database or repack?). Stop it or choose another port." }
        Start-Process (Join-Path $Paths.MySql 'bin\mysqld.exe') -ArgumentList "--defaults-file=`"$($Paths.MyIni)`"" -WorkingDirectory $Paths.Db -WindowStyle Hidden | Out-Null
    }
    for ($i = 0; $i -lt 90; $i++) { if (Test-Port $DatabasePort) { return }; Start-Sleep -Seconds 1 }
    throw 'The database did not start. See logs\mysql-error.log.'
}

# A temporary MySQL option file keeps passwords off the command line and out of the log.
function New-ClientOptions([string]$User, [string]$Password) {
    $file = Join-Path ([IO.Path]::GetTempPath()) ('coa-db-' + [guid]::NewGuid().ToString('N') + '.cnf')
    $text = "[client]`r`nuser=$User`r`n"
    if ($Password) { $text += "password=`"$Password`"`r`n" }
    $text += "host=127.0.0.1`r`nport=$DatabasePort`r`nprotocol=tcp`r`ndefault-character-set=utf8mb4`r`n"
    [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($false)))
    return $file
}

# Runs SQL and returns the result rows as strings. Throws with MySQL's message on failure.
function Invoke-Sql {
    param([Parameter(Mandatory)][string]$Options, [Parameter(Mandatory)][string]$Sql, [switch]$AllowFailure)
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $out = $Sql | & (Get-MySqlTool 'mysql.exe') "--defaults-extra-file=$Options" --batch --skip-column-names 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    $lines = @($out | ForEach-Object { if ($_ -is [Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" } })
    if ($code -ne 0) {
        if ($AllowFailure) { return $null }
        throw ('MySQL: ' + (($lines | Where-Object { $_ -notmatch 'Using a password' }) -join ' '))
    }
    return $lines
}

function Get-SqlValue([string]$Options, [string]$Sql) {
    $rows = @(Invoke-Sql $Options $Sql)
    if ($rows.Count -eq 0) { return '' }
    return [string]$rows[0]
}

function Test-SqlLogin([string]$Options) { return ($null -ne (Invoke-Sql $Options 'SELECT 1;' -AllowFailure)) }

# Creates the four schemas and the 'acore' account, and gives root the chosen password.
# Works on a fresh database (root without password) and on an existing one.
function Initialize-Database([string]$Password) {
    $root = New-ClientOptions 'root' $Password
    try {
        if (-not (Test-SqlLogin $root)) {
            Remove-Item $root -Force
            $root = New-ClientOptions 'root' ''
            if (-not (Test-SqlLogin $root)) { throw 'The database rejected the password. Use the password from the first installation.' }
        }
        $pw = $Password.Replace("'", "''")
        $sql = New-Object Text.StringBuilder
        foreach ($schema in 'acore_auth', 'acore_world', 'acore_characters', 'acore_playerbots') {
            [void]$sql.AppendLine("CREATE DATABASE IF NOT EXISTS $schema CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
        }
        foreach ($host_ in 'localhost', '127.0.0.1') {
            [void]$sql.AppendLine("CREATE USER IF NOT EXISTS 'acore'@'$host_' IDENTIFIED BY '$pw';")
            [void]$sql.AppendLine("ALTER USER 'acore'@'$host_' IDENTIFIED BY '$pw';")
            foreach ($schema in 'acore_auth', 'acore_world', 'acore_characters', 'acore_playerbots') {
                [void]$sql.AppendLine("GRANT ALL PRIVILEGES ON $schema.* TO 'acore'@'$host_';")
            }
        }
        [void]$sql.AppendLine("ALTER USER 'root'@'localhost' IDENTIFIED BY '$pw';")
        [void]$sql.AppendLine('FLUSH PRIVILEGES;')
        [void](Invoke-Sql $root $sql.ToString())
        Write-Log 'Database schemas and user are ready.'
    } finally { Remove-Item $root -Force -ErrorAction SilentlyContinue }
}

# The auth and characters databases are created from the core's base SQL files.
# If that was interrupted (for example the first worldserver start was closed),
# the database is half created and every later start fails on a missing table.
# A half-created database without accounts or characters is recreated empty;
# one that already holds data is never touched.
function Repair-IncompleteDatabases([string]$Password) {
    $options = New-ClientOptions 'acore' $Password
    try {
        foreach ($db in @(@{ Schema = 'acore_auth'; Base = 'db_auth'; Data = 'account' },
                          @{ Schema = 'acore_characters'; Base = 'db_characters'; Data = 'characters' })) {
            $baseDir = Join-Path $Paths.Source "data\sql\base\$($db.Base)"
            if (-not (Test-Path $baseDir)) { continue }
            $expected = @(Get-ChildItem $baseDir -Filter '*.sql').Count
            $tables = [int](Get-SqlValue $options "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '$($db.Schema)';")
            if ($tables -eq 0 -or $tables -ge $expected) { continue }
            $hasTable = [int](Get-SqlValue $options "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '$($db.Schema)' AND table_name = '$($db.Data)';")
            $rows = if ($hasTable -gt 0) { [int](Get-SqlValue $options "SELECT COUNT(*) FROM $($db.Schema).$($db.Data);") } else { 0 }
            if ($rows -gt 0) {
                throw "$($db.Schema) is incomplete ($tables of $expected tables) but already contains data. It was left unchanged; restore it from a backup."
            }
            Write-Log "$($db.Schema) was only partly created ($tables of $expected tables) and holds no data yet; recreating it." Yellow
            [void](Invoke-Sql $options "DROP DATABASE $($db.Schema); CREATE DATABASE $($db.Schema) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
        }
    } finally { Remove-Item $options -Force -ErrorAction SilentlyContinue }
}

# Creates missing tables and applies all pending SQL updates with the core's own
# dbimport tool, so the first worldserver start has nothing left to set up and
# errors show up here instead of in a server window that closes. The SQL files of
# modules installed through AFK Realm are held back from dbimport and applied
# afterwards with a record of their changes (see Invoke-ModuleSql).
# The name players see in the realm list. Chosen in the app on a new installation
# (passed in AC_REALM_NAME); without it the realm keeps the name it has.
function Set-RealmName([string]$Password) {
    $name = "$($env:AC_REALM_NAME)".Trim()
    if (-not $name) { return }
    if ($name.Length -gt 32 -or $name -notmatch "^[A-Za-z0-9][A-Za-z0-9 '.\-]*$") { Write-Log "The server name '$name' is not usable; the realm keeps its name." Yellow; return }
    $options = New-ClientOptions 'acore' $Password
    try {
        $quoted = "'" + $name.Replace("'", "''") + "'"
        Invoke-Sql $options "UPDATE acore_auth.realmlist SET name = $quoted ORDER BY id LIMIT 1;" | Out-Null
        Write-Log "Server name: $name"
    } catch { Write-Log "The server name could not be set: $($_.Exception.Message)" Yellow
    } finally { Remove-Item $options -Force -ErrorAction SilentlyContinue }
}

function Update-Databases([string]$Password) {
    $dbimport = Join-Path $Paths.Server 'dbimport.exe'
    if (-not (Test-Path $dbimport)) { Write-Log 'dbimport.exe was not built; the worldserver applies the updates on its first start.' Yellow; return }
    $root = New-ClientOptions 'root' $Password
    try {
        Install-ModuleJournal $root
        $managed = @(Get-ManagedModules $root | Where-Object { Test-Path $_.Folder })
        foreach ($m in $managed) {
            # Left over from an interrupted run.
            $leftover = [IO.Path]::Combine($m.Folder, 'data', 'sql.afk-held')
            if ((Test-Path $leftover) -and -not (Test-Path ([IO.Path]::Combine($m.Folder, 'data', 'sql')))) { Rename-Item $leftover 'sql' }
        }
        $held = @()
        Write-Log 'Creating tables and applying database updates. This can take a while.'
        try {
            foreach ($m in $managed) {
                $sql = [IO.Path]::Combine($m.Folder, 'data', 'sql')
                if (Test-Path $sql) { Rename-Item $sql 'sql.afk-held'; $held += $m.Folder }
            }
            $code = Invoke-Program $dbimport @('--config', (Join-Path $Paths.Configs 'dbimport.conf')) -WorkingDirectory $Paths.Server -AllowFailure
        } finally {
            foreach ($folder in $held) { Rename-Item ([IO.Path]::Combine($folder, 'data', 'sql.afk-held')) 'sql' }
        }
        if ($code -ne 0) { throw 'A database update failed. The MySQL error is shown just above in the log.' }
        try {
            Repair-ModuleJournal $root
            foreach ($m in $managed) {
                Invoke-ModuleSql $root $m
                $revision = Get-ProgramOutput $script:GitExe @('-C', $m.Folder, 'rev-parse', 'HEAD')
                if ($revision) { [void](Invoke-Sql $root ('UPDATE afk_modules.modules SET revision = {0} WHERE name = {1};' -f (ConvertTo-SqlString $revision), (ConvertTo-SqlString $m.Name))) }
            }
        } finally { Save-ModuleList $root }
    } finally { Remove-Item $root -Force -ErrorAction SilentlyContinue }
    Write-Log 'Databases are complete and up to date.'
}

function Stop-Database([string]$Password) {
    if (@(Get-DatabaseProcess).Count -eq 0) { return }
    $root = New-ClientOptions 'root' $Password
    try {
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & (Get-MySqlTool 'mysqladmin.exe') "--defaults-extra-file=$root" shutdown 2>&1 | Out-Null
        $ErrorActionPreference = $previousPreference
    } finally { Remove-Item $root -Force -ErrorAction SilentlyContinue }
    for ($i = 0; $i -lt 60 -and @(Get-DatabaseProcess).Count -gt 0; $i++) { Start-Sleep -Seconds 1 }
    if (@(Get-DatabaseProcess).Count -gt 0) { throw 'The database could not be stopped cleanly. Close it manually and check logs\mysql-error.log.' }
    Write-Log 'Database stopped cleanly.'
}

# =============================================================================
#  Source code, revisions and updates
# =============================================================================
# The revisions of the last build are kept, so a repair without compiling
# keeps using the code that was actually built.
function Read-Revisions {
    if (-not (Test-Path $Paths.Revisions)) { return }
    foreach ($line in [IO.File]::ReadAllLines($Paths.Revisions)) {
        $f = $line.Split('|')
        if ($f.Count -ne 4 -or $f[3] -notmatch '^[0-9a-f]{40}$') { continue }
        foreach ($part in @(@{ Key = 'core'; Item = $Project.Core }, @{ Key = 'module'; Item = $Project.Playerbots })) {
            if ($f[0] -eq $part.Key -and $f[1] -eq $part.Item.Repo -and $f[2] -eq $part.Item.Branch) { $part.Item.Revision = $f[3] }
        }
    }
}
function Save-Revisions {
    [IO.File]::WriteAllLines($Paths.Revisions, @(
        ('core|{0}|{1}|{2}' -f $Project.Core.Repo, $Project.Core.Branch, $Project.Core.Revision),
        ('module|{0}|{1}|{2}' -f $Project.Playerbots.Repo, $Project.Playerbots.Branch, $Project.Playerbots.Revision)))
}
function Get-BuildStamp { return ('{0}|{1}' -f $Project.Core.Revision, $Project.Playerbots.Revision) }

# Brings a repository to an exact revision. Additional module folders inside the
# core's modules\ directory are never touched.
function Sync-Repository([string]$Git, [hashtable]$Item, [string]$Folder, [switch]$KeepModules) {
    if (-not (Test-Path (Join-Path $Folder '.git'))) {
        Remove-Item $Folder -Recurse -Force -ErrorAction SilentlyContinue
        [void](Invoke-Program $Git @('clone', '--branch', $Item.Branch, '--no-tags', $Item.Repo, $Folder))
    }
    [void](Invoke-Program $Git @('-C', $Folder, 'remote', 'set-url', 'origin', $Item.Repo))
    [void](Invoke-Program $Git @('-C', $Folder, 'fetch', '--no-tags', 'origin', $Item.Branch))
    [void](Invoke-Program $Git @('-C', $Folder, 'checkout', '--force', $Item.Revision))
    [void](Invoke-Program $Git @('-C', $Folder, 'reset', '--hard', $Item.Revision))
    $clean = @('-C', $Folder, 'clean', '-ffd')
    if ($KeepModules) { $clean += @('-e', 'modules/') }
    [void](Invoke-Program $Git $clean)
    $head = Get-ProgramOutput $Git @('-C', $Folder, 'rev-parse', 'HEAD')
    if ($head -ne $Item.Revision) { throw "$Folder is at $head instead of $($Item.Revision)." }
}

function Sync-Sources([string]$Git) {
    Sync-Repository $Git $Project.Core $Paths.Source -KeepModules
    New-Item -ItemType Directory -Force -Path (Join-Path $Paths.Source 'modules') | Out-Null
    Sync-Repository $Git $Project.Playerbots $Paths.Playerbots
    Save-Revisions
}

function Get-RemoteHead([string]$Git, [hashtable]$Item) {
    $remote = Get-ProgramOutput $Git @('ls-remote', $Item.Repo, "refs/heads/$($Item.Branch)")
    $latest = if ($remote) { ($remote -split '\s+')[0] } else { '' }
    if ($latest -notmatch '^[0-9a-f]{40}$') { throw "Could not reach $($Item.Repo). Check the internet connection." }
    return $latest
}

# Installs and rebuilds always use the newest code of both branches.
function Resolve-LatestRevisions([string]$Git) {
    $Project.Core.Revision = Get-RemoteHead $Git $Project.Core
    $Project.Playerbots.Revision = Get-RemoteHead $Git $Project.Playerbots
    Write-Log ('Newest version: CoA core {0}, Playerbots {1}' -f $Project.Core.Revision.Substring(0, 8), $Project.Playerbots.Revision.Substring(0, 8)) Cyan
}

# Looks for new commits. Core and Playerbots move to the branch head; additional
# modules cloned by the user are fast-forwarded when that is safe.
function Find-Updates([string]$Git) {
    $changes = New-Object Collections.Generic.List[string]
    foreach ($entry in @(@{ Name = 'Core'; Item = $Project.Core; Folder = $Paths.Source },
                         @{ Name = 'Playerbots'; Item = $Project.Playerbots; Folder = $Paths.Playerbots })) {
        $latest = Get-RemoteHead $Git $entry.Item
        $current = if (Test-Path (Join-Path $entry.Folder '.git')) { Get-ProgramOutput $Git @('-C', $entry.Folder, 'rev-parse', 'HEAD') } else { $null }
        if ($current -eq $latest) { Write-Log "$($entry.Name) is up to date."; continue }
        $from = if ($current) { $current.Substring(0, 8) } else { 'none' }
        $changes.Add(('{0}: {1} -> {2}' -f $entry.Name, $from, $latest.Substring(0, 8)))
        $entry.Item.Revision = $latest
    }
    $modules = Join-Path $Paths.Source 'modules'
    foreach ($dir in @(Get-ChildItem $modules -Directory -ErrorAction SilentlyContinue)) {
        if ($dir.FullName -eq $Paths.Playerbots -or -not (Test-Path (Join-Path $dir.FullName '.git'))) { continue }
        $upstream = Get-ProgramOutput $Git @('-C', $dir.FullName, 'rev-parse', '--abbrev-ref', '@{u}')
        if (-not $upstream) { Write-Log "$($dir.Name): no tracking branch, skipped." Yellow; continue }
        if ($null -eq (Get-ProgramOutput $Git @('-C', $dir.FullName, 'fetch', '--quiet'))) { Write-Log "$($dir.Name): could not fetch, skipped." Yellow; continue }
        $behind = [int](Get-ProgramOutput $Git @('-C', $dir.FullName, 'rev-list', '--count', 'HEAD..@{u}'))
        if ($behind -eq 0) { Write-Log "$($dir.Name) is up to date."; continue }
        if (Get-ProgramOutput $Git @('-C', $dir.FullName, 'status', '--porcelain', '--untracked-files=no')) {
            Write-Log "$($dir.Name): $behind new commit(s), but it contains local changes; skipped." Yellow; continue
        }
        if ($null -eq (Get-ProgramOutput $Git @('-C', $dir.FullName, 'merge', '--ff-only', '@{u}'))) {
            Write-Log "$($dir.Name): $behind new commit(s), but the history has diverged; skipped." Yellow; continue
        }
        $changes.Add("$($dir.Name): $behind new commit(s)")
    }
    return ,$changes
}

# =============================================================================
#  Compiling
# =============================================================================
function Invoke-Configure([string]$CMake, $VisualStudio, [string]$OpenSsl, [string]$Boost, [switch]$Clean) {
    # A cache created for another Visual Studio instance cannot be switched, so it is started fresh.
    $cache = Join-Path $Paths.Build 'CMakeCache.txt'
    $instance = $VisualStudio.Path.Replace('\', '/')
    if ($Clean -or ((Test-Path $cache) -and -not ([IO.File]::ReadAllText($cache).Contains("CMAKE_GENERATOR_INSTANCE:INTERNAL=$instance")))) {
        Remove-Item $Paths.Build -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path $Paths.Build, $Paths.Server | Out-Null

    # The core includes conf\config.cmake automatically. Defining the Windows version
    # for every file silences the Boost.Asio "_WIN32_WINNT" warnings in modules.
    $confDir = Join-Path $Paths.Source 'conf'
    New-Item -ItemType Directory -Force -Path $confDir | Out-Null
    [IO.File]::WriteAllText((Join-Path $confDir 'config.cmake'), "add_compile_definitions(_WIN32_WINNT=0x0A00 WINVER=0x0A00)`r`n")

    # CMake's own Visual Studio search skips instances that are incomplete or wait
    # for a restart; naming the verified instance (path and version) avoids that.
    if ($VisualStudio.Version) { $instance += ",version=$($VisualStudio.Version)" }
    $forward = { param($p) $p.Replace('\', '/') }
    [void](Invoke-Program $CMake @(
        '-S', $Paths.Source, '-B', $Paths.Build, '-G', 'Visual Studio 17 2022', '-A', 'x64',
        "-DCMAKE_GENERATOR_INSTANCE=$instance",
        "-DCMAKE_INSTALL_PREFIX=$(& $forward $Paths.Server)",
        '-DTOOLS_BUILD=all', '-DSCRIPTS=static', '-DMODULES=static',
        "-DBOOST_ROOT=$(& $forward $Boost)",
        "-DOPENSSL_ROOT_DIR=$(& $forward $OpenSsl)", '-DOPENSSL_USE_STATIC_LIBS=FALSE',
        "-DMYSQL_INCLUDE_DIR=$(& $forward (Join-Path $Paths.MySql 'include'))",
        "-DMYSQL_LIBRARY=$(& $forward (Join-Path $Paths.MySql 'lib\mysqlclient.lib'))",
        "-DMYSQL_EXECUTABLE=$(& $forward (Get-MySqlTool 'mysql.exe'))",
        "-DGIT_EXECUTABLE=$(& $forward $script:GitExe)"))
}

function Invoke-Compile([string]$CMake) {
    # '--parallel' is the number of projects built at once; each project already
    # compiles on all cores. Too many at once exhausts memory (C1076/C3859), so the
    # count follows the free memory and a memory failure is retried one by one.
    $freeGB = [double](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    $jobs = [int][math]::Max(1, [math]::Min(4, [math]::Floor($freeGB / 4)))
    Write-Log ('Free memory {0:N1} GB - building {1} project(s) at a time. This may take a long time.' -f $freeGB, $jobs)
    $sources = @(Get-ChildItem $Paths.Source -Recurse -Filter '*.cpp' -File -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notmatch '\\(deps|test|tests)\\' }).Count
    Send-Event 'SOURCES' "$sources"
    $arguments = @('--build', $Paths.Build, '--config', 'RelWithDebInfo', '--target', 'INSTALL', '--parallel')
    $code = Invoke-Program $CMake ($arguments + "$jobs") -AllowFailure
    if ($code -eq 0) { return }
    $tail = (Get-Content $Paths.Log -Tail 3000 -ErrorAction SilentlyContinue) -join "`n"
    if ($jobs -gt 1 -and $tail -match 'C1076|C3859|C1060|C1002|out of heap|heap limit|Heapgrenze|virtuellen Speichers') {
        Write-Log 'The compiler ran out of memory. Retrying one project at a time (finished parts are kept). Closing games and browsers helps.' Yellow
        [void](Invoke-Program $CMake ($arguments + '1'))
        return
    }
    throw "Compiling failed (exit code $code). The first error is usually a few hundred lines above the end of logs\install.log."
}

function Copy-Runtime([string]$OpenSsl) {
    Copy-Item (Join-Path $Paths.MySql 'lib\libmysql.dll') $Paths.Server -Force
    foreach ($dll in @(Get-ChildItem $OpenSsl -Recurse -Filter '*.dll' | Where-Object { $_.Name -match '^(libssl|libcrypto)-3' -or $_.Name -eq 'legacy.dll' })) {
        Copy-Item $dll.FullName $Paths.Server -Force
    }
    if (-not (Get-ChildItem $Paths.Server -Filter 'libssl*.dll')) { throw 'The OpenSSL runtime DLLs were not found.' }
}

# =============================================================================
#  Server configuration
# =============================================================================
function Set-ConfigValue([string]$File, [string]$Key, [string]$Value) {
    $text = [IO.File]::ReadAllText($File)
    $pattern = '(?m)^[ \t]*' + [regex]::Escape($Key) + '[ \t]*=.*$'
    $line = "$Key = $Value"
    if ([regex]::IsMatch($text, $pattern)) { $text = ([regex]$pattern).Replace($text, $line.Replace('$', '$$'), 1) }
    else { $text = $text.TrimEnd("`r", "`n") + "`r`n$line`r`n" }
    [IO.File]::WriteAllText($File, $text, (New-Object Text.UTF8Encoding($false)))
}

# Activates every *.conf.dist that has no .conf yet and appends options that newer
# templates introduced, with their default values. Existing values are never changed.
function Update-ConfigFiles {
    foreach ($template in @(Get-ChildItem $Paths.Configs -Recurse -Filter '*.conf.dist')) {
        $config = $template.FullName.Substring(0, $template.FullName.Length - 5)
        if (-not (Test-Path $config)) { Copy-Item $template.FullName $config; Write-Log "Created $(Split-Path $config -Leaf)"; continue }
        $text = [IO.File]::ReadAllText($config)
        $known = @{}
        foreach ($m in [regex]::Matches($text, '(?m)^[ \t]*([A-Za-z][\w.]*)[ \t]*=')) { $known[$m.Groups[1].Value] = $true }
        $added = New-Object Collections.Generic.List[string]
        foreach ($m in [regex]::Matches([IO.File]::ReadAllText($template.FullName), '(?m)^[ \t]*([A-Za-z][\w.]*)[ \t]*=.*$')) {
            if ($known.ContainsKey($m.Groups[1].Value)) { continue }
            $known[$m.Groups[1].Value] = $true
            $added.Add($m.Value.Trim())
        }
        if ($added.Count -eq 0) { continue }
        $block = "`r`n`r`n# New options from $($template.Name) (default values, added automatically)`r`n" + ($added -join "`r`n") + "`r`n"
        [IO.File]::WriteAllText($config, $text.TrimEnd("`r", "`n") + $block, (New-Object Text.UTF8Encoding($false)))
        Write-Log "Added $($added.Count) new option(s) to $(Split-Path $config -Leaf)"
    }
}

function Set-ServerConfig([string]$Password) {
    Update-ConfigFiles
    $auth = Join-Path $Paths.Configs 'authserver.conf'
    $world = Join-Path $Paths.Configs 'worldserver.conf'
    foreach ($file in $auth, $world) { if (-not (Test-Path $file)) { throw "Missing configuration file $file." } }
    $connection = { param($schema) '"127.0.0.1;{0};acore;{1};{2}"' -f $DatabasePort, $Password, $schema }
    $mysql = '"' + (Get-MySqlTool 'mysql.exe').Replace('\', '/') + '"'
    Set-ConfigValue $auth 'LoginDatabaseInfo' (& $connection 'acore_auth')
    Set-ConfigValue $auth 'MySQLExecutable' $mysql
    Set-ConfigValue $world 'LoginDatabaseInfo' (& $connection 'acore_auth')
    Set-ConfigValue $world 'WorldDatabaseInfo' (& $connection 'acore_world')
    Set-ConfigValue $world 'CharacterDatabaseInfo' (& $connection 'acore_characters')
    Set-ConfigValue $world 'PlayerbotsDatabaseInfo' (& $connection 'acore_playerbots')
    Set-ConfigValue $world 'MySQLExecutable' $mysql
    Set-ConfigValue $world 'DataDir' ('"' + $Paths.Data.Replace('\', '/') + '"')
    # Lets the worldserver apply the fork's later SQL updates from the source tree.
    Set-ConfigValue $world 'SourceDirectory' ('"' + $Paths.Source.Replace('\', '/') + '"')
    $import = Join-Path $Paths.Configs 'dbimport.conf'
    if (Test-Path $import) {
        Set-ConfigValue $import 'LoginDatabaseInfo' (& $connection 'acore_auth')
        Set-ConfigValue $import 'WorldDatabaseInfo' (& $connection 'acore_world')
        Set-ConfigValue $import 'CharacterDatabaseInfo' (& $connection 'acore_characters')
        Set-ConfigValue $import 'MySQLExecutable' $mysql
        Set-ConfigValue $import 'SourceDirectory' ('"' + $Paths.Source.Replace('\', '/') + '"')
    }
    $bots = @(Get-ChildItem $Paths.Configs -Recurse -Filter 'playerbots.conf') | Select-Object -First 1
    if ($bots) { Set-ConfigValue $bots.FullName 'PlayerbotsDatabaseInfo' (& $connection 'acore_playerbots') }
    Write-Log 'Server configuration updated.'
}

# =============================================================================
#  CoA world data
# =============================================================================
function Resolve-Python {
    $python = Join-Path $Paths.Deps 'Python\python.exe'
    if (-not (Test-Path $python)) {
        $zip = Save-Download $Downloads.Python.Url ('python-{0}-embed-amd64.zip' -f $Downloads.Python.Version) $Downloads.Python.Sha256 $Downloads.Python.MinBytes
        Expand-Download $zip (Join-Path $Paths.Deps 'Python')
    }
    [void](Invoke-Program $python @('-c', 'import sys, hashlib, zipfile; assert sys.version_info >= (3, 11)'))
    return $python
}

# The CoA core only starts with the CoA client's own DBC tables, which the stock
# map_extractor cannot read from the client's custom patch archives. The map data
# step of the GUI extracts them with the fork's client_dbc.py, Python and mpqcli.
function Resolve-DbcTools {
    $script = Join-Path $Paths.Source 'apps\coa-dbc\client_dbc.py'
    if (-not (Test-Path $script)) { return }
    [void](Resolve-Python)
    $tool = Join-Path $Paths.Deps 'Tools\mpqcli.exe'
    if (-not (Test-Path $tool) -or -not (Test-FileHash $tool $Downloads.MpqCli.Sha256)) {
        $file = Save-Download $Downloads.MpqCli.Url ('mpqcli-{0}.exe' -f $Downloads.MpqCli.Version) $Downloads.MpqCli.Sha256 $Downloads.MpqCli.MinBytes
        New-Item -ItemType Directory -Force -Path (Split-Path $tool) | Out-Null
        Copy-Item $file $tool -Force
    }
    Write-Log 'Tools for the CoA client DBC extraction are ready.'
}

# The CoA world package is imported once, into an empty acore_world, before the
# worldserver starts for the first time. Later changes arrive as SQL updates.
function Import-WorldData([string]$Password) {
    $importer = Join-Path $Paths.Source $Project.WorldImporter
    if (-not (Test-Path $importer)) { Write-Log 'This core has no world data package; skipped.'; return }
    $options = New-ClientOptions 'acore' $Password
    try {
        $tables = [int](Get-SqlValue $options "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'acore_world';")
        if ($tables -gt 0) { Write-Log "The world database already contains $tables tables; the package is only imported into an empty database."; return }
        $python = Resolve-Python
        [void](Invoke-Program $python @($importer, 'verify'))
        Write-Log 'Importing about 500 MB of world data. This can take a while; progress is shown every 25 tables.'
        try {
            [void](Invoke-Program $python @($importer, 'bootstrap', '--mysql', (Get-MySqlTool 'mysql.exe'), '--defaults-file', $options, '--database', 'acore_world'))
        } catch {
            # A partial import has to be removed; the importer only accepts an empty database.
            Write-Log 'The world import failed; resetting acore_world so the next attempt starts clean.' Yellow
            [void](Invoke-Sql $options 'DROP DATABASE IF EXISTS acore_world; CREATE DATABASE acore_world CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;' -AllowFailure)
            throw
        }
        Write-Log 'World data imported and verified.'
    } finally { Remove-Item $options -Force -ErrorAction SilentlyContinue }
}

# =============================================================================
#  Launchers and checks
# =============================================================================
function Write-Launchers([string]$Password) {
    $portCheck = { param($port, $seconds) "powershell -NoProfile -Command `"`$t=[DateTime]::Now.AddSeconds($seconds); while([DateTime]::Now -lt `$t){ try { (New-Object Net.Sockets.TcpClient('127.0.0.1',$port)).Close(); exit 0 } catch { Start-Sleep 1 } }; exit 1`" >nul 2>&1" }
    $mysqld = Join-Path $Paths.MySql 'bin\mysqld.exe'
    $authPort = 3724
    $authConf = Join-Path $Paths.Configs 'authserver.conf'
    $m = if (Test-Path $authConf) { Select-String -Path $authConf -Pattern '^\s*RealmServerPort\s*=\s*(\d+)' | Select-Object -First 1 } else { $null }
    if ($m) { $authPort = [int]$m.Matches[0].Groups[1].Value }
    # Login for the launcher's one maintenance query (same password as in worldserver.conf).
    $cnf = Join-Path $Paths.Db 'launcher.cnf'
    [IO.File]::WriteAllText($cnf, "[client]`r`nuser=acore`r`npassword=`"$Password`"`r`nhost=127.0.0.1`r`nport=$DatabasePort`r`nprotocol=tcp`r`n", (New-Object Text.UTF8Encoding($false)))
    $mysql = Get-MySqlTool 'mysql.exe'
    # Waits for a port, but gives up as soon as the server process has closed.
    $serverCheck = { param($port, $seconds, $exe) "powershell -NoProfile -Command `"`$t=[DateTime]::Now.AddSeconds($seconds); Start-Sleep 2; while([DateTime]::Now -lt `$t){ try { (New-Object Net.Sockets.TcpClient('127.0.0.1',$port)).Close(); exit 0 } catch { if (-not (Get-Process $exe -ErrorAction SilentlyContinue)) { exit 2 }; Start-Sleep 1 } }; exit 1`" >nul 2>&1" }
    $lines = @(
        '@echo off'
        'rem Starts database, authserver and worldserver. Created by AFK Realm.'
        'setlocal'
        "if not exist `"$($Paths.Data)\maps`" echo WARNING: map data is missing in Server\Data - create it first.& pause& exit /b 1"
        (& $portCheck $DatabasePort 1)
        'if errorlevel 1 ('
        "  start `"Database`" /min `"$mysqld`" --defaults-file=`"$($Paths.MyIni)`""
        "  $(& $portCheck $DatabasePort 90)"
        '  if errorlevel 1 echo The database did not start. See logs\mysql-error.log.& pause& exit /b 1'
        ')'
        'rem A worldserver that crashed while loading leaves its realm marked "not ready"; the authserver would refuse to start.'
        "tasklist /FI `"IMAGENAME eq worldserver.exe`" | find /I `"worldserver.exe`" >nul || `"$mysql`" --defaults-extra-file=`"$cnf`" -e `"UPDATE acore_auth.realmlist SET flag = flag & ~1`" >nul 2>&1"
        "start `"Authserver`" /D `"$($Paths.Server)`" `"$($Paths.Server)\authserver.exe`" --config `"$authConf`""
        'echo Waiting for the authserver ...'
        (& $serverCheck $authPort 120 'authserver')
        'if errorlevel 2 echo The authserver closed while starting. See Server\Auth.log.& pause& exit /b 1'
        'if errorlevel 1 echo The authserver did not become ready. Check its window.& pause& exit /b 1'
        "start `"Worldserver`" /D `"$($Paths.Server)`" `"$($Paths.Server)\worldserver.exe`" --config `"$(Join-Path $Paths.Configs 'worldserver.conf')`""
    )
    [IO.File]::WriteAllLines((Join-Path $Paths.Root 'START-SERVER.cmd'), $lines, [Text.Encoding]::ASCII)
}

function Test-Installation {
    $required = @('authserver.exe', 'worldserver.exe', 'libmysql.dll', 'configs\authserver.conf', 'configs\worldserver.conf')
    foreach ($file in $required) { if (-not (Test-Path (Join-Path $Paths.Server $file))) { throw "Check failed: Server\$file is missing." } }
    foreach ($tool in @('map_extractor.exe|mapextractor.exe', 'vmap4_extractor.exe|vmap4extractor.exe', 'vmap4_assembler.exe|vmap4assembler.exe', 'mmaps_generator.exe')) {
        if (-not ($tool.Split('|') | Where-Object { Test-Path (Join-Path $Paths.Server $_) })) { throw "Check failed: Server\$($tool.Split('|')[0]) is missing." }
    }
    if (-not (Get-ChildItem $Paths.Configs -Recurse -Filter 'playerbots.conf')) { throw 'Check failed: playerbots.conf is missing; the Playerbots module was not built.' }
    Write-Log 'All checks passed.'
}

# =============================================================================
#  Modules (added through AFK Realm, with a record of their database changes)
# =============================================================================
# A module's SQL files are not left to dbimport. AFK Realm applies them itself, copies
# the tables they name beforehand and keeps the differences in the schema afk_modules
# (see module-journal.sql), so removing the module can undo them. The files are entered
# into the core's `updates` tables with the core's own hash, so neither dbimport nor the
# worldserver applies them a second time.

function Install-ModuleJournal([string]$Root) {
    if (-not (Test-Path $JournalSql)) { throw "module-journal.sql is missing next to the engine script." }
    Invoke-Stream (Get-MySqlTool 'mysql.exe') @("--defaults-extra-file=$Root", '--default-character-set=utf8mb4') -InFile $JournalSql
}

function Test-ModuleJournal([string]$Root) {
    return ([int](Get-SqlValue $Root "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'afk_modules' AND table_name = 'modules';")) -gt 0
}

# Modules installed through AFK Realm: name, repo, branch, revision, status.
function Get-ManagedModules([string]$Root) {
    if (-not (Test-ModuleJournal $Root)) { return @() }
    $rows = @(Invoke-Sql $Root 'SELECT name, repo, branch, revision, status FROM afk_modules.modules ORDER BY name;')
    return @($rows | Where-Object { $_ } | ForEach-Object {
        $f = "$_".Split("`t")
        [pscustomobject]@{ Name = $f[0]; Repo = $f[1]; Branch = $f[2]; Revision = $f[3]; Status = $f[4]; Folder = (Join-Path $Paths.Modules $f[0]) }
    })
}

function ConvertTo-SqlString([string]$Text) { return "'" + $Text.Replace('\', '\\').Replace("'", "''") + "'" }

# The database a module SQL folder belongs to, matched by folder name like the core does.
function Get-ModuleSqlDatabase([string]$FolderName) {
    $n = $FolderName.ToLowerInvariant()
    if ($n.Contains('playerbot')) { return $null }
    if ($n.Contains('world')) { return 'acore_world' }
    if ($n.Contains('characters')) { return 'acore_characters' }
    if ($n.Contains('auth')) { return 'acore_auth' }
    return $null
}

# All SQL files of a module per database, in the order the core applies them (by file name).
function Get-ModuleSqlFiles([string]$Folder) {
    $result = @()
    $sql = [IO.Path]::Combine($Folder, 'data', 'sql')
    if (-not (Test-Path $sql)) { return $result }
    foreach ($dir in @(Get-ChildItem $sql -Directory)) {
        $db = Get-ModuleSqlDatabase $dir.Name
        if (-not $db) { continue }
        foreach ($file in @(Get-ChildItem $dir.FullName -Recurse -File -Filter '*.sql')) {
            $result += [pscustomobject]@{ Db = $db; Name = $file.Name; Path = $file.FullName }
        }
    }
    # The core sorts by file name, byte by byte.
    if ($result.Count -lt 2) { return @($result) }
    $byKey = @{}
    foreach ($r in $result) { $byKey[$r.Db + [char]1 + $r.Path] = $r }
    [string[]]$keys = @($result | ForEach-Object { $_.Db + [char]1 + $_.Name + [char]1 + $_.Path })
    [Array]::Sort($keys, [StringComparer]::Ordinal)
    return @($keys | ForEach-Object { $f = $_.Split([char]1); $byKey[$f[0] + [char]1 + $f[2]] })
}

# SHA1 like the core computes it: the file is read in text mode, so on Windows CRLF
# becomes LF and a Ctrl+Z ends the text.
function Get-UpdateHash([string]$File) {
    # Latin-1 maps every byte to one character and back, so the bytes stay exactly as they are.
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $text = $latin1.GetString([IO.File]::ReadAllBytes($File))
    $end = $text.IndexOf([char]0x1A)
    if ($end -ge 0) { $text = $text.Substring(0, $end) }
    $sha = [Security.Cryptography.SHA1]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($latin1.GetBytes($text.Replace("`r`n", "`n")))).Replace('-', '') } finally { $sha.Dispose() }
}

# Table names an SQL text writes to (INSERT, REPLACE, UPDATE, DELETE, CREATE/ALTER/DROP/
# TRUNCATE/RENAME TABLE). Tables it changes some other way are found afterwards by their
# update time and reported as not undoable.
function Get-SqlTables([string]$Text, [string]$DefaultDb) {
    # Strings, comments and quoted names in one pass, so a '#' or '--' inside a string does not count as a comment.
    $token = '''(?:[^''\\]|\\.|'''')*''|"(?:[^"\\]|\\.|"")*"|`[^`]*`|/\*(?!!)[\s\S]*?\*/|(?:--[ \t]|#)[^\n]*'
    $t = [regex]::Replace($Text, $token, [Text.RegularExpressions.MatchEvaluator]{ param($m)
        $v = $m.Value
        if ($v.StartsWith('`')) { return $v }
        if ($v.StartsWith("'") -or $v.StartsWith('"')) { return "''" }
        return ' ' })
    $name = '(?:`?(\w+)`?\s*\.\s*)?`?([\w$]+)`?'
    $patterns = @(
        "\b(?:INSERT|REPLACE)\s+(?:(?:LOW_PRIORITY|DELAYED|HIGH_PRIORITY|IGNORE)\s+)*(?:INTO\s+)?$name",
        "\bUPDATE\s+(?:(?:LOW_PRIORITY|IGNORE)\s+)*$name",
        "\bDELETE\s+(?:(?:LOW_PRIORITY|QUICK|IGNORE)\s+)*(?:\w+\s+)?FROM\s+$name",
        "\b(?:CREATE|ALTER|DROP|TRUNCATE)\s+TABLE\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?$name",
        "\bTRUNCATE\s+(?!TABLE\b)$name",
        "\bRENAME\s+TABLE\s+$name",
        "\bTO\s+$name",
        "\bJOIN\s+$name"
    )
    $found = @{}
    foreach ($p in $patterns) {
        foreach ($m in [regex]::Matches($t, $p, 'IgnoreCase')) {
            $db = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $DefaultDb }
            $tbl = $m.Groups[2].Value
            if ($db -notin 'acore_auth', 'acore_characters', 'acore_world', 'acore_playerbots') { continue }
            if ($tbl -match '^(SELECT|SET|VALUES|WHERE|LIKE)$') { continue }
            $found["$db.$tbl".ToLowerInvariant()] = [pscustomobject]@{ Db = $db; Table = $tbl }
        }
    }
    return @($found.Values)
}

# Applies the module's SQL files that are new or changed since they were last applied,
# with a record of every change. Returns nothing; throws when a file fails (the changes up
# to that point are recorded, so removing the module still undoes them).
function Invoke-ModuleSql([string]$Root, $Module) {
    $files = @(Get-ModuleSqlFiles $Module.Folder)
    foreach ($group in @($files | Group-Object Db)) {
        $db = $group.Name
        $applied = @{}
        foreach ($row in @(Invoke-Sql $Root "SELECT name, hash FROM ``$db``.updates;")) {
            if (-not $row) { continue }
            $f = "$row".Split("`t"); $applied[$f[0]] = if ($f.Count -gt 1) { $f[1] } else { '' }
        }
        $pending = @($group.Group | ForEach-Object {
            $hash = Get-UpdateHash $_.Path
            if (-not $applied.ContainsKey($_.Name) -or ($applied[$_.Name] -and $applied[$_.Name] -ne $hash)) {
                [pscustomobject]@{ Name = $_.Name; Path = $_.Path; Hash = $hash }
            }
        })
        if ($pending.Count -eq 0) { continue }

        Write-Log ('{0}: applying {1} database file(s) to {2} and recording the changes ...' -f $Module.Name, $pending.Count, $db)
        $tables = @{}
        foreach ($p in $pending) {
            foreach ($t in @(Get-SqlTables ([IO.File]::ReadAllText($p.Path)) $db)) { $tables["$($t.Db).$($t.Table)".ToLowerInvariant()] = $t }
        }
        $sql = New-Object Text.StringBuilder
        [void]$sql.AppendLine('DELETE FROM afk_modules.watch;')
        [void]$sql.AppendLine(('INSERT INTO afk_modules.batches (module, db, created, files) VALUES ({0}, {1}, NOW(), {2});' -f
            (ConvertTo-SqlString $Module.Name), (ConvertTo-SqlString $db), (ConvertTo-SqlString ((@($pending | ForEach-Object { $_.Name.Replace(',', '') })) -join ','))))
        [void]$sql.AppendLine('SET @afk_batch = LAST_INSERT_ID();')
        foreach ($t in $tables.Values) {
            [void]$sql.AppendLine(('INSERT IGNORE INTO afk_modules.watch (db, tbl) VALUES ({0}, {1});' -f (ConvertTo-SqlString $t.Db), (ConvertTo-SqlString $t.Table)))
        }
        [void]$sql.AppendLine('CALL afk_modules.afk_begin(@afk_batch);')
        [void]$sql.AppendLine('SELECT @afk_batch;')
        $batch = [int](@(Invoke-Sql $Root $sql.ToString()) | Where-Object { $_ } | Select-Object -Last 1)

        $failure = $null
        foreach ($p in $pending) {
            Write-Log "  $($p.Name)"
            $watch = [Diagnostics.Stopwatch]::StartNew()
            try {
                Invoke-Stream (Get-MySqlTool 'mysql.exe') @("--defaults-extra-file=$Root", '--default-character-set=utf8', '--max-allowed-packet=1GB', $db) -InFile $p.Path
            } catch { $failure = "$($p.Name): $($_.Exception.Message)"; break }
            [void](Invoke-Sql $Root ('REPLACE INTO `{0}`.updates (name, hash, state, speed) VALUES ({1}, {2}, ''MODULE'', {3});' -f $db, (ConvertTo-SqlString $p.Name), (ConvertTo-SqlString $p.Hash), [int]$watch.ElapsedMilliseconds))
        }
        [void](Invoke-Sql $Root "CALL afk_modules.afk_finish($batch);")
        $summary = @(Invoke-Sql $Root "SELECT kind, COUNT(*), SUM(old_rows), SUM(new_rows) FROM afk_modules.entries WHERE batch = $batch GROUP BY kind;")
        foreach ($line in $summary | Where-Object { $_ }) {
            $f = "$line".Split("`t")
            switch ($f[0]) {
                'rows'      { Write-Log ('  recorded: {0} table(s), {1} row(s) added or changed, {2} row(s) replaced or removed' -f $f[1], $f[3], $f[2]) }
                'created'   { Write-Log "  recorded: $($f[1]) new table(s)" }
                'dropped'   { Write-Log "  recorded: $($f[1]) deleted table(s) (kept as a copy)" }
                'altered'   { Send-Event 'NOTE' "$($Module.Name) changed the structure of $($f[1]) existing table(s). Removing the module cannot undo that; use a backup if needed."; Write-Log "  $($f[1]) table structure change(s) - not undoable" Yellow }
                'untracked' { Send-Event 'NOTE' "$($Module.Name) changed $($f[1]) table(s) in a way that could not be recorded. Removing the module cannot undo that; use a backup if needed."; Write-Log "  $($f[1]) unrecorded table change(s) - not undoable" Yellow }
            }
        }
        if ($failure) {
            [void](Invoke-Sql $Root ('UPDATE afk_modules.modules SET status = ''sql-failed'', note = {0} WHERE name = {1};' -f (ConvertTo-SqlString $failure), (ConvertTo-SqlString $Module.Name)))
            throw "A database file of the module $($Module.Name) failed ($failure). The changes made so far were recorded, so removing the module undoes them."
        }
    }
    [void](Invoke-Sql $Root ('UPDATE afk_modules.modules SET status = ''ok'', note = NULL WHERE name = {0} AND status = ''sql-failed'';' -f (ConvertTo-SqlString $Module.Name)))
}

# A record that was interrupted (cancelled, PC switched off) is completed from the copies
# that are still there, so the changes made until then can be undone later.
function Repair-ModuleJournal([string]$Root) {
    foreach ($row in @(Invoke-Sql $Root 'SELECT id, module FROM afk_modules.batches WHERE done = 0 ORDER BY id;')) {
        if (-not $row) { continue }
        $f = "$row".Split("`t")
        $watched = [int](Get-SqlValue $Root 'SELECT COUNT(*) FROM afk_modules.watch;')
        if ($watched -gt 0) {
            Write-Log "$($f[1]): completing the record of a database step that was interrupted ..." Yellow
            [void](Invoke-Sql $Root "CALL afk_modules.afk_finish($([int]$f[0]));")
        } else {
            [void](Invoke-Sql $Root "UPDATE afk_modules.batches SET done = 1 WHERE id = $([int]$f[0]);")
        }
        [void](Invoke-Sql $Root ('UPDATE afk_modules.modules SET status = ''sql-failed'', note = ''A database step was interrupted.'' WHERE name = {0};' -f (ConvertTo-SqlString $f[1])))
    }
    # Copies left behind by an interrupted record.
    foreach ($copy in @(Invoke-Sql $Root "SELECT table_name FROM information_schema.tables WHERE table_schema = 'afk_modules' AND table_name REGEXP '^c[0-9]+_[0-9]+$';")) {
        if ($copy) { [void](Invoke-Sql $Root "DROP TABLE IF EXISTS afk_modules.``$copy``;") }
    }
}

# Undoes the recorded database changes of a module and removes it from the registry.
function Undo-ModuleSql([string]$Root, [string]$Name) {
    Write-Log "$($Name): undoing its database changes ..."
    $kept = 0; $manual = New-Object Collections.Generic.List[string]
    foreach ($line in @(Invoke-Sql $Root ("CALL afk_modules.afk_undo({0});" -f (ConvertTo-SqlString $Name)))) {
        $f = "$line".Split("`t")
        if ($f.Count -lt 4 -or $f[0] -ne 'REPORT') { continue }
        Write-Log ('  {0}: {1}' -f $f[2], $f[3])
        if ($f[1] -eq 'kept') { $kept++ }
        if ($f[1] -eq 'manual' -or $f[1] -eq 'skipped') { $manual.Add($f[2]) }
    }
    if ($kept -gt 0) { Send-Event 'NOTE' "$($Name): some rows were changed again after the module was installed (for example by a server update) and were left as they are. Details are in the log." }
    if ($manual.Count -gt 0) { Send-Event 'NOTE' ("$($Name): these tables could not be put back automatically: " + (($manual | Select-Object -Unique) -join ', ') + '. A backup from before the module was installed restores them.') }
    Send-Event 'NOTE' "$($Name) was removed and its database changes were undone."
}

# The list the GUI reads: name|repo|branch|revision|status|installed|created tables|not undoable tables
function Save-ModuleList([string]$Root) {
    $lines = @()
    if (Test-ModuleJournal $Root) {
        $sql = "SELECT m.name, m.repo, m.branch, m.revision, m.status, IFNULL(DATE_FORMAT(m.installed, '%Y-%m-%d'), ''), " +
               "IFNULL((SELECT GROUP_CONCAT(DISTINCT CONCAT(e.db, '.', e.tbl) SEPARATOR ',') FROM afk_modules.entries e JOIN afk_modules.batches b ON b.id = e.batch WHERE b.module = m.name AND e.kind = 'created'), ''), " +
               "IFNULL((SELECT GROUP_CONCAT(DISTINCT CONCAT(e.db, '.', e.tbl) SEPARATOR ',') FROM afk_modules.entries e JOIN afk_modules.batches b ON b.id = e.batch WHERE b.module = m.name AND e.kind IN ('altered', 'untracked')), '') " +
               "FROM afk_modules.modules m ORDER BY m.name;"
        $lines = @(Invoke-Sql $Root ('SET SESSION group_concat_max_len = 1000000; ' + $sql) | Where-Object { $_ } | ForEach-Object { "$_".Replace("`t", '|') })
    }
    [IO.File]::WriteAllLines($Paths.ModuleList, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
}

function Get-ModuleName([string]$Url) {
    $name = ($Url.Trim().TrimEnd('/') -split '[/:]')[-1]
    if ($name.EndsWith('.git')) { $name = $name.Substring(0, $name.Length - 4) }
    if ($name -notmatch '^[\w.\-]+$' -or $name -in '.', '..') { throw "This is not a valid module address: $Url" }
    return $name
}

# Downloads new modules and sets removed ones aside. Returns what to undo if the build fails.
function Edit-ModuleFolders([string]$Git, [string[]]$Add, [string[]]$Remove) {
    $state = @{ Added = New-Object Collections.Generic.List[string]; Removed = New-Object Collections.Generic.List[object]; Urls = @{} }
    New-Item -ItemType Directory -Force -Path $Paths.Modules | Out-Null
    try {
    foreach ($name in $Remove) {
        if ($name -notmatch '^[\w.\-]+$' -or $name -eq $Project.Playerbots.Folder) { throw "The module $name cannot be removed." }
        $folder = Join-Path $Paths.Modules $name
        if (-not (Test-Path $folder)) { Write-Log "$($name): the folder is already gone."; $state.Removed.Add(@{ Name = $name; Folder = $folder; Aside = $null }); continue }
        if (Get-ProgramOutput $Git @('-C', $Paths.Source, 'ls-files', "modules/$name")) { throw "$name is part of the CoA core and cannot be removed." }
        New-Item -ItemType Directory -Force -Path $Paths.ModuleTrash | Out-Null
        $aside = Join-Path $Paths.ModuleTrash ("{0}-{1:yyyyMMddHHmmss}" -f $name, (Get-Date))
        Move-Item $folder $aside
        $state.Removed.Add(@{ Name = $name; Folder = $folder; Aside = $aside })
        Write-Log "Removed the module folder $name (kept aside until the build has finished)."
    }
    foreach ($url in $Add) {
        $name = Get-ModuleName $url
        $folder = Join-Path $Paths.Modules $name
        if (Test-Path $folder) {
            # Left from an earlier attempt that stopped after downloading ("Try again").
            $origin = Get-ProgramOutput $Git @('-C', $folder, 'remote', 'get-url', 'origin')
            if (-not $origin -or ($origin.TrimEnd('/') -replace '\.git$', '') -ne ($url.Trim().TrimEnd('/') -replace '\.git$', '')) { throw "A module named $name is already installed." }
            Write-Log "$($name): already downloaded, using it."
            $state.Added.Add($folder)
        } else {
            $state.Added.Add($folder)
            [void](Invoke-Program $Git @('clone', '--recurse-submodules', $url.Trim(), $folder))
        }
        $hasCode = @(Get-ChildItem $folder -Recurse -File -Include '*.cpp', '*.h' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\\.git\\' }).Count -gt 0
        $hasSql = Test-Path ([IO.Path]::Combine($folder, 'data', 'sql'))
        if (-not $hasCode -and -not $hasSql) { throw "$name does not look like an AzerothCore module (no source code and no SQL files)." }
        $state.Urls[$folder] = $url.Trim()
    }
    } catch { Undo-ModuleFolders $state; throw }
    return $state
}

# After a successful build: undoes the database changes of removed modules and registers
# the new ones (their SQL files are applied by Update-Databases right after).
function Complete-ModuleChange([string]$Password, $State) {
    $root = New-ClientOptions 'root' $Password
    try {
        Install-ModuleJournal $root
        $managed = @(Get-ManagedModules $root | ForEach-Object { $_.Name })
        foreach ($r in $State.Removed) {
            if ($managed -contains $r.Name) { Undo-ModuleSql $root $r.Name }
            else { Send-Event 'NOTE' "$($r.Name) was removed. It was not installed through AFK Realm, so its database changes (if any) stay in place." }
            if ($r.Aside) { Remove-ModuleConfigs $r.Aside }
        }
        foreach ($folder in $State.Added) {
            $name = Split-Path $folder -Leaf
            $branch = Get-ProgramOutput $script:GitExe @('-C', $folder, 'rev-parse', '--abbrev-ref', 'HEAD')
            $revision = Get-ProgramOutput $script:GitExe @('-C', $folder, 'rev-parse', 'HEAD')
            [void](Invoke-Sql $root ('REPLACE INTO afk_modules.modules (name, repo, branch, revision, installed, status) VALUES ({0}, {1}, {2}, {3}, NOW(), ''ok'');' -f
                (ConvertTo-SqlString $name), (ConvertTo-SqlString $State.Urls[$folder]), (ConvertTo-SqlString "$branch"), (ConvertTo-SqlString "$revision")))
            $hasConf = @(Get-ChildItem (Join-Path $folder 'conf') -Filter '*.conf.dist' -ErrorAction SilentlyContinue).Count -gt 0
            Send-Event 'NOTE' ("$name was installed." + $(if ($hasConf) { ' Its options are in the server settings.' } else { '' }))
        }
        Save-ModuleList $root
    } finally { Remove-Item $root -Force -ErrorAction SilentlyContinue }
}

function Undo-ModuleFolders($State) {
    foreach ($folder in $State.Added) { Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue }
    foreach ($r in $State.Removed) { if ($r.Aside -and -not (Test-Path $r.Folder) -and (Test-Path $r.Aside)) { Move-Item $r.Aside $r.Folder } }
}

# Config files of a removed module, so the settings window no longer shows them.
function Remove-ModuleConfigs([string]$ModuleFolder) {
    foreach ($dist in @(Get-ChildItem (Join-Path $ModuleFolder 'conf') -Filter '*.conf.dist' -ErrorAction SilentlyContinue)) {
        foreach ($file in @(Get-ChildItem $Paths.Configs -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq $dist.Name -or $_.Name -eq ($dist.Name -replace '\.dist$', '') })) {
            Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
        }
    }
}

# After a restore, the module folders follow the restored registry: modules installed after
# the backup are taken out, modules that existed then come back at their old version.
function Sync-ModuleFolders([string]$Git, [string]$Root, [string[]]$Before) {
    $now = @(Get-ManagedModules $Root)
    foreach ($name in $Before) {
        if ($now | Where-Object { $_.Name -eq $name }) { continue }
        $folder = Join-Path $Paths.Modules $name
        if (Test-Path $folder) {
            Remove-ModuleConfigs $folder
            Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log "Removed the module $name (installed after this backup)."
        }
    }
    foreach ($m in $now) {
        if (-not (Test-Path (Join-Path $m.Folder '.git'))) {
            Remove-Item $m.Folder -Recurse -Force -ErrorAction SilentlyContinue
            if ((Invoke-Program $Git @('clone', '--recurse-submodules', $m.Repo, $m.Folder) -AllowFailure) -ne 0) { Write-Log "Could not download the module $($m.Name) again." Yellow; continue }
        }
        if ($m.Revision -match '^[0-9a-f]{40}$') {
            if ((Invoke-Program $Git @('-C', $m.Folder, 'checkout', '--force', $m.Revision) -AllowFailure) -ne 0) {
                [void](Invoke-Program $Git @('-C', $m.Folder, 'fetch', '--no-tags', 'origin') -AllowFailure)
                [void](Invoke-Program $Git @('-C', $m.Folder, 'checkout', '--force', $m.Revision) -AllowFailure)
            }
            if ($m.Branch -and $m.Branch -ne 'HEAD') { [void](Invoke-Program $Git @('-C', $m.Folder, 'checkout', '-B', $m.Branch, $m.Revision) -AllowFailure); [void](Invoke-Program $Git @('-C', $m.Folder, 'branch', '--set-upstream-to', "origin/$($m.Branch)") -AllowFailure) }
        }
    }
}

# =============================================================================
#  What a module brings along for the core, Playerbots and other config files
# =============================================================================
# A module can carry a file afk-realm.json. It names patches for the CoA core or for
# Playerbots - the two source trees AFK Realm downloads itself and resets with every
# update - and settings the module needs in other config files:
#
#   { "patches":  [ { "name": "...", "target": "mod-playerbots" | "core", "file": "patches/x.patch", "why": "..." } ],
#     "settings": [ { "file": "playerbots.conf", "key": "...", "value": "...", "why": "..." } ] }
#
# Patches are applied to the fresh source before every build. One that no longer fits
# (the code it changes has changed) or is no longer needed (the change is in the source
# already) is left out, and the server is built without it.
$ModuleManifestName = 'afk-realm.json'

function Get-ModuleManifests {
    foreach ($dir in @(Get-ChildItem $Paths.Modules -Directory -ErrorAction SilentlyContinue)) {
        $file = Join-Path $dir.FullName $ModuleManifestName
        if (-not (Test-Path $file)) { continue }
        try { $data = [IO.File]::ReadAllText($file) | ConvertFrom-Json }
        catch { Write-Log "$($dir.Name): $ModuleManifestName could not be read ($($_.Exception.Message)); ignored." Yellow; continue }
        if ($data) { @{ Name = $dir.Name; Folder = $dir.FullName; Data = $data } }
    }
}

# A property of a manifest entry as text, or '' when it is not there.
function Get-ManifestText($Entry, [string]$Name) {
    if ($null -eq $Entry) { return '' }
    $property = $Entry.PSObject.Properties[$Name]
    # One line only: these texts go into the log, and the window reads the log line by line.
    if ($property -and $null -ne $property.Value) { return ("$($property.Value)" -replace '[\r\n]+', ' ').Trim() }
    return ''
}
function Get-ManifestList($Data, [string]$Name) {
    $property = $Data.PSObject.Properties[$Name]
    if ($property -and $property.Value) { return ,@($property.Value | Where-Object { $_ -is [psobject] }) }
    return ,@()
}

function Get-PatchTarget([string]$Target) {
    switch ($Target) {
        'core' { return $Paths.Source }
        'mod-playerbots' { return $Paths.Playerbots }
        default { return $null }
    }
}

# Takes earlier patches out again: both trees go back to the revision they were downloaded at.
# Module folders inside the core tree are not tracked by it and stay as they are.
function Reset-PatchedSources([string]$Git) {
    foreach ($folder in @($Paths.Source, $Paths.Playerbots)) {
        if (-not (Test-Path (Join-Path $folder '.git'))) { continue }
        [void](Invoke-Program $Git @('-C', $folder, 'reset', '--hard', '--quiet', 'HEAD') -AllowFailure)
        # Files a patch added are not tracked, so the reset leaves them: they are cleaned out as well.
        $clean = @('-C', $folder, 'clean', '-ffd', '--quiet')
        if ($folder -eq $Paths.Source) { $clean += @('-e', 'modules/') }
        [void](Invoke-Program $Git $clean -AllowFailure)
    }
}

function Invoke-ModulePatches([string]$Git, [switch]$Quiet) {
    Reset-PatchedSources $Git
    foreach ($manifest in @(Get-ModuleManifests)) {
        foreach ($patch in (Get-ManifestList $manifest.Data 'patches')) {
            $relative = (Get-ManifestText $patch 'file').Replace('\', '/')
            $targetName = Get-ManifestText $patch 'target'
            $why = Get-ManifestText $patch 'why'
            $label = '{0}: {1}' -f $manifest.Name, $(if (Get-ManifestText $patch 'name') { Get-ManifestText $patch 'name' } else { $relative })
            $target = Get-PatchTarget $targetName
            if ($relative -notmatch '^[\w.\-]+(/[\w.\-]+)*\.(patch|diff)\z' -or $relative -match '(^|/)\.\.(/|$)' -or -not $target) {
                Write-Log "$label - not a valid patch entry (file inside the module, target core or mod-playerbots); ignored." Yellow; continue
            }
            $file = Join-Path $manifest.Folder $relative.Replace('/', '\')
            if (-not (Test-Path $file) -or -not (Test-Path (Join-Path $target '.git'))) { Write-Log "$label - the patch file or its target is missing; ignored." Yellow; continue }
            $apply = @('-C', $target, 'apply', '--ignore-whitespace', '--whitespace=nowarn')
            if ((Invoke-Program $Git ($apply + @('--check', $file)) -AllowFailure) -eq 0 -and (Invoke-Program $Git ($apply + @($file)) -AllowFailure) -eq 0) {
                Write-Log "$label - applied to $targetName." Green
            } elseif ((Invoke-Program $Git ($apply + @('--reverse', '--check', $file)) -AllowFailure) -eq 0) {
                Write-Log "$label - already part of $targetName, not needed any more."
            } else {
                Write-Log "$label - does not fit the current $targetName any more; the server is built without it." Yellow
                if (-not $Quiet) { Send-Event 'NOTE' ("$label could not be applied: the code it changes has changed. The server is built without it." + $(if ($why) { " ($why)" } else { '' })) }
            }
        }
    }
}

function Get-ConfigValue([string]$File, [string]$Key) {
    $m = [regex]::Match([IO.File]::ReadAllText($File), '(?m)^[ \t]*' + [regex]::Escape($Key) + '[ \t]*=[ \t]*(.*?)[ \t]*\r?$')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

# Settings a module asks for in other config files are written once, when the module is
# new, and remembered with the value that was there before. Changing them later is up to
# the user; when the module is gone, a value that is still the module's goes back.
function Set-ModuleSettings {
    $known = New-Object Collections.Generic.List[object]
    if (Test-Path $Paths.ModuleSettings) {
        foreach ($line in [IO.File]::ReadAllLines($Paths.ModuleSettings)) {
            # The value that was there before comes last and may contain anything, a '|' too.
            $f = $line.Split([char[]]'|', 5)
            if ($f.Count -eq 5) { $known.Add(@{ Module = $f[0]; File = $f[1]; Key = $f[2]; Value = $f[3]; Previous = $f[4] }) }
        }
    }
    $find = { param($name) @(Get-ChildItem $Paths.Configs -Recurse -File -Filter $name -ErrorAction SilentlyContinue) | Select-Object -First 1 }
    $kept = New-Object Collections.Generic.List[object]
    foreach ($entry in $known) {
        if (Test-Path (Join-Path $Paths.Modules $entry.Module)) { $kept.Add($entry); continue }
        $config = & $find $entry.File
        if ($config -and (Get-ConfigValue $config.FullName $entry.Key) -eq $entry.Value) {
            Set-ConfigValue $config.FullName $entry.Key $entry.Previous
            Write-Log "$($entry.Module) is gone: $($entry.Key) in $($entry.File) is $($entry.Previous) again."
        }
    }
    foreach ($manifest in @(Get-ModuleManifests)) {
        foreach ($setting in (Get-ManifestList $manifest.Data 'settings')) {
            $name = Get-ManifestText $setting 'file'; $key = Get-ManifestText $setting 'key'; $value = Get-ManifestText $setting 'value'
            $why = Get-ManifestText $setting 'why'
            if ($name -notmatch '^[\w.\-]+\.conf\z' -or $key -notmatch '^[A-Za-z][\w.]*\z' -or $value -match '[\r\n|]') {
                Write-Log "$($manifest.Name): the setting $key in $name is not valid; ignored." Yellow; continue
            }
            if ($kept | Where-Object { $_.Module -eq $manifest.Name -and $_.File -eq $name -and $_.Key -eq $key }) { continue }
            $config = & $find $name
            if (-not $config) { Write-Log "$($manifest.Name): $name was not found, $key was not set." Yellow; continue }
            $previous = Get-ConfigValue $config.FullName $key
            if ($null -eq $previous) { Write-Log "$($manifest.Name): $name has no option $key; not set." Yellow; continue }
            if ($previous -ne $value) {
                Set-ConfigValue $config.FullName $key $value
                Write-Log "$($manifest.Name): $key in $name set to $value (was $previous)." Green
                Send-Event 'NOTE' ("$($manifest.Name) set $key to $value in $name (was $previous)." + $(if ($why) { " $why" } else { '' }))
            }
            $kept.Add(@{ Module = $manifest.Name; File = $name; Key = $key; Value = $value; Previous = $previous })
        }
    }
    if ($kept.Count -eq 0) { Remove-Item $Paths.ModuleSettings -Force -ErrorAction SilentlyContinue; return }
    [IO.File]::WriteAllLines($Paths.ModuleSettings, @($kept | ForEach-Object { '{0}|{1}|{2}|{3}|{4}' -f $_.Module, $_.File, $_.Key, $_.Value, $_.Previous }))
}

# =============================================================================
#  Snapshots (backup and rollback)
# =============================================================================
# A snapshot holds everything an update changes: the server programs, the configs,
# all four databases and the core/Playerbots versions they were built from. Map data
# and build files are left out; they are not touched by updates.
$KeepSnapshots = 3

# Runs a program with its standard output written to a gzip file, or its standard
# input read from a gzip file or a plain file. Used for mysqldump/mysql, whose data
# must not pass through PowerShell's text pipeline. Other output goes to the log.
function Invoke-Stream {
    param([Parameter(Mandatory)][string]$Path, [string[]]$Arguments = @(), [string]$GzipOut = '', [string]$GzipIn = '', [string]$InFile = '')
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Path
    $psi.Arguments = (@($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardInput = [bool]($GzipIn -or $InFile)
    $process = [Diagnostics.Process]::Start($psi)
    $errors = $process.StandardError.ReadToEndAsync()
    $output = $null
    try {
        if ($GzipOut) {
            $file = [IO.File]::Create($GzipOut)
            $zip = New-Object IO.Compression.GZipStream($file, [IO.Compression.CompressionLevel]::Optimal)
            try { $process.StandardOutput.BaseStream.CopyTo($zip) } finally { $zip.Dispose(); $file.Dispose() }
        } else {
            $output = $process.StandardOutput.ReadToEndAsync()
        }
        if ($GzipIn) {
            $file = [IO.File]::OpenRead($GzipIn)
            $zip = New-Object IO.Compression.GZipStream($file, [IO.Compression.CompressionMode]::Decompress)
            try { $zip.CopyTo($process.StandardInput.BaseStream) } finally { $zip.Dispose(); $file.Dispose(); $process.StandardInput.Close() }
        }
        if ($InFile) {
            $file = [IO.File]::OpenRead($InFile)
            try { $file.CopyTo($process.StandardInput.BaseStream) } catch { } finally { $file.Dispose(); try { $process.StandardInput.Close() } catch { } }
        }
    } finally { $process.WaitForExit() }
    if ($output) { foreach ($line in ($output.Result -split "`r?`n" | Where-Object { $_ })) { Write-Log $line DarkGray } }
    if ($process.ExitCode -ne 0) {
        $message = ($errors.Result -split "`r?`n" | Where-Object { $_ -and $_ -notmatch 'Using a password' }) -join ' '
        throw ('{0} failed: {1}' -f (Split-Path $Path -Leaf), $message)
    }
}

function Get-ServerDatabases([string]$Options) {
    # afk_modules holds the module registry and the record of their database changes.
    $all = @(Invoke-Sql $Options "SELECT schema_name FROM information_schema.schemata WHERE schema_name IN ('acore_auth', 'acore_characters', 'acore_world', 'acore_playerbots', 'afk_modules') ORDER BY schema_name;")
    return @($all | Where-Object { $_ })
}

function Read-SnapshotManifest([string]$Folder) {
    $file = Join-Path $Folder 'manifest.txt'
    if (-not (Test-Path $file)) { throw "$Folder is not a complete snapshot." }
    $result = @{}
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        $i = $line.IndexOf('=')
        if ($i -gt 0) { $result[$line.Substring(0, $i)] = $line.Substring($i + 1) }
    }
    return $result
}

# Creates a snapshot and returns its folder name. The database must be running.
function New-Snapshot([string]$Password, [string]$Reason) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($InstallRoot))
    if ($drive.AvailableFreeSpace -lt 3GB) {
        throw ('Not enough free disk space for the backup: {0:N1} GB free on {1}, at least 3 GB are needed.' -f ($drive.AvailableFreeSpace / 1GB), $drive.Name)
    }
    New-Item -ItemType Directory -Force -Path $Paths.Backups | Out-Null
    foreach ($leftover in @(Get-ChildItem $Paths.Backups -Directory -Filter '*.partial')) { Remove-Item $leftover.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    $name = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
    $folder = Join-Path $Paths.Backups $name
    $partial = "$folder.partial"
    New-Item -ItemType Directory -Force -Path $partial | Out-Null
    $root = New-ClientOptions 'root' $Password
    try {
        $databases = Get-ServerDatabases $root
        foreach ($db in $databases) {
            Write-Log "Backing up the database $db ..."
            Invoke-Stream (Get-MySqlTool 'mysqldump.exe') @("--defaults-extra-file=$root", '--single-transaction', '--quick', '--hex-blob', '--routines',
                '--set-gtid-purged=OFF', '--default-character-set=utf8mb4', '--max-allowed-packet=512M', $db) -GzipOut (Join-Path $partial "$db.sql.gz")
        }
    } finally { Remove-Item $root -Force -ErrorAction SilentlyContinue }

    Write-Log 'Backing up the server programs and configuration ...'
    $zipFile = Join-Path $partial 'server.zip'
    $zip = [IO.Compression.ZipFile]::Open($zipFile, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = @(Get-ChildItem $Paths.Server -File | Where-Object { $_.Extension -in '.exe', '.dll', '.yaml' })
        $files += @(Get-ChildItem $Paths.Configs -File -Recurse -ErrorAction SilentlyContinue)
        foreach ($f in $files) {
            $relative = $f.FullName.Substring($Paths.Server.Length).TrimStart('\')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose() }

    # Which settings modules made in other config files belongs to the configuration saved above.
    if (Test-Path $Paths.ModuleSettings) { Copy-Item $Paths.ModuleSettings (Join-Path $partial 'module-settings.txt') }

    $stamp = if (Test-Path $Paths.BuildMarker) { [IO.File]::ReadAllText($Paths.BuildMarker).Trim() } else { '' }
    $revisions = if (Test-Path $Paths.Revisions) { [IO.File]::ReadAllLines($Paths.Revisions) } else { @() }
    $core = @($revisions | Where-Object { $_ -like 'core|*' } | ForEach-Object { $_.Split('|')[3] }) + @('') | Select-Object -First 1
    $bots = @($revisions | Where-Object { $_ -like 'module|*' } | ForEach-Object { $_.Split('|')[3] }) + @('') | Select-Object -First 1
    $size = (Get-ChildItem $partial -File | Measure-Object Length -Sum).Sum
    [IO.File]::WriteAllLines((Join-Path $partial 'manifest.txt'), @(
        'format=1', "created=$(Get-Date -Format 'yyyy-MM-dd HH:mm')", "reason=$Reason", "core=$core", "playerbots=$bots",
        "buildstamp=$stamp", "databases=$($databases -join ',')", "bytes=$size"))
    Move-Item $partial $folder
    Write-Log ('Backup saved: {0} ({1:N0} MB)' -f $name, ($size / 1MB)) Green

    # Only the newest snapshots are kept.
    $old = @(Get-ChildItem $Paths.Backups -Directory | Where-Object { $_.Name -notlike '*.partial' -and (Test-Path (Join-Path $_.FullName 'manifest.txt')) } |
             Sort-Object Name -Descending | Select-Object -Skip $KeepSnapshots)
    foreach ($o in $old) { Write-Log "Removing the old backup $($o.Name)"; Remove-Item $o.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    return $name
}

function Restore-Snapshot([string]$Name, [string]$Password) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (-not $Name) { throw 'No backup was chosen.' }
    $folder = Join-Path $Paths.Backups $Name
    $manifest = Read-SnapshotManifest $folder
    Assert-ServerStopped
    Write-Log "Restoring the backup $Name (made $($manifest['created']), $($manifest['reason']))"

    Enter-Phase 'restore' 'Restoring the databases'
    Start-Database
    $root = New-ClientOptions 'root' $Password
    try {
        if (-not (Test-SqlLogin $root)) { throw 'The database rejected the password.' }
        $modulesBefore = @(Get-ManagedModules $root | ForEach-Object { $_.Name })
        # Backups made before modules could be installed have no module registry.
        if (-not ($manifest['databases'].Split(',') -contains 'afk_modules')) { [void](Invoke-Sql $root 'DROP DATABASE IF EXISTS afk_modules;') }
        foreach ($db in $manifest['databases'].Split(',') | Where-Object { $_ }) {
            $dump = Join-Path $folder "$db.sql.gz"
            if (-not (Test-Path $dump)) { throw "The backup is missing $db.sql.gz." }
            Write-Log "Restoring the database $db ..."
            [void](Invoke-Sql $root "DROP DATABASE IF EXISTS ``$db``; CREATE DATABASE ``$db`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
            Invoke-Stream (Get-MySqlTool 'mysql.exe') @("--defaults-extra-file=$root", '--max-allowed-packet=512M', "--database=$db") -GzipIn $dump
        }
    } finally { Remove-Item $root -Force -ErrorAction SilentlyContinue }

    Enter-Phase 'files' 'Restoring the server programs and versions'
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $folder 'server.zip'))
    try {
        foreach ($entry in $zip.Entries) {
            if (-not $entry.Name) { continue }
            $target = Join-Path $Paths.Server $entry.FullName.Replace('/', '\')
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $zip.Dispose() }
    $savedSettings = Join-Path $folder 'module-settings.txt'
    if (Test-Path $savedSettings) { Copy-Item $savedSettings $Paths.ModuleSettings -Force } else { Remove-Item $Paths.ModuleSettings -Force -ErrorAction SilentlyContinue }
    Write-Log 'Server programs and configuration restored.'

    # The sources go back to the snapshot's versions, so "Check for updates" offers the newer code again.
    $git = Resolve-Git
    foreach ($part in @(@{ Item = $Project.Core; Folder = $Paths.Source; Key = 'core' }, @{ Item = $Project.Playerbots; Folder = $Paths.Playerbots; Key = 'playerbots' })) {
        $revision = $manifest[$part.Key]
        if ($revision -notmatch '^[0-9a-f]{40}$' -or -not (Test-Path (Join-Path $part.Folder '.git'))) { continue }
        $part.Item.Revision = $revision
        if ((Invoke-Program $git @('-C', $part.Folder, 'checkout', '--force', $revision) -AllowFailure) -ne 0) {
            [void](Invoke-Program $git @('-C', $part.Folder, 'fetch', '--no-tags', 'origin', $part.Item.Branch) -AllowFailure)
            [void](Invoke-Program $git @('-C', $part.Folder, 'checkout', '--force', $revision))
        }
    }
    Save-Revisions
    if ($manifest['buildstamp']) { [IO.File]::WriteAllText($Paths.BuildMarker, $manifest['buildstamp']) }
    $root = New-ClientOptions 'root' $Password
    try {
        Sync-ModuleFolders $git $root $modulesBefore
        Save-ModuleList $root
    } finally { Remove-Item $root -Force -ErrorAction SilentlyContinue }
    Stop-Database $Password
    Write-Log "The server is back at the state of $($manifest['created'])." Green
}

# =============================================================================
#  Main
# =============================================================================
function Assert-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Please run this as administrator.' }
}

function Assert-ServerStopped {
    $running = @(Get-Process -Name 'worldserver', 'authserver' -ErrorAction SilentlyContinue |
                 Where-Object { try { $_.Path -and $_.Path.StartsWith($Paths.Server, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
    if ($running.Count -gt 0) { throw 'The server is still running. Stop the worldserver and authserver first.' }
}

function Read-Password {
    if ($NonInteractive) {
        $password = $env:AC_DB_PASSWORD
        if (-not $password) { throw 'No database password was passed (environment variable AC_DB_PASSWORD).' }
    } else {
        while ($true) {
            $first = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host 'Database password (use the existing one for an existing server)' -AsSecureString)))
            $second = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host 'Repeat the password' -AsSecureString)))
            if ($first -eq $second) { $password = $first; break }
            Write-Host 'The passwords do not match.' -ForegroundColor Yellow
        }
    }
    if ($password.Length -lt 10) { throw 'The database password needs at least 10 characters.' }
    if ($password.IndexOfAny([char[]]';"\') -ge 0) { throw 'The database password must not contain a semicolon, quote or backslash.' }
    return $password
}

# Decides what to do: the -Mode parameter, a menu for an existing server, or a fresh install.
function Resolve-Mode {
    $installed = Test-Path (Join-Path $Paths.Server 'worldserver.exe')
    if ($Mode) { return $Mode }
    if (-not $installed) { return 'Install' }
    if ($NonInteractive) { throw 'A server already exists here; choose -Mode Update, Rebuild or Setup.' }
    Write-Host ''
    Write-Host "A server already exists in $($Paths.Server)." -ForegroundColor Yellow
    Write-Host '  U = check for updates and rebuild if needed'
    Write-Host '  R = rebuild from the current sources'
    Write-Host '  S = only set up database, world data and configuration'
    switch -Regex (Read-Host 'Your choice (U/R/S, anything else cancels)') {
        '^[uU]' { return 'Update' }
        '^[rR]' { return 'Rebuild' }
        '^[sS]' { return 'Setup' }
        default { return '' }
    }
}

$exitCode = 0
$password = $null
try {
    New-Item -ItemType Directory -Force -Path $Paths.Logs, $Paths.Deps, $Paths.Downloads, $Paths.Db, $Paths.Data | Out-Null
    Write-Log "===== $($Project.Name) server builder - $(Get-Date -Format 'yyyy-MM-dd HH:mm') - $InstallRoot ====="
    if (-not [Environment]::Is64BitOperatingSystem -or [Environment]::OSVersion.Version.Major -lt 10) { throw 'Windows 10 or 11 (64-bit) is required.' }
    Assert-Administrator
    Read-Revisions

    $action = Resolve-Mode
    if (-not $action) { Write-Log 'Cancelled; nothing was changed.'; Send-Event 'DONE' 'cancelled'; exit 0 }
    if ($action -eq 'Install' -and (Test-Path (Join-Path $Paths.Server 'worldserver.exe'))) {
        # A repeated installation continues where the last one stopped: without
        # compiling when the last build finished, otherwise with an incremental build.
        $finished = (Test-Path $Paths.BuildMarker) -and ([IO.File]::ReadAllText($Paths.BuildMarker).Trim() -eq (Get-BuildStamp))
        $action = if ($finished) { 'Setup' } else { 'Resume' }
    }
    Write-Log "Mode: $action"
    if ($action -eq 'Backup' -or $action -eq 'Restore') {
        if (-not $password) { $password = Read-Password }
        if ($action -eq 'Backup') {
            Enter-Phase 'backup' 'Backing up the server'
            $dbWasRunning = @(Get-DatabaseProcess).Count -gt 0
            Start-Database
            [void](New-Snapshot $password 'manual')
            if (-not $dbWasRunning) { Stop-Database $password }
        } else {
            Restore-Snapshot $Snapshot $password
        }
        Send-Event 'DONE' 'ok'
        exit 0
    }
    $compile = $action -ne 'Setup'
    if (-not $NonInteractive) { $password = Read-Password }

    Enter-Phase 'tools' 'Checking the build tools'
    $git = Resolve-Git
    if ($action -eq 'Update') {
        $changes = Find-Updates $git
        if ($changes.Count -eq 0 -and (Test-Path $Paths.BuildMarker) -and ([IO.File]::ReadAllText($Paths.BuildMarker).Trim() -eq (Get-BuildStamp))) {
            Write-Log 'Everything is up to date. Nothing was changed.' Green
            Send-Event 'DONE' 'uptodate'
            exit 0
        }
        if ($changes.Count -eq 0) { $changes.Add('The previous build did not finish; building again.') }
        foreach ($change in $changes) { Write-Log "  $change" Cyan; Send-Event 'CHANGE' $change }
    } elseif ($compile -and $action -ne 'Modules') {
        Resolve-LatestRevisions $git
    }
    $moduleAdd = @($AddModules.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $moduleRemove = @($RemoveModules.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($action -eq 'Modules' -and $moduleAdd.Count -eq 0 -and $moduleRemove.Count -eq 0) { throw 'No module was chosen.' }

    if ($compile) {
        Assert-ServerStopped
        # Before an update or rebuild changes anything, the current server is saved,
        # so it can be rolled back if the new version does not work.
        if (($action -eq 'Update' -or $action -eq 'Rebuild' -or $action -eq 'Modules') -and (Test-Path (Join-Path $Paths.Server 'worldserver.exe'))) {
            Enter-Phase 'backup' 'Backing up the current server'
            if (-not $password) { $password = Read-Password }
            Start-Database
            $what = if ($action -eq 'Modules') { 'module change' } else { $action.ToLower() }
            try { [void](New-Snapshot $password "before $what") }
            catch { throw ('The backup before the ' + $what + ' failed, so nothing was changed: ' + $_.Exception.Message) }
            Stop-Database $password
        }
        $cmake = Resolve-CMake
        Enter-Phase 'vs' 'Visual Studio C++ Build Tools'
        $vs = Resolve-VisualStudio
        Enter-Phase 'libs' 'OpenSSL and Boost'
        $openssl = Resolve-OpenSsl
        $boost = Resolve-Boost
    }
    Enter-Phase 'mysql' 'Portable MySQL database'
    Install-MySql

    $moduleState = $null
    if ($action -eq 'Modules') {
        Enter-Phase 'modules' 'Downloading and removing modules'
        $moduleState = Edit-ModuleFolders $git $moduleAdd $moduleRemove
    } elseif ($compile) {
        Enter-Phase 'source' 'Downloading the server source code'
        Sync-Sources $git
        if (-not $NonInteractive -and $action -ne 'Update') {
            Write-Host ''
            Write-Host "Optional: put additional AzerothCore modules into $(Join-Path $Paths.Source 'modules') now (one folder each, e.g. with git clone)." -ForegroundColor Yellow
            [void](Read-Host 'Press ENTER to start compiling')
        }
    }
    if ($compile) {
        try {
            # What the installed modules bring for the core and Playerbots goes into the fresh source.
            Invoke-ModulePatches $git
            Enter-Phase 'configure' 'Preparing the build (CMake)'
            Invoke-Configure $cmake $vs $openssl $boost -Clean:($action -eq 'Rebuild')
            Enter-Phase 'compile' 'Compiling the server'
            Invoke-Compile $cmake
        } catch {
            if (-not $moduleState) { throw }
            # The new programs are only installed after a successful build, so the server is unchanged.
            Undo-ModuleFolders $moduleState
            try { Invoke-ModulePatches $git -Quiet } catch { }
            $names = @($moduleState.Added | ForEach-Object { Split-Path $_ -Leaf })
            if ($names.Count -gt 0) {
                throw ('The server could not be built with ' + ($names -join ', ') + '. The module was taken out again and your server is unchanged. ' +
                       'The module is probably not compatible with this CoA version; the first compiler error is in logs\install.log. (' + $_.Exception.Message + ')')
            }
            throw ('The server could not be built without the removed module, so it was put back and your server is unchanged. (' + $_.Exception.Message + ')')
        }
        Copy-Runtime $openssl
        [IO.File]::WriteAllText($Paths.BuildMarker, (Get-BuildStamp))
    }

    Enter-Phase 'database' 'Setting up the database'
    if (-not $password) { $password = Read-Password }
    Start-Database
    Initialize-Database $password
    if ($moduleState) { Complete-ModuleChange $password $moduleState }
    Enter-Phase 'world' 'Importing the CoA world data'
    Import-WorldData $password
    Enter-Phase 'finish' 'Configuration and final checks'
    Set-ServerConfig $password
    Set-ModuleSettings
    Repair-IncompleteDatabases $password
    Update-Databases $password
    Set-RealmName $password
    Resolve-DbcTools
    Write-Launchers $password
    Test-Installation
    Stop-Database $password
    if ($moduleState) { foreach ($r in $moduleState.Removed) { if ($r.Aside) { Remove-Item $r.Aside -Recurse -Force -ErrorAction SilentlyContinue } } }

    Write-Log ''
    Write-Log 'FINISHED. The server is ready.' Green
    if (-not (Test-Path (Join-Path $Paths.Data 'maps'))) { Write-Log 'Next: create the map data from your game client, then start the server.' Yellow }
    Send-Event 'DONE' 'ok'
} catch {
    $exitCode = 1
    Write-Log ('ERROR: ' + $_.Exception.Message) Red
    Write-Log "Full log: $($Paths.Log)"
    Send-Event 'FAIL' $_.Exception.Message
} finally {
    $password = $null
}
exit $exitCode
