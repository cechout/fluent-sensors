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
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.ApplicationSettings;
using WinUIEx;

namespace FluentHwInfo
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            MainNavigationView.SelectedItem = MainNavigationView.MenuItems[0]; // this ensures that right at the start of the app, the first item in the navigation view is already selected

            //AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;

            if (AppWindow.TitleBar.ExtendsContentIntoTitleBar)
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
                AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            }

            // set the start size of the whole app window
            this.SetWindowSize(660, 400);

            // set the min size of whole app window
            var manager = WinUIEx.WindowManager.Get(this);
            manager.MinWidth = 660;
            manager.MinHeight = 400;
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
