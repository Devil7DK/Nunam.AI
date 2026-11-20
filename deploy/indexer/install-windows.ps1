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
    Service account. The default, LocalSystem, can read local disks but not network shares; to
    index shares, run as a user account that can reach them (you will be asked for its password).
#>
param(
    [string]$InstallDir = "$env:ProgramFiles\Nunam\Indexer",
    [string]$Account = "LocalSystem"
)

$ErrorActionPreference = "Stop"
$serviceName = "Nunam.Indexer"
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

# The file holds the database password and the Syncfusion key: only Administrators and SYSTEM may read it.
# A fresh ACL, because a file copied in from elsewhere keeps explicit grants that dropping inheritance leaves behind.
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($identity in @("BUILTIN\Administrators", "NT AUTHORITY\SYSTEM")) {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "FullControl", "Allow")))
}
if ($Account -ne "LocalSystem") {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($Account, "Read", "Allow")))
}
Set-Acl $localSettings $acl

$exe = Join-Path $InstallDir "Nunam.Indexer.exe"
if (-not $existing) {
    Write-Host "Creating service $serviceName..."
    $params = @{
        Name           = $serviceName
        BinaryPathName = "`"$exe`""
        DisplayName    = "Nunam Indexer"
        Description    = "Indexes local files into the Nunam search index."
        StartupType    = "Automatic"
    }
    if ($Account -ne "LocalSystem") {
        $params.Credential = Get-Credential -UserName $Account -Message "Password for the service account"
    }
    New-Service @params | Out-Null
    # Delayed start keeps boot fast; restart after 1 minute if the process dies.
    sc.exe config $serviceName start= delayed-auto | Out-Null
    sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
}

Write-Host "Starting $serviceName..."
Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table -AutoSize Name, Status, StartType
Write-Host "Logs: Event Viewer > Windows Logs > Application, source 'Nunam.Indexer'."
