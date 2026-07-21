using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

using FluentSensors.Features.Widget;
using FluentSensors.Common;


namespace FluentSensors.Features.Sensors
{
    public sealed partial class SensorsPage : Page
    {
        // === fields ===

        // general fields
        public SensorsViewModel ViewModel { get; }
        private int _infoBarTicket = 0;
        private const double SensorsPageMinContentWidth = 520;

        // command bar overflow handling fields
        private ICommandBarElement[] _commandBarPriorityOrder;
        private readonly Dictionary<ICommandBarElement, double> _commandBarButtonWidths = new();
        private HashSet<ICommandBarElement> _forcedOverflowElements;
        private bool _commandBarWidthsCached = false;
        private const double OverflowButtonReservedWidth = 48;
        private const double HeaderSpacingBuffer = 100;
        private int _commandBarOverflowStartIndex = -1; // -1 means "not computed yet" so the very first call always applies once

        // info bar
        private bool _infoBarClipHandlersAttached = false;


        // === constructor ===

        public SensorsPage()
        {
            this.InitializeComponent();
            ViewModel = SensorsViewModel.Instance; 
        }


        // === user interaction ===

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
                AnimateInfoBar(-40, true);

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

                AnimateInfoBar(-40, true);

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
            if (HiddenSensorsWindow.CurrentInstance != null)
            {
                HiddenSensorsWindow.CurrentInstance.ShowAndActivate();
                return;
            }

            var hiddenSensorsWindow = new HiddenSensorsWindow();
            hiddenSensorsWindow.Activate();
        }

        private void SelectPinned_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectPinnedSensors();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DeselectAllSensors();
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


        // === layout and rendering workarounds ===

        // helper method to fix the rendering of the items
        private void SettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsExpanderRepaintFix.Attach((SettingsExpander)sender);
        }
        private void RootScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // a ScrollViewer measures its content with infinite width while horizontal scrolling is on, and * columns
            // collapse to their minimum with infinite width
            // feeding the grid the real viewport width lets the * columns stretch again, once the viewport drops below our
            // floor, the grid stays wider than the viewport and the horizontal scrollbar shows up on its own
            RootGrid.Width = Math.Max(e.NewSize.Width, SensorsPageMinContentWidth);
        }

        // inforbar clipping
        private void InfoBarHost_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateInfoBarClip();

            // with NavigationCacheMode="Required" this Page instance is reused across navigations, and Loaded fires again
            // every time the Frame reattaches it
            // without this guard, each reattachment would pile on another SizeChanged subscription, running UpdateInfoBarClip
            // once more per resize with every navigation cycle
            if (_infoBarClipHandlersAttached) return;
            _infoBarClipHandlersAttached = true;

            InfoBarHost.SizeChanged += (_, _) => UpdateInfoBarClip();
            BottomBar.SizeChanged += (_, _) => UpdateInfoBarClip();
        }

        // Clips the InfoBar host to the area above the bottom bar,
        // so the InfoBar can never render into the bottom bar's row —
        // regardless of the bottom bar's own transparency.
        private void UpdateInfoBarClip()
        {
            double visibleHeight = InfoBarHost.ActualHeight - BottomBar.ActualHeight;
            if (visibleHeight < 0)
                visibleHeight = 0;

            InfoBarHost.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, InfoBarHost.ActualWidth, visibleHeight)
            };
        }


        // === command bar overflow handling ===

        // runs once when the command bar is first ready
        // sets the fixed priority order and takes the initial width measurement
        private void SensorListCommandBar_Loaded(object sender, RoutedEventArgs e)
        {
            _commandBarPriorityOrder = new ICommandBarElement[]
            {
                PinToWidgetButton,
                HideSensorsButton,
                ButtonSeparator,
                ResetValuesButton,
                ShowHiddenSensorsButton,

                //ButtonSeparator2,
                //SelectPinnedButton,
                //DeselectAllButton
            };

            // elements in here always in the overflow menu
            _forcedOverflowElements = new HashSet<ICommandBarElement>
            {
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

            // only elements not permanently pinned to overflow take part in the width fit
            var fittableElements = _commandBarPriorityOrder
                .Where(element => !_forcedOverflowElements.Contains(element))
                .ToArray();

            double totalWidth = fittableElements.Sum(button => _commandBarButtonWidths[button]);

            // overflow button is needed if the fittable elements alone overflow,
            // or if theres at least one forced element that needs it regardless
            bool needsOverflowButton = totalWidth > availableWidth || _forcedOverflowElements.Count > 0;
            double budget = needsOverflowButton
                ? availableWidth - OverflowButtonReservedWidth
                : availableWidth;

            double runningWidth = 0;
            int fittableOverflowStartIndex = fittableElements.Length;

            for (int i = 0; i < fittableElements.Length; i++)
            {
                double buttonWidth = _commandBarButtonWidths[fittableElements[i]];

                if (runningWidth + buttonWidth > budget)
                {
                    fittableOverflowStartIndex = i;
                    break;
                }

                runningWidth += buttonWidth;
            }

            // nothing changed since the last check: skip rebuilding
            // (stops flickering when resizing)
            if (fittableOverflowStartIndex == _commandBarOverflowStartIndex)
            {
                return;
            }

            _commandBarOverflowStartIndex = fittableOverflowStartIndex;

            SensorListCommandBar.PrimaryCommands.Clear();
            SensorListCommandBar.SecondaryCommands.Clear();

            for (int i = 0; i < fittableElements.Length; i++)
            {
                if (i < fittableOverflowStartIndex)
                {
                    SensorListCommandBar.PrimaryCommands.Add(fittableElements[i]);
                }
                else
                {
                    SensorListCommandBar.SecondaryCommands.Add(fittableElements[i]);
                }
            }

            // forced elements always land in the overflow menu, appended at the end
            foreach (var element in _commandBarPriorityOrder)
            {
                if (_forcedOverflowElements.Contains(element))
                {
                    SensorListCommandBar.SecondaryCommands.Add(element);
                }
            }
        }
    }
}