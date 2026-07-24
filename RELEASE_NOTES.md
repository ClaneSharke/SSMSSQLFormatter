**New in this release**

**Excel export**
- **Frozen header row and AutoFilter** on exported workbooks, so headers stay
  visible and results can be filtered and sorted immediately. Both on by
  default, both switchable under "Excel appearance".
- **Output folder** option — exports land in a folder you choose instead of the
  temp folder, with an optional **Save As prompt** for each export.
- **Include the query on a separate sheet** — the workbook records the SQL that
  produced the data.
- **CSV export** as an alternative format, with proper RFC 4180 quoting.

**Formatter**
- **Built-in function casing** and **data type casing** can now be set
  independently of keyword casing. Function names are only re-cased when
  immediately followed by "(", so a column that shares a function name is left
  alone; strings, comments, quoted identifiers and variables are never touched.

**Options**
- The **font name** and **font size** settings are now dropdowns. The font list
  shows standard Windows fonts that are actually installed on the machine, so it
  can't offer something that won't render; another font can still be typed in.

**Settings**
- **Tools → Export / Import Formatter Settings** writes every option to a JSON
  file and applies it back, for sharing a house style across a team or
  restoring a setup after a reinstall. Unrecognised values are skipped rather
  than overwriting existing settings.

Capture and formatting behaviour are otherwise unchanged from v2.1.1.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
