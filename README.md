# WinLic Manager

<div align="center">

![WinLic Manager](WinLicApp/winlic_256.png)

**Windows License Management Tool — GUI & PowerShell CLI**

[![Version](https://img.shields.io/badge/version-v1.5-a78bfa?style=flat-square)](https://github.com/ardennguyen/WinLic/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-7c3aed?style=flat-square)](https://github.com/ardennguyen/WinLic)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-6d28d9?style=flat-square)](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
[![License](https://img.shields.io/badge/license-MIT-6d28d9?style=flat-square)](LICENSE)

</div>

---

> 🇺🇸 [English](#-english) · 🇻🇳 [Tiếng Việt](#-tiếng-việt)

---

# 🇺🇸 English

**Navigation:**
[Overview](#overview) ·
[Download](#download) ·
[Features](#features) ·
[GUI App](#gui-app) ·
[PowerShell CLI](#powershell-cli) ·
[Build from Source](#build-from-source) ·
[Project Structure](#project-structure) ·
[Author](#author)

---

## Overview

**WinLic Manager** consists of two parallel tools — a **.NET WPF GUI application** and a **PowerShell CLI script** — that let you view, inspect, and manage Windows licensing without typing manual commands.

Both tools support the same **8 options** in full, run on **Windows 10/11**, and use WMI, Registry, and `slmgr.vbs` — **no additional installation required**.

---

## Download

Get the latest release from the [**Releases**](https://github.com/ardennguyen/WinLic/releases) page:

| File | Description |
|---|---|
| `WinLicApp-<version>.exe` | **GUI App** — run directly, no installation needed |
| `WinLicApp-<version>.exe.sha256` | Integrity check for the EXE file |
| `WinLicApp-<version>.zip` | Compressed archive of the GUI App |
| `WinLicApp-<version>.zip.sha256` | Integrity check for the ZIP file |
| `WinLicManager-<version>.ps1` | **PowerShell CLI** — standalone script |
| `WinLicManager-<version>.ps1.sha256` | Integrity check for the PS1 file |
| `WinLicPS-<version>.zip` | **PowerShell CLI bundle** — script + settings |
| `WinLicPS-<version>.zip.sha256` | Integrity check for the CLI bundle |

### Integrity Verification (optional)

```powershell
# PowerShell
Get-FileHash .\WinLicApp-<version>.zip -Algorithm SHA256
# Compare with the contents of the matching .sha256 file
```
```cmd
:: CMD / certutil
certutil -hashfile WinLicApp-<version>.zip SHA256
```

---

## Features

Both tools (GUI and CLI) provide the same **8 options**:

| # | Feature | Privilege |
|---|---------|-----------|
| 1 | **System Info & License Status** — reads OS info, activation channel, OEM BIOS key, detects DE/KMS/Retail; optional extended `/dlv` scan | No Admin required |
| 2 | **Test & Install Product Key** — validates and installs a new key via `slmgr /ipk`; auto-activates `/ato`; channel warnings; detailed error diagnostics | **Admin** |
| 3 | **Remove License Key** — removes the current key with `slmgr /upk` + `/cpky`; confirmation required before execution | **Admin** |
| 4 | **Reset Activation (Rearm)** — shows remaining rearm count (WMI); confirmation required; optional automatic restart | **Admin** |
| 5 | **3rd-Party Activation Audit** — scans **10 layers** of indicators; customizable list; 3-level results (CRITICAL / SUSPICIOUS / CLEAN) | No Admin required |
| 6 | **Change Activation Channel** — switches between GVLK/KMS and Retail; auto-installs the correct GVLK for your edition; auto-redirects after change | **Admin** |
| 7 | **Check & Remove KMS Settings** — reads KMS host/port from Registry and `/dlv`; removes KMS config; suggests next steps | **Admin** |
| 8 | **KMS Activation** — 6-step pre-check workflow: channel, GVLK, DNS SRV, TCP 1688, system clock, `/ato`; error code diagnostics | **Admin** |

> ⚠️ Options **2, 3, 4, 6, 7, 8** make real changes to the Windows licensing state. The app always asks for confirmation before executing.

---

### Option 5 — 3rd-Party Activation Audit (10 scan layers)

| Layer | Check | Detects |
|-------|-------|---------|
| 1 | System services | KMSpico, KMSAuto, vlmcsd, KMS_VL_ALL, Activation-Renewal… |
| 2 | Scheduled tasks | Automatic activation tasks from third-party tools |
| 3 | Running processes | Active activation processes |
| 4 | File paths | GenuineTicket.xml, gatherosstate.exe, Activation_task.cmd, data.dat… |
| 5 | Localhost KMS ports | TCP 1688 and custom ports listening locally |
| 6 | KMS Registry | KMS server, port, fake IP `10.0.0.10`, WoW64 |
| 7 | GVLK & Activation channel | Permanent GVLK + VOLUME = violation; non-VOLUME + generic key = valid DE; phone activation detection |
| 8 | Expiry date | TSforge KMS4k (≥ 2100), KMS38 (≥ 2037), Online KMS 180-day (165–195 days) |
| 9 | SPP timestamps | `data.dat` vs. Windows install date + Event ID 19 from Windows Update |
| 10 | SPP event log | Events 12288/12289/12290/8198 — external KMS server addresses |

**3-level results:** 🔴 CRITICAL (confirmed violation) / 🟡 SUSPICIOUS (indicators found) / 🟢 CLEAN

| Activation Method | Detected | Reason |
|---|---|---|
| KMSpico / KMSAuto (active) | ✅ | Leaves services, tasks, and files |
| vlmcsd / KMS_VL_ALL (active) | ✅ | Leaves services, open KMS ports |
| Online KMS (internet activation) | ✅ | 180-day grace period, task/service traces, event log |
| KMS38 (traces remain) | ✅ | GenuineTicket.xml, gatherosstate, 2038 expiry date |
| TSforge (traces remain) | ⚠️ | Abnormally modified data.dat |
| **MAS HWID / Digital Entitlement** | ❌ | Creates a real Digital Entitlement — leaves no traces |
| Fully cleaned tool | ❌ | No traces remain after removal |

---

### Option 8 — 6-Step KMS Activation Workflow

```
Step 1  Channel check       Must be VOLUME_KMSCLIENT
Step 2  GVLK check          Auto-installs correct key if missing
Step 3  DNS SRV lookup      _VLMCS._TCP → auto-discovers server
Step 4  TCP 1688 check      Manual entry if DNS fails
Step 5  System clock        Warning if offset > ±5 minutes
Step 6  slmgr /ato          Activates + error code diagnostics
```

---

## System Requirements

| | GUI App | PowerShell CLI |
|---|---|---|
| **OS** | Windows 10 (1903+) / Windows 11 x64 | Windows 10 / 11 |
| **.NET** | Framework 4.8 (built-in on Win 10/11) | Not required |
| **PowerShell** | Not required | 5.1+ (built-in) or 7+ |
| **Launch privilege** | No Admin required | No Admin required |
| **Options 2,3,4,6,7,8** | App prompts UAC automatically | App prompts relaunch automatically |

---

## GUI App

### Download & Run

1. Download `WinLicApp-<version>.exe` or `.zip` from [**Releases**](https://github.com/ardennguyen/WinLic/releases)
2. **Double-click** `WinLicApp-<version>.exe` — no installation needed
3. Select language **EN** or **VI** in the top-right corner
4. Select the **Windows 10/11** tab (first tab, default)
5. Click options in the left sidebar to get started

> Options 1 and 5 work immediately without Admin. When you select 2, 3, 4, 6, 7, or 8 — the app will automatically prompt for UAC elevation.

### Interface

- **Tab bar** (Microsoft Edge style) at the top of the window: **Windows 10/11** and **Office 2019+** *(in development)*
- **Left sidebar**: 8 option buttons + show-full-key checkbox + Clear Log button
- **Log area** (right): shared across all options, persists through UAC elevation
- **Inline panels**: dangerous confirmations, key input, KMS checks… appear inside the main window instead of separate dialogs

### Custom Scan Settings (Option 5)

Click the **⚙ Scan Settings** button in the sidebar to open the configuration dialog. Changes are automatically saved to `settings.ini` next to the EXE.

Click **↻ Update Defaults** to fetch the latest list from GitHub.

---

## PowerShell CLI

Designed for power users who prefer the command line. Full **bilingual EN/VI** support.

### Download & Run

1. Download `WinLicPS-<version>.zip` from [**Releases**](https://github.com/ardennguyen/WinLic/releases)
2. Extract to any folder (`WinLicManager.ps1` and `settings.ini` must be in the same folder)
3. Run the script:

```powershell
# Run directly (no Admin — prompts automatically for options 2/3/4/6/7/8)
powershell -ExecutionPolicy Bypass -File .\WinLicManager.ps1

# Or use PowerShell 7
pwsh -ExecutionPolicy Bypass -File .\WinLicManager.ps1

# Run with Admin from the start
Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File .\WinLicManager.ps1"
```

### Menu

```
  ╔══════════════════════════════════════════════════════╗
  ║         WinLic Manager v1.5                          ║
  ╠══════════════════════════════════════════════════════╣
  ║  1 -- System Info & License Status                   ║
  ║  2 -- Test & Install Product Key              [!]    ║
  ║  3 -- Remove License Key                      [!]    ║
  ║  4 -- Reset Activation (Rearm)                [!]    ║
  ║  5 -- 3rd Party Activation Audit                     ║
  ║  6 -- Change Activation Channel               [!]    ║
  ║  7 -- Check & Remove KMS Settings             [!]    ║
  ║  8 -- KMS Activation                          [!]    ║
  ║  U -- Update scan defaults from GitHub               ║
  ║  Q -- Quit                                           ║
  ╚══════════════════════════════════════════════════════╝
```

### Custom Option 5 Scan (settings.ini)

The `settings.ini` file is included in the CLI bundle. Before scanning, the CLI displays a configuration summary and asks if you want to edit it first.

| Section | Description |
|---|---|
| `[ExtraPorts]` | Additional TCP ports to check on localhost |
| `[ExtraServices]` | Additional service name keywords |
| `[ExtraTaskKeywords]` | Additional scheduled task name keywords |
| `[ExtraProcesses]` | Additional process name keywords |
| `[ExtraFilePaths]` | Additional file/folder paths to check |
| `[UserKmsPiracyDomains]` | Additional suspicious KMS domain names |
| `[UserGvlkKeys]` | Additional GVLK keys to recognize |
| `[UserGenericKeys]` | Additional HWID/DE placeholder keys |

Type `U` in the menu to fetch the latest default list from GitHub while keeping your custom settings intact.

---

## Build from Source

### Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) (targeting .NET Framework 4.8)
- Windows 10/11

### Build the GUI App

```bash
git clone https://github.com/ardennguyen/WinLic.git
cd WinLic
dotnet build WinLicApp/WinLicApp.csproj -c Release
```

Output EXE: `WinLicApp/bin/Release/net4.8-windows/WinLicApp.exe`

### PowerShell CLI

No build needed — run directly from source:

```powershell
powershell -ExecutionPolicy Bypass -File .\WinLicPS\WinLicManager.ps1
```

---

## Project Structure

```
WinLic/
├── WinLicApp/                       # GUI application (.NET Framework 4.8 WPF)
│   ├── MainWindow.xaml[.cs]         # Main UI & logic, 8 options + tab bar
│   ├── Localization.cs              # Bilingual EN/VI string table
│   ├── AppSettings.cs               # settings.ini manager (GVLK, ports, domains…)
│   ├── SettingsDialog.xaml[.cs]     # Scan Settings dialog
│   ├── AboutDialog.xaml[.cs]        # About dialog + update check
│   ├── App.xaml[.cs]                # App entry point
│   ├── app.manifest                 # UAC + DPI manifest
│   ├── winlic.ico / winlic_256.png  # App icon
│   └── WinLicApp.csproj             # .NET Framework 4.8 WPF project definition
├── WinLicPS/                        # PowerShell tool (CLI)
│   ├── WinLicManager.ps1            # Main script — 8 bilingual EN/VI options
│   ├── settings.ini                 # Scan configuration (user block)
│   └── settings.default.ini        # Default list (fetched from GitHub)
└── README.md
```

---

## Author

**Arden Nguyen Duc Huy**
- GitHub: [@ardennguyen](https://github.com/ardennguyen)
- Repository: [ardennguyen/WinLic](https://github.com/ardennguyen/WinLic)

---

## License

This project is distributed under the **MIT** license. See the [LICENSE](LICENSE) file for details.

---

# 🇻🇳 Tiếng Việt

**Điều hướng:**
[Giới thiệu](#giới-thiệu) ·
[Tải về](#tải-về) ·
[Tính năng](#tính-năng) ·
[GUI App](#gui-app-1) ·
[PowerShell CLI](#powershell-cli-1) ·
[Xây dựng từ mã nguồn](#xây-dựng-từ-mã-nguồn) ·
[Cấu trúc dự án](#cấu-trúc-dự-án) ·
[Tác giả](#tác-giả-1)

---

## Giới thiệu

**WinLic Manager** gồm hai công cụ song song — **ứng dụng GUI (.NET WPF)** và **script PowerShell CLI** — giúp bạn xem, kiểm tra và quản lý bản quyền Windows mà không cần gõ lệnh thủ công.

Cả hai công cụ đều hỗ trợ đầy đủ **8 tùy chọn** giống nhau, hoạt động trên **Windows 10/11**, sử dụng WMI, Registry và `slmgr.vbs` — **không cần cài thêm bất cứ thứ gì**.

---

## Tải về

Tải phiên bản mới nhất từ trang [**Releases**](https://github.com/ardennguyen/WinLic/releases):

| File | Mô tả |
|---|---|
| `WinLicApp-<version>.exe` | **GUI App** — chạy trực tiếp, không cần cài đặt |
| `WinLicApp-<version>.exe.sha256` | Kiểm tra toàn vẹn của file EXE |
| `WinLicApp-<version>.zip` | Bản nén của GUI App |
| `WinLicApp-<version>.zip.sha256` | Kiểm tra toàn vẹn của file ZIP |
| `WinLicManager-<version>.ps1` | **PowerShell CLI** — script độc lập |
| `WinLicManager-<version>.ps1.sha256` | Kiểm tra toàn vẹn của file PS1 |
| `WinLicPS-<version>.zip` | **PowerShell CLI bundle** — script + settings |
| `WinLicPS-<version>.zip.sha256` | Kiểm tra toàn vẹn của bộ cài CLI |

### Kiểm tra toàn vẹn (tùy chọn)

```powershell
# PowerShell
Get-FileHash .\WinLicApp-<version>.zip -Algorithm SHA256
# So sánh với nội dung file .sha256 tương ứng
```
```cmd
:: CMD / certutil
certutil -hashfile WinLicApp-<version>.zip SHA256
```

---

## Tính năng

Cả hai công cụ (GUI và CLI) đều cung cấp **8 tùy chọn** giống nhau:

| # | Tính năng | Quyền |
|---|-----------|-------|
| 1 | **Thông Tin Hệ Thống & Bản Quyền** — đọc thông tin OS, kênh kích hoạt, key OEM BIOS, phát hiện DE/KMS/Retail; tùy chọn quét mở rộng `/dlv` | Không cần Admin |
| 2 | **Kiểm Thử & Cài Key** — xác thực và cài key mới qua `slmgr /ipk`; tự động kích hoạt `/ato`; cảnh báo kênh; chẩn đoán lỗi chi tiết | **Admin** |
| 3 | **Gỡ Key Bản Quyền** — xóa key hiện tại bằng `slmgr /upk` + `/cpky`; xác nhận trước khi thực hiện | **Admin** |
| 4 | **Đặt Lại Kích Hoạt (Rearm)** — hiển thị số lần rearm còn lại (WMI); xác nhận; tùy chọn khởi động lại tự động | **Admin** |
| 5 | **Kiểm Tra Kích Hoạt Bên Thứ Ba** — quét **10 lớp** dấu hiệu; danh sách tùy chỉnh; kết quả 3 cấp (CRITICAL / SUSPICIOUS / CLEAN) | Không cần Admin |
| 6 | **Thay Đổi Kênh Kích Hoạt** — chuyển đổi giữa GVLK/KMS và Retail; tự cài GVLK đúng cho phiên bản; tự chuyển hướng sau thay đổi | **Admin** |
| 7 | **Kiểm Tra & Xóa Cài Đặt KMS** — đọc KMS host/port từ registry và `/dlv`; xóa cấu hình KMS; gợi ý bước tiếp theo | **Admin** |
| 8 | **Kích Hoạt KMS** — quy trình 6 bước kiểm tra trước: kênh, GVLK, DNS SRV, TCP 1688, đồng hồ, `/ato`; chẩn đoán mã lỗi | **Admin** |

> ⚠️ Tùy chọn **2, 3, 4, 6, 7, 8** thay đổi trạng thái bản quyền thật sự của Windows. Ứng dụng luôn yêu cầu xác nhận trước khi thực hiện.

---

### Tùy chọn 5 — Kiểm Tra Kích Hoạt Bên Thứ Ba (10 lớp quét)

| Lớp | Kiểm tra | Phát hiện |
|-----|----------|-----------|
| 1 | Dịch vụ hệ thống | KMSpico, KMSAuto, vlmcsd, KMS_VL_ALL, Activation-Renewal… |
| 2 | Tác vụ định kỳ | Tác vụ kích hoạt tự động từ công cụ bên thứ ba |
| 3 | Tiến trình đang chạy | Tiến trình kích hoạt đang hoạt động |
| 4 | Đường dẫn tệp | GenuineTicket.xml, gatherosstate.exe, Activation_task.cmd, data.dat… |
| 5 | Cổng KMS localhost | TCP 1688 và các cổng tùy chỉnh đang lắng nghe cục bộ |
| 6 | Registry KMS | Máy chủ KMS, cổng, IP giả `10.0.0.10`, WoW64 |
| 7 | GVLK & Kênh kích hoạt | GVLK vĩnh viễn + VOLUME = vi phạm; non-VOLUME + generic key = DE hợp lệ; phát hiện kích hoạt qua điện thoại |
| 8 | Ngày hết hạn | TSforge KMS4k (≥ 2100), KMS38 (≥ 2037), Online KMS 180 ngày (165–195 ngày) |
| 9 | Dấu thời gian SPP | `data.dat` so với ngày cài Windows + Event ID 19 từ Windows Update |
| 10 | Nhật ký sự kiện SPP | Event 12288/12289/12290/8198 — địa chỉ máy chủ KMS bên ngoài |

**Kết quả 3 cấp:** 🔴 CRITICAL (vi phạm xác nhận) / 🟡 SUSPICIOUS (có dấu hiệu) / 🟢 CLEAN

| Phương pháp kích hoạt | Phát hiện | Lý do |
|---|---|---|
| KMSpico / KMSAuto (đang chạy) | ✅ | Để lại dịch vụ, tác vụ, tập tin |
| vlmcsd / KMS_VL_ALL (đang chạy) | ✅ | Để lại dịch vụ, cổng KMS mở |
| Online KMS (kích hoạt qua internet) | ✅ | Thời gian ân hạn 180 ngày, dấu vết task/service, event log |
| KMS38 (còn dấu vết) | ✅ | GenuineTicket.xml, gatherosstate, ngày hết hạn 2038 |
| TSforge (còn dấu vết) | ⚠️ | data.dat bị sửa đổi bất thường |
| **MAS HWID / Digital Entitlement** | ❌ | Tạo Digital Entitlement thật — không để lại dấu vết |
| Công cụ đã gỡ sạch | ❌ | Không còn dấu vết sau khi gỡ |

---

### Tùy chọn 8 — Quy trình 6 bước Kích Hoạt KMS

```
Bước 1  Kiểm tra kênh        Phải là VOLUME_KMSCLIENT
Bước 2  Kiểm tra GVLK        Tự cài key đúng nếu thiếu
Bước 3  Tra cứu DNS SRV      _VLMCS._TCP → tự phát hiện máy chủ
Bước 4  Kiểm tra TCP 1688    Nhập thủ công nếu DNS thất bại
Bước 5  Đồng hồ hệ thống     Cảnh báo nếu lệch > ±5 phút
Bước 6  slmgr /ato           Kích hoạt + chẩn đoán mã lỗi
```

---

## Yêu cầu hệ thống

| | GUI App | PowerShell CLI |
|---|---|---|
| **OS** | Windows 10 (1903+) / Windows 11 x64 | Windows 10 / 11 |
| **.NET** | Framework 4.8 (tích hợp sẵn trên Win 10/11) | Không cần |
| **PowerShell** | Không cần | 5.1+ (tích hợp sẵn) hoặc 7+ |
| **Quyền khởi động** | Không cần Admin | Không cần Admin |
| **Quyền tùy chọn 2,3,4,6,7,8** | Tự nhắc UAC khi cần | Tự nhắc relaunch khi cần |

---

## GUI App

### Tải về & Chạy

1. Tải `WinLicApp-<version>.exe` hoặc `.zip` từ [**Releases**](https://github.com/ardennguyen/WinLic/releases)
2. **Nhấp đúp** vào `WinLicApp-<version>.exe` — không cần cài đặt
3. Chọn ngôn ngữ **EN** hoặc **VI** ở góc trên bên phải
4. Chọn tab **Windows 10/11** (tab đầu tiên, mặc định)
5. Nhấn vào các tùy chọn ở thanh bên trái để bắt đầu

> Tùy chọn 1, 5 hoạt động ngay không cần Admin. Khi chọn 2, 3, 4, 6, 7, 8 — ứng dụng sẽ tự nhắc nâng quyền qua UAC.

### Giao diện

- **Thanh tab** (kiểu Microsoft Edge) ở đầu cửa sổ: **Windows 10/11** và **Office 2019+** *(đang phát triển)*
- **Thanh bên trái**: 8 nút tùy chọn + hộp kiểm hiện key đầy đủ + nút Xóa nhật ký
- **Khu vực nhật ký** (phải): chia sẻ cho tất cả tùy chọn, giữ lại qua nâng quyền UAC
- **Các panel inline**: xác nhận nguy hiểm, nhập key, kiểm tra KMS… xuất hiện ngay trong cửa sổ chính thay vì hộp thoại riêng

### Cài đặt quét tùy chỉnh (Tùy chọn 5)

Nhấn nút **⚙ Cài đặt Kiểm tra** trong thanh bên để mở hộp thoại cấu hình. Thay đổi tự động lưu vào `settings.ini` cạnh file EXE.

Nhấn **↻ Cập Nhật Mặc Định** để tải danh sách mới nhất từ GitHub.

---

## PowerShell CLI

Dành cho power user muốn dùng công cụ qua command line. Hỗ trợ đầy đủ **song ngữ EN/VI**.

### Tải về & Chạy

1. Tải `WinLicPS-<version>.zip` từ [**Releases**](https://github.com/ardennguyen/WinLic/releases)
2. Giải nén vào một thư mục bất kỳ (`WinLicManager.ps1` và `settings.ini` cùng thư mục)
3. Chạy script:

```powershell
# Chạy trực tiếp (không cần Admin — tự nhắc khi chọn 2/3/4/6/7/8)
powershell -ExecutionPolicy Bypass -File .\WinLicManager.ps1

# Hoặc dùng PowerShell 7
pwsh -ExecutionPolicy Bypass -File .\WinLicManager.ps1

# Chạy với quyền Admin ngay từ đầu
Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File .\WinLicManager.ps1"
```

### Menu

```
  ╔══════════════════════════════════════════════════════╗
  ║         WinLic Manager v1.5                          ║
  ╠══════════════════════════════════════════════════════╣
  ║  1 -- System Info & License Status                   ║
  ║  2 -- Test & Install Product Key              [!]    ║
  ║  3 -- Remove License Key                      [!]    ║
  ║  4 -- Reset Activation (Rearm)                [!]    ║
  ║  5 -- 3rd Party Activation Audit                     ║
  ║  6 -- Change Activation Channel               [!]    ║
  ║  7 -- Check & Remove KMS Settings             [!]    ║
  ║  8 -- KMS Activation                          [!]    ║
  ║  U -- Update scan defaults from GitHub               ║
  ║  Q -- Quit                                           ║
  ╚══════════════════════════════════════════════════════╝
```

### Tùy chỉnh quét Tùy chọn 5 (settings.ini)

File `settings.ini` được đính kèm trong bộ cài CLI. Trước khi quét, CLI hiển thị tóm tắt cấu hình và hỏi có muốn chỉnh sửa trước không.

| Mục | Mô tả |
|---|---|
| `[ExtraPorts]` | Cổng TCP bổ sung để kiểm tra localhost |
| `[ExtraServices]` | Từ khóa tên dịch vụ bổ sung |
| `[ExtraTaskKeywords]` | Từ khóa tên tác vụ định kỳ bổ sung |
| `[ExtraProcesses]` | Từ khóa tên tiến trình bổ sung |
| `[ExtraFilePaths]` | Đường dẫn tập tin/thư mục bổ sung cần kiểm tra |
| `[UserKmsPiracyDomains]` | Tên miền KMS đáng ngờ bổ sung |
| `[UserGvlkKeys]` | GVLK key bổ sung để nhận diện |
| `[UserGenericKeys]` | Key HWID/DE placeholder bổ sung |

Nhập lệnh `U` trong menu để tải danh sách mặc định mới nhất từ GitHub mà vẫn giữ nguyên cài đặt tùy chỉnh của bạn.

---

## Xây dựng từ mã nguồn

### Yêu cầu

- [.NET SDK](https://dotnet.microsoft.com/download) (hỗ trợ .NET Framework 4.8 target)
- Windows 10/11

### Biên dịch GUI App

```bash
git clone https://github.com/ardennguyen/WinLic.git
cd WinLic
dotnet build WinLicApp/WinLicApp.csproj -c Release
```

EXE xuất ra tại: `WinLicApp/bin/Release/net4.8-windows/WinLicApp.exe`

### PowerShell CLI

Không cần biên dịch — chạy trực tiếp từ source:

```powershell
powershell -ExecutionPolicy Bypass -File .\WinLicPS\WinLicManager.ps1
```

---

## Cấu trúc dự án

```
WinLic/
├── WinLicApp/                       # Ứng dụng GUI (.NET Framework 4.8 WPF)
│   ├── MainWindow.xaml[.cs]         # Giao diện & logic chính, 8 tùy chọn + tab bar
│   ├── Localization.cs              # Bảng chuỗi song ngữ EN/VI
│   ├── AppSettings.cs               # Quản lý settings.ini (GVLK, ports, domains…)
│   ├── SettingsDialog.xaml[.cs]     # Hộp thoại Cài đặt Kiểm tra
│   ├── AboutDialog.xaml[.cs]        # Hộp thoại Giới thiệu + kiểm tra cập nhật
│   ├── App.xaml[.cs]                # Điểm khởi động ứng dụng
│   ├── app.manifest                 # UAC + DPI manifest
│   ├── winlic.ico / winlic_256.png  # Icon ứng dụng
│   └── WinLicApp.csproj             # Định nghĩa dự án .NET Framework 4.8 WPF
├── WinLicPS/                        # Công cụ PowerShell (CLI)
│   ├── WinLicManager.ps1            # Script chính — 8 tùy chọn song ngữ EN/VI
│   ├── settings.ini                 # Cấu hình quét (user block)
│   └── settings.default.ini        # Danh sách mặc định (tải từ GitHub)
└── README.md
```

---

## Tác giả

**Arden Nguyen Duc Huy**
- GitHub: [@ardennguyen](https://github.com/ardennguyen)
- Repository: [ardennguyen/WinLic](https://github.com/ardennguyen/WinLic)

---

## Giấy phép

Dự án này được phân phối theo giấy phép **MIT**. Xem file [LICENSE](LICENSE) để biết thêm chi tiết.

---

<div align="center">
  <sub>WinLic Manager — Made with ❤️ by Arden Nguyen Duc Huy</sub>
</div>
