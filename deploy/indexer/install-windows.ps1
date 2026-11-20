#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publishes Nunam.Indexer and installs (or updates) it as a Windows service.

.DESCRIPTION
    Run from the repository root in an elevated PowerShell:
        powershell -ExecutionPolicy Bypass -File .\deploy\indexer\install-windows.ps1
    Re-running it updates the binaries and restarts the service; appsettings.Local.json is kept.
    Needs the .NET 10 SDK to publish, the Visual C++ 2015-2022 x64 runtime for OCR, and ffmpeg on
    PATH (or Indexer:FFmpegBinaryFolder) for video.

.PARAMETER InstallDir
    Where the indexer is installed. Defaults to C:\Program Files\Nunam\Indexer.

.PARAMETER Account
    Service account. The default, the service's own virtual account NT SERVICE\Nunam.Indexer, reads
    only what it is granted: the script grants it read access to each watch path. It can't reach
    network shares; to index those, run as a user account that can (you will be asked for its
    password). LocalSystem also works, but a flaw in a file parser would then control the machine.
#>
param(
    [string]$InstallDir = "$env:ProgramFiles\Nunam\Indexer",
    [string]$Account = "NT SERVICE\Nunam.Indexer"
)

$ErrorActionPreference = "Stop"
$serviceName = "Nunam.Indexer"
$virtualAccount = "NT SERVICE\$serviceName"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne "Stopped") {
    Write-Host "Stopping $serviceName..."
    Stop-Service -Name $serviceName
    $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

# Tesseract and Leptonica link against it; without it every OCR call fails and files are indexed without recognized text.
if (-not (Test-Path "$env:SystemRoot\System32\vcruntime140_1.dll")) {
    Write-Warning "Visual C++ 2015-2022 x64 runtime not found; OCR will not work. Install it from https://aka.ms/vs/17/release/vc_redist.x64.exe"
}

Write-Host "Publishing to $InstallDir..."
# Windows PowerShell turns a native command's stderr into a terminating error under "Stop" when output is redirected.
$ErrorActionPreference = "Continue"
dotnet publish (Join-Path $repoRoot "Nunam.Indexer\Nunam.Indexer.csproj") `
    -c Release -r win-x64 --self-contained false -o $InstallDir
$publishExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($publishExit -ne 0) { throw "dotnet publish failed" }

$localSettings = Join-Path $InstallDir "appsettings.Local.json"
if (-not (Test-Path $localSettings)) {
    Copy-Item (Join-Path $PSScriptRoot "appsettings.Local.example.json") $localSettings
    Write-Warning "Created $localSettings - fill in the connection string, license key and watch paths, then run this script again."
    notepad $localSettings
    exit 1
}

$exe = Join-Path $InstallDir "Nunam.Indexer.exe"
if (-not $existing) {
    Write-Host "Creating service $serviceName..."
    New-Service -Name $serviceName -BinaryPathName "`"$exe`"" -DisplayName "Nunam Indexer" `
        -Description "Indexes local files into the Nunam search index." -StartupType Automatic | Out-Null
    # Delayed start keeps boot fast; restart after 1 minute if the process dies.
    sc.exe config $serviceName start= delayed-auto | Out-Null
    sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
}

# Set on every run, so re-running with another -Account moves an existing install over.
$current = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").StartName
if ($current -ne $Account) {
    Write-Host "Running $serviceName as $Account (was $current)..."
    if ($Account -eq $virtualAccount) {
        sc.exe sidtype $serviceName unrestricted | Out-Null
        $result = sc.exe config $serviceName obj= $Account
    } elseif ($Account -eq "LocalSystem") {
        $result = sc.exe config $serviceName obj= LocalSystem
    } else {
        $password = (Get-Credential -UserName $Account -Message "Password for the service account").GetNetworkCredential().Password
        $result = sc.exe config $serviceName obj= $Account password= $password
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not set the service account: $result" }
}

# A non-admin account can't register the event log source itself, and logging fails without it.
if (-not [System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}

# The file holds the database password and the Syncfusion key: only Administrators, SYSTEM and the
# service may read it. A fresh ACL, because a file copied in from elsewhere keeps explicit grants that
# dropping inheritance leaves behind.
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($identity in @("BUILTIN\Administrators", "NT AUTHORITY\SYSTEM")) {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "FullControl", "Allow")))
}
if ($Account -ne "LocalSystem") {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($Account, "Read", "Allow")))
}
Set-Acl $localSettings $acl

if ($Account -ne "LocalSystem") {
    $watchPaths = @((Get-Content $localSettings -Raw | ConvertFrom-Json).Indexer.WatchPaths) | Where-Object { $_ }
    foreach ($path in $watchPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Container)) {
            Write-Warning "Watch path $path not found; once it exists, run this script again to grant $Account read access."
            continue
        }
        # Inherited by everything below, except folders that block inheritance; those stay unreadable.
        Write-Host "Granting $Account read access to $path..."
        $ErrorActionPreference = "Continue"
        $result = icacls $path /grant "${Account}:(OI)(CI)RX" /Q
        $grantExit = $LASTEXITCODE
        $ErrorActionPreference = "Stop"
        if ($grantExit -ne 0) { Write-Warning "Could not grant access to ${path}: $result" }
    }
}

Write-Host "Starting $serviceName..."
Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table -AutoSize Name, Status, StartType
Write-Host "Logs: Event Viewer > Windows Logs > Application, source 'Nunam.Indexer'."
