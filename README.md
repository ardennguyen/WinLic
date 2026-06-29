# WinLic Manager

<div align="center">

![WinLic Manager](WinLicApp/winlic_256.png)

**Công cụ quản lý bản quyền Windows với giao diện đồ họa**

[![Version](https://img.shields.io/badge/phiên%20bản-1.0%20beta1-a78bfa?style=flat-square)](https://github.com/ardennguyen/WinLic/releases)
[![Platform](https://img.shields.io/badge/nền%20tảng-Windows%2010%2F11-7c3aed?style=flat-square)](https://github.com/ardennguyen/WinLic)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-6d28d9?style=flat-square)](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
[![License](https://img.shields.io/badge/giấy%20phép-MIT-6d28d9?style=flat-square)](LICENSE)

</div>

---

## Giới thiệu

**WinLic Manager** là ứng dụng Windows giúp bạn xem, kiểm tra và quản lý bản quyền Windows thông qua giao diện đồ họa thân thiện — thay thế cho việc dùng PowerShell hay command line thủ công.

Ứng dụng hỗ trợ đầy đủ **tiếng Anh** và **tiếng Việt**, hoạt động ổn định trên **Windows 10** và **Windows 11**. Sử dụng WMI, Registry và `slmgr.vbs` — **không cần cài đặt thêm bất cứ thứ gì**; .NET Framework 4.8 đã được tích hợp sẵn trong Windows 10/11.

---

## Tính năng

| # | Tính năng | Mô tả |
|---|-----------|-------|
| 1 | **Phiên bản OS & Key OEM BIOS** | Đọc thông tin hệ điều hành, kiểm tra Key Bản Quyền nhúng trong BIOS/UEFI và xác định ấn bản Windows tương ứng |
| 2 | **Kênh & Loại Bản Quyền** | Chạy `slmgr /dli` để xem tên, kênh phân phối và trạng thái kích hoạt |
| 3 | **Key & Trạng thái Kích Hoạt** | Hiển thị Key một phần đang hoạt động, Key dự phòng từ Registry, Key OEM BIOS; phát hiện phương thức kích hoạt (Digital Entitlement / KMS / Retail); báo cáo mở rộng `slmgr /dlv` tuỳ chọn |
| 4 | **Kiểm thử & Cài Key Bản Quyền** | Xác thực định dạng Key, cài đặt qua `slmgr /ipk` với phân tích lỗi chi tiết |
| 5 | **Gỡ Key Bản Quyền** | Gỡ và xoá Key hiện tại bằng `slmgr /upk` + `/cpky` |
| 6 | **Đặt Lại Kích Hoạt (Rearm)** | Đặt lại đếm ngược kích hoạt với `slmgr /rearm` |
| 7 | **Kiểm Tra Kích Hoạt Bên Thứ Ba** | Phát hiện KMS giả lập cục bộ, kiểm tra cổng TCP, dịch vụ, tác vụ định kỳ, tập tin và tiến trình đáng ngờ; cấu hình danh sách quét tùy chỉnh qua settings dialog |

---

## Phát hiện Digital Entitlement

WinLic Manager sử dụng thuật toán **kiểm tra kênh bản quyền trước** để phân biệt chính xác:

- **Digital Entitlement (DE)** — Bản quyền gắn phần cứng qua máy chủ Microsoft
- **KMS** — Bản quyền doanh nghiệp/tập thể
- **Retail / OEM** — Kích hoạt tiêu chuẩn bằng Key thật

> **Lưu ý:** Trên hệ thống DE, Key dự phòng và Key OEM BIOS có thể khác Key đang dùng — điều này hoàn toàn bình thường vì Microsoft gán một Key duy nhất từ cloud cho phần cứng của bạn.

---

## Kiểm tra kích hoạt bên thứ ba (Tùy chọn 7)

Ứng dụng quét **6 lớp dấu hiệu** để phát hiện KMS giả lập và công cụ kích hoạt lậu:

1. **Tên máy chủ KMS** — Registry + WMI: nếu trỏ `127.x.x.x` / `localhost` → KMS giả lập cục bộ
2. **Cổng KMS trên localhost** — Kết nối TCP thử nghiệm: cổng mở = KMS đang lắng nghe
3. **Dịch vụ hệ thống** — Tìm tên dịch vụ khớp với KMSpico, KMService, KMSELDI, vlmcsd…
4. **Tác vụ định kỳ** — Tìm task như AutoKMS, KMSAuto, KMS_VL_ALL…
5. **Đường dẫn tập tin** — Kiểm tra `%ProgramFiles%\KMSpico`, `KMSELDI.exe`, `\Windows\KMS\`…
6. **Tiến trình đang chạy** — Khớp tên với công cụ kích hoạt đã biết

Danh sách quét có thể **tùy chỉnh** qua nút **⚙ Cài đặt Kiểm tra** — bổ sung cổng, tên dịch vụ, tiến trình, và đường dẫn riêng. Cài đặt tùy chỉnh được lưu vào `settings.ini` cạnh file EXE.

### Giới hạn đã biết

| Loại công cụ | Phát hiện được không? | Lý do |
|---|---|---|
| KMSpico / KMSAuto (đang chạy) | ✅ Có | Để lại dịch vụ, tác vụ, tập tin |
| vlmcsd / KMS_VL_ALL (đang chạy) | ✅ Có | Để lại dịch vụ, cổng KMS mở |
| **MAS HWID / HWIDGEN** | ❌ Không | Tạo DE thật qua API Microsoft — không thể phân biệt |
| Công cụ đã gỡ sạch | ❌ Không | Không còn dấu vết sau khi gỡ cài đặt |
| Windows Loader (SLIC cũ) | ❌ Không | Sửa đổi firmware, cần phân tích BIOS |
| KMS38 (đã dọn) | ⚠️ Một phần | Chỉ phát hiện nếu máy chủ KMS vẫn trỏ localhost |

---

## Yêu cầu hệ thống

| | |
|---|---|
| **Hệ điều hành** | Windows 10 (1903+) hoặc Windows 11 (x64) |
| **Quyền** | Administrator — ứng dụng tự yêu cầu UAC khi khởi động |
| **.NET Framework** | 4.8 — **đã tích hợp sẵn** trong Windows 10/11; không cần cài thêm |
| **Phụ thuộc khác** | Không có — chỉ cần file EXE là đủ |

---

## Tải về & Sử dụng

### Tải về

Tải từ [**Releases**](https://github.com/ardennguyen/WinLic/releases):

| File | Mô tả |
|---|---|
| `WinLicApp-v1.0-beta1.zip` | **File chính** — giải nén và chạy, không cần cài đặt |
| `WinLicApp-v1.0-beta1.zip.sha256` | Kiểm tra toàn vẹn của file ZIP |
| `WinLicApp-v1.0-beta1.exe.sha256` | Kiểm tra toàn vẹn của file EXE bên trong ZIP |

### Kiểm tra toàn vẹn (tùy chọn)

```powershell
# PowerShell
Get-FileHash .\WinLicApp-v1.0-beta1.zip -Algorithm SHA256
# So sánh với nội dung WinLicApp-v1.0-beta1.zip.sha256
```
```bash
# Linux / macOS
sha256sum -c WinLicApp-v1.0-beta1.zip.sha256
```
```cmd
:: CMD / certutil
certutil -hashfile WinLicApp-v1.0-beta1.zip SHA256
```

### Chạy ứng dụng

1. Giải nén zip, **nhấp đúp** vào `WinLicApp-v1.0-beta1.exe`
2. Ứng dụng sẽ **tự yêu cầu quyền Administrator** qua UAC — hãy chấp nhận
3. Chọn ngôn ngữ **EN** hoặc **VI** ở góc trên bên phải
4. Nhấn vào các tùy chọn ở thanh bên trái để bắt đầu

### Lưu ý quan trọng

> ⚠️ Các tùy chọn **4, 5, 6** (Cài Key / Gỡ Key / Rearm) thay đổi trạng thái bản quyền thật sự của Windows. Chỉ dùng khi bạn biết chính xác mình đang làm gì.

---

## Xây dựng từ mã nguồn

### Yêu cầu

- [.NET SDK](https://dotnet.microsoft.com/download) (bất kỳ phiên bản nào hỗ trợ .NET Framework 4.8 target)
- Windows 10/11

### Biên dịch

```bash
git clone https://github.com/ardennguyen/WinLic.git
cd WinLic
dotnet build WinLicApp/WinLicApp.csproj -c Release
```

EXE xuất ra tại: `WinLicApp/bin/Release/net4.8-windows/WinLicApp.exe`

### Cấu trúc dự án

```
WinLic/
├── WinLicApp/
│   ├── MainWindow.xaml          # Giao diện chính
│   ├── MainWindow.xaml.cs       # Logic chính, 7 tùy chọn
│   ├── Localization.cs          # Bảng chuỗi EN/VIE
│   ├── AppSettings.cs           # Quản lý settings.ini (Option 7)
│   ├── SettingsDialog.xaml      # Hộp thoại Cài đặt Kiểm tra
│   ├── SettingsDialog.xaml.cs   # Logic hộp thoại cài đặt
│   ├── AboutDialog.xaml[.cs]    # Hộp thoại Giới thiệu
│   ├── InputDialog.xaml[.cs]    # Hộp thoại nhập Key
│   ├── App.xaml[.cs]            # Điểm khởi động ứng dụng
│   ├── app.manifest             # UAC + DPI manifest
│   ├── winlic.ico               # Icon ứng dụng
│   ├── winlic_256.png           # Icon 256px
│   └── WinLicApp.csproj         # Định nghĩa dự án .NET Framework 4.8 WPF
└── README.md
```

---

## Mã nguồn phiên bản hiện tại

| | |
|---|---|
| **Branch** | [`release/v1.0-beta1`](https://github.com/ardennguyen/WinLic/tree/release/v1.0-beta1) |
| **Commit** | [`b4738b0`](https://github.com/ardennguyen/WinLic/commit/b4738b0) |
| **Development** | [`main`](https://github.com/ardennguyen/WinLic/tree/main) |

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
  <sub>WinLic Manager v1.0 (beta1) — Made with ❤️ by Arden Nguyen Duc Huy</sub>
</div>
