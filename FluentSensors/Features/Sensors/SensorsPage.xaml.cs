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
        private const double HeaderSpacingBuffer = 100;
        private int _commandBarOverflowStartIndex = -1; // -1 means "not computed yet" so the very first call always applies once

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
            // commits the checked sensors as the WidgetWindow profiles new selection and gets that exact list back,
            // so persistence and what the widget actually shows can never drift apart
            var selectedSensors = ViewModel.CommitActiveProfileSelection();

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

        // Phase 1: commits the Csv profiles selection so it round-trips correctly, no csv consumer exists yet to act on it
        private void StartCsvMonitoring_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CommitActiveProfileSelection();
        }

        // Phase 1: commits the Taskbar profiles selection so it round-trips correctly
        // (the taskbar widget window itself ships in a later phase)
        private void PinToTaskbar_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CommitActiveProfileSelection();
        }

        // switches which profile the checkboxes reflect and commit to, and swaps the commit button in the command
        // bar to match (Pin to Widget / Start CSV Monitoring / Pin to Taskbar)
        private void SelectionProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem selectedItem) return;
            if (selectedItem.Tag is not string tag || !Enum.TryParse(tag, out SensorSelectionProfile profile)) return;

            ViewModel.ActiveProfile = profile;

            // the commit button occupying the priority orders first slot changed, force a full rebuild rather than
            // relying on the width-based dedup check in UpdateCommandBarOverflow
            _commandBarPriorityOrder = BuildCommandBarPriorityOrder();
            CacheCommandBarButtonWidths();
            _commandBarOverflowStartIndex = -1;
            UpdateCommandBarOverflow();
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
            _commandBarPriorityOrder = BuildCommandBarPriorityOrder();

            // elements in here always in the overflow menu
            _forcedOverflowElements = new HashSet<ICommandBarElement>
            {
                ShowHiddenSensorsButton
            };

            _commandBarOverflowStartIndex = -1;

            // Loaded fires as soon as the command bar enters the tree, not necessarily after the surrounding grids
            // layout pass has actually settled; measuring ActualWidth right here can catch everything (grid, title,
            // combobox, the buttons themselves) still at a stale/near-zero size from before that pass completed
            // deferring one dispatcher cycle guarantees a full layout pass has already run by the time we measure
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
                ShowHiddenSensorsButton,

                //ButtonSeparator2,
                //SelectPinnedButton,
                //DeselectAllButton
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
                if (element is FrameworkElement frameworkElement)
                {
                    _commandBarButtonWidths[element] = frameworkElement.ActualWidth;

                    // TEMP DIAGNOSTIC: remove once the overflow width calculation is confirmed correct again
                    Debug.WriteLine($"[CommandBarOverflow] cached {frameworkElement.Name}={frameworkElement.ActualWidth:F0}");
                }
            }

            _commandBarWidthsCached = true;
        }

        // fills the command bar strictly in priority order; the first button that does not fit anymore, and everything
        // after it, goes into the overflow menu
        // only touches PrimaryCommands/SecondaryCommands when the split actually changes, otherwise every resize tick
        // would rebuild the buttons and cause label flicker
        private void UpdateCommandBarOverflow()
        {
            double availableWidth = SensorListHeaderGrid.ActualWidth - SensorListTitleText.ActualWidth
                - SelectionProfileComboBox.ActualWidth - HeaderSpacingBuffer;

            // TEMP DIAGNOSTIC: remove once the overflow width calculation is confirmed correct again
            Debug.WriteLine($"[CommandBarOverflow] grid={SensorListHeaderGrid.ActualWidth:F0} title={SensorListTitleText.ActualWidth:F0} combo={SelectionProfileComboBox.ActualWidth:F0} available={availableWidth:F0}");

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