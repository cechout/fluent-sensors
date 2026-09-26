# 🔒 Privacy

Fluent Sensors reads the sensors in your own machine and shows them. There is no account, no sign-in, no telemetry and no analytics. Your settings and your sensor readings stay on your machine.

The app does go online for one thing: updates. It checks whether a newer version exists and loads the release notes. This page explains when that happens, where the request goes and what is sent.

## 📁 What is stored, and where

Everything the app remembers is written to your machine as plain JSON files that you can open, copy or delete:

* **Installer build:** `%LocalAppData%\FluentSensors`
* **Portable build:** a `Persistence` folder next to `FluentSensors.exe`
* **Microsoft Store build:** the packages own `LocalState` folder under `%LocalAppData%\Packages`, which Windows deletes together with the app when you uninstall it

The app shows you the exact folder it is using: **Settings**, **Backup & Reset**, **App data folder**.

These files hold your settings, your window sizes and positions, and which sensors you picked. Beside them the app keeps a `cache` folder for the release notes it has already loaded, and a `quarantine` folder for files that could not be read any more. Both can be deleted at any time, and none of it is ever uploaded.

## 🌐 When the app goes online

The installer and portable builds only ever contact GitHub, and only in these three cases. The Store build differs slightly, see [Microsoft Store version](#-microsoft-store-version).

* **On startup**, to ask `api.github.com` whether a newer release exists. This can be turned off, see below.
* **When you open the release notes**, to load the list of published versions from `api.github.com`. They are saved on your machine afterwards, so this only happens when a version is missing from that copy.
* **When you confirm an update**, to download the new build from the releases page on `github.com`. Nothing is downloaded before you press the update button.

Each of these is a plain read request. The app sends nothing along with it: no version number, no machine name, no hardware list, no sensor readings, nothing that names you.

GitHub does see the same things every website sees when you visit it: your IP address, the time, which address was asked for, and the name "FluentSensors" as the user agent. What GitHub does with that is covered by the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement).

Please note that an IP address counts as personal data in the EU. That is why this page exists, even though the app itself collects nothing.

## ⚙️ How to keep the app offline

Open **Settings**, expand **Startup**, and turn off **Check for updates on startup**. With that switch off the app never goes online on its own.

Three things still work, because you ask for them yourself:

* The update button on the start page still checks when you press it.
* Opening the release notes loads the published versions, but only when the copy saved on your machine is missing one, and at most once per app start.
* An update is still downloaded and installed when you confirm it.

## 🔌 The sensor driver

Reading CPU temperatures, motherboard sensors and RAM timings needs kernel level access, which no ordinary program has. Fluent Sensors gets it through [PawnIO](https://pawnio.eu), an open source, digitally signed driver that installs into the Windows driver store and is not bundled with this app. The app only talks to a driver that is already on your machine, it never installs one, and nothing it reads through that driver ever leaves your machine.

Without PawnIO the app still runs and still shows GPU, storage, network and memory. Only the readings that need kernel access are missing.

## 🏪 Microsoft Store version

The Store version asks the Microsoft Store whether an update exists, in the same situations and behind the same switch as above. It still reads the latest release from `api.github.com`, but only to name the new version and show its notes. When you confirm an update, the Store downloads and installs it; nothing comes from GitHub.

The Store check runs through the Store service built into Windows, which knows which version is installed, the same as for every Store app. What Microsoft does with that is covered by the [Microsoft Privacy Statement](https://privacy.microsoft.com/privacystatement). The Store can also update the app in the background on its own, as it does for all Store apps; that is a setting of the Microsoft Store, not of Fluent Sensors.
