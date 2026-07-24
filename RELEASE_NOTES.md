**Fixed: "No result data was captured" — both invocation paths**

Two root causes, both fixed:

- **Keyboard shortcut (v2.0.2):** when Ctrl+Shift+Alt+X is pressed, the user
  is still physically holding Ctrl, Shift and Alt at the moment the extension
  synthesises the grid's copy keystroke — so the grid received
  Ctrl+Shift+Alt+C instead of Ctrl+Shift+C and copied nothing. The held
  modifier keys are now released at the OS level before the copy is sent.
- **Toolbar/menu click (v2.0.3):** clicking a button runs the command while
  focus still belongs to the toolbar, so the capture keystroke never reached
  the grid. The capture now runs a moment after the click completes, once
  focus has returned to the grid.

Workflow for both paths: click in the results grid, Ctrl+A, then either
press Ctrl+Shift+Alt+X or click Export Results to Excel — a styled .xlsx
workbook opens. Multiple result sets: Ctrl+Shift+Alt+A on each grid queues
it as a sheet; Ctrl+Shift+Alt+X then opens one workbook with every set on
its own sheet.

The v2.0 freshness safeguard is unchanged: stale clipboard content (browser
text, other applications) can never be exported — if no fresh grid data is
captured, the extension refuses with guidance.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
