# v2.5.1 — Options page category order fix

**New in this release**

- Fixed: on the General options page, category **"10. Shared config"** sorted
  between **"1. Engine"** and **"2. Basics"** instead of after **"9. Format on
  save"** — `PropertyGrid` sorts categories as strings, not numbers. Every
  category number is now zero-padded (01–10) so the display order matches
  the intended order.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
