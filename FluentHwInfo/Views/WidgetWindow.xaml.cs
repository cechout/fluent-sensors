using FluentHwInfo.ViewModels;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;

namespace FluentHwInfo.Views
{
    public sealed partial class WidgetWindow : Window
    {
        private AppWindow _appWindow;

        // Expose the ViewModel so {x:Bind} in XAML can access it.
        public WidgetViewModel ViewModel { get; }

        public WidgetWindow()
        {
            ViewModel = new WidgetViewModel();

            this.InitializeComponent();

            // custom window settings
            _appWindow = this.AppWindow;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar); 
            _appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            PositionWidgetTopRight(); // custom method to position window at top-right of the screen

            // register the closed event to prevent memory leaks in the background service
            this.Closed += WidgetWindow_Closed;
        }

        private void WidgetWindow_Closed(object sender, WindowEventArgs args)
        {
            // detach the event handlers from the static HardwareMonitorService
            ViewModel.Cleanup();
        }

        private void PositionWidgetTopRight()
        {
            // get the size of the primary screen
            var displayArea = DisplayArea.Primary;
            int screenWidth = displayArea.WorkArea.Width;

            int widgetWidth = 1000;
            int widgetHeight = 600;

            // move the window to the right edge (with 10px margin)
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                screenWidth - widgetWidth - 10,
                10,
                widgetWidth,
                widgetHeight));
        }

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            // App.MainWindow.Activate();
            this.Close();
        }
    }
}