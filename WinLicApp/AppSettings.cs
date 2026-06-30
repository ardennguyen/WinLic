// =============================================================================
// AppSettings.cs  --  WinLic Manager
// =============================================================================
// Persists user customizations for the Option 7 audit scan to settings.ini
// placed alongside the executable.  The file has two blocks:
//   DEFAULT -- managed by WinLic, updated from GitHub (settings.default.ini)
//   USER    -- user additions, never overwritten by updates
//
// settings.ini section names use CamelCase (e.g. [ExtraPorts]) which is the
// canonical format shared with the PowerShell CLI.  The parser normalizes
// section headers by stripping underscores and uppercasing so both
// [ExtraPorts] and [EXTRA_PORTS] map to the same bucket.
//
// GVLK keys use key=value format:
//   W269N-WFGWX-YVC9B-4J6C9-T83GX = Windows 11/10 Pro
// The parser extracts the last 5 characters of the key portion and stores
// them in GvlkKeySuffixes for matching against WMI PartialProductKey.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace WinLicApp
{
    public static class AppSettings
    {
        // ── Paths ──────────────────────────────────────────────────────────────
        private static readonly string SettingsPath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                "settings.ini");

        /// <summary>GitHub raw URL for the default settings block.</summary>
        private const string DefaultsUrl =
            "https://raw.githubusercontent.com/ardennguyen/WinLic/main/WinLicPS/settings.default.ini";

        /// <summary>
        /// Marker line that separates the DEFAULT block from the USER block
        /// in settings.ini. The update routine replaces everything above this line.
        /// </summary>
        private const string UserBlockMarker = "USER BLOCK";

        // ── Built-in fallback defaults (used if settings.ini is missing) ──────
        // These are a minimal safety net. The authoritative list lives in
        // [KmsPiracyDomains] and [GvlkKeys] inside settings.ini.
        public static readonly int[] DefaultPorts = { 1688 };

        public static readonly string[] DefaultServices =
        {
            "KMSpico", "KMService", "WinKSO", "KMSELDI", "KMS_VL_ALL",
            "KMSAuto", "AutoKMS",  "KMSSS",  "KMSEmulator", "vlmcsd",
            "Activation-Renewal"
        };
        public static readonly string[] DefaultProcesses =
        {
            "KMSpico", "KMSELDI", "AutoKMS", "KMSAuto", "KMSguard",
            "WinKSO",  "KMService", "vlmcsd", "AAct",   "KMS_VL_ALL",
            "gatherosstate", "clipup"
        };
        public static readonly string[] DefaultTaskKeywords =
        {
            "AutoKMS", "KMSAuto", "KMS_VL_ALL", "KMSpico",
            "KMSSS",   "KMSEmulator", "KMService", "WinKSO", "vlmcsd",
            "Activation-Renewal"
        };

        // Hardcoded KMS piracy domains — mirrors [KmsPiracyDomains] in settings.default.ini.
        // Keep in sync with settings.default.ini when updating.
        public static readonly string[] DefaultKmsPiracyDomains =
        {
            "msguides",       // km8.msguides.com, kms2.msguides.com, kms9.msguides.com
            "kms.loli",       // kms.loli.beer
            "digiboy.ir",
            "0t.ng",
            "kms.chinancce",
            "kmscloud",
            "kms.cangshui",
            "kms.ddns.net",
            "e8.us.to",
            "kms.mrxinwang",
            "kms8.msguides",
            "kms9.msguides",
            "kms.xspace.in",
            "skms.netnr",
        };

        // Hardcoded GVLK suffix fallback — mirrors [GvlkKeys] in settings.default.ini.
        // Last 5 chars of each key; matched against WMI PartialProductKey.
        // Keep in sync with settings.default.ini when updating.
        public static readonly HashSet<string> HardcodedGvlkSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows 11 / 10 Semi-Annual Channel
            "T83GX", // Pro
            "GCQG9", // Pro N
            "6Q84J", // Pro for Workstations
            "6XYWF", // Pro for Workstations N
            "J447Y", // Pro Education
            "66QFC", // Pro Education N
            "VCFB2", // Education (also Win11 Pro Education HWID placeholder)
            "MDWWJ", // Education N
            "2YT43", // Enterprise
            "KHJW4", // Enterprise N
            "4M68B", // Enterprise G
            "T84FV", // Enterprise G N
            // LTSC / IoT / LTSB
            "J462D", // LTSC 2024 / Win10 LTSC 2021 / 2019
            "7CG2H", // Enterprise N LTSC
            "PDQGT", // IoT Enterprise LTSC 2024/2021
            "QJ4BJ", // Enterprise LTSB 2016
            "8B639", // Enterprise N LTSB 2016
            "76DF9", // Enterprise LTSB 2015
            "D69TJ", // Enterprise N LTSB 2015
            // Windows 8.1
            "9D6T9", // 8.1 Pro
            "B4FXY", // 8.1 Pro N
            "MKKG7", // 8.1 Enterprise
            "JFFXW", // 8.1 Enterprise N
            // HWID / DE placeholder keys
            "3V66T", // Win 10/11 Pro
            "8HVX7", // Win 10/11 Home
            "H8Q99", // Win 10 Home
            "WXCHW", // Home Single Language
            "WGGBY", // Pro Education
            "2YV77", // Pro for Workstations
            "8DEC2", // Enterprise
        };


        // ── Loaded from settings.ini DEFAULT block ─────────────────────────────
        /// <summary>Last-5-char suffixes from [GvlkKeys] in the default block.</summary>
        public static HashSet<string> DefaultGvlkSuffixes { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Piracy domains from [KmsPiracyDomains] in the default block.</summary>
        public static List<string> DefaultIniKmsPiracyDomains { get; private set; } = new List<string>();

        /// <summary>Services from [DefaultServices] in settings.ini default block.</summary>
        public static List<string> DefaultIniServices { get; private set; } = new List<string>();

        /// <summary>Tasks from [DefaultTaskKeywords] in settings.ini default block.</summary>
        public static List<string> DefaultIniTaskKeywords { get; private set; } = new List<string>();

        /// <summary>Processes from [DefaultProcesses] in settings.ini default block.</summary>
        public static List<string> DefaultIniProcesses { get; private set; } = new List<string>();

        /// <summary>Files from [DefaultFilePaths] in settings.ini default block.</summary>
        public static List<string> DefaultIniFilePaths { get; private set; } = new List<string>();

        /// <summary>Ports from [DefaultPorts] in settings.ini default block.</summary>
        public static List<int> DefaultIniPorts { get; private set; } = new List<int>();

        // ── Loaded from settings.ini USER block ────────────────────────────────
        public static List<int>    ExtraPorts            { get; set; } = new List<int>();
        public static List<string> ExtraServices         { get; set; } = new List<string>();
        public static List<string> ExtraProcesses        { get; set; } = new List<string>();
        public static List<string> ExtraTaskKeywords     { get; set; } = new List<string>();
        public static List<string> ExtraFilePaths        { get; set; } = new List<string>();
        public static List<string> ExtraKmsPiracyDomains { get; set; } = new List<string>();
        public static List<string> UserGvlkSuffixes      { get; set; } = new List<string>();

        // ── Merged views (defaults + ini defaults + user additions) ────────────
        public static int[] AllPorts =>
            DefaultPorts
            .Concat(DefaultIniPorts)
            .Concat(ExtraPorts)
            .Distinct().ToArray();

        public static HashSet<string> AllServices =>
            new HashSet<string>(
                DefaultServices
                .Concat(DefaultIniServices)
                .Concat(ExtraServices),
                StringComparer.OrdinalIgnoreCase);

        public static HashSet<string> AllProcesses =>
            new HashSet<string>(
                DefaultProcesses
                .Concat(DefaultIniProcesses)
                .Concat(ExtraProcesses),
                StringComparer.OrdinalIgnoreCase);

        public static string[] AllTaskKeywords =>
            DefaultTaskKeywords
            .Concat(DefaultIniTaskKeywords)
            .Concat(ExtraTaskKeywords)
            .Distinct().ToArray();

        /// <summary>All GVLK key suffixes (last 5 chars) from hardcoded fallback + ini defaults + user additions.</summary>
        public static HashSet<string> AllGvlkSuffixes =>
            new HashSet<string>(
                HardcodedGvlkSuffixes.Concat(DefaultGvlkSuffixes).Concat(UserGvlkSuffixes),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>Full piracy-domain list: hardcoded + ini defaults + user additions.</summary>
        public static string[] AllKmsPiracyDomains =>
            DefaultKmsPiracyDomains
            .Concat(DefaultIniKmsPiracyDomains)
            .Concat(ExtraKmsPiracyDomains)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // ── IO ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Normalizes a settings.ini section name so that [ExtraPorts],
        /// [EXTRA_PORTS], and [extra_ports] all resolve to "EXTRAPORTS".
        /// </summary>
        private static string NormalizeSection(string raw) =>
            raw.Replace("_", "").Replace(" ", "").ToUpperInvariant();

        /// <summary>
        /// Extract the last 5 alphanumeric characters of a product key string.
        /// Input: "W269N-WFGWX-YVC9B-4J6C9-T83GX" → "T83GX"
        /// </summary>
        private static string? ExtractKeySuffix(string keyLine)
        {
            // Strip comment portion (everything after '=')
            var key = keyLine.Contains('=') ? keyLine.Substring(0, keyLine.IndexOf('=')).Trim() : keyLine.Trim();
            // Strip dashes and take last 5 chars
            var alnum = key.Replace("-", "").Replace(" ", "");
            if (alnum.Length < 5) return null;
            return alnum.Substring(alnum.Length - 5).ToUpperInvariant();
        }

        /// <summary>Strip inline comments from a settings value line (text after ';').</summary>
        private static string StripInlineComment(string line)
        {
            var idx = line.IndexOf(';');
            return idx >= 0 ? line.Substring(0, idx).Trim() : line.Trim();
        }

        public static void Load()
        {
            // Reset all loaded lists
            DefaultGvlkSuffixes.Clear();
            DefaultIniKmsPiracyDomains.Clear();
            DefaultIniServices.Clear();
            DefaultIniTaskKeywords.Clear();
            DefaultIniProcesses.Clear();
            DefaultIniFilePaths.Clear();
            DefaultIniPorts.Clear();

            ExtraPorts.Clear(); ExtraServices.Clear();
            ExtraProcesses.Clear(); ExtraTaskKeywords.Clear();
            ExtraFilePaths.Clear(); ExtraKmsPiracyDomains.Clear();
            UserGvlkSuffixes.Clear();

            if (!File.Exists(SettingsPath)) return;
            try
            {
                string section = "";
                foreach (var raw in File.ReadAllLines(SettingsPath))
                {
                    var l = raw.Trim();
                    // Skip blanks and full-line comments (both ; and # are comment chars)
                    if (string.IsNullOrEmpty(l) || l.StartsWith(";") || l.StartsWith("#")) continue;
                    if (l.StartsWith("[") && l.EndsWith("]"))
                    {
                        section = NormalizeSection(l.Substring(1, l.Length - 2));
                        continue;
                    }

                    // Strip inline comments for value parsing
                    var value = StripInlineComment(l);
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    switch (section)
                    {
                        // ── DEFAULT block sections ──────────────────────────
                        case "GVLKKEYS":
                        {
                            var suffix = ExtractKeySuffix(value);
                            if (suffix != null) DefaultGvlkSuffixes.Add(suffix);
                            break;
                        }
                        case "KMSPIRACYDOMAINS":
                            DefaultIniKmsPiracyDomains.Add(value); break;
                        case "DEFAULTPORTS":
                            if (int.TryParse(value, out int dp)) DefaultIniPorts.Add(dp); break;
                        case "DEFAULTSERVICES":
                            DefaultIniServices.Add(value); break;
                        case "DEFAULTTASKEYWORDS":
                        case "DEFAULTTASKKEYWORDS":
                            DefaultIniTaskKeywords.Add(value); break;
                        case "DEFAULTPROCESSES":
                            DefaultIniProcesses.Add(value); break;
                        case "DEFAULTFILEPATHS":
                            DefaultIniFilePaths.Add(value); break;

                        // ── USER block sections ─────────────────────────────
                        case "USERGVLKKEYS":
                        {
                            var suffix = ExtractKeySuffix(value);
                            if (suffix != null) UserGvlkSuffixes.Add(suffix);
                            break;
                        }
                        case "USERKMSPIRACYDOMAINS":
                            ExtraKmsPiracyDomains.Add(value); break;

                        // Legacy / user extra sections (backward compatible)
                        case "EXTRAPORTS":
                            if (int.TryParse(value, out int p)) ExtraPorts.Add(p); break;
                        case "EXTRASERVICES":
                            ExtraServices.Add(value); break;
                        case "EXTRAPROCESSES":
                            ExtraProcesses.Add(value); break;
                        case "EXTRATASKEYWORDS":
                        case "EXTRATASKKEYWORDS":
                            ExtraTaskKeywords.Add(value); break;
                        case "EXTRAFILEPATHS":
                            ExtraFilePaths.Add(value); break;
                    }
                }
            }
            catch { /* silently ignore corrupt settings */ }
        }

        public static void Save()
        {
            // Save only user-block sections. The default block is managed by
            // UpdateDefaultsAsync() and preserved as-is.
            if (!File.Exists(SettingsPath))
            {
                // If settings.ini doesn't exist, create a minimal user block.
                try
                {
                    using var w = new StreamWriter(SettingsPath, append: false);
                    WriteUserBlock(w);
                }
                catch { }
                return;
            }

            try
            {
                // Read the existing file and find the USER block marker.
                var lines = File.ReadAllLines(SettingsPath).ToList();
                int markerLine = lines.FindIndex(l => l.Contains(UserBlockMarker));

                // Build the new user block content
                using var ms = new System.IO.MemoryStream();
                using var writer = new StreamWriter(ms, System.Text.Encoding.UTF8, 4096, true);
                WriteUserBlock(writer);
                writer.Flush();
                ms.Position = 0;
                var newUserBlock = new StreamReader(ms).ReadToEnd();

                if (markerLine >= 0)
                {
                    // Preserve default block up to (and including) the marker, replace user block
                    var defaultPart = string.Join(Environment.NewLine, lines.Take(markerLine + 1));
                    File.WriteAllText(SettingsPath,
                        defaultPart + Environment.NewLine + Environment.NewLine + newUserBlock);
                }
                else
                {
                    // No marker found — append user block
                    File.AppendAllText(SettingsPath, Environment.NewLine + newUserBlock);
                }
            }
            catch { /* ignore write failures */ }
        }

        private static void WriteUserBlock(StreamWriter w)
        {
            w.WriteLine();
            w.WriteLine("[UserGvlkKeys]");
            w.WriteLine("; Add custom GVLK/suspicious keys here: FULL-KEY = Description");
            foreach (var s in UserGvlkSuffixes)
                w.WriteLine("; (stored suffix) " + s);
            w.WriteLine();

            w.WriteLine("[UserKmsPiracyDomains]");
            w.WriteLine("; Add your own KMS piracy hostnames here");
            foreach (var s in ExtraKmsPiracyDomains) w.WriteLine(s);
            w.WriteLine();

            w.WriteLine("[ExtraPorts]");
            w.WriteLine("; Additional TCP ports to probe on localhost");
            foreach (var p in ExtraPorts) w.WriteLine(p);
            w.WriteLine();

            w.WriteLine("[ExtraServices]");
            foreach (var s in ExtraServices) w.WriteLine(s);
            w.WriteLine();

            w.WriteLine("[ExtraTaskKeywords]");
            foreach (var s in ExtraTaskKeywords) w.WriteLine(s);
            w.WriteLine();

            w.WriteLine("[ExtraProcesses]");
            foreach (var s in ExtraProcesses) w.WriteLine(s);
            w.WriteLine();

            w.WriteLine("[ExtraFilePaths]");
            foreach (var s in ExtraFilePaths) w.WriteLine(s);
        }

        // ── Auto-update ───────────────────────────────────────────────────────
        /// <summary>
        /// Downloads the latest default settings block from GitHub and replaces
        /// the DEFAULT block in settings.ini while preserving the USER block.
        /// </summary>
        /// <returns>True on success, false on network/IO error.</returns>
        public static async Task<bool> UpdateDefaultsAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.Add("User-Agent", "WinLicApp/1.0");
                var downloaded = await http.GetStringAsync(DefaultsUrl);

                string userBlock = "";
                if (File.Exists(SettingsPath))
                {
                    var existing = File.ReadAllLines(SettingsPath);
                    int markerLine = Array.FindIndex(existing, l => l.Contains(UserBlockMarker));
                    if (markerLine >= 0)
                        userBlock = string.Join(Environment.NewLine,
                            existing.Skip(markerLine));
                }

                // Build the timestamp comment
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");
                // Inject timestamp into downloaded content
                var updatedDefault = downloaded.Replace(
                    "Last-Updated:",
                    $"Last-Updated: {timestamp}  ;");

                var combined = updatedDefault.TrimEnd()
                    + Environment.NewLine + Environment.NewLine
                    + "# " + new string('═', 73) + Environment.NewLine
                    + "# ║  USER BLOCK  --  Edit freely. NEVER overwritten by \"Update defaults\".    ║" + Environment.NewLine
                    + "# " + new string('═', 73) + Environment.NewLine
                    + Environment.NewLine
                    + (string.IsNullOrWhiteSpace(userBlock) ? GetDefaultUserBlock() : userBlock);

                File.WriteAllText(SettingsPath, combined);
                Load(); // Reload after update
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetDefaultUserBlock() =>
            @"[UserGvlkKeys]
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
";

        public static string SettingsFilePath => SettingsPath;
    }
}
