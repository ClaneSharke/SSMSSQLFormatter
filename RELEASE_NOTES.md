**Fixed (root cause): "No result data was captured"**

The capture failed because the extension blocked SSMS's UI thread while
waiting for the copy to happen. The synthesised copy keystroke is delivered
through the Windows message queue, and only the UI thread can drain that
queue — so sleeping on that thread meant the results grid never processed
the keystroke at all. The clipboard was then read before any copy had
occurred, and the export refused.

The same bug explains the occasional export of unrelated content: the copy
completed only after the command had already finished, leaving the previous
clipboard contents in play during the run.

The capture now yields instead of blocking, so the message pump keeps
running and the grid actually receives the keystroke. The keystroke itself
is synthesised with direct Win32 calls rather than SendKeys, which behaves
unreliably inside the Visual Studio shell.

Both invocation paths work: click in the results grid, Ctrl+A, then either
press Ctrl+Shift+Alt+X or click Export Results to Excel — a styled .xlsx
workbook opens. Multiple result sets: Ctrl+Shift+Alt+A on each grid queues
it as a sheet, then Ctrl+Shift+Alt+X opens one workbook with every set on
its own sheet.

The freshness safeguard is unchanged: if no fresh grid data is captured,
the extension refuses with guidance rather than exporting the wrong thing.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
