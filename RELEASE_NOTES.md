# v2.6.0 — Copilot as an alternate AI provider

**New in this release**

- **AI Engine → Provider**: choose **Anthropic** (unchanged) or **Copilot** / any
  OpenAI-compatible chat endpoint. Copilot uses a Bearer-token-authenticated,
  system+user chat payload instead of Anthropic's Messages API shape, and its
  `choices[].message.content` response shape is now recognized.
- The AI confirmation prompt and Tools menu help text now name the actual
  selected provider instead of always saying "Anthropic".
- Removed a redundant duplicate response-parsing code path left over from
  wiring in Copilot support.

**Install / upgrade:** download `SsmsSqlFormatter.vsix` below, close SSMS,
double-click to install, restart SSMS. Upgrades any earlier version and
keeps your settings.
