# =============================================================================
# WinLicManager.ps1  --  Windows Licensing & Information Manager  v1.3-beta1
# =============================================================================
# Mirrors the WinLic Manager GUI application for power-user / CLI usage.
#
# Menu:
#   1 -- OS Version & OEM BIOS Key
#   2 -- License Channel & Type  (slmgr /dli)
#   3 -- Inspect Keys & Activation Status
#   4 -- Test & Install Product Key        [admin required -- prompted on demand]
#   5 -- Uninstall License Key             [admin required -- prompted on demand]
#   6 -- Reset Activation / Rearm          [admin required -- prompted on demand]
#   7 -- 3rd Party Activation Audit
#   Q -- Quit
#
# Elevation model:
#   The script starts without requiring admin.  Options 1, 2, 3, 7 work fully
#   in a standard/non-admin shell.  Selecting 4, 5, or 6 triggers a relaunch-
#   as-admin prompt (same behaviour as the GUI application).
#
# settings.ini  (optional, place next to this script):
#   Extend the Option 7 scan lists.  Format -- one value per line per section:
#
#     [ExtraPorts]          ; additional TCP ports to probe on localhost
#     1689
#     [ExtraServices]       ; additional service name keywords
#     MyKmsService
#     [ExtraTaskKeywords]   ; additional scheduled-task keywords
#     MyAutoTask
#     [ExtraProcesses]      ; additional process name keywords
#     myproc.exe
#     [ExtraFilePaths]      ; additional file / folder paths to check
#     C:\Tools\KMSTool
# =============================================================================

Set-StrictMode -Off
$ErrorActionPreference = 'SilentlyContinue'

# ---- Globals ----------------------------------------------------------------
$SCRIPT_VERSION = "v1.3-beta1"
$SCRIPT_DIR     = Split-Path -Parent $MyInvocation.MyCommand.Path
$SETTINGS_FILE  = Join-Path $SCRIPT_DIR "settings.ini"
$slmgrPath      = Join-Path $env:SystemRoot "System32\slmgr.vbs"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Known GVLK and HWID placeholder keys (last 5 chars of PartialProductKey)
# Mirrors [GvlkKeys] in settings.default.ini. Keep in sync.
$genericKeys = @{
    # Windows 11 / 10 Semi-Annual Channel
    "T83GX" = "Win 11/10 Pro"
    "GCQG9" = "Win 11/10 Pro N"
    "6Q84J" = "Win 11/10 Pro for Workstations"
    "6XYWF" = "Win 11/10 Pro for Workstations N"
    "J447Y" = "Win 11/10 Pro Education"
    "66QFC" = "Win 11/10 Pro Education N"
    "VCFB2" = "Win 11/10 Education / Pro Edu HWID"
    "MDWWJ" = "Win 11/10 Education N"
    "2YT43" = "Win 11/10 Enterprise"
    "KHJW4" = "Win 11/10 Enterprise N"
    "4M68B" = "Win 11/10 Enterprise G"
    "T84FV" = "Win 11/10 Enterprise G N"
    # LTSC / IoT / LTSB
    "J462D" = "Win 11 LTSC 2024 / Win10 LTSC 2021/2019"
    "7CG2H" = "Win 11/10 Enterprise N LTSC"
    "PDQGT" = "Win IoT Enterprise LTSC 2024/2021"
    "QJ4BJ" = "Win 10 Enterprise LTSB 2016"
    "8B639" = "Win 10 Enterprise N LTSB 2016"
    "76DF9" = "Win 10 Enterprise LTSB 2015"
    "D69TJ" = "Win 10 Enterprise N LTSB 2015"
    # Windows 8.1
    "9D6T9" = "Windows 8.1 Pro"
    "B4FXY" = "Windows 8.1 Pro N"
    "MKKG7" = "Windows 8.1 Enterprise"
    "JFFXW" = "Windows 8.1 Enterprise N"
    # HWID / DE placeholder keys
    "3V66T" = "Win 10/11 Pro (HWID placeholder)"
    "8HVX7" = "Win 10/11 Home (HWID placeholder)"
    "H8Q99" = "Win 10 Home (HWID placeholder)"
    "WXCHW" = "Win 10/11 Home Single Language (HWID placeholder)"
    "WGGBY" = "Win 10 Pro Education (HWID placeholder)"
    "2YV77" = "Win 10 Pro for Workstations (HWID placeholder)"
    "8DEC2" = "Win 10 Enterprise (HWID placeholder)"
}

# ---- settings.ini parser ----------------------------------------------------
function Read-IniSection {
    param([string]$Path, [string]$Section)
    $result = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path $Path)) { return $result }
    $inSection = $false
    foreach ($line in Get-Content $Path) {
        $line = $line.Trim()
        if ($line -match '^\[(.+)\]$') {
            $inSection = ($Matches[1].Trim() -eq $Section)
        } elseif ($inSection -and $line -and
                  -not $line.StartsWith(';') -and -not $line.StartsWith('#')) {
            $result.Add($line)
        }
    }
    return $result
}

# ---- Audit scan lists (defaults + settings.ini extensions) ------------------
$DEFAULT_PORTS    = @(1688)
$DEFAULT_SERVICES = @('KMSpico','KMService','WinKSO','KMSELDI','KMS_VL_ALL',
                       'KMSAuto','AutoKMS','KMSSS','KMSEmulator','vlmcsd','Activation-Renewal')
$DEFAULT_TASKS    = @('AutoKMS','KMSAuto','KMS_VL_ALL','KMSpico','KMSSS',
                       'KMSEmulator','KMService','WinKSO','vlmcsd','Activation-Renewal')
$DEFAULT_PROCS    = @('KMSpico','KMSELDI','AutoKMS','KMSAuto','KMSguard',
                       'WinKSO','KMService','vlmcsd','AAct','KMS_VL_ALL','gatherosstate','clipup')
# Hardcoded KMS piracy domains -- mirrors [KmsPiracyDomains] in settings.default.ini
$DEFAULT_KMS_DOMAINS = @(
    'msguides',       # km8.msguides.com, kms2.msguides.com, kms9.msguides.com
    'kms.loli',       # kms.loli.beer
    'digiboy.ir',
    '0t.ng',
    'kms.chinancce',
    'kmscloud',
    'kms.cangshui',
    'kms.ddns.net',
    'e8.us.to',
    'kms.mrxinwang',
    'kms8.msguides',
    'kms9.msguides',
    'kms.xspace.in',
    'skms.netnr'
)

function Get-ScanLists {
    $extraPorts   = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraPorts' |
                    Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ }
    $extraSvc     = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraServices'
    $extraTasks   = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraTaskKeywords'
    $extraProcs   = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraProcesses'
    $extraFiles   = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraFilePaths'
    $extraDomains = @(
        (Read-IniSection -Path $SETTINGS_FILE -Section 'KmsPiracyDomains') +
        (Read-IniSection -Path $SETTINGS_FILE -Section 'UserKmsPiracyDomains')
    ) | Where-Object { $_ }

    # Load GVLK keys from both [GvlkKeys] and [UserGvlkKeys]
    # Extract last 5 chars of each key for PartialProductKey matching
    $gvlkSuffixes = @(
        (Read-IniSection -Path $SETTINGS_FILE -Section 'GvlkKeys') +
        (Read-IniSection -Path $SETTINGS_FILE -Section 'UserGvlkKeys')
    ) | Where-Object { $_ } | ForEach-Object {
        # Handle both  "W269N-WFGWX-YVC9B-4J6C9-T83GX = Windows 11/10 Pro"
        # and bare key forms
        $keyPart = if ($_ -match '=') { ($_ -split '=')[0].Trim() } else { $_.Trim() }
        $alnum   = $keyPart -replace '[^A-Za-z0-9]',''
        if ($alnum.Length -ge 5) { $alnum.Substring($alnum.Length - 5).ToUpper() }
    } | Where-Object { $_ } | Sort-Object -Unique

    return @{
        Ports       = ($DEFAULT_PORTS    + $extraPorts)   | Sort-Object -Unique
        Services    = ($DEFAULT_SERVICES + $extraSvc)     | Sort-Object -Unique
        Tasks       = ($DEFAULT_TASKS    + $extraTasks)   | Sort-Object -Unique
        Processes   = ($DEFAULT_PROCS    + $extraProcs)   | Sort-Object -Unique
        ExtraFiles  = $extraFiles
        KmsDomains  = ($DEFAULT_KMS_DOMAINS + $extraDomains) | Sort-Object -Unique
        GvlkSuffixes = $gvlkSuffixes
    }
}

# ---- Output helpers ---------------------------------------------------------
function Write-Sep   { Write-Host ('-' * 65) -ForegroundColor DarkGray }
function Write-Sep2  { Write-Host ('=' * 65) -ForegroundColor DarkGray }
function Write-Blank { Write-Host '' }
function Write-Step  { param([string]$msg) Write-Host "[>] $msg" -ForegroundColor Cyan }
function Write-OK    { param([string]$msg) Write-Host "[+] $msg" -ForegroundColor Green }
function Write-Warn  { param([string]$msg) Write-Host "[!] $msg" -ForegroundColor Yellow }
function Write-Fail  { param([string]$msg) Write-Host "[-] $msg" -ForegroundColor Red }
function Write-Info  { param([string]$msg) Write-Host "    $msg" -ForegroundColor DarkCyan }
function Write-Cmd   { param([string]$msg) Write-Host "  CMD: $msg" -ForegroundColor DarkGray }
function Write-Data  {
    param([string]$label, [string]$value)
    Write-Host ("    {0,-28} {1}" -f $label, $value) -ForegroundColor White
}

# ---- Internet + DNS helpers for KMS cloud check -----------------------------
function Test-Internet {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $ar  = $tcp.BeginConnect('8.8.8.8', 53, $null, $null)
        $ok  = $ar.AsyncWaitHandle.WaitOne(1500) -and $tcp.Connected
        try { $tcp.EndConnect($ar) } catch {}
        $tcp.Close()
        return $ok
    } catch { return $false }
}

function Resolve-KmsHost {
    param([string]$Host)
    Write-Info 'Checking internet and attempting DNS resolution...'
    if (-not (Test-Internet)) {
        Write-Info 'No internet access -- DNS verification skipped.'
        return
    }
    try {
        $ips = [System.Net.Dns]::GetHostAddresses($Host) | ForEach-Object { $_.ToString() }
        Write-Info ("Resolves to: " + ($ips -join ', '))
        $publicIps = $ips | Where-Object {
            $_ -notmatch '^(10\.|172\.(1[6-9]|2[0-9]|3[01])\.|192\.168\.|127\.|::1)'
        }
        if ($publicIps) {
            Write-Fail 'Confirmed active: domain resolves to a public IP address.'
            Write-Fail ("  Public IP(s): " + ($publicIps -join ', '))
        } else {
            Write-Warn 'Resolves to a private/internal IP -- unusual for a cloud KMS domain.'
        }
    } catch {
        Write-Warn ("Cannot resolve host '" + $Host + "' -- service may be offline or DNS-blocked.")
    }
}

function Mask-Key {
    param([string]$key)
    if ($key.Length -lt 5) { return $key }
    return "XXXXX-XXXXX-XXXXX-XXXXX-" + $key.Substring($key.Length - 5)
}

function Ask-YesNo {
    param([string]$prompt)
    $ans = Read-Host "$prompt (y/n)"
    return ($ans -match '^y(es)?$')
}

function Run-Slmgr {
    param([string]$arg, [string]$label = '')
    if (-not (Test-Path $slmgrPath)) {
        Write-Fail "slmgr.vbs not found at $slmgrPath"
        return $null
    }
    if ($label) { Write-Step $label }
    Write-Cmd "cscript //nologo `"$slmgrPath`" $arg"
    $out = cscript //nologo $slmgrPath $arg 2>&1
    if (-not $out) {
        Write-Warn "No output from slmgr $arg"
        return $null
    }
    return $out
}

function Elevate-For-Option {
    Write-Blank
    Write-Warn "This option requires Administrator privileges."
    Write-Info "Relaunch WinLic Manager as Administrator now?"
    Write-Info "Note: The script will restart in a new elevated window."
    Write-Blank
    if (Ask-YesNo "Relaunch as Administrator") {
        try {
            Start-Process powershell.exe `
                -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" `
                -Verb RunAs
            Exit
        } catch {
            Write-Fail "Elevation failed: $_"
            Write-Fail "Please right-click PowerShell and choose 'Run as Administrator'."
        }
    } else {
        Write-Warn "Canceled. Returning to menu."
    }
}

# ---- UI ---------------------------------------------------------------------
function Show-Header {
    Clear-Host
    Write-Sep2
    Write-Host ("  WinLic Manager  {0,-18}  Windows 10 / 11" -f $SCRIPT_VERSION) -ForegroundColor Magenta
    Write-Sep2
    if ($isAdmin) {
        Write-Host "  STATUS  " -NoNewline
        Write-Host "[ADMIN] Running as Administrator -- all options available" -ForegroundColor Green
    } else {
        Write-Host "  STATUS  " -NoNewline
        Write-Host "[NO ADMIN] Options 1,2,3,7 available -- 4,5,6 will prompt for elevation" -ForegroundColor Yellow
    }
    Write-Sep
}

function Show-Menu {
    Write-Blank
    Write-Host "  1 -- OS Version & OEM BIOS Key"               -ForegroundColor White
    Write-Host "  2 -- License Channel & Type  (slmgr /dli)"    -ForegroundColor White
    Write-Host "  3 -- Inspect Keys & Activation Status"         -ForegroundColor White
    Write-Host "  4 -- Test & Install Product Key         [!]"   -ForegroundColor Red
    Write-Host "  5 -- Uninstall License Key              [!]"   -ForegroundColor Red
    Write-Host "  6 -- Reset Activation (Rearm)           [!]"   -ForegroundColor Red
    Write-Host "  7 -- 3rd Party Activation Audit"               -ForegroundColor White
    Write-Host "  U -- Update Scan Defaults from GitHub"          -ForegroundColor Cyan
    Write-Host "  Q -- Quit"                                     -ForegroundColor DarkGray
    Write-Blank
    Write-Host "  [!] These options make REAL changes to your Windows license." -ForegroundColor DarkRed
    Write-Sep
}

function Show-About {
    Write-Blank
    Write-Host "  ABOUT" -ForegroundColor White
    Write-Host "  WinLic Manager $SCRIPT_VERSION -- Windows Licensing & Information Manager" -ForegroundColor DarkCyan
    Write-Host "  Author : Arden Nguyen Duc Huy" -ForegroundColor DarkCyan
    Write-Host "  Repo   : https://github.com/ardennguyen/WinLic" -ForegroundColor DarkCyan
    Write-Blank
    Write-Host "  Options 1, 2, 3, 7 are read-only and safe for any user." -ForegroundColor Gray
    Write-Host "  Options 4, 5, 6 make REAL changes to your Windows license" -ForegroundColor DarkRed
    Write-Host "  and require Administrator privileges.  Use with care." -ForegroundColor DarkRed
    Write-Sep
}

# =============================================================================
# OPTION 1 -- OS Version & OEM BIOS Key
# =============================================================================
function Get-VersionInfo {
    Write-Blank
    Write-Host "  OPTION 1 -- OS Version & OEM BIOS Key" -ForegroundColor Magenta
    Write-Sep
    Write-Info "Reads Windows version from WMI and the OEM product key embedded"
    Write-Info "in your BIOS/UEFI firmware (OA3 -- OEM Activation 3)."
    Write-Sep
    Write-Blank

    Write-Step "Querying OS information (Win32_OperatingSystem)..."
    Write-Cmd  "Get-CimInstance Win32_OperatingSystem"
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        Write-Blank
        Write-Data "OS Edition:"   $os.Caption
        Write-Data "Version:"      $os.Version
        Write-Data "Build:"        $os.BuildNumber
        Write-Data "Architecture:" $os.OSArchitecture
    } catch {
        Write-Fail "Failed to query WMI: $_"
        Write-Warn "Falling back to winver dialog..."
        Start-Process "winver"
        return
    }

    Write-Blank
    Write-Sep

    Write-Step "Checking BIOS/UEFI OEM key (SoftwareLicensingService.OA3xOriginalProductKey)..."
    Write-Cmd  "Get-CimInstance SoftwareLicensingService | Select OA3xOriginalProductKey"
    $oemKey = (Get-CimInstance -ClassName SoftwareLicensingService).OA3xOriginalProductKey
    Write-Blank

    if ($oemKey) {
        Write-OK "BIOS OEM key detected."
        $display = if (Ask-YesNo "Show full BIOS OEM key?") { $oemKey } else { Mask-Key $oemKey }
        Write-Data "BIOS OEM Key:" $display Cyan
        $last5 = $oemKey.Substring($oemKey.Length - 5)
        if ($genericKeys.ContainsKey($last5)) {
            Write-Blank
            Write-OK "Edition match: $($genericKeys[$last5])"
        }
    } else {
        Write-Warn "No OEM key detected in BIOS/UEFI firmware."
        Write-Info "This is normal on custom-built PCs or systems without OEM pre-activation."
    }
    Write-Blank
}

# =============================================================================
# OPTION 2 -- License Channel & Type  (slmgr /dli)
# =============================================================================
function Get-LicenseInfo {
    Write-Blank
    Write-Host "  OPTION 2 -- License Channel & Type  (slmgr /dli)" -ForegroundColor Magenta
    Write-Sep
    Write-Info "Runs 'slmgr /dli' -- the Software Licensing Manager display-info command."
    Write-Info "Shows the license name, channel (Retail/OEM/Volume), and license status."
    Write-Info "Note: slmgr /dli only reveals the last 5 characters of the product key."
    Write-Sep
    Write-Blank

    $out = Run-Slmgr "/dli" "Running slmgr /dli..."
    if ($out) {
        Write-Blank
        foreach ($line in $out) {
            $t = $line.Trim()
            if (-not $t) { continue }
            if ($t -match '^License Status:') {
                Write-Host ("  {0}" -f $t) -ForegroundColor Cyan
            } elseif ($t -match '^Name:|^Description:') {
                Write-Host ("  {0}" -f $t) -ForegroundColor Green
            } else {
                Write-Host ("  {0}" -f $t)
            }
        }
    }
    Write-Blank
}

# =============================================================================
# OPTION 3 -- Inspect Keys & Activation Status
# =============================================================================
function Inspect-KeysAndLicenses {
    Write-Blank
    Write-Host "  OPTION 3 -- Inspect Keys & Activation Status" -ForegroundColor Magenta
    Write-Sep
    Write-Info "Queries the active license via WMI, BIOS firmware, and the Registry backup key."
    Write-Info "Detects Digital Entitlement, KMS, and Retail/MAK activation methods."
    Write-Sep
    Write-Blank

    $regKeyPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform"

    # 1. Active license (WMI) -------------------------------------------------
    Write-Step "Querying active Windows license (WMI SoftwareLicensingProduct)..."
    Write-Cmd  "Get-CimInstance SoftwareLicensingProduct | Where-Object { PartialProductKey -and Name -like 'Windows*' }"
    Write-Blank

    $activeProduct = Get-CimInstance -ClassName SoftwareLicensingProduct |
                     Where-Object { $_.PartialProductKey -and $_.Name -like "Windows*" }
    $partialKey = $null

    if ($activeProduct) {
        $partialKey = $activeProduct.PartialProductKey
        try {
            Write-Data "OS Edition:"     (Get-CimInstance Win32_OperatingSystem).Caption
        } catch {}
        Write-Data "License Name:"       $activeProduct.Name
        Write-Data "License Channel:"    $activeProduct.Description
        Write-Data "Active Partial Key:" $partialKey Cyan

        $statusText = switch ($activeProduct.LicenseStatus) {
            0 { "Unlicensed" }
            1 { "Licensed (Permanently Activated)" }
            2 { "OOB Grace Period" }
            3 { "OOT Grace Period" }
            4 { "Non-Genuine Grace Period" }
            5 { "Notification Mode" }
            6 { "Extended Grace Period" }
            default { "Unknown" }
        }
        $statusColor = if ($activeProduct.LicenseStatus -eq 1) { 'Green' } else { 'Red' }
        Write-Data "Activation:" $statusText $statusColor

        Write-Blank
        if ($genericKeys.ContainsKey($partialKey)) {
            if ($activeProduct.LicenseStatus -eq 1) {
                Write-OK  "Activation method:  Digital Entitlement (hardware-bound license)"
                Write-Info "Windows is licensed via a Digital Entitlement stored on Microsoft's servers."
                Write-Info "This license is bound to your hardware (motherboard fingerprint)"
                Write-Info "and/or your Microsoft Account."
                Write-Info "The active key above is a generic placeholder -- activation lives in the cloud."
                Write-Info "To verify: Settings -> System -> Activation -> look for 'Digital License'"
            } else {
                Write-Warn "Digital Entitlement placeholder key detected -- but status is NOT Licensed."
            }
        } elseif ($activeProduct.Description -match "KMS") {
            Write-OK  "Activation method:  KMS (Volume / Corporate License)"
            $kmsHost = $activeProduct.KeyManagementServiceMachine
            if ($kmsHost) { Write-Data "KMS Server:" $kmsHost Cyan }
        } else {
            Write-OK  "Activation method:  MAK / Retail / OEM key (standard activation)"
        }
    } else {
        Write-Warn "No active Windows product license found via WMI."
    }

    Write-Blank
    Write-Sep

    # 2. BIOS OEM key ---------------------------------------------------------
    Write-Step "Checking BIOS/UEFI firmware OEM key..."
    Write-Cmd  "Get-CimInstance SoftwareLicensingService | Select OA3xOriginalProductKey"
    $oemKey = (Get-CimInstance -ClassName SoftwareLicensingService).OA3xOriginalProductKey
    if ($oemKey) { Write-OK   "BIOS OEM Key: Detected" } else { Write-Warn "BIOS OEM Key: None detected" }

    # 3. Registry Backup Key --------------------------------------------------
    Write-Step "Reading Registry Backup Key (BackupProductKeyDefault)..."
    Write-Cmd  "Get-ItemProperty 'HKLM:\...\SoftwareProtectionPlatform' -Name BackupProductKeyDefault"
    $regKey = (Get-ItemProperty -Path $regKeyPath -Name "BackupProductKeyDefault").BackupProductKeyDefault
    if ($regKey) { Write-OK   "Registry Backup Key (BackupProductKeyDefault): Detected" } `
    else         { Write-Warn "Registry Backup Key (BackupProductKeyDefault): None found" }

    # Display keys (optional) -------------------------------------------------
    if ($oemKey -or $regKey) {
        Write-Blank
        $showFull = Ask-YesNo "Show full product key(s)? No = last 5 chars only"
        Write-Blank
        Write-Host "  -- KEY DETAILS --" -ForegroundColor Cyan
        if ($oemKey) {
            $val = if ($showFull) { $oemKey } else { Mask-Key $oemKey }
            Write-Data "BIOS OEM Key:" $val Cyan
        }
        if ($regKey) {
            $val = if ($showFull) { $regKey } else { Mask-Key $regKey }
            Write-Data "Registry Backup Key (BackupProductKeyDefault):" $val Cyan
        }
    }

    Write-Blank
    Write-Sep

    # 4. Compare backup vs active ---------------------------------------------
    if ($regKey -and $partialKey) {
        if (-not $regKey.EndsWith($partialKey)) {
            Write-Warn "Registry Backup Key does NOT match the currently active license key."
            Write-Info "Registry path: HKLM\...\SoftwareProtectionPlatform -> BackupProductKeyDefault"
            Write-Info "This stale backup key is a leftover from a previous activation or edition upgrade."
            Write-Data "  Active key ends with:" $partialKey Yellow
            Write-Data "  Backup Key ends with:" $regKey.Substring($regKey.Length - 5) Yellow
            Write-Blank
            Write-Info "Your active Windows activation will NOT be affected by removing the backup key."
            Write-Info "This only removes the stale backup copy stored in the registry."
            Write-Blank

            if ($isAdmin) {
                if (Ask-YesNo "Remove the stale Registry Backup Key?") {
                    Write-Cmd "Remove-ItemProperty -Path '$regKeyPath' -Name 'BackupProductKeyDefault' -Force"
                    try {
                        Remove-ItemProperty -Path $regKeyPath -Name "BackupProductKeyDefault" -Force
                        Write-OK "Registry Backup Key removed successfully."
                    } catch {
                        Write-Fail "Could not remove key: $_"
                    }
                } else {
                    Write-Warn "Registry Backup Key kept."
                }
            } else {
                Write-Warn "Run as Administrator to remove the stale backup key."
            }
        } else {
            Write-OK "Registry Backup Key matches the active product key."
        }
    }

    Write-Blank
    Write-Sep

    # 5. Optional slmgr /dlv --------------------------------------------------
    Write-Blank
    Write-Host "  -- EXTENDED LICENSE REPORT (slmgr /dlv) --" -ForegroundColor DarkCyan
    Write-Info "The /dlv report reveals additional details not shown by /dli:"
    Write-Info "  * License channel & sub-channel (Retail / OEM / Volume)"
    Write-Info "  * Full SKU ID and description"
    Write-Info "  * KMS server configuration & client count"
    Write-Info "  * Remaining rearm (reset) count"
    Write-Info "  * Activation expiry / grace-period deadline"
    Write-Info "  * Unique machine ID (CMID)"
    Write-Blank

    if (Ask-YesNo "Run the full slmgr /dlv extended report?") {
        $out = Run-Slmgr "/dlv" "Running slmgr /dlv..."
        if ($out) {
            Write-Blank
            foreach ($line in $out) {
                $t = $line.Trim()
                if (-not $t) { continue }
                if ($t -match '^License Status:') {
                    Write-Host ("  {0}" -f $t) -ForegroundColor Cyan
                } elseif ($t -match '^Name:|^Description:|^SKU ID:') {
                    Write-Host ("  {0}" -f $t) -ForegroundColor Green
                } else {
                    Write-Host ("  {0}" -f $t)
                }
            }
        }
    }
    Write-Blank
}

# =============================================================================
# OPTION 4 -- Test & Install Product Key
# =============================================================================
function Test-ProductKey {
    if (-not $isAdmin) { Elevate-For-Option; return }

    Write-Blank
    Write-Host "  OPTION 4 -- Test & Install Product Key" -ForegroundColor Magenta
    Write-Sep
    Write-Info "Tests a product key by attempting local installation via slmgr /ipk."
    Write-Info "If the key matches the installed edition and is valid, it will be accepted."
    Write-Info "If rejected (SKU mismatch / invalid / blocked), an error code is returned."
    Write-Sep
    Write-Blank

    # Show current active key for reference
    Write-Step "Reading current active license..."
    $activeProduct = Get-CimInstance -ClassName SoftwareLicensingProduct |
                     Where-Object { $_.PartialProductKey -and $_.Name -like "Windows*" }
    if ($activeProduct) {
        Write-Data "  Current edition:"     $activeProduct.Name
        Write-Data "  Current partial key:" $activeProduct.PartialProductKey Cyan
        $statusText = switch ($activeProduct.LicenseStatus) {
            1 { "Licensed (Activated)" } 0 { "Unlicensed" } default { "Other ($($activeProduct.LicenseStatus))" }
        }
        $statusColor = if ($activeProduct.LicenseStatus -eq 1) { 'Green' } else { 'Yellow' }
        Write-Data "  Current status:"     $statusText $statusColor
    } else {
        Write-Warn "  No active license detected."
    }
    Write-Blank
    Write-Sep
    Write-Blank

    $rawKey = Read-Host "  Enter the new 25-character product key (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX)"
    $key    = $rawKey.Trim().ToUpper()

    if ($key -notmatch '^[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}$') {
        Write-Fail "Invalid format. Key must be XXXXX-XXXXX-XXXXX-XXXXX-XXXXX"
        return
    }

    $displayKey = if (Ask-YesNo "Show full key in the command log?") { $key } else { Mask-Key $key }

    # Confirmation before installing
    Write-Blank
    Write-Sep
    Write-Warn "CONFIRM INSTALLATION"
    Write-Host "  New key  : $displayKey" -ForegroundColor Cyan
    if ($activeProduct) {
        Write-Host ("  Replaces : ...{0}  ({1})" -f $activeProduct.PartialProductKey, $activeProduct.Name) -ForegroundColor DarkYellow
    }
    Write-Warn "If accepted, this key will REPLACE your current product key immediately."
    Write-Sep
    Write-Blank

    if (-not (Ask-YesNo "Install this key now?")) {
        Write-Warn "Canceled -- no changes made."
        return
    }

    Write-Blank
    Write-Step "Installing product key..."
    Write-Cmd  "cscript //nologo `"$slmgrPath`" /ipk $displayKey"
    Write-Blank

    $out     = cscript //nologo $slmgrPath /ipk $key 2>&1
    $fullOut = [string]::Join(" ", $out)

    foreach ($line in $out) { Write-Host ("  {0}" -f $line.Trim()) }

    Write-Blank
    Write-Sep

    if ($fullOut -match 'Error:' -or $fullOut -match '0x') {
        Write-Fail "Key was rejected by Windows."
        if    ($fullOut -match '0xC004F069') {
            Write-Warn "Diagnosis: SKU Mismatch (0xC004F069) -- key belongs to a different edition."
        } elseif ($fullOut -match '0xC004F050') {
            Write-Warn "Diagnosis: Invalid Key (0xC004F050) -- key is invalid or mistyped."
        } elseif ($fullOut -match '0xC004C003') {
            Write-Warn "Diagnosis: Blocked Key (0xC004C003) -- key has been blacklisted by Microsoft."
        } else {
            Write-Warn "Diagnosis: Installation failed -- check the error code."
        }
        Write-Blank
        Write-Info "Reference: https://learn.microsoft.com/windows-server/get-started/activation-error-codes"
    } else {
        Write-OK  "Key accepted! Windows will attempt online activation automatically."
        Write-Info "Check activation status with Option 2 or Option 3."
    }
    Write-Blank
}

# =============================================================================
# OPTION 5 -- Uninstall License Key
# =============================================================================
function Remove-License {
    if (-not $isAdmin) { Elevate-For-Option; return }

    Write-Blank
    Write-Host "  OPTION 5 -- Uninstall License Key" -ForegroundColor Magenta
    Write-Sep
    Write-Warn "WARNING -- This will uninstall the current Windows product key AND clear"
    Write-Warn "           it from the registry. Windows will become UNACTIVATED."
    Write-Sep
    Write-Blank

    if (-not (Ask-YesNo "Are you sure you want to proceed?")) {
        Write-Warn "Canceled."
        return
    }

    Write-Blank
    $out = Run-Slmgr "/upk" "Uninstalling product key (slmgr /upk)..."
    if ($out) { foreach ($l in $out) { Write-Host ("  {0}" -f $l.Trim()) } }

    Write-Blank
    $out = Run-Slmgr "/cpky" "Clearing key from registry (slmgr /cpky)..."
    if ($out) { foreach ($l in $out) { Write-Host ("  {0}" -f $l.Trim()) } }

    Write-Blank
    Write-OK "Product key uninstalled and registry cleared."
    Write-Blank
}

# =============================================================================
# OPTION 6 -- Reset Activation (Rearm)
# =============================================================================
function Reset-Activation {
    if (-not $isAdmin) { Elevate-For-Option; return }

    Write-Blank
    Write-Host "  OPTION 6 -- Reset Activation (Rearm)" -ForegroundColor Magenta
    Write-Sep
    Write-Warn "WARNING -- This resets the licensing status and activation timers."
    Write-Warn "           A computer restart is required for changes to take effect."
    Write-Sep
    Write-Blank

    if (-not (Ask-YesNo "Are you sure you want to proceed?")) {
        Write-Warn "Canceled."
        return
    }

    Write-Blank
    $out = Run-Slmgr "/rearm" "Executing slmgr /rearm..."
    if ($out) { foreach ($l in $out) { Write-Host ("  {0}" -f $l.Trim()) } }

    Write-Blank
    Write-OK "Rearm complete. Restart your computer for changes to take effect."
    Write-Blank

    if (Ask-YesNo "Restart the computer now?") {
        Write-Warn "Restarting in 5 seconds..."
        Start-Sleep 5
        Restart-Computer -Force
    }
    Write-Blank
}

# =============================================================================
# OPTION 7 -- 3rd Party Activation Audit
# =============================================================================
function Invoke-ActivationAudit {
    Write-Blank
    Write-Host "  OPTION 7 -- 3rd Party Activation Audit" -ForegroundColor Magenta
    Write-Sep

    # ---- Settings & configuration summary -----------------------------------
    :settingsLoop while ($true) {
        $lists = Get-ScanLists

        $iniStatus  = if (Test-Path $SETTINGS_FILE) { "[loaded]" } else { "[not found -- using built-in defaults]" }
        $iniColor   = if (Test-Path $SETTINGS_FILE) { 'Green'    } else { 'DarkYellow' }

        $extraPortCount   = ($lists.Ports     | Where-Object { $_ -notin $DEFAULT_PORTS    }).Count
        $extraSvcCount    = ($lists.Services  | Where-Object { $_ -notin $DEFAULT_SERVICES }).Count
        $extraTaskCount   = ($lists.Tasks     | Where-Object { $_ -notin $DEFAULT_TASKS    }).Count
        $extraProcCount   = ($lists.Processes | Where-Object { $_ -notin $DEFAULT_PROCS    }).Count
        $extraFileCount   = @($lists.ExtraFiles).Count
        $extraDomainCount = ($lists.KmsDomains | Where-Object { $_ -notin $DEFAULT_KMS_DOMAINS }).Count

        Write-Host "  SCAN CONFIGURATION" -ForegroundColor White
        Write-Host ("  Settings file:   {0}  " -f $SETTINGS_FILE) -NoNewline
        Write-Host $iniStatus -ForegroundColor $iniColor
        Write-Blank
        Write-Host ("  {0,-18} {1}" -f "Ports to probe:", ($lists.Ports -join ", ")) -ForegroundColor $(if ($extraPortCount) { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} built-in{2}" -f "Services:",   $DEFAULT_SERVICES.Count,    $(if ($extraSvcCount)    { "  + $extraSvcCount custom" }    else { "" })) -ForegroundColor $(if ($extraSvcCount)    { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} built-in{2}" -f "Tasks:",      $DEFAULT_TASKS.Count,       $(if ($extraTaskCount)   { "  + $extraTaskCount custom" }   else { "" })) -ForegroundColor $(if ($extraTaskCount)   { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} built-in{2}" -f "Processes:",  $DEFAULT_PROCS.Count,       $(if ($extraProcCount)   { "  + $extraProcCount custom" }   else { "" })) -ForegroundColor $(if ($extraProcCount)   { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1}" -f "Extra file paths:", $(if ($extraFileCount) { "$extraFileCount custom" } else { "(none)" })) -ForegroundColor $(if ($extraFileCount) { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} built-in{2}" -f "KMS piracy domains:", $DEFAULT_KMS_DOMAINS.Count, $(if ($extraDomainCount) { "  + $extraDomainCount custom" } else { "" })) -ForegroundColor $(if ($extraDomainCount) { 'Cyan' } else { 'Gray' })
        Write-Blank
        Write-Sep

        Write-Blank
        Write-Host "  Edit settings.ini to add custom ports, services, tasks, processes, or file" -ForegroundColor DarkCyan
        Write-Host "  paths before scanning.  The file is at:" -ForegroundColor DarkCyan
        Write-Host "    $SETTINGS_FILE" -ForegroundColor DarkGray
        Write-Blank

        $editChoice = Read-Host "  Edit settings.ini before scanning? (y/n/skip to proceed)"
        if ($editChoice -match '^y(es)?$') {
            if (Test-Path $SETTINGS_FILE) {
                Write-Info "Opening settings.ini in Notepad -- close the window to continue..."
                Start-Process notepad.exe -ArgumentList $SETTINGS_FILE -Wait
                Write-Step "Reloading scan configuration..."
                Write-Blank
                # Loop back to show updated configuration
            } else {
                Write-Warn "settings.ini not found at: $SETTINGS_FILE"
                Write-Info "The file should be in the same folder as WinLicManager.ps1."
                Write-Info "If you cloned the repo, ensure WinLicPS\settings.ini exists."
                Write-Blank
                Read-Host "  Press Enter to continue with built-in defaults..."
                break settingsLoop
            }
        } else {
            break settingsLoop
        }
    }

    Write-Blank
    Write-Sep

    # Preamble ----------------------------------------------------------------
    Write-Host "  WHAT THIS SCAN CHECKS" -ForegroundColor White
    Write-Info "(1) KMS server name / registry  ->  detects LOCAL emulators (KMSpico, vlmcsd) AND CLOUD piracy KMS services"
    Write-Info "(2) Localhost port probe         ->  confirms a live local KMS listener is running"
    Write-Info "(3) System services              ->  residual activation services that survive reboots"
    Write-Info "(4) Scheduled tasks              ->  periodic re-activation tasks (AutoKMS, KMSAuto, Activation-Renewal...)"
    Write-Info "(5) File / folder paths          ->  installation leftovers + KMS38 GenuineTicket + MAS renewal artifacts"
    Write-Info "(6) Running processes            ->  active activation tool processes at scan time"
    Write-Info "(7) GVLK key + permanent act.    ->  detects TSforge / KMS38 / HWID piracy patterns"
    Write-Info "(8) Activation expiry anomaly    ->  year 2038 (KMS38), 2100+ (TSforge KMS4k), ~180d (Online KMS)"
    Write-Info "(9) SPP store timestamp          ->  LOW CONFIDENCE indicator of TSforge data.dat modification"
    Write-Blank
    Write-Host "  KNOWN LIMITATIONS -- CANNOT DETECT" -ForegroundColor DarkYellow
    Write-Info "* HWID tools (MAS HWID, HWIDGEN): create a genuine Digital Entitlement via MS API"
    Write-Info "* Cleaned activations: if tool was fully removed, all traces are gone"
    Write-Info "* Legacy SLIC/OEM BIOS patching (Windows Loader era): modifies firmware tables"
    Write-Info "* KMS38 (cleaned): hard to distinguish from legit corporate KMS unless still on localhost"
    Write-Sep
    Write-Blank

    $suspiciousCount = 0
    $criticalKms     = $false

    # (1) KMS server name -----------------------------------------------------
    Write-Step "(1) Checking KMS server name (registry + WMI)..."
    Write-Info "KMS piracy operates in two forms:"
    Write-Info "  LOCAL EMULATOR  -- fake KMS server on 127.x.x.x (KMSpico, vlmcsd, KMSAuto)"
    Write-Info "  CLOUD SERVICE   -- third-party internet KMS host (e.g. km8.msguides.com)"
    Write-Info "Microsoft does NOT provide public KMS servers. Any cloud KMS is a piracy service."
    Write-Cmd  "Get-CimInstance SoftwareLicensingProduct | Select KeyManagementServiceMachine"
    Write-Blank

    $kmsHost = $null
    try {
        $kmsHost = (Get-CimInstance -ClassName SoftwareLicensingProduct |
                    Where-Object { $_.PartialProductKey -and $_.Name -like "Windows*" }
                   ).KeyManagementServiceMachine
        if (-not $kmsHost) {
            $kmsHost = (Get-ItemProperty `
                "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform" `
                -Name "KeyManagementServiceName" -ErrorAction SilentlyContinue).KeyManagementServiceName
        }
        if (-not $kmsHost) {
            # WoW64 fallback (32-bit view on 64-bit OS)
            $kmsHost = (Get-ItemProperty `
                "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform" `
                -Name "KeyManagementServiceName" -ErrorAction SilentlyContinue).KeyManagementServiceName
        }
    } catch {}

    if ($kmsHost) {
        Write-Data "KMS server configured:" $kmsHost

        # -- Classify the configured KMS host --
        $isLocal       = $kmsHost -match '^(127\.|localhost|::1|0\.0\.0\.0)'
        $isBogusPlaceholder = $kmsHost -eq '10.0.0.10'
        $isMsOfficial  = $kmsHost -match '\.(microsoft\.com|windows\.net)$'
        $isPrivateIp   = $kmsHost -match '^(10\.|172\.(1[6-9]|2[0-9]|3[01])\.|192\.168\.)'
        $isPrivateHost = (-not $isPrivateIp) -and (
                             $kmsHost -match '\.(local|internal|corp|lan|intranet|home)$' -or
                             $kmsHost -notmatch '\.'
                         )
        # Use the merged list from settings.ini + built-in defaults
        $isKnownPiracy = $lists.KmsDomains | Where-Object { $kmsHost -match [regex]::Escape($_) }

        if ($isLocal) {
            Write-Fail "LOCAL KMS EMULATOR -- KMS server points to localhost/127.x.x.x!"
            Write-Fail "A fake KMS server (KMSpico, vlmcsd, KMSAuto) is running locally."
            $criticalKms = $true
            $suspiciousCount++
        } elseif ($isBogusPlaceholder) {
            Write-Fail "BOGUS KMS PLACEHOLDER DETECTED -- KMS server is 10.0.0.10"
            Write-Fail "This is a non-routable IP used by MAS Online KMS (no renewal task installed)."
            Write-Fail "It prevents Office activation banners but does NOT legitimately activate Windows."
            $criticalKms = $true
            $suspiciousCount++
        } elseif ($isKnownPiracy) {
            Write-Fail "KNOWN PIRACY KMS DOMAIN DETECTED!"
            Write-Fail "  Server : $kmsHost"
            Write-Fail "  This domain is a recognized third-party activation service."
            Write-Fail "  It is NOT operated by Microsoft."
            $criticalKms = $true
            $suspiciousCount++
            Resolve-KmsHost -Host $kmsHost
        } elseif ($isMsOfficial) {
            Write-OK  "Microsoft Azure KMS endpoint -- legitimate Microsoft-operated server."
            Write-Warn "Note: Azure KMS is only valid inside Microsoft Azure virtual machines."
            Write-Warn "If this is NOT an Azure VM, this configuration is unusual."
            Resolve-KmsHost -Host $kmsHost
        } elseif ($isPrivateIp -or $isPrivateHost) {
            Write-OK  "KMS server is on a private/internal network address."
            Write-Info "Consistent with a legitimate corporate deployment."
        } else {
            Write-Fail "CLOUD KMS PIRACY SERVICE DETECTED!"
            Write-Fail "  Server : $kmsHost"
            Write-Fail "  This is a public internet KMS host -- NOT a Microsoft service."
            Write-Warn "  Microsoft does NOT provide public KMS servers."
            Write-Warn "  This is almost certainly an unauthorized third-party activation service."
            $criticalKms = $true
            $suspiciousCount++
            Resolve-KmsHost -Host $kmsHost
        }
    } else {
        Write-OK "No KMS server configured."
    }

    # 1f. Check Office KMS registry
    $officeKmsHost = $null
    try {
        $officeKmsHost = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\OfficeSoftwareProtectionPlatform' `
            -Name 'KeyManagementServiceName' -ErrorAction SilentlyContinue).KeyManagementServiceName
    } catch {}
    if ($officeKmsHost) {
        $officeIsKnownPiracy = $lists.KmsDomains | Where-Object { $officeKmsHost -match [regex]::Escape($_) }
        $officeIsExternal    = $officeKmsHost -notmatch '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.|127\.|localhost)'
        if ($officeIsKnownPiracy -or $officeIsExternal) {
            Write-Warn "Office KMS server configured: $officeKmsHost (suspicious -- piracy domain or external host)"
            $suspiciousCount++
        }
    }

    Write-Blank
    Write-Sep

    # (2) Port probe ----------------------------------------------------------
    Write-Step "(2) Probing configured KMS port(s) on localhost..."
    Write-Info "An open port means a local KMS emulator is actively listening --"
    Write-Info "the strongest single indicator of local KMS-based piracy."
    Write-Blank

    foreach ($port in $lists.Ports) {
        $open = $false
        try {
            $tcp  = New-Object System.Net.Sockets.TcpClient
            $ar   = $tcp.BeginConnect("127.0.0.1", $port, $null, $null)
            $open = $ar.AsyncWaitHandle.WaitOne(800) -and $tcp.Connected
            try { $tcp.EndConnect($ar) } catch {}
            $tcp.Close()
        } catch {}

        if ($open) {
            Write-Fail "Port $port OPEN on localhost -- a local KMS listener is actively running!"
            $criticalKms = $true
            $suspiciousCount++
        } else {
            Write-OK  "Port $port on localhost is closed (no local KMS listener)."
        }
    }

    Write-Blank
    Write-Sep

    # (3) System services -----------------------------------------------------
    Write-Step "(3) Scanning system services..."
    Write-Info "KMS emulators often install as Windows services to survive reboots."
    Write-Blank

    $foundServices = @()
    try {
        $allSvc = Get-CimInstance -ClassName Win32_Service
        foreach ($svcName in $lists.Services) {
            $match = $allSvc | Where-Object {
                $_.Name        -like "*$svcName*" -or
                $_.DisplayName -like "*$svcName*"
            }
            foreach ($s in $match) {
                $foundServices += "$($s.Name)  ($($s.DisplayName))  [$($s.State)]"
            }
        }
    } catch { Write-Warn "Service scan error: $_" }

    $foundServices = $foundServices | Sort-Object -Unique
    if ($foundServices) {
        foreach ($s in $foundServices) { Write-Fail "Suspicious service detected:  $s"; $suspiciousCount++ }
    } else {
        Write-OK "No suspicious services found."
    }

    Write-Blank
    Write-Sep

    # (4) Scheduled tasks -----------------------------------------------------
    Write-Step "(4) Scanning scheduled tasks..."
    Write-Info "Activation tools like KMSpico create scheduled tasks (e.g. 'AutoKMS')"
    Write-Info "that re-activate Windows periodically to prevent grace-period expiry."
    Write-Blank

    $foundTasks = @()
    try {
        $allTasks = Get-ScheduledTask
        foreach ($kw in $lists.Tasks) {
            $match = $allTasks | Where-Object { $_.TaskName -like "*$kw*" }
            foreach ($t in $match) {
                $foundTasks += "$($t.TaskName)  [$($t.State)]  ($($t.TaskPath))"
            }
        }
    } catch { Write-Warn "Task scan error: $_" }

    $foundTasks = $foundTasks | Sort-Object -Unique
    if ($foundTasks) {
        foreach ($t in $foundTasks) { Write-Fail "Suspicious scheduled task:  $t"; $suspiciousCount++ }
    } else {
        Write-OK "No suspicious scheduled tasks found."
    }

    Write-Blank
    Write-Sep

    # (5) Known file / folder paths -------------------------------------------
    Write-Step "(5) Scanning known tool file / folder paths..."
    Write-Info "Known installation folders, executables, and patched system files are checked."
    Write-Blank

    $pf   = $env:ProgramFiles
    $pf86 = ${env:ProgramFiles(x86)}
    $apd  = $env:APPDATA
    $pgd  = $env:ProgramData
    $win  = $env:SystemRoot
    $sys  = Join-Path $win "System32"

    $builtIn = @(
        @{ P = (Join-Path $pf   "KMSpico");              T = "KMSpico" },
        @{ P = (Join-Path $pf86 "KMSpico");              T = "KMSpico" },
        @{ P = (Join-Path $apd  "KMSpico");              T = "KMSpico" },
        @{ P = (Join-Path $pgd  "KMSpico");              T = "KMSpico" },
        @{ P = (Join-Path $sys  "KMSELDI.exe");          T = "KMSpico/ELDI" },
        @{ P = (Join-Path $pf   "KMSAuto Net");          T = "KMSAuto Net" },
        @{ P = (Join-Path $pf86 "KMSAuto Net");          T = "KMSAuto Net" },
        @{ P = (Join-Path $pf   "KMSAuto");              T = "KMSAuto" },
        @{ P = (Join-Path $pf86 "KMSAuto");              T = "KMSAuto" },
        @{ P = (Join-Path $win  "KMS");                  T = "KMS tools folder" },
        @{ P = (Join-Path $sys  "SppExtComObj.exe.bak"); T = "Patched SPP backup" },
        @{ P = (Join-Path $pf   "AAct");                 T = "AAct" },
        @{ P = (Join-Path $pf86 "AAct");                 T = "AAct" },
        # KMS38 / ClipSVC artifact
        @{ P = "C:\ProgramData\Microsoft\Windows\ClipSVC\GenuineTicket\GenuineTicket.xml"; T = "KMS38 GenuineTicket" },
        # MAS Online KMS renewal task artifacts
        @{ P = "C:\Program Files\Activation-Renewal\Activation_task.cmd"; T = "MAS Online KMS renewal task" },
        @{ P = "C:\Program Files\Activation-Renewal\Info.txt";            T = "MAS Online KMS renewal info" }
    )

    $allPaths = $builtIn
    foreach ($ep in $lists.ExtraFiles) {
        $allPaths += @{ P = $ep; T = "[Custom]" }
    }

    $foundFiles = @()
    foreach ($entry in $allPaths) {
        if (Test-Path $entry.P) {
            $foundFiles += "$($entry.T)  ->  $($entry.P)"
        }
    }

    if ($foundFiles) {
        foreach ($f in $foundFiles) { Write-Fail "Known activation tool path found:  $f"; $suspiciousCount++ }
    } else {
        Write-OK "No known activation tool files or folders found."
    }

    Write-Blank
    Write-Sep

    # (6) Running processes ---------------------------------------------------
    Write-Step "(6) Scanning running processes..."
    Write-Info "Some activation tools leave resident processes at scan time."
    Write-Blank

    $foundProcs = @()
    try {
        $allProcs = Get-Process
        foreach ($pn in $lists.Processes) {
            $match = $allProcs | Where-Object { $_.Name -like "*$pn*" }
            foreach ($p in $match) {
                $foundProcs += "$($p.Name)  (PID $($p.Id))"
            }
        }
    } catch { Write-Warn "Process scan error: $_" }

    $foundProcs = $foundProcs | Sort-Object -Unique
    if ($foundProcs) {
        foreach ($p in $foundProcs) { Write-Fail "Suspicious process running:  $p"; $suspiciousCount++ }
    } else {
        Write-OK "No suspicious processes running."
    }

    Write-Blank
    Write-Sep

    # (7) GVLK + Activation Channel check ----------------------------------------
    Write-Step "(7) Checking activation channel and installed key type (WMI)..."
    Write-Info "GVLK (Generic Volume License Keys) combined with permanent activation"
    Write-Info "(no KMS renewal countdown) strongly indicates TSforge, KMS38, or HWID piracy."
    Write-Info "Legitimate enterprise KMS clients always show an active 180-day countdown."
    if ($lists.GvlkSuffixes.Count -gt 0) {
        Write-Info "GVLK suffixes loaded from settings.ini: $($lists.GvlkSuffixes.Count)"
    } else {
        Write-Info "No GVLK keys in settings.ini -- only generic DE placeholder keys checked."
    }
    Write-Blank

    $wmiLicStatus = $null
    try {
        $wmiLicStatus = Get-CimInstance -ClassName Win32_SoftwareLicensingProduct |
            Where-Object { $_.PartialProductKey -and $_.ApplicationID -eq '55c92734-d682-4d71-983e-d6ec3f16059f' } |
            Select-Object -First 1
    } catch {}

    if ($wmiLicStatus) {
        $ppk        = $wmiLicStatus.PartialProductKey
        $licStatus  = $wmiLicStatus.LicenseStatus
        $graceMins  = $wmiLicStatus.GracePeriodRemaining
        $desc       = $wmiLicStatus.Description

        $isLicensed  = $licStatus -eq 1
        $isPermanent = ($graceMins -eq 0 -or $graceMins -eq $null) -and $isLicensed

        # Check GVLK suffix
        $isGvlk     = $false
        if ($lists.GvlkSuffixes.Count -gt 0) {
            $isGvlk = $lists.GvlkSuffixes -contains $ppk.ToUpper()
        }
        # Also check against built-in genericKeys table
        if (-not $isGvlk -and $genericKeys.ContainsKey($ppk)) {
            $isGvlk = $true
        }

        # 7a. Phone activation anomaly (TSforge ZeroCID indicator)
        if ($desc -match 'phone' -and $isLicensed) {
            Write-Fail "ANOMALOUS PHONE ACTIVATION DETECTED!"
            Write-Fail "  Windows reports Phone activation with no record of a phone activation flow."
            Write-Fail "  This is a characteristic indicator of TSforge ZeroCID sub-method."
            $criticalKms = $true
            $suspiciousCount++
        }

        # 7b. GVLK + permanent = likely piracy
        if ($isGvlk -and $isPermanent) {
            Write-Fail "LIKELY PIRACY -- GVLK/placeholder key detected with PERMANENT activation!"
            Write-Fail "  PartialProductKey: $ppk"
            Write-Fail "  Legitimate enterprise KMS clients always have a 180-day renewal countdown."
            Write-Fail "  This pattern matches TSforge, KMS38, or HWID activation via MAS."
            $criticalKms = $true
            $suspiciousCount++
        } elseif ($isGvlk -and -not $isPermanent) {
            Write-OK  "GVLK key detected with active KMS renewal (grace: $graceMins min)."
            Write-Info "Consistent with legitimate enterprise KMS activation. Verify the KMS server."
        } else {
            Write-OK  "Installed key (ends: $ppk) is NOT a known GVLK or placeholder key."
        }
    } else {
        Write-Info "Could not retrieve WMI licensing data for channel check."
    }

    Write-Blank
    Write-Sep

    # (8) Activation expiry analysis ----------------------------------------------
    Write-Step "(8) Analyzing activation expiry date (WMI)..."
    Write-Info "Unusual expiry dates are strong indicators of specific piracy methods:"
    Write-Info "  * Year ~2038 = KMS38 (32-bit timestamp max)"
    Write-Info "  * Year 2100+ = TSforge KMS4k (4000+ year forged KMS lease)"
    Write-Info "  * ~180 days  = Online KMS renewal cycle"
    Write-Blank

    if ($wmiLicStatus) {
        $graceMins2 = $wmiLicStatus.GracePeriodRemaining
        $licStatus2 = $wmiLicStatus.LicenseStatus
        if (($graceMins2 -eq 0 -or $graceMins2 -eq $null) -and $licStatus2 -eq 1) {
            Write-OK "Windows reports permanent activation (no expiry countdown)."
        } elseif ($graceMins2 -gt 0) {
            $expiry = (Get-Date).AddMinutes($graceMins2)
            $exYear = $expiry.Year
            $daysLeft = ($expiry - (Get-Date)).TotalDays
            Write-Data "Expiry date:" ($expiry.ToString('yyyy-MM-dd HH:mm'))
            if ($exYear -ge 2100) {
                Write-Fail "TSFORGE KMS4K DETECTED -- Expiry year $exYear (thousands of years in the future)!"
                Write-Fail "TSforge forges a KMS lease directly into the SPP trusted store."
                $criticalKms = $true
                $suspiciousCount++
            } elseif ($exYear -ge 2037) {
                Write-Fail "KMS38 LEGACY ACTIVATION -- Expiry year $exYear (~2038-01-19 = max 32-bit timestamp)"
                Write-Warn "KMS38 was patched in KB5068861 (Nov 2025) but pre-patched machines may still show this."
                $criticalKms = $true
                $suspiciousCount++
            } elseif ($daysLeft -ge 165 -and $daysLeft -le 195) {
                Write-Warn "ONLINE KMS 180-DAY CYCLE -- Expiry ~$([int]$daysLeft) days, consistent with Online KMS renewal."
                Write-Warn "Combined with an external KMS server above, this strongly indicates MAS Online KMS."
                $suspiciousCount++
            } else {
                Write-OK "Activation expiry date appears normal."
            }
        }
    } else {
        Write-Info "WMI licensing data not available for expiry analysis."
    }

    Write-Blank
    Write-Sep

    # (9) TSforge SPP store file timestamp (LOW CONFIDENCE) ------------------------
    Write-Step "(9) Checking SPP trusted store file timestamp [LOW CONFIDENCE]..."
    Write-Info "TSforge modifies the SPP trusted store (data.dat) directly."
    Write-Info "A LastWriteTime not correlated with any Windows Update event is a LOW CONFIDENCE indicator."
    Write-Info "Windows Update and legitimate troubleshooting can also modify this file."
    Write-Blank

    $datPath = "$env:SystemRoot\System32\spp\store\2.0\data.dat"
    if (Test-Path $datPath) {
        $datMod = (Get-Item $datPath).LastWriteTime
        Write-Data "SPP store LastWriteTime:" ($datMod.ToString('yyyy-MM-dd HH:mm'))

        # Compare against Windows Install Date from registry
        try {
            $installUnix = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' `
                -Name 'InstallDate' -ErrorAction Stop).InstallDate
            $installDate = (Get-Date '1970-01-01').AddSeconds($installUnix).ToLocalTime()
            Write-Data "Windows InstallDate:" ($installDate.ToString('yyyy-MM-dd HH:mm'))
            if ($datMod -lt $installDate) {
                Write-Warn "[LOW CONFIDENCE] SPP store timestamp predates Windows install -- highly anomalous."
                $suspiciousCount++
            }
        } catch {}

        # Check for nearby Windows Update event (Event ID 19)
        $hasNearbyUpdate = $false
        try {
            $events = Get-EventLog -LogName System -Source 'Microsoft-Windows-WindowsUpdateClient' `
                -Newest 200 -ErrorAction SilentlyContinue |
                Where-Object { $_.EventID -eq 19 -and [Math]::Abs(($_.TimeWritten - $datMod).TotalHours) -lt 48 }
            $hasNearbyUpdate = ($events -ne $null -and @($events).Count -gt 0)
        } catch {}
        if ($hasNearbyUpdate) {
            Write-OK "SPP store timestamp correlates with a Windows Update event -- no anomaly."
        } else {
            Write-Warn "[LOW CONFIDENCE] SPP store was modified with no correlated Windows Update event."
            Write-Warn "  This MAY indicate TSforge activation. Use 'slmgr /dlv' for further investigation."
            $suspiciousCount++
        }
    } else {
        Write-Info "SPP store file not found at expected path -- cannot check."
    }

    Write-Blank
    Write-Sep2

    # (10) SPP Security Event Log ---------------------------------------------
    Write-Step "(10) Checking SPP Security activation event log..."
    Write-Info "Queries System event log for Microsoft-Windows-Security-SPP events."
    Write-Info "Event 12290 (KMS request) records the server address -- external = piracy indicator."
    Write-Blank

    try {
        $sppIds  = @(12288, 12289, 12290, 8198)
        $sppEvts = Get-EventLog -LogName System -Newest 500 -ErrorAction SilentlyContinue |
            Where-Object { $sppIds -contains $_.EventID -and
                ($_.Source -like '*Security-SPP*' -or $_.Source -like '*SoftwareProtection*') }

        if (-not $sppEvts -or @($sppEvts).Count -eq 0) {
            Write-OK "No SPP activation security events found in System log."
        } else {
            Write-Data "SPP events found:" "@($sppEvts).Count"
            $extFound = $false
            foreach ($ev in ($sppEvts | Where-Object { $_.EventID -eq 12290 })) {
                # Extract server address from message
                $addrMatch = [regex]::Match($ev.Message,
                    '(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}|[a-zA-Z0-9\-]+\.[a-zA-Z]{2,}(?:\.[a-zA-Z]{2,})?)')
                if ($addrMatch.Success) {
                    $addr = $addrMatch.Value
                    $isPrivate = $addr -match '^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.|127\.|::1|localhost|0\.0\.0\.0)'
                    if (-not $isPrivate) {
                        Write-Fail "SPP event 12290: KMS request to external server: $addr"
                        Write-Fail "  This confirms activation via an unauthorized public KMS service."
                        $criticalKms = $true
                        $extFound    = $true
                        $suspiciousCount++
                    }
                }
            }
            if (-not $extFound) {
                Write-OK "SPP events present -- no external KMS server address found in event data."
            }
        }
    } catch {
        Write-Warn "SPP event log check error: $_"
    }

    Write-Blank
    Write-Sep2

    # Results -----------------------------------------------------------------

    Write-Blank
    Write-Host "  RESULTS" -ForegroundColor White
    Write-Blank

    if ($criticalKms) {
        Write-Fail "CRITICAL: Piracy KMS activation detected (local emulator, bogus IP, or unauthorized cloud service)."
        Write-Fail "          Windows is almost certainly activated by an unauthorized 3rd-party method."
        Write-Fail "          Total indicators flagged: $suspiciousCount"
    } elseif ($suspiciousCount -gt 0) {
        Write-Warn "One or more suspicious indicators found -- review the results above."
        Write-Warn "Total indicators flagged: $suspiciousCount"
    } else {
        Write-OK  "No 3rd-party activation indicators detected -- system appears clean."
    }

    if (Test-Path $SETTINGS_FILE) {
        Write-Blank
        Write-Info "Custom scan lists loaded from: $SETTINGS_FILE"
    }
    Write-Blank

    # Legal notice (always shown)
    Write-Sep
    Write-Host "  ACTIVATION AUDIT -- LEGAL NOTICE" -ForegroundColor Yellow
    Write-Info "Using Windows without a genuine license purchased from Microsoft or an"
    Write-Info "authorized reseller violates Microsoft's Terms of Service (EULA SS4)."
    Write-Info "Enterprise / OEM users: verify licensing with your IT department or OEM."
    Write-Info "Check your genuine license status: https://aka.ms/MyAccount"
    Write-Blank
    Write-Info "SCAN LIMITATIONS: HWID via MAS creates a real MS digital license (undetectable)."
    Write-Info "Any tool removed after use leaves no trace. Corporate KMS may trigger GVLK warnings."
    Write-Blank
}

# =============================================================================
# OPTION U -- Update scan defaults from GitHub
# =============================================================================
function Update-DefaultSettings {
    Write-Blank
    Write-Host "  UPDATE SCAN DEFAULTS" -ForegroundColor Magenta
    Write-Sep
    Write-Info "Downloads the latest settings.default.ini from the WinLic GitHub repository."
    Write-Info "Your user-added entries (ExtraPorts, ExtraServices, etc.) are preserved."
    Write-Info "Source: https://raw.githubusercontent.com/ardennguyen/WinLic/main/WinLicPS/settings.default.ini"
    Write-Sep
    Write-Blank

    # Check internet first
    $online = $false
    try {
        $null = [System.Net.Dns]::GetHostEntry("raw.githubusercontent.com")
        $online = $true
    } catch {}

    if (-not $online) {
        Write-Fail "No internet connection detected -- cannot reach GitHub."
        Write-Fail "Please check your network and try again."
        Write-Blank
        return
    }

    Write-Step "Downloading latest defaults from GitHub..."
    $url      = "https://raw.githubusercontent.com/ardennguyen/WinLic/main/WinLicPS/settings.default.ini"
    $tmpPath  = Join-Path $SCRIPT_DIR "settings.default.ini.tmp"

    try {
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "WinLicManager/1.0")
        $downloaded = $wc.DownloadString($url)
    } catch {
        Write-Fail "Download failed: $_"
        Write-Blank
        return
    }

    # Inject timestamp
    $ts          = (Get-Date -Format "yyyy-MM-dd HH:mm UTC")
    $downloaded  = $downloaded -replace 'Last-Updated:', "Last-Updated: $ts  ;"

    # Preserve user block from existing settings.ini
    $userBlock = ""
    if (Test-Path $SETTINGS_FILE) {
        $existing = Get-Content $SETTINGS_FILE
        $markerIdx = ($existing | Select-String 'USER BLOCK' | Select-Object -First 1).LineNumber
        if ($markerIdx -gt 0) {
            $userBlock = ($existing | Select-Object -Skip ($markerIdx - 1)) -join "`r`n"
        }
    }

    # Build combined file
    $separator = @(
        "",
        "# " + ("=" * 73),
        "# ||  USER BLOCK  --  Edit freely. NEVER overwritten by 'Update defaults'.  ||",
        "# " + ("=" * 73),
        ""
    ) -join "`r`n"

    if ($userBlock) {
        $combined = $downloaded.TrimEnd() + "`r`n`r`n" + $userBlock
    } else {
        $combined = $downloaded.TrimEnd() + "`r`n" + $separator + @"
[UserGvlkKeys]
; Add custom GVLK/suspicious keys here: FULL-KEY = Description

[UserKmsPiracyDomains]
; Add your own KMS piracy hostnames here

[ExtraPorts]
; Additional TCP ports to probe on localhost

[ExtraServices]
; Additional service name keywords

[ExtraTaskKeywords]
; Additional scheduled task keywords

[ExtraProcesses]
; Additional process name keywords

[ExtraFilePaths]
; Additional file paths to check
"@
    }

    try {
        [System.IO.File]::WriteAllText($SETTINGS_FILE, $combined, [System.Text.Encoding]::UTF8)
        Write-OK  "settings.ini updated successfully!"
        Write-Data "File:" $SETTINGS_FILE
        Write-Data "Timestamp:" $ts
    } catch {
        Write-Fail "Could not write settings.ini: $_"
    }

    Write-Blank
}

# =============================================================================
# Main loop
# =============================================================================
$script:firstLoad = $true

do {
    Show-Header
    if ($script:firstLoad) {
        Show-About
        $script:firstLoad = $false
    }
    Show-Menu
    $choice = (Read-Host "  Select option (1-7, U, Q to quit)").Trim().ToUpper()
    Write-Blank

    switch ($choice) {
        "1" { Get-VersionInfo;         Read-Host "  Press Enter to return to menu..." }
        "2" { Get-LicenseInfo;         Read-Host "  Press Enter to return to menu..." }
        "3" { Inspect-KeysAndLicenses; Read-Host "  Press Enter to return to menu..." }
        "4" { Test-ProductKey;         Read-Host "  Press Enter to return to menu..." }
        "5" { Remove-License;          Read-Host "  Press Enter to return to menu..." }
        "6" { Reset-Activation;        Read-Host "  Press Enter to return to menu..." }
        "7" { Invoke-ActivationAudit;    Read-Host "  Press Enter to return to menu..." }
        "U" { Update-DefaultSettings;    Read-Host "  Press Enter to return to menu..." }
        "Q" { Write-OK "Goodbye!"; break }
        default {
            Write-Warn "Invalid option -- choose 1-7, U, or Q."
            Start-Sleep -Seconds 1
        }
    }
} while ($choice -ne "Q")
