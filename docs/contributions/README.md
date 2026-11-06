# Contribution guide: taskbar readability, positioning and network units

This guide describes the final patch against upstream commit
`e4889379f2c7019ec2beb635fb981cbac3f57300` (application version 1.8.0).
It is an unofficial contribution, not an upstream release or guarantee of acceptance.

## User-visible changes

| Area | Final behavior | Compatibility/default |
| --- | --- | --- |
| Network throughput | Values below 1 MB/s display KB/s; values at/above 1 MB/s retain MB/s; existing GB/s boundary is preserved | Display only; raw readings, CSV base units, thresholds and bit-rate mode unchanged |
| Graph hover | Throughput hover values include the correctly scaled unit | Other sensor types retain existing formatting |
| Tray placement | Optional Show on the Left of the System Tray below position lock; follows live tray bounds, adds a divider | Off by default; manual offsets preserved; dragging overridden while enabled |
| Background | Calculated, Transparent or in-app Acrylic | Existing selection preserved; no native graph-card backdrop hosts |
| Plot blur | Independent Blur Graph toggle, combinable with every background | Off by default; small equal-axis Gaussian filter on line/fill only |
| Network labels | Optional Network Labels dropdown: Text labels or Direction icons | Text default; only known Download/Upload Throughput labels change |
| Direction icons | Drawn arrows with wide heads; 8 by 10 DIP, 1.8 DIP rounded stroke | 12 DIP reserved beside values; horizontal and side taskbars |
| Value text | SemiBold taskbar readouts | Values and unit formatting unchanged |

Tray detection reads native TrayNotifyWnd bounds each second and asynchronously
scans task buttons with UI Automation, with a single pending scan and 500 ms
connection/transaction timeouts. Stale results are rejected after taskbar handle
changes/reset. Placement uses physical screen coordinates and DPI conversion,
handles four taskbar edges and negative monitor origins, and leaves an 8 DIP gap
after task buttons. When no reliable landmarks or insufficient room exist, the
widget has zero along-axis length rather than covering shell buttons. Existing
primary-taskbar behavior remains. Repositioning hides a stale anchored flyout.

Gaussian plot filtering uses the existing LiveCharts/Skia image-filter API with
sigma 0.65 on both axes. It does not cover the plot with a frosted/tinted overlay.
Names, values, interaction overlays and alarm sections stay sharp. Paint recreation
reapplies/removes the filter on mode changes and subsequent updates.

Direction icons affect display names only. Known Download/Download Speed and
Upload/Upload Speed Throughput sensors map to direction markers, rendered as
paths. Custom names, stored sensor identities, CSV headers and unrelated types
stay intact. Flyout status-row names remain descriptive text.

## Saved settings and migration

- TaskbarWidgetNextToTray: false when absent. Saved manual position/lock settings
  are retained and become effective again when tray anchoring is turned off.
- TaskbarGraphCardMaterial: absent/unknown numeric values fall back to the old
  TaskbarUseTransparentGraphBackground flag. The legacy flag is still saved.
- TaskbarBlurGraph: nullable persisted flag, false for fresh files. Explicit saved
  on/off wins; legacy Blurred or Mica backgrounds become Transparent with blur on.
  Old enum names remain readable but are not dropdown choices.
- TaskbarNetworkDirectionIcons: false when absent; independent of color/blur.
- Runtime settings events refresh graph consumers; graph view models unsubscribe
  on disposal. UI mutations follow the app's existing UI-thread settings path.

No dependency or application-version bump is included. PublishTrimmed=false,
ReadyToRun, WinUI locale payloads and existing packaging conventions are preserved.
Busy's MIT notice is copied into build and publish output.

## Crash investigation and Acrylic limitation

Initial native SystemBackdropElement graph cards caused reported freezes/crashes
when switching backgrounds and when changing a selected sensor then pressing
Update. Windows logged Microsoft.UI.Input.dll fail-fast 0xc0000409 at offset
0x9a763. Retaining the host during dropdown transitions did not cover graph
replacement. The final patch removes graph-card SystemBackdropElement and its
custom native controller implementation completely. Graph cards now use a normal
XAML Border with AcrylicBackgroundFillColorDefaultBrush. This is in-app Acrylic;
it cannot blur external windows/the desktop behind the taskbar. The independent
flyout-window material remains unchanged. Transparency/accessibility policies may
produce a solid Acrylic fallback. Gaussian plot blur does not depend on Acrylic.

Host teardown is the suspected trigger, not a debugger-confirmed native root cause.
The exact sensor-selection Update reproduction has **not** been verified after
the final correction. Computer-use initialization fails with a missing kernel
assets path. Do not describe this contribution as fully crash-verified.

## Validation

Use full Visual Studio MSBuild for the application; dotnet build cannot resolve
its COM references. See repository AGENTS.md for restore, build and FolderProfile
publish commands. Generated logs belong under logs/ and are not committed.

| Check | Latest local evidence |
| --- | --- |
| Full x64 Release build + FolderProfile publish | Passed final native-host-removal revision; existing nullable/analyzer and PRI263 Network_LinkSpeed warnings remain |
| tests/ThroughputFormatting | 21 boundary/raw-unit/culture checks passed |
| tests/TaskbarPlacement | 108 geometry checks over four edges, three DPIs and negative origins passed |
| tests/GraphCardMaterials | 19 material migration, 16 independent blur and 7 direction mapping checks passed |
| tests/TaskbarProbe -- --verify-widget | Native tray/button discovery, cache reset/handle change and actual installed-widget adjacency passed on prior installed revisions at 144 DPI |
| Whitespace, XML/resource uniqueness, git diff checks | Passed affected changes |

Before merge: repeat Download → Upload → Update with flyout open/closed, each
background and blur on/off; repeat pin/unpin, restart, Explorer restart, crowded
tray expansion/contraction, horizontal/vertical taskbars, light/dark themes,
Windows transparency off and high contrast. Confirm arrow/value clipping at
125–200% scaling. These remain manual requirements, not completed claims.

## Source, licensing and review map

Busy was cloned separately, unchanged, at
`ebef0832f8b5e2c6a78aaf8266b39b313354453f` from
https://github.com/aivanyuk/busy. Only tray/task-button positioning ideas/code were
adapted. See THIRD-PARTY-NOTICES.md for full MIT attribution. No Busy executable,
sensor collection, taskbar UI or other application features are bundled.

- [Throughput details](automatic-throughput-units.md)
- [Tray placement details](tray-adjacent-widget.md)
- [Current graph behavior and historical investigation](taskbar-graph-materials.md)
- Core/Taskbar/WinTaskbarTrayTracker.cs and TaskbarWidgetPlacement.cs: discovery/geometry.
- TaskbarWidgetWindow.TrayPlacement.cs: divider and tray-mode UI integration.
- Common/Sensors/SensorUnitFormatter.cs and NetworkDirectionLabel.cs: display logic.
- Common/UI/GraphCardMaterial.cs and NetworkDirectionIcon.cs: migration/vector geometry.
- Controls/SensorGraph: view-model bindings, background, plot filter and readouts.
- Features/Settings and Persistence: localized controls and saved preferences.
- tests/ and .github/workflows/pr-build.yml: regression projects and CI integration.
- tools/Update-LocalInstallation.ps1: local Program Files update helper, not startup code.

## Personal release and rollback

The personal build is unofficial and retains upstream application version 1.8.0.
Its separate personal tag identifies the patched source. It does not contain the
PawnIO driver; existing upstream prerequisites still apply. Never treat this as an
official upstream update. The built-in updater continues to follow upstream;
installing a later official release may replace local features until upstream
incorporates them. A prerelease label communicates the outstanding manual checks.

A portable archive contains one top-level FluentSensors folder and portable.txt;
it stores settings separately from the installed app. The installer uses the
upstream install identity/location and can replace the installed copy. Back up
settings before switching builds. Release assets must include THIRD-PARTY-NOTICES.
Local update backups are under artifacts/installation-backups; the helper verifies
all copied file hashes and attempts app-file rollback on startup failure. Restoring
app files does not automatically restore settings. Do not publish local backups,
user settings, diagnostic logs, credentials or hardware sensor snapshots.

Personal installer packaging note: the local Inno compiler does not support the
upstream `modern dynamic` wizard style. The personal installer was compiled from
a temporary copy using `modern`, with absolute source/output directories; the
tracked upstream installer script is unchanged. Binary version remains 1.8.0.
This changes wizard presentation only. The portable package uses the existing
FolderProfile output with all WinUI locale folders and MIT notices intact.
