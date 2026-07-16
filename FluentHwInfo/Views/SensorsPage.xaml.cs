using FluentHwInfo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentHwInfo.Helpers;
using CommunityToolkit.WinUI.Controls;

namespace FluentHwInfo.Views
{
    public sealed partial class SensorsPage : Page
    {
        public SensorsViewModel ViewModel { get; }
        private static WidgetWindow _currentWidgetWindow = null; // we remember the currently open widget window 
        private int _infoBarTicket = 0;
        private HiddenSensorsWindow _currentHiddenSensorsWindow;


        // constructor
        public SensorsPage()
        {
            this.InitializeComponent();
            ViewModel = SensorsViewModel.Instance; // we bind the UI simply to the central singleton instance
        }


        // user interaction
        private async void PinToWidget_Click(object sender, RoutedEventArgs e)
        {
            // flatten the nested groups and filter for selected items
            // this is LINQ, which is a more concise way to write the same logic as the long form below
            var selectedSensors = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Where(sensor => sensor.IsSelected)
                .ToList();

            // the long form without LINQ would look like this:
            //List<SensorRowViewModel> selectedSensors = new List<SensorRowViewModel>();

            //// 1. go through each hardware group "the boxes"
            //foreach (var group in ViewModel.HardwareGroups)
            //{
            //    // 2. go through each sensor in this group (the contents of the box); that is what is known as "flattening"
            //    foreach (var sensor in group.Sensors)
            //    {
            //        // 3. check if the checkbox is set; that is the ".Where"
            //        if (sensor.IsSelected == true)
            //        {
            //            // 4. pack it in our final list; that is the ".ToList()"
            //            selectedSensors.Add(sensor);
            //        }
            //    }
            //}

            // show flyout when no sensor was selected
            if (selectedSensors.Count == 0)
            {
                _infoBarTicket++;
                int currentTicket = _infoBarTicket;

                // show inforbar
                AnimateInfoBar(0, true);

                await Task.Delay(2000);

                if (currentTicket == _infoBarTicket)
                {
                    // hide infobar
                    AnimateInfoBar(100, false);
                }
                return;
            }

            // if the window is already open, we force it to close
            // the null-conditional operator (?.) only calls Close() if it is not null
            _currentWidgetWindow?.Close();

            // we rebuild build the window completely fresh with the new data in any case
            _currentWidgetWindow = new WidgetWindow(selectedSensors);
            // the journey of selectedSensors: SensorsPage (View) -> WidgetWindow (View) -> WidgetViewModel (ViewModel)

            // we let the new WidgetWindow instance register a closed event handler to itself
            // why do we do this? because if the user manually closes the widget window, this _currentWidgetWindow variable
            // would still point to the old (now closed) window
            // it the user would then click on "Pin to Widget" again, the app would think the old window is still open and
            // would try to close it again, which would throw an exception because the old window is already closed
            _currentWidgetWindow.Closed += (s, args) =>
            {
                _currentWidgetWindow = null;
            };

            _currentWidgetWindow.Activate();
        }
        // InfoBar animation
        private void AnimateInfoBar(double targetY, bool isHitTestVisible)
        {
            NoSensorsInfoBar.IsHitTestVisible = isHitTestVisible;

            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

            var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, InfoBarTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "Y");

            sb.Children.Add(animY);
            sb.Begin();
        }

        private void ResetMinMax_Click(object sender, RoutedEventArgs e)
        {
            // we iterate through all nested groups and all sensors
            foreach (var group in ViewModel.HardwareGroups)
            {
                foreach (var sensor in group.Sensors)
                {
                    sensor.ResetMinMax();
                }
            }
        }

        private async void HideSensors_Click(object sender, RoutedEventArgs e)
        {
            // check across all groups whether anything is selected at all
            bool anySelected = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Any(sensor => sensor.IsSelected);

            // show the same "nothing selected" flyout as PinToWidget_Click
            if (!anySelected)
            {
                _infoBarTicket++;
                int currentTicket = _infoBarTicket;

                AnimateInfoBar(0, true);

                await Task.Delay(2000);

                if (currentTicket == _infoBarTicket)
                {
                    AnimateInfoBar(100, false);
                }
                return;
            }

            ViewModel.HideSelectedSensors();
        }

        private void ShowHiddenSensors_Click(object sender, RoutedEventArgs e)
        {
            // if a window is already open, force it to close and rebuild fresh, same pattern as the widget window
            _currentHiddenSensorsWindow?.Close();

            _currentHiddenSensorsWindow = new HiddenSensorsWindow();

            _currentHiddenSensorsWindow.Closed += (s, args) =>
            {
                _currentHiddenSensorsWindow = null;
            };

            _currentHiddenSensorsWindow.Activate();
        }


        // helper method to fix the rendering of the items
        private void SettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsExpanderRepaintFix.Attach((SettingsExpander)sender);
        }
    }
}