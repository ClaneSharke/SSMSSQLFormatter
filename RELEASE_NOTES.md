# v2.4.0 — Format on save, SQLCMD support, secure key storage, and more

**New in this release**

- **Format on save** — opt-in setting that auto-formats `.sql` documents right
  before they're written to disk (Ctrl+S, Save All, etc.). Always uses the
  rule-based engine, so saving is never delayed by a network call or a
  confirmation prompt; a script that fails to parse is saved untouched.
- **SQLCMD-mode support** — `:setvar`, `:r`, `:connect`, `:on error`, and
  similar directives no longer cause the whole script to fail formatting.
  They're extracted before parsing and spliced back exactly where they were.
- **Secure AI API key storage** — the Anthropic API key is now stored in
  Windows Credential Manager for the current user instead of in plain text
  in the settings registry. Existing keys are migrated automatically the
  first time settings are loaded; if Credential Manager is unavailable, the
  key falls back to the previous plain-text storage rather than being lost.
- **Align `=` in assignments** (off by default) — pads shorter left-hand
  sides in a run of consecutive `name = expr` lines (SET clause assignments,
  old-style `alias = expr` SELECT items) so every `=` lines up.
- **Max line length** (off by default) — wraps a long top-level comma list
  (SELECT/GROUP BY/ORDER BY kept on one line) one item per line once it
  exceeds the configured length.
- **Preview Format diff view** — a new "Show differences" toggle in the
  Preview window renders a colored diff (added/removed/unchanged lines)
  against the original script.
- **Format Files...** — new Tools menu command that formats one or more
  `.sql` files on disk in place (rule-based engine, original encoding
  preserved), with a confirmation prompt and a per-file failure report.
- Expanded automated test coverage across the AI formatter, CSV/Excel
  export, and command-layer helpers.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
