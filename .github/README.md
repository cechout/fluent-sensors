<img width="2560" height="810" alt="frame9" src="https://github.com/user-attachments/assets/d5a4833b-6f2e-45d0-87fc-de07be210cd0" />


###

There aren't many hardware monitoring tools that actually look native on Windows 11. Fluent Sensors is an attempt to fix that, showing sensor data, CPU, GPU, RAM, temperatures, clocks, fans, in a clean, native Fluent Design interface.

## ✨ Features

* **Sensors Page:** Shows every sensor found, with the option to pin sensors to a separate, always-visible widget window or to the taskbar. Any sensor can get a threshold that colors it once its value crosses a limit.
* **Hardware View Page:** Shows every hardware component LibreHardwareMonitorLib finds as its own tab, so multiple CPUs, GPUs, or drives each get their own tab. Each tab shows the most important graphs for that component, plus static info like cache size, RAM speed, or storage type.
* **CSV Logging:** Records the sensors you pick to a CSV file, for spreadsheets or any other tool.
* **The Engine (LibreHardwareMonitorLib):** All sensor data, CPU, GPU, RAM, storage, network, is read using the open source [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) library, which reaches the hardware through the [PawnIO](https://pawnio.eu) driver. Note: this library has some limitations and can struggle to read certain sensors, like the ones from integrated graphics cards.
* **The Interface (WinUI 3):** Built with [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) and the [Windows App SDK](https://github.com/microsoft/WindowsAppSDK), so it looks and behaves like a native Windows 11 app.
* **The Graphs (LiveCharts2 & SkiaSharp):** Sensor graphs are rendered with [LiveCharts2](https://github.com/Live-Charts/LiveCharts2), which runs on [SkiaSharp](https://github.com/mono/SkiaSharp).

## 🔧 Performance

How it currently looks performance-wise:
* **WinUI 3 memory leaks:** WinUI 3 has known platform-level memory leaks, for example [secondary windows not fully releasing after closing](https://github.com/microsoft/microsoft-ui-xaml/issues/9063). Fluent Sensors works around these by hiding and reusing windows instead of destroying them.
* **General optimization:** WinUI 3 is not the fastest UI framework, so manual optimization work is ongoing.

## 📦 Download

* **[Microsoft Store](https://apps.microsoft.com/detail/9PK7F87MWXKF):** installs and updates through the Store. Windows 11 only.
* **Installer** (`FluentSensors_Installer.exe`): installs into `Program Files` with a start menu entry and an uninstaller.
* **Portable** (`FluentSensors_Portable_<version>.zip`): unzip anywhere and run `FluentSensors.exe`. Settings stay in a `Persistence` folder next to it, so deleting the folder removes every trace.

Installer and portable are on the [releases page](https://github.com/cechout/fluent-sensors/releases), run on Windows 10 as well and update themselves from inside the app. Every build is x64 and needs administrator rights to read the hardware. CPU temperatures, motherboard sensors and RAM timings also need the [PawnIO](https://pawnio.eu) driver, which is installed separately; without it everything else still works.

## 🔒 Privacy

The app collects nothing, has no telemetry and no account. It only goes online for updates: to check whether a newer version exists, on GitHub or in the Microsoft Store, and to load the release notes. One switch in the settings keeps it from going online on its own. [PRIVACY.md](PRIVACY.md) explains exactly what is sent and when.

## 🛠️ How to Build

### 1. Prerequisites
To build and run this project, it is highly recommended to use **Visual Studio 2026**, since the project targets .NET 10.
Before opening the solution, make sure you have the following workloads installed via the **Visual Studio Installer**:

* **.NET desktop development**
* **WinUI application development** (Make sure that ".NET WinUI app development tools" is checked in the optional components on the right side).

### 2. Clone the Repository
```ps
git clone https://github.com/cechout/fluent-sensors.git
```

### 3. Build and Run
* Start Visual Studio **as administrator**. The app requires elevation, so a Visual Studio without it cannot launch it.
* Open `FluentSensors.slnx`.
* In the top toolbar, set the Solution Platform to `x64` and the launch profile to `FluentSensors (Unpackaged)`. *Note: WinUI 3 projects do not support 'Any CPU' builds.*
* Press `F5` to build and run the application.

Building from the command line needs `msbuild.exe` instead of `dotnet build`, because a COM reference in the project is only supported by the full MSBuild. [AGENTS.md](../AGENTS.md) has the exact commands.

And now you're good to go!
