# Taskbar graph background materials

Settings > Taskbar Widget & Flyout Appearance > Taskbar Graph > Graph Background
Source offers Calculated, Transparent, Acrylic and Mica. Existing selections are
preserved. Acrylic and Mica render within each graph card, behind its label and
value, including horizontal and vertical taskbar graphs and flyout graph cards.
The separate flyout window material remains independent. The original Calculated
and Transparent behavior is retained, including the existing flyout card fill.

The app already uses Windows App SDK 2.2; no package upgrade is required.
`SystemBackdropElement` hosts the native material below the chart and text. A
per-card `GraphCardBackdrop` keeps input active because embedded taskbar windows
never take focus. It copies theme and high-contrast configuration from XAML,
uses Windows material policies, and disposes controllers on target disconnect.
Unsupported materials use the themed card brush. Windows transparency,
accessibility and power policies can replace effects with their solid fallback.
Mica uses the desktop wallpaper; Acrylic blurs what is behind the surface.

`TaskbarGraphCardMaterial` is persisted separately. When absent or an unknown
numeric value, the previous `TaskbarUseTransparentGraphBackground` determines the
selection. Saving retains that legacy flag for older builds. Turning the material
off releases its controller while keeping the native host attached. Graph colors, sensor values, graph dimensions,
tray placement and the main widget window appearance are unchanged.

## Verification

- `dotnet run --project tests/GraphCardMaterials`: legacy migration, explicit
  choices taking precedence and string-enum persistence round trips (16 checks).
- Existing throughput formatting and taskbar placement checks should remain green.
- Full x64 Release build and FolderProfile publication are required.
- Visual checks still required: all four choices while embedded next to the tray,
  flyout cards, light/dark themes, Windows transparency off, vertical taskbars,
  Explorer restart, and switching back to Calculated/Transparent. Automated
  computer-use initialization currently fails with a missing kernel-assets path;
  build success alone does not establish visual blur correctness.

API reference: [Microsoft system backdrop documentation](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops).

## Local verification on 2026-11-06

Release build and FolderProfile publish passed. The 16 material migration tests,
21 throughput tests, 108 placement tests, whitespace validation, XAML/resource XML
validation and resource-key uniqueness checks passed. Existing nullable/analyzer
warnings and the existing PRI263 missing Network_LinkSpeed neutral resource warning
remain. No visual material checks are claimed.

The installed app was updated with the existing safe updater and restarted. Every
published file was hash-verified against the installation. Rollback app files and
settings are in `artifacts/installation-backups/20261106-045050-747`; result is in
`logs/installation-20261106-045050-747.json`. Installed assembly SHA-256:
`DA6BA061626A48A02CD2FAAC9B9BF7548B37E3CA422708BF9A8380E3B064D154`.

The live installed-widget probe also passed: at 144 DPI the graph widget ended
exactly at the detected tray boundary. Existing settings fields and sensor state
matched the backup after restart. Sensor selections had one additional Taskbar
entry relative to the pre-stop snapshot; it was left intact rather than reverting
live selection changes.

## Material-switch crash correction

The initial installed implementation crashed when selecting Acrylic followed by
Transparent or Mica. Windows Application Error records identify
`Microsoft.UI.Input.dll`, exception `0xc0000409`, offset `0x9a763`.
Evidence is saved under `logs/graph-material-crashes-*.json`.

The suspected trigger is native host teardown during the settings input callback:
the old path collapsed `SystemBackdropElement` and cleared or replaced its
SystemBackdrop. The corrected path keeps the host visible, uses opacity to hide
it, and retains one GraphCardBackdrop instance. Only material controllers change;
each target is explicitly removed before its controller is disposed. Theme and
high-contrast configuration remain connected. No controller remains for
Transparent or Calculated. The host lifetime ends with its graph/window.

Build and settings tests cannot establish that the native input crash is resolved.
Required live regression: repeatedly cycle Acrylic > Transparent > Mica >
Calculated > Acrylic with the taskbar embedded and the flyout both open and closed,
then close/reopen the flyout and restart the app. Screen-control initialization is
unavailable in this session, so these live transitions remain unverified.

The correction passed the full x64 Release build, FolderProfile publish, 16 saved-setting checks, whitespace verification and git diff checks. Existing compiler and PRI263 warnings remain. Live material transitions still require verification.
