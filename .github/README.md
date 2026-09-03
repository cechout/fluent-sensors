<img width="2560" height="810" alt="frame9" src="https://github.com/user-attachments/assets/d5a4833b-6f2e-45d0-87fc-de07be210cd0" />


###

There aren't many hardware monitoring tools that actually look native on Windows 11. Fluent Sensors is an attempt to fix that, showing sensor data, CPU, GPU, RAM, temperatures, clocks, fans, in a clean, native Fluent Design interface.

## ✨ Features

* **Sensors Page:** Shows every sensor found, with the option to pin sensors to a separate, always-visible widget window or to the taskbar.
* **Hardware View Page:** Shows every hardware component LibreHardwareMonitorLib finds as its own tab, so multiple CPUs, GPUs, or drives each get their own tab. Each tab shows the most important graphs for that component, plus static info like cache size, RAM speed, or storage type.
* **The Engine (LibreHardwareMonitorLib):** All sensor data, CPU, GPU, RAM, storage, network, is read using the open source [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) library. Note: this library has some limitations and can struggle to read certain sensors, like the ones from integrated graphics cards.
* **The Interface (WinUI 3 + MVVM):** Built with the Windows App SDK for the native Windows 11 Fluent Design look, using the Model-View-ViewModel pattern to keep the UI cleanly separated from the background logic.
* **The Graphs (LiveCharts2 & SkiaSharp):** Sensor graphs are rendered with [LiveCharts2](https://github.com/Live-Charts/LiveCharts2), which runs on SkiaSharp.

## 🔧 Performance

How it currently looks performance-wise:
* **Rendering gates:** only currently visible graphs actually render, hidden ones just keep collecting data in the background.
* **WinUI 3 memory leaks:** WinUI 3 has known platform-level memory leaks, for example [secondary windows not fully releasing after closing](https://github.com/microsoft/microsoft-ui-xaml/issues/9063). Fluent Sensors works around these by hiding and reusing windows instead of destroying them.
* **General optimization:** WinUI 3 is not the fastest UI framework, so manual optimization work is ongoing.

## 🛠️ How to Build

### 1. Prerequisites
To build and run this project, it is highly recommended to use **Visual Studio 2022** (Version 17.0 or later). 
Before opening the solution, make sure you have the following workloads installed via the **Visual Studio Installer**:

* **.NET Desktop Development**
* **Windows application development** (Make sure that the "Windows App SDK C# Templates" are checked in the optional components on the right side).

### 2. Clone the Repository
```ps
git clone https://github.com/cechout/fluent-sensors.git
```

### 3. Build and Run
* Open the solution file in Visual Studio.
* Right-click on the Solution in the Solution Explorer and select **Restore NuGet Packages** (Visual Studio usually does this automatically on the first build).
* Right-click on the `FluentSensors` project in the Solution Explorer and select `Set as Startup Project`.
* In the top toolbar, change the Solution Platform from `Any CPU` to `x64`. *Note: WinUI 3 projects do not support 'Any CPU' builds.*
* Press `F5` to build and run the application.

And now you're good to go!
