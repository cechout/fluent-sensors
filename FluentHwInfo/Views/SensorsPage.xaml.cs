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
        private int _infoBarTicket = 0;
        private HiddenSensorsWindow _currentHiddenSensorsWindow;

        // for control button prority ordering and overflow handling
        private ICommandBarElement[] _commandBarPriorityOrder;
        private readonly Dictionary<ICommandBarElement, double> _commandBarButtonWidths = new();
        private bool _commandBarWidthsCached = false;
        private const double OverflowButtonReservedWidth = 48;
        private const double HeaderSpacingBuffer = 64;
        private int _commandBarOverflowStartIndex = -1; // -1 means "not computed yet" so the very first call always applies once


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

            // if a widget is already open, whether pinned earlier this session or auto-restored on app
            // launch, we force it to close
            // WidgetWindow.CurrentInstance is the single source of truth for this, it sets and clears itself in the
            // WidgetWindow constructor/Closed handler
            WidgetWindow.CurrentInstance?.Close();

            // we rebuild the window completely fresh with the new data in any case
            var widgetWindow = new WidgetWindow(selectedSensors);
            // the journey of selectedSensors: SensorsPage (View) -> WidgetWindow (View) -> WidgetViewModel (ViewModel)

            widgetWindow.Activate();
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


        // overflow handling for the command bar
        // runs once when the command bar is first ready
        // sets the fixed priority order and takes the initial width measurement
        private void SensorListCommandBar_Loaded(object sender, RoutedEventArgs e)
        {
            _commandBarPriorityOrder = new ICommandBarElement[]
            {
                PinToWidgetButton,
                ButtonSeparator,
                ResetValuesButton,
                HideSensorsButton,
                ShowHiddenSensorsButton
            };

            _commandBarOverflowStartIndex = -1;
            CacheCommandBarButtonWidths();
            UpdateCommandBarOverflow();
        }
        // recalculates the overflow split whenever the header changes size
        private void SensorListHeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_commandBarWidthsCached)
            {
                UpdateCommandBarOverflow();
            }
        }
        // measures every button once while its still fully visible with its label
        // (so we know later how much space each one actually needs)
        private void CacheCommandBarButtonWidths()
        {
            foreach (var element in _commandBarPriorityOrder)
            {
                if (element is FrameworkElement frameworkElement)
                {
                    _commandBarButtonWidths[element] = frameworkElement.ActualWidth;
                }
            }

            _commandBarWidthsCached = true;
        }
        // Fills the command bar strictly in priority order; the first button that doesn't
        // fit anymore, and everything after it, goes into the overflow menu.
        // Only touches PrimaryCommands/SecondaryCommands when the split actually changes,
        // otherwise every resize tick would rebuild the buttons and cause label flicker.
        private void UpdateCommandBarOverflow()
        {
            double availableWidth = SensorListHeaderGrid.ActualWidth - SensorListTitleText.ActualWidth - HeaderSpacingBuffer;
            double totalWidth = _commandBarPriorityOrder.Sum(button => _commandBarButtonWidths[button]);

            double budget = totalWidth <= availableWidth
                ? availableWidth
                : availableWidth - OverflowButtonReservedWidth;

            double runningWidth = 0;
            int overflowStartIndex = _commandBarPriorityOrder.Length;

            for (int i = 0; i < _commandBarPriorityOrder.Length; i++)
            {
                double buttonWidth = _commandBarButtonWidths[_commandBarPriorityOrder[i]];

                if (runningWidth + buttonWidth > budget)
                {
                    overflowStartIndex = i;
                    break;
                }

                runningWidth += buttonWidth;
            }

            // nothing changed since the last check: skip rebuilding
            // (stops flickering when resizing)
            if (overflowStartIndex == _commandBarOverflowStartIndex)
            {
                return;
            }

            _commandBarOverflowStartIndex = overflowStartIndex;

            SensorListCommandBar.PrimaryCommands.Clear();
            SensorListCommandBar.SecondaryCommands.Clear();

            for (int i = 0; i < _commandBarPriorityOrder.Length; i++)
            {
                if (i < overflowStartIndex)
                {
                    SensorListCommandBar.PrimaryCommands.Add(_commandBarPriorityOrder[i]);
                }
                else
                {
                    SensorListCommandBar.SecondaryCommands.Add(_commandBarPriorityOrder[i]);
                }
            }
        }
    }
}