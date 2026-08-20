# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A VSIX extension for SQL Server Management Studio (SSMS) 21/22 that formats T-SQL in the query
window, plus a standalone CLI (`ssmssqlfmt`) that reuses the same core formatting engine for CI
gating. Windows-only, .NET Framework 4.8, C#.

Two formatting engines, selectable in Tools → Options → Format T-SQL Script:
- **Rule-based** (`Formatting/ScriptDomFormatter.cs`) — Microsoft `ScriptDom` parser + script
  generator, fully offline/deterministic. Used everywhere that must never call the network:
  format-on-save, format-on-paste, the CLI, batch file formatting.
- **AI** (`Formatting/AiFormatter.cs`) — calls the Anthropic Messages API or a Copilot/OpenAI-compatible
  chat endpoint using the user's own key/token (stored via `Options/CredentialVault.cs` — Windows
  Credential Manager, DPAPI — never plain text). Only ever invoked from the interactive "Format
  T-SQL Script" command, never from save/paste/batch paths.

## Build

Requires Windows + Visual Studio 2022 with the "Visual Studio extension development" workload
(installs the VS SDK and .NET Framework 4.8 targeting pack).

```
build.cmd                 # one-shot local build (finds MSBuild via vswhere), Release config
                           # output: src\SsmsSqlFormatter\bin\Release\SsmsSqlFormatter.vsix
```

or manually:
```
msbuild SsmsSqlFormatter.sln /t:Restore /p:Configuration=Release
msbuild SsmsSqlFormatter.sln /p:Configuration=Release /p:DeployExtension=false /m
```

`build.ps1` does restore + build + run the NUnit test DLL via `vstest.console.exe` in one step
(locates MSBuild/vstest through `vswhere` the same way).

No VS install needed for CI: GitHub Actions (`.github/workflows/build.yml`, `ci.yml`) build on
`windows-latest`/`windows-2022` runners using `nuget restore` + `msbuild`.

F5 in Visual Studio launches the VS experimental instance for debugging (manifest also targets
VS 2022). To debug inside SSMS itself, change `StartProgram` in `SsmsSqlFormatter.csproj` to the
SSMS `Ssms.exe` path, or attach to a running `Ssms.exe` process.

## Tests

NUnit tests live in `src/SsmsSqlFormatter.Tests/` (project references `SsmsSqlFormatter.csproj`
directly — no VS SDK dependency needed for most of them since the formatter core targets
`IFormatterOptions`, not the VS-specific `GeneralOptions`).

```
msbuild SsmsSqlFormatter.sln /p:Configuration=Debug   # build first
vstest.console.exe src\SsmsSqlFormatter.Tests\bin\Debug\SsmsSqlFormatter.Tests.dll

# run a single test / fixture:
vstest.console.exe src\SsmsSqlFormatter.Tests\bin\Debug\SsmsSqlFormatter.Tests.dll /Tests:ScriptDomFormatterTests
```

CI (`.github/workflows/ci.yml`) currently only builds — it does not run the test suite as a
separate step, so treat `build.ps1`'s `vstest` invocation as the reference way to run tests
locally.

## Architecture

### Settings live behind an interface, not a concrete VS type

`Options/IFormatterOptions.cs` is the contract the rule-based formatter core actually reads
(keyword casing, indentation, comma placement, per-clause line-break/alignment toggles, etc.).
Two implementations:
- `Options/GeneralOptions.cs` — a `DialogPage` subclass, backs the real Tools → Options UI inside
  SSMS/VS. Has a VS SDK dependency.
- `Options/FormatterSettings.cs` — a dependency-free POCO with identical defaults, used by the CLI
  and directly instantiable in tests without any VS SDK / SSMS install.

When changing a formatting option, it almost always needs a matching property added to **both**
`IFormatterOptions` and `FormatterSettings` (and usually `GeneralOptions` + its serializer round
trip), or the CLI/tests and the VSIX UI will drift apart. The one deliberate exception is
`GeneralOptions.ExpandSelectStar` (see "Expand SELECT *" below) — it's VSIX-only and never reaches
`ScriptDomFormatter.Format`, so putting it on the shared interface would incorrectly imply the CLI
could honor it.

### Shared team config: `.sqlformatter.json`

`Options/FormatterConfigDiscovery.cs` walks upward from a file's directory looking for a
`.sqlformatter.json` (same JSON produced by "Export Formatter Settings" in the VSIX, read via
`Options/FormatterSettingsSerializer.cs`). If found, it overlays onto the base settings for that
one format operation only — never mutates the caller's settings object, and any discovery/parse
failure silently falls back to the base settings (a bad or missing repo config must never block
formatting). Both the VSIX and the CLI (`tools/SsmsSqlFormatter.Cli`) go through this same
discovery path, which is how "one style file shared between IDE and CI" works.

### Entry points into formatting

- `FormatSqlCommand.cs` (~1000 lines) — the SSMS command handler: selection-vs-document formatting
  as one undo unit, status bar messages, the Excel-results-grid export commands (via clipboard as
  a transport channel — see the detailed comments in `AcquireResultsAsync`), Export/Import
  Settings, Preview Format, Batch Format, Format All Open Files.
- `RunningDocTableEvents.cs` — implements format-on-save by hooking `IVsRunningDocumentTable`;
  always uses the rule-based engine and leaves unparseable files untouched.
- `FormatOnPasteListener.cs` — MEF `ITextViewCreationListener` that detects "a multi-line paste
  just happened" (vs. ordinary typing) and triggers rule-based formatting. Runs independently of
  package load — see the load-on-demand note in `SsmsSqlFormatterPackage.cs` about forcing package
  load via `IVsShell.LoadPackage` the first time it's needed.
- `Formatting/BatchFormatter.cs` — shared by "Format Files...", "Format All Open Files", and the
  CLI's `check`/`format` commands.
- `tools/SsmsSqlFormatter.Cli/Program.cs` — `ssmssqlfmt check|format <paths> [--config x.json]`;
  `check` exits 1 if anything would change or fails to parse (build/PR gate).

### Expand SELECT *

Resolves `SELECT *` (and `alias.*`) into explicit, ordered column lists using real table/view
structure — reachable via the `ExpandSelectStar` option (folds into the main Format command) or
the standalone "Expand SELECT *" Tools menu command. VSIX-only; never touched by format-on-save,
format-on-paste, batch formatting, or the CLI, because it needs a live database connection.

- `Formatting/SelectStarExpander.cs` — the testable core. `RewriteGivenSchema` is a pure,
  synchronous AST walk (given an `ISchemaCatalog`) that splices plain replacement text directly
  into the *original* source at each `SelectStarExpression`'s own `StartOffset`/`FragmentLength` —
  deliberately **not** regenerated through `Sql160ScriptGenerator`, which would drop comments
  before `ScriptDomFormatter`'s own comment handling ever saw them (same reason
  `ReinjectComments` exists). Anything it can't confidently resolve (CTE, derived table, unknown
  table, ambiguous schema) is left as `SELECT *`, independently per occurrence. `ExpandAsync` is
  the thin async wrapper that resolves real columns via a lookup delegate.
- `Options/SsmsConnectionDiscovery.cs` — reads the active query window's connection via pure
  reflection into `Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache` (an assembly
  that ships only inside an SSMS install, never as a NuGet package — referencing it at compile
  time would break CI, which builds without SSMS present). Every step is defensive; any failure
  returns null, which callers already treat as "expansion unavailable." This is the one part of
  the whole feature that can't be exercised by CI or an automated agent — only by running inside
  real SSMS with a live connection.
- `Formatting/SqlSchemaLookup.cs` — the only piece that actually queries a database
  (`INFORMATION_SCHEMA.COLUMNS`), given a plain ADO.NET connection string.

### Package load

`SsmsSqlFormatterPackage.cs` is an `AsyncPackage` that loads on demand (first command invocation),
not at SSMS startup, to keep startup fast. `SsmsSqlFormatterPackage.Instance` is the static
singleton other components (like the MEF-based paste listener, which runs outside normal package
load) reach through.

### Rule-based engine internals

`Formatting/ScriptDomFormatter.cs` (~1650 lines) is the largest file in the project: parses with
`Microsoft.SqlServer.TransactSql.ScriptDom`, then regenerates script text according to
`IFormatterOptions`, applying the Classic/Modern/Custom style presets. Known tradeoff (documented
in README): since output is regenerated from the AST, comments can be dropped — a warning fires
when comments are detected unless the AI engine is used instead. A script that fails to parse is
always left completely untouched (this is treated as a feature, not a bug — see
`FormatSqlCommandTests.cs` / `ScriptDomFormatterTests.cs` for the expected behavior).

### AI engine internals

`Formatting/AiFormatter.cs` supports two provider shapes via `AiOptions.Provider`: native Anthropic
Messages API, and Copilot/any OpenAI-compatible chat-completions endpoint (`choices[].message.content`
response shape). The API key/token is pulled from `CredentialVault`, never persisted in plain text.
The user's script text (including embedded literals) is sent to the provider — this is why
confirm-before-send is on by default in `AiOptions`.

## Versioning

`tools/update-version.ps1 -Version X.Y.Z` updates both the VSIX manifest
(`src/SsmsSqlFormatter/source.extension.vsixmanifest`) and `AssemblyInfo.cs` in one step — use it
instead of hand-editing either file when bumping the version. `.github/workflows/release.yml`
builds and attaches the VSIX to a GitHub Release when a `v*` tag is pushed.
