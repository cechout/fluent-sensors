# Optional tray-adjacent taskbar widget

## Behavior

Settings → Taskbar Widget & Flyout Behavior now includes **Show on the Left of the
System Tray**, directly below **Lock Taskbar Widget Position**. It is off by
default, including for existing settings files that predate the property.

When enabled, the widget ends immediately before the notification area, with a
theme-aware 1 DIP divider and 4 DIP margins on either side. Native tray bounds are
read each second, so the widget follows tray expansion/contraction even when the
outer taskbar rectangle is unchanged. On a vertical taskbar, it sits above the
tray and the divider is horizontal. The existing primary-taskbar selection remains.

Tray anchoring overrides dragging while enabled. It never writes a computed tray
position into the saved per-edge manual offsets. Turning it off restores the
existing manual position and normal drag-lock behavior. Pin selection, graphs,
sensor acquisition, flyout content, startup settings and the KB/s fix are preserved.

The widget waits for reliable tray and task-button bounds. If the complete widget
does not fit between the app buttons and the tray with an 8 DIP button gap, its
native child window has zero length until room returns. It does not cover app
buttons, move shell controls, drop pinned selections, or reserve taskbar space.
Reducing graph widths or the pinned sensor count can make it fit a crowded bar.

## Reference and implementation

Busy was cloned to the sibling `Busy` directory from https://github.com/aivanyuk/busy
at commit `ebef0832f8b5e2c6a78aaf8266b39b313354453f`. Busy remains unmodified.

Adapted reference files:

- `app/src/taskbar/explorer.rs`: notification-area bounds and free span before it.
- `app/src/taskbar/tasks.rs`: app-button bounds through background UI Automation.
- `app/src/taskbar/mod.rs`: timer-driven geometry checks and divider dimensions.

Only placement/detection/divider behavior is adapted, into the existing WinUI
widget. Busy's MIT notice is retained in [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md),
which is copied into build and publish output.

`WinTaskbarTrayTracker` performs native reads synchronously and bounds UIA work to
one pending worker. Each worker owns its COM client, with connection/transaction
timeouts. Only value rectangles cross to the UI thread. Temporary scan failures
retain the last button bounds for the same taskbar; reset/generation checks discard
results from a prior toggle, widget reopen or taskbar HWND. A new window instance
gets its own tracker on Explorer recreation. There is no change to shared shell
styles, registry entries, other apps' windows, or Busy configuration.

`TaskbarWidgetPlacement.CalculateNearTray` checks bounds, monitor band, available
space, orientation and DPI-scaled lengths. The widget's existing timer moves it
only when its calculated rectangle changes. Manual placement does not run the new
tracker. The toggle's model default, load/save fields, notification event, XAML
control and English resource strings are all included. Other UI languages use the
existing English fallback for these new strings.

## Verification

From the repository root, capture command output under `logs/`:

```powershell
dotnet run --project tests/TaskbarPlacement/TaskbarPlacement.csproj -c Release
dotnet run --project tests/ThroughputFormatting/ThroughputFormatting.csproj -c Release
```

After the full Release build (see [AGENTS.md](../../AGENTS.md)), a read-only live
integration check can reuse its generated COM interop assemblies:

```powershell
dotnet run --project tests/TaskbarProbe/TaskbarProbe.csproj -c Release
```

With the option enabled and the installed taskbar widget open, add `-- --verify-widget`
to verify that the running child window ends at the actual tray boundary. The probe
uses a PerMonitorV2 manifest like the app, so native and UIA rectangles are compared
in physical pixels rather than DPI-virtualized coordinates.

Results on 2026-11-06:

- 108 placement checks passed: four edges, 96/144/192 DPI, negative monitor origins,
  tray growth, task-button growth, recovery, missing landmarks and stale monitor bands.
- Live probe detected this PC's tray and task-button bounds at 144 DPI. Reset and
  HWND-change checks passed. It reads Explorer but does not move or restart it.
- Installed-widget verification passed at 144 DPI: widget `(1875, 1532, 164, 65)`
  ended exactly at tray X `2039`. These are one measured snapshot, not fixed offsets.
- Full Release build, changed-file whitespace verification and self-contained
  FolderProfile publish passed. Existing project warnings remain; the new tracker
  and divider partial file emitted no compiler diagnostics.
- XML parsing, resource-key uniqueness, documentation references and diff whitespace
  passed. The PR workflow now runs geometry and throughput checks after building.
- Visual acceptance is not yet verified: the desktop-control runtime fails to
  initialize with `failed to write kernel assets: The system cannot find the path specified`.

Manual acceptance: toggle on/off, check the divider in light/dark taskbar themes,
expand/collapse tray content, open/close apps to change button occupancy, resize
graph width and pinned count, close/reopen the widget, restart the app, and check
taskbar edge/DPI changes and Explorer restart recovery. Confirm manual offsets and
pin selections survive toggling and restart. Real tray movement, divider rendering,
flyout interaction and Explorer restart are not established by pure geometry tests.

## Installation and rollback

Use [tools/Update-LocalInstallation.ps1](../../tools/Update-LocalInstallation.ps1)
after building and publishing with FolderProfile. It backs up the complete installed
app and settings, verifies copied hashes and checks startup, restoring original app
files on failure. The feature stays off until the user enables it.

To roll back, run the helper as administrator with `-RestoreFrom` pointing to the
backup directory recorded in the timestamped `logs/installation-*.json` result.
Rollback preserves current settings; old app versions ignore the new false-by-default
setting. An official update can overwrite this local contribution.

Installed successfully on 2026-11-06. Backup:
`artifacts/installation-backups/20261106-042732-194`.
All copied payload hashes were verified and the app passed the startup check.
Assembly SHA-256: `95F4F7147ACD32F9EC026369F264DEDA9C3D74D2727166AA44C340A341D133EB`.
All pre-existing setting fields, sensor selections, sensor state and saved window
positions matched the backup after restart. The new setting was subsequently found
enabled and that saved choice was preserved. The code default remains false.

For the prior KB/s-only custom build, run as administrator:

```powershell
.\tools\Update-LocalInstallation.ps1 -RestoreFrom '.\artifacts\installation-backups\20261106-042732-194'
```

No upstream push or pull request was made.
