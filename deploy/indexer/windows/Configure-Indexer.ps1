<#
.SYNOPSIS
    Sets up the Nunam Indexer service that the MSI installed, and starts it.

.DESCRIPTION
    Start menu > Configure Nunam Indexer runs it. It opens appsettings.Local.json in Notepad while the
    connection string is still the template's; once that is filled in, it grants the service account
    read access to each watch path and (re)starts the service. Run it again after changing the watch
    paths. The installer runs it with -Unattended after every install and upgrade.

.PARAMETER Account
    Service account. The default, the service's own virtual account NT SERVICE\Nunam.Indexer, reads
    only what it is granted. It can't reach network shares; to index those, use a user account that
    can (you will be asked for its password). LocalSystem also works, but a flaw in a file parser would
    then control the machine. An upgrade puts the service back on the virtual account.

.PARAMETER Unattended
    Never prompt or open Notepad, and leave the service stopped while the settings are the template's.
#>
param(
    [string]$Account = "NT SERVICE\Nunam.Indexer",
    [switch]$Unattended
)

$ErrorActionPreference = "Stop"
$serviceName = "Nunam.Indexer"
$virtualAccount = "NT SERVICE\$serviceName"
$installDir = $PSScriptRoot
$settingsPath = Join-Path $installDir "appsettings.Local.json"

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) {
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-Account", "`"$Account`"")
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    exit
}

function Finish([string]$message) {
    if ($message) { Write-Host $message }
    if (-not $Unattended) { Read-Host "Press Enter to close" | Out-Null }
    exit
}

# Profile folders (C:\Users\name) are readable only by their owner. Granting on a drive root would
# rewrite the permissions of every file on it, so a watch path holding profiles gets grants on each
# profile's own folders instead, AppData and hidden folders left out; everything else on the drive is
# already readable by Users.
function Get-GrantTargets([string]$path) {
    $full = [IO.Path]::GetFullPath($path).TrimEnd('\')
    $profilesRoot = Join-Path $env:SystemDrive "Users"
    $isDriveRoot = $full.Length -le 2
    $profiles = @(Get-ChildItem -LiteralPath $profilesRoot -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $_.Name -notin @("Default", "Public") })

    $covered = if (($isDriveRoot -and $profilesRoot.StartsWith($full, "OrdinalIgnoreCase")) -or $full -ieq $profilesRoot) {
        $profiles
    } else {
        @($profiles | Where-Object { $_.FullName -ieq $full })
    }

    if ($covered.Count -eq 0) {
        if ($isDriveRoot) { return @() }
        return @(@{ Path = $full; Inherit = $true })
    }

    foreach ($profile in $covered) {
        @{ Path = $profile.FullName; Inherit = $false }
        Get-ChildItem -LiteralPath $profile.FullName -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { -not ($_.Attributes -band ([IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::ReparsePoint)) -and
                           -not $_.Name.StartsWith(".") -and $_.Name -ine "AppData" } |
            ForEach-Object { @{ Path = $_.FullName; Inherit = $true } }
    }
}

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if (-not $service) { Finish "The $serviceName service isn't installed; install the Nunam Indexer MSI first." }

# The same places the indexer looks, unless Indexer:OcrTesseractPath points elsewhere.
if (-not (Get-Command tesseract.exe -ErrorAction SilentlyContinue) -and
    -not (Test-Path (Join-Path $env:ProgramFiles "Tesseract-OCR\tesseract.exe"))) {
    Write-Warning "Tesseract not found; images and scanned PDFs will be indexed without their text. Install it with: winget install UB-Mannheim.TesseractOCR"
}

if (-not (Test-Path $settingsPath)) {
    Copy-Item (Join-Path $installDir "appsettings.Local.example.json") $settingsPath
}

# The file holds the database password: only Administrators, SYSTEM and the
# service may read it. A fresh ACL, because a file copied in from elsewhere keeps explicit grants that
# dropping inheritance leaves behind. Set before the file is filled in, so the secrets never sit in a readable file.
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($identity in @("BUILTIN\Administrators", "NT AUTHORITY\SYSTEM")) {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "FullControl", "Allow")))
}
if ($Account -ne "LocalSystem") {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($Account, "Read", "Allow")))
}
Set-Acl $settingsPath $acl

$isTemplate = { (Get-Content $settingsPath -Raw | ConvertFrom-Json).ConnectionStrings.Postgres -like "Paste the line*" }
if ((& $isTemplate) -and -not $Unattended) {
    Write-Host "Fill in the connection string, license key and watch paths, then save and close Notepad."
    Start-Process notepad.exe -ArgumentList "`"$settingsPath`"" -Wait
}

Stop-Service -Name $serviceName -ErrorAction SilentlyContinue

# Delayed start keeps boot fast; restart after 1 minute if the process dies.
sc.exe config $serviceName start= delayed-auto | Out-Null
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

if ($Account -eq $virtualAccount) { sc.exe sidtype $serviceName unrestricted | Out-Null }
if ($service.StartName -ne $Account) {
    Write-Host "Running $serviceName as $Account (was $($service.StartName))..."
    if ($Account -eq $virtualAccount) {
        $result = sc.exe config $serviceName obj= $Account
    } elseif ($Account -eq "LocalSystem") {
        $result = sc.exe config $serviceName obj= LocalSystem
    } elseif ($Unattended) {
        Finish "Not switching to $Account without a password; run Configure Nunam Indexer."
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

if (& $isTemplate) {
    Finish "appsettings.Local.json still has the template's connection string; the service stays stopped."
}

if ($Account -ne "LocalSystem") {
    $watchPaths = @((Get-Content $settingsPath -Raw | ConvertFrom-Json).Indexer.WatchPaths) | Where-Object { $_ }
    foreach ($path in $watchPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Container)) {
            Write-Warning "Watch path $path not found; once it exists, run Configure Nunam Indexer again to grant $Account read access."
            continue
        }
        foreach ($target in Get-GrantTargets $path) {
            # Inherited by everything below, except folders that block inheritance; those stay unreadable.
            # A profile folder itself passes it to its own files only, not to AppData and the like.
            $rights = if ($target.Inherit) { "(OI)(CI)RX" } else { "(OI)RX" }
            Write-Host "Granting $Account read access to $($target.Path)..."
            $ErrorActionPreference = "Continue"
            $result = icacls $target.Path /grant "${Account}:$rights" /Q
            $grantExit = $LASTEXITCODE
            $ErrorActionPreference = "Stop"
            if ($grantExit -ne 0) { Write-Warning "Could not grant access to $($target.Path): $result" }
        }
    }
}

Write-Host "Starting $serviceName..."
Start-Service -Name $serviceName
Finish "Running. Logs: Event Viewer > Windows Logs > Application, source '$serviceName'."
