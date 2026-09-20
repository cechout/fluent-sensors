# FluentSensors Development Guidelines

This project is a C#/.NET 10 WinUI 3 desktop app that reads hardware sensors through
LibreHardwareMonitorLib and shows them in a native Windows 11 interface. It ships unpackaged and
self-contained, and it requires administrator rights because the sensor library loads a kernel driver.

- Keep the work scoped to what was asked. Avoid opportunistic refactors, formatting churn, dependency
  bumps and drive-by renames.
- Read the surrounding code before adding an abstraction. Prefer the MVVM, service and singleton patterns
  that the file already uses.
- Preserve existing comments verbatim when you change the code around them.
- Everything is English: code, comments, commit messages, UI strings and release notes.
- Always follow `.editorconfig`. Text files are LF; `.gitattributes` pins the checkout, and the CI
  `format` job fails on CRLF.
- Prefix types by where their data comes from: `Lhm` for types facing LibreHardwareMonitorLib, `Win` for
  Windows-native ones (WMI, Win32, DXGI).
- All UI updates from background threads go through `DispatcherQueue.TryEnqueue`. `LhmHardwareTreeService`
  already marshals its own mutations to the UI thread, so do not dispatch again in its consumers.
- Check `AppDistribution` before assuming how the app was installed. It is the single source of truth for
  the installer, portable and Microsoft Store channels, and for whether self-update is available at all.
- Treat sensor reading, taskbar embedding, window lifetime and settings persistence as high-risk areas.
- Never translate sensor names, hardware type names or CSV column headers. They come from
  LibreHardwareMonitorLib and are a contract with other tools and with the recorded files.
- Prefer targeted search over full file reads, and cap any command whose output could be large.

## Comment Style

A comment explains what a piece of code does and why it exists, not how the framework works underneath.
Weight scales with how non-obvious the reason is. A helper whose name already says everything needs no
comment at all.

- **Never write XML documentation comments.** No `/// <summary>`, `/// <param>`, `/// <returns>` or
  `/// <inheritdoc/>`, on any member, under any circumstances. Use `//` single-line comments only.
- Section headers inside a class are exactly `// === section name ===`: lowercase, three equals signs, a
  blank line above and below. Never draw banner dividers.
- Group related fields under a `// topic name` or `// --- topic name ---` subheader, with short trailing
  comments on the individual lines.
- Open comments in lowercase and leave off the sentence-ending period. They read as fragments, not
  sentences.
- No apostrophes, neither contractions nor possessives. Write `its`, `thats`, `does not`, `WMIs`.
- No em dashes, and no loose `-` joining two clauses. Use a semicolon, or start a new line.
- Do not restate what the line below already says, and do not narrate the option that was not taken.

Four tags mark code that is not ordinary. Use each only for what it names:

- `// --- workaround: short name ---` for a bug in the platform or a third-party library, never for one
  of ours. State the problem, link the issue or the confirmed repro, then state the fix.
- `// --- memory leak: short name ---` for the WinUI 3 retained-instance pattern, where a window is
  hidden and reused because a real close never releases it.
- `// --- revisit: short name ---` for a deliberate temporary shape that has a named trigger for
  changing it.
- `// KNOWN UNRELIABLE:` for an external data source (WMI, firmware, driver) whose values cannot be
  trusted, paired with `(unreliable)` on the matching UI label.

XAML comments follow the same wording rules and add three of their own. Put two spaces inside the
markers (`<!--  note  -->`). When the text does not fit on one line it becomes a block with the markers
on their own lines, never several stacked one-liners. A double hyphen inside a comment is an XML parse
error, so anomaly tags are written plainly there: `<!--  workaround: short name  -->`.

## Project Structure

```text
FluentSensors/
├── Common/       cross-cutting helpers: Csv, Markdown, Sensors, UI
├── Controls/     reusable controls: InfoPopup, SensorGraph, SensorRow, SensorTile, Threshold
├── Core/         infrastructure: Lhm, Startup, StaticInfo, Taskbar, Update
├── Diagnostics/
├── Features/     one folder per screen or feature, view plus view model and any window it owns:
│                 AppStatus, CsvLogging, Performance, Sensors, Settings, Start, TaskbarWidget,
│                 Update, Widget
├── Persistence/  Models, Services
└── Properties/   PublishProfiles
```

`Setup/` holds the Inno Setup installer script and the MSIX packaging script. `.github/` holds the
workflows, the issue and pull request templates and the public README.

## Build

Build with `msbuild.exe`, never `dotnet build`. `ResolveComReference` exists only in the full .NET
Framework MSBuild, and the COM reference this project needs for UIAutomation makes `dotnet build` fail
with `MSB4803`. Restore through the .NET CLI as usual, then hand the build to `msbuild.exe`.

WinUI 3 has no `Any CPU` configuration, so always pass `-p:Platform=x64`.

```powershell
dotnet restore FluentSensors/FluentSensors.csproj -p:Platform=x64 -r win-x64 -p:SelfContained=true
msbuild FluentSensors/FluentSensors.csproj -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true
```

Publish only through the `FolderProfile` publish profile, and only after a full build pass. The XAML
compiler resolves `x:Bind` against project-local types in `MarkupCompilePass2`, which needs the
`LocalAssembly` that a build produces; publishing a fresh checkout without one fails with `WMC0909` or
`WMC9999`.

```powershell
msbuild FluentSensors/FluentSensors.csproj -t:Publish -p:Configuration=Release -p:Platform=x64 -p:PublishProfile=FolderProfile -p:NoBuild=true
```

## Test

There is no test project. The bar for a change is that the build stays green and that the screen it
touches was opened in a running app and looked at. Report what you did not verify instead of implying
it passed.

## Commit & Push

Run these before committing, and stage only what belongs to the change:

```powershell
git status --short
git diff --check
```

Branches are `feature/`, `fix/`, `chore/`, `refactor/` or `docs/` followed by a short name. Releases use
their own `update/` branch.

A commit message is one short imperative sentence with a lowercase conventional prefix, and it is
exactly one line. No body, no trailers, and no `Co-Authored-By:` line unless authorship was explicitly
asked for.

```text
feat: add a graph fill fade and configurable performance graph time ranges
fix: make the hardware category icons follow the theme switch
refactor: split the settings page into sections
chore: regenerate the package visual assets
build: v1.5.0
```

## Open a PR

Pull requests target `main` and are squash merged, so the pull request title becomes the commit message
on `main` and has to carry the same prefix a hand-written commit would.

Reference issues without closing them, as `Part of #N`. An issue is closed by hand once the change is
released, not by the merge.

Size the description by the diff, and check the length against this ladder before posting:

- A fix with one cause gets one or two bullets and nothing else. No paragraph.
- A small feature, or a fix whose behaviour change would surprise a reader, gets two sentences and up to
  three bullets.
- A branch that adds a subsystem or a distribution channel gets the full shape.

The opening paragraph carries context the diff cannot. When the title and two bullets already carry it,
the heading stands alone above the bullets. Do not argue the fix in the body: why a change is correct
belongs in the code comment, and if it seems worth saying in both places, the comment is what is
missing.

**The prefix decides what reaches users.** Release notes are drafted from `feat:` and `fix:` pull
requests only. `chore:`, `build:`, `docs:` and `refactor:` are left out, because someone downloading the
app cannot notice them.

## Things That Must Not Change

These are load-bearing. Breaking one of them fails at runtime or on a user machine, not in the build.

- **`PublishTrimmed=false` stays.** Trimming strips the WinRT and COM interop types the app needs and it
  crashes on start.
- **Never prune the WinUI `.mui` locale folders.** They look like translations for languages the app does
  not have, but `Microsoft.ui.xaml.dll` keeps its resources only there, and the folder actually loaded is
  the one matching the Windows UI language. Removing them kills the app with `STATUS_STOWED_EXCEPTION`
  the moment the visual tree is built.
- **Deselect Windows App SDK components with `ExcludeAssets`, never by deleting files from the publish
  output.** Every component ships a package fragment that the self-contained targets turn into the
  embedded regfree WinRT manifest. Deleting only the binaries leaves the manifest declaring classes that
  are gone, and the process fails to start with "side-by-side configuration is incorrect".
- **ReadyToRun stays.** It costs real size but buys startup time. Size is not a lever we spend against
  performance.
- **Never bump `<Version>` in a feature branch.** The bump is a release activity and belongs on the same
  commit that carries the tag.
- **Release asset names are a contract with the in-app updater.** `FluentSensors_Installer.exe` and
  `FluentSensors_Portable_<version>.zip`. Renaming one degrades the updater silently instead of breaking
  it loudly.
- **The portable zip keeps its top-level folder.** The updater copies out of that inner directory, so
  flattening the archive breaks every copy of the app already in the field.
- **The release workflow creates a draft, and a human publishes it.** The updater only ever sees
  published, non-draft, non-prerelease releases. Publishing straight from CI would ship every tag to
  every running install the moment the build finishes.
