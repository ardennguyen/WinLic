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
            // Windows 11 / 10 Semi-Annual Channel (KMS Client / GVLK)
            "T83GX", // Pro
            "GCQG9", // Pro N
            "6Q84J", // Pro for Workstations
            "6XYWF", // Pro for Workstations N
            "J447Y", // Pro Education
            "66QFC", // Pro Education N
            "VCFB2", // Education
            "MDWWJ", // Education N
            "2YT43", // Enterprise
            "KHJW4", // Enterprise N
            "4M68B", // Enterprise G
            "T84FV", // Enterprise G N
            "H8Q99", // Home (KMS)
            "844BM", // Home N (KMS)
            "YTDFH", // Home Single Language (KMS)
            "D3WVR", // Home Country Specific (KMS)
            // LTSC / IoT / LTSB
            "J462D", // LTSC 2024 / Win10 LTSC 2021 / 2019
            "7CG2H", // Enterprise N LTSC
            "PDQGT", // IoT Enterprise LTSC 2024/2021
            "QJ4BJ", // Enterprise LTSB 2016
            "8B639", // Enterprise N LTSB 2016
            "76DF9", // Enterprise LTSB 2015
            "D69TJ", // Enterprise N LTSB 2015
            "MDWWW", // Enterprise S (KMS)
            // Windows 8.1
            "9D6T9", // 8.1 Pro
            "B4FXY", // 8.1 Pro N
            "MKKG7", // 8.1 Enterprise
            "JFFXW", // 8.1 Enterprise N
            // Generic RTM / HWID / OEM 3.0 Placeholder Keys
            "8HVX7", // Win 11/10 Home (RTM)
            "WXCHW", // Win 11/10 Home N (RTM) / Home SL (HWID)
            "6F4BT", // Win 11/10 Home SL (RTM)
            "8TYMD", // Win 11 Home Country Specific (RTM)
            "3V66T", // Win 11/10 Pro (RTM / HWID)
            "PKCKT", // Win 11/10 Pro N (RTM)
            "2YV77", // Win 11/10 Pro for Workstations (RTM)
            "WT2RQ", // Win 11/10 Pro N for Workstations (RTM)
            "MHBPB", // Win 11/10 Pro Education (RTM)
            "QPF8P", // Win 11/10 Pro Education N (RTM)
            "7CFBY", // Win 11/10 Education (RTM)
            "DRR8H", // Win 11/10 Education N (RTM)
            "8HV2C", // Win 11/10 Enterprise (RTM)
            "X766F", // Win 11/10 Enterprise N (RTM)
            "B4H4T", // Win 11/10 Enterprise G N (RTM)
            "7QW8P", // Win 10 S (RTM)
            "DYJWX", // Win 10 Enterprise S (RTM)
            "V22HG", // Win 10 Home (Default)
            "DGPHV", // Win 10 Home N (Default)
            "46YP4", // Win 10 Pro / Pro N (Default)
            "D3WV4", // Win 10 SL (Default)
            "9D7YC", // Win 10 CHN SL (Default)
            "FC2HD", // Win 10 Home (OEM 3.0)
            "VH636", // Win 10 Home N (OEM 3.0)
            "WFG6P", // Win 10 Pro (OEM 3.0)
            "TCQG3", // Win 10 Pro N (OEM 3.0)
            "HH64C", // Win 10 SL (OEM 3.0)
            "H6DDY", // Win 10 CHN SL (OEM 3.0)
            "WGGBY", // Pro Education (HWID)
            "8DEC2", // Enterprise (HWID)
        };

        // Hardcoded GVLK key suffix → description for display (mirrors HardcodedGvlkSuffixes).
        public static readonly Dictionary<string, string> HardcodedGvlkKeyDescriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows 11 / 10 Semi-Annual Channel
            { "T83GX", "Windows 10/11 Pro  (W269N-WFGWX-YVC9B-4J6C9-T83GX)" },
            { "GCQG9", "Windows 10/11 Pro N  (MH37W-N47XK-V7XM9-C7227-GCQG9)" },
            { "6Q84J", "Windows 10/11 Pro for Workstations  (NRG8B-VKK3Q-CXVCJ-9G2XF-6Q84J)" },
            { "6XYWF", "Windows 10/11 Pro for Workstations N  (9FNHH-K3HBT-3W4TD-6383H-6XYWF)" },
            { "J447Y", "Windows 10/11 Pro Education  (6TP4R-GNPTD-KYYHQ-7B7DP-J447Y)" },
            { "66QFC", "Windows 10/11 Pro Education N  (YVWGF-BXNMC-HTQYQ-CPQ99-66QFC)" },
            { "VCFB2", "Windows 10/11 Education  (NW6C2-QMPVW-D7KKK-3GKT6-VCFB2)" },
            { "MDWWJ", "Windows 10/11 Education N  (2WH4N-8QGBV-H22JP-CT43Q-MDWWJ)" },
            { "2YT43", "Windows 10/11 Enterprise  (NPPR9-FWDCX-D2C8J-H872K-2YT43)" },
            { "KHJW4", "Windows 10/11 Enterprise N  (DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4)" },
            { "4M68B", "Windows 10 Enterprise G  (YYVX9-NTFWV-6MDM3-9PT4T-4M68B)" },
            { "T84FV", "Windows 10 Enterprise G N  (44RPN-FTY23-9VTTB-MP9BX-T84FV)" },
            { "H8Q99", "Windows 10/11 Home KMS  (TX9XD-98N7V-6WMQ6-BX7FG-H8Q99)" },
            { "844BM", "Windows 10/11 Home N KMS  (3KHY7-WNT83-DGQKR-F7HPR-844BM)" },
            { "YTDFH", "Windows 10/11 Home Single Language KMS  (7HNRX-D7KGG-3K4RQ-4WPJ4-YTDFH)" },
            { "D3WVR", "Windows 11 Home Country Specific KMS  (PVMJN-6DFY6-9CCP6-7BKTT-D3WVR)" },
            // LTSC / IoT / LTSB
            { "J462D", "Windows 11 LTSC 2024 / 10 LTSC 2021 / 2019  (M7XTQ-FN8P6-TTKYV-9D4CC-J462D)" },
            { "7CG2H", "Windows 10/11 Enterprise N LTSC  (92NFX-8DJQP-P6BBQ-THF9C-7CG2H)" },
            { "PDQGT", "Windows IoT Enterprise LTSC 2024/2021  (KBN8V-HFGQ4-MGXVD-347P6-PDQGT)" },
            { "QJ4BJ", "Windows 10 Enterprise LTSB 2016  (DCPHK-NFMTC-H88MJ-PFHPY-QJ4BJ)" },
            { "8B639", "Windows 10 Enterprise N LTSB 2016  (QFFDN-GRT3P-VKWWX-X7T3R-8B639)" },
            { "76DF9", "Windows 10 Enterprise LTSB 2015  (WNMTR-4C88C-JK8YV-HQ7T2-76DF9)" },
            { "D69TJ", "Windows 10 Enterprise N LTSB 2015  (2F77B-TNFGY-69QQF-B8YKP-D69TJ)" },
            { "MDWWW", "Windows 10 Enterprise S KMS  (FWN7H-PF93Q-4GGP8-M8RF3-MDWWW)" },
            // Windows 8.1
            { "9D6T9", "Windows 8.1 Pro  (GCRJD-8NW9H-F2CDX-CCM8D-9D6T9)" },
            { "B4FXY", "Windows 8.1 Pro N  (HMCNV-VVBFX-7HMBH-CTY9B-B4FXY)" },
            { "MKKG7", "Windows 8.1 Enterprise  (MHF9N-XY6XB-WVXMC-BTDCT-MKKG7)" },
            { "JFFXW", "Windows 8.1 Enterprise N  (TT4HM-HN7YT-62K67-RGRQJ-JFFXW)" },
        };

        // Hardcoded HWID/DE generic placeholder key fallback.
        // Maps last-5-char suffix → human-readable description.
        // Ref: https://learn.microsoft.com/en-us/windows-server/get-started/kms-client-activation-keys
        public static readonly Dictionary<string, string> HardcodedGenericKeys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "8HVX7", "Windows 11/10 Home RTM  (YTMG3-N6DKC-DKB77-7M9GH-8HVX7)" },
            { "WXCHW", "Windows 11/10 Home N RTM / Home SL HWID  (4CPRK-NM3K3-X6XXQ-RXX86-WXCHW)" },
            { "6F4BT", "Windows 11/10 Home Single Language RTM  (BT79Q-G7N6G-PGBYW-4YWX6-6F4BT)" },
            { "8TYMD", "Windows 11 Home Country Specific RTM  (N2434-X9D7W-8PF6X-8DV9T-8TYMD)" },
            { "3V66T", "Windows 11/10 Pro RTM / HWID  (VK7JG-NPHTM-C97JM-9MPGT-3V66T)" },
            { "PKCKT", "Windows 11/10 Pro N RTM  (2B87N-8KFHP-DKV6R-Y2C8J-PKCKT)" },
            { "2YV77", "Windows 11/10 Pro for Workstations RTM  (DXG7C-N36C4-C4HTG-X4T3X-2YV77)" },
            { "WT2RQ", "Windows 11/10 Pro N for Workstations RTM  (WYPNQ-8C467-V2W6J-TX4WX-WT2RQ)" },
            { "MHBPB", "Windows 11/10 Pro Education RTM  (8PTT6-RNW4C-6V7J2-C2D3X-MHBPB)" },
            { "QPF8P", "Windows 11/10 Pro Education N RTM  (GJTYN-HDMQY-FRR76-HVGC7-QPF8P)" },
            { "7CFBY", "Windows 11/10 Education RTM  (YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY)" },
            { "DRR8H", "Windows 11/10 Education N RTM  (84NGF-MHBT6-FXBX8-QWJK7-DRR8H)" },
            { "8HV2C", "Windows 11/10 Enterprise RTM  (XGVPP-NMH47-7TTHJ-W3FW7-8HV2C)" },
            { "X766F", "Windows 11/10 Enterprise N RTM  (WGGHN-J84D6-QYCPR-T7PJ7-X766F)" },
            { "B4H4T", "Windows 11/10 Enterprise G N RTM  (FW7NV-4T673-HF4VX-9X4MM-B4H4T)" },
            { "H8Q99", "Windows 10 Home KMS  (TX9XD-98N7V-6WMQ6-BX7FG-H8Q99)" },
            { "WGGBY", "Windows 10 Pro Education HWID  (8PTT6-RNW57-N3YKV-MJNWM-WGGBY)" },
            { "8DEC2", "Windows 10 Enterprise HWID  (XGVPP-NMH47-7TTHJ-W3FW7-8DEC2)" },
            { "VCFB2", "Windows 11 Pro Education HWID  (BW6C2-QMPVW-D7KKK-3GKT6-VCFB2)" },
            { "2YT43", "Windows 11/10 Enterprise KMS/GVLK  (NPPR9-FWDCX-D2C8J-H872K-2YT43)" },
            { "J462D", "Windows 10/11 LTSC KMS/GVLK  (M7XTQ-FN8P6-TTKYV-9D4CC-J462D)" },
            { "7QW8P", "Windows 10 S RTM  (3NF4D-GF9GY-63VKH-QRC3V-7QW8P)" },
            { "DYJWX", "Windows 10 Enterprise S RTM  (NK96Y-D9CD8-W44CQ-R8YTK-DYJWX)" },
            { "V22HG", "Windows 10 Home Default  (46J3N-RY6B3-BJFDY-VBFT9-V22HG)" },
            { "DGPHV", "Windows 10 Home N Default  (PGGM7-N77TC-KVR98-D82KJ-DGPHV)" },
            { "46YP4", "Windows 10 Pro / Pro N Default  (RHGJR-N7FVY-Q3B8F-KBQ6V-46YP4)" },
            { "D3WV4", "Windows 10 SL Default  (GH37Y-TNG7X-PP2TK-CMRMT-D3WV4)" },
            { "9D7YC", "Windows 10 CHN SL Default  (68WP7-N2JMW-B676K-WR24Q-9D7YC)" },
            { "FC2HD", "Windows 10 Home OEM 3.0  (37GNV-YCQVD-38XP9-T848R-FC2HD)" },
            { "VH636", "Windows 10 Home N OEM 3.0  (33CY4-NPKCC-V98JP-42G8W-VH636)" },
            { "WFG6P", "Windows 10 Pro OEM 3.0  (NF6HC-QH89W-F8WYV-WWXV4-WFG6P)" },
            { "TCQG3", "Windows 10 Pro N OEM 3.0  (NH7W7-BMC3R-4W9XT-94B6D-TCQG3)" },
            { "HH64C", "Windows 10 SL OEM 3.0  (NTRHT-XTHTG-GBWCG-4MTMP-HH64C)" },
            { "H6DDY", "Windows 10 CHN SL OEM 3.0  (7B6NC-V3438-TRQG7-8TCCX-H6DDY)" },
        };


        // ── Loaded from settings.ini DEFAULT block ─────────────────────────────
        /// <summary>Last-5-char suffixes from [GvlkKeys] in the default block.</summary>
        public static HashSet<string> DefaultGvlkSuffixes { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Suffix → description from [GvlkKeys] lines in the default block.</summary>
        public static Dictionary<string, string> DefaultGvlkKeyDescriptions { get; private set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        // ── Generic placeholder keys (from [GenericKeys] / [UserGenericKeys]) ──
        /// <summary>Key-suffix → description loaded from [GenericKeys] in the default settings block.</summary>
        public static Dictionary<string, string> DefaultGenericKeyDescriptions { get; private set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Key-suffix → description loaded from [UserGenericKeys].</summary>
        public static Dictionary<string, string> UserGenericKeyDescriptions { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Merged generic key suffix → description: hardcoded + ini default + user additions.</summary>
        public static Dictionary<string, string> AllGenericKeyDescriptions =>
            HardcodedGenericKeys
                .Concat(DefaultGenericKeyDescriptions)
                .Concat(UserGenericKeyDescriptions)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

        /// <summary>All generic key suffixes (last 5 chars). Used for DE activation detection.</summary>
        public static HashSet<string> AllGenericKeySuffixes =>
            new HashSet<string>(AllGenericKeyDescriptions.Keys, StringComparer.OrdinalIgnoreCase);

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

        /// <summary>All GVLK suffix → description: hardcoded + ini defaults. Used in settings display.</summary>
        public static Dictionary<string, string> AllGvlkKeyDescriptions =>
            HardcodedGvlkKeyDescriptions
                .Concat(DefaultGvlkKeyDescriptions)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Combined GVLK + Generic key descriptions for the Settings dialog display.
        /// GVLK entries first (labeled [KMS GVLK]), then HWID/DE placeholder entries ([HWID/DE]).
        /// INI-loaded entries override hardcoded ones; user additions are merged last.
        /// </summary>
        public static Dictionary<string, string> AllKeyDescriptionsForDisplay
        {
            get
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // GVLK keys first
                foreach (var kv in AllGvlkKeyDescriptions)
                    result[kv.Key] = "[KMS/GVLK] " + kv.Value;
                // Generic / HWID-DE keys (may overlap suffixes — generic wins for the display label)
                foreach (var kv in AllGenericKeyDescriptions)
                    result[kv.Key] = "[HWID/DE] " + kv.Value;
                return result;
            }
        }

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
            DefaultGvlkKeyDescriptions.Clear();
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
            DefaultGenericKeyDescriptions.Clear();
            UserGenericKeyDescriptions.Clear();

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
                            if (suffix != null)
                            {
                                DefaultGvlkSuffixes.Add(suffix);
                                var desc = value.Contains('=') ? value.Substring(value.IndexOf('=') + 1).Trim() : suffix;
                                DefaultGvlkKeyDescriptions[suffix] = desc;
                            }
                            break;
                        }
                        case "GENERICKEYS":
                        {
                            var suffix = ExtractKeySuffix(value);
                            var desc   = value.Contains('=') ? value.Substring(value.IndexOf('=') + 1).Trim() : value.Trim();
                            if (suffix != null) DefaultGenericKeyDescriptions[suffix] = desc;
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
                        case "USERGENERICKEYS":
                        {
                            var suffix = ExtractKeySuffix(value);
                            var desc   = value.Contains('=') ? value.Substring(value.IndexOf('=') + 1).Trim() : value.Trim();
                            if (suffix != null) UserGenericKeyDescriptions[suffix] = desc;
                            break;
                        }

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

            w.WriteLine("[UserGenericKeys]");
            w.WriteLine("; Add custom HWID/DE placeholder key suffixes: KEY-SUFFIX = Description");
            foreach (var kv in UserGenericKeyDescriptions)
                w.WriteLine(kv.Key + " = " + kv.Value);
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
