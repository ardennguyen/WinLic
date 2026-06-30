# WinLic Manager

<div align="center">

![WinLic Manager](WinLicApp/winlic_256.png)

**Công cụ quản lý bản quyền Windows — GUI & PowerShell CLI**

[![Version](https://img.shields.io/badge/phiên%20bản-v1.3--beta1-a78bfa?style=flat-square)](https://github.com/ardennguyen/WinLic/releases)
[![Platform](https://img.shields.io/badge/nền%20tảng-Windows%2010%2F11-7c3aed?style=flat-square)](https://github.com/ardennguyen/WinLic)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-6d28d9?style=flat-square)](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
[![License](https://img.shields.io/badge/giấy%20phép-MIT-6d28d9?style=flat-square)](LICENSE)

</div>

---

**Điều hướng / Navigate:**
[Giới thiệu](#giới-thiệu) ·
[Tải về](#tải-về) ·
[Tính năng](#tính-năng) ·
[GUI App](#gui-app) ·
[PowerShell CLI](#powershell-cli) ·
[Xây dựng từ mã nguồn](#xây-dựng-từ-mã-nguồn) ·
[Cấu trúc dự án](#cấu-trúc-dự-án) ·
[Tác giả](#tác-giả)

---

## Giới thiệu

**WinLic Manager** gồm hai công cụ song song — **ứng dụng GUI (.NET WPF)** và **script PowerShell CLI** — giúp bạn xem, kiểm tra và quản lý bản quyền Windows mà không cần gõ lệnh thủ công.

Cả hai công cụ đều hỗ trợ đầy đủ **7 tùy chọn** giống nhau, hoạt động trên **Windows 10/11**, sử dụng WMI, Registry và `slmgr.vbs` — **không cần cài thêm bất cứ thứ gì**.

---

## Tải về

Tải phiên bản mới nhất từ trang [**Releases**](https://github.com/ardennguyen/WinLic/releases):

| File | Mô tả |
|---|---|
| `WinLicApp-<version>.zip` | **GUI App** — giải nén và chạy, không cần cài đặt |
| `WinLicApp-<version>.exe.sha256` | Kiểm tra toàn vẹn của file EXE |
| `WinLicApp-<version>.zip.sha256` | Kiểm tra toàn vẹn của file ZIP |
| `WinLicManager-<version>.ps1` | **PowerShell CLI** — script độc lập |
| `WinLicManager-<version>.ps1.sha256` | Kiểm tra toàn vẹn của file PS1 |
| `WinLicPS-<version>.zip` | **PowerShell CLI bundle** — script + settings.ini |
| `WinLicPS-<version>.zip.sha256` | Kiểm tra toàn vẹn của bộ cài CLI |

### Kiểm tra toàn vẹn (tùy chọn)

```powershell
# PowerShell
Get-FileHash .\WinLicApp-<version>.zip -Algorithm SHA256
# So sánh với nội dung file .sha256 tương ứng
```
```bash
# Linux / macOS
sha256sum -c WinLicApp-<version>.zip.sha256
```
```cmd
:: CMD / certutil
certutil -hashfile WinLicApp-<version>.zip SHA256
```

---

## Tính năng

Cả hai công cụ (GUI và CLI) đều cung cấp 7 tùy chọn giống nhau:

| # | Tính năng | Quyền |
|---|-----------|-------|
| 1 | **Phiên bản OS & Key OEM BIOS** — đọc thông tin hệ điều hành, key nhúng trong BIOS/UEFI | Không cần Admin |
| 2 | **Kênh & Loại Bản Quyền** — chạy `slmgr /dli`, xem kênh phân phối và trạng thái | Không cần Admin |
| 3 | **Key & Trạng thái Kích Hoạt** — WMI + Registry + `slmgr /dlv` tùy chọn; phát hiện Digital Entitlement / KMS / Retail | Không cần Admin |
| 4 | **Kiểm thử & Cài Key** — xác thực và cài key mới qua `slmgr /ipk`, phân tích lỗi chi tiết | **Admin** |
| 5 | **Gỡ Key Bản Quyền** — xóa key hiện tại bằng `slmgr /upk` + `/cpky` | **Admin** |
| 6 | **Đặt Lại Kích Hoạt (Rearm)** — reset đếm ngược kích hoạt với `slmgr /rearm` | **Admin** |
| 7 | **Kiểm Tra Kích Hoạt Bên Thứ Ba** — quét **9 lớp** dấu hiệu KMS, TSforge, KMS38; danh sách quét tùy chỉnh qua `settings.ini`; tự cập nhật từ GitHub | Không cần Admin |

> ⚠️ Tùy chọn **4, 5, 6** thay đổi trạng thái bản quyền thật sự của Windows. Ứng dụng sẽ yêu cầu xác nhận trước khi thực hiện. Chỉ dùng khi bạn biết chính xác mình đang làm gì.

### Phát hiện kích hoạt bên thứ ba (Tùy chọn 7)

Quét **9 lớp dấu hiệu** để phát hiện các phương pháp kích hoạt không được phép:

| Lớp | Kiểm tra | Phát hiện |
|-----|----------|-----------|
| 1 | Key GVLK (WMI PartialProductKey) | 30 key Volume/Setup đã biết; GVLK vĩnh viễn = dấu hiệu vi phạm |
| 2 | Thời gian ân hạn / hết hạn | Phân loại KMS38 (≥ 2037), TSforge (≥ 2100), Online KMS (165–195 ngày) |
| 3 | Dấu vết Online KMS | Dịch vụ, tác vụ, tiến trình, IP giả `10.0.0.10`, registry WoW64 |
| 4 | Dấu vết KMS38 | `GenuineTicket.xml`, `gatherosstate.exe`, `clipup.exe` |
| 5 | Dấu thời gian TSforge | `data.dat` so với ngày cài Windows + Event ID 19 |
| 6 | Nhật ký sự kiện SPP | Event 12290 — địa chỉ máy chủ KMS ngoài |
| 7 | Thông báo pháp lý | Giới hạn đã biết và trường hợp dương tính giả |

| Loại công cụ | Phát hiện | Lý do |
|---|---|---|
| KMSpico / KMSAuto (đang chạy) | ✅ | Để lại dịch vụ, tác vụ, tập tin |
| vlmcsd / KMS_VL_ALL (đang chạy) | ✅ | Để lại dịch vụ, cổng KMS mở |
| Online KMS (kích hoạt qua internet) | ✅ | Thời gian ân hạn 180 ngày, dấu vết task/service |
| KMS38 (còn dấu vết) | ✅ | `GenuineTicket.xml`, gatherosstate, ngày hết hạn 2038 |
| TSforge (còn dấu vết) | ⚠️ | `data.dat` bị sửa đổi — [LOW CONFIDENCE] |
| **MAS HWID / HWIDGEN** | ❌ | Tạo Digital Entitlement thật qua API Microsoft |
| Công cụ đã gỡ sạch | ❌ | Không còn dấu vết |

---

## Yêu cầu hệ thống

| | GUI App | PowerShell CLI |
|---|---|---|
| **OS** | Windows 10 (1903+) / Windows 11 x64 | Windows 10 / 11 |
| **.NET** | Framework 4.8 (tích hợp sẵn) | Không cần |
| **Quyền khởi động** | Không cần Admin | Không cần Admin |
| **Quyền tùy chọn 4/5/6** | Tự nhắc UAC khi cần | Tự nhắc relaunch khi cần |
| **Phụ thuộc khác** | Không có | PowerShell 5.1+ (có sẵn trên Win 10/11) |

---

## GUI App

### Tải về & Chạy

1. Tải `WinLicApp-<version>.zip` từ [**Releases**](https://github.com/ardennguyen/WinLic/releases)
2. Giải nén, **nhấp đúp** vào `WinLicApp-<version>.exe`
3. Chọn ngôn ngữ **EN** hoặc **VI** ở góc trên bên phải
4. Nhấn vào các tùy chọn ở thanh bên trái để bắt đầu

> Tùy chọn 1, 2, 3, 7 hoạt động ngay không cần Admin. Khi chọn 4/5/6, ứng dụng sẽ tự nhắc nâng quyền qua UAC.

### Cài đặt quét tùy chỉnh (Tùy chọn 7)

Nhấn nút **⚙ Cài đặt Kiểm tra** ngay dưới Tùy chọn 7 để mở hộp thoại cấu hình danh sách quét. Cài đặt tự động lưu vào `settings.ini` cạnh file EXE.

---

## PowerShell CLI

Dành cho power user muốn dùng công cụ qua command line.

### Tải về & Chạy

1. Tải `WinLicPS-<version>.zip` từ [**Releases**](https://github.com/ardennguyen/WinLic/releases)
2. Giải nén vào một thư mục bất kỳ (giữ nguyên cấu trúc — `WinLicManager.ps1` và `settings.ini` cùng thư mục)
3. Chạy script:

```powershell
# Chạy trực tiếp (không cần Admin — tự nhắc khi chọn 4/5/6)
powershell -ExecutionPolicy Bypass -File .\WinLicManager.ps1

# Chạy với quyền Admin ngay từ đầu
Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File .\WinLicManager.ps1"
```

### Menu

```
  1 -- OS Version & OEM BIOS Key
  2 -- License Channel & Type  (slmgr /dli)
  3 -- Inspect Keys & Activation Status
  4 -- Test & Install Product Key         [!]   ← nguy hiểm
  5 -- Uninstall License Key              [!]   ← nguy hiểm
  6 -- Reset Activation (Rearm)           [!]   ← nguy hiểm
  7 -- 3rd Party Activation Audit
  U -- Update scan defaults from GitHub
  Q -- Quit
```

### Tùy chỉnh quét Tùy chọn 7 (settings.ini)

File `settings.ini` được đính kèm trong bộ cài CLI. Mở và bỏ chú thích các dòng cần thiết:

```powershell
notepad .\settings.ini
```

File `settings.ini` phải nằm **cùng thư mục** với `WinLicManager.ps1`. Khi vào Tùy chọn 7, script sẽ hiển thị cấu hình hiện tại và hỏi có muốn chỉnh sửa trước khi quét không.

Các mục có thể tùy chỉnh:

| Mục | Mô tả |
|---|---|
| `[ExtraPorts]` | Cổng TCP bổ sung để thử kết nối localhost |
| `[ExtraServices]` | Từ khóa tên dịch vụ bổ sung |
| `[ExtraTaskKeywords]` | Từ khóa tên tác vụ định kỳ bổ sung |
| `[ExtraProcesses]` | Từ khóa tên tiến trình bổ sung |
| `[ExtraFilePaths]` | Đường dẫn tập tin/thư mục bổ sung cần kiểm tra |

---

## Xây dựng từ mã nguồn

### Yêu cầu

- [.NET SDK](https://dotnet.microsoft.com/download) (bất kỳ phiên bản nào hỗ trợ .NET Framework 4.8 target)
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
│   ├── MainWindow.xaml[.cs]         # Giao diện & logic chính, 7 tùy chọn
│   ├── Localization.cs              # Bảng chuỗi EN/VIE
│   ├── AppSettings.cs               # Quản lý settings.ini (Tùy chọn 7)
│   ├── SettingsDialog.xaml[.cs]     # Hộp thoại Cài đặt Kiểm tra
│   ├── AboutDialog.xaml[.cs]        # Hộp thoại Giới thiệu
│   ├── InputDialog.xaml[.cs]        # Hộp thoại nhập Key
│   ├── App.xaml[.cs]                # Điểm khởi động ứng dụng
│   ├── app.manifest                 # UAC + DPI manifest
│   ├── winlic.ico / winlic_256.png  # Icon ứng dụng
│   └── WinLicApp.csproj             # Định nghĩa dự án .NET Framework 4.8 WPF
├── WinLicPS/                        # Công cụ PowerShell (CLI)
│   ├── WinLicManager.ps1            # Script chính — gương 7 tùy chọn của GUI
│   └── settings.ini                 # Cấu hình quét Tùy chọn 7 (có hướng dẫn)
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
