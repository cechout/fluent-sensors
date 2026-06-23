<img width="2560" height="810" alt="frame2" src="https://github.com/user-attachments/assets/4609864d-065d-4cfe-bc28-a97d35664f65" />

###
Reading hardware sensors often results in cluttered and old-looking interfaces. This project aims to change that. 

FluentHwInfo is a hardware monitoring application built with **WinUI 3**. The goal is to display deep system diagnostics (like CPU, GPU, RAM, and thermals) in a clean, native Windows 11 user interface.

## ⚙️ Under the Hood
The app is built with a straightforward architecture and native Windows 11 integration in mind.

* **The Interface (WinUI 3):** Built with the Windows App SDK to get the standard Windows 11 Fluent Design look.
* **The Architecture (MVVM):** Uses the Model-View-ViewModel pattern to keep the UI completely separate from the background code.
* **The Engine (LibreHardwareMonitor):** Uses the `LibreHardwareMonitorLib` package to read system sensors (CPU, GPU, Memory, etc.). *Note: This library currently has some limitations and might struggle to read some sensors like the ones from the integrated graphics cards (iGPUs).*
* **The Graphs (LiveCharts2 & SkiaSharp):** The visual sensor graphs are rendered using `LiveCharts2`, which runs on `SkiaSharp` to draw the graphs smoothly.


## 🛠️ How to Run

### 1. Prerequisites
To build and run this project, it is highly recommended to use **Visual Studio 2022** (Version 17.0 or later). 
Before opening the solution, make sure you have the following workloads installed via the **Visual Studio Installer**:

* **.NET Desktop Development**
* **Windows application development** (Make sure that the "Windows App SDK C# Templates" are checked in the optional components on the right side).

### 2. Clone the Repository
```ps
git clone https://github.com/cechout/fluent-hwinfo.git
```

### 3. Build and Run
* Open the solution file in Visual Studio.
* Right-click on the Solution in the Solution Explorer and select **Restore NuGet Packages** (Visual Studio usually does this automatically on the first build).
* Right-click on the `FluentHwInfo` project in the Solution Explorer and select `Set as Startup Project`.
* In the top toolbar, change the Solution Platform from `Any CPU` to your specific system architecture (e.g., `x64`). *Note: WinUI 3 projects do not support 'Any CPU' builds.*
* Press `F5` to build and run the application.

And now you're good to go!
