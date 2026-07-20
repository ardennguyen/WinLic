using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

        // Generic key detection is now driven by AppSettings.AllGenericKeySuffixes
        // (loaded from settings.ini [GenericKeys] + [UserGenericKeys] + hardcoded fallback).
        // The helper below looks up a display description from AllGenericKeyDescriptions.
        private static string? GetGenericKeyDescription(string? partialKey)
            => partialKey != null && AppSettings.AllGenericKeyDescriptions.TryGetValue(partialKey, out var d) ? d : null;

        private readonly bool _isAdmin;
        private bool _firstAction = true;

        // Session log temp file — used to preserve log across elevation relaunches.
        // %TEMP% resolves to the current user's private temp folder
        // (e.g. C:\Users\<user>\AppData\Local\Temp) and is always writable,
        // even for standard (non-admin) accounts.
        private static readonly string SessionLogPath =
            Path.Combine(Path.GetTempPath(), "winlic_session.log");
        // Called once from the constructor — subscribes to KmsPanel size changes so the
        // window always grows to fit, even as the preflight fills in text and shows sub-sections.
        private void HookKmsPanelResize()
        {
            KmsPanel.SizeChanged += (_, e) =>
            {
                if (KmsPanel.Visibility != Visibility.Visible) return;

                // Measure unconstrained to get the panel's true natural height.
                // (InvalidateMeasure clears the parent-constrained cached measure.)
                double availW = KmsPanel.ActualWidth > 1 ? KmsPanel.ActualWidth : 700;
                KmsPanel.InvalidateMeasure();
                KmsPanel.Measure(new System.Windows.Size(availW, double.PositiveInfinity));
                double naturalH = KmsPanel.DesiredSize.Height;

                // How much is the panel being clipped? Grow the window by exactly that + padding.
                // This correctly accounts for header/statusbar rows consuming content-area space.
                double deficit = naturalH - e.NewSize.Height;
                if (deficit > 2)
                    this.Height += deficit + 40;
            };
        }

        // Legacy call sites kept as no-ops — the SizeChanged hook handles everything.
        private void EnsurePanelFits(FrameworkElement _) { }

        // Reusable resize hook for any overlay panel.
        private void HookPanelResize(FrameworkElement panel)
        {
            panel.SizeChanged += (_, e) =>
            {
                if (panel.Visibility != Visibility.Visible) return;
                double availW = panel.ActualWidth > 1 ? panel.ActualWidth : 700;
                panel.InvalidateMeasure();
                panel.Measure(new System.Windows.Size(availW, double.PositiveInfinity));
                double naturalH = panel.DesiredSize.Height;
                double deficit  = naturalH - e.NewSize.Height;
                if (deficit > 2) this.Height += deficit + 40;
            };
        }



        // =========================================================================
        // Constructor
        // =========================================================================
        public MainWindow()
        {
            _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                           .IsInRole(WindowsBuiltInRole.Administrator);
            AppSettings.Load();
            InitializeComponent();
            HookKmsPanelResize();       // KMS panel (uses dedicated hook with identical logic)
            HookPanelResize(DlvPanel);             // new Option 1 /dlv panel
            HookPanelResize(RearmPanel);           // new Option 4 danger panel
            HookPanelResize(ChangeChannelPanel);   // new Option 6 panel
            HookPanelResize(KmsSettingsPanel);     // new Option 7 panel

            RefreshLanguage();


            // Restore log from the session before elevation (if any)
            // Must come BEFORE the startup messages so restored output appears above them
            RestoreSessionLog();

            if (_isAdmin)
            {
                AdminStatusText.Text       = L.Get("AdminOk");
                AdminStatusText.Foreground = BrushOk;
                LogInfo(string.Format(L.Get("Startup_Ready"), L.Get("About_Version")));
            }
            else
            {
                AdminStatusText.Text       = L.Get("AdminWarn");
                AdminStatusText.Foreground = BrushWarn;
                BtnElevate.Visibility      = Visibility.Visible;
                LogWarn(L.Get("Startup_NoAdmin"));
            }
        }

        // =========================================================================
        // Language
        // =========================================================================
        private void RefreshLanguage()
        {
            Title                      = L.Get("AppTitle");
            AdminStatusText.Text       = _isAdmin ? L.Get("AdminOk") : L.Get("AdminWarn");
            AdminStatusText.Foreground = _isAdmin ? BrushOk : BrushWarn;
            TxtAppVersion.Text         = "  " + L.Get("About_Version");
            BtnElevate.Content         = L.Get("BtnElevate");
            BtnAbout.Content           = L.Get("BtnAbout");

            BtnVersionInfo.Content     = L.Get("Btn1");
            BtnTestKey.Content         = L.Get("Btn2");
            BtnRemoveLicense.Content   = L.Get("Btn3");
            BtnResetActivation.Content = L.Get("Btn4");
            BtnPiracyCheck.Content     = L.Get("Btn5");
            BtnKmsActivate.Content     = L.Get("Btn8");
            BtnChangeChannel.Content   = L.Get("Btn6");
            BtnKmsSettings.Content     = L.Get("Btn7");
            BtnAuditSettings.Content   = L.Get("BtnAuditSettings");
            BtnClear.Content           = L.Get("BtnClear");
            ChkShowFullKey.Content     = L.Get("ChkShowKey");
            StatusBar.Text             = L.Get("Ready");

            // Key Entry Panel labels
            KpTitle.Text          = L.Get("KP_Title");
            KpInfo1.Text          = L.Get("KP_Info1");
            KpInfo2.Text          = L.Get("KP_Info2");
            KpWarn.Text           = L.Get("KP_Warn");
            KpConfirmLabel.Text   = L.Get("KP_TypeOk");
            KpHint.Text           = L.Get("KP_Hint");
            BtnKeyCancel.Content  = L.Get("KP_Cancel");
            BtnKeyInstall.Content = L.Get("KP_Install");

            BtnLangEN.Style  = (Style)(L.Current == Lang.EN
                ? FindResource("LangBtnActive") : FindResource("LangBtn"));
            BtnLangVIE.Style = (Style)(L.Current == Lang.VIE
                ? FindResource("LangBtnActive") : FindResource("LangBtn"));

            // DLV panel static labels
            DlvTitle.Text        = L.Get("DLV_PANEL_TITLE");
            DlvDesc1.Text        = L.Get("DLV_DESC1");
            DlvDesc2.Text        = L.Get("DLV_DESC2");
            BtnDlvCancel.Content = L.Get("DLV_CANCEL");
            BtnDlvRun.Content    = L.Get("DLV_RUN");

            // Rearm panel static labels
            RpTitle.Text              = L.Get("R4_PANEL_TITLE");
            RpWarn1.Text              = L.Get("R4_WARN1");
            RpWarn2.Text              = L.Get("R4_WARN2");
            RpCountLabel.Text         = L.Get("R4_COUNT_LABEL");
            RpConfirmLabel.Text       = L.Get("R4_CONFIRM_LABEL");
            RpRestartCheck.Content    = L.Get("R4_RESTART_CHECK");
            BtnRpCancel.Content       = L.Get("R4_CANCEL");
            BtnRpConfirm.Content      = L.Get("R4_CONFIRM");
            // Tab labels
            TabWin.Content        = L.Get("TAB_WIN");
            TabOffice.Content     = L.Get("TAB_OFFICE");
            OfficeDevTitle.Text   = L.Get("TAB_OFFICE_TITLE");
            OfficeDevDesc.Text    = L.Get("TAB_OFFICE_DESC");
        }

        private void TabWin_Click(object sender, RoutedEventArgs e)
        {
            WinTabContent.Visibility     = Visibility.Visible;
            OfficeTabContent.Visibility  = Visibility.Collapsed;
            TabWin.Style    = (Style)FindResource("TabBtnActive");
            TabOffice.Style = (Style)FindResource("TabBtnInactive");
        }

        private void TabOffice_Click(object sender, RoutedEventArgs e)
        {
            WinTabContent.Visibility     = Visibility.Collapsed;
            OfficeTabContent.Visibility  = Visibility.Visible;
            TabWin.Style    = (Style)FindResource("TabBtnInactive");
            TabOffice.Style = (Style)FindResource("TabBtnActive");
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
        private void LogOk(string msg)    => LogLine($"  {msg}", ColOk,    bold: true);
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

        /// <summary>Returns true when the "Show Full Keys" checkbox in the sidebar is checked.</summary>
        private bool ShowFullKey => ChkShowFullKey.IsChecked == true;

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
            // Check AppSettings generic key descriptions first
            var desc = GetGenericKeyDescription(last5);
            if (desc != null) return desc;

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
            if (partialKey != null && AppSettings.AllGenericKeySuffixes.Contains(partialKey))
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
        // Option 1 — Full System & License Info (merged: old Options 1 + 2 + 3)
        // =========================================================================
        private void BtnVersionInfo_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act1");

            // ── OS information ────────────────────────────────────────────────
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

            // ── Registry backup key (read first for DE detection) ─────────────
            LogBlank();
            LogFetch(L.Get("Fetch_RegKey"));
            string? regKey = null;
            try
            {
                using var rk = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform");
                regKey = rk?.GetValue("BackupProductKeyDefault")?.ToString();
            }
            catch (Exception ex) { LogError(L.Get("O3_RegReadErr") + ex.Message); }

            // ── Active Windows license (WMI) ──────────────────────────────────
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

            // ── Activation method block ───────────────────────────────────────
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

            // ── BIOS OEM key ──────────────────────────────────────────────────
            LogBlank();
            LogFetch(L.Get("Fetch_BiosKey"));
            string? oemKey = null;
            using var svcRes = WmiQuery("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
            if (svcRes != null)
                foreach (ManagementObject obj in svcRes)
                    oemKey = obj["OA3xOriginalProductKey"]?.ToString();

            bool hasOem = !string.IsNullOrWhiteSpace(oemKey);
            LogData(L.Get("D_BiosOemKey"), hasOem ? L.Get("O3_BiosDetected") : L.Get("O3_BiosNone"));
            if (hasOem)
            {
                LogFetch(L.Get("Fetch_OemEdition"));
                var ed = IdentifyKeyEdition(oemKey!);
                if (!string.IsNullOrEmpty(ed)) LogOk(L.Get("OemEd_Found") + "  " + ed);
                else                           LogInfo(L.Get("OemEd_NoMatch"));
            }

            // ── Registry backup key status ────────────────────────────────────
            bool hasReg = !string.IsNullOrWhiteSpace(regKey);
            LogData(L.Get("D_RegBackupKey"), hasReg ? L.Get("O3_BiosDetected") : L.Get("O3_RegNone"));

            // ── Installed Key (DigitalProductId decoder) ──────────────────────
            LogFetch(L.Get("Fetch_InstalledKey"));
            string? installedKey = null;
            try
            {
                using var rk = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (rk?.GetValue("DigitalProductId") is byte[] dpId)
                    installedKey = DecodeProductKeyWin8AndUp(dpId);
            }
            catch { }

            bool hasInstalled = !string.IsNullOrWhiteSpace(installedKey);
            LogData(L.Get("D_InstalledKey"),
                    hasInstalled ? L.Get("O3_BiosDetected") : L.Get("O3_RegNone"));

            // ── Key reveal (driven by sidebar checkbox) ───────────────────────
            if (hasOem || hasReg || hasInstalled)
            {
                LogBlank();
                bool full = ShowFullKey;
                if (hasInstalled) LogKey(L.Get("O3_KeyInstalled") + (full ? installedKey! : MaskKey(installedKey!)));
                if (hasOem)       LogKey(L.Get("O3_KeyBios")      + (full ? oemKey!       : MaskKey(oemKey!)));
                if (hasReg)       LogKey(L.Get("O3_KeyReg")       + (full ? regKey!       : MaskKey(regKey!)));
            }

            // ── Mismatch detection ────────────────────────────────────────────
            if (hasReg && !string.IsNullOrEmpty(partialKey))
            {
                LogBlank();
                bool match = regKey!.EndsWith(partialKey, StringComparison.OrdinalIgnoreCase);
                if (!match && activationMethod == ActivationMethod.DE)
                    LogInfo(L.Get("DE_KeyMismatch"));
                else if (!match)
                {
                    LogWarn(L.Get("O3_Mismatch"));
                    LogDiag(L.Get("O3_MismatchReason"));
                    LogDiag(L.Get("O3_ActivePartial") + partialKey);
                    { var rp = regKey.Split('-'); LogDiag(L.Get("O3_BackupEnds") + rp[rp.Length - 1]); }
                    LogBlank();
                    if (_isAdmin)
                    {
                        if (AskConfirm(L.Get("O3_ConfirmRemove")))
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
                    else
                        LogWarn(L.Get("O3_NeedAdmin"));
                }
                else { LogOk(L.Get("O3_KeyMatch")); }
            }

            // ── slmgr /dli — License Channel info ────────────────────────────
            LogBlank();
            LogSep();
            LogFetch(L.Get("Act1_DliHeader"));
            LogInfo(L.Get("O2_Note"));
            LogBlank();
            var dliOutput = RunSlmgr("/dli");
            if (string.IsNullOrWhiteSpace(dliOutput))
                LogWarn(L.Get("O2_NoOutput"));
            else
                LogSlmgrOutput(dliOutput);

            // ── Extended info: slmgr /dlv — show panel instead of MessageBox ────
            DlvTitle.Text  = L.Get("DLV_PANEL_TITLE");
            DlvDesc1.Text  = L.Get("DLV_DESC1");
            DlvDesc2.Text  = L.Get("DLV_DESC2");
            BtnDlvCancel.Content = L.Get("DLV_CANCEL");
            BtnDlvRun.Content    = L.Get("DLV_RUN");
            DlvPanel.Visibility  = Visibility.Visible;
            EnsurePanelFits(DlvPanel);
        }


        private void BtnDlvCancel_Click(object sender, RoutedEventArgs e)
            => DlvPanel.Visibility = Visibility.Collapsed;

        private void BtnDlvRun_Click(object sender, RoutedEventArgs e)
        {
            DlvPanel.Visibility = Visibility.Collapsed;
            LogBlank();
            LogSlmgrOutput(RunSlmgr("/dlv"));
        }


        // Old BtnSlmgrDli_Click and BtnInspectKeys_Click removed — merged into BtnVersionInfo_Click above.


        // =========================================================================
        // PidGenX P/Invoke wrapper
        // pidgenx.dll is present at %windir%\System32\pidgenx.dll on ALL Win10/11.
        // pkeyconfig.xrm-ms lives at %windir%\System32\spp\tokens\pkeyconfig\
        // =========================================================================
        private static class PidGenXNative
        {
            [DllImport("pidgenx.dll", CharSet = CharSet.Unicode,
                       CallingConvention = CallingConvention.StdCall)]
            internal static extern int PidGenX(
                string productKey,
                string pkeyConfigPath,
                string pkeyConfigPath2,
                string groupId,
                IntPtr pProductId,
                IntPtr pDigPid,
                IntPtr pDigPid4);
        }

        private static readonly string PkcPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "spp", "tokens", "pkeyconfig", "pkeyconfig.xrm-ms");

        private static readonly string[] _channelTokens =
            { "OEM:DM", "OEM:NONSLP", "OEM:SLP", "Retail", "Volume:GVLK", "Volume:MAK", "Volume:CSVLK" };

        // =========================================================================
        // Option 2 — PidGenX Key Analysis helper
        // Tier 1: format check (25 alphanum, 5x5).
        // Tier 2: real P/Invoke to pidgenx.dll → pkeyconfig lookup → channel string.
        // No side effects, no network, no key replacement.
        // =========================================================================

        // Stores the last PidGenX result so EvaluateBannerFromBoxes can log it.
        private (bool Valid, string Channel) _lastPidResult = (false, "");
        // Tracks the last key that was written to the log (avoids duplicate entries
        // if the user types, then re-types the same complete key).
        private string _lastBannerKey = "";

        /// <summary>
        /// Validates the structural format of a product key (Tier 1) then calls
        /// pidgenx.dll (Tier 2) to identify the channel from pkeyconfig.xrm-ms.
        /// Returns validity and the channel string (e.g. "Retail", "OEM:DM").
        /// No registry changes, no network, no key replacement.
        /// </summary>
        private static (bool Valid, string Channel) CheckKeyChecksum(string key)
        {
            // ── Tier 1: format check ──────────────────────────────────────────
            var stripped = key.Replace("-", "").ToUpperInvariant();
            if (stripped.Length != 25) return (false, "");
            foreach (var ch in stripped)
                if (!char.IsLetterOrDigit(ch)) return (false, "");

            // ── Tier 2: pidgenx.dll P/Invoke ────────────────────────────────
            if (File.Exists(PkcPath))
            {
                const int Buf = 1024;
                IntPtr pPid   = Marshal.AllocHGlobal(Buf);
                IntPtr pDpid  = Marshal.AllocHGlobal(Buf);
                IntPtr pDpid4 = Marshal.AllocHGlobal(Buf);
                try
                {
                    // zero-fill buffers
                    for (int i = 0; i < Buf; i++)
                    {
                        Marshal.WriteByte(pPid,   i, 0);
                        Marshal.WriteByte(pDpid,  i, 0);
                        Marshal.WriteByte(pDpid4, i, 0);
                    }
                    int hr = PidGenXNative.PidGenX(
                        key, PkcPath, PkcPath, null,
                        pPid, pDpid, pDpid4);

                    if (hr != 0)
                    {
                        // pidgenx couldn't match the key in pkeyconfig (may be a valid
                        // key for a different SKU/edition). Fall back to format-only.
                        return (true, "");
                    }

                    // Scan DigPid4 buffer for recognisable channel token
                    var bytes = new byte[Buf];
                    Marshal.Copy(pDpid4, bytes, 0, Buf);
                    var buf = Encoding.Unicode.GetString(bytes);
                    foreach (var tok in _channelTokens)
                        if (buf.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                            return (true, tok);

                    return (true, ""); // pidgenx OK but channel token not found
                }
                catch { /* fall through to format-only result below */ }
                finally
                {
                    Marshal.FreeHGlobal(pPid);
                    Marshal.FreeHGlobal(pDpid);
                    Marshal.FreeHGlobal(pDpid4);
                }
            }
            // Tier 2 unavailable — return format-only result
            return (true, "");
        }

        /// <summary>
        /// Show or update the PidBanner element in the key entry panel.
        /// Call after CheckKeyChecksum completes.
        /// </summary>
        private void ShowPidBanner(bool valid, string detailLine)
        {
            if (valid)
            {
                PidBanner.Background   = new SolidColorBrush(Color.FromRgb(0xf0, 0xfd, 0xf4));
                PidBanner.BorderBrush  = new SolidColorBrush(Color.FromRgb(0x86, 0xef, 0xac));
                PidBannerIcon.Text     = "✔";
                PidBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3d));
                PidBannerText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
                PidBannerText.Text     = string.IsNullOrEmpty(detailLine)
                    ? L.Get("KP_PidValid")
                    : L.Get("KP_PidValid") + "  |  " + detailLine;
            }
            else
            {
                PidBanner.Background   = new SolidColorBrush(Color.FromRgb(0xfe, 0xf2, 0xf2));
                PidBanner.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xfc, 0xa5, 0xa5));
                PidBannerIcon.Text     = "⚠";
                PidBannerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xb9, 0x1c, 0x1c));
                PidBannerText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1b, 0x1b));
                PidBannerText.Text     = L.Get("KP_PidInvalid");
            }
            PidBanner.Visibility = Visibility.Visible;
        }

        // =========================================================================
        // Option 2 — Test & Install New Product Key  (was Option 4)
        // Key entry uses inline segmented panel instead of InputDialog
        // =========================================================================
        private void BtnTestKey_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;

            // Log the action header and pre-arm info to output FIRST
            LogAction("Act2");
            LogInfo(L.Get("O4_Info1"));
            LogInfo(L.Get("O4_Info2"));
            LogWarn(L.Get("KP_Warn"));


            // Read and log current active license — same block as CLI Option 2
            LogFetch(L.Get("O2_READ_LIC"));
            try
            {
                using var lr = WmiQuery(
                    "SELECT Name,Description,PartialProductKey,LicenseStatus " +
                    "FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL");
                bool found = false;
                if (lr != null)
                    foreach (ManagementObject obj in lr)
                    {
                        var name    = obj["Name"]?.ToString() ?? "";
                        if (!name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)) continue;
                        var partial = obj["PartialProductKey"]?.ToString() ?? "";
                        uint raw    = obj["LicenseStatus"] is uint u ? u : 0;
                        var desc    = obj["Description"]?.ToString() ?? "";
                        var status  = LicenseStatusText(raw);
                        LogData(L.Get("O2_CUR_ED"),  name);
                        LogData(L.Get("O2_CUR_KEY"), partial);
                        if (raw == 1) LogOk(L.Get("O2_CUR_STATUS") + status);
                        else          LogWarn(L.Get("O2_CUR_STATUS") + status);
                        // Channel warning
                        var descUp = desc.ToUpperInvariant();
                        if (descUp.Contains("VOLUME_KMSCLIENT"))                               LogWarn(L.Get("O2_WARN_CHAN_KMS"));
                        else if (descUp.Contains("VOLUME_KMS") && !descUp.Contains("KMSCLIENT")) LogWarn(L.Get("O2_WARN_CHAN_KMSHOST"));
                        else if (descUp.Contains("SUBSCRIPTION"))                              LogWarn(L.Get("O2_WARN_CHAN_SUB"));
                        found = true;
                        break;
                    }
                if (!found) LogWarn(L.Get("O2_NO_LIC"));
            }
            catch { LogWarn(L.Get("O2_NO_LIC")); }

            LogBlank();
            LogInfo(L.Get("O2_NET_NOTICE"));

            LogBlank();

            // Log ABOUT note so user knows what Phase 1 will do
            LogInfo(L.Get("O2_PIDGX_ABOUT"));
            LogBlank();

            // Populate panel (also kept in sync by RefreshLanguage on lang switch)
            KpInfo1.Text        = L.Get("KP_Info1");
            KpInfo2.Text        = L.Get("KP_Info2");
            KpWarn.Text         = L.Get("KP_Warn");
            KpConfirmLabel.Text = L.Get("KP_TypeOk");

            // Clear boxes, reset PidBanner, and show the panel
            KBox1.Text = KBox2.Text = KBox3.Text = KBox4.Text = KBox5.Text = "";
            KpOkBox.Text = string.Empty;
            PidBanner.Visibility = Visibility.Collapsed;
            _lastPidResult  = (false, "");
            _lastBannerKey  = "";
            KeyEntryPanel.Visibility = Visibility.Visible;
            EnsurePanelFits(KeyEntryPanel);
            KBox1.Focus();
        }

        // ── Keyboard navigation for segmented key boxes ───────────────────────
        private TextBox[] KeyBoxes => new[] { KBox1, KBox2, KBox3, KBox4, KBox5 };

        private void KeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;
            int idx = int.Parse((string)tb.Tag) - 1;  // 0-based index

            switch (e.Key)
            {
                case Key.Back:
                    // If box is empty jump to previous; otherwise let WPF delete normally
                    if (tb.Text.Length == 0 && idx > 0)
                    {
                        var prev = KeyBoxes[idx - 1];
                        prev.Focus();
                        prev.CaretIndex = prev.Text.Length;
                        e.Handled = true;
                    }
                    break;

                case Key.Delete:
                    // Delete key must NOT move out of the current block — WPF default is fine
                    // (it only deletes at the caret), just make sure we don't jump.
                    // Nothing extra needed; this case is here to explicitly document the intent.
                    break;

                case Key.Tab when Keyboard.Modifiers == ModifierKeys.Shift:
                    if (idx > 0) { KeyBoxes[idx - 1].Focus(); e.Handled = true; }
                    break;

                case Key.Tab:
                    if (idx < 4) { KeyBoxes[idx + 1].Focus(); e.Handled = true; }
                    break;

                case Key.Enter:
                    e.Handled = true;
                    // If on last box, move focus to OK box; otherwise submit
                    if (idx == 4) KpOkBox.Focus();
                    else InstallKeyFromPanel();
                    break;

                case Key.Escape:
                    e.Handled = true;
                    CancelKeyPanel();
                    break;
            }
        }

        private bool _textChangedGuard = false;

        private void KeyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_textChangedGuard) return;   // prevent re-entrant fire from tb.Text = clean
            if (sender is not TextBox tb) return;
            int idx = int.Parse((string)tb.Tag) - 1;  // 0-based

            // Strip non-alphanumeric chars that bypassed CharacterCasing
            var clean = new string(tb.Text.ToUpperInvariant()
                .Where(c => char.IsLetterOrDigit(c)).ToArray());
            if (clean != tb.Text)
            {
                _textChangedGuard = true;
                tb.Text = clean;
                tb.CaretIndex = clean.Length;
                _textChangedGuard = false;
            }

            // Auto-jump to next box when this one is full (5 chars)
            if (tb.Text.Length == 5 && idx < 4)
                KeyBoxes[idx + 1].Focus();

            // Re-evaluate banner on every keystroke
            EvaluateBannerFromBoxes();
        }

        /// <summary>
        /// Assembles all 5 key segments and shows/hides the format banner.
        /// When the key becomes complete (25 chars), also writes the full Phase 1
        /// KEY ANALYSIS block to the main log immediately — before the user clicks
        /// Install. This way Phase 1 results are always visible before Phase 2.
        /// </summary>
        private void EvaluateBannerFromBoxes()
        {
            var assembled = KBox1.Text + KBox2.Text + KBox3.Text + KBox4.Text + KBox5.Text;
            if (assembled.Length == 25)
            {
                var fullKey = string.Join("-",
                    KBox1.Text, KBox2.Text, KBox3.Text, KBox4.Text, KBox5.Text)
                    .ToUpperInvariant();
                var (valid, channel) = CheckKeyChecksum(fullKey);
                _lastPidResult = (valid, channel);

                // Show banner (includes channel when pidgenx succeeded)
                var bannerDetail = (valid && !string.IsNullOrEmpty(channel))
                    ? L.Get("KP_PidChannel") + channel
                    : "";
                ShowPidBanner(valid, bannerDetail);

                // Write Phase 1 analysis to log only once per unique full key
                if (fullKey != _lastBannerKey)
                {
                    _lastBannerKey = fullKey;
                    LogSep();
                    LogInfo(L.Get("O2_PIDGX_HDR"));
                    if (valid)
                    {
                        LogOk(L.Get("O2_PIDGX_VALID"));
                        if (!string.IsNullOrEmpty(channel))
                        {
                            LogInfo(L.Get("KP_PidChannel") + channel);
                            LogInfo(L.Get("O2_PIDGX_SRC_PIDGENX"));
                        }
                        else
                        {
                            LogInfo(L.Get("O2_PIDGX_SRC_CHK"));
                        }
                    }
                    else
                    {
                        LogError(L.Get("O2_PIDGX_INVALID"));
                        LogInfo(File.Exists(PkcPath)
                            ? L.Get("O2_PIDGX_SRC_PIDGENX")
                            : L.Get("O2_PIDGX_SRC_CHK"));
                    }
                    LogSep();
                }
            }
            else if (PidBanner.Visibility == Visibility.Visible)
            {
                // Key is incomplete — hide stale result
                PidBanner.Visibility = Visibility.Collapsed;
                _lastPidResult = (false, "");
            }
        }

        private void KeyBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(typeof(string))) return;
            var raw = e.DataObject.GetData(typeof(string)) as string ?? "";

            // Strip separators only (dashes, spaces, dots) — keep ALL alphanumeric chars.
            // Do NOT filter by the Windows key alphabet here: invalid characters should
            // reach the boxes intact so the red banner correctly reports the bad character.
            // Alphabet validation happens in CheckKeyChecksum / EvaluateBannerFromBoxes.
            var clean = new string(raw.ToUpperInvariant()
                                      .Where(char.IsLetterOrDigit)
                                      .ToArray());

            // Only intercept when enough chars for a multi-box paste (> 5 means at least 2 blocks)
            if (clean.Length <= 5) return;   // let normal single-box paste proceed

            // Cancel the normal paste and distribute chars starting at box 0
            e.CancelCommand();

            _textChangedGuard = true;   // suppress per-assignment TextChanged evaluations
            for (int b = 0; b < 5; b++)
            {
                int start = b * 5;
                KeyBoxes[b].Text = start < clean.Length
                    ? clean.Substring(start, Math.Min(5, clean.Length - start))
                    : string.Empty;
            }
            _textChangedGuard = false;

            // Single banner evaluation after all boxes are filled
            EvaluateBannerFromBoxes();

            // Move focus: if all 5 are full go to the OK box, otherwise last non-full box
            var firstIncomplete = Array.FindIndex(KeyBoxes, bx => bx.Text.Length < 5);
            if (firstIncomplete < 0)
                KpOkBox.Focus();
            else
                KeyBoxes[firstIncomplete].Focus();
        }

        private void BtnKeyInstall_Click(object sender, RoutedEventArgs e)
            => InstallKeyFromPanel();

        private void BtnKeyCancel_Click(object sender, RoutedEventArgs e)
            => CancelKeyPanel();

        private void CancelKeyPanel()
        {
            KeyEntryPanel.Visibility = Visibility.Collapsed;
            LogInfo(L.Get("O4_Cancelled"));
        }

        private void KpOkBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { e.Handled = true; InstallKeyFromPanel(); }
            if (e.Key == Key.Escape) { e.Handled = true; CancelKeyPanel(); }
        }

        private void InstallKeyFromPanel()
        {
            var key = string.Join("-",
                KBox1.Text.Trim(), KBox2.Text.Trim(),
                KBox3.Text.Trim(), KBox4.Text.Trim(), KBox5.Text.Trim())
                .ToUpperInvariant();

            // Validate key format
            if (!Regex.IsMatch(key,
                @"^[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}$"))
            {
                LogError(L.Get("O4_BadFormat"));
                KBox1.Focus();
                return;
            }

            // Validate OK confirmation
            if (!KpOkBox.Text.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                LogError(L.Get("KP_BadInput"));
                KpOkBox.SelectAll();
                KpOkBox.Focus();
                return;
            }

            // Hide the panel before running slmgr (which may take a moment)
            KeyEntryPanel.Visibility = Visibility.Collapsed;

            // Phase 1 result was already written to the log by EvaluateBannerFromBoxes
            // when the user finished typing the key. Add Phase 2 separator.
            LogSep();
            LogInfo(L.Get("O2_PHASE2_HDR"));

            bool full = ShowFullKey;
            LogInfo(L.Get("O4_Installing") + (full ? key : MaskKey(key)));
            LogBlank();

            var output = RunSlmgr($"/ipk {key}");
            LogSlmgrOutput(output);

            bool fail = output.Contains("Error") || output.Contains("0x");
            LogBlank();
            if (!fail)
            {
                LogOk(L.Get("O4_Success1"));
                LogInfo(L.Get("O4_Success2"));

                // ── Auto online activation (/ato) ─────────────────────────────
                LogBlank();
                // ── Internet pre-check before /ato ───────────────────────────
                bool netOk = false;
                try
                {
                    using var tc  = new System.Net.Sockets.TcpClient();
                    var ar = tc.BeginConnect("8.8.8.8", 53, null, null);
                    netOk = ar.AsyncWaitHandle.WaitOne(1500) && tc.Connected;
                    try { tc.EndConnect(ar); } catch { }
                }
                catch { }
                if (!netOk)
                    LogWarn(L.Get("O2_ATO_NO_INTERNET"));

                LogInfo(L.Get("O2_ATO_AUTO"));
                var atoOut = RunSlmgr("/ato");
                LogSlmgrOutput(atoOut);
                LogBlank();
                bool atoFail = atoOut.Contains("Error") || atoOut.Contains("0x");
                if (!atoFail)
                {
                    LogOk(L.Get("O2_ATO_SUCCESS"));
                }
                else
                {
                    LogError(L.Get("O2_ATO_FAIL") + " " + atoOut.Trim());
                    if      (atoOut.Contains("0x80070005")) LogDiag(L.Get("O2_DIAG_ACCESS_DENIED"));
                    else if (atoOut.Contains("0x80070490")) LogDiag(L.Get("O2_DIAG_DIDNTWORK"));
                    else if (atoOut.Contains("0xC004C001")) LogDiag(L.Get("O2_DIAG_SERVER_INVALID"));
                    else if (atoOut.Contains("0xC004C008")) LogDiag(L.Get("O2_DIAG_KEY_LIMIT"));
                    else if (atoOut.Contains("0xC004C020") || atoOut.Contains("0xC004C021"))
                                                            LogDiag(L.Get("O2_DIAG_MAK_LIMIT"));
                    else if (atoOut.Contains("0xC004B100")) LogDiag(L.Get("O2_DIAG_SERVER_NOACT"));
                    else if (atoOut.Contains("0xC004E003")) LogDiag(L.Get("O2_DIAG_LIC_EVAL_FAIL"));
                    else if (atoOut.Contains("0xC004F009")) LogDiag(L.Get("O2_DIAG_GRACE"));
                    else if (atoOut.Contains("0xC004F00F")) LogDiag(L.Get("O2_DIAG_HW_CHANGED"));
                    else if (atoOut.Contains("0xC004F038")) LogDiag(L.Get("O2_DIAG_KMS_COUNT"));
                    else if (atoOut.Contains("0xC004F039")) LogDiag(L.Get("O2_DIAG_KMS_DISABLED"));
                    else if (atoOut.Contains("0xC004F041")) LogDiag(L.Get("O2_DIAG_KMS_NOT_ACT"));
                    else if (atoOut.Contains("0xC004F042")) LogDiag(L.Get("O2_DIAG_KMS_WRONG"));
                    else if (atoOut.Contains("0xC004F064")) LogDiag(L.Get("O2_DIAG_NONGENUINE_EXP"));
                    else if (atoOut.Contains("0xC004F065")) LogDiag(L.Get("O2_DIAG_NONGENUINE_GRC"));
                    else if (atoOut.Contains("0xC004F06C")) LogDiag(L.Get("O2_DIAG_KMS_CLOCK"));
                    else if (atoOut.Contains("0x8004FE21")) LogDiag(L.Get("O2_DIAG_NOTGENUINE"));
                    else if (atoOut.Contains("0x8007007B") || atoOut.Contains("0x80092328"))
                                                            LogDiag(L.Get("O2_DIAG_DNS_NONAME"));
                    else if (atoOut.Contains("0x800706BA")) LogDiag(L.Get("O2_DIAG_RPC_UNAVAIL"));
                    else if (atoOut.Contains("0x8007232A") || atoOut.Contains("0x8007251D"))
                                                            LogDiag(L.Get("O2_DIAG_DNS_FAIL"));
                    else if (atoOut.Contains("0x8007232B")) LogDiag(L.Get("O2_DIAG_NO_NET"));
                    else if (atoOut.Contains("0x80072EE2") || atoOut.Contains("0x80072EE7") || atoOut.Contains("0x80072EFD"))
                                                            LogDiag(L.Get("O2_DIAG_NET_TIMEOUT"));
                    else if (atoOut.Contains("0xC004F074")) LogDiag(L.Get("O2_DIAG_KMS_NO_SRV"));
                    LogBlank();
                    LogHelp(L.Get("O8KMS_REF_URL"));
                }
            }
            else
            {
                LogError(L.Get("O4_Fail"));
                if      (output.Contains("0x80070005")) LogDiag(L.Get("O2_DIAG_ACCESS_DENIED"));
                else if (output.Contains("0xC004E016")) LogDiag(L.Get("O4_DiagGen"));
                else if (output.Contains("0xC004F069")) LogDiag(L.Get("O4_DiagSku"));
                else if (output.Contains("0xC004F050")) LogDiag(L.Get("O4_DiagInvalid"));
                else if (output.Contains("0xC004C003")) LogDiag(L.Get("O4_DiagBlocked"));
                else if (output.Contains("0xC004C008")) LogDiag(L.Get("O2_DIAG_KEY_LIMIT"));
                else if (output.Contains("0xC004F035")) LogDiag(L.Get("O2_DIAG_VOL_LIC"));
                else if (output.Contains("0xC004F051")) LogDiag(L.Get("O2_DIAG_KEY_BLOCKED_SLS"));
                else                                     LogDiag(L.Get("O4_DiagGeneral"));
                LogBlank();
                LogHelp("https://support.microsoft.com/help/10738");
                LogHelp("https://learn.microsoft.com/en-us/windows-server/get-started/activation-error-codes");
            }
        }

        // =========================================================================
        // Option 3 — Remove Activation  (inline confirm panel, mirrors Option 2 style)
        // =========================================================================
        private void BtnRemoveLicense_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;

            // Log the action header and pre-arm warnings to output FIRST
            LogAction("Act3");
            LogWarn(L.Get("O3_Remove_Warn1"));
            LogWarn(L.Get("O3_Remove_Warn2"));
            LogBlank();

            // Populate panel text (localized)
            RcpTitle.Text        = L.Get("O3_Remove_Title");
            RcpWarn1.Text        = L.Get("O3_Remove_Warn1");
            RcpWarn2.Text        = L.Get("O3_Remove_Warn2");
            RcpConfirmLabel.Text = L.Get("O3_Remove_TypeOk");
            RcpHint.Text         = L.Get("O3_Remove_Hint");
            BtnRemoveCancel.Content  = L.Get("O3_Remove_Cancel");
            BtnRemoveConfirm.Content = L.Get("O3_Remove_Confirm");

            // Clear input and show panel
            RcpOkBox.Text = string.Empty;
            RemoveConfirmPanel.Visibility = Visibility.Visible;
            EnsurePanelFits(RemoveConfirmPanel);
            RcpOkBox.Focus();
        }

        private void BtnRemoveCancel_Click(object sender, RoutedEventArgs e)
        {
            RemoveConfirmPanel.Visibility = Visibility.Collapsed;
            LogInfo(L.Get("O3_Cancelled"));
        }

        private void BtnRemoveConfirm_Click(object sender, RoutedEventArgs e)
            => ExecuteRemove();

        private void RcpOkBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { e.Handled = true; ExecuteRemove(); }
            if (e.Key == Key.Escape) { e.Handled = true; BtnRemoveCancel_Click(sender, e); }
        }

        private void ExecuteRemove()
        {
            if (!RcpOkBox.Text.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                LogError(L.Get("O3_Remove_BadInput"));
                RcpOkBox.SelectAll();
                RcpOkBox.Focus();
                return;
            }

            RemoveConfirmPanel.Visibility = Visibility.Collapsed;

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
        // Option 4 — Reset Activation / Rearm  (was Option 6)
        // =========================================================================
        private void BtnResetActivation_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;

            // WMI always has the live count; registry key only appears after first rearm
            int rearmCount = -1;
            try
            {
                using var svcRes = WmiQuery(
                    "SELECT RemainingWindowsRearmCount FROM SoftwareLicensingService");
                if (svcRes != null)
                    foreach (ManagementObject obj in svcRes)
                    {
                        var v = obj["RemainingWindowsRearmCount"];
                        if (v != null) { rearmCount = Convert.ToInt32(v); break; }
                    }
            }
            catch { }

            // Registry fallback (present only after first rearm on some builds)
            if (rearmCount < 0)
            {
                try
                {
                    using var rk = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform");
                    var v = rk?.GetValue("RemainingWindowsRearmCount");
                    if (v != null) rearmCount = Convert.ToInt32(v);
                }
                catch { }
            }


            // Populate and show RearmPanel
            RpTitle.Text         = L.Get("R4_PANEL_TITLE");
            RpWarn1.Text         = L.Get("R4_WARN1");
            RpWarn2.Text         = L.Get("R4_WARN2");
            RpCountLabel.Text    = L.Get("R4_COUNT_LABEL");
            RpCountValue.Text    = rearmCount >= 0 ? rearmCount.ToString() : "?";
            RpConfirmLabel.Text  = L.Get("R4_CONFIRM_LABEL");
            RpRestartCheck.Content = L.Get("R4_RESTART_CHECK");
            RpRestartCheck.IsChecked = false;
            BtnRpCancel.Content  = L.Get("R4_CANCEL");
            BtnRpConfirm.Content = L.Get("R4_CONFIRM");
            RpOkBox.Text         = string.Empty;

            RearmPanel.Visibility = Visibility.Visible;
            EnsurePanelFits(RearmPanel);
        }

        private void BtnRpCancel_Click(object sender, RoutedEventArgs e)
        {
            RearmPanel.Visibility = Visibility.Collapsed;
            LogInfo(L.Get("R4_CANCELLED"));
        }

        private void BtnRpConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!RpOkBox.Text.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                RpOkBox.BorderBrush = new SolidColorBrush(Colors.Red);
                return;
            }

            bool doRestart = RpRestartCheck.IsChecked == true;
            RearmPanel.Visibility = Visibility.Collapsed;

            LogAction("Act4");
            LogBlank();
            LogInfo(L.Get("O6_Rearming"));
            LogSlmgrOutput(RunSlmgr("/rearm"));

            LogBlank();
            LogOk(L.Get("O6_Done"));

            if (doRestart)
            {
                LogInfo(L.Get("O6_Restarting"));
                Process.Start("shutdown.exe", "/r /t 5 /c \"WinLicManager rearm restart\"");
            }
        }


        // =========================================================================
        // Option 5 — 3rd-Party Activation Audit  (was Option 7)
        // =========================================================================
        private void BtnPiracyCheck_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act5");

            // ── Verbose preamble: what this scan covers ────────────────────────
            LogLine($"  {L.Get("P7_Header")}", ColAction, bold: true);
            LogDiag(L.Get("P7_CanDetect1"));
            LogDiag(L.Get("P7_CanDetect2"));
            LogDiag(L.Get("P7_CanDetect3"));
            LogDiag(L.Get("P7_CanDetect4"));
            LogDiag(L.Get("P7_CanDetect5"));
            LogDiag(L.Get("P7_CanDetect6"));
            LogDiag(L.Get("P7_CanDetect7"));
            LogDiag(L.Get("P7_CanDetect8"));
            LogDiag(L.Get("P7_CanDetect9"));
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
                    "FROM SoftwareLicensingProduct " +
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

                        bool isLicensed     = licStatus == 1;
                        bool isPermanent    = graceMins == 0 && isLicensed;
                        bool isGvlk         = AppSettings.AllGvlkSuffixes.Contains(ppk);
                        bool isPhone        = desc.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0;
                        // Channel check: VOLUME_KMSCLIENT = KMS (corporate or pirate), RETAIL/OEM_DM = DE
                        bool isVolumeKms    = desc.IndexOf("VOLUME_KMSCLIENT", StringComparison.OrdinalIgnoreCase) >= 0;
                        // Also check the ProductKeyChannel property if present (more reliable)
                        string? pkChannel = mo.Properties.Cast<PropertyData>()
                            .FirstOrDefault(p => p.Name.Equals("ProductKeyChannel", StringComparison.OrdinalIgnoreCase))?.Value?.ToString() ?? "";
                        if (pkChannel?.IndexOf("VOLUME_KMSCLIENT", StringComparison.OrdinalIgnoreCase) >= 0)
                            isVolumeKms = true;

                        // 7a. Phone activation anomaly (TSforge ZeroCID indicator)
                        if (isPhone && isLicensed)
                        {
                            LogError(L.Get("P7_PhoneChannel"));
                            criticalKms = true;
                            suspiciousCount++;
                        }

                        // 7b. GVLK + permanent check — must distinguish channel:
                        //   VOLUME_KMSCLIENT + permanent = KMS38 / TSforge / piracy (real KMS always has 180d countdown)
                        //   RETAIL / OEM_DM  + permanent = legitimate Digital Entitlement (HWID)
                        if (isGvlk && isPermanent && isVolumeKms)
                        {
                            LogError(string.Format(L.Get("P7_GvlkPermanentVolume"), ppk));
                            criticalKms = true;
                            suspiciousCount++;
                        }
                        else if (isGvlk && isPermanent && !isVolumeKms)
                        {
                            // RETAIL / OEM_DM channel + permanent = Digital Entitlement (HWID)
                            // This is indistinguishable from genuine DE — do NOT flag.
                            LogOk(string.Format(L.Get("P7_GvlkDeActivation"), ppk, 
                                desc.IndexOf("OEM_DM", StringComparison.OrdinalIgnoreCase) >= 0 ? "OEM_DM" :
                                desc.IndexOf("RETAIL", StringComparison.OrdinalIgnoreCase) >= 0 ? "RETAIL" : "non-VOLUME"));
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
                        LogFetch(L.Get("Fetch_ActExpiry"));
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
                    LogInfo(L.Get("P7_NoKeyWmi"));
            }
            catch (Exception ex) { LogWarn(string.Format(L.Get("P7_WmiErr"), ex.Message)); }

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
                    // Iterate all System log entries — no source filter to catch all WU events.
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

                    // Only warn if data.dat is significantly newer than install date AND
                    // there is no correlated Windows Update event.
                    // (data.dat predating install date is not a reliable indicator — skip that check.)
                    if (hasNearbyUpdate)
                        LogOk(L.Get("P7_SppStoreOk"));
                    else if (installDate > DateTime.MinValue && datMod > installDate.AddDays(2))
                    {
                        LogWarn(string.Format(L.Get("P7_SppStoreModified"),
                            datMod.ToString("yyyy-MM-dd HH:mm")));
                        suspiciousCount++;
                    }
                    else
                        LogOk(L.Get("P7_SppStoreOk"));
                }
            }
            catch (Exception ex) { LogWarn(string.Format(L.Get("P7_SppStoreErr"), ex.Message)); }

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
                    LogOk(L.Get("P7_SppEventsNone"));
                }
                else
                {
                    LogInfo(string.Format(L.Get("P7_SppEventsFound"), sppEvents.Count));
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
                                    LogError(string.Format(L.Get("P7_SppEventsExt"), addr));
                                    LogError(L.Get("P7_SppEventsConf"));
                                    criticalKms = true;
                                    externalKmsInEvent = true;
                                    suspiciousCount++;
                                }
                            }
                        }
                    }
                    if (!externalKmsInEvent)
                        LogOk(L.Get("P7_SppEventsOk"));
                }
            }
            catch (Exception ex) { LogWarn(string.Format(L.Get("P7_SppEventsErr"), ex.Message)); }

            // ── Summary ────────────────────────────────────────────────────

            LogBlank();
            LogSep();
            LogLine($"  {L.Get("P7_SummaryHeader")}", ColAction, bold: true);
            LogBlank();
            if (criticalKms)
            {
                LogError(L.Get("P7_Critical"));
                if (suspiciousCount > 1)
                    LogError(string.Format(L.Get("P7_IndicatorCount"), suspiciousCount));
            }
            else if (suspiciousCount > 0)
            {
                LogWarn(L.Get("P7_Suspicious"));
                LogWarn(string.Format(L.Get("P7_IndicatorCount"), suspiciousCount));
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

        // =========================================================================
        // Installed Key Decoder
        // =========================================================================
        private static string? DecodeProductKeyWin8AndUp(byte[] digitalProductId)
        {
            if (digitalProductId == null || digitalProductId.Length < 67)
                return null;

            var key = new byte[15];
            Array.Copy(digitalProductId, 52, key, 0, 15);
            var isWin8 = (byte)((key[14] / 6) & 1);
            key[14] = (byte)((key[14] & 0xf7) | ((isWin8 & 2) * 4));

            const string chars = "BCDFGHJKMPQRTVWXY2346789";
            var decodedChars = new char[25];
            
            int last = 0;
            for (var i = 24; i >= 0; i--)
            {
                int current = 0;
                for (var j = 14; j >= 0; j--)
                {
                    current = (current * 256) ^ key[j];
                    key[j] = (byte)(current / 24);
                    current %= 24;
                }
                decodedChars[i] = chars[current];
                last = current;
            }

            var decodedKey = new string(decodedChars);
            
            if (isWin8 == 1)
            {
                string keypart1 = decodedKey.Substring(1, last);
                string keypart2 = decodedKey.Substring(1 + last);
                decodedKey = keypart1 + "N" + keypart2;
            }

            for (var i = 5; i < decodedKey.Length; i += 6)
                decodedKey = decodedKey.Insert(i, "-");

            return decodedKey;
        }
        // =========================================================================
        // Option 6 — KMS Activation
        // =========================================================================

        // GVLK table: edition name pattern → full GVLK key
        private static readonly (string Pattern, string Edition, string Key)[] GvlkTable = new[]
        {
            ("Pro N",                  "Windows 10/11 Pro N",                   "MH37W-N47XK-V7XM9-C7227-GCQG9"),
            ("Pro Education N",        "Windows 10/11 Pro Education N",         "YVWGF-BXNMC-HTQYQ-CPQ99-66QFC"),
            ("Pro Education",          "Windows 10/11 Pro Education",           "6TP4R-GNPTD-KYYHQ-7B7DP-J447Y"),
            ("Pro",                    "Windows 10/11 Pro",                     "W269N-WFGWX-YVC9B-4J6C9-T83GX"),
            ("Education N",            "Windows 10/11 Education N",             "2WH4N-8QGBV-H22JP-CT43Q-MDWWJ"),
            ("Education",              "Windows 10/11 Education",               "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2"),
            ("Enterprise G N",         "Windows 10/11 Enterprise G N",          "44RPN-FTY23-9VTTB-MP9BX-T84FV"),
            ("Enterprise G",           "Windows 10/11 Enterprise G",            "YYVX9-NTFWV-6MDM3-9PT4T-4M68B"),
            ("Enterprise N",           "Windows 10/11 Enterprise N",            "DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4"),
            ("Enterprise LTSC 2021",   "Windows 10 Enterprise LTSC 2021",       "M7XTQ-FN8P6-TTKYV-9D4CC-J462D"),
            ("Enterprise LTSC 2019",   "Windows 10 Enterprise LTSC 2019",       "M7XTQ-FN8P6-TTKYV-9D4CC-J462D"),
            ("Enterprise LTSC 2016",   "Windows 10 Enterprise LTSC 2016",       "DCPHK-NFMTC-H88MJ-PFHPY-QJ4BJ"),
            ("Enterprise",             "Windows 10/11 Enterprise",              "NPPR9-FWDCX-D2C8J-H872K-2YT43"),
            ("Server 2022 Datacenter", "Windows Server 2022 Datacenter",        "WX4NM-KYWYW-QJJR4-XV3QB-6VM33"),
            ("Server 2022 Standard",   "Windows Server 2022 Standard",          "VDYBN-27WPP-V4HQT-9VMD4-VMK7H"),
            ("Server 2019 Datacenter", "Windows Server 2019 Datacenter",        "WMDGN-G9PQG-XVVXX-R3X43-63DFG"),
            ("Server 2019 Standard",   "Windows Server 2019 Standard",          "N69G4-B89J2-4G8F4-WWYCC-J464C"),
            ("Server 2016 Datacenter", "Windows Server 2016 Datacenter",        "CB7KF-BWN84-R7R2Y-793K2-8XDDG"),
            ("Server 2016 Standard",   "Windows Server 2016 Standard",          "WC2BQ-8NRM3-FDDYY-2BFGV-KHKQY"),
            ("Server 2012 R2 Datacenter","Windows Server 2012 R2 Datacenter",   "W3GGN-FT8W3-Y4M27-J84CP-Q3VJ9"),
            ("Server 2012 R2 Standard","Windows Server 2012 R2 Standard",       "D2N9P-3P6X9-2R39C-7RTCD-MDVJX"),
        };

        private string? _kmsResolvedHost = null;   // set during preflight, consumed by Proceed
        private bool    _kmsGvlkNeeded   = false;  // true when GVLK install required first
        private string? _kmsGvlkKey      = null;   // GVLK to install if needed
        private bool    _kmsManualNeeded = false;  // true when DNS failed, manual host required
        private bool    _kmsPort1688Ok   = false;  // true when TCP 1688 test passed

        private void BtnKmsActivate_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;

            // Log pre-arm to main output
            LogAction("Act8");
            LogInfo(L.Get("O8KMS_DESC1"));
            LogInfo(L.Get("O8KMS_DESC2"));
            LogInfo(L.Get("O8KMS_DESC3"));
            LogBlank();

            // Reset state
            _kmsResolvedHost = null;
            _kmsGvlkNeeded   = false;
            _kmsGvlkKey      = null;
            _kmsManualNeeded = false;
            _kmsPort1688Ok   = false;

            // Populate panel labels
            KmsPanelTitle.Text       = L.Get("O8KMS_PANEL_TITLE");
            KmsDesc1.Text            = L.Get("O8KMS_DESC1");
            KmsDesc2.Text            = L.Get("O8KMS_DESC2");
            KmsDesc3.Text            = L.Get("O8KMS_DESC3");
            KmsGvlkConfirmLabel.Text = L.Get("O8KMS_GVLK_CONFIRM");
            KmsManualLabel.Text      = L.Get("O8KMS_MANUAL_LABEL");
            KmsManualHint.Text       = L.Get("O8KMS_MANUAL_HINT");
            BtnKmsCancel.Content     = L.Get("O8KMS_CANCEL");
            BtnKmsProceed.Content    = L.Get("O8KMS_PROCEED");
            BtnKmsProceed.IsEnabled  = false;

            // Clear step labels
            KmsStep1.Text = L.Get("O8KMS_CHK_CHANNEL");
            KmsStep2.Text = L.Get("O8KMS_CHK_GVLK");
            KmsStep3.Text = L.Get("O8KMS_CHK_DNS");
            KmsStep4.Text = L.Get("O8KMS_CHK_PORT");
            KmsStep5.Text = L.Get("O8KMS_CHK_CLOCK");
            KmsStep6.Text = L.Get("O8KMS_CHK_ACTIVATE");
            KmsGvlkSection.Visibility   = Visibility.Collapsed;
            KmsManualSection.Visibility = Visibility.Collapsed;
            KmsHostBox.Text             = string.Empty;
            KmsGvlkOkBox.Text           = string.Empty;

            KmsPanel.Visibility = Visibility.Visible;
            EnsurePanelFits(KmsPanel);

            // Run preflight asynchronously to keep UI responsive
            _ = RunKmsPreflightAsync();
        }

        private async System.Threading.Tasks.Task RunKmsPreflightAsync()
        {
            // ── Step 1: Channel check ────────────────────────────────────────────
            string channelDesc = "(unknown)";
            string? partialKey = null;
            string? activeName = null;
            try
            {
                using var licRes = WmiQuery(
                    "SELECT Name,Description,PartialProductKey,ProductKeyChannel " +
                    "FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL");
                if (licRes != null)
                    foreach (ManagementObject obj in licRes)
                    {
                        var n = obj["Name"]?.ToString() ?? "";
                        if (!n.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)) continue;
                        channelDesc = obj["Description"]?.ToString() ?? "(unknown)";
                        partialKey  = obj["PartialProductKey"]?.ToString();
                        activeName  = n;
                        break;
                    }
            }
            catch { }

            bool isKmsClient = channelDesc.ToUpperInvariant().Contains("VOLUME_KMSCLIENT");
            bool isKmsHost   = channelDesc.ToUpperInvariant().Contains("VOLUME_KMS") && !isKmsClient;

            KmsStep1.Text = $"✔ {L.Get("O8KMS_CHK_CHANNEL")}  {channelDesc}";
            if (isKmsHost)      { LogWarn(L.Get("O8KMS_WARN_KMSHOST")); }
            if (!isKmsClient)   LogWarn(L.Get("O8KMS_WARN_NOTVOLUME"));

            await System.Threading.Tasks.Task.Delay(100);

            // ── Step 2: GVLK check ───────────────────────────────────────────────
            // VOLUME_KMSCLIENT channel means a GVLK is already installed (the definitive indicator).
            // Fallback: check the partial key suffix against our GVLK table.
            var gvlkSuffixes = new System.Collections.Generic.HashSet<string>(
                GvlkTable.Select(t => t.Key.Substring(t.Key.Length - 5)),
                StringComparer.OrdinalIgnoreCase);
            bool isGvlk = isKmsClient ||
                          (partialKey != null && gvlkSuffixes.Contains(partialKey));
            if (isGvlk)
            {
                KmsStep2.Text = $"✔ {L.Get("O8KMS_GVLK_OK")}{partialKey}";
                KmsGvlkSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                KmsStep2.Text = $"⚠ {L.Get("O8KMS_GVLK_MISSING")}";
                // Try to find GVLK for this edition
                string gvlkKey = "";
                string gvlkEdition = "";
                if (activeName != null)
                    foreach (var (pat, ed, k) in GvlkTable)
                        if (activeName.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                        { gvlkKey = k; gvlkEdition = ed; break; }

                if (!string.IsNullOrEmpty(gvlkKey))
                {
                    _kmsGvlkNeeded = true;
                    _kmsGvlkKey    = gvlkKey;
                    KmsGvlkLabel.Text = L.Get("O8KMS_GVLK_FOUND_KEY") + "  " + gvlkEdition;
                    KmsGvlkKey.Text   = gvlkKey;
                    KmsGvlkSection.Visibility = Visibility.Visible;
                    EnsurePanelFits(KmsPanel);
                }
                else
                {
                    KmsGvlkLabel.Text = L.Get("O8KMS_GVLK_NOMAP");
                    KmsGvlkSection.Visibility = Visibility.Visible;
                    EnsurePanelFits(KmsPanel);
                    // Cannot proceed without GVLK — leave proceed disabled
                    return;
                }
            }

            await System.Threading.Tasks.Task.Delay(100);

            // ── Step 3: DNS SRV lookup ───────────────────────────────────────────
            string? dnsHost = null;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("nslookup")
                {
                    Arguments = "-type=SRV _VLMCS._TCP",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                string nsOut = await System.Threading.Tasks.Task.Run(() =>
                {
                    string o = proc!.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    return o;
                });
                // Parse svr hostname from nslookup output: line containing "svr hostname = <host>"
                var m = System.Text.RegularExpressions.Regex.Match(
                    nsOut, @"svr hostname\s*=\s*(\S+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) dnsHost = m.Groups[1].Value.TrimEnd('.');
            }
            catch { }

            if (!string.IsNullOrEmpty(dnsHost))
            {
                _kmsResolvedHost = dnsHost;
                KmsStep3.Text = $"✔ {L.Get("O8KMS_DNS_FOUND")}{dnsHost}";
                KmsManualSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                KmsStep3.Text = $"⚠ {L.Get("O8KMS_DNS_FAIL")}";
                _kmsManualNeeded = true;
                KmsManualSection.Visibility = Visibility.Visible;
                EnsurePanelFits(KmsPanel);
                // Enable proceed so user can supply host manually; port/clock steps will run on proceed
                BtnKmsProceed.IsEnabled = true;
                return;
            }

            await System.Threading.Tasks.Task.Delay(50);

            // ── Step 4: TCP 1688 test ────────────────────────────────────────────
            await RunKmsPortCheckAsync(_kmsResolvedHost!);

            await System.Threading.Tasks.Task.Delay(50);

            // ── Step 5: Clock advisory ───────────────────────────────────────────
            KmsStep5.Text = $"ℹ {L.Get("O8KMS_CLOCK_WARN")}";

            BtnKmsProceed.IsEnabled = true;
        }

        private async System.Threading.Tasks.Task RunKmsPortCheckAsync(string host)
        {
            bool connected = await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var tcp = new System.Net.Sockets.TcpClient();
                    tcp.Connect(host, 1688);   // synchronous; 20 s OS default timeout
                    return true;
                }
                catch { return false; }
            });
            if (connected)
            {
                _kmsPort1688Ok = true;
                KmsStep4.Text = $"\u2714 {L.Get("O8KMS_PORT_OK")}{host}:1688";
            }
            else
            {
                _kmsPort1688Ok = false;
                KmsStep4.Text = $"\u26a0 {L.Get("O8KMS_PORT_FAIL")}{host}:1688";
            }
        }


        private async void BtnKmsProceed_Click(object sender, RoutedEventArgs e)
        {
            BtnKmsProceed.IsEnabled = false;
            BtnKmsCancel.IsEnabled  = false;

            // If DNS failed, read manual host from textbox
            if (_kmsManualNeeded)
            {
                var manualHost = KmsHostBox.Text.Trim();
                if (string.IsNullOrEmpty(manualHost))
                {
                    LogWarn(L.Get("O8KMS_CANCELED"));
                    KmsPanel.Visibility = Visibility.Collapsed;
                    return;
                }
                _kmsResolvedHost = manualHost;
                KmsStep3.Text = $"→ Manual host: {manualHost}";

                // Test port 1688 on manual host
                await RunKmsPortCheckAsync(manualHost);
                KmsStep5.Text = $"ℹ {L.Get("O8KMS_CLOCK_WARN")}";
            }

            KmsPanel.Visibility = Visibility.Collapsed;

            // ── GVLK install if needed ───────────────────────────────────────────
            if (_kmsGvlkNeeded && !string.IsNullOrEmpty(_kmsGvlkKey))
            {
                // User must have typed OK in KmsGvlkOkBox
                if (!KmsGvlkOkBox.Text.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    LogWarn(L.Get("O8KMS_GVLK_CANCELED"));
                    return;
                }
                LogInfo(L.Get("O8KMS_GVLK_INSTALLING"));
                var ipkOut = RunSlmgr($"/ipk {_kmsGvlkKey}");
                LogSlmgrOutput(ipkOut);
                if (ipkOut.Contains("Error") || ipkOut.Contains("0x"))
                {
                    LogError(L.Get("O8KMS_FAIL"));
                    LogHelp(L.Get("O8KMS_REF_URL"));
                    return;
                }
                LogOk(L.Get("O8KMS_GVLK_DONE"));
                LogBlank();
            }

            // ── /skms — persist only if port 1688 reachable ──────────────────────
            if (!string.IsNullOrEmpty(_kmsResolvedHost))
            {
                if (_kmsPort1688Ok)
                {
                    LogInfo(L.Get("O8KMS_SKMS_PERSIST"));
                    var skmsOut = RunSlmgr($"/skms {_kmsResolvedHost}");
                    LogSlmgrOutput(skmsOut);
                }
                else
                {
                    LogWarn(L.Get("O8KMS_SKMS_NOPERSIST"));
                }
                LogBlank();
            }

            // ── Step 6: slmgr /ato ───────────────────────────────────────────────
            KmsStep6.Text = $"→ {L.Get("O8KMS_CHK_ACTIVATE")}";
            LogInfo(L.Get("O8KMS_CHK_ACTIVATE"));
            var atoOut = RunSlmgr("/ato");
            LogSlmgrOutput(atoOut);

            bool atoFail = atoOut.Contains("Error") || atoOut.Contains("0x");
            LogBlank();
            if (!atoFail)
            {
                LogOk(L.Get("O8KMS_SUCCESS"));
                LogInfo(L.Get("O8KMS_SUCCESS2"));
            }
            else
            {
                LogError(L.Get("O8KMS_FAIL"));
                if      (atoOut.Contains("0x80070005")) LogDiag(L.Get("O2_DIAG_ACCESS_DENIED"));
                else if (atoOut.Contains("0xC004E003")) LogDiag(L.Get("O2_DIAG_LIC_EVAL_FAIL"));
                else if (atoOut.Contains("0xC004F00F")) LogDiag(L.Get("O2_DIAG_HW_CHANGED"));
                else if (atoOut.Contains("0xC004F038")) LogDiag(L.Get("O8KMS_DIAG_COUNT"));
                else if (atoOut.Contains("0xC004F039")) LogDiag(L.Get("O8KMS_DIAG_NOTENABLED"));
                else if (atoOut.Contains("0xC004F041")) LogDiag(L.Get("O8KMS_DIAG_HOSTNACT"));
                else if (atoOut.Contains("0xC004F042")) LogDiag(L.Get("O8KMS_DIAG_WRONGHOST"));
                else if (atoOut.Contains("0xC004F064")) LogDiag(L.Get("O2_DIAG_NONGENUINE_EXP"));
                else if (atoOut.Contains("0xC004F065")) LogDiag(L.Get("O2_DIAG_NONGENUINE_GRC"));
                else if (atoOut.Contains("0xC004F06C")) LogDiag(L.Get("O8KMS_DIAG_CLOCK"));
                else if (atoOut.Contains("0xC004F074")) LogDiag(L.Get("O8KMS_DIAG_NOCONTACT"));
                else if (atoOut.Contains("0x8007007B") || atoOut.Contains("0x8007232B") ||
                         atoOut.Contains("0x8007251D") || atoOut.Contains("0x80092328"))
                                                         LogDiag(L.Get("O8KMS_DIAG_DNS"));
                else if (atoOut.Contains("0x800706BA")) LogDiag(L.Get("O2_DIAG_RPC_UNAVAIL"));
                else if (atoOut.Contains("0x8007232A")) LogDiag(L.Get("O2_DIAG_DNS_FAIL"));
                else if (atoOut.Contains("0x80072EE2") || atoOut.Contains("0x80072EE7") || atoOut.Contains("0x80072EFD"))
                                                         LogDiag(L.Get("O2_DIAG_NET_TIMEOUT"));
                else if (atoOut.Contains("0xC004F035")) LogDiag(L.Get("O8KMS_DIAG_VOLK"));
                LogBlank();
                LogHelp(L.Get("O8KMS_REF_URL"));
            }
        }

        private void BtnKmsCancel_Click(object sender, RoutedEventArgs e)
        {
            KmsPanel.Visibility = Visibility.Collapsed;
            LogInfo(L.Get("O8KMS_CANCELED"));
        }

        // =====================================================================
        // Option 6 — Change Activation Channel
        // =====================================================================
        private string? _chTargetIsKms = null;   // "KMS" or "RETAIL"
        private string? _chGvlkKey     = null;

        private void BtnChangeChannel_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;

            LogAction("Act6");
            LogInfo(L.Get("O6CH_DESC"));
            LogBlank();

            // Detect current channel via WMI
            string channel = "(unknown)", edition = "(unknown)", partKey = "?????";
            try
            {
                using var res = WmiQuery(
                    "SELECT Name, Description, PartialProductKey FROM SoftwareLicensingProduct " +
                    "WHERE PartialProductKey IS NOT NULL");
                if (res != null)
                    foreach (System.Management.ManagementObject mo in res)
                    {
                        string n = mo["Name"]?.ToString() ?? "";
                        if (!n.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)) continue;
                        channel = mo["Description"]?.ToString() ?? channel;
                        partKey = mo["PartialProductKey"]?.ToString() ?? partKey;
                        edition = n;
                        break;
                    }
            }
            catch { }

            // Populate info labels
            ChPanelTitle.Text  = L.Get("O6CH_PANEL_TITLE");
            ChDesc.Text        = L.Get("O6CH_DESC");
            ChLblChannel.Text  = L.Get("O6CH_CURRENT_CHANNEL");
            ChValChannel.Text  = channel;
            ChLblEdition.Text  = L.Get("O6CH_CURRENT_EDITION");
            ChValEdition.Text  = edition;
            ChLblKey.Text      = L.Get("O6CH_CURRENT_KEY");
            ChValKey.Text      = partKey;

            BtnChToKms.Content    = L.Get("O6CH_TO_KMS");
            BtnChToRetail.Content = L.Get("O6CH_TO_RETAIL");
            BtnChCancel.Content   = L.Get("O6CH_CANCEL");
            BtnChProceed.Content  = L.Get("O6CH_PROCEED");

            // Disable buttons that don't apply
            bool isKmsClient = channel.ToUpperInvariant().Contains("VOLUME_KMSCLIENT");
            bool isKmsHost   = channel.ToUpperInvariant().Contains("VOLUME_KMS") && !isKmsClient;
            BtnChToKms.IsEnabled    = !isKmsClient && !isKmsHost;
            BtnChToRetail.IsEnabled = !isKmsHost;

            ChGvlkSection.Visibility  = Visibility.Collapsed;
            ChRetailSection.Visibility = Visibility.Collapsed;
            BtnChProceed.IsEnabled    = false;
            ChGvlkOkBox.Text          = string.Empty;
            _chTargetIsKms            = null;
            _chGvlkKey                = null;

            if (isKmsHost)
            {
                ChDesc.Text = L.Get("O6CH_HOST_WARN");
                LogWarn(L.Get("O6CH_HOST_WARN"));
            }

            ChangeChannelPanel.Visibility = Visibility.Visible;
            EnsurePanelFits(ChangeChannelPanel);
        }

        private void BtnChToKms_Click(object sender, RoutedEventArgs e)
        {
            _chTargetIsKms     = "KMS";
            ChRetailSection.Visibility = Visibility.Collapsed;

            // Look up GVLK for current edition
            string editionName = ChValEdition.Text;
            _chGvlkKey = null;
            string gvlkEdition = "";
            foreach (var entry in GvlkTable)
            {
                if (editionName.IndexOf(entry.Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _chGvlkKey  = entry.Key;
                    gvlkEdition = entry.Edition;
                    break;
                }
            }

            if (_chGvlkKey != null)
            {
                ChGvlkLabel.Text        = L.Get("O6CH_GVLK_LABEL") + "  " + gvlkEdition;
                ChGvlkKey.Text          = _chGvlkKey;
                ChGvlkConfirmLabel.Text = L.Get("O6CH_GVLK_CONFIRM");
                ChGvlkSection.Visibility = Visibility.Visible;
                BtnChProceed.IsEnabled   = true;
                EnsurePanelFits(ChangeChannelPanel);
            }
            else
            {
                ChGvlkLabel.Text         = L.Get("O6CH_GVLK_NOMAP");
                ChGvlkKey.Text           = string.Empty;
                ChGvlkSection.Visibility  = Visibility.Visible;
                BtnChProceed.IsEnabled    = false;
                LogWarn(L.Get("O6CH_GVLK_NOMAP"));
            }
        }

        private void BtnChToRetail_Click(object sender, RoutedEventArgs e)
        {
            _chTargetIsKms    = "RETAIL";
            ChGvlkSection.Visibility  = Visibility.Collapsed;
            ChRetailMsg.Text          = L.Get("O6CH_RETAIL_MSG");
            ChRetailSection.Visibility = Visibility.Visible;
            BtnChProceed.IsEnabled    = true;
            EnsurePanelFits(ChangeChannelPanel);
        }

        private void BtnChCancel_Click(object sender, RoutedEventArgs e)
        {
            ChangeChannelPanel.Visibility = Visibility.Collapsed;
            LogInfo(L.Get("O6CH_CANCELLED"));
        }

        private async void BtnChProceed_Click(object sender, RoutedEventArgs e)
        {
            BtnChProceed.IsEnabled = false;
            BtnChCancel.IsEnabled  = false;

            if (_chTargetIsKms == "RETAIL")
            {
                // Redirect to Option 2
                LogInfo(L.Get("O6CH_REDIRECT_OPT2"));
                ChangeChannelPanel.Visibility = Visibility.Collapsed;
                BtnChCancel.IsEnabled = true;
                await System.Threading.Tasks.Task.Delay(300);
                BtnTestKey_Click(BtnTestKey, new RoutedEventArgs());
                return;
            }

            if (_chTargetIsKms == "KMS" && _chGvlkKey != null)
            {
                // Validate OK
                if (!ChGvlkOkBox.Text.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    LogWarn(L.Get("O8KMS_GVLK_CANCELED"));
                    BtnChProceed.IsEnabled = true;
                    BtnChCancel.IsEnabled  = true;
                    return;
                }

                // Install GVLK
                LogInfo(L.Get("O6CH_GVLK_INSTALLING"));
                var ipkOut = RunSlmgr($"/ipk {_chGvlkKey}");
                LogSlmgrOutput(ipkOut);

                if (ipkOut.Contains("Error") || ipkOut.Contains("0x"))
                {
                    LogError(L.Get("O8KMS_FAIL"));
                    ChangeChannelPanel.Visibility = Visibility.Collapsed;
                    BtnChCancel.IsEnabled = true;
                    return;
                }

                LogOk(L.Get("O6CH_GVLK_DONE"));
                LogBlank();

                // Redirect to Option 8 (KMS Activation)
                LogInfo(L.Get("O6CH_REDIRECT_OPT8"));
                ChangeChannelPanel.Visibility = Visibility.Collapsed;
                BtnChCancel.IsEnabled = true;
                await System.Threading.Tasks.Task.Delay(500);
                BtnKmsActivate_Click(BtnKmsActivate, new RoutedEventArgs());
                return;
            }

            BtnChProceed.IsEnabled = true;
            BtnChCancel.IsEnabled  = true;
        }

        // =====================================================================
        // Option 7 — Check & Remove KMS Settings
        // =====================================================================

        private void BtnKmsSettings_Click(object sender, RoutedEventArgs e)
        {
            LogAction("Act7");
            LogInfo(L.Get("O7KMS_DESC"));
            LogBlank();

            // Read registry
            string regHost = L.Get("O7KMS_NOT_SET");
            string regPort = L.Get("O7KMS_DEFAULT_PORT");
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SoftwareLicensingService");
                if (key != null)
                {
                    var h = key.GetValue("KeyManagementServiceMachine") as string;
                    var p = key.GetValue("KeyManagementServicePort") as string;
                    if (!string.IsNullOrWhiteSpace(h)) regHost = h;
                    if (!string.IsNullOrWhiteSpace(p)) regPort = p;
                }
            }
            catch { }

            // Read slmgr /dlv for KMS host
            string dlvHost = L.Get("O7KMS_NOT_SET");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cscript")
                {
                    Arguments              = "//NoLogo %windir%\\System32\\slmgr.vbs /dlv",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                string dlvOut = proc!.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                var m = System.Text.RegularExpressions.Regex.Match(
                    dlvOut, @"KMS machine name[^:]*:\s*(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) dlvHost = m.Groups[1].Value.Trim();
            }
            catch { }

            // Populate labels
            Ks7PanelTitle.Text  = L.Get("O7KMS_PANEL_TITLE");
            Ks7Desc.Text        = L.Get("O7KMS_DESC");
            Ks7LblRegHost.Text  = L.Get("O7KMS_REG_HOST");
            Ks7ValRegHost.Text  = regHost;
            Ks7LblRegPort.Text  = L.Get("O7KMS_REG_PORT");
            Ks7ValRegPort.Text  = regPort;
            Ks7LblDlvHost.Text  = L.Get("O7KMS_DLV_HOST");
            Ks7ValDlvHost.Text  = dlvHost;
            BtnKs7Close.Content = L.Get("O7KMS_CLOSE");
            BtnKs7Clear.Content = L.Get("O7KMS_CLEAR_BTN");
            Ks7OkBox.Text       = string.Empty;

            bool hasCustomHost = regHost != L.Get("O7KMS_NOT_SET");
            if (hasCustomHost)
            {
                Ks7StatusLabel.Text       = L.Get("O7KMS_CUSTOM_ACTIVE");
                Ks7StatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#b45309"));
                Ks7ClearLabel.Text        = L.Get("O7KMS_CLEAR_LABEL");
                Ks7ConfirmLabel.Text      = L.Get("O7KMS_CLEAR_CONFIRM");
                Ks7ClearSection.Visibility  = Visibility.Visible;
                Ks7SuggestSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                Ks7StatusLabel.Text       = L.Get("O7KMS_NONE_ACTIVE");
                Ks7StatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15803d"));
                Ks7ClearSection.Visibility   = Visibility.Collapsed;
                Ks7SuggestSection.Visibility = Visibility.Collapsed;
            }

            // Log findings
            LogInfo(L.Get("O7KMS_REG_HOST")  + " " + regHost);
            LogInfo(L.Get("O7KMS_REG_PORT")  + " " + regPort);
            LogInfo(L.Get("O7KMS_DLV_HOST")  + " " + dlvHost);

            KmsSettingsPanel.Visibility = Visibility.Visible;
            EnsurePanelFits(KmsSettingsPanel);
        }

        private void BtnKs7Clear_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            if (!Ks7OkBox.Text.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                LogWarn(L.Get("O7KMS_CLEAR_CONFIRM"));
                return;
            }

            BtnKs7Clear.IsEnabled = false;
            LogInfo(L.Get("O7KMS_CLEARING"));

            var ckmsOut = RunSlmgr("/ckms");
            LogSlmgrOutput(ckmsOut);

            bool failed = ckmsOut.Contains("Error") || ckmsOut.Contains("0x");
            if (failed)
            {
                LogError(L.Get("O7KMS_CLEAR_FAILED"));
            }
            else
            {
                LogOk(L.Get("O7KMS_CLEARED"));
                Ks7ValRegHost.Text = L.Get("O7KMS_NOT_SET");
                Ks7ValRegPort.Text = L.Get("O7KMS_DEFAULT_PORT");
                Ks7StatusLabel.Text = L.Get("O7KMS_CLEARED");
                Ks7StatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15803d"));
                Ks7ClearSection.Visibility = Visibility.Collapsed;

                // Show next-step suggestions
                Ks7Suggest1.Text = L.Get("O7KMS_NEXT_OPT8");
                Ks7Suggest2.Text = L.Get("O7KMS_NEXT_OPT6");
                Ks7Suggest3.Text = L.Get("O7KMS_NEXT_DNS");
                Ks7SuggestSection.Visibility = Visibility.Visible;
                LogBlank();
                LogInfo(L.Get("O7KMS_NEXT_OPT8"));
                LogInfo(L.Get("O7KMS_NEXT_OPT6"));
                LogInfo(L.Get("O7KMS_NEXT_DNS"));
            }
        }

        private void BtnKs7Close_Click(object sender, RoutedEventArgs e)
        {
            KmsSettingsPanel.Visibility = Visibility.Collapsed;
        }
    }
}
