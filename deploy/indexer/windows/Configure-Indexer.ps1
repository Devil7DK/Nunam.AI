<#
.SYNOPSIS
    Sets up the Nunam Indexer service that the MSI installed, and starts it.

.DESCRIPTION
    Start menu > Configure Nunam Indexer runs it. It opens appsettings.Local.json in Notepad while the
    connection string is still the template's; once that is filled in, it grants the service account
    read access to each watch path and (re)starts the service. Run it again after changing the watch
    paths, or to change the account the service runs as. The installer runs it with -Register and
    -Unattended after every install and upgrade.

.PARAMETER Account
    Service account; without it the script asks, offering the current one. The service's own virtual
    account, NT SERVICE\Nunam.Indexer, reads only the watch paths, which the script grants it, and
    can't reach network shares. A user account (DOMAIN\user, asks for its password) reads what that
    user can, with no grants. LocalSystem also works, but a flaw in a file parser would then control
    the machine. Upgrades keep the account.

.PARAMETER Credential
    The user account's password, for running without the password prompt: -Credential (Get-Credential).

.PARAMETER Unattended
    Never prompt or open Notepad, keep the service's account unless -Account is given, and leave the
    service stopped while the settings are the template's.

.PARAMETER Register
    Create the service, or point it at this folder. The installer runs this.
#>
param(
    [string]$Account,
    [pscredential]$Credential,
    [switch]$Unattended,
    [switch]$Register,
    [string]$InvokingUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
)

$ErrorActionPreference = "Stop"
$serviceName = "Nunam.Indexer"
$virtualAccount = "NT SERVICE\$serviceName"
$installDir = $PSScriptRoot
$settingsPath = Join-Path $installDir "appsettings.Local.json"

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) {
    $arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-InvokingUser", "`"$InvokingUser`"")
    if ($Account) { $arguments += @("-Account", "`"$Account`"") }
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

# The installer creates the service here rather than with ServiceInstall, which would recreate it on
# every upgrade and so drop a user account's password.
if ($Register) {
    $binaryPath = "`"$(Join-Path $installDir "Nunam.Indexer.exe")`""
    $existing = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    if ($existing) {
        $result = Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments @{ PathName = $binaryPath }
        if ($result.ReturnValue -ne 0) { throw "Could not update the service: error $($result.ReturnValue)" }
    } else {
        # Created stopped; the account is set before anything can start it.
        New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName "Nunam Indexer" -StartupType Automatic `
            -Description "Indexes local files into the Nunam search index." | Out-Null
        sc.exe sidtype $serviceName unrestricted | Out-Null
        $result = sc.exe config $serviceName obj= $virtualAccount
        if ($LASTEXITCODE -ne 0) { throw "Could not set the service account: $result" }
    }
    exit 0
}

function Get-Sid([string]$name) {
    try { (New-Object Security.Principal.NTAccount($name)).Translate([Security.Principal.SecurityIdentifier]) } catch { $null }
}

function Get-AccountName([string]$name) {
    if ($name -eq "LocalSystem") { return $name }
    $sid = Get-Sid $name
    if ($sid) { $sid.Translate([Security.Principal.NTAccount]).Value } else { $name }
}

# sc.exe and Win32_Service.Change don't grant "Log on as a service" the way the Services console does,
# and without it the service fails to start with error 1069.
$lsaSource = @"
using System;
using System.Runtime.InteropServices;

public static class NunamLogon
{
    [StructLayout(LayoutKind.Sequential)]
    struct LsaString { public ushort Length, MaximumLength; public IntPtr Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    struct LsaObjectAttributes { public int Length; public IntPtr RootDirectory, ObjectName; public int Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }

    [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr system, ref LsaObjectAttributes attributes, int access, out IntPtr policy);
    [DllImport("advapi32.dll")] static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, LsaString[] rights, int count);
    [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr policy);
    [DllImport("advapi32.dll")] static extern int LsaNtStatusToWinError(uint status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool LogonUser(string user, string domain, string password, int type, int provider, out IntPtr token);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    public static int GrantServiceLogon(byte[] sid)
    {
        var attributes = new LsaObjectAttributes { Length = Marshal.SizeOf(typeof(LsaObjectAttributes)) };
        IntPtr policy;
        // POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES
        var status = LsaOpenPolicy(IntPtr.Zero, ref attributes, 0x10 | 0x800, out policy);
        if (status != 0) return LsaNtStatusToWinError(status);
        const string right = "SeServiceLogonRight";
        var name = new LsaString { Buffer = Marshal.StringToHGlobalUni(right), Length = (ushort)(right.Length * 2), MaximumLength = (ushort)(right.Length * 2 + 2) };
        try { return LsaNtStatusToWinError(LsaAddAccountRights(policy, sid, new[] { name }, 1)); }
        finally { Marshal.FreeHGlobal(name.Buffer); LsaClose(policy); }
    }

    // A service logon, as the service control manager does it, so a wrong password shows up here.
    public static int CheckServiceLogon(string user, string domain, string password)
    {
        IntPtr token;
        if (!LogonUser(user, domain, password, 5, 0, out token)) return Marshal.GetLastWin32Error();
        CloseHandle(token);
        return 0;
    }
}
"@

function Set-UserAccount([string]$name) {
    $sid = Get-Sid $name
    if (-not $sid) { throw "No account named $name." }
    $bytes = New-Object byte[] $sid.BinaryLength
    $sid.GetBinaryForm($bytes, 0)
    if (-not ("NunamLogon" -as [type])) { Add-Type -TypeDefinition $lsaSource }
    $grant = [NunamLogon]::GrantServiceLogon($bytes)
    if ($grant -ne 0) { throw "Could not let $name log on as a service: error $grant" }

    $domain, $user = if ($name.Contains("\")) { $name -split "\\", 2 } else { $null, $name }
    for ($attempt = 1; ; $attempt++) {
        $given = $Credential -and $attempt -eq 1
        $entered = if ($given) { $Credential } else { Get-Credential -UserName $name -Message "Password for $name, which the indexer will run as" }
        if (-not $entered) { throw "No password given; the service account is unchanged." }
        $password = $entered.GetNetworkCredential().Password
        $logon = [NunamLogon]::CheckServiceLogon($user, $domain, $password)
        if ($logon -eq 0) { break }
        # 1326: wrong user name or password.
        if ($logon -ne 1326 -or $attempt -ge 3 -or $Unattended) { throw "Could not sign in as ${name}: error $logon" }
        Write-Warning "Wrong password for $name."
    }

    # Through WMI, not sc.exe, so the password isn't on a command line.
    $result = Invoke-CimMethod -InputObject (Get-CimInstance Win32_Service -Filter "Name='$serviceName'") -MethodName Change `
        -Arguments @{ StartName = $name; StartPassword = $password }
    if ($result.ReturnValue -ne 0) { throw "Could not set the service account: error $($result.ReturnValue)" }
}

function Read-AccountChoice([string]$current) {
    $me = Get-AccountName $InvokingUser
    Write-Host ""
    Write-Host "Which account should the indexer run as? It runs as $current now."
    Write-Host "  1  Its own account, $virtualAccount. Reads only the watch paths, which this grants it."
    Write-Host "     Can't reach network shares."
    Write-Host "  2  Your account, $me. Reads everything you can, network shares included, with no grants."
    Write-Host "     Asks for your password; run this again after you change it."
    Write-Host "  3  Another account."
    while ($true) {
        switch ((Read-Host "Choose 1, 2 or 3, or press Enter to keep $current").Trim()) {
            "" { return @{ Name = $current; Password = $false } }
            "1" { return @{ Name = $virtualAccount; Password = $false } }
            "2" { return @{ Name = $me; Password = $true } }
            "3" {
                $other = (Read-Host "Account (DOMAIN\user, or .\user for a local one)").Trim()
                if ($other -eq "LocalSystem") { return @{ Name = $other; Password = $false } }
                if ($other -and (Get-Sid $other)) { return @{ Name = (Get-AccountName $other); Password = $true } }
                Write-Warning "No account named '$other'."
            }
        }
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

$currentAccount = Get-AccountName $service.StartName
$askPassword = $false
if ($Account) {
    $Account = Get-AccountName $Account
    $askPassword = $Account -notin @($virtualAccount, "LocalSystem") -and ($Credential -or $Account -ne $currentAccount)
} elseif ($Unattended) {
    $Account = $currentAccount
} else {
    $choice = Read-AccountChoice $currentAccount
    $Account = $choice.Name
    $askPassword = $choice.Password
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

# The virtual account exists only while the service has its own SID.
sc.exe sidtype $serviceName unrestricted | Out-Null
if ($askPassword) {
    if ($Unattended -and -not $Credential) { Finish "Not switching to $Account without a password; run Configure Nunam Indexer." }
    Write-Host "Running $serviceName as $Account (was $currentAccount)..."
    Set-UserAccount $Account
} elseif ($Account -ne $currentAccount) {
    Write-Host "Running $serviceName as $Account (was $currentAccount)..."
    $result = sc.exe config $serviceName obj= $Account
    if ($LASTEXITCODE -ne 0) { throw "Could not set the service account: $result" }
}

# A non-admin account can't register the event log source itself, and logging fails without it.
if (-not [System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}

if (& $isTemplate) {
    Finish "appsettings.Local.json still has the template's connection string; the service stays stopped."
}

# Other accounts read what their user can; granting them other people's profiles would widen that.
if ($Account -eq $virtualAccount) {
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
try {
    Start-Service -Name $serviceName
} catch {
    if ($Account -notin @($virtualAccount, "LocalSystem")) {
        Write-Warning "If $Account's password changed, run Configure Nunam Indexer again and choose the account with its new password."
    }
    throw
}
Finish "Running. Logs: Event Viewer > Windows Logs > Application, source '$serviceName'."
