using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using FluentSensors.Common.Localization;
using FluentSensors.Common.UI;


namespace FluentSensors.Controls.SensorGraph
{
    // the sensor panel:
    // composable chrome around a SensorGraphControl; every optional piece (title row, status row, y-axis and threshold
    // controls, tap actions) is its own property, set in XAML in any combination
    // GraphTapAction and ButtonTapAction pick what a tap on the graph or the status row button does; once either is
    // ShowFlyout, the threshold flyout badge replaces the button control panel
    public sealed partial class SensorPanelControl : UserControl
    {
        // === fields ===

        // true shows the switch UI for a slot with one candidate too, false shows plain text there
        private const bool ShowSwitchUiForSingleCandidate = true;

        // graph-color card background alpha (UseGraphColorCardBackground)
        private const byte GraphColorCardBackgroundAlphaDark = 44;
        private const byte GraphColorCardBackgroundAlphaLight = 44;

        // control panel slide; the hosts width animates, so the graph between them resizes along (same curve both ways)
        private const int ControlPanelSlideDurationMs = 300;
        private const EasingMode ControlPanelSlideEasing = EasingMode.EaseInOut;
        private Storyboard? _yAxisControlsStoryboard;
        private Storyboard? _thresholdControlsStoryboard;


        // === constructor ===

        public SensorPanelControl()
        {
            InitializeComponent();

            // the card background alpha depends on the theme, and the CardBackgroundOverride
            // x:Bind does not re-run on its own
            ActualThemeChanged += (s, e) => Bindings.Update();
        }


        // === dependency properties ===

        public SensorGraphViewModel ViewModel
        {
            get => (SensorGraphViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SensorGraphViewModel),
                typeof(SensorPanelControl),
                new PropertyMetadata(null, OnOverrideChanged));

        // known alternatives for this slot; null means this panel never switches at all
        public ObservableCollection<SensorSwitchCandidate> SwitchCandidates
        {
            get => (ObservableCollection<SensorSwitchCandidate>)GetValue(SwitchCandidatesProperty);
            set => SetValue(SwitchCandidatesProperty, value);
        }
        public static readonly DependencyProperty SwitchCandidatesProperty =
            DependencyProperty.Register(
                nameof(SwitchCandidates),
                typeof(ObservableCollection<SensorSwitchCandidate>),
                typeof(SensorPanelControl),
                new PropertyMetadata(null, OnSwitchCandidatesChanged));

        private static void OnSwitchCandidatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorPanelControl panel) return;

            // candidates can bind after ViewModel (XAML attribute order), so the overrides are re-applied for
            // a candidates own Y-axis max
            panel.ApplyOverridesToViewModel();
            panel.SyncSwitchSelection();
        }

        // a title row above everything, the sensor name only
        public bool ShowTitleRow
        {
            get => (bool)GetValue(ShowTitleRowProperty);
            set => SetValue(ShowTitleRowProperty, value);
        }
        public static readonly DependencyProperty ShowTitleRowProperty =
            DependencyProperty.Register(
                nameof(ShowTitleRow),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // name and unit in the status row instead of the name only
        public bool ShowUnitInStatusRow
        {
            get => (bool)GetValue(ShowUnitInStatusRowProperty);
            set => SetValue(ShowUnitInStatusRowProperty, value);
        }
        public static readonly DependencyProperty ShowUnitInStatusRowProperty =
            DependencyProperty.Register(
                nameof(ShowUnitInStatusRow),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // inline status row; toggle button, Y-max, sensor name, current value
        public bool ShowStatusRow
        {
            get => (bool)GetValue(ShowStatusRowProperty);
            set => SetValue(ShowStatusRowProperty, value);
        }
        public static readonly DependencyProperty ShowStatusRowProperty =
            DependencyProperty.Register(
                nameof(ShowStatusRow),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // the status row without toggle button and switch UI, for a read-only tile; needs ShowStatusRow too
        public bool ShowCompactStatusRow
        {
            get => (bool)GetValue(ShowCompactStatusRowProperty);
            set => SetValue(ShowCompactStatusRowProperty, value);
        }
        public static readonly DependencyProperty ShowCompactStatusRowProperty =
            DependencyProperty.Register(
                nameof(ShowCompactStatusRow),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // what a tap on the graph does; (None by default)
        public TapAction GraphTapAction
        {
            get => (TapAction)GetValue(GraphTapActionProperty);
            set => SetValue(GraphTapActionProperty, value);
        }
        public static readonly DependencyProperty GraphTapActionProperty =
            DependencyProperty.Register(
                nameof(GraphTapAction),
                typeof(TapAction),
                typeof(SensorPanelControl),
                new PropertyMetadata(TapAction.None));

        // what a tap on the status row button does; (TogglePanel by default)
        public TapAction ButtonTapAction
        {
            get => (TapAction)GetValue(ButtonTapActionProperty);
            set => SetValue(ButtonTapActionProperty, value);
        }
        public static readonly DependencyProperty ButtonTapActionProperty =
            DependencyProperty.Register(
                nameof(ButtonTapAction),
                typeof(TapAction),
                typeof(SensorPanelControl),
                new PropertyMetadata(TapAction.TogglePanel));

        // sensor name for the not-found placeholder while ViewModel is null
        public string PlaceholderSensorName
        {
            get => (string)GetValue(PlaceholderSensorNameProperty);
            set => SetValue(PlaceholderSensorNameProperty, value);
        }
        public static readonly DependencyProperty PlaceholderSensorNameProperty =
            DependencyProperty.Register(
                nameof(PlaceholderSensorName),
                typeof(string),
                typeof(SensorPanelControl),
                new PropertyMetadata(string.Empty));

        // unit next to PlaceholderSensorName
        public string PlaceholderUnit
        {
            get => (string)GetValue(PlaceholderUnitProperty);
            set => SetValue(PlaceholderUnitProperty, value);
        }
        public static readonly DependencyProperty PlaceholderUnitProperty =
            DependencyProperty.Register(
                nameof(PlaceholderUnit),
                typeof(string),
                typeof(SensorPanelControl),
                new PropertyMetadata(string.Empty));

        // whether the threshold badge shows in flyout mode; false keeps the tap-to-flyout without the badge
        public bool ShowThresholdFlyoutIndicator
        {
            get => (bool)GetValue(ShowThresholdFlyoutIndicatorProperty);
            set => SetValue(ShowThresholdFlyoutIndicatorProperty, value);
        }
        public static readonly DependencyProperty ShowThresholdFlyoutIndicatorProperty =
            DependencyProperty.Register(
                nameof(ShowThresholdFlyoutIndicator),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true));

        // Y-axis buttons in the control panel; increase, decrease, auto
        public bool ShowYAxisControls
        {
            get => (bool)GetValue(ShowYAxisControlsProperty);
            set => SetValue(ShowYAxisControlsProperty, value);
        }
        public static readonly DependencyProperty ShowYAxisControlsProperty =
            DependencyProperty.Register(
                nameof(ShowYAxisControls),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // threshold buttons in the control panel; increase, decrease, enable, direction, color
        public bool ShowThresholdControls
        {
            get => (bool)GetValue(ShowThresholdControlsProperty);
            set => SetValue(ShowThresholdControlsProperty, value);
        }
        public static readonly DependencyProperty ShowThresholdControlsProperty =
            DependencyProperty.Register(
                nameof(ShowThresholdControls),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // a small gray "name (unit)" label inside the graph, top left; (independent of the title and status rows)
        public bool ShowGraphLabel
        {
            get => (bool)GetValue(ShowGraphLabelProperty);
            set => SetValue(ShowGraphLabelProperty, value);
        }
        public static readonly DependencyProperty ShowGraphLabelProperty =
            DependencyProperty.Register(
                nameof(ShowGraphLabel),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // the sensor name in the graph header overlay
        public bool ShowGraphName
        {
            get => (bool)GetValue(ShowGraphNameProperty);
            set => SetValue(ShowGraphNameProperty, value);
        }
        public static readonly DependencyProperty ShowGraphNameProperty =
            DependencyProperty.Register(
                nameof(ShowGraphName),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true));

        // the current value in the graph header overlay
        public bool ShowGraphCurrentValue
        {
            get => (bool)GetValue(ShowGraphCurrentValueProperty);
            set => SetValue(ShowGraphCurrentValueProperty, value);
        }
        public static readonly DependencyProperty ShowGraphCurrentValueProperty =
            DependencyProperty.Register(
                nameof(ShowGraphCurrentValue),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true));

        // the graph color for this instance, whatever the global color setting; alpha 0 = no override
        public Windows.UI.Color GraphColorOverride
        {
            get => (Windows.UI.Color)GetValue(GraphColorOverrideProperty);
            set => SetValue(GraphColorOverrideProperty, value);
        }
        public static readonly DependencyProperty GraphColorOverrideProperty =
            DependencyProperty.Register(
                nameof(GraphColorOverride),
                typeof(Windows.UI.Color),
                typeof(SensorPanelControl),
                new PropertyMetadata(Windows.UI.Color.FromArgb(0, 0, 0, 0)));

        // history shown by this graph, independent of the setting; NaN = no override
        public double GraphTimeSpanOverrideSeconds
        {
            get => (double)GetValue(GraphTimeSpanOverrideSecondsProperty);
            set => SetValue(GraphTimeSpanOverrideSecondsProperty, value);
        }
        public static readonly DependencyProperty GraphTimeSpanOverrideSecondsProperty =
            DependencyProperty.Register(
                nameof(GraphTimeSpanOverrideSeconds),
                typeof(double),
                typeof(SensorPanelControl),
                new PropertyMetadata(double.NaN, OnOverrideChanged));

        // Inherit = no override; (the persisted IsAutoScaled state applies)
        public BoolOverride IsAutoScaledOverride
        {
            get => (BoolOverride)GetValue(IsAutoScaledOverrideProperty);
            set => SetValue(IsAutoScaledOverrideProperty, value);
        }
        public static readonly DependencyProperty IsAutoScaledOverrideProperty =
            DependencyProperty.Register(
                nameof(IsAutoScaledOverride),
                typeof(BoolOverride),
                typeof(SensorPanelControl),
                new PropertyMetadata(BoolOverride.Inherit, OnOverrideChanged));

        // NaN = no override
        public double ManualYMaxOverride
        {
            get => (double)GetValue(ManualYMaxOverrideProperty);
            set => SetValue(ManualYMaxOverrideProperty, value);
        }
        public static readonly DependencyProperty ManualYMaxOverrideProperty =
            DependencyProperty.Register(
                nameof(ManualYMaxOverride),
                typeof(double),
                typeof(SensorPanelControl),
                new PropertyMetadata(double.NaN, OnOverrideChanged));

        // pass-through to SensorGraphControl.ThresholdLabelAlwaysVisible; visual only, never persisted
        public bool ThresholdLabelAlwaysVisible
        {
            get => (bool)GetValue(ThresholdLabelAlwaysVisibleProperty);
            set => SetValue(ThresholdLabelAlwaysVisibleProperty, value);
        }
        public static readonly DependencyProperty ThresholdLabelAlwaysVisibleProperty =
            DependencyProperty.Register(
                nameof(ThresholdLabelAlwaysVisible),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true));

        // pass-through to SensorGraphControl.ShowCardBackground; off for a consumer that draws its own card
        public bool ShowGraphCardBackground
        {
            get => (bool)GetValue(ShowGraphCardBackgroundProperty);
            set => SetValue(ShowGraphCardBackgroundProperty, value);
        }
        public static readonly DependencyProperty ShowGraphCardBackgroundProperty =
            DependencyProperty.Register(
                nameof(ShowGraphCardBackground),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true, OnCardBackgroundVisibilityChanged));

        // the CardBackgroundOverride x:Bind does not re-run when this changes after load (like
        // the theme in the constructor)
        private static void OnCardBackgroundVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorPanelControl panel) panel.Bindings.Update();
        }

        // pass-through to SensorGraphControl.CardBorderOverride; false = transparent
        public bool ShowGraphCardBorder
        {
            get => (bool)GetValue(ShowGraphCardBorderProperty);
            set => SetValue(ShowGraphCardBorderProperty, value);
        }
        public static readonly DependencyProperty ShowGraphCardBorderProperty =
            DependencyProperty.Register(
                nameof(ShowGraphCardBorder),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true));

        // the card background as an alpha tint of the graph color, see GraphColorCardBackgroundAlphaDark
        public bool UseGraphColorCardBackground
        {
            get => (bool)GetValue(UseGraphColorCardBackgroundProperty);
            set => SetValue(UseGraphColorCardBackgroundProperty, value);
        }
        public static readonly DependencyProperty UseGraphColorCardBackgroundProperty =
            DependencyProperty.Register(
                nameof(UseGraphColorCardBackground),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(false));

        // pass-through to SensorGraphControl.IsHoverEnabled; false for a decorative graph (no
        // hover circle, no value label)
        public bool IsGraphHoverEnabled
        {
            get => (bool)GetValue(IsGraphHoverEnabledProperty);
            set => SetValue(IsGraphHoverEnabledProperty, value);
        }
        public static readonly DependencyProperty IsGraphHoverEnabledProperty =
            DependencyProperty.Register(
                nameof(IsGraphHoverEnabled),
                typeof(bool),
                typeof(SensorPanelControl),
                new PropertyMetadata(true));

        // ViewModel or one of the three overrides changed; re-applies all of them, so XAML
        // attribute order does not matter
        private static void OnOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorPanelControl panel) return;

            panel.ApplyOverridesToViewModel();

            // the x:Bind path to SensorGraphControl.Values (ViewModel.SensorData) is not re-evaluated once ViewModel is
            // null and would keep showing the previous sensor, so the chart is cleared here
            if (e.Property == ViewModelProperty && e.NewValue == null)
            {
                panel.GraphControl.Values = new ObservableCollection<double?>();
            }

            if (e.Property == ViewModelProperty) panel.SyncSwitchSelection();

            // another sensor brings its own panel state, shown as is
            if (e.Property == ViewModelProperty) panel.ApplyControlPanelState(animate: false);

            // --- workaround: x:Bind function bindings only track the arguments own path, not what
            // the function body reads ---
            // problem: the GetXOrPlaceholder functions take ViewModel itself (to survive a null one), so x:Bind reruns
            // them only when ViewModel is swapped, never when its values change on every tick
            // fix: Bindings.Update() on the ViewModels PropertyChanged; the old one is unsubscribed, so a switched-away
            // sensor does not keep this control alive
            if (e.Property == ViewModelProperty)
            {
                if (e.OldValue is SensorGraphViewModel oldViewModel) oldViewModel.PropertyChanged -= panel.OnViewModelPropertyChanged;
                if (e.NewValue is SensorGraphViewModel newViewModel) newViewModel.PropertyChanged += panel.OnViewModelPropertyChanged;
            }
        }

        private void ApplyOverridesToViewModel()
        {
            double? graphTimeSpanSeconds = double.IsNaN(GraphTimeSpanOverrideSeconds) ? (double?)null : GraphTimeSpanOverrideSeconds;

            bool? isAutoScaled = IsAutoScaledOverride switch
            {
                BoolOverride.True => true,
                BoolOverride.False => false,
                _ => null
            };

            double? panelYMax = double.IsNaN(ManualYMaxOverride) ? (double?)null : ManualYMaxOverride;

            // a switch candidate can carry its own Y-axis max (Free Space scaling to the drive Total Space); for the
            // active sensor that wins and forces manual scaling
            double? candidateYMax = GetActiveCandidateYMax();
            double? manualYMax = candidateYMax ?? panelYMax;
            if (candidateYMax.HasValue) isAutoScaled = false;

            ViewModel?.ApplyViewOverrides(graphTimeSpanSeconds, isAutoScaled, manualYMax);
        }

        // the Y-axis max of the candidate matching the active ViewModel, if it has one
        private double? GetActiveCandidateYMax()
        {
            if (ViewModel == null || SwitchCandidates == null) return null;
            var active = SwitchCandidates.FirstOrDefault(c => c.SensorId == ViewModel.SensorId);
            return active?.YMaxOverride;
        }


        // === bindable helper surfaces ===

        // the chart row and its buttons; collapsed while ViewModel is null (the hardware does not report the sensor),
        // the status row stays with placeholders
        private Visibility GetContentVisibility(SensorGraphViewModel viewModel) =>
            viewModel == null ? Visibility.Collapsed : Visibility.Visible;

        // only with a real sensor; (no placeholder variant, unlike the status row)
        private Visibility GetTitleRowVisibility(bool showTitleRow, SensorGraphViewModel viewModel) =>
            showTitleRow && viewModel != null ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetNotFoundVisibility(SensorGraphViewModel viewModel) =>
            viewModel == null ? Visibility.Visible : Visibility.Collapsed;

        // name and unit come from the consumer, a null ViewModel has none
        private string FormatNotFoundMessage(string sensorName, string unit)
        {
            return AppStrings.Format("SensorPanel_NotFound", string.IsNullOrEmpty(unit) ? sensorName : $"{sensorName} ({unit})");
        }

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // status row placeholder while ViewModel is null; the row keeps its place (a start
        // page tile for a missing sensor)
        private string GetTextOrPlaceholder(string value) => string.IsNullOrEmpty(value) ? "--" : value;

        // --- workaround: x:Bind skips function calls whose argument path runs through null ---
        // problem: with a function argument path like ViewModel.ActualYMaxText and a null ViewModel, x:Bind never calls
        // the function and leaves the target at its default (confirmed platform bug):
        // https://github.com/microsoft/microsoft-ui-xaml/issues/2166
        // fix: pass ViewModel itself and navigate null-safe inside the method
        private string GetYMaxOrPlaceholder(SensorGraphViewModel viewModel) => GetTextOrPlaceholder(viewModel?.ActualYMaxText);

        private string GetCurrentValueOrPlaceholder(SensorGraphViewModel viewModel) => GetTextOrPlaceholder(viewModel?.CurrentValueText);

        // the plain value color follows this controls ActualTheme, since the taskbar widget (Windows theme)
        // and the flyout (app theme) render the same view models; a threshold color is theme independent
        // and comes from the view model
        private Brush GetCurrentValueColorOrDefault(SensorGraphViewModel viewModel)
        {
            if (viewModel != null && viewModel.IsThresholdColorActive) return viewModel.CurrentValueColor;

            return DefaultTextColor.ForTheme(ActualTheme == ElementTheme.Dark);
        }

        // a missing sensor keeps its name from PlaceholderSensorName; only value and y-axis fall back to "--"
        private string GetStatusRowTitleOrPlaceholder(bool showUnit, SensorGraphViewModel viewModel, string placeholderName, string placeholderUnit)
        {
            if (viewModel != null) return GetTextOrPlaceholder(GetStatusRowTitle(showUnit, viewModel.SensorName, viewModel.DisplayNameWithUnit));

            if (string.IsNullOrEmpty(placeholderName)) return "--";

            string placeholderNameWithUnit = string.IsNullOrEmpty(placeholderUnit) ? placeholderName : $"{placeholderName} ({placeholderUnit})";
            return GetStatusRowTitle(showUnit, placeholderName, placeholderNameWithUnit);
        }

        private Brush GetBrushOrDefault(Brush value) => value ?? DefaultTextColor.Resolve();

        // the two status row variants are exclusive; compact wins
        private Visibility GetStandardStatusRowVisibility(bool showStatusRow, bool showCompactStatusRow) =>
            showStatusRow && !showCompactStatusRow ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetCompactStatusRowVisibility(bool showStatusRow, bool showCompactStatusRow) =>
            showStatusRow && showCompactStatusRow ? Visibility.Visible : Visibility.Collapsed;

        private string GetStatusRowTitle(bool showUnit, string name, string nameWithUnit)
        {
            return showUnit ? nameWithUnit : name;
        }

        // switch button and its plain text fallback share one cell
        private Visibility GetSwitchButtonVisibility(ObservableCollection<SensorSwitchCandidate> candidates)
        {
            return IsSwitchUiActive(candidates) ? Visibility.Visible : Visibility.Collapsed;
        }

        private Visibility GetSwitchTextVisibility(ObservableCollection<SensorSwitchCandidate> candidates)
        {
            return IsSwitchUiActive(candidates) ? Visibility.Collapsed : Visibility.Visible;
        }

        // null never shows the switch UI; with one candidate ShowSwitchUiForSingleCandidate decides
        private bool IsSwitchUiActive(ObservableCollection<SensorSwitchCandidate> candidates)
        {
            if (candidates == null) return false;
            return candidates.Count > 1 || ShowSwitchUiForSingleCandidate;
        }

        // either tap opens the flyout badge; (badge and button control panel are exclusive, this governs both)
        private bool IsFlyoutModeActive(TapAction graphTapAction, TapAction buttonTapAction)
        {
            return graphTapAction == TapAction.ShowFlyout || buttonTapAction == TapAction.ShowFlyout;
        }

        // Y-axis and threshold controls share the control panel; flyout mode collapses both,
        // whatever Show*Controls says
        // (the open state itself sits on the hosts around them, see ApplyControlPanelState)
        private Visibility GetYAxisControlsVisibility(bool showYAxisControls, TapAction graphTapAction, TapAction buttonTapAction)
        {
            return showYAxisControls && !IsFlyoutModeActive(graphTapAction, buttonTapAction) ? Visibility.Visible : Visibility.Collapsed;
        }

        private Visibility GetThresholdControlsVisibility(bool showThresholdControls, TapAction graphTapAction, TapAction buttonTapAction)
        {
            return showThresholdControls && !IsFlyoutModeActive(graphTapAction, buttonTapAction) ? Visibility.Visible : Visibility.Collapsed;
        }

        // the badge only when a tap opens it
        private Visibility GetThresholdFlyoutVisibility(TapAction graphTapAction, TapAction buttonTapAction)
        {
            return IsFlyoutModeActive(graphTapAction, buttonTapAction) ? Visibility.Visible : Visibility.Collapsed;
        }

        // the automatic color or this instances override; (alpha 0 = not set, a real accent is never transparent)
        private Windows.UI.Color GetEffectiveGraphColor(Windows.UI.Color overrideColor, Windows.UI.Color autoColor)
        {
            return overrideColor.A == 0 ? autoColor : overrideColor;
        }

        // badge opacity; visibility stays with flyout mode, so the badge keeps its place and ShowFlyout() keeps working
        private double BoolToOpacity(bool value) => value ? 1.0 : 0.0;

        // ShowGraphCardBackground and UseGraphColorCardBackground into SensorGraphControl.CardBackgroundOverride:
        // showBackground = false -> transparent
        // useGraphColor = true   -> theme-dependent alpha tint of the graph color
        // default                -> null (the themed card background)
        private Windows.UI.Color? GetEffectiveCardBackground(bool showBackground, bool useGraphColor, Windows.UI.Color overrideColor, Windows.UI.Color autoColor)
        {
            if (!showBackground)
            {
                return Windows.UI.Color.FromArgb(0, 0, 0, 0);
            }

            if (useGraphColor)
            {
                Windows.UI.Color effectiveGraphColor = GetEffectiveGraphColor(overrideColor, autoColor);
                byte alpha = IsDarkTheme() ? GraphColorCardBackgroundAlphaDark : GraphColorCardBackgroundAlphaLight;
                return Windows.UI.Color.FromArgb(alpha, effectiveGraphColor.R, effectiveGraphColor.G, effectiveGraphColor.B);
            }

            return null;
        }

        // ActualTheme, not the app setting, so a window with its own theme (the taskbar widget follows Windows) is
        // right; the constructor re-runs the bindings on a change
        private bool IsDarkTheme()
        {
            return ActualTheme == ElementTheme.Dark;
        }

        private Windows.UI.Color? BoolToCardBorderOverride(bool showBorder) =>
            showBorder ? null : Windows.UI.Color.FromArgb(0, 0, 0, 0);


        // === event handlers ===

        // see the workaround in OnOverrideChanged
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Bindings.Update();

            if (e.PropertyName == nameof(SensorGraphViewModel.ControlPanelVisibility))
            {
                ApplyControlPanelState(animate: IsLoaded);
            }
        }

        // the part of the panel outside the host is cut off; the left panel keeps its right edge on the graph, so
        // it slides in from the left; the right one sits on the graphs edge anyway and slides in from the right
        private void ControlPanelHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var host = (FrameworkElement)sender;
            host.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };

            if (host == YAxisControlsHost)
            {
                YAxisControlsTransform.X = e.NewSize.Width - GetPanelWidth(YAxisControls);
            }
        }

        private void GraphControl_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ExecuteTapAction(GraphTapAction);
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteTapAction(ButtonTapAction);
        }

        // keeps the closed combobox text on the active sensor
        private void SwitchCandidateComboBox_DropDownOpened(object sender, object e)
        {
            SyncSwitchSelection();
        }

        private void SwitchButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchCandidateComboBox.IsDropDownOpen = true;
        }

        // the closing combobox takes the focus, invisible but still under the arrow keys; queued after that, the focus
        // goes back to the switch button
        private void SwitchCandidateComboBox_DropDownClosed(object sender, object e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (SwitchCandidateComboBox.FocusState != FocusState.Unfocused) SwitchButton.Focus(FocusState.Programmatic);
            });
        }

        // resolves the pick (its graph is built once, then cached) into ViewModel
        private void SwitchCandidateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SwitchCandidateComboBox.SelectedItem is not SensorSwitchCandidate candidate) return;
            if (ViewModel != null && candidate.SensorId == ViewModel.SensorId) return;

            ViewModel = candidate.Resolve();
        }


        // === private helpers ===

        private void ExecuteTapAction(TapAction action)
        {
            switch (action)
            {
                case TapAction.TogglePanel:
                    ViewModel?.ToggleControlPanel();
                    break;
                case TapAction.ShowFlyout:
                    ThresholdFlyoutBadge.ShowFlyout();
                    break;
            }
        }

        // opens or closes both panel hosts; a closed host is collapsed, so its buttons leave the tab order
        private void ApplyControlPanelState(bool animate)
        {
            bool isOpen = ViewModel?.ControlPanelVisibility == Visibility.Visible;

            _yAxisControlsStoryboard = SetControlPanelOpen(YAxisControlsHost, GetPanelWidth(YAxisControls), isOpen, animate, _yAxisControlsStoryboard);
            _thresholdControlsStoryboard = SetControlPanelOpen(ThresholdControlsHost, GetPanelWidth(ThresholdControls), isOpen, animate, _thresholdControlsStoryboard);
        }

        // slides the host width from where it is now, so a toggle during a running slide turns around smoothly; once
        // open the width goes back to auto and follows the content
        private static Storyboard? SetControlPanelOpen(FrameworkElement host, double panelWidth, bool isOpen, bool animate, Storyboard? running)
        {
            double from = host.Visibility == Visibility.Visible ? host.ActualWidth : 0;
            running?.Stop();

            if (!animate || panelWidth <= 0)
            {
                host.Width = double.NaN;
                host.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
                return null;
            }

            host.Width = from;
            host.Visibility = Visibility.Visible;

            var animation = new DoubleAnimation
            {
                From = from,
                To = isOpen ? panelWidth : 0,
                Duration = TimeSpan.FromMilliseconds(ControlPanelSlideDurationMs),
                EasingFunction = new CubicEase { EasingMode = ControlPanelSlideEasing },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animation, host);
            Storyboard.SetTargetProperty(animation, "Width");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Completed += (_, _) =>
            {
                host.Width = double.NaN;
                if (!isOpen) host.Visibility = Visibility.Collapsed;
                storyboard.Stop();
            };
            storyboard.Begin();

            return storyboard;
        }

        // the width a panel takes when open; 0 while its Show*Controls or the flyout mode hides it
        private static double GetPanelWidth(FrameworkElement panel)
        {
            if (panel.Visibility != Visibility.Visible) return 0;
            if (!double.IsNaN(panel.Width)) return panel.Width;

            // the threshold block, two fixed width columns side by side
            return panel is Panel container
                ? container.Children.OfType<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible).Sum(child => child.Width)
                : 0;
        }

        private void SyncSwitchSelection()
        {
            if (ViewModel == null || SwitchCandidates == null) return;
            SwitchCandidateComboBox.SelectedItem = SwitchCandidates.FirstOrDefault(c => c.SensorId == ViewModel.SensorId);
        }
    }
}
