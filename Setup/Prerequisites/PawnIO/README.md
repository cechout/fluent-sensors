# PawnIO installer prerequisite

Run `./tools/Prepare-PawnIO.ps1` before compiling `Setup/Inno_Setup.iss`.
The release workflow runs it automatically. The executable is downloaded at build
time, ignored by Git, and embedded unchanged in the Fluent Sensors installer.
Installation needs no network connection.

- Version: 2.2.0
- Official binary: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- SHA256: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`
- Authenticode publisher: namazso.eu (the preparation script requires a valid signature).
- Redistribution: the official setup executable states, "This installer can be redistributed unmodified."
- Driver source and licensing: https://github.com/namazso/PawnIO
- Developer website: https://pawnio.eu/

Setup offers hardware sensor support, selected by default when PawnIO is missing,
older than 2.2.0, or has incomplete registration/files. It executes the official
signed installer using `-install -silent`, without the unrestricted edition flag.
The extracted payload is checked against the pinned SHA256 before execution.
A complete installation of 2.2.0 or newer is preserved. Detection checks the
64-bit uninstall registration, version, uninstaller, library file and driver service.
A stopped driver alone does not mean it needs reinstalling: it can start on demand.

The prerequisite runs before Fluent Sensors files are changed. Error codes stop
setup with an explanation and are recorded in the Inno `/LOG` output. Code 3010
requests a restart and suppresses the launch checkbox until the next app start.
Setup never restarts Windows itself when `/NORESTART` is specified. Uninstalling
Fluent Sensors leaves PawnIO installed because other hardware monitors may use it.
Old PawnIO editions which the official installer refuses to upgrade require
repair/removal through PawnIO's own installer; Fluent Sensors does not forcibly
remove a shared driver.

The portable ZIP and Microsoft Store package do not contain this prerequisite;
those users still install PawnIO separately. Local source runs also need it.
This change affects installer packaging, not the sensor engine or sensor filtering.

When updating the prerequisite, update the version/hash in both
`tools/Prepare-PawnIO.ps1` and `Setup/Inno_Setup.iss`, and verify the official
release checksum, signature and redistribution terms again.

Before release, check a missing/incomplete installation, a current installation
(skipped), explicit opt-out, and installation failure. Test restart-required
behavior on a disposable Windows machine when that exit code can be reproduced.

## Local validation

On 2026-11-09, the bundled installer was tested against an incomplete PawnIO
installation (stopped driver service, no uninstall registration or program files).
The elevated sensor probe returned no numeric CPU temperatures before setup or
after opting out. After setup it returned 35 numeric temperature sensors, including
CPU Package, Core Max, Core Average and individual cores, as well as package power.
The installed Fluent Sensors sensor list was opened and CPU Package/core readings
were visually confirmed. Running setup again skipped the existing prerequisite
and retained all 35 numeric temperature readings. All three installer runs returned
exit code 0. App files and settings were backed up before these installation tests.

A small isolated installer exercised the same prerequisite code with an absent
registration fixture and an intentionally incorrect payload hash. With default
task selection it stopped before driver execution with exit code 7 and a checksum
error; opting out returned 0. The payload used in the real installer passed SHA256
and Authenticode verification. Detailed diagnostic snapshots and installer/compiler
logs are retained locally under `logs/pawnio-*` and excluded from Git.

Restart-required exit 3010, upgrades from older PawnIO versions, and a completely
clean Windows installation remain manual checks. No reboot was needed on this PC.
The application payload was reused from the verified personal 20261109.3 build;
this change modifies setup and release packaging only.

Existing personal releases through `personal-1.8.0-20261109.3` predate this change
and do not bundle PawnIO. Use an installer built from the updated setup script.
