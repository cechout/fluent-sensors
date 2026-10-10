using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

using FluentSensors.Common.Localization;
using FluentSensors.Features.Widget;
using FluentSensors.Features.TaskbarWidget;
using FluentSensors.Features.CsvLogging;
using FluentSensors.Common.UI;
using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Services;


namespace FluentSensors.Features.Sensors
{
    public sealed partial class SensorsPage : Page
    {
        // === fields ===

        public SensorsViewModel ViewModel { get; }
        private int _infoBarTicket = 0;

        // SelectionChanged fires during InitializeComponent, before ViewModel is assigned (same guard as SettingsPage)
        private bool _isLoading = true;

        // command bar overflow
        private ICommandBarElement[] _commandBarPriorityOrder;
        private readonly Dictionary<ICommandBarElement, double> _commandBarButtonWidths = new();
        private HashSet<ICommandBarElement> _forcedOverflowElements;
        private bool _commandBarWidthsCached = false;
        private const double OverflowButtonReservedWidth = 48;
        private const double LeftSectionMinWidth = 300; // from SensorListTitleText
        private int _commandBarOverflowStartIndex = -1;

        // command bar entrance after a profile switch or a pin; hidden at once, then slid in from the right
        private Microsoft.UI.Xaml.Media.Animation.Storyboard? _commandBarEntranceStoryboard;
        private const double CommandBarEntranceOffset = 40; // px
        private const int CommandBarEntranceDelayMs = 10;
        private const int CommandBarEntranceDurationMs = 300;

        // info bar
        private bool _infoBarClipHandlersAttached = false;

        // stats elapsed readout; polled four times a second like the start page uptime, so the shown second never skips
        // (the view model only raises on a text change)
        private static readonly TimeSpan StatsElapsedTimerInterval = TimeSpan.FromMilliseconds(250);
        private DispatcherQueueTimer? _statsElapsedTimer;


        // === constructor ===

        public SensorsPage()
        {
            this.InitializeComponent();
            ViewModel = SensorsViewModel.Instance;

            // back on the last profile; still under the loading guard, SensorListCommandBar_Loaded builds the bar from
            // ActiveProfile a moment later
            var lastProfile = SettingsService.Instance.LastSensorProfile;
            ViewModel.ActiveWidgetIndex = SettingsService.Instance.LastWidgetWindowIndex;
            ViewModel.ActiveProfile = lastProfile;
            SelectProfile(lastProfile);

            // the widget window numbers, 1 and up
            WidgetWindowComboBox.ItemsSource = Enumerable.Range(1, WidgetWindow.MaxWidgetWindows).ToList();
            WidgetWindowComboBox.SelectedIndex = ViewModel.ActiveWidgetIndex;

            // the group header icons are plain brushes, rebuilt on ActualTheme (the AppTheme setting fires
            // before the new theme is in place)
            Loaded += (s, e) => ApplyActualTheme();
            ActualThemeChanged += (s, e) => ApplyActualTheme();

            // the elapsed readout ticks only while loaded and catches up on return
            Loaded += (s, e) =>
            {
                ViewModel.RefreshStatsElapsed();
                _statsElapsedTimer ??= CreateStatsElapsedTimer();
                _statsElapsedTimer.Start();
            };
            Unloaded += (s, e) => _statsElapsedTimer?.Stop();

            // the taskbar buttons follow the widget; only while loaded, the bar is rebuilt on every Loaded anyway
            Loaded += (s, e) => ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Unloaded += (s, e) => ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

            _isLoading = false;

            void ApplyActualTheme()
            {
                HardwareColorMode.IsDarkTheme = ActualTheme == ElementTheme.Dark;
                ViewModel.RefreshGroupIconBrushes();
            }

            DispatcherQueueTimer CreateStatsElapsedTimer()
            {
                var timer = DispatcherQueue.CreateTimer();
                timer.Interval = StatsElapsedTimerInterval;
                timer.Tick += (s, e) => ViewModel.RefreshStatsElapsed();
                return timer;
            }
        }


        // === user interaction ===

        private async void PinToWidget_Click(object sender, RoutedEventArgs e)
        {
            // opens or reconfigures the widget with the checked sensors; (persisted on every toggle already)
            var selectedSensors = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Where(sensor => sensor.IsSelected)
                .ToList();

            // nothing selected: the info bar
            if (selectedSensors.Count == 0)
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

            // reuses an open or hidden widget window (see WidgetWindow._retainedInstances)
            WidgetWindow.ShowWithSensors(ViewModel.ActiveWidgetIndex, selectedSensors);
        }

        // opens or reconfigures the csv logger with the checked sensors
        private async void StartCsvMonitoring_Click(object sender, RoutedEventArgs e)
        {
            var selectedSensors = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Where(sensor => sensor.IsSelected)
                .ToList();

            // an empty selection only counts while nothing records; a running recording keeps its sensors, the
            // button just brings the logger up
            if (selectedSensors.Count == 0 && !CsvLoggingService.Instance.IsRunning)
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

            // reuses an open or hidden logger window (see CsvLoggerWindow._retainedInstance)
            CsvLoggerWindow.ShowWithSensors(selectedSensors);
        }

        private async void PinToTaskbar_Click(object sender, RoutedEventArgs e)
        {
            // opens or reconfigures the taskbar widget with the checked sensors
            var selectedSensors = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Where(sensor => sensor.IsSelected)
                .ToList();

            // nothing selected: the info bar
            if (selectedSensors.Count == 0)
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

            // reuses an embedded or hidden taskbar widget
            TaskbarWidgetWindow.ShowWithSensors(selectedSensors);
        }

        // the only way to close the taskbar widget; (the flyout has no close button)
        private void CloseTaskbarWidget_Click(object sender, RoutedEventArgs e)
        {
            bool hadKeyboardFocus = TaskbarCloseButton.FocusState == FocusState.Keyboard;

            TaskbarWidgetWindow.CurrentInstance?.CloseWidget();

            // the pair leaves the bar, so keyboard focus moves on to the pin button instead of getting lost
            if (hadKeyboardFocus)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => PinToTaskbarButton.Focus(FocusState.Keyboard));
            }
        }

        // the update and close pair hovers as one: the half under the pointer takes its own hover and pressed fill,
        // the other half gets the same hover brush as its resting background
        private void TaskbarWidgetButtons_PointerEntered(object sender, RoutedEventArgs e)
        {
            var themeKey = TaskbarWidgetButtonsGrid.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
            var themeDictionary = (ResourceDictionary)TaskbarWidgetButtonsGrid.Resources.ThemeDictionaries[themeKey];
            var hoverBrush = (Brush)themeDictionary["TaskbarWidgetButtonsHoverBrush"];

            UpdateTaskbarButton.Background = hoverBrush;
            CloseTaskbarWidgetButton.Background = hoverBrush;
        }

        private void TaskbarWidgetButtons_PointerExited(object sender, RoutedEventArgs e)
        {
            UpdateTaskbarButton.ClearValue(Control.BackgroundProperty);
            CloseTaskbarWidgetButton.ClearValue(Control.BackgroundProperty);
        }

        // the commit button of each profile follows the open state of its window
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            bool affectsActiveProfile = e.PropertyName switch
            {
                nameof(SensorsViewModel.IsWidgetOpen) => ViewModel.IsWidgetProfileActive,
                nameof(SensorsViewModel.IsCsvLoggerOpen) => ViewModel.IsCsvProfileActive,
                nameof(SensorsViewModel.IsTaskbarWidgetOpen) => ViewModel.IsTaskbarProfileActive,
                _ => false
            };
            if (!affectsActiveProfile || _forcedOverflowElements == null) return;

            RebuildCommandBarOverflow(animate: true);
        }

        // the profile the checkboxes reflect and persist to, with the matching action button (Pin to Widget, Start
        // CSV Logging, Pin to Taskbar)
        private void SelectionProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem selectedItem) return;
            if (selectedItem.Tag is not string tag || !Enum.TryParse(tag, out SensorSelectionProfile profile)) return;

            ViewModel.ActiveProfile = profile;
            SettingsService.Instance.LastSensorProfile = profile;
            RebuildCommandBarOverflow(animate: true);
        }

        // which widget window the widget profile shows; the checkboxes, the pin button and its open state follow it
        private void WidgetWindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || WidgetWindowComboBox.SelectedIndex < 0) return;

            ViewModel.ActiveWidgetIndex = WidgetWindowComboBox.SelectedIndex;
            SettingsService.Instance.LastWidgetWindowIndex = ViewModel.ActiveWidgetIndex;
            RebuildCommandBarOverflow(animate: true);
        }

        // opens the list on a profile from outside (the taskbar flyout); through the ComboBox, so the handler above
        // stays the one place pairing profile and command bar
        public void SelectProfile(SensorSelectionProfile profile)
        {
            foreach (var item in SelectionProfileComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag
                    && Enum.TryParse(tag, out SensorSelectionProfile itemProfile)
                    && itemProfile == profile)
                {
                    // already selected: SelectionChanged does not fire
                    SelectionProfileComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        // the group counterpart of SelectProfile, for the start page sensor count button; matches LhmHardwareName
        // (HardwareName shows the WMI model for storage and network) and collapses every other group
        public void ExpandHardwareGroup(IReadOnlyList<string> lhmHardwareNames)
        {
            if (lhmHardwareNames == null || lhmHardwareNames.Count == 0) return;

            HardwareGroupViewModel target = null;

            foreach (var group in ViewModel.HardwareGroups)
            {
                bool wanted = lhmHardwareNames.Contains(group.LhmHardwareName, StringComparer.OrdinalIgnoreCase);
                group.IsExpanded = wanted;

                // scrolls to the first match; (memory matches several groups)
                if (wanted && target == null) target = group;
            }

            if (target == null) return;

            // at Low priority, after the expanders laid out at their new height
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ScrollToGroup(target));
        }

        // a plain ItemsControl has no public ContainerFromItem, so the tree is walked for the
        // presenter carrying the group
        private void ScrollToGroup(HardwareGroupViewModel group)
        {
            var container = FindContainer(HardwareItemsControl, group);

            container?.StartBringIntoView(new BringIntoViewOptions
            {
                VerticalAlignmentRatio = 0,
                AnimationDesired = true
            });
        }

        private static FrameworkElement FindContainer(DependencyObject root, object dataContext)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is ContentPresenter presenter && ReferenceEquals(presenter.DataContext, dataContext))
                {
                    return presenter;
                }

                var found = FindContainer(child, dataContext);
                if (found != null) return found;
            }

            return null;
        }

        private void ResetMinMax_Click(object sender, RoutedEventArgs e)
        {
            foreach (var group in ViewModel.HardwareGroups)
            {
                foreach (var sensor in group.Sensors)
                {
                    sensor.ResetMinMax();
                }
            }

            ViewModel.RestartStatsElapsed();
        }

        private async void HideSensors_Click(object sender, RoutedEventArgs e)
        {
            bool anySelected = ViewModel.HardwareGroups
                .SelectMany(group => group.Sensors)
                .Any(sensor => sensor.IsSelected);

            // the same info bar as PinToWidget_Click
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

        // see SettingsExpanderRepaintFix
        private void SettingsExpander_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsExpanderRepaintFix.Attach((SettingsExpander)sender);
        }
        private void RootScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // with horizontal scrolling on, a ScrollViewer measures with infinite width and the star columns collapse;
            // the real viewport width lets them stretch
            RootGrid.Width = e.NewSize.Width;
        }

        // info bar clipping
        private void InfoBarHost_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateInfoBarClip();

            // NavigationCacheMode keeps this page, Loaded fires on every reattach; subscribed once
            if (_infoBarClipHandlersAttached) return;
            _infoBarClipHandlersAttached = true;

            InfoBarHost.SizeChanged += (_, _) => UpdateInfoBarClip();
            BottomBar.SizeChanged += (_, _) => UpdateInfoBarClip();
        }

        // clips the info bar host above the bottom bar, whatever the bar transparency
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

        // once the command bar is ready; the forced overflow set and the first build
        private void SensorListCommandBar_Loaded(object sender, RoutedEventArgs e)
        {
            _forcedOverflowElements = new HashSet<ICommandBarElement>
            {
                ShowHiddenSensorsButton
            };

            RebuildCommandBarOverflow();
        }

        // an AppBarButton outside the bar reports another ActualWidth (DefaultLabelPosition only applies to its
        // children), so every element goes in as primary first and the split follows one tick later
        // animated, the bar stays hidden through that in between state and slides in once the split is done
        private void RebuildCommandBarOverflow(bool animate = false)
        {
            // off the tree a storyboard may never complete and would leave the bar hidden
            animate &= SensorListCommandBar.IsLoaded;

            if (animate)
            {
                HideCommandBar();
            }
            else
            {
                ShowCommandBar();
            }

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

                if (animate)
                {
                    AnimateCommandBarEntrance();
                }
            });
        }

        // a running entrance stops and falls back to the hidden base values
        private void HideCommandBar()
        {
            _commandBarEntranceStoryboard?.Stop();
            _commandBarEntranceStoryboard = null;

            SensorListCommandBar.Opacity = 0;
            CommandBarTransform.X = CommandBarEntranceOffset;
        }

        private void ShowCommandBar()
        {
            _commandBarEntranceStoryboard?.Stop();
            _commandBarEntranceStoryboard = null;

            SensorListCommandBar.Opacity = 1;
            CommandBarTransform.X = 0;
        }

        private void AnimateCommandBarEntrance()
        {
            var beginTime = TimeSpan.FromMilliseconds(CommandBarEntranceDelayMs);
            var duration = TimeSpan.FromMilliseconds(CommandBarEntranceDurationMs);
            var easing = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

            var animX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = CommandBarEntranceOffset,
                To = 0,
                BeginTime = beginTime,
                Duration = duration,
                EasingFunction = easing
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animX, CommandBarTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animX, "X");

            var animOpacity = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                BeginTime = beginTime,
                Duration = duration,
                EasingFunction = easing
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animOpacity, SensorListCommandBar);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animOpacity, "Opacity");

            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            sb.Children.Add(animX);
            sb.Children.Add(animOpacity);

            // the end values become the base values, so nothing depends on a held animation
            sb.Completed += (_, _) =>
            {
                if (_commandBarEntranceStoryboard != sb) return;

                SensorListCommandBar.Opacity = 1;
                CommandBarTransform.X = 0;
                sb.Stop();
                _commandBarEntranceStoryboard = null;
            };

            _commandBarEntranceStoryboard = sb;
            sb.Begin();
        }

        // with the commit button of the active profile; an open window gets it as an update button, the taskbar widget
        // as its update and close pair with a separator behind (it has no close of its own)
        private ICommandBarElement[] BuildCommandBarPriorityOrder()
        {
            PinToWidgetButton.Label = AppStrings.Get(ViewModel.IsWidgetOpen ? "Sensors_UpdateWidget" : "Sensors_PinToWidget");
            PinToWidgetIcon.Glyph = ViewModel.IsWidgetOpen ? "\uE895" : "\uE718";
            StartCsvMonitoringButton.Label = AppStrings.Get(ViewModel.IsCsvLoggerOpen ? "Sensors_UpdateCsvLogging" : "Sensors_StartCsvLogging");
            StartCsvMonitoringIcon.Glyph = ViewModel.IsCsvLoggerOpen ? "\uE895" : "\uE8A7";

            var order = new List<ICommandBarElement>();
            switch (ViewModel.ActiveProfile)
            {
                case SensorSelectionProfile.Csv:
                    order.Add(StartCsvMonitoringButton);
                    break;
                case SensorSelectionProfile.Taskbar when ViewModel.IsTaskbarWidgetOpen:
                    // --- revisit: split taskbar buttons ---
                    // the joined pair (TaskbarWidgetButtonsContainer) is parked with x:Load while two plain buttons are tried out
                    order.Add(TaskbarUpdateButton);
                    order.Add(TaskbarCloseButton);
                    order.Add(TaskbarWidgetButtonsSeparator);
                    break;
                case SensorSelectionProfile.Taskbar:
                    order.Add(PinToTaskbarButton);
                    break;
                default:
                    order.Add(PinToWidgetButton);
                    break;
            }

            order.Add(HideSensorsButton);
            order.Add(ButtonSeparator);
            order.Add(ResetValuesButton);
            order.Add(ShowHiddenSensorsButton);

            return order.ToArray();
        }

        // the overflow split follows the header size
        private void SensorListHeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_commandBarWidthsCached)
            {
                UpdateCommandBarOverflow();
            }
        }

        // every button once, while still visible with its label
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

        // fills the bar in priority order, the first unit that does not fit and everything after it overflows; only a
        // changed split rebuilds (or every resize tick would flicker the labels)
        // the first unit stays in the bar even when it does not fit, so the bar never shows only the overflow button
        private void UpdateCommandBarOverflow()
        {
            double leftSectionWidth = Math.Max(LeftSectionMinWidth, SensorListTitlePanel.ActualWidth);
            double availableWidth = SensorListHeaderGrid.ActualWidth - leftSectionWidth;

            if (availableWidth <= 0) return;

            // only elements not pinned to overflow, in units, so a separator never dangles alone
            var fittableUnits = GroupIntoOverflowUnits(
                _commandBarPriorityOrder.Where(element => !_forcedOverflowElements.Contains(element)));

            double totalWidth = fittableUnits.Sum(unit => unit.Sum(element => _commandBarButtonWidths.GetValueOrDefault(element, 40)));

            // the overflow button, for an overflow or a forced element
            bool needsOverflowButton = totalWidth > availableWidth || _forcedOverflowElements.Count > 0;
            double budget = needsOverflowButton
                ? availableWidth - OverflowButtonReservedWidth
                : availableWidth;

            double runningWidth = 0;
            int fittableOverflowStartUnitIndex = fittableUnits.Count;

            for (int i = 0; i < fittableUnits.Count; i++)
            {
                double unitWidth = fittableUnits[i].Sum(element => _commandBarButtonWidths.GetValueOrDefault(element, 40));

                if (i > 0 && runningWidth + unitWidth > budget)
                {
                    fittableOverflowStartUnitIndex = i;
                    break;
                }

                runningWidth += unitWidth;
            }

            // unchanged split
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

            // forced elements always at the end of the overflow menu
            foreach (var element in _commandBarPriorityOrder)
            {
                if (_forcedOverflowElements.Contains(element))
                {
                    SensorListCommandBar.SecondaryCommands.Add(element);
                }
            }
        }

        // a separator joins the unit of the element before it, so the fit never cuts between them
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
