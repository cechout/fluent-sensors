using FluentHwInfo.Services;
using FluentHwInfo.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.ApplicationSettings;
using WinUIEx;

namespace FluentHwInfo
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow CurrentInstance { get; private set; } // we save here the current instance of the MainWindow class


        // constructor
        public MainWindow()
        {
            this.InitializeComponent();
            this.AppWindow.SetIcon("Assets\\Icon\\Icon.ico");

            // instance management
            CurrentInstance = this;
            this.Closed += (s, args) =>
            {
                FluentHwInfo.Services.SettingsService.Instance.ThemeChanged -= OnThemeChanged;
                CurrentInstance = null;
            };

            // this ensures that right at the start of the app, the first item in the navigation view is already selected
            // MainNavigationView.SelectedItem = MainNavigationView.MenuItems[0];
            // for now we just leave it unselected

            // AppWindow configuration
            // theme 
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;

            if (AppWindow.TitleBar.ExtendsContentIntoTitleBar)
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
                AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            }
            // size
            this.SetWindowSize(700, 700);
            var manager = WinUIEx.WindowManager.Get(this);
            manager.MinWidth = 600;
            manager.MinHeight = 400;

            FluentHwInfo.Services.SettingsService.Instance.ThemeChanged += OnThemeChanged;
            ApplyTitleBarTheme(FluentHwInfo.Services.SettingsService.Instance.AppTheme);

            // add the loaded event handler to the content of the window
            ((FrameworkElement)this.Content).Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // load the HardwareMonitorService singleton instance asynchronously
            await StartHardwareServiceAsync();
        }

        private async Task StartHardwareServiceAsync()
        {
            var monitor = FluentHwInfo.Services.HardwareMonitorService.Instance;

            // scan motherboard
            LoadingStatusText.Text = "Initializing motherboard...";
            LoadingProgressBar.Value = 25;
            await monitor.InitMotherboardAsync();
            // await Task.Delay(4000000);

            // scan CPU
            LoadingStatusText.Text = "Scanning CPU...";
            LoadingProgressBar.Value = 50;
            await monitor.InitCpuAsync();

            // scan GPU
            LoadingStatusText.Text = "Scanning GPU...";
            LoadingProgressBar.Value = 75;
            await monitor.InitGpuAsync();

            // scan memory and storage
            LoadingStatusText.Text = "Checking memory and storage...";
            LoadingProgressBar.Value = 100;
            await monitor.InitMemoryAndStorageAsync();

            // no we start the HardwareMonitorService loop manually
            monitor.StartMonitoring();

            // we explicitly wait until the ViewModel has received and processed the very first data payload
            LoadingStatusText.Text = "Waiting for data...";
            await FluentHwInfo.ViewModels.SensorsViewModel.Instance.WaitForInitialLoadAsync();

            // now we are finished loading
            LoadingStatusText.Text = "Ready";
            await Task.Delay(500);

            // show the main grid
            
            MainNavigationView.Visibility = Visibility.Visible;

            // manually close navigation pane
            this.DispatcherQueue.TryEnqueue(() =>
            {
                MainNavigationView.IsPaneOpen = false;
            });
            await Task.Delay(200);

            SplashOverlay.Visibility = Visibility.Collapsed;
            MainNavigationView.SelectedItem = MainNavigationView.MenuItems[0];
        }


        // theme change handling
        private void OnThemeChanged(string newTheme)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTitleBarTheme(newTheme);
            });
        }

        private void ApplyTitleBarTheme(string themeTag)
        {
            AppWindow.TitleBar.PreferredTheme = themeTag switch
            {
                "Light" => Microsoft.UI.Windowing.TitleBarTheme.Light,
                "Dark" => Microsoft.UI.Windowing.TitleBarTheme.Dark,
                _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode
            };
        }

        // this method is called whenever an item in the navigation view is clicked
        private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // checks if native settings item got clicked
            if (args.IsSettingsSelected)
            {
                //contentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            // In a menu like this, you could theoretically click on simple separators or plain headings as well
            // the event fires on everything and simply returns the object as a completely generic, unnamed "object"
            // thats why we check if the clicked item is actually a NavigationViewItem
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                string pageTag = selectedItem.Tag.ToString(); // we pull the value of the tag of the selected item
                switch (pageTag)
                {
                    case "Sensors":
                        // typeof() specifies the class that the frame should load
                        contentFrame.Navigate(typeof(SensorsPage));
                        break;

                    case "Settings":
                        contentFrame.Navigate(typeof(SettingsPage)); 
                        break;
                }
            }
        }
    }
}
