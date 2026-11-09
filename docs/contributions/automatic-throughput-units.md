# Automatic throughput units

## Contribution

Upstream: https://github.com/cechout/fluent-sensors

Base commit: `e4889379f2c7019ec2beb635fb981cbac3f57300` (version 1.8.0).
Branch: `fix/automatic-throughput-units`.

Small transfer rates previously remained in MB/s and rounded to `0.0 MB/s`.
Byte-based throughput now displays KB/s below 1 MB/s, then returns to MB/s as traffic
increases. The existing GB/s boundary is preserved. Examples:

| Stored rate (MiB/s) | Display |
| --- | --- |
| 0 | 0.0 KB/s |
| 0.0009765625 | 1.0 KB/s |
| 0.25 | 256.0 KB/s |
| 1 | 1.0 MB/s |
| 2.4 | 2.4 MB/s |
| 1000 | 1.0 GB/s |

The existing app labels binary units as KB/MB, so the down-conversion uses 1024.
One decimal place and regional number formatting are preserved. Very small rates
can still round to `0.0 KB/s`; this contribution does not add a B/s tier.

## Scope and invariants

The shared `SensorUnitFormatter.Scale` drives the sensor list, current/min/max/average
readouts, widgets, taskbar, graph maximum labels and network performance readouts.
This also affects storage throughput sensors because they use the same sensor type.
Graph hover labels include the throughput unit, since an individual sample and the
maximum of the graph can use different units.

Raw samples remain in MiB/s. CSV units and values, threshold persistence, manual
axis settings, graph history and sensor selection formats do not change. Bit-based
speeds keep their existing Mbit/s and Gbit/s behavior. No dependencies or release
version numbers change.

## Reproduce and validate

Run from the repository root, saving command output under `logs/`:

```powershell
dotnet run --project tests/ThroughputFormatting/ThroughputFormatting.csproj -c Release
dotnet restore FluentSensors/FluentSensors.csproj -p:Platform=x64 -r win-x64 -p:SelfContained=true
msbuild FluentSensors/FluentSensors.csproj -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true
msbuild FluentSensors/FluentSensors.csproj -t:Publish -p:Configuration=Release -p:Platform=x64 -p:PublishProfile=FolderProfile -p:NoBuild=true
git diff --check
```

Use Visual Studio's full `msbuild.exe`, as required by [AGENTS.md](../../AGENTS.md).
The standalone check links the actual formatter sources without loading WinUI or
hardware drivers. It covers 21 assertions: zero, small values, both sides of the
KB/MB boundary, downward transitions, the existing GB boundary, graph scaling,
raw/CSV units, threshold conversion, bits, unrelated sensor types and decimal commas.

Manual acceptance: open a network throughput sensor, pin it to the taskbar and a
floating widget, observe idle traffic, download enough to cross 1 MB/s, then stop.
Check graph hover and maximum labels, restart with existing selections, and confirm
the units switch back down. Also inspect a storage-throughput sensor and a CSV export.
Taskbar slots may omit a unit if there is insufficient room, per existing layout logic.

## Local installation and rollback

Keep the custom build identifiable by this branch and document; the application
version remains upstream's 1.8.0. An official update can replace this local patch.
Do not automatically disable the user's chosen update settings.

The local update helper must back up the complete installed directory and local
settings before replacing files. It must verify copied hashes and restore backed-up
files on failure. Keep rollback artifacts outside the tracked contribution, under
`artifacts/`, and operational logs under `logs/`.

The helper is [tools/Update-LocalInstallation.ps1](../../tools/Update-LocalInstallation.ps1).
Run it from an administrator PowerShell after the build and publish above:

```powershell
.\tools\Update-LocalInstallation.ps1
```

It targets only `C:\Program Files\Fluent Sensors`, backs up all existing files and
`%LOCALAPPDATA%\FluentSensors`, stops only the process at that installed executable
path, copies and hash-checks the complete published payload, and restarts it. A
startup failure restores original application files. No settings are replaced.
The helper writes a timestamped JSON result under `logs/`.

To restore the pre-change application files from an administrator PowerShell:

```powershell
.\tools\Update-LocalInstallation.ps1 -RestoreFrom '.\artifacts\installation-backups\20261106-035836-631'
```

Rollback makes another backup first and leaves the current settings intact. The
saved `settings` subfolder is available for manual recovery if separately needed.
Restore copies original files back; additional payload files are not deleted.

## Validation record

- Formatter checks: 21 passed on 2026-11-06.
- Full Release build and self-contained FolderProfile publish: passed.
- Changed application-file whitespace check and `git diff --check`: passed.
- Compiler warnings were present outside the modified code (COM marshaling,
  nullability, duplicate using directives, a stackalloc analyzer finding, and
  `Terms/Network_LinkSpeed` without a neutral resource fallback). These were not
  changed as part of this formatting fix.
- Installed on 2026-11-06 at `C:\Program Files\Fluent Sensors`; all published file
  hashes were verified after copying. The restarted app stayed running through
  the ten-second startup check. Installed assembly SHA-256:
  `A940F5F620C1C7E8B3492B56A949B521516B4D205D20F1F9ED8E0807184E5DDC`.
- Original app and settings backup: `artifacts/installation-backups/20261106-035836-631`.
- Visual inspection and the manual acceptance sequence above remain unverified;
  the desktop-control runtime returned
  `failed to write kernel assets: The system cannot find the path specified`.
- No upstream push or pull request has been made.
