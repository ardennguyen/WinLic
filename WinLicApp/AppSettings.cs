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
using System.Text.RegularExpressions;
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

        // Hardcoded GVLK key suffix → description for display (mirrors HardcodedGvlkSuffixes).
        public static readonly Dictionary<string, string> HardcodedGvlkKeyDescriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "TX9XD-98N7V-6WMQ6-BX7FG-H8Q99", "Windows 10/11 Home" },
            { "W269N-WFGWX-YVC9B-4J6C9-T83GX", "Windows 10/11 Pro" },
            { "MH37W-N47XK-V7XM9-C7227-GCQG9", "Windows 10/11 Pro N" },
            { "NRG8B-VKK3Q-CXVCJ-9G2XF-6Q84J", "Windows 10/11 Pro for Workstations" },
            { "9FNHH-K3HBT-3W4TD-6383H-6XYWF", "Windows 10/11 Pro N for Workstations" },
            { "6TP4R-GNPTD-KYYHQ-7B7DP-J447Y", "Windows 10/11 Pro Education" },
            { "YVWGF-BXNMC-HTQYQ-CPQ99-66QFC", "Windows 10/11 Pro Education N" },
            { "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2", "Windows 10/11 Education" },
            { "2WH4N-8QGBV-H22JP-CT43Q-MDWWJ", "Windows 10/11 Education N" },
            { "NPPR9-FWDCX-D2C8J-H872K-2YT43", "Windows 10/11 Enterprise" },
            { "DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4", "Windows 10/11 Enterprise N" },
            { "YYVX9-NTFWV-6MDM3-9PT4T-4M68B", "Windows 10/11 Enterprise G" },
            { "4K36P-JN4VD-GDC6V-KDT89-DYFKP", "Windows 10/11 Enterprise G N" },
            { "M7XTQ-FN8P6-TTKYV-9D4CC-J462D", "Windows 10/11 Enterprise LTSC 2021/2024" },
            { "92NFX-8DJQP-P6BBQ-THF9C-7CG2H", "Windows 10/11 Enterprise N LTSC 2021/2024" },
            { "MHF9N-XY6XB-WVXMC-BTDCT-MKKG7", "Windows 10 Enterprise LTSC 2019" },
            { "VR9PX-W3VD3-BPPRH-8DCR8-WX77J", "Windows 10 Enterprise N LTSC 2019" },
            { "DCPHK-NFMTC-H88MJ-PFHPY-QJ4BJ", "Windows 10 Enterprise LTSB 2016" },
            { "QFFDN-GRT3P-VKWWX-X7T3R-8B639", "Windows 10 Enterprise N LTSB 2016" },
            { "WNMTR-4C88C-JK8YV-HQ7T2-76DF9", "Windows 10 Enterprise LTSB 2015" },
            { "2F77B-TNFGY-69QQF-B8YKP-D69TJ", "Windows 10 Enterprise N LTSB 2015" },
            { "7HNRX-D7KGG-3K4RQ-4WPJ4-YTDFH", "Windows 10/11 Home Single Language" },
            { "PVMJN-6DFY6-9CCP6-7BKTT-D3WVR", "Windows 10/11 Home Country Specific" },
            { "3NF4D-GF9GY-63VKH-QRC3V-7QW8P", "Windows 10/11 S" },
            { "XGVPP-NMH47-7TTHJ-W3FW7-8HV2C", "Windows Server 2016 Datacenter" },
            { "CB7KF-BWN84-R7R2Y-793K2-8XDDG", "Windows Server 2016 Standard" },
            { "WC2BQ-8NRM3-FDDYY-2BFGV-KHKQY", "Windows Server 2016 Essentials" },
            { "JCKRF-N37P4-C2D82-9YXRT-4M63B", "Windows Server 2016 Cloud Storage" },
            { "WMDGN-G9PQG-XVVXX-R3X43-63DFG", "Windows Server 2019 Datacenter" },
            { "N69G4-B89J2-4G8F4-WWYCC-J464C", "Windows Server 2019 Standard" },
            { "WVDHN-86M7X-466P6-VHXV7-YY726", "Windows Server 2019 Essentials" },
            { "WX4NM-KYWYW-QJJR4-XV3QB-6VM33", "Windows Server 2022 Datacenter" },
            { "VDYBN-27WPP-V4HQT-9VMD4-VMK7H", "Windows Server 2022 Standard" },
            { "KNC87-3J2TX-XB4WP-VCPJV-M4FWM", "Windows Server 2022 Datacenter Azure Edition" },
            { "2NXVK-3TMDQ-X7Q28-F848H-F6YVM", "Windows Server 2025 Datacenter" },
            { "TVRH6-WHNXV-R9WG3-9XRFY-MY832", "Windows Server 2025 Standard" },
            { "74YFP-3QFB3-KQT8W-PMXWJ-7M648", "Windows Server 2025 Datacenter Azure Edition / Windows Server 2008 R2 Datacenter" },
            { "489J6-VHDMP-X63PK-3K798-CPX3Y", "Windows Server 2012 R2 Server Standard / Windows Server 2008 R2 Enterprise" },
            { "W3GGN-FT8W3-Y4M27-J84CP-Q3VJ9", "Windows Server 2012 R2 Datacenter" },
            { "22XQ2-VRXRG-P8D42-K34TD-G3QQC", "Windows Server 2012 R2 Essentials / Windows Server 2008 Datacenter without Hyper-V" },
            { "XC9B7-NBPP2-83J2H-RHMBY-92BT4", "Windows Server 2012" },
            { "48HP8-DN98B-MYWDG-T2DCC-8W83P", "Windows Server 2012 Datacenter" },
            { "HM7DN-YVMH3-46JC3-XYTG7-CYQJJ", "Windows Server 2012 Standard" },
            { "33PXH-7Y6KF-2VJC9-XBBR8-HVTHH", "Windows Server 2012 MultiPoint Standard" },
            { "YFKBB-PQJJV-G996G-VWGXY-2V3X8", "Windows Server 2012 MultiPoint Premium" },
            { "XNH6W-2V9GX-RGJ4K-Y8X6F-QGJ2G", "Windows Server 2008 R2 Web" },
            { "7M67G-PC374-GR742-YH8V4-TCBY3", "Windows Server 2008 R2 HPC edition / Windows Server 2008 HPC" },
            { "YC6KT-GKW9T-YTKYR-T4X34-R7VHC", "Windows Server 2008 R2 Standard" },
            { "GT63C-RJFQ3-4GMB6-BRFB9-CB83V", "Windows Server 2008 R2 for Itanium-based Systems" },
            { "WYR28-R7TFJ-3X2YQ-YCY4H-M249D", "Windows Server 2008 Web" },
            { "TM24T-X9RMF-VWXK6-X8JC9-BFGM2", "Windows Server 2008 Standard" },
            { "YQGMW-MPWTJ-34KDK-48M3W-X4Q6V", "Windows Server 2008 Standard without Hyper-V" },
            { "39BXF-X8Q23-P2WWT-38T2F-G3FPG", "Windows Server 2008 Enterprise" },
            { "RCTX3-KWVHP-BR6TB-RB6DM-6X7HP", "Windows Server 2008 Enterprise without Hyper-V" },
            { "4DWFP-JF3DJ-B7DTH-78FJB-PDRHK", "Windows Server 2008 Datacenter" },
            { "4DDJ-2MBR3-82QGD-K2G6K-Q4RD8", "Windows Server 2008 for Itanium-Based Systems" },
            { "BN3D2-R7TKB-3YPBD-8DRP2-27GG4", "Windows 10/11 IoT Enterprise" },
        };

        // Hardcoded HWID/DE generic placeholder key fallback.
        // Maps last-5-char suffix → human-readable description.
        // Ref: https://learn.microsoft.com/en-us/windows-server/get-started/kms-client-activation-keys
        public static readonly Dictionary<string, string> HardcodedGenericKeys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "YTMG3-N6DKC-DKB77-7M9GH-8HVX7", "Windows 11/10 Home RTM" },
            { "4CPRK-NM3K3-X6XXQ-RXX86-WXCHW", "Windows 11/10 Home N RTM" },
            { "BT79Q-G7N6G-PGBYW-4YWX6-6F4BT", "Windows 11/10 Home Single Language RTM" },
            { "N2434-X9D7W-8PF6X-8DV9T-8TYMD", "Windows 11 Home Country Specific RTM" },
            { "VK7JG-NPHTM-C97JM-9MPGT-3V66T", "Windows 11/10 Pro RTM" },
            { "2B87N-8KFHP-DKV6R-Y2C8J-PKCKT", "Windows 11/10 Pro N RTM" },
            { "DXG7C-N36C4-C4HTG-X4T3X-2YV77", "Windows 11/10 Pro for Workstations RTM" },
            { "WYPNQ-8C467-V2W6J-TX4WX-WT2RQ", "Windows 11/10 Pro N for Workstations RTM" },
            { "8PTT6-RNW4C-6V7J2-C2D3X-MHBPB", "Windows 11/10 Pro Education RTM" },
            { "GJTYN-HDMQY-FRR76-HVGC7-QPF8P", "Windows 11/10 Pro Education N RTM" },
            { "YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY", "Windows 11/10 Education RTM" },
            { "84NGF-MHBT6-FXBX8-QWJK7-DRR8H", "Windows 11/10 Education N RTM" },
            { "XGVPP-NMH47-7TTHJ-W3FW7-8HV2C", "Windows 11/10 Enterprise RTM" },
            { "WGGHN-J84D6-QYCPR-T7PJ7-X766F", "Windows 11/10 Enterprise N RTM" },
            { "FW7NV-4T673-HF4VX-9X4MM-B4H4T", "Windows 11/10 Enterprise G N RTM" },
            { "TX9XD-98N7V-6WMQ6-BX7FG-H8Q99", "Windows 10/11 Home HWID" },
            { "3KHY7-WNT83-DGQKR-F7HPR-844BM", "Windows 10/11 Home N HWID" },
            { "7HNRX-D7KGG-3K4RQ-4WPJ4-YTDFH", "Windows 10/11 Home Single Language HWID" },
            { "PVMJN-6DFY6-9CCP6-7BKTT-D3WVR", "Windows 11 Home Country Specific HWID" },
            { "8PTT6-RNW57-N3YKV-MJNWM-WGGBY", "Windows 10 Pro Education HWID" },
            { "XGVPP-NMH47-7TTHJ-W3FW7-8DEC2", "Windows 10 Enterprise HWID" },
            { "BW6C2-QMPVW-D7KKK-3GKT6-VCFB2", "Windows 11 Pro Education HWID" },
            { "3NF4D-GF9GY-63VKH-QRC3V-7QW8P", "Windows 10 S RTM" },
            { "NK96Y-D9CD8-W44CQ-R8YTK-DYJWX", "Windows 10 Enterprise S RTM" },
            { "46J3N-RY6B3-BJFDY-VBFT9-V22HG", "Windows 10 Home Default" },
            { "PGGM7-N77TC-KVR98-D82KJ-DGPHV", "Windows 10 Home N Default" },
            { "RHGJR-N7FVY-Q3B8F-KBQ6V-46YP4", "Windows 10 Pro / Pro N Default" },
            { "GH37Y-TNG7X-PP2TK-CMRMT-D3WV4", "Windows 10 SL Default" },
            { "68WP7-N2JMW-B676K-WR24Q-9D7YC", "Windows 10 CHN SL Default" },
            { "37GNV-YCQVD-38XP9-T848R-FC2HD", "Windows 10 Home OEM 3.0" },
            { "33CY4-NPKCC-V98JP-42G8W-VH636", "Windows 10 Home N OEM 3.0" },
            { "NF6HC-QH89W-F8WYV-WWXV4-WFG6P", "Windows 10 Pro OEM 3.0" },
            { "NH7W7-BMC3R-4W9XT-94B6D-TCQG3", "Windows 10 Pro N OEM 3.0" },
            { "NTRHT-XTHTG-GBWCG-4MTMP-HH64C", "Windows 10 SL OEM 3.0" },
            { "7B6NC-V3438-TRQG7-8TCCX-H6DDY", "Windows 10 CHN SL OEM 3.0" },
            { "3V6Q6-NQXCX-V8YXR-9QCYV-QPFCT", "Windows 10/11 Enterprise N HWID" },
            { "XKCNC-J26Q9-KFHD2-FKTHY-KD72Y", "Windows 10/11 PPI Pro HWID" },
            { "PJB47-8PN2T-MCGDY-JTY3D-CBCPV", "Windows 11 Enterprise LTSC 2024 HWID" },
            { "KCNVH-YKWX8-GJJB9-H9FDT-6F7W2", "Windows 10 Enterprise LTSC 2021 HWID" },
            { "43TBQ-NH92J-XKTM7-KT3KK-P39PB", "Windows 10 Enterprise LTSC 2019 HWID" },
            { "FWN7H-PF93Q-4GGP8-M8RF3-MDWWW", "Windows 10 Enterprise LTSB 2015 HWID" },
            { "2DBW3-N2PJG-MVHW3-G7TDK-9HKR4", "Windows 10 Enterprise N LTSB 2016 HWID" },
            { "NTX6B-BRYC2-K6786-F6MVQ-M7V2X", "Windows 10 Enterprise N LTSB 2015 HWID" },
            { "3XP6D-CRND4-DRYM2-GM84D-4GG8Y", "Windows 10/11 Professional Country Specific" },
            { "NJCF7-PW8QT-3324D-688JX-2YV66", "Windows 10/11 Server Rdsh HWID" },
            { "V3WVW-N2PV2-CGWC3-34QGF-VMJ2C", "Windows 10/11 Cloud HWID" },
            { "NH9J3-68WK7-6FB93-4K3DF-DJ4F6", "Windows 10/11 Cloud N HWID" },
            { "XQQYW-NFFMW-XJPBH-K8732-CKFFD", "Windows 10/11 IoT Enterprise HWID" },
            { "QPM6N-7J2WJ-P88HH-P3YRH-YY74H", "Windows 10 IoT Enterprise LTSC 2021 HWID" },
            { "CGK42-GYN6Y-VD22B-BX98W-J8JXD", "Windows 11 IoT Enterprise LTSC 2024 HWID" },
            { "K9VKN-3BGWV-Y624W-MCRMQ-BHDCD", "Windows 10/11 Cloud Edition N HWID" },
            { "KY7PN-VR6RX-83W6Y-6DDYQ-T6R4W", "Windows 10/11 Cloud Edition HWID" },
            { "N979K-XWD77-YW3GB-HBGH6-D32MH", "Windows 10/11 IoT Enterprise SK HWID" },
            { "P8Q7T-WNK7X-PMFXY-VXHBG-RRK69", "Windows 10/11 IoT Enterprise K HWID" },
            { "TMP2N-KGFHJ-PWM6F-68KCQ-3PJBP", "Windows 10/11 WNC HWID" },
        };


        // ── Loaded from settings.ini DEFAULT block ─────────────────────────────
        /// <summary>Last-5-char suffixes from [GvlkKeys] in the default block.</summary>

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
            new HashSet<string>(AllGenericKeyDescriptions.Keys.Select(k => k.Length >= 5 ? k.Substring(k.Length - 5) : k), StringComparer.OrdinalIgnoreCase);

        // ── Full keys for advisory notice matching ──
        public static HashSet<string> FullGvlkKeys { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> FullGenericKeys { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


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
                AllGvlkKeyDescriptions.Keys.Select(k => k.Length >= 5 ? k.Substring(k.Length - 5) : k).Concat(UserGvlkSuffixes),
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

        /// <summary>
        /// Extract the full 25-character product key string.
        /// </summary>
        private static string? ExtractFullKey(string keyLine)
        {
            var key = keyLine.Contains('=') ? keyLine.Substring(0, keyLine.IndexOf('=')).Trim() : keyLine.Trim();
            if (key.Length == 29 && key.Contains("-")) return key.ToUpperInvariant();
            return null;
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
            FullGvlkKeys.Clear();
            FullGenericKeys.Clear();

            var pkRegex = new System.Text.RegularExpressions.Regex(@"\b[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var desc in HardcodedGenericKeys.Values)
            {
                var match = pkRegex.Match(desc);
                if (match.Success) FullGenericKeys.Add(match.Value.ToUpperInvariant());
            }
            foreach (var k in HardcodedGenericKeys.Keys)
            {
                if (k.Length >= 25 && k.Contains("-")) FullGenericKeys.Add(k.ToUpperInvariant());
            }
            foreach (var k in HardcodedGenericKeys.Keys)
            {
                if (k.Length >= 25 && k.Contains("-")) FullGenericKeys.Add(k.ToUpperInvariant());
            }

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
                            var fullKey = ExtractFullKey(value);
                            if (suffix != null)
                            {
                                var desc = value.Contains('=') ? value.Substring(value.IndexOf('=') + 1).Trim() : (fullKey ?? suffix);
                                DefaultGvlkKeyDescriptions[fullKey ?? suffix] = desc;
                            }
                            break;
                        }
                        case "GENERICKEYS":
                        {
                            var suffix = ExtractKeySuffix(value);
                            var fullKey = ExtractFullKey(value);
                            if (fullKey != null) FullGenericKeys.Add(fullKey);
                            var desc   = value.Contains('=') ? value.Substring(value.IndexOf('=') + 1).Trim() : value.Trim();
                            if (fullKey != null || suffix != null) DefaultGenericKeyDescriptions[fullKey ?? suffix] = desc;
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
                            var fullKey = ExtractFullKey(value);
                            if (suffix != null) UserGvlkSuffixes.Add(suffix);
                            break;
                        }
                        case "USERKMSPIRACYDOMAINS":
                            ExtraKmsPiracyDomains.Add(value); break;
                        case "USERGENERICKEYS":
                        {
                            var suffix = ExtractKeySuffix(value);
                            var fullKey = ExtractFullKey(value);
                            if (fullKey != null) FullGenericKeys.Add(fullKey);
                            var desc   = value.Contains('=') ? value.Substring(value.IndexOf('=') + 1).Trim() : value.Trim();
                            if (fullKey != null || suffix != null) UserGenericKeyDescriptions[fullKey ?? suffix] = desc;
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
                    WriteUserBlock(w, includeHeader: true);
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
                using var writer = new StreamWriter(ms, new System.Text.UTF8Encoding(false), 4096, true);
                WriteUserBlock(writer); // generate WITHOUT header
                writer.Flush();
                ms.Position = 0;
                var newUserBlock = new StreamReader(ms).ReadToEnd();

                if (markerLine >= 0)
                {
                    // The old header had a bottom border on the line after the marker.
                    // To preserve the existing header perfectly, we take markerLine + 2 lines.
                    // This includes the top border (if any), the middle marker line, and the bottom border.
                    var linesToTake = markerLine + 2 <= lines.Count ? markerLine + 2 : lines.Count;
                    var defaultPart = string.Join(Environment.NewLine, lines.Take(linesToTake));
                    File.WriteAllText(SettingsPath,
                        defaultPart + Environment.NewLine + Environment.NewLine + newUserBlock,
                        new System.Text.UTF8Encoding(false));
                }
                else
                {
                    // No marker found — append user block WITH header
                    var header = string.Join(Environment.NewLine, new[]
                    {
                        "# ╔═══════════════════════════════════════════════════════════════════════════╗",
                        "# ║  USER BLOCK  --  Edit freely. NEVER overwritten by \"Update defaults\".     ║",
                        "# ╚═══════════════════════════════════════════════════════════════════════════╝",
                        ""
                    });
                    File.AppendAllText(SettingsPath, Environment.NewLine + header + newUserBlock);
                }
            }
            catch { /* ignore write failures */ }
        }

        private static void WriteUserBlock(StreamWriter w, bool includeHeader = false)
        {
            w.WriteLine();
            if (includeHeader)
            {
                w.WriteLine("# ╔═══════════════════════════════════════════════════════════════════════════╗");
                w.WriteLine("# ║  USER BLOCK  --  Edit freely. NEVER overwritten by \"Update defaults\".     ║");
                w.WriteLine("# ╚═══════════════════════════════════════════════════════════════════════════╝");
                w.WriteLine();
            }
            
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [UserGvlkKeys]");
            w.WriteLine("# Add custom GVLK or suspicious keys here. Same format as [GvlkKeys]:");
            w.WriteLine("#   FULL-KEY = Description");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[UserGvlkKeys]");
            w.WriteLine("; MY-CUSTOM-XXXXX-XXXXX-XXXXX = Custom suspicious key");
            // Also write out any dynamically tracked FULL keys if we have them. (The app only tracks suffixes though)
            foreach (var s in UserGvlkSuffixes) w.WriteLine("; (stored suffix) " + s);

            w.WriteLine();
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [UserGenericKeys]");
            w.WriteLine("# Add custom HWID/DE placeholder keys here.");
            w.WriteLine("# Same format as [GenericKeys]:  FULL-KEY = Description");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[UserGenericKeys]");
            w.WriteLine("; YYYYY-YYYYY-YYYYY-YYYYY-XXXXX = My additional placeholder key");
            // For generic keys, we have full descriptions
            foreach (var kv in UserGenericKeyDescriptions) w.WriteLine(kv.Key + " = " + kv.Value);

            w.WriteLine();
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [UserKmsPiracyDomains]");
            w.WriteLine("# Add your own known piracy KMS hostnames here (one keyword per line).");
            w.WriteLine("# The built-in [KmsPiracyDomains] above is also always active.");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[UserKmsPiracyDomains]");
            w.WriteLine("; my.custom.piracy.kms.example.com");
            foreach (var s in ExtraKmsPiracyDomains) w.WriteLine(s);

            w.WriteLine();
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [ExtraPorts]");
            w.WriteLine("# Additional TCP ports to probe on localhost for KMS listeners.");
            w.WriteLine("# Built-in default: 1688 (standard Microsoft KMS port).");
            w.WriteLine("# Some vlmcsd instances use non-standard ports configured with -P flag.");
            w.WriteLine("# Valid range: 1-65535 (non-integer lines are silently ignored)");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[ExtraPorts]");
            w.WriteLine("; 1689");
            w.WriteLine("; 8080");
            foreach (var p in ExtraPorts) w.WriteLine(p);

            w.WriteLine();
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [ExtraServices]");
            w.WriteLine("# Additional Windows service name keywords to flag as suspicious.");
            w.WriteLine("# Matching is case-insensitive wildcard (*keyword*).");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[ExtraServices]");
            w.WriteLine("; MyKmsService");
            w.WriteLine("; CustomActivator");
            foreach (var s in ExtraServices) w.WriteLine(s);

            w.WriteLine();
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [ExtraTaskKeywords]");
            w.WriteLine("# Additional scheduled task name keywords to flag as suspicious.");
            w.WriteLine("# Matching is case-insensitive (*keyword* substring).");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[ExtraTaskKeywords]");
            w.WriteLine("; AutoActivate");
            w.WriteLine("; LicenseRenew");
            foreach (var s in ExtraTaskKeywords) w.WriteLine(s);

            w.WriteLine();
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [ExtraProcesses]");
            w.WriteLine("# Additional process name keywords to flag as suspicious.");
            w.WriteLine("# Matching is case-insensitive (*keyword* substring). .exe extension optional.");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[ExtraProcesses]");
            w.WriteLine("; mykms.exe");
            w.WriteLine("; kms_server");
            foreach (var s in ExtraProcesses) w.WriteLine(s);

            w.WriteLine();
            w.WriteLine();
            w.WriteLine("# =============================================================================");
            w.WriteLine("# [ExtraFilePaths]");
            w.WriteLine("# Additional absolute file or folder paths to check for activation tool traces.");
            w.WriteLine("# Provide FULL absolute paths. Environment variables are NOT expanded here.");
            w.WriteLine("# =============================================================================");
            w.WriteLine("[ExtraFilePaths]");
            w.WriteLine("; C:\\Tools\\KMSTool");
            w.WriteLine("; C:\\ProgramData\\CustomKMS\\server.exe");
            foreach (var s in ExtraFilePaths) w.WriteLine(s);
        }

        // ── Auto-update ───────────────────────────────────────────────────────
        /// <summary>
        /// Downloads the latest default settings block from GitHub and replaces
        /// the DEFAULT block in settings.ini while preserving the USER block.
        /// </summary>
        /// <returns>Branch name on success, null on network/IO error.</returns>
        public static async Task<string> UpdateDefaultsAsync()
        {
            try
            {
                var branchMatch = Regex.Match(AboutDialog.AppVersion, @"^v\d+\.\d+");
                var branch = branchMatch.Success ? branchMatch.Value : "main";
                var defaultsUrl = $"https://raw.githubusercontent.com/ardennguyen/WinLic/{branch}/WinLicPS/settings.default.ini";

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.Add("User-Agent", "WinLicApp/1.0");
                var downloaded = await http.GetStringAsync(defaultsUrl);

                string userBlock = "";
                if (File.Exists(SettingsPath))
                {
                    var existing = File.ReadAllLines(SettingsPath);
                    int markerLine = Array.FindIndex(existing, l => l.Contains(UserBlockMarker));
                    if (markerLine >= 0)
                    {
                        // Take from the top border of the user block
                        int startIndex = markerLine > 0 ? markerLine - 1 : markerLine;
                        userBlock = string.Join(Environment.NewLine, existing.Skip(startIndex));
                    }
                }

                // Build the timestamp comment
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");
                // Inject timestamp into downloaded content
                var updatedDefault = downloaded.Replace(
                    "Last-Updated:",
                    $"Last-Updated: {timestamp}  ;");

                string finalUserBlock = string.IsNullOrWhiteSpace(userBlock) 
                    ? GetDefaultUserBlock() 
                    : userBlock;

                var combined = updatedDefault.TrimEnd()
                    + Environment.NewLine + Environment.NewLine
                    + finalUserBlock;

                File.WriteAllText(SettingsPath, combined, new System.Text.UTF8Encoding(false));
                Load(); // Reload after update
                return branch;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDefaultUserBlock()
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new StreamWriter(ms, new System.Text.UTF8Encoding(false), 4096, true);
            WriteUserBlock(writer, includeHeader: true);
            writer.Flush();
            ms.Position = 0;
            return new StreamReader(ms).ReadToEnd();
        }

        public static string SettingsFilePath => SettingsPath;
    }
}
