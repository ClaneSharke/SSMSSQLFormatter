# v2.3.0 — export progress indicator

**New: progress indicator during export**

Exporting results now shows a small "Capturing results… / Writing workbook…"
status window so it's clear the export is running. Input to SSMS is briefly
blocked while the file is written and Excel is launched, which prevents a
stray click from disrupting the export - a click during the write could
previously interfere with it.

The capture itself (reading the results grid) is unaffected and still relies
on the grid keeping focus: click in the grid, Ctrl+A, then Ctrl+Shift+Alt+X.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
