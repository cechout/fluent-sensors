# Taskbar graph background and blur settings

## Current implementation

Graph Background Source offers Calculated, Transparent and Acrylic. Blur Graph
is an independent 0.65-sigma Gaussian filter on the plotted line and fill. Text,
values and hover/threshold overlays stay sharp. Network labels can use vector
arrows; their display size is 8 by 10 DIP with a 1.8 DIP stroke and 12 DIP reserved
width. Taskbar values use SemiBold. Original sensor names and data are preserved.

Graph cards now use an ordinary XAML Border with the theme's
AcrylicBackgroundFillColorDefaultBrush for Acrylic. There are no
SystemBackdropElement instances or native backdrop controllers in graph cards.
This is in-app Acrylic, not desktop-backdrop Acrylic: it cannot blur external
windows/taskbar content. The flyout window's independent native background
settings are unchanged. Windows policy can give Acrylic a solid fallback.

The native graph-card host introduced earlier was implicated in repeated
Microsoft.UI.Input.dll fail-fast crashes (0xc0000409, offset 0x9a763), first on
background switching and then on sensor-selection Update, which rebuilds graphs.
Keeping that host attached only avoided one teardown path. This revision removes
the native host entirely so graph replacement uses ordinary XAML elements. The
native crash's complete call stack has not been obtained; host teardown remains
the suspected trigger, rather than a debugger-proven root cause.

Settings migration retains old enum names for reading. Legacy Blurred/Mica becomes
Transparent plus blur enabled. Explicit TaskbarBlurGraph wins. Tests cover material
migration, independent blur precedence and direction mapping. Required live
regression: repeatedly change Download to Upload and press Update with flyout
open/closed, each background and blur on/off; repeat pin/unpin and window restart.
Visual automation is unavailable, so native input-transition success must not be
inferred from build/tests alone.

Validation for the selection-update correction: full x64 Release build and FolderProfile publish passed, along with 19 material migration, 16 independent blur and 7 direction mapping checks, whitespace and XAML XML validation. Existing compiler and PRI263 warnings remain. The reported sensor-update interaction still needs live verification.

## Historical implementation and validation

The text below records earlier revisions; native graph-card backdrop code and
its workaround have now been removed as described above.

# Taskbar graph background materials

Settings > Taskbar Widget & Flyout Appearance > Taskbar Graph > Graph Background
Source offers Calculated, Transparent and Acrylic. A separate Blur Graph toggle
combines with any of these backgrounds. Existing selections are
preserved. Acrylic renders behind each graph card. Blurred graph applies a small, uniform Gaussian filter to the chart line and fill.
There is no tinted overlay. Chart content is softened
while the label and value remain sharp. These apply to horizontal and vertical
taskbar graphs and flyout graph cards.
The separate flyout window material remains independent. The original Calculated
and Transparent behavior is retained, including the existing flyout card fill.

The app already uses Windows App SDK 2.2; no package upgrade is required.
`SystemBackdropElement` hosts the native material below the chart and text. A
per-card `GraphCardBackdrop` keeps input active because embedded taskbar windows
never take focus. It copies theme and high-contrast configuration from XAML,
uses Windows material policies, and disposes controllers on target disconnect.
Unsupported materials use the themed card brush. Windows transparency,
accessibility and power policies can replace effects with their solid fallback.
The Mica dropdown choice was replaced because it did not visibly improve graph
readability. Saved Mica selections migrate to Blurred graph. Acrylic blurs what is
behind the surface; Blurred graph blurs the plotted content itself.

`TaskbarGraphCardMaterial` is persisted separately. When absent or an unknown
numeric value, the previous `TaskbarUseTransparentGraphBackground` determines the
selection. Saving retains that legacy flag for older builds. Turning the material
off releases its controller while keeping the native host attached. Graph colors, sensor values, graph dimensions,
tray placement and the main widget window appearance are unchanged.

## Verification

- `dotnet run --project tests/GraphCardMaterials`: legacy migration, explicit
  choices taking precedence and string-enum persistence round trips and saved Mica migration (19 checks).
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

## Blurred graph and optional network direction icons

The current dropdown replaces Mica with **Blurred graph**. The graph line and fill use LiveCharts Skia Gaussian image filters with equal
0.65 sigma in both directions. Name, value and hover/threshold overlays remain
sharp. No frosted XAML brush obscures the graph, and this does not create or tear down a native
SystemBackdropElement when the blurred style changes. Windows transparency and
accessibility policies can substitute a solid brush fallback.

A **Network Labels** dropdown next to Graph Background Source offers Text labels
(default) and Direction icons. Download/Download Speed becomes a down arrow;
Upload/Upload Speed becomes an up arrow, only for Throughput sensors in taskbar
scopes. Custom sensor names and unrelated sensor types remain unchanged. This
changes display labels only, including side-taskbar labels; sensor identity,
stored names, CSV names and values are untouched. The flyout status-row names
remain descriptive text. The preference persists as TaskbarNetworkDirectionIcons.

Regression checks cover saved Mica migration, all material choices, string-enum
round trips (19 checks), and direction-label mapping including custom names and
unrelated types (7 checks). Visual blur and rapid switching still need manual
verification because computer-use initialization is unavailable in this session.

The Blurred graph and icon update passed x64 Release build, FolderProfile publish, whitespace and XML/resource-key checks. Native visual blur and rapid switching are not verified automatically.

## Subtle Gaussian blur correction

The earlier in-app acrylic overlay hid the chart and changed appearance with
activation. It has been removed. Gaussian filtering now happens in the Skia plot
paints themselves; the graph retains its original colors/alpha and no activation
policy controls this effect. Selecting another material recreates unfiltered
paints immediately. Paint regeneration after new data, accent/threshold changes,
style changes and rendering resume reapplies the filter. No paint/filter is
allocated on unchanged ticks. The 0.65 sigma applies equally on both axes.
Threshold alarm sections and interactive overlays remain sharp.

Direction labels now use the heavier U+2B07/U+2B06 arrows and bold font weight on
horizontal and vertical taskbars. Ordinary names retain their normal font weight.
Tests verify the updated mappings. Native visual strength remains unverified by
automation; check the result on screen at the user's display scale.

## Independent blur and vector direction icons

Blur Graph is now a separate toggle below Graph Background Source. It applies the
same 0.65-sigma Gaussian to the plotted line and fill with Calculated, Transparent
or Acrylic backgrounds. Changing the background never changes the blur toggle.
Taskbar and flyout graph view models receive both settings independently; labels
and values stay sharp. Fresh settings default blur off. The nullable persisted
TaskbarBlurGraph property distinguishes an explicit choice from an old file:
legacy Blurred (and the previously migrated Mica) becomes Transparent with blur on.
An explicit saved blur setting always wins. Old enum names remain readable but
are no longer dropdown choices. Migration and precedence have 16 dedicated checks.

Direction arrows are drawn paths rather than font glyphs: an 8 by 10 DIP display box with a 1.8-DIP round stroke, scaled uniformly
from the original stem and wide arrowhead geometry. Horizontal graph labels reserve 12 DIP so
the adjacent numeric value cannot collapse the arrowhead. Side-taskbar names
switch between text and the same vector inside one layout slot, preserving the
separate sensor-source icon. Original text labels and sensor identity are intact.

Full build, publish, setting tests and manual visual checks are required for this
update. Automated screen control remains unavailable; visual arrow clipping,
rapid background switching and all background/blur combinations need on-screen
verification. Existing historical validation above describes earlier revisions.

Independent blur/vector revision: full x64 Release build, FolderProfile publication, 19 material migration checks, 16 independent blur checks, 7 direction mapping checks, whitespace and XML/resource validation passed. Existing compiler and PRI263 warnings remain. Native visual checks remain outstanding.

Arrow size refinement: reduced both taskbar orientations from 10 by 12 to 8 by 10 DIP, with a 1.8 DIP stroke. This retains the wide vector arrowhead and reduces reserved horizontal space to 12 DIP. Visual size still requires on-screen confirmation.

Taskbar value text now uses SemiBold in both horizontal graph overlays and side-taskbar value labels; numeric format, unit sizing and values remain unchanged.
