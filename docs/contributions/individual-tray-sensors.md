# Individual tray sensors, compact cards and reordering

This extends the existing taskbar sensor profile with optional individual notification-area indicators, a compact taskbar style and saved sensor ordering. Defaults remain the existing taskbar graph view. The compact visual design is inspired by [MicaStats](https://github.com/manoi-bms/MicaStats), with attribution in THIRD-PARTY-NOTICES.md; sensor detection stays with Fluent Sensors.

## Controls and behavior

Settings > Taskbar Widget & Flyout Behavior includes a default-to-tray toggle. Each sensor header in System Sensors replaces its redundant source icon with a placement button and a separate layout button:

- The placement arrows move that sensor between taskbar and tray, overriding the global default.
- The panel-layout icon switches its horizontal taskbar appearance between the existing graph and a compact label/value/level card. This preference survives moving to the tray and back.
- Taskbar and tray indicators both open the existing System Sensors flyout, which retains every selected sensor and its graphs.

Multiple tray sensors get separate native icons. Each has a deterministic identity derived from its sensor ID and executable path; they do not replace each other. Removing a selection removes only that icon. Closing the widget removes all its indicators; reopening restores them. The notification library handles Explorer recreation. Deferred UI routing avoids removing/recreating controls inside their initiating click callback, and cleanup guards ignore late callbacks.

The tray has numeric-only bold white text on opaque gray (RGB 55/58/60), rounded corners and padding. Temperature, percentage and rate symbols/units are omitted in the icon; the tooltip retains the full name, formatted value and units. Network numbers keep automatic KB/MB/GB scaling. Glyphs use Bahnschrift SemiCondensed with an Arial fallback, natural proportions, winding fill (avoiding overlap cutouts in digit 4), a 40/64-height content area and a 10/64 corner radius. Native system small-icon size/DPI determines the bitmap size. Windows owns tray icon order, slot size and overflow visibility; no shell visibility settings are changed.

## Compact taskbar cards

Compact cards contain a top label, a semibold value and a right-side level bar. They use 8-DIP labels and 12-DIP values. CPU package temperature uses CPU instead of TMP; dedicated maximum/average sensors retain MAX/AVG. Network labels are DOWN/UP. Temperature cards retain the degree symbol, percentages retain %, and network cards retain the complete automatically scaled unit.

The level fill uses the selected hardware/type/accent/custom taskbar graph color. A breached enabled threshold overrides it with that individual sensor's threshold color, matching the graph/value; clearing/disabling the alarm restores the base graph color. Tray numbers stay white independently.

Cards measure the complete live text instead of ellipsizing it. The widget uses the same measured width, preserving activation, visibility and sibling order during routine resize. Width updates no longer explicitly hide the flyout. Initial embedding still shows/applies the window frame; actual taskbar edge/handle changes retain their existing relocation handling. Padding/top inset aligns mixed graph/card rows. Vertical taskbars retain their existing graph presentation. The flyout graph stays visible and sharp irrespective of compact style and taskbar-only blur.

The existing graph style's upper-right semibold value defaults to the original 10 DIP. Settings > Taskbar Widget & Flyout Appearance > Taskbar Graph > Graph Value Text Size offers Default (10 DIP) or Larger (11 DIP), applying live without recreating graphs. `TaskbarUseLargerGraphValues` defaults to false for older settings and is saved in app settings. Only the embedded taskbar graph changes; compact/tray sizes, flyout values and other graph surfaces remain separate.

## Reorder mode

The move icon immediately left of Pause enters a separate list of named sensors with grab handles and up/down buttons. Graphs cannot receive hover or threshold interactions through this list. Drag to preview an order; release saves it. Canceled capture restores the last saved order. Arrow moves save immediately. The check mark returns to graphs; hiding the flyout also exits the mode. Pause state remains independent.

Pointer capture belongs to the stable scrolling container while rows move. This avoids the native WinUI system drag-and-drop path affected by [Microsoft issue 7690](https://github.com/microsoft/microsoft-ui-xaml/issues/7690) in elevated unpackaged apps. Arrow actions and placement reconciliation run after their UI callback. The mode retains the graph-sized flyout window and permits scrolling long selections. It changes the taskbar/flyout order; it cannot force Windows to reorder native notification icons.

## Persistence and implementation map

All preferences are in sensor-selections.json:

| Field | Meaning and default |
| --- | --- |
| TaskbarSensorsDefaultToTray | false when absent; default destination |
| TaskbarSensorTrayOverrides | Per-sensor destination overriding the global setting |
| CompactTaskbarSensors | Independent set of compact-style IDs |
| TaskbarSensorOrder | Separate ordered-ID preference; absent means discovery order |

Selection membership is unchanged. New IDs without an order preference follow saved IDs. Deselected/disconnected IDs retain preferences for re-selection/reconnection. Constructor, selection reconfiguration and placement updates reapply order to taskbar/flyout collections using moves, retaining existing graph instances, histories and pause state.

- SensorSelectionState/Service: persistence, overrides, order and placement notifications.
- TaskbarWidgetViewModel: existing graphs, filtered taskbar subset, order reconciliation and tray lifetime.
- SensorTrayIndicators/Renderer: native icon identity, click dispatch, icon ownership, number rendering and disposal.
- TaskbarSensorSlot: mixed graph/card presentation, per-sensor colors, live measurement and render gate.
- TaskbarFlyoutWindow.Reorder: isolated pointer mode and keyboard-accessible arrow controls.
- WinTaskbarEmbedder: separate initial embedding and nonactivating resize flags.
- SensorPanelControl and SettingsPage: localized placement/style buttons and global default.
- App: existing flush plus a timestamped local managed-exception log under the persistence directory's logs folder; no telemetry.

## Validation and remaining limits

Release x64 local builds and launches passed on 2026-11-09, with the existing nullable/analyzer and PRI263 warning. Existing throughput/geometry/material suites remain applicable. tests/TaskbarSensorPresentation links the actual state/service and taskbar view model with stubbed UI, hardware and native tray dependencies. It checks legacy defaults, duplicates, mixed placement, saved/restarted order, selection/reconnection, retained graph identity/history/pause, and unchanged membership/style preferences. CI runs this project.

Local renderer/native harnesses under ignored logs checked 24 numeric samples at 16/24/32 pixels, both renderer themes, rounded alpha corners, formatting, and distinct per-sensor tray identities. Previews were inspected. These are not a live multi-monitor or Explorer-restart test. User screenshots guided sizing, color and threshold corrections. A live screenshot confirmed the reorder button beside Pause. The automation helper reports higher Windows integrity for this elevated app, so it cannot verify interactive drag sequences.

Before merge/release validation, repeat multiple taskbar/tray switches, global default plus individual overrides, mixed graph/card/tray rows, close/reopen, pin/unpin, pause/resume, selection Update and restart. Check per-sensor threshold edits/breach/reset; menu and flyout focus during changing network widths; reorder first/last, cancellation, scrolling, repeated toggles and keyboard arrows; then order/placement/history after reconfiguration and restart. Check light/dark/high contrast, small taskbars, display scaling, vertical edges and Explorer restart. A passing build/harness does not establish these live checks.

Personal release packaging and upstream review are covered by the [complete contribution guide](README.md). Local trial configuration is copied separately; releases must exclude personal settings, logs and hardware snapshots.

The font-size preference was built and launched locally on 2026-11-09. Changed XAML/resources, unique localization keys and whitespace checks passed. The local trial was set to Default; interactive selection/restart persistence remains a manual check because the app is elevated. Personal release and PR documentation include this preference alongside the earlier presentation features.
