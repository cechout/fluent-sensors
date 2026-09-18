# 🔒 Privacy

Fluent Sensors reads the sensors in your own machine and shows them. There is no account, no sign-in, no telemetry and no analytics. Your settings and your sensor readings stay on your machine.

The app does go online for one thing: it asks GitHub whether a newer version exists, and it loads the release notes from there. This page explains when that happens and what is sent.

## 📁 What is stored, and where

Everything the app remembers is written to your machine as plain JSON files that you can open, copy or delete:

* **Installer build:** `%LocalAppData%\FluentHwInfo`
* **Portable build:** a `Persistence` folder next to `FluentSensors.exe`

These files hold your settings, your window sizes and positions, and which sensors you picked. Nothing else is written, and none of it is ever uploaded.

## 🌐 When the app goes online

The app only ever contacts `api.github.com`, and only in these three cases:

* **On startup**, to ask whether a newer release exists. This can be turned off, see below.
* **When you open the release notes**, to load the list of published versions. They are saved on your machine afterwards, so this only happens when a version is missing from that copy.
* **When you confirm an update**, to download the new build. Nothing is downloaded before you press the update button.

Each of these is a plain read request. The app sends nothing along with it: no version number, no machine name, no hardware list, no sensor readings, nothing that names you.

GitHub does see the same things every website sees when you visit it: your IP address, the time, which address was asked for, and the name "FluentSensors" as the user agent. What GitHub does with that is covered by the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement).

Please note that an IP address counts as personal data in the EU. That is why this page exists, even though the app itself collects nothing.

## ⚙️ How to keep the app offline

Open **Settings**, expand **Startup**, and turn off **Check for updates on startup**. With that switch off the app never goes online on its own. The release notes then only show the versions that were already saved on your machine.

Two things still work, because you ask for them yourself:

* The update button on the start page still checks when you press it.
* An update is still downloaded and installed when you confirm it.

## 🏪 Microsoft Store version

The Store version never checks for updates and never downloads a new build, because the Store handles updates itself. It does still load the release notes, in the same way and with the same switch as described above.

## 📬 Questions

Open an issue on the [project page](https://github.com/cechout/fluent-sensors/issues).
