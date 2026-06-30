using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;

namespace WinLicApp
{
    public partial class AboutDialog : Window
    {
        // Current version tag — must match the GitHub release tag format (e.g. "v1.0-beta2")
        // Keep in sync with About_Version in Localization.cs.
        private const string CurrentVersion = "v1.0-beta2";
        private const string ReleasesUrl    = "https://github.com/ardennguyen/WinLic/releases";
        private const string LatestApiUrl   = "https://api.github.com/repos/ardennguyen/WinLic/releases/latest";

        public AboutDialog()
        {
            InitializeComponent();
            ApplyLanguage();
            // Kick off async version check without blocking the UI
            _ = CheckForUpdateAsync();
        }

        private void ApplyLanguage()
        {
            Title              = L.Get("About_Title");
            TxtVersion.Text    = L.Get("About_Version");
            TxtLblAuthor.Text  = L.Get("About_Author");
            TxtLblGitHub.Text  = L.Get("About_GitHub");
            TxtLblRepo.Text    = L.Get("About_Repo");
            TxtDesc.Text       = L.Get("About_Desc");
            BtnClose.Content   = L.Get("About_Close");
            TxtVerStatus.Text  = L.Get("About_CheckingVer");
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                // GitHub API requires a User-Agent header
                client.DefaultRequestHeaders.Add("User-Agent", "WinLicApp-UpdateCheck/1.0");

                var json = await client.GetStringAsync(LatestApiUrl).ConfigureAwait(false);

                // Parse "tag_name" field from JSON without adding a JSON library
                var match = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (!match.Success)
                {
                    SetVerStatus(L.Get("About_VerError"), isNew: false);
                    return;
                }

                var latestTag = match.Groups[1].Value.Trim();

                // Compare tags — treat as newer if they differ (simple string compare on semver-like tags)
                bool isNewer = !string.Equals(latestTag, CurrentVersion, StringComparison.OrdinalIgnoreCase)
                               && IsTagNewer(latestTag, CurrentVersion);

                if (isNewer)
                {
                    // Run on UI thread to update controls
                    Dispatcher.Invoke(() =>
                    {
                        TxtVerStatus.Text        = L.Get("About_NewVer");
                        TxtVerStatus.Foreground  = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x7c, 0x3a, 0xed));

                        LinkNewVersion.NavigateUri = new Uri(ReleasesUrl);
                        LinkNewVersion.Inlines.Clear();
                        LinkNewVersion.Inlines.Add(latestTag);
                        TxtVerLink.Visibility = Visibility.Visible;
                    });
                }
                else
                {
                    SetVerStatus(L.Get("About_UpToDate"), isNew: false,
                        color: System.Windows.Media.Color.FromRgb(0x16, 0xa3, 0x4a));
                }
            }
            catch
            {
                SetVerStatus(L.Get("About_VerError"), isNew: false);
            }
        }

        private void SetVerStatus(string text, bool isNew,
            System.Windows.Media.Color? color = null)
        {
            Dispatcher.Invoke(() =>
            {
                TxtVerStatus.Text = text;
                if (color.HasValue)
                    TxtVerStatus.Foreground = new System.Windows.Media.SolidColorBrush(color.Value);
                TxtVerLink.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        /// <summary>
        /// Returns true if <paramref name="latest"/> is a higher version than <paramref name="current"/>.
        /// Strips leading 'v' and compares numerically where possible, falls back to string ordering.
        /// </summary>
        private static bool IsTagNewer(string latest, string current)
        {
            Func<string, string> strip = s => s.TrimStart(new[] { 'v', 'V' });

            Func<string, Version?> parse = s =>
            {
                var m = Regex.Match(strip(s), @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?");
                if (!m.Success) return null;
                int major = int.Parse(m.Groups[1].Value);
                int minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                int patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                return new Version(major, minor, patch);
            };

            var lv = parse(latest);
            var cv = parse(current);
            if (lv != null && cv != null && lv != cv) return lv > cv;

            bool latestIsRelease = !strip(latest).Contains("-");
            bool currentIsPreRel = strip(current).Contains("beta") || strip(current).Contains("rc");
            return latestIsRelease && currentIsPreRel;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
