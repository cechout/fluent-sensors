using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using FluentSensors.Core.Startup;


namespace FluentSensors
{
    // the entry point, in place of the one the XAML compiler generates
    public static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // WinUI picks its gpu inside Application.Start, so even the App constructor is too late
            WinGpuPreference.Apply();

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }
}
