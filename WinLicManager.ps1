# =============================================================================
# WinLicManager.ps1  --  Windows Licensing & Information Manager  v1.0 (beta1)
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
$SCRIPT_VERSION = "v1.0 (beta1)"
$SCRIPT_DIR     = Split-Path -Parent $MyInvocation.MyCommand.Path
$SETTINGS_FILE  = Join-Path $SCRIPT_DIR "settings.ini"
$slmgrPath      = Join-Path $env:SystemRoot "System32\slmgr.vbs"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Known generic Digital Entitlement placeholder keys (matched by last 5 chars)
$genericKeys = @{
    "3V66T" = "Windows 10/11 Pro  (VK7JG-NPHTM-C97JM-9MPGT-3V66T)"
    "3TTL4" = "Windows 10/11 Home  (YTMG3-N6KGA-8B33D-XXYF2-3TTL4)"
    "WXCHW" = "Windows 10/11 Home Single Language  (4CPRK-NM3K3-X6XXQ-RXX86-WXCHW)"
    "PR4Y7" = "Windows Pro Education  (8PTT6-RNW4C-X6V77-D23ST-PR4Y7)"
    "2YV77" = "Windows Pro Workstations  (DXG7C-N36C4-C4HTG-X4T3X-2YV77)"
    "8DEC2" = "Windows Enterprise  (XGVPP-NMH47-7TTHJ-W3FW7-8DEC2)"
    "28UTV" = "Windows Enterprise  (NPPR9-FWDCX-D2C8J-H8P65-28UTV)"
    "7CFBY" = "Windows Education  (YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY)"
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
                       'KMSAuto','AutoKMS','KMSSS','KMSEmulator','vlmcsd')
$DEFAULT_TASKS    = @('AutoKMS','KMSAuto','KMS_VL_ALL','KMSpico','KMSSS',
                       'KMSEmulator','KMService','WinKSO','vlmcsd')
$DEFAULT_PROCS    = @('KMSpico','KMSELDI','AutoKMS','KMSAuto','KMSguard',
                       'WinKSO','KMService','vlmcsd','AAct','KMS_VL_ALL')

function Get-ScanLists {
    $extraPorts = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraPorts' |
                  Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ }
    $extraSvc   = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraServices'
    $extraTasks = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraTaskKeywords'
    $extraProcs = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraProcesses'
    $extraFiles = Read-IniSection -Path $SETTINGS_FILE -Section 'ExtraFilePaths'
    return @{
        Ports      = ($DEFAULT_PORTS    + $extraPorts)  | Sort-Object -Unique
        Services   = ($DEFAULT_SERVICES + $extraSvc)    | Sort-Object -Unique
        Tasks      = ($DEFAULT_TASKS    + $extraTasks)  | Sort-Object -Unique
        Processes  = ($DEFAULT_PROCS    + $extraProcs)  | Sort-Object -Unique
        ExtraFiles = $extraFiles
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
    param([string]$label, [string]$value, [ConsoleColor]$color = 'Green')
    Write-Host ("  {0,-30}" -f $label) -NoNewline
    Write-Host $value -ForegroundColor $color
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
        Write-Warn "Cancelled. Returning to menu."
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
    $c = if ($isAdmin) { 'White' } else { 'DarkYellow' }
    Write-Host "  4 -- Test & Install Product Key"               -ForegroundColor $c
    Write-Host "  5 -- Uninstall License Key"                    -ForegroundColor $c
    Write-Host "  6 -- Reset Activation (Rearm)"                 -ForegroundColor $c
    Write-Host "  7 -- 3rd Party Activation Audit"               -ForegroundColor White
    Write-Host "  Q -- Quit"                                     -ForegroundColor DarkGray
    Write-Blank
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
    Write-Info "Your existing activation will NOT be harmed by a rejected key."
    Write-Sep
    Write-Blank

    $rawKey = Read-Host "  Enter the 25-character product key (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX)"
    $key    = $rawKey.Trim().ToUpper()

    if ($key -notmatch '^[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}$') {
        Write-Fail "Invalid format. Key must be XXXXX-XXXXX-XXXXX-XXXXX-XXXXX"
        return
    }

    $displayKey = if (Ask-YesNo "Show full key in the command log?") { $key } else { Mask-Key $key }

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
        Write-Warn "Cancelled."
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
        Write-Warn "Cancelled."
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

    # Preamble ----------------------------------------------------------------
    Write-Host "  WHAT THIS SCAN CHECKS" -ForegroundColor White
    Write-Info "(1) KMS server name / registry  ->  detects local KMS emulators (KMSpico, KMSAuto, vlmcsd...)"
    Write-Info "(2) Localhost port probe         ->  confirms a live local KMS listener is running"
    Write-Info "(3) System services              ->  residual activation services that survive reboots"
    Write-Info "(4) Scheduled tasks              ->  periodic re-activation tasks (AutoKMS, KMSAuto...)"
    Write-Info "(5) File / folder paths          ->  installation leftovers from common tools"
    Write-Info "(6) Running processes            ->  active activation tool processes at scan time"
    Write-Blank
    Write-Host "  KNOWN LIMITATIONS -- CANNOT DETECT" -ForegroundColor DarkYellow
    Write-Info "* HWID tools (MAS HWID, HWIDGEN): create a genuine Digital Entitlement via MS API"
    Write-Info "* Cleaned activations: if tool was fully removed, all traces are gone"
    Write-Info "* Legacy SLIC/OEM BIOS patching (Windows Loader era): modifies firmware tables"
    Write-Info "* KMS38 (cleaned): hard to distinguish from legit corporate KMS unless still on localhost"
    Write-Sep
    Write-Blank

    $lists           = Get-ScanLists
    $suspiciousCount = 0
    $criticalKms     = $false

    # (1) KMS server name -----------------------------------------------------
    Write-Step "(1) Checking KMS server name (registry + WMI)..."
    Write-Info "KMS emulators set the KMS server to 127.x.x.x / localhost to trick Windows"
    Write-Info "into believing it contacted a real corporate KMS server."
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
                -Name "KeyManagementServiceName").KeyManagementServiceName
        }
    } catch {}

    if ($kmsHost) {
        Write-Data "KMS server configured:" $kmsHost
        if ($kmsHost -match '^(127\.|localhost|::1)') {
            Write-Fail "LOCAL KMS EMULATOR -- KMS server is pointing to localhost/127.x.x.x!"
            $criticalKms = $true
            $suspiciousCount++
        } else {
            Write-OK "KMS server is an external address -- consistent with legitimate corporate deployment."
        }
    } else {
        Write-OK "No KMS server configured."
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
        @{ P = (Join-Path $pf86 "AAct");                 T = "AAct" }
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
    Write-Sep2

    # Results -----------------------------------------------------------------
    Write-Blank
    Write-Host "  RESULTS" -ForegroundColor White
    Write-Blank

    if ($criticalKms) {
        Write-Fail "CRITICAL: Local KMS emulator confirmed."
        Write-Fail "          Windows is almost certainly activated by a 3rd-party KMS tool."
    } elseif ($suspiciousCount -gt 0) {
        Write-Warn "One or more suspicious indicators found -- review the results above."
        Write-Warn "Total indicators: $suspiciousCount"
    } else {
        Write-OK  "No 3rd-party activation indicators detected -- system appears clean."
    }

    if (Test-Path $SETTINGS_FILE) {
        Write-Blank
        Write-Info "Custom scan lists loaded from: $SETTINGS_FILE"
    }
    Write-Blank
}

# =============================================================================
# Main loop
# =============================================================================
do {
    Show-Header
    Show-Menu
    $choice = (Read-Host "  Select option (1-7, Q to quit)").Trim().ToUpper()
    Write-Blank

    switch ($choice) {
        "1" { Get-VersionInfo;         Read-Host "  Press Enter to return to menu..." }
        "2" { Get-LicenseInfo;         Read-Host "  Press Enter to return to menu..." }
        "3" { Inspect-KeysAndLicenses; Read-Host "  Press Enter to return to menu..." }
        "4" { Test-ProductKey;         Read-Host "  Press Enter to return to menu..." }
        "5" { Remove-License;          Read-Host "  Press Enter to return to menu..." }
        "6" { Reset-Activation;        Read-Host "  Press Enter to return to menu..." }
        "7" { Invoke-ActivationAudit;  Read-Host "  Press Enter to return to menu..." }
        "Q" { Write-OK "Goodbye!"; break }
        default {
            Write-Warn "Invalid option -- choose 1-7 or Q."
            Start-Sleep -Seconds 1
        }
    }
} while ($choice -ne "Q")
