<img width="2560" height="810" alt="frame" src="https://github.com/user-attachments/assets/f32d7421-bc9b-4619-95ab-ee8088829a16" />


###
Reading hardware sensors often results in cluttered and old-looking interfaces. This project aims to change that. 

FluentHwInfo is a hardware monitoring application built with **WinUI 3**. The goal is to display deep system diagnostics (like CPU, GPU, RAM, and thermals) in a clean, native Windows 11 user interface.

## ⚙️ Under the Hood
The application is built with a focus on clean architecture and native Windows 11 integration.

* **The Interface (WinUI 3):** Built using the Windows App SDK to provide a native, modern user interface that strictly follows the Fluent Design system guidelines.
* **The Architecture (MVVM):** Structured using the Model-View-ViewModel pattern to completely decouple the frontend UI from the background logic.
* **The Engine (LibreHardwareMonitor):** The core hardware polling relies on the `LibreHardwareMonitorLib` package, which handles the heavy lifting of reading the low-level system sensors (CPU, GPU, Mainboard, Memory, Network, etc.).


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
