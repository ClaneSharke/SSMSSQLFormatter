# v2.4.1 — Blank-line spacing now applies inside nested blocks

**New in this release**

- Fixed: **"Blank lines between statements"** only spaced statements directly
  inside a batch, so it had no effect on anything inside a stored
  procedure/function/trigger body or a `BEGIN...END`/`IF`/`WHILE`/`TRY-CATCH`
  block — which covers most real T-SQL. It now applies at every nesting
  level, so a blank line is inserted after a nested block closes too.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
