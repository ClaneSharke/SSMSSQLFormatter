# v2.5.0 — CLI tool, shared repo config, syntax-highlighted diff, JOIN/CASE alignment, format-on-paste

**New in this release**

- **Standalone CLI (`ssmssqlfmt`)** — `tools/SsmsSqlFormatter.Cli` builds a command-line
  tool with `check` (dry-run, exits non-zero if anything would change or fails to
  parse — for gating a build/PR) and `format` subcommands. No SSMS/VS install
  required.
- **Shared team config** — drop a `.sqlformatter.json` (same format as Export
  Formatter Settings) into a repo folder and it's picked up automatically by
  anyone (or any CI run) formatting a file under that folder, in both SSMS and
  the CLI. Turn off with **Use folder-level .sqlformatter.json** if you'd rather
  a folder's contents never affect formatting.
- **Syntax-colored diff** — the Preview window's "Show differences" view now
  colors keywords, strings, numbers and comments, not just added/removed lines.
- **Align ON in JOIN clauses** / **Align THEN in CASE expressions** — two new
  alignment options. Each also reshapes ScriptDom's default layout first
  (condensing a JOIN clause onto one line, or expanding a multi-branch CASE
  onto separate lines) since the generator never lays these out in a shape
  that leaves anything to align.
- **Format on paste** — reformats a `.sql` document right after a detected
  paste (a single multi-line insert, distinct from ordinary typing). Please
  report any issue — this is the first feature in the extension built on VS's
  live-editor (MEF) extensibility rather than its command/save infrastructure.
- **Format All Open Files** — formats every currently-open `.sql` document in
  place as a normal, undoable editor edit; nothing is written to disk unless
  you save afterward.
- **Fixed:** exporting settings could crash with "Self referencing loop
  detected for property 'AutomationObject'" — reflecting over a settings
  object's properties was also picking up inherited, non-serializable
  DialogPage/COM properties.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
