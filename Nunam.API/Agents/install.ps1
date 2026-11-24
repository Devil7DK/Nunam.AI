<#
.SYNOPSIS
    Installs the Nunam indexer on this machine and connects it to the server it came from.

.DESCRIPTION
    The web UI's Machines > Add machine shows the command to run, with a one-time code, in PowerShell
    opened as administrator:
    & ([scriptblock]::Create((irm <server>/api/agent/install.ps1))) -Server <server> -Code <code>
#>
param(
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$Code,
    [string]$Name,
    [switch]$RemoteManagement
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$Server = $Server.TrimEnd("/")

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) { throw "Run it in PowerShell opened as administrator." }

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
$msi = Join-Path $env:TEMP "nunam-indexer-$arch.msi"
Write-Host "Downloading the indexer from $Server..."
Invoke-WebRequest -UseBasicParsing -Uri "$Server/api/agent/packages/msi?arch=$arch" -OutFile $msi

Write-Host "Installing..."
$install = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart" -Wait -PassThru
Remove-Item $msi -ErrorAction SilentlyContinue
if ($install.ExitCode -notin 0, 3010) { throw "The installer failed with code $($install.ExitCode)." }

$installDir = Join-Path $env:ProgramFiles "Nunam\Indexer"
$arguments = @("enroll", "--server", $Server, "--code", $Code)
if ($Name) { $arguments += @("--name", $Name) }
if ($RemoteManagement) { $arguments += "--remote-management" }
& (Join-Path $installDir "Nunam.Indexer.exe") @arguments
if ($LASTEXITCODE -ne 0) { throw "Connecting to the server failed." }

# Asks which account the service runs as, grants it the watch paths and starts it. Run as a file
# under its own policy: the default execution policy blocks .ps1 files, though not this pasted script.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $installDir "Configure-Indexer.ps1")
if ($LASTEXITCODE) { throw "Configure Nunam Indexer failed; run it from the Start menu." }
