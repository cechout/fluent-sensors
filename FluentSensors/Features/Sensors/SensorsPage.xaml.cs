using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

using FluentSensors.Features.Widget;
using FluentSensors.Features.TaskbarWidget;
using FluentSensors.Common.UI;
using FluentSensors.Common.Sensors;


namespace FluentSensors.Features.Sensors
{
    public sealed partial class SensorsPage : Page
    {
        // === fields ===

        // general fields
        public SensorsViewModel ViewModel { get; }
        private int _infoBarTicket = 0;
        private const double SensorsPageMinContentWidth = 520;

        // flag to prevent event handlers from firing during initialization
        //
        // (same pattern as SettingsPage SelectionProfileComboBox SelectedIndex="0" in xaml fires SelectionChanged
        // during InitializeComponent, before ViewModel below is even assigned)
        private bool _isLoading = true;

        // command bar overflow handling fields
        private ICommandBarElement[] _commandBarPriorityOrder;
        private readonly Dictionary<ICommandBarElement, double> _commandBarButtonWidths = new();
        private HashSet<ICommandBarElement> _forcedOverflowElements;
        private bool _commandBarWidthsCached = false;
        private const double OverflowButtonReservedWidth = 48;
        private const double LeftSectionMinWidth = 260; // minimum width measured from SensorListTitleText
        private int _commandBarOverflowStartIndex = -1;

        // info bar
        private bool _infoBarClipHandlersAttached = false;


        // === constructor ===

        public SensorsPage()
        {
            this.InitializeComponent();
            ViewModel = SensorsViewModel.Instance;
            _isLoading = false;
        }


        // === user interaction ===

        private async void PinToWidget_Click(object sender, RoutedEventArgs e)
        {
            // this is the real action: open or reconfigure the widget window with whatever is currently checked
            // persistence already happened live as each checkbox was toggled, this button has nothing left to commit
            var selectedSensors = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Where(sensor => sensor.IsSelected)
                .ToList();

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

            // reuses the existing widget window if one is open or was previously hidden, only creates a fresh native window if
            // none exists yet at all (see WidgetWindow._retainedInstance)
            WidgetWindow.ShowWithSensors(selectedSensors);
        }

        // Phase 1: no csv consumer exists yet, theres no action to perform on the current selection
        private void StartCsvMonitoring_Click(object sender, RoutedEventArgs e)
        {
        }

        private async void PinToTaskbar_Click(object sender, RoutedEventArgs e)
        {
            // opens or reconfigures the taskbar widget window with whatever is currently checked
            // persistence already happened live as each checkbox was toggled, this button triggers the visual update
            var selectedSensors = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Where(sensor => sensor.IsSelected)
                .ToList();

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

            // reuses the existing taskbar widget window if one is embedded or was previously hidden, only creates a fresh
            // native window if none exists yet at all
            TaskbarWidgetWindow.ShowWithSensors(selectedSensors);
        }

        // switches which profile the checkboxes reflect and persist to, and swaps the action button in the command
        // bar to match (Pin to Widget / Start CSV Logging / Pin to Taskbar)
        private void SelectionProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem selectedItem) return;
            if (selectedItem.Tag is not string tag || !Enum.TryParse(tag, out SensorSelectionProfile profile)) return;

            ViewModel.ActiveProfile = profile;
            RebuildCommandBarOverflow();
        }

        // entry point for callers outside the page (currently the taskbar flyout) that want the list to open on a
        // specific profile; drives the ComboBox rather than ViewModel.ActiveProfile so the handler above stays the
        // single place that pairs the profile switch with the command bar rebuild
        public void SelectProfile(SensorSelectionProfile profile)
        {
            foreach (var item in SelectionProfileComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && Enum.TryParse(tag, out SensorSelectionProfile itemProfile)
                    && itemProfile == profile)
                {
                    // no-op if it is already the selected one, SelectionChanged simply does not fire
                    SelectionProfileComboBox.SelectedItem = item;
                    return;
                }
            }
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
        // sets the priority order and takes the initial width measurement
        private void SensorListCommandBar_Loaded(object sender, RoutedEventArgs e)
        {
            _forcedOverflowElements = new HashSet<ICommandBarElement>
            {
                ShowHiddenSensorsButton
            };

            RebuildCommandBarOverflow();
        }

        // an AppBarButton that isnt currently a live PrimaryCommand or SecondaryCommand of this CommandBar does not
        // report the same ActualWidth it gets once actually placed and arranged inside it, DefaultLabelPosition and
        // compact rendering only apply to the bars current children
        // fix: two phases, first every element goes in as a PrimaryCommand unconditionally so each one gets a real,
        // correctly labeled layout pass; the actual primary/secondary split only happens once that has settled
        // (next dispatcher tick), once ActualWidth is trustworthy
        private void RebuildCommandBarOverflow()
        {
            _commandBarPriorityOrder = BuildCommandBarPriorityOrder();
            _commandBarOverflowStartIndex = -1;

            SensorListCommandBar.PrimaryCommands.Clear();
            SensorListCommandBar.SecondaryCommands.Clear();
            foreach (var element in _commandBarPriorityOrder)
            {
                SensorListCommandBar.PrimaryCommands.Add(element);
            }

            this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                CacheCommandBarButtonWidths();
                UpdateCommandBarOverflow();
            });
        }

        // picks whichever commit button matches the active selection profile
        private ICommandBarElement[] BuildCommandBarPriorityOrder()
        {
            ICommandBarElement commitButton = ViewModel.ActiveProfile switch
            {
                SensorSelectionProfile.WidgetWindow => PinToWidgetButton,
                SensorSelectionProfile.Csv => StartCsvMonitoringButton,
                SensorSelectionProfile.Taskbar => PinToTaskbarButton,
                _ => PinToWidgetButton
            };

            return new ICommandBarElement[]
            {
                commitButton,
                HideSensorsButton,
                ButtonSeparator,
                ResetValuesButton,
                ShowHiddenSensorsButton
            };
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
                if (element is FrameworkElement frameworkElement && frameworkElement.ActualWidth > 0)
                {
                    _commandBarButtonWidths[element] = frameworkElement.ActualWidth;
                }
            }

            _commandBarWidthsCached = true;
        }

        // fills the command bar strictly in priority order; the first unit that does not fit anymore, and everything
        // after it, goes into the overflow menu
        // only touches PrimaryCommands/SecondaryCommands when the split actually changes, otherwise every resize tick
        // would rebuild the buttons and cause label flicker
        private void UpdateCommandBarOverflow()
        {
            double leftSectionWidth = Math.Max(LeftSectionMinWidth, SensorListTitleText.ActualWidth + SelectionProfileComboBox.ActualWidth + 16);
            double availableWidth = SensorListHeaderGrid.ActualWidth - leftSectionWidth;

            if (availableWidth <= 0) return;

            // (only elements not permanently pinned to overflow take part in the width fit, grouped into units so a
            // separator can never end up dangling alone)
            var fittableUnits = GroupIntoOverflowUnits(
                _commandBarPriorityOrder.Where(element => !_forcedOverflowElements.Contains(element)));

            double totalWidth = fittableUnits.Sum(unit => unit.Sum(element => _commandBarButtonWidths.GetValueOrDefault(element, 40)));

            // overflow button is needed if the fittable elements alone overflow,
            // or if theres at least one forced element that needs it regardless
            bool needsOverflowButton = totalWidth > availableWidth || _forcedOverflowElements.Count > 0;
            double budget = needsOverflowButton
                ? availableWidth - OverflowButtonReservedWidth
                : availableWidth;

            double runningWidth = 0;
            int fittableOverflowStartUnitIndex = fittableUnits.Count;

            for (int i = 0; i < fittableUnits.Count; i++)
            {
                double unitWidth = fittableUnits[i].Sum(element => _commandBarButtonWidths.GetValueOrDefault(element, 40));

                if (runningWidth + unitWidth > budget)
                {
                    fittableOverflowStartUnitIndex = i;
                    break;
                }

                runningWidth += unitWidth;
            }

            // nothing changed since the last check: skip rebuilding
            // (stops flickering when resizing)
            if (fittableOverflowStartUnitIndex == _commandBarOverflowStartIndex)
            {
                return;
            }

            _commandBarOverflowStartIndex = fittableOverflowStartUnitIndex;

            SensorListCommandBar.PrimaryCommands.Clear();
            SensorListCommandBar.SecondaryCommands.Clear();

            for (int i = 0; i < fittableUnits.Count; i++)
            {
                var targetCommands = i < fittableOverflowStartUnitIndex
                    ? SensorListCommandBar.PrimaryCommands
                    : SensorListCommandBar.SecondaryCommands;

                foreach (var element in fittableUnits[i])
                {
                    targetCommands.Add(element);
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

        // AppBarSeparators are visually bonded to whichever element comes right before them in the priority order,
        // grouping them into that elements unit means the fit check can never cut between a button and the
        // separator immediately following it, so neither one ends up dangling alone on the wrong side of the split
        private static List<ICommandBarElement[]> GroupIntoOverflowUnits(IEnumerable<ICommandBarElement> elements)
        {
            var units = new List<ICommandBarElement[]>();

            foreach (var element in elements)
            {
                if (element is AppBarSeparator && units.Count > 0)
                {
                    units[^1] = units[^1].Append(element).ToArray();
                }
                else
                {
                    units.Add(new[] { element });
                }
            }

            return units;
        }
    }
}
