# Third-party notices

The optional tray-adjacent placement logic in `WinTaskbarTrayTracker.cs`,
`TaskbarWidgetPlacement.CalculateNearTray`, and the divider sizing in
`TaskbarWidgetWindow.TrayPlacement.cs` is adapted from
[busy](https://github.com/aivanyuk/busy), commit
`ebef0832f8b5e2c6a78aaf8266b39b313354453f`, particularly
`app/src/taskbar/explorer.rs`, `app/src/taskbar/tasks.rs`, and `app/src/taskbar/mod.rs`.
It is adapted to C# and Fluent Sensors' existing WinUI widget; no Busy telemetry,
settings UI, rendering engine, or updater is incorporated.

## busy — MIT License

Copyright (c) 2026 busy contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
