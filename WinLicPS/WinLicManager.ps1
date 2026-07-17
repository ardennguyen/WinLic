# =============================================================================
# WinLicManager.ps1  --  Windows Licensing & Information Manager  v1.3-beta3
# =============================================================================
# Mirrors the WinLic Manager GUI application for power-user / CLI usage.
#
# Menu:
#   1 -- Full System & License Info
#   2 -- Test & Install New Product Key    [admin required -- prompted on demand]
#   3 -- Remove Activation                [admin required -- prompted on demand]
#   4 -- Reset Activation / Rearm         [admin required -- prompted on demand]
#   5 -- 3rd Party Activation Audit
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

# Ensure UTF-8 output so Vietnamese diacritics render correctly in both PS5 and PS7
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding           = [System.Text.Encoding]::UTF8

# ---- Globals ----------------------------------------------------------------
$SCRIPT_VERSION = "v1.3-beta3"
$SCRIPT_DIR     = Split-Path -Parent $MyInvocation.MyCommand.Path
$SETTINGS_FILE  = Join-Path $SCRIPT_DIR "settings.ini"
$slmgrPath      = Join-Path $env:SystemRoot "System32\slmgr.vbs"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# ---- PowerShell version detection ------------------------------------------
# Prefer pwsh (PS7) over powershell.exe (PS5) for elevation and relaunching.
$isPS7   = $PSVersionTable.PSVersion.Major -ge 7
$_pwshCmd = Get-Command pwsh -ErrorAction SilentlyContinue
if (-not $_pwshCmd) { $_pwshCmd = Get-Command pwsh.exe -ErrorAction SilentlyContinue }
$pwshExe  = if ($_pwshCmd) { $_pwshCmd.Source } else { $null }
$_wtCmd   = Get-Command wt -ErrorAction SilentlyContinue
$wtExe    = if ($_wtCmd)   { $_wtCmd.Source   } else { $null }
# The shell executable to use when relaunching (elevated or PS7)
$shellExe = if ($pwshExe) { $pwshExe } else { 'powershell.exe' }

# ---- Language system ---------------------------------------------------------

$script:Lang = 'EN'   # default; overridden by Select-Language at first launch



# Bilingual string table: key -> @('EN text', 'VI text')

# Vietnamese strings use actual UTF-8 characters embedded directly in this file.

$Str = @{
    # -- Header / status --
    'HDR_ADMIN'    = @('[ADMIN] Running as Administrator -- all options available',
                       '[ADMIN] Đang chạy với quyền Administrator -- tất cả tùy chọn khả dụng')
    'HDR_NOADMIN'  = @('[NO ADMIN] Options 1,5 available -- 2,3,4 will prompt for elevation',
                       '[KHÔNG ADMIN] Tùy chọn 1,5 khả dụng -- 2,3,4 yêu cầu quyền nâng cao')
    # -- About section --
    'ABOUT_HDR'    = @('ABOUT', 'GIỚI THIỆU')
    'ABOUT_OPT1'   = @('Option 1 is read-only and safe for any user.',
                       'Tùy chọn 1 chỉ đọc và an toàn cho mọi người dùng.')
    'ABOUT_OPT234' = @('Options 2, 3, 4 make REAL changes to your Windows license',
                       'Tùy chọn 2, 3, 4 thay đổi THẬT SỰ bản quyền Windows của bạn')
    'ABOUT_ADMIN'  = @('and require Administrator privileges.  Use with care.',
                       'và yêu cầu quyền Administrator.  Sử dụng cẩn thận.')
    # -- Menu --
    'MENU_1'       = @('1 -- Full System & License Info',
                       '1 -- Thông tin Hệ thống & Bản quyền Đầy đủ')
    'MENU_2'       = @('2 -- Test & Install New Product Key    [!]',
                       '2 -- Kiểm thử & Cài Key Mới            [!]')
    'MENU_3'       = @('3 -- Remove Activation                 [!]',
                       '3 -- Gỡ Kích Hoạt                      [!]')
    'MENU_4'       = @('4 -- Reset Activation (Rearm)          [!]',
                       '4 -- Đặt lại Kích Hoạt (Rearm)         [!]')
    'MENU_5'       = @('5 -- 3rd Party Activation Audit',
                       '5 -- Kiểm Tra Kích Hoạt Bên Thứ Ba')
    'MENU_6'       = @('6 -- Change Activation Channel          [!]',
                       '6 -- Thay Đổi Kênh Kích Hoạt            [!]')
    'MENU_7'       = @('7 -- Check & Remove KMS Settings',
                       '7 -- Kiểm Tra & Xóa Cài Đặt KMS')

    'MENU_8'       = @('8 -- KMS Activation                    [!]',
                       '6 -- Kích Hoạt KMS                     [!]')
    'MENU_U'       = @('U -- Update Scan Defaults from GitHub',
                       'U -- Cập nhật Tự động từ GitHub')
    'MENU_Q'       = @('Q -- Quit', 'Q -- Thoát')
    'MENU_WARN'    = @('[!] These options make REAL changes to your Windows license.',
                       '[!] Các tùy chọn này thay đổi THẬT SỰ bản quyền Windows của bạn.')
    'MENU_SELECT'  = @('Select option (1-8, U, Q to quit)',
                       'Chọn tùy chọn (1-6, U, Q để thoát)')
    # -- Shared prompts --
    'PRESS_ENTER'  = @('Press Enter to return to menu...', 'Nhấn Enter để quay lại menu...')
    'GOODBYE'      = @('Goodbye!', 'Tạm biệt!')
    'INVALID_OPT'  = @('Invalid option -- choose 1-6, U, or Q.',
                       'Lựa chọn không hợp lệ -- chọn 1-6, U, hoặc Q.')
    'ELEVATE_WARN' = @('This option requires Administrator privileges.',
                       'Tùy chọn này yêu cầu quyền Administrator.')
    'ELEVATE_ASK'  = @('Relaunch WinLic Manager as Administrator now?',
                       'Khởi chạy lại WinLic Manager với quyền Administrator ngay bây giờ?')
    'ELEVATE_NOTE' = @('Note: The script will restart in a new elevated window.',
                       'Lưu ý: Script sẽ khởi động lại trong cửa sổ nâng cao mới.')
    'ELEVATE_DO'   = @('Relaunch as Administrator', 'Khởi chạy lại với quyền Administrator')
    'ELEVATE_FAIL' = @('Elevation failed: ', 'Nâng quyền thất bại: ')
    'ELEVATE_HINT' = @('Please right-click PowerShell and choose Run as Administrator.',
                       'Hãy nhấn chuột phải vào PowerShell và chọn Run as Administrator.')
    'PS7_AVAIL'    = @('PowerShell 7 (pwsh.exe) is available on this machine. Running in PS7 gives best compatibility.',
                       'PowerShell 7 (pwsh.exe) có sẵn trên máy này. Chạy bằng PS7 cho kết quả tốt nhất.')
    'PS7_RELAUNCH' = @('Relaunch in PowerShell 7 now?', 'Khởi chạy lại bằng PowerShell 7 ngay bây giờ?')
    'PS7_DOING'    = @('Relaunching in PowerShell 7...', 'Đang khởi chạy lại bằng PowerShell 7...')
    'PS7_SKIP'     = @('Continuing in PowerShell 5.', 'Tiếp tục với PowerShell 5.')
    'CANCEL_BACK'  = @('Canceled. Returning to menu.', 'Đã hủy. Quay lại menu.')
    'YES_NO'       = @('(y/n)', '(y/n)')

    # =========================================================================
    # Option 1 -- Full System & License Info
    # =========================================================================
    'O1_OPT_HDR'   = @('Option 1 -- Full System & License Info',
                        'Tùy chọn 1 -- Thông tin Hệ thống & Bản quyền')
    'O1_OPT_DESC1' = @('Shows OS build, BIOS OEM key, active license via WMI, registry backup key,',
                        'Hiển thị bản dựng HĐH, Key OEM BIOS, bản quyền đang hoạt động qua WMI, key dự phòng registry,')
    'O1_OPT_DESC2' = @('installed key (from DigitalProductId), and optionally the full slmgr /dlv report.',
                        'key đã cài đặt (từ DigitalProductId) và tùy chọn báo cáo đầy đủ slmgr /dlv.')
    'O1_STEP_OS'   = @('Querying OS information...', 'Đang truy vấn thông tin hệ điều hành...')
    'O1_STEP_REG'  = @('Checking Registry Backup Key...', 'Đang kiểm tra Key Dự phòng Registry...')
    'O1_STEP_LIC'  = @('Querying active Windows license (WMI)...', 'Đang truy vấn bản quyền Windows đang hoạt động (WMI)...')
    'O1_STEP_BIOS' = @('Checking BIOS/UEFI OEM key...', 'Đang kiểm tra Key OEM BIOS/UEFI...')
    'O1_STEP_INST' = @('Decoding installed key from registry...', 'Đang giải mã Key đã cài đặt từ Registry...')
    # Data labels (mirror GUI D_* keys)
    'O1_LBL_OS'    = @('OS Edition:', 'Phiên bản Windows:')
    'O1_LBL_VER'   = @('Version:', 'Số phiên bản:')
    'O1_LBL_BUILD' = @('Build:', 'Số build:')
    'O1_LBL_ARCH'  = @('Architecture:', 'Kiến trúc hệ thống:')
    'O1_LBL_NAME'  = @('License Name:', 'Tên bản quyền:')
    'O1_LBL_CHAN'  = @('License Channel:', 'Kênh phân phối:')
    'O1_LBL_PARTIAL' = @('Active Partial Key:', 'Key một phần:')
    'O1_LBL_ACT'   = @('Activation:', 'Kích hoạt:')
    'O1_LBL_BIOS'  = @('BIOS OEM Key:', 'Key Bản Quyền OEM BIOS:')
    'O1_LBL_REG'   = @('Registry Backup Key:', 'Key Dự phòng (Registry):')
    'O1_LBL_INST'  = @('Installed Key:', 'Key Đã Cài đặt:')
    'O1_LBL_KMS'   = @('KMS Server:', 'Máy chủ KMS:')
    # Status messages
    'O1_BIOS_DETECT' = @('BIOS OEM Key: Detected', 'Key OEM BIOS: Đã phát hiện')
    'O1_BIOS_NONE'   = @('BIOS OEM Key: None detected', 'Key OEM BIOS: Không phát hiện')
    'O1_REG_DETECT'  = @('Registry Backup Key: Detected', 'Key Dự phòng Registry: Đã phát hiện')
    'O1_REG_NONE'    = @('Registry Backup Key: None found', 'Key Dự phòng Registry: Không tìm thấy')
    'O1_INST_OK'     = @('Installed Key: Detected', 'Key Đã Cài đặt: Đã phát hiện')
    'O1_INST_NO'     = @('Installed Key: None found', 'Key Đã Cài đặt: Không tìm thấy')
    'O1_NOACT'       = @('No active Windows product license found via WMI.',
                          'Không tìm thấy bản quyền Windows đang hoạt động qua WMI.')
    'O1_NOACT_NOTE'  = @('This is normal on custom-built PCs or systems without OEM pre-activation.',
                          'Điều này bình thường trên máy tính tự lắp hoặc hệ thống không có kích hoạt OEM sẵn.')
    # Activation method blocks (mirror GUI DE_*/KMS_*/MAK_*)
    'O1_DE_OK'     = @('Activation method:  Digital Entitlement (HWID / DE)',
                        'Phương thức kích hoạt:  Digital Entitlement (HWID / DE)')
    'O1_DE_1'      = @('Windows is activated via a Digital Entitlement recorded on Microsoft servers.',
                        'Windows được kích hoạt qua Digital Entitlement lưu trên máy chủ Microsoft.')
    'O1_DE_2'      = @('Bound to your hardware fingerprint and Microsoft Account.',
                        'Gắn với dấu vân tay phần cứng và tài khoản Microsoft của bạn.')
    'O1_DE_3'      = @('The active key is a generic placeholder -- the actual entitlement lives in the cloud.',
                        'Key đang dùng là key chung thay thế -- quyền kích hoạt thực sự nằm trên cloud.')
    'O1_DE_VFY'    = @('To verify: Settings > System > Activation > look for Digital license',
                        'Xác nhận: Cài đặt > Hệ thống > Kích hoạt > tìm Giấy phép kỹ thuật số')
    'O1_DE_MISMATCH' = @('The BIOS OEM key and Registry Backup key may differ from the active key -- this is normal for DE-activated systems.',
                          'Key OEM BIOS và dự phòng có thể khác Key đang dùng -- điều này bình thường với hệ thống kích hoạt bằng DE.')
    'O1_KMS_OK'    = @('Activation method:  KMS (Volume / Corporate License)',
                        'Phương thức kích hoạt:  KMS (Bản quyền Doanh nghiệp/Tập thể)')
    'O1_MAK_OK'    = @('Activation method:  MAK / Retail / OEM key (standard activation)',
                        'Phương thức kích hoạt:  MAK / Retail / OEM (kích hoạt tiêu chuẩn)')
    # License status (mirror GUI LS_*)
    'O1_LS_0'      = @('Unlicensed', 'Chưa được cấp phép')
    'O1_LS_1'      = @('Licensed (Permanently Activated)', 'Đã được cấp phép (Kích hoạt vĩnh viễn)')
    'O1_LS_2'      = @('OOB Grace Period', 'Thời gian ân hạn OOB')
    'O1_LS_3'      = @('OOT Grace Period', 'Thời gian ân hạn OOT')
    'O1_LS_4'      = @('Non-Genuine Grace Period', 'Thời gian ân hạn không chính hãng')
    'O1_LS_5'      = @('Notification Mode', 'Chế độ thông báo')
    'O1_LS_6'      = @('Extended Grace Period', 'Thời gian ân hạn mở rộng')
    'O1_LS_UNK'    = @('Unknown', 'Không xác định')
    # OEM edition match
    'O1_ED_MATCH'  = @('Edition match found:', 'Phát hiện ấn bản tương ứng:')
    # Key detail section
    'O1_KEYS_HDR'  = @('-- KEY DETAILS --', '-- CHI TIẾT KEY BẢN QUYỀN --')
    'O1_SHOWFULL'  = @('Show full product key(s)? No = last 5 chars only',
                        'Hiển thị đầy đủ key? No = chỉ 5 ký tự cuối')
    'O1_SHOWBIOS'  = @('Show full BIOS OEM key?',
                        'Hiển thị đầy đủ Key OEM BIOS?')
    'O1_KEY_INST'  = @('Installed Key:         ', 'Key Đã cài đặt:        ')
    'O1_KEY_BIOS'  = @('BIOS OEM Key:          ', 'Key OEM BIOS:          ')
    'O1_KEY_REG'   = @('Registry Backup Key:   ', 'Key Dự phòng (Registry):  ')
    # Mismatch detection
    'O1_MISMATCH'  = @('Registry Backup Key does NOT match the currently active license key.',
                        'Key Dự phòng trong Registry KHÔNG khớp với Key Bản Quyền đang hoạt động.')
    'O1_MISMATCH_REASON' = @('This can happen after an edition upgrade or a change in activation method.',
                              'Điều này có thể xảy ra sau khi nâng cấp ấn bản hoặc thay đổi phương thức kích hoạt.')
    'O1_ACTIVE_ENDS' = @('  Active key ends with:         ', '  Key đang dùng kết thúc với:  ')
    'O1_BACKUP_ENDS' = @('  Registry Backup Key ends with: ', '  Key Dự phòng Registry kết thúc với:  ')
    'O1_STALE_NOTE'  = @('This is a stale backup from a previous activation or edition upgrade.',
                          'Đây là key dự phòng cũ từ lần kích hoạt hoặc nâng cấp ấn bản trước đó.')
    'O1_ACTIVE_SAFE' = @('Your active Windows activation is NOT affected.',
                          'Trạng thái kích hoạt Windows hiện tại KHÔNG bị ảnh hưởng.')
    'O1_REMOVE_ASK'  = @('Remove the stale Registry Backup Key?',
                          'Xóa Key Dự phòng cũ khỏi Registry?')
    'O1_REMOVE_OK'   = @('Registry Backup Key removed successfully.',
                          'Đã xóa Key Dự phòng khỏi Registry thành công.')
    'O1_REMOVE_FAIL' = @('Could not remove key: ', 'Không thể xóa Key Dự phòng: ')
    'O1_REMOVE_KEPT' = @('Registry Backup Key kept.', 'Đã giữ lại Key Dự phòng Registry.')
    'O1_NEED_ADMIN'  = @('Run as Administrator to remove the stale backup key.',
                          'Chạy với quyền Administrator để xóa key dự phòng cũ.')
    'O1_KEY_MATCH'   = @('Registry Backup Key matches the active product key.',
                          'Key Dự phòng trong Registry khớp với Key Bản Quyền đang hoạt động.')
    # slmgr /dli section
    'O1_DLI_HDR'   = @('-- LICENSE CHANNEL INFO (slmgr /dli) --',
                        '-- THÔNG TIN KÊNH BẢN QUYỀN (slmgr /dli) --')
    'O1_DLI_NOTE'  = @('slmgr /dli shows a summary of the active license. Only the last 5 characters of the product key are revealed.',
                        'slmgr /dli hiển thị tóm tắt bản quyền đang hoạt động. Chỉ 5 ký tự cuối của Key Bản Quyền được hiển thị.')
    'O1_RUNNING_DLI' = @('Running slmgr /dli...', 'Đang chạy slmgr /dli...')
    'O1_NO_OUTPUT'   = @('No output received from slmgr /dli.', 'Không nhận được kết quả từ slmgr /dli.')
    # slmgr /dlv section
    'O1_DLV_HDR'    = @('-- EXTENDED LICENSE REPORT (slmgr /dlv) --',
                         '-- BÁO CÁO BẢN QUYỀN MỞ RỘNG (slmgr /dlv) --')
    'O1_DLV_REVEAL' = @('Reveals: license channel, SKU, KMS server config, rearm count, expiry, CMID.',
                         'Hiển thị: kênh bản quyền, SKU, cấu hình KMS, số lần rearm, thời hạn, CMID.')
    'O1_DLV_ASK'    = @('Also run the full slmgr /dlv extended report?',
                         'Có muốn chạy báo cáo mở rộng slmgr /dlv không?')
    'O1_RUNNING_DLV' = @('Running slmgr /dlv...', 'Đang chạy slmgr /dlv...')
    'O1_LIC_STATUS_PFX' = @('License Status:  ', 'Trạng thái bản quyền:  ')
    'O1_WMI_FAIL'    = @('Failed to query WMI: ', 'Truy vấn WMI thất bại: ')

    # =========================================================================
    # Option 2 -- Test & Install New Product Key
    # =========================================================================
    'O2_OPT_HDR'    = @('Option 2 -- Test & Install New Product Key',
                         'Tùy chọn 2 -- Kiểm thử & Cài Key Mới')
    'O2_DESC1'      = @('Tests a product key by attempting local installation via slmgr /ipk.',
                         'Kiểm thử key bằng cách thử cài đặt cục bộ qua slmgr /ipk.')
    'O2_DESC2'      = @('If the key matches the installed edition and is valid, it will be accepted.',
                         'Nếu key khớp với ấn bản đã cài và hợp lệ, nó sẽ được chấp nhận.')
    'O2_DESC3'      = @('If rejected (SKU mismatch / invalid / blocked), an error code is returned.',
                         'Nếu bị từ chối (sai SKU / không hợp lệ / bị chặn), mã lỗi sẽ được trả về.')
    'O2_READ_LIC'   = @('Reading current active license...', 'Đang đọc bản quyền đang hoạt động...')
    'O2_CUR_ED'     = @('  Current edition:', '  Ấn bản hiện tại:')
    'O2_CUR_KEY'    = @('  Current partial key:', '  Key một phần hiện tại:')
    'O2_CUR_STATUS' = @('  Current status:', '  Trạng thái hiện tại:')
    'O2_NO_LIC'     = @('  No active license detected.', '  Không phát hiện bản quyền đang hoạt động.')
    'O2_INFO1'      = @('Key testing works by attempting local installation of the entered key.',
                         'Kiểm thử Key bằng cách thử cài đặt key đã nhập trên máy.')
    'O2_INFO2'      = @('Your existing activation will NOT be harmed if the key is rejected or belongs to a different edition.',
                         'Trạng thái kích hoạt hiện tại sẽ KHÔNG bị ảnh hưởng nếu key bị từ chối hoặc thuộc ấn bản khác.')
    'O2_PROMPT'     = @('  Enter the new 25-character product key (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX)',
                         '  Nhập Key Bản Quyền mới gồm 25 ký tự (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX)')
    'O2_SHOW_KEY'   = @('Show full key in the command log?', 'Hiển thị đầy đủ key trong nhật ký lệnh?')
    'O2_CONFIRM_HDR' = @('CONFIRM INSTALLATION', 'XÁC NHẬN CÀI ĐẶT')
    'O2_NEW_KEY'    = @('  New key  : ', '  Key mới : ')
    'O2_REPLACES'   = @('  Replaces : ...', '  Thay thế : ...')
    'O2_CONFIRM_WARN' = @('If accepted, this key will REPLACE your current product key immediately.',
                           'Nếu được chấp nhận, key này sẽ THAY THẾ key bản quyền hiện tại ngay lập tức.')
    'O2_CONFIRM_ASK' = @('Install this key now?', 'Cài đặt key này ngay bây giờ?')
    'O2_WARN_OVERWRITE' = @('WARNING: If the key is compatible, it WILL overwrite your current product key immediately.',
                             'CẢNH BÁO: Nếu key tương thích, nó SẼ ghi đè key bản quyền hiện tại ngay lập tức.')
    'O2_CONFIRM_TYPE_OK' = @('  Type OK (then Enter) to confirm installation, or press Enter to cancel: ',
                              '  Nhập OK rồi Enter để xác nhận, hoặc Enter để hủy: ')
    'O2_CONFIRM_BAD_INPUT' = @('Input not recognized -- installation canceled.',
                                'Đầu vào không hợp lệ -- đã hủy cài đặt.')
    'O2_CANCELED_NO' = @('Canceled -- no changes made.', 'Đã hủy -- không có thay đổi.')
    'O2_INSTALLING' = @('Installing product key...', 'Đang cài đặt key bản quyền...')
    'O2_CANCEL'     = @('Canceled -- no key entered.', 'Đã hủy -- không nhập Key Bản Quyền.')
    'O2_BADFMT'     = @('Invalid format. Key must be XXXXX-XXXXX-XXXXX-XXXXX-XXXXX',
                         'Định dạng không hợp lệ. Key phải có dạng XXXXX-XXXXX-XXXXX-XXXXX-XXXXX')
    'O2_INSTALL'    = @('Installing key:  ', 'Đang cài đặt key:  ')
    'O2_SUCCESS'    = @('Key accepted! Windows will attempt online activation automatically.',
                         'Key được chấp nhận! Windows sẽ tự động kích hoạt trực tuyến.')
    'O2_SUCCESS2'   = @('Check activation status with Option 1.',
                         'Kiểm tra trạng thái kích hoạt ở Tùy chọn 1.')
    'O2_FAIL'       = @('Key was rejected by Windows.', 'Key Bản Quyền bị Windows từ chối.')
    'O2_DIAG_SKU'   = @('Diagnosis: SKU Mismatch (0xC004F069) -- key belongs to a different edition.',
                         'Chẩn đoán: Sai SKU (0xC004F069) -- key thuộc ấn bản Windows khác.')
    'O2_DIAG_INVALID' = @('Diagnosis: Invalid Key (0xC004F050) -- key is invalid or mistyped.',
                           'Chẩn đoán: Key không hợp lệ (0xC004F050) -- key sai hoặc nhập nhầm.')
    'O2_DIAG_BLOCKED' = @('Diagnosis: Blocked Key (0xC004C003) -- key has been blacklisted by Microsoft.',
                           'Chẩn đoán: Key bị chặn (0xC004C003) -- key đã bị Microsoft thu hồi.')
    'O2_DIAG_GENERAL' = @('Diagnosis: Installation failed -- check the error code.',
                           'Chẩn đoán: Cài đặt thất bại -- kiểm tra mã lỗi.')
    'O2_REF_URL'    = @('Reference: https://learn.microsoft.com/windows-server/get-started/activation-error-codes',
                         'Tham khảo: https://learn.microsoft.com/windows-server/get-started/activation-error-codes')

    # -- Option 2 -- channel warnings (shown in pre-arm) --
    'O2_WARN_CHAN_KMS'  = @('[!] Current channel is VOLUME_KMSCLIENT -- this option is for Retail/OEM/MAK keys. For KMS activation use Option 6.',
                             '[!] Kênh hiện tại là VOLUME_KMSCLIENT -- tùy chọn này dành cho key Retail/OEM/MAK. Dùng Tùy chọn 6 để kích hoạt KMS.')
    'O2_WARN_CHAN_KMSHOST' = @('[!] This machine appears to be a KMS HOST (VOLUME_KMS channel). Use Option 6 for KMS client activation.',
                                '[!] Máy này có vẻ là KMS HOST (kênh VOLUME_KMS). Dùng Tùy chọn 6 để kích hoạt KMS client.')
    'O2_WARN_CHAN_SUB'  = @('[!] Subscription-based activation is not managed by this option.',
                             '[!] Kích hoạt theo đăng ký không được quản lý bởi tùy chọn này.')

    # -- Option 2 -- auto /ato results --
    'O2_ATO_AUTO'          = @('Attempting online activation automatically (slmgr /ato)...',
                                'Đang tự động kích hoạt trực tuyến (slmgr /ato)...')
    'O2_ATO_SUCCESS'       = @('Online activation succeeded.', 'Kích hoạt trực tuyến thành công.')
    'O2_ATO_FAIL'          = @('Online activation failed.', 'Kích hoạt trực tuyến thất bại.')
    'O2_DIAG_DIDNTWORK'    = @('Diagnosis: Key Did Not Work (0x80070490) -- key may be invalid or not accepted by this edition.',
                                'Chẩn đoán: Key Không Hoạt Động (0x80070490) -- key có thể không hợp lệ hoặc không được chấp nhận bởi ấn bản này.')
    'O2_DIAG_SERVER_INVALID' = @('Diagnosis: Server Reports Key Invalid (0xC004C001) -- Microsoft activation server rejected this MAK. Verify the key.',
                                  'Chẩn đoán: Máy Chủ Báo Key Không Hợp Lệ (0xC004C001) -- máy chủ kích hoạt Microsoft từ chối MAK này. Hãy kiểm tra lại key.')
    'O2_DIAG_MAK_LIMIT'    = @('Diagnosis: MAK Activation Limit Exceeded (0xC004C020 / 0xC004C021) -- all activations used. Contact Microsoft Licensing.',
                                'Chẩn đoán: Vượt Giới Hạn Kích Hoạt MAK (0xC004C020 / 0xC004C021) -- đã dùng hết số lần kích hoạt. Liên hệ Microsoft Licensing.')
    'O2_DIAG_SERVER_NOACT' = @('Diagnosis: Server Could Not Activate (0xC004B100) -- Microsoft server was unable to activate this key. Verify the MAK with Microsoft Licensing.',
                                'Chẩn đoán: Máy Chủ Không Thể Kích Hoạt (0xC004B100) -- máy chủ Microsoft không thể kích hoạt key này. Xác minh MAK với Microsoft Licensing.')
    'O2_DIAG_GRACE'        = @('Diagnosis: Grace Period Expired (0xC004F009) -- the activation grace period has ended. Contact Microsoft Licensing.',
                                'Chẩn đoán: Hết Thời Gian Ân Hạn (0xC004F009) -- thời gian ân hạn kích hoạt đã kết thúc. Liên hệ Microsoft Licensing.')
    'O2_DIAG_NOTGENUINE'   = @('Diagnosis: Not Genuine Windows (0x8004FE21) -- system files may be modified or corrupted. Reinstall Windows.',
                                'Chẩn đoán: Windows Không Chính Hãng (0x8004FE21) -- tệp hệ thống có thể bị thay đổi hoặc hỏng. Cài lại Windows.')

    # =========================================================================
    # Option 3 -- Remove Activation
    # =========================================================================
    'O3_OPT_HDR'  = @('Option 3 -- Remove Activation', 'Tùy chọn 3 -- Gỡ Kích Hoạt')
    'O3_WARN1'    = @('WARNING -- This will uninstall the current Windows product key AND clear',
                       'CẢNH BÁO -- Thao tác này sẽ gỡ cài đặt Key Bản Quyền Windows hiện tại VÀ xóa')
    'O3_WARN2'    = @('           it from the registry. Windows will become UNACTIVATED.',
                       '           khỏi Registry. Windows sẽ trở thành CHƯA ĐƯỢC KÍCH HOẠT.')
    'O3_CONFIRM'  = @('This will uninstall the current product key and clear the license store. Continue?',
                       'Thao tác này sẽ gỡ cài đặt key hiện tại và xóa kho bản quyền. Tiếp tục?')
    'O3_CANCEL'   = @('Canceled -- activation not changed.', 'Đã hủy -- kích hoạt không thay đổi.')
    'O3_UNINST'   = @('Uninstalling product key  (slmgr /upk)...', 'Đang gỡ cài đặt Key Bản Quyền  (slmgr /upk)...')
    'O3_CLEAR'    = @('Clearing key from registry  (slmgr /cpky)...', 'Đang xóa Key Bản Quyền khỏi Registry  (slmgr /cpky)...')
    'O3_DONE'     = @('Product key uninstalled and registry cleared.',
                       'Đã gỡ cài đặt Key Bản Quyền và xóa khỏi Registry.')

    # =========================================================================
    # Option 4 -- Reset Activation (Rearm)
    # =========================================================================
    'O4_OPT_HDR'      = @('Option 4 -- Reset Activation (Rearm)', 'Tùy chọn 4 -- Đặt Lại Kích Hoạt (Rearm)')
    'O4_WARN1'        = @('WARNING -- This resets the licensing status and activation timers (rearm).',
                           'CẢNH BÁO -- Thao tác này đặt lại trạng thái bản quyền và bộ đếm kích hoạt (rearm).')
    'O4_WARN2'        = @('           A computer restart is required for changes to take effect.',
                           '           Cần khởi động lại máy tính để áp dụng thay đổi.')
    'O4_CONFIRM'      = @('Continue with rearm?', 'Tiếp tục với rearm?')
    'O4_CANCEL'       = @('Canceled -- activation not changed.', 'Đã hủy -- kích hoạt không thay đổi.')
    'O4_RUNNING_REARM' = @('Executing  slmgr /rearm...', 'Đang thực thi  slmgr /rearm...')
    'O4_DONE'         = @('Rearm complete. Restart your computer for changes to take effect.',
                           'Rearm hoàn tất. Khởi động lại máy tính để áp dụng thay đổi.')
    'O4_RESTART_ASK'  = @('Restart the computer now?', 'Khởi động lại máy tính ngay bây giờ?')
    'O4_RESTARTING'   = @('Restarting in 5 seconds...', 'Đang khởi động lại sau 5 giây...')

    # =========================================================================
    # Option 5 -- 3rd Party Activation Audit
    # =========================================================================
    'O5_OPT_HDR'   = @('Option 5 -- 3rd Party Activation Audit', 'Tùy chọn 5 -- Kiểm Tra Kích Hoạt Bên Thứ Ba')
    # Scan configuration summary
    'O5_CFG_TITLE'   = @('SCAN CONFIGURATION', 'CẤU HÌNH QUÉT')
    'O5_CFG_FILE'    = @('Settings file:', 'Tệp cài đặt:')
    'O5_CFG_PORTS'   = @('Ports to probe:', 'Cổng cần kiểm tra:')
    'O5_CFG_SVC'     = @('Services:', 'Dịch vụ:')
    'O5_CFG_TASKS'   = @('Tasks:', 'Tác vụ:')
    'O5_CFG_PROCS'   = @('Processes:', 'Tiến trình:')
    'O5_CFG_FILES'   = @('Extra file paths:', 'Đường dẫn tệp bổ sung:')
    'O5_CFG_DOMAINS' = @('KMS piracy domains:', 'Tên miền KMS lậu:')
    'O5_CFG_BUILTIN' = @('built-in', 'mặc định')
    'O5_CFG_CUSTOM'  = @('custom', 'tùy chỉnh')
    'O5_CFG_NONE'    = @('(none)', '(không có)')
    'O5_EDIT_HINT1'  = @('Edit settings.ini to add custom ports, services, tasks, processes, or file', 'Chỉnh sửa settings.ini để thêm cổng, dịch vụ, tác vụ, tiến trình hoặc tệp')
    'O5_EDIT_HINT2'  = @('paths before scanning.  The file is at:', 'tùy chỉnh trước khi quét.  Tệp nằm tại:')
    'O5_EDIT_ASK'    = @('Edit settings.ini before scanning? (y/n/skip to proceed)',
                          'Chỉnh sửa settings.ini trước khi quét? (y/n/skip to proceed)')
    'O5_OPENING_INI' = @('Opening settings.ini in Notepad -- close the window to continue...',
                          'Đang mở settings.ini trong Notepad -- đóng cửa sổ để tiếp tục...')
    'O5_RELOADING'   = @('Reloading scan configuration...', 'Đang tải lại cấu hình quét...')
    'O5_INI_NOT_FOUND' = @('settings.ini not found at: ', 'Không tìm thấy settings.ini tại: ')
    'O5_INI_HINT1'   = @('The file should be in the same folder as WinLicManager.ps1.',
                          'Tệp phải nằm cùng thư mục với WinLicManager.ps1.')
    'O5_INI_HINT2'   = @('If you cloned the repo, ensure WinLicPS\settings.ini exists.',
                          'Nếu bạn clone repo, hãy đảm bảo WinLicPS\settings.ini tồn tại.')
    'O5_INI_CONTINUE' = @('  Press Enter to continue with built-in defaults...',
                           '  Nhấn Enter để tiếp tục với cài đặt mặc định...')
    # Preamble (mirrors GUI P7_Header / P7_CanDetect* / P7_LimitHeader / P7_Limit*)
    'O5_SCAN_HDR'    = @('== WHAT THIS SCAN CHECKS ==', '== CÁC KIỂM TRA TRONG LẦN QUÉT NÀY ==')
    'O5_CAN1'        = @('(1) KMS server name / registry  ->  detects LOCAL emulators (KMSpico, vlmcsd) AND CLOUD piracy KMS services',
                          '(1) Tên máy chủ KMS / registry  →  phát hiện KMS giả lập CỤC BỘ (KMSpico, vlmcsd...) VÀ dịch vụ KMS lậu trên CLOUD (ví dụ msguides.com, máy chủ KMS công cộng)')
    'O5_CAN2'        = @('(2) Localhost port probe         ->  confirms a live local KMS listener is running',
                          '(2) Kiểm tra cổng localhost      ->  xác nhận dịch vụ KMS cục bộ đang hoạt động')
    'O5_CAN3'        = @('(3) System services              ->  residual activation services that survive reboots',
                          '(3) Dịch vụ hệ thống             →  dịch vụ kích hoạt cài cố định, tồn tại qua các lần khởi động lại')
    'O5_CAN4'        = @('(4) Scheduled tasks              ->  periodic re-activation tasks (AutoKMS, KMSAuto, Activation-Renewal...)',
                          '(4) Tác vụ định kỳ               →  tác vụ tự động kích hoạt lại, phổ biến ở KMSpico/KMSAuto')
    'O5_CAN5'        = @('(5) File / folder paths          ->  installation leftovers + KMS38 GenuineTicket + MAS renewal artifacts',
                          '(5) Đường dẫn tệp / thư mục      →  phần còn lại sau khi cài đặt công cụ kích hoạt')
    'O5_CAN6'        = @('(6) Running processes            ->  active activation tool processes at scan time',
                          '(6) Tiến trình đang chạy         ->  công cụ kích hoạt đang hoạt động tại thời điểm quét')
    'O5_CAN7'        = @('(7) GVLK key + permanent act.    ->  detects TSforge / KMS38 / HWID piracy patterns',
                          '(7) Khóa GVLK + kích hoạt vĩnh viễn →  phát hiện kiểu kích hoạt TSforge / KMS38 / HWID lậu')
    'O5_CAN8'        = @('(8) Activation expiry anomaly    ->  year 2038 (KMS38), 2100+ (TSforge KMS4k), ~180d (Online KMS)',
                          '(8) Ngày hết hạn kích hoạt bất thường →  cảnh báo năm 2038 (KMS38), tương lai xa (TSforge), hoặc chu kỳ 180 ngày (Online KMS)')
    'O5_CAN9'        = @('(9) SPP store timestamp          ->  LOW CONFIDENCE indicator of TSforge data.dat modification',
                          '(9) Dấu thời gian kho SPP        →  dấu hiệu ĐỘ TIN CẬY THẤP về việc TSforge sửa đổi kho SPP')
    'O5_LIMIT_HDR'   = @('KNOWN LIMITATIONS -- CANNOT DETECT', 'GIỚI HẠN ĐÃ BIẾT -- KHÔNG THỂ PHÁT HIỆN')
    'O5_LIMIT1'      = @('* HWID tools (MAS HWID, HWIDGEN): create a genuine Digital Entitlement via MS API',
                          '* HWID (MAS): lấy bản quyền kỹ thuật số DO MICROSOFT CẤP -- không thể phân biệt với bản quyền mua. Chỉ có thể phát hiện nếu GVLK / key chung vẫn còn cài đặt.')
    'O5_LIMIT2'      = @('* Cleaned activations: if tool was fully removed, all traces are gone',
                          '* Kích hoạt đã dọn sạch: nếu công cụ bị gỡ hoàn toàn sau khi dùng, mọi dấu vết đều mất -- kích hoạt vẫn còn nhưng không để lại vết.')
    'O5_LIMIT3'      = @('* Legacy SLIC/OEM BIOS patching (Windows Loader era): modifies firmware tables',
                          '* Vá SLIC/OEM BIOS kiểu cũ (thời Windows Loader): sửa đổi bảng firmware ở mức không thể thấy qua phân tích phần mềm.')
    'O5_LIMIT4'      = @('* Corporate KMS: legitimate company KMS on an internal server may trigger GVLK warnings -- verify with IT.',
                          '* KMS doanh nghiệp: KMS hợp lệ của công ty trên máy chủ nội bộ có thể kích hoạt một số cảnh báo GVLK -- luôn xác nhận với bộ phận IT.')
    # Per-check step labels (mirror GUI Fetch_* keys)
    'O5_STEP1'       = @('① Checking KMS server name (registry + WMI)...', '① Kiểm tra tên máy chủ KMS (registry + WMI)...')
    'O5_STEP2'       = @('② Probing configured KMS port(s) on localhost...', '② Kiểm tra cổng KMS đã cấu hình trên localhost...')
    'O5_STEP3'       = @('③ Scanning system services...', '③ Quét dịch vụ hệ thống...')
    'O5_STEP4'       = @('④ Scanning scheduled tasks...', '④ Quét tác vụ định kỳ...')
    'O5_STEP5'       = @('⑤ Scanning known tool file / folder paths...', '⑤ Quét đường dẫn tệp / thư mục công cụ đã biết...')
    'O5_STEP6'       = @('⑥ Scanning running processes...', '⑥ Quét tiến trình đang chạy...')
    'O5_STEP7'       = @('⑦ Checking activation channel and installed key type (WMI)...', '⑦ Kiểm tra kênh kích hoạt và loại khóa (WMI)...')
    'O5_STEP8'       = @('⑧ Analyzing activation expiry date (WMI)...', '⑧ Phân tích ngày hết hạn kích hoạt (WMI)...')
    'O5_STEP9'       = @('⑨ Checking SPP trusted store file timestamp [LOW CONFIDENCE]...', '⑨ Kiểm tra dấu thời gian tệp kho tin cậy SPP [ĐỘ TIN CẬY THẤP]...')
    'O5_STEP10'      = @('⑩ Checking SPP Security activation event log...', '⑩ Kiểm tra nhật ký sự kiện kích hoạt SPP...')
    # Per-check explain texts (mirror GUI P7_*Explain)
    'O5_KMS_EXP1'    = @('KMS piracy operates in two forms:', 'KMS lậu hoạt động dưới hai dạng:')
    'O5_KMS_EXP2'    = @('  LOCAL EMULATOR  -- fake KMS server on 127.x.x.x (KMSpico, vlmcsd, KMSAuto)',
                          '  GIẢ LẬP CỤC BỘ  -- máy chủ KMS giả chạy trên 127.x.x.x (KMSpico, vlmcsd, KMSAuto)')
    'O5_KMS_EXP3'    = @('  CLOUD SERVICE   -- third-party internet KMS host (e.g. km8.msguides.com)',
                          '  DỊCH VỤ CLOUD   -- máy chủ KMS bên thứ ba trên internet (vd km8.msguides.com)')
    'O5_KMS_EXP4'    = @('Microsoft does NOT provide public KMS servers. Any cloud KMS is a piracy service.',
                          'Microsoft KHÔNG cung cấp máy chủ KMS công cộng. Bất kỳ cloud KMS nào là dịch vụ lậu.')
    'O5_PORT_EXP1'   = @('An open port means a local KMS emulator is actively listening --',
                          'Cổng mở có nghĩa là có KMS giả lập đang lắng nghe --')
    'O5_PORT_EXP2'   = @('the strongest single indicator of local KMS-based piracy.',
                          'đây là dấu hiệu đơn lẻ mạnh nhất của kích hoạt KMS lậu cục bộ.')
    'O5_SVC_EXP'     = @('KMS emulators often install as Windows services to survive reboots and periodically re-activate. Known service names are checked below.',
                          'KMS giả lập thường cài đặt dưới dạng dịch vụ Windows để tồn tại qua khởi động lại và định kỳ kích hoạt lại. Các tên dịch vụ đã biết được kiểm tra bên dưới.')
    'O5_TASK_EXP1'   = @('Activation tools like KMSpico create scheduled tasks (e.g. AutoKMS)',
                          'Công cụ như KMSpico tạo tác vụ định kỳ (vd AutoKMS)')
    'O5_TASK_EXP2'   = @('that re-activate Windows periodically to prevent grace-period expiry.',
                          'để kích hoạt lại Windows theo chu kỳ, ngăn hết hạn thời gian ân hạn.')
    'O5_FILE_EXP'    = @('Known installation folders, executables (KMSELDI.exe), and patched system files are checked against the known paths list.',
                          'Thư mục cài đặt, tệp thực thi (KMSELDI.exe) và tệp hệ thống bị vá được kiểm tra đối chiếu danh sách đường dẫn đã biết.')
    'O5_PROC_EXP'    = @('Some activation tools leave resident processes at scan time. This check lists running processes whose names match known tools.',
                          'Một số công cụ kích hoạt để lại tiến trình thường trú. Kiểm tra này liệt kê các tiến trình đang chạy có tên trùng với công cụ đã biết.')
    'O5_GVLK_EXP1'  = @('GVLK (Generic Volume License Keys) combined with permanent activation',
                          'GVLK (Khóa Cấp phép Số lượng Lớn Dùng Chung) kết hợp với kích hoạt vĩnh viễn')
    'O5_GVLK_EXP2'  = @('(no KMS renewal countdown) strongly indicates TSforge, KMS38, or HWID piracy.',
                          '(không có đếm ngược gia hạn KMS) là dấu hiệu mạnh của TSforge, KMS38 hoặc HWID lậu.')
    'O5_GVLK_EXP3'  = @('Legitimate enterprise KMS clients always show an active 180-day countdown.',
                          'KMS doanh nghiệp hợp lệ luôn hiển thị đếm ngược 180 ngày đang hoạt động.')
    'O5_GVLK_EXP4'  = @('Channel VOLUME_KMSCLIENT + GVLK + no countdown -> not valid. Channel RETAIL or OEM_DM + placeholder + permanent -> valid DE (HWID).',
                          'Kênh VOLUME_KMSCLIENT + GVLK + không có đếm ngược → không hợp lệ. Kênh RETAIL hoặc OEM_DM + placeholder + vĩnh viễn → kích hoạt Digital Entitlement (HWID) hợp lệ.')
    'O5_GVLK_COUNT' = @('GVLK suffixes loaded from settings.ini: ', 'Hậu tố GVLK đã tải từ settings.ini: ')
    'O5_GVLK_NONE'  = @('No GVLK keys in settings.ini -- only generic DE placeholder keys checked.',
                          'Không có khóa GVLK trong settings.ini -- chỉ kiểm tra các khóa placeholder DE chung.')
    'O5_EXPIRY_EXP1' = @('Unusual expiry dates are strong indicators of specific piracy methods:',
                          'Ngày hết hạn bất thường là dấu hiệu mạnh của phương thức kích hoạt lậu cụ thể:')
    'O5_EXPIRY_EXP2' = @('  Year approx 2038 = KMS38 (32-bit timestamp max)',
                          '  Năm khoảng 2038 = KMS38 (giá trị tối đa timestamp 32-bit)')
    'O5_EXPIRY_EXP3' = @('  Year 2100+       = TSforge KMS4k (4000-year forged KMS lease)',
                          '  Năm 2100+        = TSforge KMS4k (hợp đồng KMS giả mạo 4000 năm)')
    'O5_EXPIRY_EXP4' = @('  ~180 days        = Online KMS renewal cycle',
                          '  ~180 ngày        = chu kỳ gia hạn Online KMS')
    'O5_SPP_EXP1'   = @('TSforge modifies the SPP trusted store (data.dat) directly.',
                          'TSforge sửa đổi trực tiếp kho tin cậy SPP (data.dat).')
    'O5_SPP_EXP2'   = @('A LastWriteTime not correlated with any Windows Update event is a LOW CONFIDENCE indicator.',
                          'Dấu LastWriteTime không tương ứng với sự kiện Windows Update là dấu hiệu ĐỘ TIN CẬY THẤP.')
    'O5_SPP_EXP3'   = @('Windows Update and legitimate troubleshooting can also modify this file.',
                          'Windows Update và khắc phục sự cố hợp lệ cũng có thể sửa đổi tệp này.')
    'O5_SPPE_EXP1'  = @('Queries System event log for Microsoft-Windows-Security-SPP events.',
                          'Truy vấn nhật ký sự kiện Hệ thống tìm các sự kiện Microsoft-Windows-Security-SPP.')
    'O5_SPPE_EXP2'  = @('Event 12290 (KMS request) records the server address -- external = piracy indicator.',
                          'Sự kiện 12290 (yêu cầu KMS) ghi lại địa chỉ máy chủ -- bên ngoài = dấu hiệu lậu.')
    # Scan results (mirror GUI P7_*)
    'O5_KMS_NONE'   = @('No KMS server configured.', 'Không có máy chủ KMS nào được cấu hình.')
    'O5_KMS_NAME'   = @('KMS server configured:', 'Máy chủ KMS đã cấu hình:')
    'O5_KMS_LOCAL'  = @('LOCAL KMS EMULATOR -- KMS server points to localhost/127.x.x.x!',
                         'KMS GIẢ LẬP CỤC BỘ -- Máy chủ KMS trỏ đến localhost/127.x.x.x!')
    'O5_KMS_FAKE'   = @('A fake KMS server (KMSpico, vlmcsd, KMSAuto) is running locally.',
                         'Có máy chủ KMS giả (KMSpico, vlmcsd, KMSAuto) đang chạy cục bộ.')
    'O5_KMS_BOGUS'  = @('BOGUS KMS PLACEHOLDER DETECTED -- KMS server is 10.0.0.10',
                         'PHÁT HIỆN IP KMS GIẢ -- Máy chủ KMS được đặt thành 10.0.0.10')
    'O5_KMS_BOGUS2' = @('This is a non-routable IP used by MAS Online KMS (no renewal task installed).',
                         'Đây là IP không thể định tuyến được MAS Online KMS sử dụng (không cài tác vụ gia hạn).')
    'O5_KMS_BOGUS3' = @('It prevents Office activation banners but does NOT legitimately activate Windows.',
                         'Nó ngăn thông báo kích hoạt Office nhưng KHÔNG kích hoạt Windows hợp lệ.')
    'O5_KMS_PIRACY_KNOWN' = @('KNOWN PIRACY KMS DOMAIN DETECTED!', 'PHÁT HIỆN TÊN MIỀN KMS LẬU ĐÃ BIẾT!')
    'O5_KMS_PIRACY_SERVER' = @('  Server : ', '  Máy chủ : ')
    'O5_KMS_PIRACY_NOTE'  = @('  This domain is a recognized third-party activation service.',
                               '  Tên miền này là dịch vụ kích hoạt bên thứ ba đã được nhận dạng.')
    'O5_KMS_PIRACY_NOT_MS' = @('  It is NOT operated by Microsoft.', '  Không do Microsoft vận hành.')
    'O5_KMS_CLOUD'   = @('CLOUD KMS PIRACY SERVICE DETECTED!', 'PHÁT HIỆN DỊCH VỤ KMS LẬU TRÊN CLOUD!')
    'O5_KMS_CLOUD2'  = @('  This is a public internet KMS host -- NOT a Microsoft service.',
                          '  Đây là máy chủ KMS công cộng trên internet -- KHÔNG phải dịch vụ Microsoft.')
    'O5_KMS_CLOUD3'  = @('  Microsoft does NOT provide public KMS servers.',
                          '  Microsoft KHÔNG cung cấp máy chủ KMS công cộng.')
    'O5_KMS_CLOUD4'  = @('  This is almost certainly an unauthorized third-party activation service.',
                          '  Đây gần như chắc chắn là dịch vụ kích hoạt trái phép của bên thứ ba.')
    'O5_KMS_MSOFF'   = @('Microsoft Azure KMS endpoint -- legitimate Microsoft-operated server.',
                          'Điểm cuối KMS Azure của Microsoft -- máy chủ hợp lệ do Microsoft vận hành.')
    'O5_KMS_MSOFF2'  = @('Note: Azure KMS is only valid inside Microsoft Azure virtual machines.',
                          'Lưu ý: KMS Azure chỉ hợp lệ trong máy ảo Microsoft Azure.')
    'O5_KMS_MSOFF3'  = @('If this is NOT an Azure VM, this configuration is unusual.',
                          'Nếu đây KHÔNG phải máy ảo Azure, cấu hình này bất thường.')
    'O5_KMS_PRIV'    = @('KMS server is on a private/internal network address.',
                          'Máy chủ KMS trên địa chỉ mạng nội bộ.')
    'O5_KMS_PRIV2'   = @('Consistent with a legitimate corporate deployment.',
                          'Phù hợp với triển khai doanh nghiệp hợp lệ.')
    'O5_OFF_KMS'     = @('Office KMS server configured: {0} (suspicious -- piracy domain or external host)',
                          'Máy chủ KMS Office đã cấu hình: {0} (đáng ngờ -- tên miền lậu hoặc host bên ngoài)')
    'O5_PORT_OPEN'   = @('Port {0} OPEN on localhost -- a local KMS listener is actively running!',
                          'Cổng {0} ĐANG MỞ trên localhost -- có dịch vụ KMS cục bộ đang chạy!')
    'O5_PORT_CLOSED' = @('Port {0} on localhost is closed (no local KMS listener).',
                          'Cổng {0} trên localhost đã đóng -- không có dịch vụ KMS cục bộ.')
    'O5_SVC_FOUND'   = @('Suspicious service detected:  ', 'Phát hiện dịch vụ đáng ngờ:  ')
    'O5_SVC_NONE'    = @('No suspicious services found.', 'Không phát hiện dịch vụ đáng ngờ.')
    'O5_TASK_FOUND'  = @('Suspicious scheduled task:  ', 'Phát hiện tác vụ định kỳ đáng ngờ:  ')
    'O5_TASK_NONE'   = @('No suspicious scheduled tasks found.', 'Không phát hiện tác vụ định kỳ đáng ngờ.')
    'O5_FILE_FOUND'  = @('Known activation tool path found:  ', 'Phát hiện đường dẫn công cụ kích hoạt:  ')
    'O5_FILE_NONE'   = @('No known activation tool files or folders found.', 'Không phát hiện tệp hoặc thư mục công cụ kích hoạt.')
    'O5_PROC_FOUND'  = @('Suspicious process running:  ', 'Phát hiện tiến trình đáng ngờ:  ')
    'O5_PROC_NONE'   = @('No suspicious processes running.', 'Không phát hiện tiến trình đáng ngờ.')
    # GVLK results
    'O5_PHONE'       = @('ANOMALOUS PHONE ACTIVATION DETECTED!', 'PHÁT HIỆN KÍCH HOẠT ĐIỆN THOẠI BẤT THƯỜNG!')
    'O5_PHONE2'      = @('  Windows reports Phone activation with no record of a phone activation flow.',
                          '  Windows báo cáo kích hoạt Điện thoại mà không có quy trình kích hoạt điện thoại.')
    'O5_PHONE3'      = @('  This is a characteristic indicator of TSforge ZeroCID sub-method.',
                          '  Đây là dấu hiệu đặc trưng của phương thức con TSforge ZeroCID.')
    'O5_GVLK_PERM_VOL' = @('LIKELY PIRACY -- VOLUME_KMSCLIENT key [{0}] installed with PERMANENT activation!',
                             'CÓ KHẢ NĂNG LẬU -- Khóa VOLUME_KMSCLIENT [{0}] được cài với kích hoạt VĨNH VIỄN!')
    'O5_GVLK_PV2'   = @('  Description: ', '  Mô tả: ')
    'O5_GVLK_PV3'   = @('  Legitimate enterprise KMS clients always have a 180-day renewal countdown.',
                          '  KMS doanh nghiệp hợp lệ luôn có đếm ngược gia hạn 180 ngày.')
    'O5_GVLK_PV4'   = @('  This pattern matches KMS38 or TSforge KMS4k.',
                          '  Mẫu này khớp với KMS38 hoặc TSforge KMS4k.')
    'O5_HWID_DE'     = @('HWID/DE placeholder key [{0}] -- channel [{1}], permanent activation.',
                          'Khóa HWID/DE placeholder [{0}] -- kênh [{1}], kích hoạt vĩnh viễn.')
    'O5_HWID_DE2'    = @('This is consistent with legitimate Digital Entitlement (HWID/DE) activation.',
                          'Phù hợp với kích hoạt Digital Entitlement (HWID/DE) hợp lệ.')
    'O5_HWID_DE3'    = @('Microsoft issues a real server-side digital license for this hardware.',
                          'Microsoft cấp giấy phép kỹ thuật số thật từ phía máy chủ cho phần cứng này.')
    'O5_HWID_DE4'    = @('WMI-indistinguishable from a genuine retail license at this level.',
                          'Không thể phân biệt qua WMI với bản quyền bán lẻ chính hãng ở cấp này.')
    'O5_GVLK_KMS'   = @('GVLK key detected with active KMS renewal (grace: {0} min).',
                          'Phát hiện khóa GVLK với gia hạn KMS đang hoạt động (ân hạn: {0} phút).')
    'O5_GVLK_KMS2'  = @('Consistent with legitimate enterprise KMS activation. Verify the KMS server.',
                          'Phù hợp với kích hoạt KMS doanh nghiệp hợp lệ. Xác minh máy chủ KMS.')
    'O5_GVLK_NONE2' = @('Installed key (ends: {0}) is NOT a known GVLK or placeholder key.',
                          'Khóa đã cài (kết thúc: {0}) KHÔNG phải GVLK hoặc khóa placeholder đã biết.')
    'O5_NO_WMI'      = @('Could not retrieve WMI licensing data for channel check.',
                          'Không thể lấy dữ liệu cấp phép WMI để kiểm tra kênh.')
    # Expiry analysis
    'O5_EXP_PERM'    = @('Windows reports permanent activation (no expiry countdown).',
                          'Windows báo cáo kích hoạt vĩnh viễn (không có đếm ngược hết hạn).')
    'O5_EXP_DATE'    = @('Expiry date:', 'Ngày hết hạn:')
    'O5_EXP_TSFORGE' = @('TSFORGE KMS4K DETECTED -- Expiry year {0} (thousands of years in the future)!',
                          'PHÁT HIỆN TSFORGE KMS4K -- Năm hết hạn {0} (hàng nghìn năm trong tương lai)!')
    'O5_EXP_TSFORG2' = @('TSforge forges a KMS lease directly into the SPP trusted store.',
                          'TSforge giả mạo hợp đồng thuê KMS trực tiếp vào kho tin cậy SPP.')
    'O5_EXP_KMS38'   = @('KMS38 LEGACY ACTIVATION -- Expiry year {0} (~2038-01-19 = max 32-bit timestamp)',
                          'KÍCH HOẠT KMS38 CŨ -- Năm hết hạn {0} (~2038-01-19 = giá trị tối đa timestamp 32-bit)')
    'O5_EXP_KMS38_2' = @('KMS38 was patched in KB5068861 (Nov 2025) but pre-patched machines may still show this.',
                           'KMS38 đã được vá trong KB5068861 (Th11 2025) nhưng máy chưa vá vẫn có thể hiển thị điều này.')
    'O5_EXP_ONLKMS'  = @('ONLINE KMS 180-DAY CYCLE -- Expiry ~{0} days, consistent with Online KMS renewal.',
                           'CHU KỲ KMS TRỰC TUYẾN 180 NGÀY -- Hết hạn ~{0} ngày, phù hợp với gia hạn Online KMS.')
    'O5_EXP_ONLKMS2' = @('Combined with an external KMS server above, this strongly indicates MAS Online KMS.',
                           'Kết hợp với máy chủ KMS bên ngoài ở trên, điều này cho thấy rõ MAS Online KMS.')
    'O5_EXP_NORMAL'  = @('Activation expiry date appears normal.', 'Ngày hết hạn kích hoạt có vẻ bình thường.')
    'O5_NO_EXP_WMI'  = @('WMI licensing data not available for expiry analysis.',
                          'Dữ liệu cấp phép WMI không khả dụng để phân tích hết hạn.')
    # SPP store results
    'O5_SPP_PATH'    = @('SPP store LastWriteTime:', 'LastWriteTime kho SPP:')
    'O5_SPP_INSTALL' = @('Windows InstallDate:', 'Ngày cài đặt Windows:')
    'O5_SPP_OK'      = @('SPP store timestamp correlates with a Windows Update event -- no anomaly.',
                          'Dấu thời gian kho SPP tương ứng với sự kiện Windows Update -- không có bất thường.')
    'O5_SPP_WARN1'   = @('[LOW CONFIDENCE] SPP store was modified with no correlated Windows Update event.',
                          '[ĐỘ TIN CẬY THẤP] Kho SPP đã được sửa đổi mà không có sự kiện Windows Update tương ứng.')
    'O5_SPP_WARN2'   = @('  This MAY indicate TSforge activation. Use slmgr /dlv for further investigation.',
                          '  Điều này CÓ THỂ cho thấy kích hoạt TSforge. Dùng slmgr /dlv để điều tra thêm.')
    'O5_SPP_NORMAL'  = @('SPP store timestamp appears normal.', 'Dấu thời gian kho SPP có vẻ bình thường.')
    'O5_SPP_NOT_FOUND' = @('SPP store file not found at expected path -- cannot check.',
                            'Không tìm thấy tệp kho SPP tại đường dẫn dự kiến -- không thể kiểm tra.')
    # SPP event log results
    'O5_SPPE_NONE'   = @('No SPP activation security events found in System log.',
                          'Không tìm thấy sự kiện bảo mật kích hoạt SPP trong nhật ký Hệ thống.')
    'O5_SPPE_COUNT'  = @('SPP events found:', 'Tìm thấy sự kiện SPP:')
    'O5_SPPE_EXT'    = @('SPP event 12290: KMS request to external server: ',
                          'Sự kiện SPP 12290: Yêu cầu KMS đến máy chủ bên ngoài: ')
    'O5_SPPE_CONF'   = @('  This confirms activation via an unauthorized public KMS service.',
                          '  Điều này xác nhận kích hoạt qua dịch vụ KMS công cộng trái phép.')
    'O5_SPPE_OK'     = @('SPP events present -- no external KMS server address found in event data.',
                          'Có sự kiện SPP -- không tìm thấy địa chỉ máy chủ KMS bên ngoài trong dữ liệu sự kiện.')
    'O5_SPPE_ERR'    = @('SPP event log check error: ', 'Lỗi kiểm tra nhật ký sự kiện SPP: ')
    # Results summary
    'O5_RESULTS_HDR' = @('RESULTS', 'KẾT QUẢ')
    'O5_CRITICAL'    = @('CRITICAL: Piracy KMS activation detected (local emulator, bogus IP, or unauthorized cloud service).',
                          'NGHIÊM TRỌNG: Phát hiện kích hoạt KMS lậu (giả lập cục bộ, IP giả, hoặc dịch vụ cloud trái phép).')
    'O5_CRITICAL2'   = @('          Windows is almost certainly activated by an unauthorized 3rd-party method.',
                          '          Windows gần như chắc chắn đang được kích hoạt bằng phương pháp trái phép.')
    'O5_CRIT_COUNT'  = @('          Total indicators flagged: ', '          Tổng số dấu hiệu bị đánh dấu: ')
    'O5_SUSPICIOUS'  = @('One or more suspicious indicators found -- review the results above.',
                          'Phát hiện một hoặc nhiều dấu hiệu đáng ngờ -- xem xét kết quả ở trên.')
    'O5_SUSP_COUNT'  = @('Total indicators flagged: ', 'Tổng số dấu hiệu bị đánh dấu: ')
    'O5_CLEAN'       = @('No 3rd-party activation indicators detected -- system appears clean.',
                          'Không phát hiện dấu hiệu kích hoạt bên thứ ba -- hệ thống có vẻ sạch.')
    'O5_CUSTOM_LOADED' = @('Custom scan lists loaded from: ', 'Danh sách quét tùy chỉnh đã tải từ: ')
    # Legal notice (mirror GUI P7_Legal*)
    'O5_LEGAL_HDR'   = @('ACTIVATION AUDIT -- LEGAL NOTICE', 'KIỂM TRA KÍCH HOẠT -- THÔNG BÁO PHÁP LÝ')
    'O5_LEGAL1'      = @('Using Windows without a genuine license purchased from Microsoft or an',
                          'Sử dụng Windows không có bản quyền chính hãng mua từ Microsoft hoặc')
    'O5_LEGAL2'      = @('authorized reseller violates Microsoft Terms of Service (EULA SS4).',
                          'đại lý được ủy quyền vi phạm Điều khoản dịch vụ của Microsoft (EULA §4).')
    'O5_LEGAL3'      = @('Enterprise / OEM users: verify licensing with your IT department or OEM.',
                          'Người dùng doanh nghiệp / OEM: xác minh bản quyền với bộ phận IT hoặc OEM.')
    'O5_LEGAL4'      = @('Check your genuine license status: https://aka.ms/MyAccount',
                          'Kiểm tra trạng thái bản quyền chính hãng: https://aka.ms/MyAccount')
    'O5_LEGAL_LIM'   = @('SCAN LIMITATIONS: HWID via MAS creates a real MS digital license (undetectable).',
                          'GIỚI HẠN QUÉT: HWID qua MAS tạo bản quyền MS thật (không phát hiện được).')
    'O5_LEGAL_LIM2'  = @('Any tool removed after use leaves no trace. Corporate KMS may trigger GVLK warnings.',
                          'Công cụ bị gỡ sau khi dùng không để lại dấu vết. KMS doanh nghiệp có thể kích hoạt cảnh báo GVLK.')

    # =========================================================================
    # Option U -- Update Scan Defaults
    # =========================================================================
    'OU_OPT_HDR'    = @('UPDATE SCAN DEFAULTS', 'CẬP NHẬT MẶC ĐỊNH QUÉT')
    'OU_INFO1'      = @('Downloads the latest settings.default.ini from the WinLic GitHub repository.',
                         'Tải xuống settings.default.ini mới nhất từ kho GitHub WinLic.')
    'OU_INFO2'      = @('Your user-added entries (ExtraPorts, ExtraServices, etc.) are preserved.',
                         'Các mục bạn đã thêm (ExtraPorts, ExtraServices, v.v.) được giữ lại.')
    'OU_INFO3'      = @('Source: https://raw.githubusercontent.com/ardennguyen/WinLic/main/WinLicPS/settings.default.ini',
                         'Nguồn: https://raw.githubusercontent.com/ardennguyen/WinLic/main/WinLicPS/settings.default.ini')
    'OU_NO_NET1'    = @('No internet connection detected -- cannot reach GitHub.', 'Không phát hiện kết nối internet -- không thể kết nối GitHub.')
    'OU_NO_NET2'    = @('Please check your network and try again.', 'Vui lòng kiểm tra mạng và thử lại.')
    'OU_DOWNLOADING' = @('Downloading latest defaults from GitHub...', 'Đang tải xuống mặc định mới nhất từ GitHub...')
    'OU_DL_FAIL'    = @('Download failed: ', 'Tải xuống thất bại: ')
    'OU_SUCCESS'    = @('settings.ini updated successfully!', 'Đã cập nhật settings.ini thành công!')
    'OU_FILE'       = @('File:', 'Tệp:')
    'OU_TIMESTAMP'  = @('Timestamp:', 'Dấu thời gian:')
    'OU_WRITE_FAIL' = @('Could not write settings.ini: ', 'Không thể ghi settings.ini: ')

    # =========================================================================
    # Resolve-KmsHost (DNS check helper)
    # =========================================================================
    'DNS_CHECKING'    = @('Checking internet and attempting DNS resolution...',
                           'Đang kiểm tra kết nối internet và phân giải tên miền qua DNS...')
    'DNS_NO_NET'      = @('No internet access -- DNS verification skipped.',
                           'Không có kết nối internet -- bỏ qua bước xác minh DNS.')
    'DNS_RESOLVES'    = @('Resolves to: ', 'Phân giải thành: ')
    'DNS_PUBLIC'      = @('Confirmed active: domain resolves to a public IP address.',
                           'Xác nhận đang hoạt động: tên miền phân giải thành địa chỉ IP công cộng.')
    'DNS_PUB_IPS'     = @('  Public IP(s): ', '  IP công cộng: ')
    'DNS_PRIVATE'     = @('Resolves to a private/internal IP -- unusual for a cloud KMS domain.',
                           'Phân giải thành IP nội bộ -- bất thường với tên miền cloud KMS.')
    'DNS_NO_RESOLVE'  = @('Cannot resolve host -- service may be offline or DNS-blocked.',
                           'Không thể phân giải tên miền -- dịch vụ có thể ngoại tuyến hoặc bị chặn DNS.')

    # =========================================================================
    # Option 6 -- KMS Activation
    # =========================================================================
    'O8KMS_OPT_HDR'    = @('Option 8 -- KMS Activation', 'Tùy chọn 6 -- Kích Hoạt KMS')
    'O8KMS_DESC1'      = @('KMS (Key Management Service) is an enterprise volume-licensing activation channel.',
                            'KMS (Dịch Vụ Quản Lý Key) là kênh kích hoạt cấp phép doanh nghiệp.')
    'O8KMS_DESC2'      = @('Requirements: GVLK key installed, KMS host reachable on TCP 1688, DNS SRV (_VLMCS._TCP) or manual host, clock within 4 h of KMS host.',
                            'Yêu cầu: Đã cài key GVLK, KMS host truy cập được qua TCP 1688, DNS SRV (_VLMCS._TCP) hoặc nhập thủ công, đồng hồ lệch tối đa 4 giờ.')
    'O8KMS_DESC3'      = @('KMS activation expires after 180 days and renews automatically every 7 days while on the corporate network.',
                            'Kích hoạt KMS hết hạn sau 180 ngày và tự động gia hạn mỗi 7 ngày khi kết nối mạng nội bộ.')
    'O8KMS_WARN_NOTVOLUME' = @('[!] Current channel is not VOLUME_KMSCLIENT. A GVLK must be installed first.',
                                '[!] Kênh hiện tại không phải VOLUME_KMSCLIENT. Cần cài key GVLK trước.')
    'O8KMS_WARN_KMSHOST'   = @('[!] This machine is a KMS HOST (VOLUME_KMS channel) -- not a KMS client. This option targets KMS clients only.',
                                '[!] Máy này là KMS HOST (kênh VOLUME_KMS) -- không phải KMS client. Tùy chọn này chỉ dành cho KMS client.')
    'O8KMS_CHK_CHANNEL'   = @('[1] Checking activation channel...',   '[1] Kiểm tra kênh kích hoạt...')
    'O8KMS_CHK_GVLK'     = @('[2] Checking installed key (GVLK)...',  '[2] Kiểm tra key đã cài (GVLK)...')
    'O8KMS_CHK_DNS'      = @('[3] Searching for KMS host via DNS SRV (_VLMCS._TCP)...', '[3] Tìm KMS host qua DNS SRV (_VLMCS._TCP)...')
    'O8KMS_CHK_PORT'     = @('[4] Testing connection to KMS host on TCP 1688...',        '[4] Kiểm tra kết nối đến KMS host trên TCP 1688...')
    'O8KMS_CHK_CLOCK'    = @('[5] Note: system clock must be within 4 hours of KMS host.', '[5] Lưu ý: đồng hồ hệ thống phải lệch tối đa 4 giờ so với KMS host.')
    'O8KMS_CHK_ACTIVATE' = @('[6] Activating via slmgr /ato...',      '[6] Kích hoạt qua slmgr /ato...')
    'O8KMS_GVLK_OK'      = @('[OK] GVLK detected: ',                  '[OK] Đã phát hiện GVLK: ')
    'O8KMS_GVLK_MISSING' = @('[!] Installed key is not a GVLK. KMS activation will fail without a valid GVLK.',
                              '[!] Key đã cài không phải GVLK. Kích hoạt KMS sẽ thất bại nếu không có GVLK hợp lệ.')
    'O8KMS_GVLK_FOUND'   = @('[?] GVLK found for this edition:',      '[?] GVLK tìm thấy cho ấn bản này:')
    'O8KMS_GVLK_CONFIRM' = @('  Type OK (then Enter) to install this GVLK, or press Enter to cancel: ',
                              '  Nhập OK (rồi Enter) để cài GVLK này, hoặc nhấn Enter để hủy: ')
    'O8KMS_GVLK_INSTALL' = @('Installing GVLK...',                    'Đang cài GVLK...')
    'O8KMS_GVLK_DONE'    = @('[OK] GVLK installed. Proceeding with KMS activation.',
                              '[OK] Đã cài GVLK. Tiếp tục kích hoạt KMS.')
    'O8KMS_GVLK_NOMAP'   = @('[!] No GVLK found for this edition. Contact your IT administrator.',
                              '[!] Không tìm thấy GVLK cho ấn bản này. Liên hệ quản trị viên CNTT.')
    'O8KMS_GVLK_CANCELED' = @('GVLK installation canceled.', 'Đã hủy cài đặt GVLK.')
    'O8KMS_DNS_FOUND'    = @('[OK] KMS host found via DNS: ',          '[OK] Đã tìm thấy KMS host qua DNS: ')
    'O8KMS_DNS_FAIL'     = @('[!] DNS auto-discovery failed. No _VLMCS._TCP SRV record found.',
                              '[!] Tìm kiếm DNS tự động thất bại. Không tìm thấy bản ghi SRV _VLMCS._TCP.')
    'O8KMS_MANUAL_PROMPT' = @('  Enter KMS server hostname or IP (or press Enter to cancel): ',
                               '  Nhập hostname hoặc IP của KMS server (hoặc nhấn Enter để hủy): ')
    'O8KMS_SKMS_PERSIST' = @('TCP 1688 reachable -- setting KMS server permanently (slmgr /skms).',
                              'TCP 1688 truy cập được -- đặt KMS server vĩnh viễn (slmgr /skms).')
    'O8KMS_SKMS_NOPERSIST' = @('[!] TCP 1688 unreachable -- KMS server NOT persisted. Try again when server is reachable.',
                                '[!] TCP 1688 không truy cập được -- KMS server KHÔNG được lưu. Thử lại khi server truy cập được.')
    'O8KMS_PORT_OK'      = @('[OK] TCP 1688 reachable on ',            '[OK] TCP 1688 truy cập được trên ')
    'O8KMS_PORT_FAIL'    = @('[!] TCP 1688 unreachable on ',            '[!] TCP 1688 không truy cập được trên ')
    'O8KMS_CLOCK_WARN'   = @('[i] Could not verify clock sync. If activation fails with 0xC004F06C, check your system time.',
                              '[i] Không thể xác minh đồng bộ đồng hồ. Nếu thất bại với 0xC004F06C, hãy kiểm tra đồng hồ hệ thống.')
    'O8KMS_SUCCESS'      = @('[OK] KMS activation succeeded!',         '[OK] Kích hoạt KMS thành công!')
    'O8KMS_SUCCESS2'     = @('Activation valid for 180 days; renews automatically every 7 days on the corporate network.',
                              'Kích hoạt có hiệu lực 180 ngày; tự động gia hạn mỗi 7 ngày khi kết nối mạng nội bộ.')
    'O8KMS_FAIL'         = @('[FAIL] KMS activation failed.',           '[THẤT BẠI] Kích hoạt KMS thất bại.')
    'O8KMS_DIAG_COUNT'   = @('Diagnosis: KMS Count Insufficient (0xC004F038) -- pool needs 25+ Windows client or 5+ Server activations.',
                              'Chẩn đoán: Số Lượng KMS Không Đủ (0xC004F038) -- pool cần 25+ client hoặc 5+ Server.')
    'O8KMS_DIAG_NOTENABLED' = @('Diagnosis: KMS Host Not Responding (0xC004F039) -- host not enabled or firewall blocks TCP 1688.',
                                 'Chẩn đoán: KMS Host Không Phản Hồi (0xC004F039) -- host không hoạt động hoặc tường lửa chặn TCP 1688.')
    'O8KMS_DIAG_HOSTNACT' = @('Diagnosis: KMS Host Not Activated (0xC004F041) -- the KMS host itself must be activated first.',
                               'Chẩn đoán: KMS Host Chưa Kích Hoạt (0xC004F041) -- bản thân KMS host cần được kích hoạt trước.')
    'O8KMS_DIAG_WRONGHOST' = @('Diagnosis: Wrong KMS Host (0xC004F042) -- host cannot activate this product/edition.',
                                'Chẩn đoán: KMS Host Sai (0xC004F042) -- host không thể kích hoạt sản phẩm/ấn bản này.')
    'O8KMS_DIAG_CLOCK'   = @('Diagnosis: Clock Skew (0xC004F06C) -- system time > 4 hours from KMS host. Sync via NTP.',
                              'Chẩn đoán: Lệch Đồng Hồ (0xC004F06C) -- thời gian lệch > 4 giờ. Đồng bộ qua NTP.')
    'O8KMS_DIAG_NOCONTACT' = @('Diagnosis: No KMS Reachable (0xC004F074) -- all KMS hosts returned errors. Check DNS, network, and Event Log ID 12288.',
                                'Chẩn đoán: Không Liên Hệ Được KMS (0xC004F074) -- tất cả host đều lỗi. Kiểm tra DNS, mạng, Event ID 12288.')
    'O8KMS_DIAG_DNS'     = @('Diagnosis: DNS Name Not Found (0x8007007B / 0x8007232B / 0x8007251D) -- KMS SRV missing in DNS or no KMS host installed.',
                              'Chẩn đoán: Không Tìm Thấy Tên DNS (0x8007007B / 0x8007232B / 0x8007251D) -- thiếu bản ghi SRV KMS hoặc chưa cài KMS host.')
    'O8KMS_DIAG_VOLK'    = @('Diagnosis: Invalid Volume License Key (0xC004F035) -- installed key is not a valid GVLK. Install the correct GVLK first.',
                              'Chẩn đoán: Key Bản Quyền Không Hợp Lệ (0xC004F035) -- key đã cài không phải GVLK hợp lệ. Cài GVLK đúng trước.')
    'O8KMS_CANCELED'     = @('KMS activation canceled.', 'Đã hủy kích hoạt KMS.')
    'O8KMS_REF_URL'      = @('Reference: https://learn.microsoft.com/en-us/troubleshoot/windows-server/licensing-and-activation/troubleshoot-activation-error-codes',
                              'Tham khảo: https://learn.microsoft.com/en-us/troubleshoot/windows-server/licensing-and-activation/troubleshoot-activation-error-codes')

    # =========================================================================
    # Option 6 -- Change Activation Channel
    # =========================================================================
    'O6CH_OPT_HDR'         = @('Option 6 -- Change Activation Channel',
                                'Tùy chọn 6 -- Thay Đổi Kênh Kích Hoạt')
    'O6CH_DESC'            = @('Switch your Windows licensing channel. To KMS: installs the GVLK for your edition. To RETAIL/MAK: redirects to Option 2.',
                                'Chuyển đổi kênh bản quyền Windows. Sang KMS: cài GVLK cho ấn bản. Sang RETAIL/MAK: chuyển đến Tùy chọn 2.')
    'O6CH_CURRENT_CHANNEL' = @('Current channel:',                'Kênh hiện tại:')
    'O6CH_CURRENT_KEY'     = @('Current partial key:',            'Key một phần hiện tại:')
    'O6CH_CURRENT_EDITION' = @('Edition:',                        'Ấn bản:')
    'O6CH_TO_KMS'          = @('[A] Switch to VOLUME_KMSCLIENT (KMS)',  '[A] Chuyển sang VOLUME_KMSCLIENT (KMS)')
    'O6CH_TO_RETAIL'       = @('[B] Switch to RETAIL/MAK',             '[B] Chuyển sang RETAIL/MAK')
    'O6CH_GVLK_LABEL'      = @('GVLK for this edition:',               'GVLK cho ấn bản này:')
    'O6CH_GVLK_INSTALLING' = @('Installing GVLK...',                   'Đang cài GVLK...')
    'O6CH_GVLK_DONE'       = @('[OK] GVLK installed. Run Option 8 to complete KMS activation.',
                                '[OK] Đã cài GVLK. Dùng Tùy chọn 8 để hoàn tất kích hoạt KMS.')
    'O6CH_GVLK_NOMAP'      = @('[!] No GVLK found for this edition. Contact your IT administrator.',
                                '[!] Không tìm thấy GVLK cho ấn bản này. Liên hệ quản trị viên IT.')
    'O6CH_RETAIL_MSG'      = @('To switch to RETAIL or MAK, run Option 2 -- Test & Install New Key.',
                                'Để chuyển sang RETAIL hoặc MAK, dùng Tùy chọn 2 -- Kiểm thử & Cài Key Mới.')
    'O6CH_HOST_WARN'       = @('[!] This machine is a KMS HOST (VOLUME_KMS). This tool targets KMS clients only.',
                                '[!] Máy này là KMS HOST (kênh VOLUME_KMS). Công cụ này chỉ dành cho KMS client.')
    'O6CH_ALREADY_KMS'     = @('[i] Already on VOLUME_KMSCLIENT channel.',  '[i] Đã ở kênh VOLUME_KMSCLIENT.')
    'O6CH_CANCELLED'       = @('Channel change cancelled.',               'Đã hủy thay đổi kênh.')

    # =========================================================================
    # Option 7 -- Check & Remove KMS Settings
    # =========================================================================
    'O7KMS_OPT_HDR'        = @('Option 7 -- Check & Remove KMS Settings',
                                'Tùy chọn 7 -- Kiểm Tra & Xóa Cài Đặt KMS')
    'O7KMS_DESC'           = @('View configured KMS server settings and optionally clear them to restore DNS auto-discovery.',
                                'Xem cài đặt KMS server đang được cấu hình và tùy chọn xóa để khôi phục tự động phát hiện DNS.')
    'O7KMS_REG_HOST'       = @('KMS Host (registry):',                    'KMS Host (registry):')
    'O7KMS_REG_PORT'       = @('KMS Port (registry):',                    'KMS Port (registry):')
    'O7KMS_DLV_HOST'       = @('KMS Host (slmgr /dlv):',                 'KMS Host (slmgr /dlv):')
    'O7KMS_NOT_SET'        = @('not set',                                 'chưa đặt')
    'O7KMS_DEFAULT_PORT'   = @('default (1688)',                          'mặc định (1688)')
    'O7KMS_NONE_ACTIVE'    = @('[i] No custom KMS server configured. DNS auto-discovery is active.',
                                '[i] Chưa có KMS server tùy chỉnh. Tự động phát hiện DNS đang hoạt động.')
    'O7KMS_CUSTOM_ACTIVE'  = @('[!] A custom KMS server is currently configured.',
                                '[!] Đang có KMS server tùy chỉnh được cấu hình.')
    'O7KMS_CLEAR_CONFIRM'  = @('  Type OK (then Enter) to clear KMS server setting, or press Enter to skip: ',
                                '  Nhập OK (rồi Enter) để xóa cài đặt KMS server, hoặc nhấn Enter để bỏ qua: ')
    'O7KMS_CLEARING'       = @('Clearing KMS server setting (slmgr /ckms)...',
                                'Đang xóa cài đặt KMS server (slmgr /ckms)...')
    'O7KMS_CLEARED'        = @('[OK] KMS server setting cleared. Windows will use DNS auto-discovery.',
                                '[OK] Đã xóa cài đặt KMS server. Windows sẽ dùng tự động phát hiện DNS.')
    'O7KMS_CLEAR_FAILED'   = @('[FAIL] Failed to clear KMS server setting.',
                                '[THẤT BẠI] Không thể xóa cài đặt KMS server.')
    'O7KMS_NEXT_OPT8'      = @('  -> Use Option 8 to activate with a new KMS host',
                                '  -> Dùng Tùy chọn 8 để kích hoạt với KMS host mới')
    'O7KMS_NEXT_OPT6'      = @('  -> Use Option 6 to change your activation channel',
                                '  -> Dùng Tùy chọn 6 để thay đổi kênh kích hoạt')
    'O7KMS_NEXT_DNS'       = @('  -> Windows will attempt DNS SRV auto-discovery on next activation',
                                '  -> Windows sẽ thử tự động phát hiện DNS SRV trong lần kích hoạt tiếp theo')
    'O7KMS_CANCELLED'      = @('No changes made.',                        'Không có thay đổi nào.')
}




# T() -- look up a string in the current language

function T {

    param([string]$key)

    if (-not $Str.ContainsKey($key)) { return $key }

    $pair = $Str[$key]

    if ($script:Lang -eq 'VI' -and $pair[1]) { return $pair[1] }

    return $pair[0]

}



# Select-Language -- one-time bilingual prompt before first menu

function Select-Language {

    Write-Sep2

    Write-Host ('  WinLic Manager  ' + $SCRIPT_VERSION) -ForegroundColor Magenta

    Write-Sep2

    Write-Host ''

    Write-Host '  Select language / Chọn ngôn ngữ:' -ForegroundColor White

    Write-Host '    [1]  English' -ForegroundColor Cyan

    Write-Host '    [2]  Tiếng Việt' -ForegroundColor Cyan

    Write-Host ''

    $ans = (Read-Host '  Language / Ngôn ngữ (1/2)').Trim()

    if ($ans -eq '2') {

        $script:Lang = 'VI'

        Write-Host '  Đã chọn: Tiếng Việt.' -ForegroundColor Green

    } else {

        $script:Lang = 'EN'

        Write-Host '  Language set to English.' -ForegroundColor Green

    }
    Write-Host ''
}

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

# Augment $genericKeys from settings.ini [GenericKeys] and [UserGenericKeys]
# Falls back to hardcoded table above if settings.ini is missing or empty.
$_gkLines = @(
    (Read-IniSection -Path $SETTINGS_FILE -Section 'GenericKeys') +
    (Read-IniSection -Path $SETTINGS_FILE -Section 'UserGenericKeys')
) | Where-Object { $_ }
foreach ($_gkLine in $_gkLines) {
    # Format: FULL-KEY = Description  (or bare key)
    $keyPart = if ($_gkLine -match '=') { ($_gkLine -split '=')[0].Trim() } else { $_gkLine.Trim() }
    $alnum   = $keyPart -replace "[^A-Za-z0-9]", ''
    if ($alnum.Length -ge 5) {
        $suffix = $alnum.Substring($alnum.Length - 5).ToUpper()
        $desc   = if ($_gkLine -match '=') { ($_gkLine -split '=', 2)[1].Trim() } else { 'Custom generic key' }
        if (-not $genericKeys.ContainsKey($suffix)) {
            $genericKeys[$suffix] = $desc
        }
    }
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
        $alnum   = $keyPart -replace "[^A-Za-z0-9]",''
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
# Write-Info: bright Cyan matches GUI ColInfo (#2563eb)
function Write-Info  { param([string]$msg) Write-Host "    $msg" -ForegroundColor Cyan }
# Write-Diag: DarkYellow matches GUI ColDiag (#92400e) -- diagnostic/context detail
function Write-Diag  { param([string]$msg) Write-Host "    $msg" -ForegroundColor DarkYellow }
function Write-Cmd   { param([string]$msg) Write-Host "  CMD: $msg" -ForegroundColor DarkGray }
# Write-Data: optional 3rd param sets value color (matches GUI LogData label=Gray / value=data color)
function Write-Data  {
    param([string]$label, [string]$value, [string]$color = 'White')
    $labelPart = ("    {0,-28} " -f $label)
    Write-Host $labelPart -NoNewline -ForegroundColor DarkGray
    Write-Host $value -ForegroundColor $color
}
# Write-Key: teal/cyan for key values -- mirrors GUI LogKey (ColKey #0e7490, bold, 🔑 prefix)
function Write-Key   { param([string]$msg) Write-Host "  [KEY] $msg" -ForegroundColor Cyan }
# Write-DE: Magenta for Digital Entitlement -- mirrors GUI LogDE (ColDE #7c3aed, bold, 💡 prefix)
function Write-DE    { param([string]$msg) Write-Host "  [DE] $msg" -ForegroundColor Magenta }

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
    Write-Info (T 'DNS_CHECKING')
    if (-not (Test-Internet)) {
        Write-Info (T 'DNS_NO_NET')
        return
    }
    try {
        $ips = [System.Net.Dns]::GetHostAddresses($Host) | ForEach-Object { $_.ToString() }
        Write-Info ((T 'DNS_RESOLVES') + ($ips -join ', '))
        $publicIps = $ips | Where-Object {
            $_ -notmatch '^(10\.|172\.(1[6-9]|2[0-9]|3[01])\.|192\.168\.|127\.|::1)'
        }
        if ($publicIps) {
            Write-Fail (T 'DNS_PUBLIC')
            Write-Fail ((T 'DNS_PUB_IPS') + ($publicIps -join ', '))
        } else {
            Write-Warn (T 'DNS_PRIVATE')
        }
    } catch {
        Write-Warn (T 'DNS_NO_RESOLVE')
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
    Write-Warn (T 'ELEVATE_WARN')
    Write-Info (T 'ELEVATE_ASK')
    Write-Info (T 'ELEVATE_NOTE')
    Write-Blank
    if (Ask-YesNo (T 'ELEVATE_DO')) {
        try {
            $args = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
            if ($wtExe) {
                # Open elevated tab in Windows Terminal
                Start-Process $wtExe `
                    -ArgumentList "new-tab $shellExe $args" `
                    -Verb RunAs
            } else {
                Start-Process $shellExe `
                    -ArgumentList $args `
                    -Verb RunAs
            }
            Exit
        } catch {
            Write-Fail ((T 'ELEVATE_FAIL') + $_)
            Write-Fail (T 'ELEVATE_HINT')
        }
    } else {
        Write-Warn (T 'CANCEL_BACK')
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
        Write-Host (T 'HDR_ADMIN') -ForegroundColor Green
    } else {
        Write-Host "  STATUS  " -NoNewline
        Write-Host (T 'HDR_NOADMIN') -ForegroundColor Yellow
    }
    Write-Sep
}

function Show-Menu {
    Write-Blank
    Write-Host ("  " + (T 'MENU_1')) -ForegroundColor White
    Write-Host ("  " + (T 'MENU_2')) -ForegroundColor Red
    Write-Host ("  " + (T 'MENU_3')) -ForegroundColor Red
    Write-Host ("  " + (T 'MENU_4')) -ForegroundColor Red
    Write-Host ("  " + (T 'MENU_5')) -ForegroundColor White
    Write-Host ("  " + (T 'MENU_6')) -ForegroundColor Red
    Write-Host ("  " + (T 'MENU_7')) -ForegroundColor White
    Write-Host ("  " + (T 'MENU_8')) -ForegroundColor Red
    Write-Host ("  " + (T 'MENU_U')) -ForegroundColor Cyan
    Write-Host ("  " + (T 'MENU_Q')) -ForegroundColor DarkGray
    Write-Blank
    Write-Host ("  " + (T 'MENU_WARN')) -ForegroundColor DarkRed
    Write-Sep
}

function Show-About {
    Write-Blank
    Write-Host ('  ' + (T 'ABOUT_HDR')) -ForegroundColor White
    Write-Host "  WinLic Manager $SCRIPT_VERSION -- Windows Licensing & Information Manager" -ForegroundColor Cyan
    Write-Host '  Author : Arden Nguyen Duc Huy' -ForegroundColor Cyan
    Write-Host '  Repo   : https://github.com/ardennguyen/WinLic' -ForegroundColor Cyan
    Write-Blank
    Write-Host ('  ' + (T 'ABOUT_OPT1')) -ForegroundColor Gray
    Write-Host ('  ' + (T 'ABOUT_OPT234')) -ForegroundColor DarkRed
    Write-Host ('  ' + (T 'ABOUT_ADMIN')) -ForegroundColor DarkRed
    Write-Sep
}

# =============================================================================
# License status helper
# =============================================================================
function Get-LicenseStatusText {
    param([int]$status)
    switch ($status) {
        0 { return (T 'O1_LS_0') }
        1 { return (T 'O1_LS_1') }
        2 { return (T 'O1_LS_2') }
        3 { return (T 'O1_LS_3') }
        4 { return (T 'O1_LS_4') }
        5 { return (T 'O1_LS_5') }
        6 { return (T 'O1_LS_6') }
        default { return ((T 'O1_LS_UNK') + " ($status)") }
    }
}

# =============================================================================
# OPTION 1 -- Full System & License Info
# (Merged: OS Version, License Channel /dli, Keys & Activation Status, optional /dlv)
# =============================================================================
function Get-VersionInfo {
    Write-Blank
    Write-Host ("  " + (T 'O1_OPT_HDR')) -ForegroundColor Magenta
    Write-Sep
    Write-Diag (T 'O1_OPT_DESC1')
    Write-Diag (T 'O1_OPT_DESC2')
    Write-Sep
    Write-Blank

    # ── 1a. OS Version ─────────────────────────────────────────────────────
    Write-Step (T 'O1_STEP_OS')
    Write-Cmd  "Get-CimInstance Win32_OperatingSystem"
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        Write-Blank
        Write-Data (T 'O1_LBL_OS')    $os.Caption
        Write-Data (T 'O1_LBL_VER')   $os.Version
        Write-Data (T 'O1_LBL_BUILD') $os.BuildNumber
        Write-Data (T 'O1_LBL_ARCH')  $os.OSArchitecture
    } catch {
        Write-Fail ((T 'O1_WMI_FAIL') + $_)
        Start-Process "winver"
        return
    }

    Write-Blank
    Write-Sep

    # ── 1b. BIOS OEM Key ───────────────────────────────────────────────────
    Write-Step (T 'O1_STEP_BIOS')
    Write-Cmd  "Get-CimInstance SoftwareLicensingService | Select OA3xOriginalProductKey"
    $oemKey = (Get-CimInstance -ClassName SoftwareLicensingService).OA3xOriginalProductKey
    Write-Blank

    if ($oemKey) {
        Write-OK (T 'O1_BIOS_DETECT')
        $display = if (Ask-YesNo (T 'O1_SHOWBIOS')) { $oemKey } else { Mask-Key $oemKey }
        Write-Data (T 'O1_LBL_BIOS') $display 'Cyan'
        $last5 = $oemKey.Substring($oemKey.Length - 5)
        if ($genericKeys.ContainsKey($last5)) {
            Write-Blank
            Write-OK ((T 'O1_ED_MATCH') + "  " + $genericKeys[$last5])
        }
    } else {
        Write-Warn (T 'O1_BIOS_NONE')
        Write-Diag (T 'O1_NOACT_NOTE')
    }

    Write-Blank
    Write-Sep

    # ── 1c. Active License (WMI) ───────────────────────────────────────────
    Write-Step (T 'O1_STEP_LIC')
    Write-Cmd  "Get-CimInstance SoftwareLicensingProduct | Where PartialProductKey and Name like Windows*"
    Write-Blank

    $regKeyPath   = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform"
    $activeProduct = Get-CimInstance -ClassName SoftwareLicensingProduct |
                     Where-Object { $_.PartialProductKey -and $_.Name -like "Windows*" }
    $partialKey   = $null

    if ($activeProduct) {
        $partialKey = $activeProduct.PartialProductKey
        try { Write-Data (T 'O1_LBL_OS') (Get-CimInstance Win32_OperatingSystem).Caption } catch {}
        Write-Data (T 'O1_LBL_NAME')    $activeProduct.Name
        Write-Data (T 'O1_LBL_CHAN')    $activeProduct.Description
        Write-Data (T 'O1_LBL_PARTIAL') $partialKey 'Cyan'

        $statusText  = Get-LicenseStatusText $activeProduct.LicenseStatus
        $statusColor = if ($activeProduct.LicenseStatus -eq 1) { 'Green' } else { 'Red' }
        Write-Data (T 'O1_LBL_ACT') $statusText $statusColor

        Write-Blank
        $isVolume = $activeProduct.Description -match 'VOLUME_KMSCLIENT'
        if ($genericKeys.ContainsKey($partialKey) -and $activeProduct.LicenseStatus -eq 1 -and -not $isVolume) {
            Write-DE   (T 'O1_DE_OK')
            Write-Diag ("Channel: " + $activeProduct.Description)
            Write-Diag (T 'O1_DE_1')
            Write-Diag (T 'O1_DE_2')
            Write-Diag (T 'O1_DE_3')
            Write-Diag (T 'O1_DE_VFY')
        } elseif ($activeProduct.Description -match 'VOLUME_KMSCLIENT') {
            Write-OK  (T 'O1_KMS_OK')
            $kmsHost = $activeProduct.KeyManagementServiceMachine
            if ($kmsHost) { Write-Data (T 'O1_LBL_KMS') $kmsHost 'Cyan' }
        } else {
            Write-OK  (T 'O1_MAK_OK')
        }
    } else {
        Write-Warn (T 'O1_NOACT')
    }

    Write-Blank
    Write-Sep

    # ── 1d. Registry & Installed Key ──────────────────────────────────────
    Write-Step (T 'O1_STEP_REG')
    $regKey = (Get-ItemProperty -Path $regKeyPath -Name "BackupProductKeyDefault" -ErrorAction SilentlyContinue).BackupProductKeyDefault
    if ($regKey) { Write-OK (T 'O1_REG_DETECT') } else { Write-Warn (T 'O1_REG_NONE') }

    Write-Step (T 'O1_STEP_INST')
    $installedKey = Get-InstalledProductKey
    if ($installedKey) { Write-OK (T 'O1_INST_OK') } else { Write-Warn (T 'O1_INST_NO') }

    # Display key values (optional)
    if ($oemKey -or $regKey -or $installedKey) {
        Write-Blank
        $showFull = Ask-YesNo (T 'O1_SHOWFULL')
        Write-Blank
        Write-Host ("  " + (T 'O1_KEYS_HDR')) -ForegroundColor Cyan
        if ($installedKey) {
            $val = if ($showFull) { $installedKey } else { Mask-Key $installedKey }
            Write-Key ((T 'O1_KEY_INST') + $val)
        }
        if ($oemKey) {
            $val = if ($showFull) { $oemKey } else { Mask-Key $oemKey }
            Write-Key ((T 'O1_KEY_BIOS') + $val)
        }
        if ($regKey) {
            $val = if ($showFull) { $regKey } else { Mask-Key $regKey }
            Write-Key ((T 'O1_KEY_REG') + $val)
        }
    }

    Write-Blank
    Write-Sep

    # Key mismatch check
    if ($regKey -and $partialKey -and -not $regKey.EndsWith($partialKey)) {
        Write-Warn (T 'O1_MISMATCH')
        Write-Data (T 'O1_ACTIVE_ENDS') $partialKey 'Yellow'
        Write-Data (T 'O1_BACKUP_ENDS') $regKey.Substring($regKey.Length - 5) 'Yellow'
        Write-Diag (T 'O1_STALE_NOTE')
        Write-Diag (T 'O1_ACTIVE_SAFE')
        Write-Blank
        if ($isAdmin) {
            if (Ask-YesNo (T 'O1_REMOVE_ASK')) {
                try {
                    Remove-ItemProperty -Path $regKeyPath -Name "BackupProductKeyDefault" -Force
                    Write-OK (T 'O1_REMOVE_OK')
                } catch { Write-Fail ((T 'O1_REMOVE_FAIL') + $_) }
            } else { Write-Warn (T 'O1_REMOVE_KEPT') }
        } else {
            Write-Warn (T 'O1_NEED_ADMIN')
        }
    } elseif ($regKey -and $partialKey) {
        Write-OK (T 'O1_KEY_MATCH')
    }

    Write-Blank
    Write-Sep

    # ── 1e. License Channel /dli ───────────────────────────────────────────
    Write-Blank
    Write-Host ("  " + (T 'O1_DLI_HDR')) -ForegroundColor Cyan
    Write-Diag (T 'O1_DLI_NOTE')
    $out = Run-Slmgr "/dli" (T 'O1_RUNNING_DLI')
    if ($out) {
        Write-Blank
        foreach ($line in $out) {
            $t = $line.Trim()
            if (-not $t) { continue }
            if ($t -match '^License Status:') { Write-Host ("  {0}" -f $t) -ForegroundColor Green }
            elseif ($t -match '^Name:|^Description:') { Write-Host ("  {0}" -f $t) -ForegroundColor Cyan }
            else { Write-Host ("  {0}" -f $t) }
        }
    } else {
        Write-Warn (T 'O1_NO_OUTPUT')
    }

    Write-Blank
    Write-Sep

    # ── 1f. Extended info /dlv (optional) ─────────────────────────────────
    Write-Blank
    Write-Host ("  " + (T 'O1_DLV_HDR')) -ForegroundColor Cyan
    Write-Diag (T 'O1_DLV_REVEAL')
    Write-Blank

    if (Ask-YesNo (T 'O1_DLV_ASK')) {
        $out = Run-Slmgr "/dlv" (T 'O1_RUNNING_DLV')
        if ($out) {
            Write-Blank
            foreach ($line in $out) {
                $t = $line.Trim()
                if (-not $t) { continue }
                if ($t -match '^License Status:') { Write-Host ("  {0}" -f $t) -ForegroundColor Green }
                elseif ($t -match '^Name:|^Description:|^SKU ID:') { Write-Host ("  {0}" -f $t) -ForegroundColor Cyan }
                else { Write-Host ("  {0}" -f $t) }
            }
        }
    }
    Write-Blank
}

# =============================================================================
# Get Installed Product Key (Decodes DigitalProductId)
# =============================================================================
function Get-InstalledProductKey {
    try {
        $dpId = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -Name "DigitalProductId" -ErrorAction SilentlyContinue).DigitalProductId
        if (-not $dpId -or $dpId.Length -lt 67) { return $null }
        $keyBytes = New-Object byte[] 15
        [Array]::Copy($dpId, 52, $keyBytes, 0, 15)
        $isWin8 = [math]::Truncate($keyBytes[14] / 6) -band 1
        $keyBytes[14] = ($keyBytes[14] -band 0xF7) -bor (($isWin8 -band 2) * 4)
        $chars = "BCDFGHJKMPQRTVWXY2346789"
        $decodedChars = New-Object char[] 25
        $last = 0
        for ($i = 24; $i -ge 0; $i--) {
            $current = 0
            for ($j = 14; $j -ge 0; $j--) {
                $current = ($current * 256) -bxor $keyBytes[$j]
                $keyBytes[$j] = [math]::Floor($current / 24)
                $current = $current % 24
            }
            $decodedChars[$i] = $chars[$current]
            $last = $current
        }
        $decodedKey = [string]::Join("", $decodedChars)
        if ($isWin8 -eq 1) {
            $part1 = $decodedKey.Substring(1, $last)
            $part2 = $decodedKey.Substring(1 + $last)
            $decodedKey = $part1 + "N" + $part2
        }
        for ($i = 5; $i -lt $decodedKey.Length; $i += 6) {
            $decodedKey = $decodedKey.Insert($i, "-")
        }
        return $decodedKey
    } catch { return $null }
}



# =============================================================================
# OPTION 2 -- Test & Install New Product Key
# =============================================================================
function Test-ProductKey {
    if (-not $isAdmin) { Elevate-For-Option; return }

    Write-Blank
    Write-Host ("  " + (T 'O2_OPT_HDR')) -ForegroundColor Magenta
    Write-Sep
    Write-Diag (T 'O2_DESC1')
    Write-Diag (T 'O2_DESC2')
    Write-Diag (T 'O2_DESC3')
    Write-Warn (T 'O2_WARN_OVERWRITE')
    Write-Sep
    Write-Blank

    # Show current active key for reference
    Write-Step (T 'O2_READ_LIC')
    $activeProduct = Get-CimInstance -ClassName SoftwareLicensingProduct |
                     Where-Object { $_.PartialProductKey -and $_.Name -like "Windows*" }
    if ($activeProduct) {
        Write-Data (T 'O2_CUR_ED')     $activeProduct.Name
        Write-Data (T 'O2_CUR_KEY')    $activeProduct.PartialProductKey 'Cyan'
        $statusText  = Get-LicenseStatusText $activeProduct.LicenseStatus
        $statusColor = if ($activeProduct.LicenseStatus -eq 1) { 'Green' } else { 'Yellow' }
        Write-Data (T 'O2_CUR_STATUS') $statusText $statusColor
    } else {
        Write-Warn (T 'O2_NO_LIC')
    }
    Write-Blank
    Write-Sep
    Write-Blank

    Write-Info (T 'O2_INFO1')
    Write-Info (T 'O2_INFO2')
    Write-Warn (T 'O2_WARN_OVERWRITE')

    # Channel awareness -- warn if VOLUME_KMSCLIENT, VOLUME_KMS, or Subscription
    if ($activeProduct) {
        $chanDesc = ($activeProduct.Description + '').ToUpperInvariant()
        if     ($chanDesc -match 'VOLUME_KMSCLIENT')                       { Write-Warn (T 'O2_WARN_CHAN_KMS') }
        elseif ($chanDesc -match 'VOLUME_KMS' -and $chanDesc -notmatch 'KMSCLIENT') { Write-Warn (T 'O2_WARN_CHAN_KMSHOST') }
        elseif ($chanDesc -match 'SUBSCRIPTION')                           { Write-Warn (T 'O2_WARN_CHAN_SUB') }
    }
    Write-Blank

    $rawKey = Read-Host (T 'O2_PROMPT')
    $key    = $rawKey.Trim().ToUpper()

    if ($key -notmatch '^[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}$') {
        Write-Fail (T 'O2_BADFMT')
        return
    }

    $displayKey = if (Ask-YesNo (T 'O2_SHOW_KEY')) { $key } else { Mask-Key $key }

    # Confirmation before installing — require typing OK
    Write-Blank
    Write-Sep
    Write-Warn (T 'O2_CONFIRM_HDR')
    Write-Host ((T 'O2_NEW_KEY') + $displayKey) -ForegroundColor Cyan
    if ($activeProduct) {
        Write-Host ((T 'O2_REPLACES') + $activeProduct.PartialProductKey + '  (' + $activeProduct.Name + ')') -ForegroundColor DarkYellow
    }
    Write-Warn (T 'O2_CONFIRM_WARN')
    Write-Sep
    Write-Blank

    $okInput = Read-Host (T 'O2_CONFIRM_TYPE_OK')
    if ($okInput.Trim().ToUpper() -ne 'OK') {
        Write-Warn (T 'O2_CONFIRM_BAD_INPUT')
        return
    }

    Write-Blank
    Write-Step (T 'O2_INSTALLING')
    Write-Cmd  "cscript //nologo `"$slmgrPath`" /ipk $displayKey"
    Write-Blank

    $out     = cscript //nologo $slmgrPath /ipk $key 2>&1
    $fullOut = [string]::Join(" ", $out)

    foreach ($line in $out) { Write-Host ("  {0}" -f $line.Trim()) }

    Write-Blank
    Write-Sep

    if ($fullOut -match 'Error:' -or $fullOut -match '0x') {
        Write-Fail (T 'O2_FAIL')
        if    ($fullOut -match '0xC004F069') {
            Write-Warn (T 'O2_DIAG_SKU')
        } elseif ($fullOut -match '0xC004F050') {
            Write-Warn (T 'O2_DIAG_INVALID')
        } elseif ($fullOut -match '0xC004C003') {
            Write-Warn (T 'O2_DIAG_BLOCKED')
        } else {
            Write-Warn (T 'O2_DIAG_GENERAL')
        }
        Write-Blank
        Write-Info (T 'O2_REF_URL')
    } else {
        Write-OK  (T 'O2_SUCCESS')
        Write-Info (T 'O2_SUCCESS2')

        # -- Auto /ato after successful /ipk --------------------------------
        Write-Blank
        Write-Step (T 'O2_ATO_AUTO')
        Write-Cmd  "cscript //nologo `"$slmgrPath`" /ato"
        $atoOut     = cscript //nologo $slmgrPath /ato 2>&1
        $atoFull    = [string]::Join(' ', $atoOut)
        foreach ($line in $atoOut) { Write-Host ("  {0}" -f $line.Trim()) }
        Write-Blank
        if ($atoFull -match 'Error:' -or $atoFull -match '0x') {
            Write-Fail (T 'O2_ATO_FAIL')
            if      ($atoFull -match '0x80070490') { Write-Warn (T 'O2_DIAG_DIDNTWORK') }
            elseif  ($atoFull -match '0xC004C001') { Write-Warn (T 'O2_DIAG_SERVER_INVALID') }
            elseif  ($atoFull -match '0xC004C020|0xC004C021') { Write-Warn (T 'O2_DIAG_MAK_LIMIT') }
            elseif  ($atoFull -match '0xC004B100') { Write-Warn (T 'O2_DIAG_SERVER_NOACT') }
            elseif  ($atoFull -match '0xC004F009') { Write-Warn (T 'O2_DIAG_GRACE') }
            elseif  ($atoFull -match '0x8004FE21') { Write-Warn (T 'O2_DIAG_NOTGENUINE') }
            Write-Blank
            Write-Info (T 'O8KMS_REF_URL')
        } else {
            Write-OK (T 'O2_ATO_SUCCESS')
        }
    }
    Write-Blank
}

# =============================================================================
# OPTION 3 -- Remove Activation
# =============================================================================
function Remove-License {
    if (-not $isAdmin) { Elevate-For-Option; return }

    Write-Blank
    Write-Host ("  " + (T 'O3_OPT_HDR')) -ForegroundColor Magenta
    Write-Sep
    Write-Warn (T 'O3_WARN1')
    Write-Warn (T 'O3_WARN2')
    Write-Sep
    Write-Blank

    if (-not (Ask-YesNo (T 'O3_CONFIRM'))) {
        Write-Warn (T 'O3_CANCEL')
        return
    }

    Write-Blank
    $out = Run-Slmgr "/upk" (T 'O3_UNINST')
    if ($out) { foreach ($l in $out) { Write-Host ("  {0}" -f $l.Trim()) } }

    Write-Blank
    $out = Run-Slmgr "/cpky" (T 'O3_CLEAR')
    if ($out) { foreach ($l in $out) { Write-Host ("  {0}" -f $l.Trim()) } }

    Write-Blank
    Write-OK (T 'O3_DONE')
    Write-Blank
}

# =============================================================================
# OPTION 4 -- Reset Activation (Rearm)
# =============================================================================
function Reset-Activation {
    if (-not $isAdmin) { Elevate-For-Option; return }

    Write-Blank
    Write-Host ("  " + (T 'O4_OPT_HDR')) -ForegroundColor Magenta
    Write-Sep
    Write-Warn (T 'O4_WARN1')
    Write-Warn (T 'O4_WARN2')
    Write-Sep
    Write-Blank

    if (-not (Ask-YesNo (T 'O4_CONFIRM'))) {
        Write-Warn (T 'O4_CANCEL')
        return
    }

    Write-Blank
    $out = Run-Slmgr "/rearm" (T 'O4_RUNNING_REARM')
    if ($out) { foreach ($l in $out) { Write-Host ("  {0}" -f $l.Trim()) } }

    Write-Blank
    Write-OK (T 'O4_DONE')
    Write-Blank

    if (Ask-YesNo (T 'O4_RESTART_ASK')) {
        Write-Warn (T 'O4_RESTARTING')
        Start-Sleep 5
        Restart-Computer -Force
    }
    Write-Blank
}

# =============================================================================
# OPTION 5 -- 3rd Party Activation Audit
# =============================================================================
function Invoke-ActivationAudit {
    Write-Blank
    Write-Host ("  " + (T 'O5_OPT_HDR')) -ForegroundColor Magenta
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

        $builtIn = T 'O5_CFG_BUILTIN'
        $custom  = T 'O5_CFG_CUSTOM'
        $none    = T 'O5_CFG_NONE'

        Write-Host ("  " + (T 'O5_CFG_TITLE')) -ForegroundColor White
        Write-Host ("  " + (T 'O5_CFG_FILE') + "  $SETTINGS_FILE  ") -NoNewline
        Write-Host $iniStatus -ForegroundColor $iniColor
        Write-Blank
        Write-Host ("  {0,-18} {1}" -f (T 'O5_CFG_PORTS'), ($lists.Ports -join ", ")) -ForegroundColor $(if ($extraPortCount) { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} $builtIn{2}" -f (T 'O5_CFG_SVC'),   $DEFAULT_SERVICES.Count,    $(if ($extraSvcCount)    { "  + $extraSvcCount $custom" }    else { "" })) -ForegroundColor $(if ($extraSvcCount)    { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} $builtIn{2}" -f (T 'O5_CFG_TASKS'),  $DEFAULT_TASKS.Count,       $(if ($extraTaskCount)   { "  + $extraTaskCount $custom" }   else { "" })) -ForegroundColor $(if ($extraTaskCount)   { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} $builtIn{2}" -f (T 'O5_CFG_PROCS'),  $DEFAULT_PROCS.Count,       $(if ($extraProcCount)   { "  + $extraProcCount $custom" }   else { "" })) -ForegroundColor $(if ($extraProcCount)   { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1}" -f (T 'O5_CFG_FILES'),   $(if ($extraFileCount) { "$extraFileCount $custom" } else { $none })) -ForegroundColor $(if ($extraFileCount) { 'Cyan' } else { 'Gray' })
        Write-Host ("  {0,-18} {1} $builtIn{2}" -f (T 'O5_CFG_DOMAINS'), $DEFAULT_KMS_DOMAINS.Count, $(if ($extraDomainCount) { "  + $extraDomainCount $custom" } else { "" })) -ForegroundColor $(if ($extraDomainCount) { 'Cyan' } else { 'Gray' })
        Write-Blank
        Write-Sep

        Write-Blank
        Write-Diag (T 'O5_EDIT_HINT1')
        Write-Diag (T 'O5_EDIT_HINT2')
        Write-Host "    $SETTINGS_FILE" -ForegroundColor DarkGray
        Write-Blank

        $editChoice = Read-Host (T 'O5_EDIT_ASK')
        if ($editChoice -match '^y(es)?$') {
            if (Test-Path $SETTINGS_FILE) {
                Write-Info (T 'O5_OPENING_INI')
                Start-Process notepad.exe -ArgumentList $SETTINGS_FILE -Wait
                Write-Step (T 'O5_RELOADING')
                Write-Blank
                # Loop back to show updated configuration
            } else {
                Write-Warn ((T 'O5_INI_NOT_FOUND') + $SETTINGS_FILE)
                Write-Info (T 'O5_INI_HINT1')
                Write-Info (T 'O5_INI_HINT2')
                Write-Blank
                Read-Host (T 'O5_INI_CONTINUE')
                break settingsLoop
            }
        } else {
            break settingsLoop
        }
    }

    Write-Blank
    Write-Sep

    # Preamble ----------------------------------------------------------------
    Write-Host ("  " + (T 'O5_SCAN_HDR')) -ForegroundColor Magenta
    Write-Diag (T 'O5_CAN1')
    Write-Diag (T 'O5_CAN2')
    Write-Diag (T 'O5_CAN3')
    Write-Diag (T 'O5_CAN4')
    Write-Diag (T 'O5_CAN5')
    Write-Diag (T 'O5_CAN6')
    Write-Diag (T 'O5_CAN7')
    Write-Diag (T 'O5_CAN8')
    Write-Diag (T 'O5_CAN9')
    Write-Blank
    Write-Host ("  " + (T 'O5_LIMIT_HDR')) -ForegroundColor DarkYellow
    Write-Diag (T 'O5_LIMIT1')
    Write-Diag (T 'O5_LIMIT2')
    Write-Diag (T 'O5_LIMIT3')
    Write-Diag (T 'O5_LIMIT4')
    Write-Sep
    Write-Blank

    $suspiciousCount = 0
    $criticalKms     = $false

    # (1) KMS server name -----------------------------------------------------
    Write-Step (T 'O5_STEP1')
    Write-Diag (T 'O5_KMS_EXP1')
    Write-Diag (T 'O5_KMS_EXP2')
    Write-Diag (T 'O5_KMS_EXP3')
    Write-Diag (T 'O5_KMS_EXP4')
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
        Write-Data (T 'O5_KMS_NAME') $kmsHost

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
            Write-Fail (T 'O5_KMS_LOCAL')
            Write-Fail (T 'O5_KMS_FAKE')
            $criticalKms = $true
            $suspiciousCount++
        } elseif ($isBogusPlaceholder) {
            Write-Fail (T 'O5_KMS_BOGUS')
            Write-Fail (T 'O5_KMS_BOGUS2')
            Write-Fail (T 'O5_KMS_BOGUS3')
            $criticalKms = $true
            $suspiciousCount++
        } elseif ($isKnownPiracy) {
            Write-Fail (T 'O5_KMS_PIRACY_KNOWN')
            Write-Fail ((T 'O5_KMS_PIRACY_SERVER') + $kmsHost)
            Write-Fail (T 'O5_KMS_PIRACY_NOTE')
            Write-Fail (T 'O5_KMS_PIRACY_NOT_MS')
            $criticalKms = $true
            $suspiciousCount++
            Resolve-KmsHost -Host $kmsHost
        } elseif ($isMsOfficial) {
            Write-OK  (T 'O5_KMS_MSOFF')
            Write-Warn (T 'O5_KMS_MSOFF2')
            Write-Warn (T 'O5_KMS_MSOFF3')
            Resolve-KmsHost -Host $kmsHost
        } elseif ($isPrivateIp -or $isPrivateHost) {
            Write-OK  (T 'O5_KMS_PRIV')
            Write-Info (T 'O5_KMS_PRIV2')
        } else {
            Write-Fail (T 'O5_KMS_CLOUD')
            Write-Fail ((T 'O5_KMS_PIRACY_SERVER') + $kmsHost)
            Write-Fail (T 'O5_KMS_CLOUD2')
            Write-Warn (T 'O5_KMS_CLOUD3')
            Write-Warn (T 'O5_KMS_CLOUD4')
            $criticalKms = $true
            $suspiciousCount++
            Resolve-KmsHost -Host $kmsHost
        }
    } else {
        Write-OK (T 'O5_KMS_NONE')
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
            Write-Warn ([string]::Format((T 'O5_OFF_KMS'), $officeKmsHost))
            $suspiciousCount++
        }
    }

    Write-Blank
    Write-Sep

    # (2) Port probe ----------------------------------------------------------
    Write-Step (T 'O5_STEP2')
    Write-Diag (T 'O5_PORT_EXP1')
    Write-Diag (T 'O5_PORT_EXP2')
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
            Write-Fail ([string]::Format((T 'O5_PORT_OPEN'), $port))
            $criticalKms = $true
            $suspiciousCount++
        } else {
            Write-OK  ([string]::Format((T 'O5_PORT_CLOSED'), $port))
        }
    }

    Write-Blank
    Write-Sep

    # (3) System services -----------------------------------------------------
    Write-Step (T 'O5_STEP3')
    Write-Diag (T 'O5_SVC_EXP')
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
        foreach ($s in $foundServices) { Write-Fail ((T 'O5_SVC_FOUND') + $s); $suspiciousCount++ }
    } else {
        Write-OK (T 'O5_SVC_NONE')
    }

    Write-Blank
    Write-Sep

    # (4) Scheduled tasks -----------------------------------------------------
    Write-Step (T 'O5_STEP4')
    Write-Diag (T 'O5_TASK_EXP1')
    Write-Diag (T 'O5_TASK_EXP2')
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
        foreach ($t in $foundTasks) { Write-Fail ((T 'O5_TASK_FOUND') + $t); $suspiciousCount++ }
    } else {
        Write-OK (T 'O5_TASK_NONE')
    }

    Write-Blank
    Write-Sep

    # (5) Known file / folder paths -------------------------------------------
    Write-Step (T 'O5_STEP5')
    Write-Diag (T 'O5_FILE_EXP')
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
        foreach ($f in $foundFiles) { Write-Warn ((T 'O5_FILE_FOUND') + $f); $suspiciousCount++ }
    } else {
        Write-OK (T 'O5_FILE_NONE')
    }

    Write-Blank
    Write-Sep

    # (6) Running processes ---------------------------------------------------
    Write-Step (T 'O5_STEP6')
    Write-Diag (T 'O5_PROC_EXP')
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
        foreach ($p in $foundProcs) { Write-Fail ((T 'O5_PROC_FOUND') + $p); $suspiciousCount++ }
    } else {
        Write-OK (T 'O5_PROC_NONE')
    }

    Write-Blank
    Write-Sep

    # (7) GVLK + Activation Channel check ----------------------------------------
    Write-Step (T 'O5_STEP7')
    Write-Diag (T 'O5_GVLK_EXP1')
    Write-Diag (T 'O5_GVLK_EXP2')
    Write-Diag (T 'O5_GVLK_EXP3')
    Write-Diag (T 'O5_GVLK_EXP4')
    if ($lists.GvlkSuffixes.Count -gt 0) {
        Write-Info ((T 'O5_GVLK_COUNT') + $lists.GvlkSuffixes.Count)
    } else {
        Write-Info (T 'O5_GVLK_NONE')
    }
    Write-Blank

    $wmiLicStatus = $null
    try {
        $wmiLicStatus = Get-CimInstance -ClassName SoftwareLicensingProduct |
            Where-Object { $_.PartialProductKey -and $_.ApplicationID -eq '55c92734-d682-4d71-983e-d6ec3f16059f' } |
            Select-Object -First 1
    } catch {}

    if ($wmiLicStatus) {
        $ppk        = $wmiLicStatus.PartialProductKey
        $licStatus  = $wmiLicStatus.LicenseStatus
        $graceMins  = $wmiLicStatus.GracePeriodRemaining
        $desc       = $wmiLicStatus.Description
        $pkChannel  = $wmiLicStatus.ProductKeyChannel  # more reliable channel field

        $isLicensed   = $licStatus -eq 1
        $isPermanent  = ($graceMins -eq 0 -or $graceMins -eq $null) -and $isLicensed
        # VOLUME_KMSCLIENT = KMS piracy candidate; RETAIL/OEM_DM = DE activation (legitimate)
        $isVolumeKms  = ($desc -match 'VOLUME_KMSCLIENT') -or ($pkChannel -match 'Volume')

        # Check GVLK suffix
        $isGvlk = $false
        if ($lists.GvlkSuffixes.Count -gt 0) {
            $isGvlk = $lists.GvlkSuffixes -contains $ppk.ToUpper()
        }
        # Also check against built-in genericKeys table
        if (-not $isGvlk -and $genericKeys.ContainsKey($ppk)) {
            $isGvlk = $true
        }

        # 7a. Phone activation anomaly (TSforge ZeroCID indicator)
        if ($desc -match 'phone' -and $isLicensed) {
            Write-Fail (T 'O5_PHONE')
            Write-Fail (T 'O5_PHONE2')
            Write-Fail (T 'O5_PHONE3')
            $criticalKms = $true
            $suspiciousCount++
        }

        # 7b. GVLK check -- distinguish by channel:
        #   VOLUME_KMSCLIENT + permanent = KMS38 / TSforge / piracy
        #   RETAIL / OEM_DM  + permanent = legitimate Digital Entitlement (HWID)
        if ($isGvlk -and $isPermanent -and $isVolumeKms) {
            Write-Fail ([string]::Format((T 'O5_GVLK_PERM_VOL'), $ppk))
            Write-Fail ((T 'O5_GVLK_PV2') + $desc)
            Write-Fail (T 'O5_GVLK_PV3')
            Write-Fail (T 'O5_GVLK_PV4')
            $criticalKms = $true
            $suspiciousCount++
        } elseif ($isGvlk -and $isPermanent -and -not $isVolumeKms) {
            # RETAIL/OEM_DM channel + permanent = Digital Entitlement = legitimate
            $channel = if ($desc -match 'OEM_DM') { 'OEM_DM' } elseif ($desc -match 'RETAIL') { 'RETAIL' } else { 'non-VOLUME' }
            Write-DE ([string]::Format((T 'O5_HWID_DE'), $ppk, $channel))
            Write-OK  (T 'O5_HWID_DE2')
            Write-Diag (T 'O5_HWID_DE3')
            Write-Diag (T 'O5_HWID_DE4')
        } elseif ($isGvlk -and -not $isPermanent) {
            Write-OK  ([string]::Format((T 'O5_GVLK_KMS'), $graceMins))
            Write-Info (T 'O5_GVLK_KMS2')
        } else {
            Write-OK  ([string]::Format((T 'O5_GVLK_NONE2'), $ppk))
        }
    } else {
        Write-Info (T 'O5_NO_WMI')
    }

    Write-Blank
    Write-Sep

    # (8) Activation expiry analysis ----------------------------------------------
    Write-Step (T 'O5_STEP8')
    Write-Diag (T 'O5_EXPIRY_EXP1')
    Write-Diag (T 'O5_EXPIRY_EXP2')
    Write-Diag (T 'O5_EXPIRY_EXP3')
    Write-Diag (T 'O5_EXPIRY_EXP4')
    Write-Blank

    if ($wmiLicStatus) {
        $graceMins2 = $wmiLicStatus.GracePeriodRemaining
        $licStatus2 = $wmiLicStatus.LicenseStatus
        if (($graceMins2 -eq 0 -or $graceMins2 -eq $null) -and $licStatus2 -eq 1) {
            Write-OK (T 'O5_EXP_PERM')
        } elseif ($graceMins2 -gt 0) {
            $expiry = (Get-Date).AddMinutes($graceMins2)
            $exYear = $expiry.Year
            $daysLeft = ($expiry - (Get-Date)).TotalDays
            Write-Data (T 'O5_EXP_DATE') ($expiry.ToString('yyyy-MM-dd HH:mm'))
            if ($exYear -ge 2100) {
                Write-Fail ([string]::Format((T 'O5_EXP_TSFORGE'), $exYear))
                Write-Fail (T 'O5_EXP_TSFORG2')
                $criticalKms = $true
                $suspiciousCount++
            } elseif ($exYear -ge 2037) {
                Write-Fail ([string]::Format((T 'O5_EXP_KMS38'), $exYear))
                Write-Warn (T 'O5_EXP_KMS38_2')
                $criticalKms = $true
                $suspiciousCount++
            } elseif ($daysLeft -ge 165 -and $daysLeft -le 195) {
                Write-Warn ([string]::Format((T 'O5_EXP_ONLKMS'), [int]$daysLeft))
                Write-Warn (T 'O5_EXP_ONLKMS2')
                $suspiciousCount++
            } else {
                Write-OK (T 'O5_EXP_NORMAL')
            }
        }
    } else {
        Write-Info (T 'O5_NO_EXP_WMI')
    }

    Write-Blank
    Write-Sep

    # (9) TSforge SPP store file timestamp (LOW CONFIDENCE) ------------------------
    Write-Step (T 'O5_STEP9')
    Write-Diag (T 'O5_SPP_EXP1')
    Write-Diag (T 'O5_SPP_EXP2')
    Write-Diag (T 'O5_SPP_EXP3')
    Write-Blank

    $datPath = "$env:SystemRoot\System32\spp\store\2.0\data.dat"
    if (Test-Path -LiteralPath $datPath -Force) {
        $datMod = (Get-Item -LiteralPath $datPath -Force).LastWriteTime
        Write-Data (T 'O5_SPP_PATH') ($datMod.ToString('yyyy-MM-dd HH:mm'))

        # Compare against Windows Install Date from registry
        $installDate = $null
        try {
            $installUnix = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' `
                -Name 'InstallDate' -ErrorAction Stop).InstallDate
            $installDate = (Get-Date '1970-01-01').AddSeconds($installUnix).ToLocalTime()
            Write-Data (T 'O5_SPP_INSTALL') ($installDate.ToString('yyyy-MM-dd HH:mm'))
        } catch {}

        # Check for nearby Windows Update event (Event ID 19)
        $hasNearbyUpdate = $false
        try {
            $events = Get-EventLog -LogName System -Newest 500 -ErrorAction SilentlyContinue |
                Where-Object { $_.EventID -eq 19 -and [Math]::Abs(($_.TimeWritten - $datMod).TotalHours) -lt 48 }
            $hasNearbyUpdate = ($events -ne $null -and @($events).Count -gt 0)
        } catch {}

        # Only warn if data.dat is significantly newer than install AND there is no correlated WU event.
        if ($hasNearbyUpdate) {
            Write-OK (T 'O5_SPP_OK')
        } elseif ($installDate -ne $null -and $datMod -gt $installDate.AddDays(2)) {
            Write-Warn (T 'O5_SPP_WARN1')
            Write-Warn (T 'O5_SPP_WARN2')
            $suspiciousCount++
        } else {
            Write-OK (T 'O5_SPP_NORMAL')
        }
    } else {
        Write-Info (T 'O5_SPP_NOT_FOUND')
    }

    Write-Blank
    Write-Sep

    # (10) SPP Security Event Log ---------------------------------------------
    Write-Step (T 'O5_STEP10')
    Write-Diag (T 'O5_SPPE_EXP1')
    Write-Diag (T 'O5_SPPE_EXP2')
    Write-Blank

    try {
        $sppIds  = @(12288, 12289, 12290, 8198)
        $sppEvts = Get-EventLog -LogName System -Newest 500 -ErrorAction SilentlyContinue |
            Where-Object { $sppIds -contains $_.EventID -and
                ($_.Source -like '*Security-SPP*' -or $_.Source -like '*SoftwareProtection*') }

        if (-not $sppEvts -or @($sppEvts).Count -eq 0) {
            Write-OK (T 'O5_SPPE_NONE')
        } else {
            Write-Data (T 'O5_SPPE_COUNT') "$(@($sppEvts).Count)"
            $extFound = $false
            foreach ($ev in ($sppEvts | Where-Object { $_.EventID -eq 12290 })) {
                # Extract server address from message (IP or domain)
                # Use backtick-escaped brackets to prevent PS type-literal parsing
                $oct         = '\d{1,3}'
                $ipPat       = "$oct\.$oct\.$oct\.$oct"
                $alnumClass  = 'a-zA-Z0-9'
                $domPat      = "`[$alnumClass`]`[$alnumClass\.\-`]*\.[`a-zA-Z`]{2,}"
                $addrPattern = "($ipPat|$domPat)"
                $addrMatch = [regex]::Match($ev.Message, $addrPattern)
                if ($addrMatch.Success) {
                    $addr = $addrMatch.Value
                    $isPrivate = $addr -match '^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.|127\.|::1|localhost|0\.0\.0\.0)'
                    if (-not $isPrivate) {
                        Write-Fail ((T 'O5_SPPE_EXT') + $addr)
                        Write-Fail (T 'O5_SPPE_CONF')
                        $criticalKms = $true
                        $extFound    = $true
                        $suspiciousCount++
                    }
                }
            }
            if (-not $extFound) {
                Write-OK (T 'O5_SPPE_OK')
            }
        }
    } catch {
        Write-Warn ((T 'O5_SPPE_ERR') + $_)
    }

    Write-Blank
    Write-Sep

    # Results -----------------------------------------------------------------

    Write-Blank
    Write-Host ("  " + (T 'O5_RESULTS_HDR')) -ForegroundColor White
    Write-Blank

    if ($criticalKms) {
        Write-Fail (T 'O5_CRITICAL')
        Write-Fail (T 'O5_CRITICAL2')
        Write-Fail ((T 'O5_CRIT_COUNT') + $suspiciousCount)
    } elseif ($suspiciousCount -gt 0) {
        Write-Warn (T 'O5_SUSPICIOUS')
        Write-Warn ((T 'O5_SUSP_COUNT') + $suspiciousCount)
    } else {
        Write-OK  (T 'O5_CLEAN')
    }

    if (Test-Path $SETTINGS_FILE) {
        Write-Blank
        Write-Info ((T 'O5_CUSTOM_LOADED') + $SETTINGS_FILE)
    }
    Write-Blank

    # Legal notice (always shown)
    Write-Sep
    Write-Host ("  " + (T 'O5_LEGAL_HDR')) -ForegroundColor Yellow
    Write-Diag (T 'O5_LEGAL1')
    Write-Diag (T 'O5_LEGAL2')
    Write-Diag (T 'O5_LEGAL3')
    Write-Diag (T 'O5_LEGAL4')
    Write-Blank
    Write-Diag (T 'O5_LEGAL_LIM')
    Write-Diag (T 'O5_LEGAL_LIM2')
    Write-Blank
}

# =============================================================================
# OPTION U -- Update scan defaults from GitHub
# =============================================================================
function Update-DefaultSettings {
    Write-Blank
    Write-Host ("  " + (T 'OU_OPT_HDR')) -ForegroundColor Magenta
    Write-Sep
    Write-Info (T 'OU_INFO1')
    Write-Info (T 'OU_INFO2')
    Write-Info (T 'OU_INFO3')
    Write-Sep
    Write-Blank

    # Check internet first
    $online = $false
    try {
        $null = [System.Net.Dns]::GetHostEntry("raw.githubusercontent.com")
        $online = $true
    } catch {}

    if (-not $online) {
        Write-Fail (T 'OU_NO_NET1')
        Write-Fail (T 'OU_NO_NET2')
        Write-Blank
        return
    }

    Write-Step (T 'OU_DOWNLOADING')
    $url      = "https://raw.githubusercontent.com/ardennguyen/WinLic/main/WinLicPS/settings.default.ini"
    $tmpPath  = Join-Path $SCRIPT_DIR "settings.default.ini.tmp"

    try {
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "WinLicManager/1.0")
        $downloaded = $wc.DownloadString($url)
    } catch {
        Write-Fail ((T 'OU_DL_FAIL') + $_)
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

    # Build the user-block separator and default user sections using safe string building
    $br      = "`r`n"
    $eq73    = "=" * 73
    $sepLine = "# $eq73"
    $sepText = '# ||  USER BLOCK  --  Edit freely. NEVER overwritten by Update-defaults.  ||'
    $separator = ($br + $sepLine + $br + $sepText + $br + $sepLine + $br)

    $userSections  = "[UserGvlkKeys]" + $br
    $userSections += "; Add custom GVLK/suspicious keys here: FULL-KEY = Description" + $br + $br
    $userSections += "[UserKmsPiracyDomains]" + $br
    $userSections += "; Add your own KMS piracy hostnames here" + $br + $br
    $userSections += "[ExtraPorts]" + $br
    $userSections += "; Additional TCP ports to probe on localhost" + $br + $br
    $userSections += "[ExtraServices]" + $br
    $userSections += "; Additional service name keywords" + $br + $br
    $userSections += "[ExtraTaskKeywords]" + $br
    $userSections += "; Additional scheduled task keywords" + $br + $br
    $userSections += "[ExtraProcesses]" + $br
    $userSections += "; Additional process name keywords" + $br + $br
    $userSections += "[ExtraFilePaths]" + $br
    $userSections += "; Additional file paths to check" + $br

    if ($userBlock) {
        $combined = $downloaded.TrimEnd() + $br + $br + $userBlock
    } else {
        $combined = $downloaded.TrimEnd() + $br + $separator + $br + $userSections
    }


    try {
        [System.IO.File]::WriteAllText($SETTINGS_FILE, $combined, [System.Text.Encoding]::UTF8)
        Write-OK  (T 'OU_SUCCESS')
        Write-Data (T 'OU_FILE')      $SETTINGS_FILE
        Write-Data (T 'OU_TIMESTAMP') $ts
    } catch {
        Write-Fail ((T 'OU_WRITE_FAIL') + $_)
    }

    Write-Blank
}

# =============================================================================
# OPTION 6 -- KMS Activation
# =============================================================================

# GVLK table: pattern (substring of edition name) -> GVLK key
$KmsGvlkTable = @(
    @{ Pat='Pro N';                 Ed='Windows 10/11 Pro N';                Key='MH37W-N47XK-V7XM9-C7227-GCQG9' },
    @{ Pat='Pro Education N';       Ed='Windows 10/11 Pro Education N';      Key='YVWGF-BXNMC-HTQYQ-CPQ99-66QFC' },
    @{ Pat='Pro Education';         Ed='Windows 10/11 Pro Education';        Key='6TP4R-GNPTD-KYYHQ-7B7DP-J447Y' },
    @{ Pat='Pro';                   Ed='Windows 10/11 Pro';                  Key='W269N-WFGWX-YVC9B-4J6C9-T83GX' },
    @{ Pat='Education N';           Ed='Windows 10/11 Education N';          Key='2WH4N-8QGBV-H22JP-CT43Q-MDWWJ' },
    @{ Pat='Education';             Ed='Windows 10/11 Education';            Key='NW6C2-QMPVW-D7KKK-3GKT6-VCFB2' },
    @{ Pat='Enterprise G N';        Ed='Windows 10/11 Enterprise G N';       Key='44RPN-FTY23-9VTTB-MP9BX-T84FV' },
    @{ Pat='Enterprise G';          Ed='Windows 10/11 Enterprise G';         Key='YYVX9-NTFWV-6MDM3-9PT4T-4M68B' },
    @{ Pat='Enterprise N';          Ed='Windows 10/11 Enterprise N';         Key='DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4' },
    @{ Pat='Enterprise LTSC 2021';  Ed='Windows 10 Enterprise LTSC 2021';   Key='M7XTQ-FN8P6-TTKYV-9D4CC-J462D' },
    @{ Pat='Enterprise LTSC 2019';  Ed='Windows 10 Enterprise LTSC 2019';   Key='M7XTQ-FN8P6-TTKYV-9D4CC-J462D' },
    @{ Pat='Enterprise LTSC 2016';  Ed='Windows 10 Enterprise LTSC 2016';   Key='DCPHK-NFMTC-H88MJ-PFHPY-QJ4BJ' },
    @{ Pat='Enterprise';            Ed='Windows 10/11 Enterprise';           Key='NPPR9-FWDCX-D2C8J-H872K-2YT43' },
    @{ Pat='Server 2022 Datacenter';Ed='Windows Server 2022 Datacenter';    Key='WX4NM-KYWYW-QJJR4-XV3QB-6VM33' },
    @{ Pat='Server 2022 Standard';  Ed='Windows Server 2022 Standard';      Key='VDYBN-27WPP-V4HQT-9VMD4-VMK7H' },
    @{ Pat='Server 2019 Datacenter';Ed='Windows Server 2019 Datacenter';    Key='WMDGN-G9PQG-XVVXX-R3X43-63DFG' },
    @{ Pat='Server 2019 Standard';  Ed='Windows Server 2019 Standard';      Key='N69G4-B89J2-4G8F4-WWYCC-J464C' },
    @{ Pat='Server 2016 Datacenter';Ed='Windows Server 2016 Datacenter';    Key='CB7KF-BWN84-R7R2Y-793K2-8XDDG' },
    @{ Pat='Server 2016 Standard';  Ed='Windows Server 2016 Standard';      Key='WC2BQ-8NRM3-FDDYY-2BFGV-KHKQY' },
    @{ Pat='Server 2012 R2 Datacenter';Ed='Windows Server 2012 R2 Datacenter';Key='W3GGN-FT8W3-Y4M27-J84CP-Q3VJ9' },
    @{ Pat='Server 2012 R2 Standard';Ed='Windows Server 2012 R2 Standard'; Key='D2N9P-3P6X9-2R39C-7RTCD-MDVJX' }
)

# =============================================================================
# Option 6 -- Change Activation Channel
# =============================================================================
function Set-ActivationChannel {
    Write-Blank
    Write-Host ("  " + (T 'O6CH_OPT_HDR')) -ForegroundColor Magenta
    Write-Blank
    Write-Diag (T 'O6CH_DESC')
    Write-Blank

    # Detect current channel via WMI
    $wmi = $null
    try {
        $wmi = Get-WmiObject -Class SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL" -ErrorAction Stop |
               Where-Object { $_.Name -like "Windows*" } | Select-Object -First 1
    } catch {}
    $channel = if ($wmi) { $wmi.Description } else { "(unknown)" }
    $partKey = if ($wmi -and $wmi.PartialProductKey) { $wmi.PartialProductKey } else { "?????" }
    $edition = if ($wmi) { $wmi.Name } else { "(unknown)" }

    Write-Info  ((T 'O6CH_CURRENT_CHANNEL') + " " + $channel)
    Write-Info  ((T 'O6CH_CURRENT_EDITION') + " " + $edition)
    Write-Info  ((T 'O6CH_CURRENT_KEY') + " " + $partKey)
    Write-Blank

    $isKmsClient = $channel -match "VOLUME_KMSCLIENT"
    $isKmsHost   = $channel -match "VOLUME_KMS" -and -not $isKmsClient

    if ($isKmsHost) {
        Write-Warn (T 'O6CH_HOST_WARN')
        return
    }

    # Show options
    Write-Host ("  " + (T 'O6CH_TO_KMS'))    -ForegroundColor $(if ($isKmsClient) { "DarkGray" } else { "White" })
    Write-Host ("  " + (T 'O6CH_TO_RETAIL')) -ForegroundColor $(if ($isKmsClient) { "White" } else { "DarkGray" })
    Write-Blank
    $choice = (Read-Host "  [A] KMS  [B] RETAIL/MAK  [C] Cancel").Trim().ToUpper()

    switch ($choice) {
        'A' {
            if ($isKmsClient) { Write-Info (T 'O6CH_ALREADY_KMS'); return }
            # GVLK lookup table
            $gvlkMap = @{
                "Pro N"                    = "MH37W-N47XK-V7XM9-C7227-GCQG9"
                "Pro Education N"          = "YVWGF-BXNMC-HTQYQ-CPQ99-66QFC"
                "Pro Education"            = "6TP4R-GNPTD-KYYHQ-7B7DP-J447Y"
                "Pro"                      = "W269N-WFGWX-YVC9B-4J6C9-T83GX"
                "Education N"              = "2WH4N-8QGBV-H22JP-CT43Q-MDWWJ"
                "Education"                = "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2"
                "Enterprise G N"           = "44RPN-FTY23-9VTTB-MP9BX-T84FV"
                "Enterprise G"             = "YYVX9-NTFWV-6MDM3-9PT4T-4M68B"
                "Enterprise N"             = "DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4"
                "Enterprise LTSC 2021"     = "M7XTQ-FN8P6-TTKYV-9D4CC-J462D"
                "Enterprise LTSC 2019"     = "M7XTQ-FN8P6-TTKYV-9D4CC-J462D"
                "Enterprise LTSC 2016"     = "DCPHK-NFMTC-H88MJ-PFHPY-QJ4BJ"
                "Enterprise"               = "NPPR9-FWDCX-D2C8J-H872K-2YT43"
                "Server 2022 Datacenter"   = "WX4NM-KYWYW-QJJR4-XV3QB-6VM33"
                "Server 2022 Standard"     = "VDYBN-27WPP-V4HQT-9VMD4-VMK7H"
                "Server 2019 Datacenter"   = "WMDGN-G9PQG-XVVXX-R3X43-63DFG"
                "Server 2019 Standard"     = "N69G4-B89J2-4G8F4-WWYCC-J464C"
                "Server 2016 Datacenter"   = "CB7KF-BWN84-R7R2Y-793K2-8XDDG"
                "Server 2016 Standard"     = "WC2BQ-8NRM3-FDDYY-2BFGV-KHKQY"
                "Server 2012 R2 Datacenter"= "W3GGN-FT8W3-Y4M27-J84CP-Q3VJ9"
                "Server 2012 R2 Standard"  = "D2N9P-3P6X9-2R39C-7RTCD-MDVJX"
            }
            $gvlk = $null
            foreach ($pat in $gvlkMap.Keys) {
                if ($edition -like "*$pat*") { $gvlk = $gvlkMap[$pat]; break }
            }
            if (-not $gvlk) {
                Write-Warn (T 'O6CH_GVLK_NOMAP')
                return
            }
            Write-Info ((T 'O6CH_GVLK_LABEL') + " " + $gvlk)
            $ok = (Read-Host (T 'O8KMS_GVLK_CONFIRM')).Trim()
            if ($ok -ne 'OK') { Write-Info (T 'O6CH_CANCELLED'); return }
            Write-Step (T 'O6CH_GVLK_INSTALLING')
            $ipk = & cscript //NoLogo "$env:windir\System32\slmgr.vbs" /ipk $gvlk 2>&1 | Out-String
            Write-Host ("  " + $ipk.Trim()) -ForegroundColor Gray
            if ($ipk -match "successfully") {
                Write-OK (T 'O6CH_GVLK_DONE')
            } else {
                Write-Fail (T 'O8KMS_FAIL')
            }
        }
        'B' {
            Write-Info (T 'O6CH_RETAIL_MSG')
        }
        default { Write-Info (T 'O6CH_CANCELLED') }
    }
}

# =============================================================================
# Option 7 -- Check & Remove KMS Settings
# =============================================================================
function Get-KmsSettings {
    Write-Blank
    Write-Host ("  " + (T 'O7KMS_OPT_HDR')) -ForegroundColor Cyan
    Write-Blank
    Write-Diag (T 'O7KMS_DESC')
    Write-Blank

    # Read registry
    $regKey  = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareLicensingService"
    $regHost = (Get-ItemProperty -Path $regKey -Name "KeyManagementServiceMachine" -ErrorAction SilentlyContinue).KeyManagementServiceMachine
    $regPort = (Get-ItemProperty -Path $regKey -Name "KeyManagementServicePort" -ErrorAction SilentlyContinue).KeyManagementServicePort
    if (-not $regHost) { $regHost = T 'O7KMS_NOT_SET' }
    if (-not $regPort) { $regPort = T 'O7KMS_DEFAULT_PORT' }

    # Read slmgr /dlv
    $dlvHost = T 'O7KMS_NOT_SET'
    try {
        $dlvOut = & cscript //NoLogo "$env:windir\System32\slmgr.vbs" /dlv 2>&1 | Out-String
        if ($dlvOut -match "KMS machine name[^:]*:\s*(.+)") { $dlvHost = $Matches[1].Trim() }
    } catch {}

    # Display
    Write-Info ((T 'O7KMS_REG_HOST') + " " + $regHost)
    Write-Info ((T 'O7KMS_REG_PORT') + " " + $regPort)
    Write-Info ((T 'O7KMS_DLV_HOST') + " " + $dlvHost)
    Write-Blank

    $hasCustom = $regHost -ne (T 'O7KMS_NOT_SET')
    if (-not $hasCustom) {
        Write-OK (T 'O7KMS_NONE_ACTIVE')
        return
    }

    Write-Warn (T 'O7KMS_CUSTOM_ACTIVE')
    Write-Blank
    $ok = (Read-Host (T 'O7KMS_CLEAR_CONFIRM')).Trim()
    if ($ok -ne 'OK') { Write-Info (T 'O7KMS_CANCELLED'); return }

    Write-Step (T 'O7KMS_CLEARING')
    $ckms = & cscript //NoLogo "$env:windir\System32\slmgr.vbs" /ckms 2>&1 | Out-String
    Write-Host ("  " + $ckms.Trim()) -ForegroundColor Gray

    if ($ckms -match "successfully") {
        Write-OK (T 'O7KMS_CLEARED')
    } else {
        Write-Fail (T 'O7KMS_CLEAR_FAILED')
        return
    }

    Write-Blank
    Write-Info (T 'O7KMS_NEXT_OPT8')
    Write-Info (T 'O7KMS_NEXT_OPT6')
    Write-Info (T 'O7KMS_NEXT_DNS')
}

function Invoke-KmsActivation {
    if (-not $isAdmin) { Elevate-For-Option; return }

    Write-Blank
    Write-Host ("  " + (T 'O8KMS_OPT_HDR')) -ForegroundColor Magenta
    Write-Sep
    Write-Diag (T 'O8KMS_DESC1')
    Write-Diag (T 'O8KMS_DESC2')
    Write-Diag (T 'O8KMS_DESC3')
    Write-Sep
    Write-Blank

    # -- Step 1: Channel check -----------------------------------------------
    Write-Step (T 'O8KMS_CHK_CHANNEL')
    $activeProduct = Get-CimInstance -ClassName SoftwareLicensingProduct |
                     Where-Object { $_.PartialProductKey -and $_.Name -like 'Windows*' }

    $chanDesc  = ''
    $partKey   = ''
    $edName    = ''
    if ($activeProduct) {
        $chanDesc = ($activeProduct.Description + '').ToUpperInvariant()
        $partKey  = $activeProduct.PartialProductKey
        $edName   = $activeProduct.Name
        Write-Data '  Channel:' $activeProduct.Description 'Cyan'
    }

    $isKmsClient = $chanDesc -match 'VOLUME_KMSCLIENT'
    $isKmsHost   = ($chanDesc -match 'VOLUME_KMS') -and (-not $isKmsClient)

    if ($isKmsHost)    { Write-Warn (T 'O8KMS_WARN_KMSHOST') }
    if (-not $isKmsClient) { Write-Warn (T 'O8KMS_WARN_NOTVOLUME') }

    Write-Blank

    # -- Step 2: GVLK check --------------------------------------------------
    Write-Step (T 'O8KMS_CHK_GVLK')

    # Compute partial-key suffixes for our GVLK table
    $gvlkSuffixes = @{}
    foreach ($entry in $KmsGvlkTable) { $gvlkSuffixes[$entry.Key.Substring($entry.Key.Length - 5)] = $true }

    $isGvlk = $isKmsClient -or ($partKey -and $gvlkSuffixes.ContainsKey($partKey))
    $gvlkToInstall = $null
    $gvlkEdition   = $null

    if ($isGvlk) {
        Write-OK ((T 'O8KMS_GVLK_OK') + $partKey)
    } else {
        Write-Warn (T 'O8KMS_GVLK_MISSING')
        # Try to find a matching GVLK for this edition
        foreach ($entry in $KmsGvlkTable) {
            if ($edName -match [regex]::Escape($entry.Pat)) {
                $gvlkToInstall = $entry.Key
                $gvlkEdition   = $entry.Ed
                break
            }
        }
        if ($gvlkToInstall) {
            Write-Blank
            Write-Host ("  " + (T 'O8KMS_GVLK_FOUND') + "  $gvlkEdition") -ForegroundColor Yellow
            Write-Host ("  Key: $gvlkToInstall") -ForegroundColor Cyan
            Write-Blank
            $okInput = Read-Host (T 'O8KMS_GVLK_CONFIRM')
            if ($okInput.Trim().ToUpper() -ne 'OK') {
                Write-Warn (T 'O8KMS_GVLK_CANCELED')
                return
            }
            Write-Blank
            Write-Step (T 'O8KMS_GVLK_INSTALL')
            Write-Cmd  "cscript //nologo `"$slmgrPath`" /ipk $gvlkToInstall"
            $ipkOut  = cscript //nologo $slmgrPath /ipk $gvlkToInstall 2>&1
            $ipkFull = [string]::Join(' ', $ipkOut)
            foreach ($line in $ipkOut) { Write-Host ("  {0}" -f $line.Trim()) }
            Write-Blank
            if ($ipkFull -match 'Error:' -or $ipkFull -match '0x') {
                Write-Fail (T 'O8KMS_FAIL')
                Write-Info (T 'O8KMS_REF_URL')
                return
            }
            Write-OK (T 'O8KMS_GVLK_DONE')
            Write-Blank
        } else {
            Write-Warn (T 'O8KMS_GVLK_NOMAP')
            Write-Info (T 'O8KMS_REF_URL')
            return
        }
    }

    # -- Step 3: DNS SRV lookup ----------------------------------------------
    Write-Step (T 'O8KMS_CHK_DNS')
    $kmsHost = $null
    try {
        $nsOut = & nslookup -type=SRV _VLMCS._TCP 2>&1 | Out-String
        $m = [regex]::Match($nsOut, 'svr hostname\s*=\s*(\S+)', 'IgnoreCase')
        if ($m.Success) { $kmsHost = $m.Groups[1].Value.TrimEnd('.') }
    } catch {}

    if ($kmsHost) {
        Write-OK ((T 'O8KMS_DNS_FOUND') + $kmsHost)
    } else {
        Write-Warn (T 'O8KMS_DNS_FAIL')
        Write-Blank
        $manualInput = Read-Host (T 'O8KMS_MANUAL_PROMPT')
        $manualInput = $manualInput.Trim()
        if (-not $manualInput) {
            Write-Warn (T 'O8KMS_CANCELED')
            return
        }
        $kmsHost = $manualInput
        Write-Host ("  -> Manual host: $kmsHost") -ForegroundColor Yellow
    }
    Write-Blank

    # -- Step 4: TCP 1688 test -----------------------------------------------
    Write-Step ((T 'O8KMS_CHK_PORT') + " $kmsHost:1688")
    $port1688ok = $false
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect($kmsHost, 1688)
        $tcp.Close()
        $port1688ok = $true
        Write-OK ((T 'O8KMS_PORT_OK') + "${kmsHost}:1688")
    } catch {
        Write-Warn ((T 'O8KMS_PORT_FAIL') + "${kmsHost}:1688")
    }
    Write-Blank

    # -- Step 5: Clock advisory ----------------------------------------------
    Write-Step (T 'O8KMS_CHK_CLOCK')
    Write-Diag (T 'O8KMS_CLOCK_WARN')
    Write-Blank

    # -- /skms: persist only if TCP 1688 is reachable ------------------------
    if ($port1688ok) {
        Write-Step (T 'O8KMS_SKMS_PERSIST')
        Write-Cmd  "cscript //nologo `"$slmgrPath`" /skms $kmsHost"
        $skmsOut = cscript //nologo $slmgrPath /skms $kmsHost 2>&1
        foreach ($line in $skmsOut) { Write-Host ("  {0}" -f $line.Trim()) }
        Write-Blank
    } else {
        Write-Warn (T 'O8KMS_SKMS_NOPERSIST')
        Write-Blank
    }

    # -- Step 6: slmgr /ato --------------------------------------------------
    Write-Step (T 'O8KMS_CHK_ACTIVATE')
    Write-Cmd  "cscript //nologo `"$slmgrPath`" /ato"
    $atoOut  = cscript //nologo $slmgrPath /ato 2>&1
    $atoFull = [string]::Join(' ', $atoOut)
    foreach ($line in $atoOut) { Write-Host ("  {0}" -f $line.Trim()) }
    Write-Blank
    Write-Sep

    if ($atoFull -match 'Error:' -or $atoFull -match '0x') {
        Write-Fail (T 'O8KMS_FAIL')
        if      ($atoFull -match '0xC004F038') { Write-Warn (T 'O8KMS_DIAG_COUNT') }
        elseif  ($atoFull -match '0xC004F039') { Write-Warn (T 'O8KMS_DIAG_NOTENABLED') }
        elseif  ($atoFull -match '0xC004F041') { Write-Warn (T 'O8KMS_DIAG_HOSTNACT') }
        elseif  ($atoFull -match '0xC004F042') { Write-Warn (T 'O8KMS_DIAG_WRONGHOST') }
        elseif  ($atoFull -match '0xC004F06C') { Write-Warn (T 'O8KMS_DIAG_CLOCK') }
        elseif  ($atoFull -match '0xC004F074') { Write-Warn (T 'O8KMS_DIAG_NOCONTACT') }
        elseif  ($atoFull -match '0x8007007B|0x8007232B|0x8007251D|0x80092328') { Write-Warn (T 'O8KMS_DIAG_DNS') }
        elseif  ($atoFull -match '0xC004F035') { Write-Warn (T 'O8KMS_DIAG_VOLK') }
        Write-Blank
        Write-Info (T 'O8KMS_REF_URL')
    } else {
        Write-OK  (T 'O8KMS_SUCCESS')
        Write-Info (T 'O8KMS_SUCCESS2')
    }
    Write-Blank
}

# =============================================================================
# Main loop
# =============================================================================

$script:firstLoad = $true

do {
    if ($script:firstLoad) {
        Select-Language   # one-time language choice before header
        # PS7 upgrade offer: if running PS5 but PS7 is installed, offer to relaunch
        if (-not $isPS7 -and $pwshExe) {
            Write-Blank
            Write-Info (T 'PS7_AVAIL')
            if (Ask-YesNo (T 'PS7_RELAUNCH')) {
                Write-OK (T 'PS7_DOING')
                Start-Sleep -Milliseconds 800
                try {
                    $args7 = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
                    if ($wtExe) {
                        Start-Process $wtExe -ArgumentList "new-tab $pwshExe $args7"
                    } else {
                        Start-Process $pwshExe -ArgumentList $args7
                    }
                    Exit
                } catch {}
            } else {
                Write-Info (T 'PS7_SKIP')
            }
            Write-Blank
        }
    }
    Show-Header
    if ($script:firstLoad) {
        Show-About
        $script:firstLoad = $false
    }
    Show-Menu
    $choice = (Read-Host ("  " + (T 'MENU_SELECT'))).Trim().ToUpper()
    Write-Blank

    switch ($choice) {
        '1' { Get-VersionInfo;        Read-Host ("  " + (T 'PRESS_ENTER')) }
        '2' { Test-ProductKey;        Read-Host ("  " + (T 'PRESS_ENTER')) }
        '3' { Remove-License;         Read-Host ("  " + (T 'PRESS_ENTER')) }
        '4' { Reset-Activation;       Read-Host ("  " + (T 'PRESS_ENTER')) }
        '5' { Invoke-ActivationAudit; Read-Host ("  " + (T 'PRESS_ENTER')) }
        '6' { Set-ActivationChannel; Read-Host ("  " + (T 'PRESS_ENTER')) }
        '7' { Get-KmsSettings;       Read-Host ("  " + (T 'PRESS_ENTER')) }
        '8' { Invoke-KmsActivation;   Read-Host ("  " + (T 'PRESS_ENTER')) }
        'U' { Update-DefaultSettings; Read-Host ("  " + (T 'PRESS_ENTER')) }
        'Q' { Write-OK (T 'GOODBYE'); break }
        default {
            Write-Warn (T 'INVALID_OPT')
            Start-Sleep -Seconds 1
        }
    }
} while ($choice -ne 'Q')

