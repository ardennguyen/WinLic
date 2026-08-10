using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace WinLicApp
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();
            ApplyLanguage();
            PopulateFields();
        }

        // ── Language ──────────────────────────────────────────────────────────────
        private void ApplyLanguage()
        {
            Title               = L.Get("SD_Title");
            TxtDialogTitle.Text = L.Get("SD_Title");
            TxtDialogDesc.Text  = L.Get("SD_Desc");

            GbGenericKeys.Header = L.Get("SD_GenericKeys");
            GbPorts.Header    = L.Get("SD_Ports");
            GbServices.Header = L.Get("SD_Services");
            GbTasks.Header    = L.Get("SD_Tasks");
            GbProcs.Header    = L.Get("SD_Procs");
            GbFiles.Header    = L.Get("SD_Files");

            TxtGenericDefNote.Text       = L.Get("SD_GenericDefNote");
            TxtGenericDefaultsLabel.Text = L.Get("SD_Defaults");
            TxtGenericUserLabel.Text     = L.Get("SD_GenericUserNote");
            TxtGenericLinkLabel.Text     = L.Get("SD_GenericLink");

            var defLabel   = L.Get("SD_Defaults");
            var custLabel  = L.Get("SD_Custom");
            TxtDefaultsLabel0.Text = defLabel; TxtCustomLabel0.Text = custLabel;
            TxtDefaultsLabel1.Text = defLabel; TxtCustomLabel1.Text = custLabel;
            TxtDefaultsLabel2.Text = defLabel; TxtCustomLabel2.Text = custLabel;
            TxtDefaultsLabel3.Text = defLabel; TxtCustomLabel3.Text = custLabel;

            TxtFilesNote.Text  = L.Get("SD_FilesNote");
            TxtSaveNote.Text   = L.Get("SD_SaveNote");
            BtnSave.Content           = L.Get("SD_Save");
            BtnCancel.Content         = L.Get("Dialog_Cancel");
            BtnUpdateDefaults.Content = L.Get("P7_UpdateDefaults");
        }

        // ── Populate with current values ──────────────────────────────────────────
        private void PopulateFields()
        {
            // All known keys — GVLK (KMS) first, then HWID/DE placeholder keys
            TxtDefaultGenericKeys.Text = string.Join(Environment.NewLine,
                AppSettings.AllKeyDescriptionsForDisplay
                    .OrderBy(kv => kv.Value)     // groups [HWID/DE] then [KMS/GVLK] alphabetically
                    .Select(kv => $"{kv.Key} = {kv.Value}"));

            // Generic keys — user additions
            TxtExtraGenericKeys.Text = string.Join(Environment.NewLine,
                AppSettings.UserGenericKeyDescriptions
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key} = {kv.Value}"));

            TxtDefaultPorts.Text    = string.Join(Environment.NewLine, AppSettings.DefaultPorts);
            TxtDefaultServices.Text = string.Join(Environment.NewLine, AppSettings.DefaultServices);
            TxtDefaultTasks.Text    = string.Join(Environment.NewLine, AppSettings.DefaultTaskKeywords);
            TxtDefaultProcs.Text    = string.Join(Environment.NewLine, AppSettings.DefaultProcesses);

            TxtExtraPorts.Text    = string.Join(Environment.NewLine, AppSettings.ExtraPorts);
            TxtExtraServices.Text = string.Join(Environment.NewLine, AppSettings.ExtraServices);
            TxtExtraTasks.Text    = string.Join(Environment.NewLine, AppSettings.ExtraTaskKeywords);
            TxtExtraProcs.Text    = string.Join(Environment.NewLine, AppSettings.ExtraProcesses);
            TxtExtraFiles.Text    = string.Join(Environment.NewLine, AppSettings.ExtraFilePaths);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private static System.Collections.Generic.List<string> ParseLines(string text) =>
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

        // ── Buttons ───────────────────────────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Parse ports (integers only)
            AppSettings.ExtraPorts = ParseLines(TxtExtraPorts.Text)
                .Select(l => int.TryParse(l, out int p) ? p : -1)
                .Where(p => p > 0 && p <= 65535)
                .Distinct()
                .ToList();

            // Parse user generic keys: KEY = Description  (or bare KEY)
            var userGeneric = new System.Collections.Generic.Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var line in ParseLines(TxtExtraGenericKeys.Text))
            {
                var suffix = line.Contains('=')
                    ? line.Substring(0, line.IndexOf('=')).Trim()
                    : line.Trim();
                // Extract last 5 alphanumeric chars
                var alnum = new string(suffix.Where(char.IsLetterOrDigit).ToArray());
                if (alnum.Length >= 5)
                {
                    var key5 = alnum.Substring(alnum.Length - 5).ToUpperInvariant();
                    var desc = line.Contains('=')
                        ? line.Substring(line.IndexOf('=') + 1).Trim()
                        : "Custom generic key";
                    userGeneric[key5] = desc;
                }
            }
            AppSettings.UserGenericKeyDescriptions = userGeneric;

            AppSettings.ExtraServices     = ParseLines(TxtExtraServices.Text);
            AppSettings.ExtraTaskKeywords = ParseLines(TxtExtraTasks.Text);
            AppSettings.ExtraProcesses    = ParseLines(TxtExtraProcs.Text);
            AppSettings.ExtraFilePaths    = ParseLines(TxtExtraFiles.Text);

            AppSettings.Save();

            MessageBox.Show(
                L.Get("SD_Saved") + "\n" + AppSettings.SettingsFilePath,
                L.Get("SD_Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void BtnUpdateDefaults_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdateDefaults.IsEnabled = false;
            TxtSaveNote.Text = L.Get("P7_UpdateChecking");
            TxtSaveNote.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));

            try
            {
                string branch = await AppSettings.UpdateDefaultsAsync();
                if (!string.IsNullOrEmpty(branch))
                {
                    PopulateFields();
                    TxtSaveNote.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x40, 0xA0, 0x50));
                    TxtSaveNote.Text = string.Format(L.Get("P7_UpdateSuccess"), branch);
                }
                else
                {
                    TxtSaveNote.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0x40, 0x40));
                    TxtSaveNote.Text = L.Get("P7_UpdateFail");
                }
            }
            catch
            {
                TxtSaveNote.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0x40, 0x40));
                TxtSaveNote.Text = L.Get("P7_UpdateFail");
            }

            BtnUpdateDefaults.Content   = L.Get("P7_UpdateDefaults");
            BtnUpdateDefaults.IsEnabled = true;
        }
        private void HlinkMsKeys_RequestNavigate(object sender,
            System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
