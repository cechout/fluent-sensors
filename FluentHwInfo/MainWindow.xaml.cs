using FluentHwInfo.Views;
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

namespace FluentHwInfo
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            // das sorgt dafür, dass direkt beim start der app das erste item (Dashboard) 
            // ausgewählt ist und der rahmen nicht leer bleibt.
            nvSample.SelectedItem = nvSample.MenuItems[0];
        }

        // diese methode feuert jedes mal, wenn du links auf ein icon klickst
        private void nvSample_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // wir greifen uns das angeklickte item
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                // wir holen uns das stichwort (z.b. "DashboardPage")
                string pageTag = selectedItem.Tag.ToString();

                // je nach stichwort laden wir die passende seite in den frame
                switch (pageTag)
                {
                    case "CPU":
                        // typeof() sagt dem frame genau, welche klasse er laden soll
                        contentFrame.Navigate(typeof(CPUMonitoring));
                        break;

                    case "GPU":

                        // typeof() sagt dem frame genau, welche klasse er laden soll
                        //contentFrame.Navigate(typeof(GPUMonitoring));
                        break;

                    case "Settings":
                        // contentFrame.Navigate(typeof(SettingsPage)); // erst einkommentieren, wenn die page existiert
                        break;
                }
            }
        }
    }
}
