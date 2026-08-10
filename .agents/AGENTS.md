# WinLic Manager — Agent Rules
# Workspace: f:/Coding/winlic
# Scope: project-specific rules applied to all agents working in this repo

---

## Artifact Storage Policy

### Where to save files

| Content type | Save to |
|---|---|
| Long-form documentation, implementation plans, design decisions | `agent/docs/` (project root) |
| Release notes (new or updated) | `agent/docs/` AND `releases/<version>/` |
| Generated images, icons, media | `agent/work/` |
| Generated/scaffolded code snippets not yet in source | `agent/work/` |
| Temporary one-off scripts, debug dumps, intermediate data | Agent brain dir scratch only (`<brainDir>/scratch/`) |
| Final source code | The actual source path (`WinLicApp/`, `WinLicPS/`) |

### Rules

1. **Never use the brain `artifacts/` or brain root for project documentation.** The brain dir is ephemeral — it is not part of the repository and will not persist between conversations. Save any document the project should keep in `agent/docs/` instead.

2. **`agent/docs/` is the canonical home for:**
   - `implementation_plan.md` — the active plan for a feature or fix
   - `task.md` — the task checklist while executing a plan
   - `walkthrough.md` — post-implementation summary
   - All release notes (`RELEASE_NOTES_v*.md`)
   - Any architecture or design notes

3. **`agent/work/` is the canonical home for:**
   - Generated images and icons
   - Scaffold or template files not yet integrated into source
   - Any build artefacts an agent produces that are not release deliverables

4. **Release deliverables** (EXE, PS1, ZIP, SHA256 files) always go in `releases/<version>/` as they already do.

5. **After completing a plan**, update or create `agent/docs/walkthrough.md` to summarise what changed, what was tested, and any known follow-up items.

6. **At the start of a new task**, check `agent/docs/` for an existing `implementation_plan.md` or `walkthrough.md` to understand prior context before beginning research.

---

## Project Conventions

- Version strings must be kept in sync across **three files** every release bump:
  - `WinLicApp/Localization.cs` → `About_Version`
  - `WinLicApp/AboutDialog.xaml.cs` → `CurrentVersion`
  - `WinLicPS/WinLicManager.ps1` → `$SCRIPT_VERSION` + header comment line 2

- Release branches follow the naming `v<MAJOR>.<MINOR>-<STAGE><N>` (e.g. `v1.3-beta2`). Hotfixes increment the stage number, not add `-hotfix` suffixes.

- Every release requires all 8 artifacts:
  `*-<ver>.exe`, `*-<ver>.exe.sha256`, `*-<ver>.zip`, `*-<ver>.zip.sha256`,
  `WinLicManager-<ver>.ps1`, `WinLicManager-<ver>.ps1.sha256`,
  `WinLicPS-<ver>.zip`, `WinLicPS-<ver>.zip.sha256`

- Release notes are always bilingual (🇺🇸 EN + 🇻🇳 VI) and placed in both `releases/<version>/` and copied to `agent/docs/`.

- `releases/` is in `.gitignore` — local only. Tag + branch the source; upload artifacts manually to the GitHub Release page.

- Build command: `dotnet build WinLicApp/WinLicApp.csproj --configuration Release`
- Output: `WinLicApp/bin/Release/net4.8-windows/WinLicApp.exe`

---

## Git Commit Policy

### When to commit
Commit **after each logical feature or fix is complete and verified** — do not let
changes accumulate across multiple features in a single uncommitted state. Specifically:

- After every new option added (GUI handler + CLI function + localization)
- After any UI restructure (new panel, layout change, style update)
- After any settings/AppSettings change
- After a bug fix, even a small one

### What each commit must include
Every commit message MUST cover **all files changed**, grouped by area. Use this structure:

```
<type>(<scope>): <short summary>

=== AREA NAME ===
- Bullet describing each meaningful change
- Be exhaustive: if a file changed, it must appear
```

**Types:** `feat` / `fix` / `refactor` / `chore` / `docs`
**Scope:** feature name (e.g. `option-6`, `tab-ui`, `rearm-panel`)

### Commit message depth requirement
A commit message is **not acceptable** if it omits:
- Any new function, method, or handler added
- Any new localization key group (name the group, e.g. `O6CH_*`, `R4_*`)
- Any new UI panel, XAML structure, or style
- Any CLI multi-language string table addition
- Any new AppSettings property or settings.ini section
- Any behavioral change to an existing option

### Before committing: inspect `git diff --stat HEAD`
Every modified file must be accounted for in the commit message.
If a file appears in the diff but not in the message, the commit is incomplete.

### Use `git commit -F <file>` for long messages
Write the full message to `<brainDir>/scratch/commit_msg.txt` and use:
```
git commit -F "<path-to-file>"
```
Never truncate a commit message due to shell escaping.

### ❌ Never do these without explicit user instruction
- Version bumps (`About_Version`, `CurrentVersion`, `$SCRIPT_VERSION`)
- `git tag` — tagging is the user's decision
- `git push` or `git push --tags` (Note: After EVERY commit, you MUST proactively ask the user if they want you to push the changes!)
- Branch creation or switching (`git checkout -b`, `git switch`)
- Merging or rebasing

---

## PS1 Parse Check (Mandatory)

**Every edit to `WinLicPS/WinLicManager.ps1` MUST follow this sequence before the file is copied to `releases/test/` or any release directory:**

**Step 1 — Verify/apply UTF-8 BOM** (MUST run FIRST — a missing BOM causes PS5 to misread Vietnamese diacritics as apostrophes, producing hundreds of false parse errors)

**Step 2 — PS5 + PS7 parse checks** (only valid after BOM is confirmed present)

Run the dedicated helper script to perform the BOM and syntax checks. Confirm it succeeds before deploying:

```powershell
pwsh -File agent/work/parse_check.ps1
```

### Known PowerShell 5 Parser Gotchas

PowerShell 5's lexer is significantly stricter than PS7. These patterns cause parse failures that are **invisible to the PS7 parser but break PS5 at runtime**:

| Pattern | Problem | Safe Alternative |
|---|---|---|
| `"text with 'apostrophe' inside"` | `'` opens a new single-quoted string token; corrupts parse state for hundreds of lines after | Use double-quotes escaped: `"text with apostrophe inside"` — remove or rephrase embedded quotes |
| `'regex [a-zA-Z0-9] pattern'` | `[...]` in a single-quoted string is parsed as a type literal | Build via variable: `$cls = 'a-zA-Z0-9'; $pat = "[$cls]"` |
| `"regex [a-zA-Z0-9] pattern"` | `[...]` in a double-quoted string also triggers type-literal parsing | Use backtick-escaped brackets: `` "`[$cls`]" `` or build via string parts |
| `@"...multiline..."@` used inline (e.g. `$x = "a" + @"`) | Here-string `@"` must be at the END of its line with nothing after it; `"@` must be at column 0 | Use explicit string concatenation instead |
| `"# \|\| text \|\|"` or `"... \|\| ..."` in double-quoted strings | `||` is parsed as the OR operator | Use single-quoted string for the whole value, or replace `||` with `--` |
| `.ps1` file saved without BOM and containing non-ASCII characters (e.g. Vietnamese diacritics) | PS5 reads files as Windows-1252 by default; UTF-8 multi-byte sequences contain bytes that look like `'` (0x27), causing string-termination mid-token and cascading parse errors | **Always save `WinLicManager.ps1` with UTF-8 BOM.** After any write_to_file or file edit, run the one-liner below to ensure the BOM is present. |

**Rule of thumb:** Avoid single quotes entirely inside double-quoted PS5 strings. Use `"` only for strings that need variable interpolation; use `'` for pure literals that contain NO embedded single quotes.

### ❌ No Romanized Vietnamese — Ever

**Every Vietnamese string in `$Str` MUST use proper diacritics (ă ơ ư đ ấ ề ộ…). Romanized fallbacks like `Khong co thay doi`, `Canh bao`, `Da huy` are NEVER acceptable — not as a "PS5 safety" measure, not as a shortcut, not for any reason.**

Vietnamese diacritics are **fully safe in PS5** as long as the file has a UTF-8 BOM (which is enforced above). The only genuine PS5 string dangers are embedded single-quotes and square-bracket type-literal patterns — NOT diacritics.

Before committing any new `@('EN', 'VI')` entries, run this scan to catch romanized VI strings:

```powershell
pwsh -ExecutionPolicy Bypass -Command {
    $lines = [System.IO.File]::ReadAllLines('F:\Coding\winlic\WinLicPS\WinLicManager.ps1',
        [System.Text.Encoding]::UTF8)
    $diacritic = '[àáảãạăắằẳẵặâấầẩẫậèéẻẽẹêếềểễệìíỉĩịòóỏõọôốồổỗộơớờởỡợùúủũụưứừửữựỳýỷỹỵđ]'
    $inStr = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match "@\('([^']*)',\s*'([^']*)'\)") {
            $vi = $Matches[2]
            if ($vi.Length -gt 5 -and $vi -match '[A-Za-z]{4}' -and $vi -notmatch $diacritic) {
                "L$($i+1) [ROMANIZED?]: $($line.Trim())"
            }
        } elseif ($line -match "@\('([^']*)',\s*$") { $inStr = $true }
        elseif ($inStr) {
            if ($line -match "^\s+'([^']*)'\)") {
                $vi = $Matches[1]
                if ($vi.Length -gt 5 -and $vi -match '[A-Za-z]{4}' -and $vi -notmatch $diacritic) {
                    "L$($i+1) [ROMANIZED?]: $vi"
                }
            }
            $inStr = $false
        }
    }
    "Scan complete."
}
```

Known false positives from this scan (intentional English technical terms in VI strings):
- `'Key OEM BIOS:'` — "Key", "OEM", "BIOS" are correct technical terms in Vietnamese
- `'LastWriteTime kho SPP:'` — "LastWriteTime" is a Windows API term kept in English



### UTF-8 BOM Requirement (Mandatory for this script)

`WinLicManager.ps1` contains Vietnamese diacritics in the `$Str` string table. PS5 (`powershell.exe`) reads `.ps1` files as Windows-1252 **unless** the file begins with a UTF-8 BOM (`0xEF 0xBB 0xBF`).

**After every file write, run Step 1 (BOM check) BEFORE Step 2 (parse checks).** See the "PS1 Parse Check" section above — the BOM check command is Step 1 of that sequence.

> **Why this matters:** UTF-8 multi-byte sequences for characters like `ọ` (U+1ECD = bytes `0xE1 0xBB 0x8D`) contain `0x27` (`'`) as one of their bytes. When PS5 reads the file as Latin-1, it sees a literal apostrophe mid-token and terminates string literals early, producing hundreds of cascading parse errors — none of which point to the real cause. Running parse checks without a BOM in place yields **false failures** and wastes debugging time.
