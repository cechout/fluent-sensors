<img width="2560" height="810" alt="frame8" src="https://github.com/user-attachments/assets/760155b4-6a3e-4915-849c-4d2dc4856b77" />


###

There aren't many hardware monitoring tools that actually look native on Windows 11. FluentSensors is an attempt to fix that, showing the same deep sensor data, CPU, GPU, RAM, temperatures, clocks, fans, in a clean, native Fluent Design interface.

## ✨ Features

* **The Engine (LibreHardwareMonitorLib):** Reads all sensors, CPU, GPU, RAM, storage, network, using the open source [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) library. Note: this library has some limitations and can struggle to read certain sensors, like the ones from integrated graphics cards (iGPUs).
* **The Interface (WinUI 3 + MVVM):** Built with the Windows App SDK for the native Windows 11 Fluent Design look, using the Model-View-ViewModel pattern to keep the UI cleanly separated from the background logic.
* **Sensors Page:** Shows every sensor found, with the option to pin your most important ones to a separate, always-visible widget window.
* **Hardware View Page:** Shows every hardware component LibreHardwareMonitorLib finds as its own tab, so multiple CPUs, GPUs, or drives each get their own tab. Only RAM is grouped into a single tab. Each tab shows the most important graphs for that component, plus static info like cache size, RAM speed, or storage type.
* **The Graphs (LiveCharts2 & SkiaSharp):** Sensor graphs are rendered with `LiveCharts2`, which runs on `SkiaSharp` for smooth drawing.


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
* In the top toolbar, change the Solution Platform from `Any CPU` to your specific system architecture (e.g., `x64`). *Note: WinUI 3 projects do not support 'Any CPU' builds.*
* Press `F5` to build and run the application.

And now you're good to go!
