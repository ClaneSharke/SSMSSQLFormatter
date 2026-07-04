# SSMS SQL Formatter (Rules + AI)

A VSIX extension for **SQL Server Management Studio 21 / 22** that formats T-SQL in the query window.

Two engines:

| Engine | How it works | Pros | Cons |
|---|---|---|---|
| **Rule-based** | Microsoft `ScriptDom` parser + script generator (fully offline) | Instant, deterministic, understands all T-SQL | Regenerating from the parse tree can drop/move comments |
| **AI** | Anthropic Claude API with **your own API key** | Preserves comments, follows free-form style instructions ("leading commas", "align equals signs") | Needs network + API key; script text is sent to the API |

Two built-in style presets — **Classic** (old-format compact: trailing commas, inline JOINs, fewer line breaks) and **Modern** (each column/JOIN/predicate on its own line, aligned bodies) — plus a **Custom** preset where every individual option applies.

## Important caveat

Microsoft **does not officially support extensions in SSMS 21/22**. They are tolerated and widely used (SSMSBoost, community formatters, etc.), but if SSMS misbehaves with an extension installed, Microsoft won't help. Use at your own risk, and keep the VSIX handy so you can uninstall via the VSIX installer if needed.

## Prerequisites (build machine)

- Windows with **Visual Studio 2022** (Community is fine)
- Workload: **Visual Studio extension development** (installs the VS SDK)
- .NET Framework 4.8 targeting pack (included with the workload)
- SSMS 21 or 22 installed (to install/test the result)

## Build — no Visual Studio needed (GitHub Actions)

Don't want to install VS? Push this folder to a GitHub repo (public or private). The included
workflow (`.github/workflows/build.yml`) compiles it on GitHub's free Windows runners:

1. Create a repo and push this project (`git init`, `git add .`, `git commit`, `git push`).
2. On GitHub, open the **Actions** tab → the "Build VSIX" run → download the
   **SsmsSqlFormatter-vsix** artifact. That zip contains the ready-to-install `SsmsSqlFormatter.vsix`.
3. Tag a version (`git tag v1.0.0 && git push --tags`) to get the VSIX attached to a GitHub Release
   — a permanent free download link you can share.

## Build — locally

Run `build.cmd` from the project root (needs VS 2022 with the extension development workload), or manually:

1. Open `SsmsSqlFormatter.sln` in Visual Studio 2022.
2. Restore NuGet packages (right-click solution → *Restore NuGet Packages*). If the pinned `Microsoft.SqlServer.TransactSql.ScriptDom` version isn't found, update it to the latest 161.x via NuGet Package Manager.
3. Switch to **Release** and Build. The output is:
   `src\SsmsSqlFormatter\bin\Release\SsmsSqlFormatter.vsix`

## Install into SSMS

SSMS doesn't (yet) have an Extensions marketplace, so install manually:

**Option A — double-click** the `.vsix`. On SSMS 21+, the VSIX installer usually detects SSMS as a target ("SQL Server Management Studio 21"). Tick it and install.

**Option B — command line** (if the installer doesn't list SSMS):

```
"C:\Program Files\Microsoft SQL Server Management Studio 21\Release\Common7\IDE\VSIXInstaller.exe" SsmsSqlFormatter.vsix
```

(Adjust the path for your SSMS install; SSMS 22 lives under `...Management Studio 22\...`.)

Restart SSMS after installing.

**Uninstall:** `VSIXInstaller.exe /uninstall:SsmsSqlFormatter.7f3d2a1e-5b8c-4e9f-a6d0-1c4b7e2f9a35`

## Use

- Open a query window, press **Ctrl+Shift+Alt+F**, or
- Right-click in the editor → **Format T-SQL Script**, or
- **Tools → Format T-SQL Script**

With text selected, only the selection is formatted; otherwise the whole document. The change is one undo unit — **Ctrl+Z** reverts it.

## Configure

**Tools → Options → SQL Formatter**

- **General**: engine (RuleBased / Ai), style preset (Classic / Modern / Custom), keyword casing, indent size, semicolons, per-clause line breaks, multiline lists, alignment, comment warning.
- **AI Engine**: Anthropic API key, model, max tokens, timeout, custom style instructions, "use General options as style guide", fallback to rule-based on error, confirm-before-send.

### AI engine notes

- Get a key at https://console.anthropic.com — API usage is billed to that key separately from any Claude.ai subscription.
- The key is stored in the SSMS settings registry hive in **plain text**. Don't configure it on shared machines. (Hardening idea: swap the ApiKey property for a DPAPI/`ProtectedData` wrapper or Windows Credential Manager.)
- Your script text — including any embedded literals/data — is sent to the API. The confirm-before-send prompt is on by default for that reason.
- For very large scripts, raise **Max output tokens** and **Timeout**.

## Debugging

F5 launches the **Visual Studio experimental instance** (the manifest also targets VS 2022 so the extension loads there). To debug inside SSMS itself, change `StartProgram` in the `.csproj` to your `Ssms.exe` path, or attach the debugger to a running `Ssms.exe` process.

## Known limitations

- Rule-based engine: regenerates the script from the AST, so **comments can be dropped** (you get a warning when comments are detected — or use the AI engine, which preserves them).
- Rule-based engine leaves the script untouched if it doesn't parse (you'll get the parse error with line/column — arguably a feature).
- SQLCMD-mode scripts (`:setvar`, `:connect`) won't parse with ScriptDom; use the AI engine for those.
- After a new major SSMS release, the manifest's `InstallationTarget` upper bound (`[21.0,23.0)`) may need bumping.

## Project layout

```
src/SsmsSqlFormatter/
  SsmsSqlFormatterPackage.cs   – AsyncPackage, registers command + options pages
  FormatSqlCommand.cs          – command handler (selection/document, undo unit, status bar)
  SsmsSqlFormatter.vsct        – menus + Ctrl+Shift+Alt+F keybinding
  source.extension.vsixmanifest – targets Microsoft.VisualStudio.Ssms [21.0,23.0)
  Formatting/
    ScriptDomFormatter.cs      – rule-based engine + Classic/Modern/Custom presets
    AiFormatter.cs             – Anthropic Messages API client
  Options/
    GeneralOptions.cs          – Tools>Options "General" page
    AiOptions.cs               – Tools>Options "AI Engine" page
```
