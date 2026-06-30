using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;


namespace WinLicApp
{
    public partial class MainWindow : Window
    {
        // ── Color tokens — calibrated for white log background ──────────────────
        private static readonly SolidColorBrush ColSep    = Freeze("#e2dcf4");
        private static readonly SolidColorBrush ColAction = Freeze("#6d28d9");
        private static readonly SolidColorBrush ColCmd    = Freeze("#94a3b8");
        private static readonly SolidColorBrush ColFetch  = Freeze("#a8b4cc");
        private static readonly SolidColorBrush ColInfo   = Freeze("#2563eb");
        private static readonly SolidColorBrush ColOk     = Freeze("#15803d");
        private static readonly SolidColorBrush ColWarn   = Freeze("#b45309");
        private static readonly SolidColorBrush ColError  = Freeze("#b91c1c");
        private static readonly SolidColorBrush ColKey    = Freeze("#0e7490");
        private static readonly SolidColorBrush ColDiag   = Freeze("#92400e");
        private static readonly SolidColorBrush ColHelp   = Freeze("#1d4ed8");
        private static readonly SolidColorBrush ColData   = Freeze("#1e1b4b");
        private static readonly SolidColorBrush ColLabel  = Freeze("#6b7280");
        private static readonly SolidColorBrush ColDE     = Freeze("#7c3aed");

        private static SolidColorBrush Freeze(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private static readonly SolidColorBrush BrushOk   = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3d));
        private static readonly SolidColorBrush BrushWarn = new SolidColorBrush(Color.FromRgb(0xb4, 0x53, 0x09));

        // ── Known generic placeholder keys ────────────────────────────────────────
        private static readonly Dictionary<string, string> GenericKeys = new()
        {
            { "3V66T", "Windows 10/11 Pro  (VK7JG-NPHTM-C97JM-9MPGT-3V66T)" },
            { "3TTL4", "Windows 10/11 Home  (YTMG3-N6KGA-8B33D-XXYF2-3TTL4)" },
            { "WXCHW", "Windows 10/11 Home Single Language  (4CPRK-NM3K3-X6XXQ-RXX86-WXCHW)" },
            { "PR4Y7", "Windows Pro Education  (8PTT6-RNW4C-X6V77-D23ST-PR4Y7)" },
            { "2YV77", "Windows Pro Workstations  (DXG7C-N36C4-C4HTG-X4T3X-2YV77)" },
            { "8DEC2", "Windows Enterprise  (XGVPP-NMH47-7TTHJ-W3FW7-8DEC2)" },
            { "28UTV", "Windows Enterprise  (NPPR9-FWDCX-D2C8J-H8P65-28UTV)" },
            { "7CFBY", "Windows Education  (YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY)" },
        };

        private readonly bool _isAdmin;
        private bool _firstAction = true;

        // Session log temp file — used to preserve log across elevation relaunches.
        // %TEMP% resolves to the current user's private temp folder
        // (e.g. C:\Users\<user>\AppData\Local\Temp) and is always writable,
        // even for standard (non-admin) accounts.
        private static readonly string SessionLogPath =
            Path.Combine(Path.GetTempPath(), "winlic_session.log");

        // =========================================================================
        // Constructor
        // =========================================================================
        public MainWindow()
        {
            _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                           .IsInRole(WindowsBuiltInRole.Administrator);
            AppSettings.Load();
            InitializeComponent();
            RefreshLanguage();

            if (_isAdmin)
            {
                AdminStatusText.Text       = L.Get("AdminOk");
                AdminStatusText.Foreground = BrushOk;
                LogInfo(L.Get("Startup_Ready"));
            }
            else
            {
                AdminStatusText.Text       = L.Get("AdminWarn");
                AdminStatusText.Foreground = BrushWarn;
                BtnElevate.Visibility      = Visibility.Visible;
                LogWarn(L.Get("Startup_NoAdmin"));
            }

            // Restore log from the session before elevation (if any)
            RestoreSessionLog();
        }

        // =========================================================================
        // Language
        // =========================================================================
        private void RefreshLanguage()
        {
            Title                      = L.Get("AppTitle");
            AdminStatusText.Text       = _isAdmin ? L.Get("AdminOk") : L.Get("AdminWarn");
            AdminStatusText.Foreground = _isAdmin ? BrushOk : BrushWarn;
            BtnElevate.Content         = L.Get("BtnElevate");
            BtnAbout.Content           = L.Get("BtnAbout");

            BtnVersionInfo.Content     = L.Get("Btn1");
            BtnSlmgrDli.Content        = L.Get("Btn2");
            BtnInspectKeys.Content     = L.Get("Btn3");
            BtnTestKey.Content         = L.Get("Btn4");
            BtnRemoveLicense.Content   = L.Get("Btn5");
            BtnResetActivation.Content = L.Get("Btn6");
            BtnPiracyCheck.Content     = L.Get("Btn7");
            BtnAuditSettings.Content   = L.Get("BtnAuditSettings");
            BtnClear.Content           = L.Get("BtnClear");
            StatusBar.Text             = L.Get("Ready");

            BtnLangEN.Style  = (Style)(L.Current == Lang.EN
                ? FindResource("LangBtnActive") : FindResource("LangBtn"));
            BtnLangVIE.Style = (Style)(L.Current == Lang.VIE
                ? FindResource("LangBtnActive") : FindResource("LangBtn"));
        }

        private void BtnLangEN_Click(object sender, RoutedEventArgs e)
        { L.Current = Lang.EN;  RefreshLanguage(); }

        private void BtnLangVIE_Click(object sender, RoutedEventArgs e)
        { L.Current = Lang.VIE; RefreshLanguage(); }

        // =========================================================================
        // About
        // =========================================================================
        private void BtnAbout_Click(object sender, RoutedEventArgs e)
            => new AboutDialog { Owner = this }.ShowDialog();

        // =========================================================================
        // Option 7 — Audit settings
        // =========================================================================
        private void BtnAuditSettings_Click(object sender, RoutedEventArgs e)
            => new SettingsDialog { Owner = this }.ShowDialog();

        // =========================================================================
        // Elevation
        // =========================================================================
        private void Elevate()
        {
            // Save current log to %TEMP% so the elevated instance can restore it
            try
            {
                var sb = new StringBuilder();
                foreach (var block in LogDocument.Blocks)
                {
                    if (block is Paragraph p)
                    {
                        var text = new TextRange(p.ContentStart, p.ContentEnd).Text;
                        sb.AppendLine(text);
                    }
                }
                File.WriteAllText(SessionLogPath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* best effort — don't block elevation if save fails */ }

            try
            {
                Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName)
                { Verb = "runas", UseShellExecute = true });
            }
            catch (Exception ex) { LogError(L.Get("ElevateFail") + ex.Message); return; }
            Application.Current.Shutdown();
        }

        private void BtnElevate_Click(object sender, RoutedEventArgs e) => Elevate();

        // Restore log lines saved before the last elevation relaunch.
        // Uses a muted color so previous-session lines are visually distinct.
        private void RestoreSessionLog()
        {
            if (!File.Exists(SessionLogPath)) return;
            try
            {
                var lines = File.ReadAllLines(SessionLogPath, Encoding.UTF8);
                File.Delete(SessionLogPath);
                if (lines.Length == 0) return;

                LogSep();
                LogLine("  " + L.Get("LogRestored"), ColLabel, bold: true);
                LogSep();
                foreach (var line in lines)
                    if (!string.IsNullOrEmpty(line))
                        LogLine(line, ColLabel);
                LogSep();
            }
            catch { /* best effort */ }
        }

        // =========================================================================
        // Logging helpers
        // =========================================================================
        private void LogLine(string text, SolidColorBrush? brush = null,
                             bool bold = false, double size = 12)
        {
            var run  = new Run(text)
            {
                Foreground = brush ?? ColData,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize   = size
            };
            var para = new Paragraph(run) { Margin = new Thickness(0, 0, 0, 1) };
            LogDocument.Blocks.Add(para);
            LogBox.ScrollToEnd();
            var s = text.TrimStart();
            StatusBar.Text = s.Length > 90 ? s.Substring(0, 90) + "…" : s;
        }

        private void LogSep()
            => LogDocument.Blocks.Add(
               new Paragraph(new Run("  " + new string('─', 62)))
               { Margin = new Thickness(0), Foreground = ColSep });

        private void LogBlank()
            => LogDocument.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 4) });

        private void LogAction(string locKey)
        {
            if (!_firstAction) LogBlank();
            _firstAction = false;
            LogSep();
            LogLine($"  {L.Get(locKey)}", ColAction, bold: true, size: 13);
            LogSep();
        }

        private void LogCmd(string cmd)   => LogLine($"  $ {cmd}", ColCmd);
        private void LogFetch(string msg) => LogLine($"  … {msg}", ColFetch);
        private void LogInfo(string msg)  => LogLine($"  ℹ  {msg}", ColInfo);
        private void LogOk(string msg)    => LogLine($"  ✔  {msg}", ColOk,    bold: true);
        private void LogWarn(string msg)  => LogLine($"  ⚠  {msg}", ColWarn);
        private void LogError(string msg) => LogLine($"  ✘  {msg}", ColError, bold: true);
        private void LogKey(string msg)   => LogLine($"  🔑 {msg}", ColKey,   bold: true);
        private void LogDiag(string msg)  => LogLine($"     {msg}", ColDiag);
        private void LogHelp(string url)  => LogLine($"  📖 {url}", ColHelp);
        private void LogDE(string msg)    => LogLine($"  💡 {msg}", ColDE,    bold: true);

        private void LogData(string label, string value)
        {
            if (string.IsNullOrEmpty(label)) { LogLine($"     {value}", ColData); return; }
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 1) };
            var pad  = Math.Max(label.Length, 28);
            para.Inlines.Add(new Run($"     {label.PadRight(pad)}  ") { Foreground = ColLabel, FontSize = 12 });
            para.Inlines.Add(new Run(value)                           { Foreground = ColData,  FontSize = 12 });
            LogDocument.Blocks.Add(para);
            LogBox.ScrollToEnd();
        }

        // =========================================================================
        // WMI
        // =========================================================================
        private ManagementObjectCollection? WmiQuery(string wql)
        {
            try { return new ManagementObjectSearcher(wql).Get(); }
            catch (Exception ex) { LogError("WMI: " + ex.Message); return null; }
        }

        // =========================================================================
        // slmgr runner + output parser
        // =========================================================================
        private string SlmgrPath =>
            System.IO.Path.Combine(
                Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
                @"System32\slmgr.vbs");

        private string RunSlmgr(string option)
        {
            var path = SlmgrPath;
            if (!System.IO.File.Exists(path)) { LogError("slmgr.vbs not found: " + path); return ""; }
            var args = $"//nologo \"{path}\" {option}";
            LogCmd($"cscript.exe {args}");
            try
            {
                var psi = new ProcessStartInfo("cscript.exe", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                using var proc = Process.Start(psi)!;
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit();
                if (!string.IsNullOrEmpty(stderr)) LogWarn(stderr);
                return stdout;
            }
            catch (Exception ex) { LogError(ex.Message); return ""; }
        }

        private void LogSlmgrOutput(string raw)
        {
            foreach (var rawLine in raw.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var colon = line.IndexOf(':');
                if (colon > 0 && colon < 40)
                {
                    var lbl = line.Substring(0, colon).Trim();
                    var val = line.Substring(colon + 1).Trim();
                    if (lbl == "License Status")
                    {
                        if (val.StartsWith("Licensed") && !val.Contains("Unlicensed"))
                            LogOk(L.Get("O2_LicenseStatus") + val);
                        else
                            LogWarn(L.Get("O2_LicenseStatus") + val);
                    }
                    else { LogData(lbl + ":", val); }
                }
                else { LogLine($"     {line}", ColData); }
            }
        }

        // =========================================================================
        // Helpers
        // =========================================================================
        private static string MaskKey(string key)
        {
            var p = key.Split('-');
            return p.Length == 5 ? $"XXXXX-XXXXX-XXXXX-XXXXX-{p[4]}"
                                 : "XXXXX-XXXXX-XXXXX-XXXXX-" + (key.Length >= 5 ? key.Substring(key.Length - 5) : key);
        }

        private string LicenseStatusText(uint s) => s switch
        {
            0 => L.Get("LS_0"), 1 => L.Get("LS_1"), 2 => L.Get("LS_2"),
            3 => L.Get("LS_3"), 4 => L.Get("LS_4"), 5 => L.Get("LS_5"),
            6 => L.Get("LS_6"), _ => L.Get("LS_Unknown") + $" ({s})"
        };

        private bool AskShowFullKey(string ctx)
            => MessageBox.Show($"{ctx}\n\n{L.Get("ShowKey_Q")}",
                   L.Get("ShowKey_Title"), MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        private bool AskConfirm(string msg)
            => MessageBox.Show(msg, L.Get("Confirm_Title"),
                               MessageBoxButton.YesNo, MessageBoxImage.Warning)
               == MessageBoxResult.Yes;

        private string? PromptInput(string prompt, string title)
        {
            var d = new InputDialog(prompt, title) { Owner = this };
            return d.ShowDialog() == true ? d.InputValue : null;
        }

        private bool RequireAdmin()
        {
            if (_isAdmin) return true;

            // Offer to relaunch as admin — Elevate() closes this instance
            if (MessageBox.Show(
                    L.Get("ElevateFromOption"),
                    L.Get("ElevateTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Elevate();
            }
            return false;
        }

        /// <summary>
        /// Identify the Windows edition a product key belongs to.
        /// Checks the last-5 chars against the generic key table, then cross-references
        /// all SoftwareLicensingProduct WMI entries (read-only, no install attempt).
        /// </summary>
        private string? IdentifyKeyEdition(string fullKey)
        {
            var parts = fullKey.Split('-'); var last5 = parts[parts.Length - 1];
            if (GenericKeys.TryGetValue(last5, out var generic)) return generic;

            using var res = WmiQuery(
                "SELECT Name, PartialProductKey FROM SoftwareLicensingProduct");
            if (res == null) return null;
            foreach (ManagementObject obj in res)
            {
                var name    = obj["Name"]?.ToString() ?? "";
                var partial = obj["PartialProductKey"]?.ToString();
                if (!string.IsNullOrEmpty(partial) &&
                    fullKey.EndsWith(partial, StringComparison.OrdinalIgnoreCase) &&
                    name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }

        // ── Activation method detection ───────────────────────────────────────────
        private enum ActivationMethod { Unknown, DE, KMS, Standard }

        /// <summary>
        /// Channel-first detection strategy:
        ///  - VOLUME channel → check KMS fields (stale KMS fields on RETAIL systems are ignored).
        ///  - Non-VOLUME (RETAIL / OEM / …) → cannot be KMS.
        ///    Generic key = certain DE.
        ///    Non-generic + backup-key mismatch = strong DE signal (cloud key reassignment).
        ///    Non-generic + keys match = Standard retail/OEM.
        /// </summary>
        private static ActivationMethod DetectActivationMethod(
            ManagementObject obj, string? partialKey, string? regKey)
        {
            var desc = (obj["Description"]?.ToString() ?? "").ToUpperInvariant();

            // VOLUME channel only: can be KMS or MAK
            if (desc.Contains("VOLUME"))
            {
                var  kmsName = obj["DiscoveredKeyManagementServiceMachineName"]?.ToString() ?? "";
                uint kmsCnt  = 0;
                try { kmsCnt = Convert.ToUInt32(obj["KeyManagementServiceCurrentCount"] ?? 0); }
                catch { /* may be null or wrong type on some OS builds */ }

                return (!string.IsNullOrEmpty(kmsName) || kmsCnt > 0)
                    ? ActivationMethod.KMS
                    : ActivationMethod.Standard;
            }

            // Non-volume: RETAIL, OEM, or other — KMS is impossible here
            if (partialKey != null && GenericKeys.ContainsKey(partialKey))
                return ActivationMethod.DE;   // generic placeholder = confirmed DE

            // Real (non-generic) RETAIL key:
            // Backup-key tail ≠ active key tail → Microsoft cloud-assigned a new key = DE
            if (!string.IsNullOrEmpty(regKey) && !string.IsNullOrEmpty(partialKey))
            {
                if (!regKey.EndsWith(partialKey, StringComparison.OrdinalIgnoreCase))
                    return ActivationMethod.DE;
            }

            return ActivationMethod.Standard;
        }

        // =========================================================================
        // Option 1 — Version & BIOS OEM Key
        // =========================================================================
        private void BtnVersionInfo_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act1");

            LogFetch(L.Get("Fetch_OS"));
            using var osRes = WmiQuery(
                "SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem");
            if (osRes != null)
                foreach (ManagementObject obj in osRes)
                {
                    LogData(L.Get("D_OsEdition"),  obj["Caption"]?.ToString()        ?? "—");
                    LogData(L.Get("D_Version"),     obj["Version"]?.ToString()        ?? "—");
                    LogData(L.Get("D_Build"),       obj["BuildNumber"]?.ToString()    ?? "—");
                    LogData(L.Get("D_Arch"),        obj["OSArchitecture"]?.ToString() ?? "—");
                }

            LogBlank();
            LogFetch(L.Get("Fetch_BiosKey"));
            using var svcRes = WmiQuery("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
            string? oemKey = null;
            if (svcRes != null)
                foreach (ManagementObject obj in svcRes)
                    oemKey = obj["OA3xOriginalProductKey"]?.ToString();

            if (!string.IsNullOrWhiteSpace(oemKey))
            {
                bool full = AskShowFullKey(L.Get("O1_BiosDetectedCtx"));
                LogKey(L.Get("O1_BiosKey") + (full ? oemKey : MaskKey(oemKey)));

                LogBlank();
                LogFetch(L.Get("Fetch_OemEdition"));
                var edition = IdentifyKeyEdition(oemKey);
                if (!string.IsNullOrEmpty(edition))
                    LogOk(L.Get("OemEd_Found") + "  " + edition);
                else
                {
                    LogInfo(L.Get("OemEd_NoMatch"));
                    LogDiag(L.Get("OemEd_Hint"));
                }
            }
            else
            {
                LogInfo(L.Get("O1_BiosNone"));
            }
        }

        // =========================================================================
        // Option 2 — slmgr /dli
        // =========================================================================
        private void BtnSlmgrDli_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act2");
            LogInfo(L.Get("O2_Note"));
            LogBlank();
            LogFetch("Running  slmgr /dli…  (slmgr: Software Licensing Manager)");
            var output = RunSlmgr("/dli");
            if (string.IsNullOrWhiteSpace(output))
                LogWarn(L.Get("O2_NoOutput"));
            else
                LogSlmgrOutput(output);
        }

        // =========================================================================
        // Option 3 — Inspect Active & Backup Keys
        // =========================================================================
        private void BtnInspectKeys_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act3");

            // Read registry backup key FIRST — required for channel-first DE detection
            LogFetch(L.Get("Fetch_RegKey"));
            string? regKey = null;
            try
            {
                using var rk = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform");
                regKey = rk?.GetValue("BackupProductKeyDefault")?.ToString();
            }
            catch (Exception ex) { LogError(L.Get("O3_RegReadErr") + ex.Message); }

            // Query active Windows license
            LogFetch(L.Get("Fetch_License"));
            string? partialKey  = null;
            bool    foundActive = false;
            bool    isLicensed  = false;
            string? kmsServer   = null;
            var     activationMethod = ActivationMethod.Unknown;

            using var licRes = WmiQuery(
                "SELECT Name,Description,PartialProductKey,LicenseStatus," +
                "DiscoveredKeyManagementServiceMachineName,KeyManagementServiceCurrentCount " +
                "FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL");

            if (licRes != null)
            {
                foreach (ManagementObject obj in licRes)
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    if (!name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)) continue;

                    foundActive = true;
                    partialKey  = obj["PartialProductKey"]?.ToString();
                    uint raw    = obj["LicenseStatus"] is uint u ? u : 0;
                    isLicensed  = (raw == 1);
                    kmsServer   = obj["DiscoveredKeyManagementServiceMachineName"]?.ToString();

                    activationMethod = DetectActivationMethod(obj, partialKey, regKey);

                    LogData(L.Get("D_Edition"),    name);
                    LogData(L.Get("D_Channel"),    obj["Description"]?.ToString() ?? "—");
                    LogData(L.Get("D_PartialKey"), partialKey ?? "—");

                    if (isLicensed) LogOk(L.Get("O3_Activation") + LicenseStatusText(raw));
                    else            LogWarn(L.Get("O3_Activation") + LicenseStatusText(raw));
                    break;
                }
            }
            if (!foundActive) LogWarn(L.Get("O3_NoLicense"));

            // Activation method block
            LogBlank();
            switch (activationMethod)
            {
                case ActivationMethod.DE:
                    if (isLicensed)
                    {
                        LogDE(L.Get("DE_Confirmed"));
                        LogDiag(L.Get("DE_Explain1"));
                        LogDiag(L.Get("DE_Explain2"));
                        LogDiag(L.Get("DE_Explain3"));
                        LogDiag(L.Get("DE_KeyMismatch"));
                        LogDiag(L.Get("DE_Verify"));
                    }
                    else
                    {
                        LogWarn(L.Get("DE_NotActivated"));
                        LogDiag(L.Get("DE_Verify"));
                    }
                    break;

                case ActivationMethod.KMS:
                    LogOk(L.Get("KMS_Detected"));
                    if (!string.IsNullOrEmpty(kmsServer))
                        LogData(L.Get("KMS_Server"), kmsServer);
                    break;

                case ActivationMethod.Standard:
                    if (isLicensed) LogOk(L.Get("MAK_Detected"));
                    else            LogWarn(L.Get("MAK_Detected"));
                    break;
            }

            // BIOS OEM key
            LogBlank();
            LogFetch(L.Get("Fetch_BiosKey2"));
            string? oemKey = null;
            using var svcRes = WmiQuery("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
            if (svcRes != null)
                foreach (ManagementObject obj in svcRes)
                    oemKey = obj["OA3xOriginalProductKey"]?.ToString();

            bool hasOem = !string.IsNullOrWhiteSpace(oemKey);
            LogData(L.Get("D_BiosOemKey"),
                    hasOem ? L.Get("O3_BiosDetected") : L.Get("O3_BiosNone"));

            if (hasOem)
            {
                LogFetch(L.Get("Fetch_OemEdition"));
                var ed = IdentifyKeyEdition(oemKey!);
                if (!string.IsNullOrEmpty(ed)) LogOk(L.Get("OemEd_Found") + "  " + ed);
                else                           LogInfo(L.Get("OemEd_NoMatch"));
            }

            // Registry backup key
            bool hasReg = !string.IsNullOrWhiteSpace(regKey);
            LogData(L.Get("D_RegBackupKey"),
                    hasReg ? L.Get("O3_BiosDetected") : L.Get("O3_RegNone"));

            // Reveal keys
            if (hasOem || hasReg)
            {
                LogBlank();
                bool full = AskShowFullKey(L.Get("O3_KeysFoundCtx"));
                if (hasOem) LogKey(L.Get("O3_KeyBios") + (full ? oemKey! : MaskKey(oemKey!)));
                if (hasReg) LogKey(L.Get("O3_KeyReg")  + (full ? regKey! : MaskKey(regKey!)));
            }

            // Mismatch report — DE systems always have a mismatch (expected behavior)
            if (hasReg && !string.IsNullOrEmpty(partialKey))
            {
                LogBlank();
                bool match = regKey!.EndsWith(partialKey, StringComparison.OrdinalIgnoreCase);

                if (!match && activationMethod == ActivationMethod.DE)
                {
                    // For DE: mismatch is expected — cloud key assignment differs from original
                    LogInfo(L.Get("DE_KeyMismatch"));
                }
                else if (!match)
                {
                    LogWarn(L.Get("O3_Mismatch"));
                    LogDiag(L.Get("O3_MismatchReason"));
                    LogDiag(L.Get("O3_ActivePartial") + partialKey);
                    { var rp = regKey.Split('-'); LogDiag(L.Get("O3_BackupEnds") + rp[rp.Length - 1]); }

                    if (_isAdmin && AskConfirm(L.Get("O3_ConfirmRemove")))
                    {
                        try
                        {
                            using var rk = Registry.LocalMachine.OpenSubKey(
                                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform",
                                writable: true);
                            rk?.DeleteValue("BackupProductKeyDefault", throwOnMissingValue: false);
                            LogOk(L.Get("O3_RegKeyRemoved"));
                        }
                        catch (Exception ex) { LogError(L.Get("O3_RemoveErr") + ex.Message); }
                    }
                }
                else { LogOk(L.Get("O3_KeyMatch")); }
            }

            // Optional /dlv
            if (MessageBox.Show(L.Get("O3_DlvQ"), L.Get("O3_DlvTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes)
            {
                LogBlank();
                LogSlmgrOutput(RunSlmgr("/dlv"));
            }
        }

        // =========================================================================
        // Option 4 — Test & Apply Key
        // =========================================================================
        private void BtnTestKey_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act4");
            if (!RequireAdmin()) return;

            LogInfo(L.Get("O4_Info1"));
            LogInfo(L.Get("O4_Info2"));
            LogBlank();

            var key = PromptInput(L.Get("O4_Prompt"), L.Get("O4_PromptTitle"));
            if (string.IsNullOrWhiteSpace(key)) { LogInfo(L.Get("O4_Cancelled")); return; }

            key = key.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(key,
                @"^[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}$"))
            { LogError(L.Get("O4_BadFormat")); return; }

            bool showFull = AskShowFullKey(L.Get("O4_ShowKeyCtx"));
            LogInfo(L.Get("O4_Installing") + (showFull ? key : MaskKey(key)));
            LogBlank();

            var output = RunSlmgr($"/ipk {key}");
            LogSlmgrOutput(output);

            bool fail = output.Contains("Error") || output.Contains("0x");
            LogBlank();
            if (!fail)
            {
                LogOk(L.Get("O4_Success1"));
                LogInfo(L.Get("O4_Success2"));
            }
            else
            {
                LogError(L.Get("O4_Fail"));
                if (output.Contains("0xC004F069"))      LogDiag(L.Get("O4_DiagSku"));
                else if (output.Contains("0xC004F050")) LogDiag(L.Get("O4_DiagInvalid"));
                else if (output.Contains("0xC004C003")) LogDiag(L.Get("O4_DiagBlocked"));
                else                                     LogDiag(L.Get("O4_DiagGeneral"));
                LogBlank();
                LogHelp("https://support.microsoft.com/help/10738");
                LogHelp("https://learn.microsoft.com/en-us/windows-server/get-started/activation-error-codes");
            }
        }

        // =========================================================================
        // Option 5 — Uninstall Key
        // =========================================================================
        private void BtnRemoveLicense_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act5");
            if (!RequireAdmin()) return;

            if (!AskConfirm(L.Get("O5_Confirm")))
            { LogInfo(L.Get("O5_Cancelled")); return; }

            LogBlank();
            LogInfo(L.Get("O5_Uninstalling"));
            LogSlmgrOutput(RunSlmgr("/upk"));

            LogBlank();
            LogInfo(L.Get("O5_Clearing"));
            LogSlmgrOutput(RunSlmgr("/cpky"));

            LogBlank();
            LogOk(L.Get("O5_Done"));
        }

        // =========================================================================
        // Option 6 — Reset / Rearm
        // =========================================================================
        private void BtnResetActivation_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act6");
            if (!RequireAdmin()) return;

            if (!AskConfirm(L.Get("O6_Confirm")))
            { LogInfo(L.Get("O6_Cancelled")); return; }

            LogBlank();
            LogInfo(L.Get("O6_Rearming"));
            LogSlmgrOutput(RunSlmgr("/rearm"));

            LogBlank();
            LogOk(L.Get("O6_Done"));

            if (AskConfirm(L.Get("O6_RestartQ")))
            {
                LogInfo(L.Get("O6_Restarting"));
                Process.Start("shutdown.exe", "/r /t 5 /c \"WinLicManager rearm restart\"");
            }
        }

        // =========================================================================
        // Option 7 — 3rd-party activation audit
        // =========================================================================
        private void BtnPiracyCheck_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act7");

            // ── Verbose preamble: what this scan covers ────────────────────────
            LogLine($"  {L.Get("P7_Header")}", ColAction, bold: true);
            LogDiag(L.Get("P7_CanDetect1"));
            LogDiag(L.Get("P7_CanDetect2"));
            LogDiag(L.Get("P7_CanDetect3"));
            LogDiag(L.Get("P7_CanDetect4"));
            LogDiag(L.Get("P7_CanDetect5"));
            LogDiag(L.Get("P7_CanDetect6"));
            LogBlank();
            LogLine($"  {L.Get("P7_LimitHeader")}", ColWarn, bold: true);
            LogDiag(L.Get("P7_Limit1"));
            LogDiag(L.Get("P7_Limit2"));
            LogDiag(L.Get("P7_Limit3"));
            LogDiag(L.Get("P7_Limit4"));
            LogBlank();

            // Show KMS piracy domain count (built-in + user additions from settings.ini)
            int builtInDomains = AppSettings.DefaultKmsPiracyDomains.Length;
            int extraDomains   = AppSettings.ExtraKmsPiracyDomains.Count;
            LogDiag(string.Format(L.Get("P7_KmsDomainCount"),
                builtInDomains, extraDomains, builtInDomains + extraDomains));

            LogSep();
            LogBlank();

            int suspiciousCount = 0;
            bool criticalKms   = false;

            // ── 1. KMS server name (registry + WMI) ─────────────────────────────
            LogFetch(L.Get("Fetch_KmsHost"));
            LogDiag(L.Get("P7_KmsExplain"));
            string? kmsHost = null;
            try
            {
                using var rk = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform");
                kmsHost = rk?.GetValue("KeyManagementServiceName")?.ToString();
            }
            catch { }

            // Also check WoW64 registry path
            if (string.IsNullOrEmpty(kmsHost))
            {
                try
                {
                    using var rk2 = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform");
                    kmsHost = rk2?.GetValue("KeyManagementServiceName")?.ToString();
                }
                catch { }
            }

            // Also check WMI SoftwareLicensingService
            if (string.IsNullOrEmpty(kmsHost))
            {
                using var svcR = WmiQuery("SELECT KeyManagementServiceMachine FROM SoftwareLicensingService");
                if (svcR != null)
                    foreach (ManagementObject o in svcR)
                        kmsHost = o["KeyManagementServiceMachine"]?.ToString();
            }

            if (string.IsNullOrEmpty(kmsHost))
            {
                LogOk(L.Get("P7_KmsNone"));
            }
            else
            {
                LogData(L.Get("P7_KmsName"), kmsHost);

                // 1a. Local loopback — local KMS emulator (KMSpico, vlmcsd…)
                bool isLocal = kmsHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                            || kmsHost.StartsWith("127.") || kmsHost.StartsWith("::1")
                            || kmsHost == "0.0.0.0";

                // 1b. MAS bogus placeholder IP — 10.0.0.10 is non-routable, used when no renewal task installed
                bool isBogusPlaceholder = kmsHost == "10.0.0.10";

                // 1c. Microsoft-operated Azure KMS (only valid inside Azure VMs)
                bool isMsOfficial = kmsHost.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase)
                                 || kmsHost.EndsWith(".windows.net",   StringComparison.OrdinalIgnoreCase);

                // 1c. Private / corporate network (RFC 1918 IPs or internal hostnames)
                bool isPrivateIp   = Regex.IsMatch(kmsHost,
                    @"^10\.|^172\.(1[6-9]|2[0-9]|3[01])\.|^192\.168\.");
                bool isPrivateHost = !isPrivateIp
                                  && !IPAddress.TryParse(kmsHost, out _)
                                  && (kmsHost.EndsWith(".local",    StringComparison.OrdinalIgnoreCase)
                                   || kmsHost.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
                                   || kmsHost.EndsWith(".corp",     StringComparison.OrdinalIgnoreCase)
                                   || kmsHost.EndsWith(".lan",      StringComparison.OrdinalIgnoreCase)
                                   || kmsHost.EndsWith(".intranet", StringComparison.OrdinalIgnoreCase)
                                   || !kmsHost.Contains('.'));

                // 1d. Known piracy cloud KMS providers (from settings.ini + built-in defaults)
                string[] knownPiracyDomains = AppSettings.AllKmsPiracyDomains;
                bool isKnownPiracy = knownPiracyDomains
                    .Any(d => kmsHost.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0);

                if (isLocal)
                {
                    LogError(L.Get("P7_KmsLocal"));
                    criticalKms = true;
                    suspiciousCount++;
                }
                else if (isBogusPlaceholder)
                {
                    LogError(L.Get("P7_KmsBogusIp"));
                    criticalKms = true;
                    suspiciousCount++;
                }
                else if (isKnownPiracy)
                {
                    LogError(L.Get("P7_KmsKnownPiracy"));
                    LogError($"  Domain: {kmsHost}");
                    criticalKms = true;
                    suspiciousCount++;
                    CheckKmsHostDns(kmsHost);
                }
                else if (isMsOfficial)
                {
                    LogOk(L.Get("P7_KmsMsOfficial"));
                    LogInfo(L.Get("P7_KmsMsOfficialNote"));
                    CheckKmsHostDns(kmsHost);
                }
                else if (isPrivateIp || isPrivateHost)
                {
                    LogInfo(L.Get("P7_KmsCorporate"));
                }
                else
                {
                    // Public internet, unknown domain — cloud piracy KMS
                    LogError(L.Get("P7_KmsCloudPiracy"));
                    LogError($"  Server: {kmsHost}");
                    criticalKms = true;
                    suspiciousCount++;
                    CheckKmsHostDns(kmsHost);
                }
            }

            // ── 2. Probe localhost on all configured KMS port(s) ──────────────────
            LogFetch(L.Get("Fetch_Port1688"));
            LogDiag(L.Get("P7_Port1688Explain"));
            foreach (var port in AppSettings.AllPorts)
            {
                bool open = false;
                try
                {
                    using var tcp = new TcpClient();
                    var ar = tcp.BeginConnect("127.0.0.1", port, null, null);
                    open = ar.AsyncWaitHandle.WaitOne(800) && tcp.Connected;
                    try { tcp.EndConnect(ar); } catch { }
                }
                catch { }
                if (open)
                {
                    LogError(string.Format(L.Get("P7_PortOpen"), port));
                    criticalKms = true;
                    suspiciousCount++;
                }
                else
                {
                    LogOk(string.Format(L.Get("P7_PortClosed"), port));
                }
            }

            // ── 3. Suspicious services ───────────────────────────────────
            LogFetch(L.Get("Fetch_Services"));
            LogDiag(L.Get("P7_ServiceExplain"));
            var susServices = AppSettings.AllServices;
            var foundServices = new List<string>();
            using var svcRes = WmiQuery(
                "SELECT Name,DisplayName FROM Win32_Service");
            if (svcRes != null)
                foreach (ManagementObject obj in svcRes)
                {
                    var nm = obj["Name"]?.ToString() ?? "";
                    var dn = obj["DisplayName"]?.ToString() ?? "";
                    if (susServices.Any(s =>
                            nm.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            dn.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                        foundServices.Add($"{nm}  ({dn})");
                }
            if (foundServices.Count == 0)
                LogOk(L.Get("P7_NoServices"));
            else
                foreach (var s in foundServices)
                { LogWarn(L.Get("P7_ServiceFound") + "  " + s); suspiciousCount++; }

            // ── 4. Suspicious scheduled tasks ───────────────────────────
            LogFetch(L.Get("Fetch_Tasks"));
            LogDiag(L.Get("P7_TaskExplain"));
            var susTaskKeywords = AppSettings.AllTaskKeywords;
            var foundTasks = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("schtasks", "/query /fo csv /nh")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false,       CreateNoWindow = true
                };
                using var proc = Process.Start(psi)!;
                var lines = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                foreach (var line in lines.Split('\n'))
                {
                    if (susTaskKeywords.Any(k =>
                            line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        var parts = line.Split(',');
                        var taskName = parts.Length > 0
                            ? parts[0].Trim('"', ' ', '\r') : line.Trim();
                        foundTasks.Add(taskName);
                    }
                }
            }
            catch { }
            if (foundTasks.Count == 0)
                LogOk(L.Get("P7_NoTasks"));
            else
                foreach (var t in foundTasks)
                { LogWarn(L.Get("P7_TaskFound") + "  " + t); suspiciousCount++; }

            // ── 5. Known file/folder paths ───────────────────────────────
            LogFetch(L.Get("Fetch_Files"));
            LogDiag(L.Get("P7_FileExplain"));
            var pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var apd  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var pgd  = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var win  = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            var sys  = System.IO.Path.Combine(win, "System32");

            // Build the built-in suspicious path list
            var builtInPaths = new (string Path, string Tool)[]
            {
                (System.IO.Path.Combine(pf,   "KMSpico"),       "KMSpico"),
                (System.IO.Path.Combine(pf86, "KMSpico"),       "KMSpico"),
                (System.IO.Path.Combine(apd,  "KMSpico"),       "KMSpico"),
                (System.IO.Path.Combine(pgd,  "KMSpico"),       "KMSpico"),
                (System.IO.Path.Combine(sys,  "KMSELDI.exe"),   "KMSpico/ELDI"),
                (System.IO.Path.Combine(pf,   "KMSAuto Net"),   "KMSAuto Net"),
                (System.IO.Path.Combine(pf86, "KMSAuto Net"),   "KMSAuto Net"),
                (System.IO.Path.Combine(pf,   "KMSAuto"),       "KMSAuto"),
                (System.IO.Path.Combine(pf86, "KMSAuto"),       "KMSAuto"),
                (System.IO.Path.Combine(win,  "KMS"),           "KMS tools folder"),
                (System.IO.Path.Combine(sys,  "SppExtComObj.exe.bak"), "Patched SPP backup"),
                (System.IO.Path.Combine(pf,   "AAct"),          "AAct"),
                (System.IO.Path.Combine(pf86, "AAct"),          "AAct"),
                // KMS38 / ClipSVC artifact
                (@"C:\ProgramData\Microsoft\Windows\ClipSVC\GenuineTicket\GenuineTicket.xml", "KMS38 GenuineTicket"),
                // MAS Online KMS renewal task artifacts
                (@"C:\Program Files\Activation-Renewal\Activation_task.cmd", "MAS Online KMS renewal task"),
                (@"C:\Program Files\Activation-Renewal\Info.txt",            "MAS Online KMS renewal info"),
            };

            // Merge with user-added paths
            var allPaths = builtInPaths.ToList();
            foreach (var extra in AppSettings.ExtraFilePaths)
                allPaths.Add((extra, "[Custom]"));

            var suspiciousPaths = allPaths.ToArray();

            var foundFiles = new List<string>();
            foreach (var (path, tool) in suspiciousPaths)
            {
                if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path))
                    foundFiles.Add($"{tool}  →  {path}");
            }
            if (foundFiles.Count == 0)
                LogOk(L.Get("P7_NoFiles"));
            else
                foreach (var f in foundFiles)
                { LogWarn(L.Get("P7_FileFound") + "  " + f); suspiciousCount++; }

            // ── 6. Suspicious running processes ───────────────────────────
            LogFetch(L.Get("Fetch_Procs"));
            LogDiag(L.Get("P7_ProcExplain"));
            var susProcNames = AppSettings.AllProcesses;
            var foundProcs = new List<string>();
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (susProcNames.Any(n =>
                                proc.ProcessName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                            foundProcs.Add($"{proc.ProcessName}  (PID {proc.Id})");
                    }
                    catch { }
                }
            }
            catch { }
            if (foundProcs.Count == 0)
                LogOk(L.Get("P7_NoProcs"));
            else
                foreach (var p in foundProcs)
                { LogWarn(L.Get("P7_ProcFound") + "  " + p); suspiciousCount++; }

            // ── 7. GVLK + Activation Channel Check (WMI) ────────────────
            LogBlank();
            LogSep();
            LogFetch(L.Get("Fetch_ActChannel"));
            LogDiag(L.Get("P7_GvlkExplain"));
            LogDiag(string.Format(L.Get("SD_GvlkCount"), AppSettings.AllGvlkSuffixes.Count));
            try
            {
                using var licSearch = WmiQuery(
                    "SELECT PartialProductKey, LicenseStatus, Description, GracePeriodRemaining " +
                    "FROM Win32_SoftwareLicensingProduct " +
                    "WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f'");
                bool foundAnyKey = false;
                if (licSearch != null)
                {
                    foreach (ManagementObject mo in licSearch)
                    {
                        string? ppk = mo["PartialProductKey"]?.ToString();
                        if (string.IsNullOrWhiteSpace(ppk)) continue;
                        foundAnyKey = true;

                        uint licStatus = 0;
                        try { licStatus = Convert.ToUInt32(mo["LicenseStatus"]); } catch { }
                        uint graceMins = 0;
                        try { graceMins = Convert.ToUInt32(mo["GracePeriodRemaining"]); } catch { }
                        string desc = mo["Description"]?.ToString() ?? "";

                        bool isLicensed  = licStatus == 1;
                        bool isPermanent = graceMins == 0 && isLicensed;
                        bool isGvlk      = AppSettings.AllGvlkSuffixes.Contains(ppk);
                        bool isPhone     = desc.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0;

                        // 7a. Phone activation anomaly (TSforge ZeroCID indicator)
                        if (isPhone && isLicensed)
                        {
                            LogError(L.Get("P7_PhoneChannel"));
                            criticalKms = true;
                            suspiciousCount++;
                        }

                        // 7b. GVLK + permanent = piracy (legitimate KMS always has 180-day countdown)
                        if (isGvlk && isPermanent)
                        {
                            LogError(string.Format(L.Get("P7_GvlkPermanent"), ppk));
                            criticalKms = true;
                            suspiciousCount++;
                        }
                        else if (isGvlk)
                        {
                            LogOk(string.Format(L.Get("P7_GvlkWithKms"), ppk));
                        }
                        else
                        {
                            LogOk(L.Get("P7_NoGvlk"));
                        }

                        // 7c. Office KMS registry bonus check
                        try
                        {
                            string? offKms = null;
                            using var offRk = Registry.LocalMachine.OpenSubKey(
                                @"SOFTWARE\Microsoft\OfficeSoftwareProtectionPlatform");
                            offKms = offRk?.GetValue("KeyManagementServiceName")?.ToString();
                            if (!string.IsNullOrWhiteSpace(offKms))
                            {
                                bool offPiracy = AppSettings.AllKmsPiracyDomains
                                    .Any(d => offKms.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0);
                                bool offExternal =
                                    !Regex.IsMatch(offKms, @"^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.|127\.)") &&
                                    !offKms.Equals("localhost", StringComparison.OrdinalIgnoreCase);
                                if (offPiracy || offExternal)
                                {
                                    LogWarn(string.Format(L.Get("P7_OfficeKmsFound"), offKms));
                                    suspiciousCount++;
                                }
                            }
                        }
                        catch { }

                        // ── 8. Expiry / grace period analysis
                        LogBlank();
                        LogSep();
                        LogFetch("\u2467 Analyzing activation expiry (WMI)…");
                        LogDiag(L.Get("P7_ExpiryExplain"));

                        if (graceMins == 0 && isLicensed)
                        {
                            LogInfo(L.Get("P7_ExpiryPermanent"));
                        }
                        else if (graceMins > 0)
                        {
                            var expiry = DateTime.Now.AddMinutes(graceMins);
                            if (expiry.Year >= 2100)
                            {
                                LogError(string.Format(L.Get("P7_TsforgeExpiry"), expiry.Year));
                                criticalKms = true;
                                suspiciousCount++;
                            }
                            else if (expiry.Year >= 2037)
                            {
                                LogError(string.Format(L.Get("P7_Kms38Expiry"), expiry.Year));
                                criticalKms = true;
                                suspiciousCount++;
                            }
                            else
                            {
                                double daysLeft = (expiry - DateTime.Now).TotalDays;
                                if (daysLeft >= 165 && daysLeft <= 195)
                                {
                                    LogWarn(L.Get("P7_OnlineKms180"));
                                    suspiciousCount++;
                                }
                                else
                                {
                                    LogOk(L.Get("P7_ExpiryNormal"));
                                }
                            }
                        }

                        break; // Only process the first licensed key entry
                    }
                }
                if (!foundAnyKey)
                    LogInfo("No licensed Windows product key found via WMI.");
            }
            catch (Exception ex) { LogWarn($"WMI activation channel error: {ex.Message}"); }

            // ── 9. TSforge SPP store file timestamp (LOW CONFIDENCE) ───────
            LogBlank();
            LogSep();
            LogFetch(L.Get("Fetch_SppEvents"));
            LogDiag(L.Get("P7_SppStoreExplain"));
            try
            {
                const string datPath = @"C:\Windows\System32\spp\store\2.0\data.dat";
                if (!System.IO.File.Exists(datPath))
                {
                    LogInfo(L.Get("P7_SppStoreNotFound"));
                }
                else
                {
                    var datMod = System.IO.File.GetLastWriteTime(datPath);

                    // Get Windows install date from registry
                    DateTime installDate = DateTime.MinValue;
                    try
                    {
                        using var rk = Registry.LocalMachine.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                        if (rk?.GetValue("InstallDate") is int epoch)
                            installDate = DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime;
                    }
                    catch { }

                    // Check for nearby Windows Update event (ID 19 = successful update installed)
                    bool hasNearbyUpdate = false;
                    try
                    {
                        var evLog = new System.Diagnostics.EventLog("System");
                        foreach (System.Diagnostics.EventLogEntry ev in evLog.Entries)
                        {
                            if (ev.InstanceId == 19 && Math.Abs((ev.TimeWritten - datMod).TotalHours) < 48)
                            { hasNearbyUpdate = true; break; }
                        }
                    }
                    catch { }

                    if (hasNearbyUpdate)
                        LogOk(L.Get("P7_SppStoreOk"));
                    else if (datMod > installDate.AddDays(2))
                    {
                        LogWarn(string.Format(L.Get("P7_SppStoreModified"),
                            datMod.ToString("yyyy-MM-dd HH:mm")));
                        suspiciousCount++;
                    }
                    else
                        LogOk(L.Get("P7_SppStoreOk"));
                }
            }
            catch (Exception ex) { LogWarn($"SPP store check error: {ex.Message}"); }

            // ── Layer 6: SPP Security Event Log ───────────────────────────
            LogBlank();
            LogSep();
            LogDiag(L.Get("Fetch_SppEvents"));
            LogBlank();
            try
            {
                var sppLog = new System.Diagnostics.EventLog("System");
                var sppIds = new System.Collections.Generic.HashSet<long> { 12288, 12289, 12290, 8198 };
                bool IsPrivateIp(string ip) =>
                    ip.StartsWith("10.") || ip.StartsWith("192.168.") ||
                    System.Text.RegularExpressions.Regex.IsMatch(ip, @"^172\.(1[6-9]|2\d|3[01])\.") ||
                    ip == "127.0.0.1" || ip == "::1" || ip == "0.0.0.0";

                var sppEvents = new System.Collections.Generic.List<System.Diagnostics.EventLogEntry>();
                foreach (System.Diagnostics.EventLogEntry ev in sppLog.Entries)
                {
                    if (sppIds.Contains(ev.InstanceId) &&
                        (ev.Source?.Contains("Security-SPP") == true ||
                         ev.Source?.Contains("SoftwareProtection") == true))
                        sppEvents.Add(ev);
                }

                if (sppEvents.Count == 0)
                {
                    LogOk("No SPP activation security events found in System log.");
                }
                else
                {
                    LogInfo($"Found {sppEvents.Count} SPP security event(s) in System log.");
                    bool externalKmsInEvent = false;
                    foreach (var ev in sppEvents)
                    {
                        if (ev.InstanceId == 12290 && ev.Message != null)
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(
                                ev.Message,
                                @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}|[a-zA-Z0-9\-]+\.[a-zA-Z]{2,}(?:\.[a-zA-Z]{2,})?)");
                            if (match.Success)
                            {
                                var addr = match.Value;
                                if (!IsPrivateIp(addr) && addr != "localhost")
                                {
                                    LogError($"SPP event 12290: KMS request to external server: {addr}");
                                    LogError("This confirms activation via an unauthorized public KMS service.");
                                    criticalKms = true;
                                    externalKmsInEvent = true;
                                    suspiciousCount++;
                                }
                            }
                        }
                    }
                    if (!externalKmsInEvent)
                        LogOk("SPP events present — no external KMS server address detected in event data.");
                }
            }
            catch (Exception ex) { LogWarn($"SPP event log check error: {ex.Message}"); }

            // ── Summary ────────────────────────────────────────────────────

            LogBlank();
            LogSep();
            LogLine($"  {L.Get("P7_SummaryHeader")}", ColAction, bold: true);
            LogBlank();
            if (criticalKms)
            {
                LogError(L.Get("P7_Critical"));
                if (suspiciousCount > 1)
                    LogError($"  Total indicators flagged: {suspiciousCount}");
            }
            else if (suspiciousCount > 0)
            {
                LogWarn(L.Get("P7_Suspicious"));
                LogWarn($"  Total indicators flagged: {suspiciousCount}");
            }
            else
                LogOk(L.Get("P7_Clean"));

            // ── Legal notice (always shown) ─────────────────────────────
            LogBlank();
            LogSep();
            LogLine($"  {L.Get("P7_LegalHeader")}", ColWarn, bold: true);
            LogDiag(L.Get("P7_LegalLine1"));
            LogDiag(L.Get("P7_LegalLine2"));
            LogDiag(L.Get("P7_LegalLine3"));
            LogDiag(L.Get("P7_LegalLine4"));
            LogBlank();
            LogDiag(L.Get("P7_LegalScanLimit"));
        }

        // =========================================================================
        // KMS DNS resolution helper (called from Option 7)
        // =========================================================================
        private void CheckKmsHostDns(string host)
        {
            LogInfo(L.Get("P7_CheckDns"));

            // 1. Quick internet check — TCP to Google Public DNS 8.8.8.8:53
            bool hasInternet = false;
            try
            {
                using var tc = new System.Net.Sockets.TcpClient();
                var ar = tc.BeginConnect("8.8.8.8", 53, null, null);
                hasInternet = ar.AsyncWaitHandle.WaitOne(1500) && tc.Connected;
                try { tc.EndConnect(ar); } catch { }
            }
            catch { }

            if (!hasInternet)
            {
                LogInfo(L.Get("P7_NoInternet"));
                return;
            }

            // 2. Attempt DNS resolution
            try
            {
                var ips     = System.Net.Dns.GetHostAddresses(host);
                var ipStrs  = ips.Select(ip => ip.ToString()).ToArray();
                LogInfo(L.Get("P7_KmsDnsResolved") + string.Join(", ", ipStrs));

                // Check if any resolved IP is publicly routable (not RFC 1918 / loopback)
                bool hasPublicIp = ipStrs.Any(ip =>
                    !Regex.IsMatch(ip, @"^(10\.|172\.(1[6-9]|2[0-9]|3[01])\.|192\.168\.|127\.|::1)"));
                if (hasPublicIp)
                    LogError(L.Get("P7_KmsDnsPublic"));
                else
                    LogWarn("  Resolved to private/internal IP — unusual for a cloud KMS domain.");
            }
            catch
            {
                LogWarn(L.Get("P7_KmsDnsNoResolve"));
            }
        }

        // =========================================================================
        // Clear log
        // =========================================================================
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            LogDocument.Blocks.Clear();
            _firstAction   = true;
            StatusBar.Text = L.Get("LogCleared");
        }
    }
}
