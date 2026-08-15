using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using FluentSensors.Common.UI;


namespace FluentSensors.Controls.SensorGraph
{
    // composable chrome around a SensorGraphControl:
    // every optional piece (title row, status row, y-axis/threshold controls, graph-tap-to-toggle, ...) is its own
    // independent property instead of a fixed set of presets, so any combination a consumer needs can be set directly
    // in XAML without touching this class
    //
    // GraphTapAction/ButtonTapAction each independently control what a tap on the graph itself, or on the toggle
    // button in the status row, should trigger; whenever either one is set to TapAction.ShowFlyout, the button-based
    // control panel (ShowYAxisControls/ShowThresholdControls) is replaced by the compact threshold flyout badge; there
    // is no separate switch for that, it is purely derived from these two
    public sealed partial class SensorPanelControl : UserControl
    {
        // === fields ===

        // manual toggle: true also shows the switch UI for a slot with exactly one candidate, false falls back to
        // plain text in that case
        private const bool ShowSwitchUiForSingleCandidate = true;


        // === constructor ===

        public SensorPanelControl()
        {
            InitializeComponent();
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

            // candidates can bind after ViewModel depending on XAML attribute order; re-apply so a candidates own
            // Y-axis max is honored even if it was not yet reachable the first time overrides ran
            panel.ApplyOverridesToViewModel();
            panel.SyncSwitchSelection();
        }

        // separate title row above everything else, showing just the sensor name
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

        // status row shows name + unit combined instead of just the name (no effect if ShowStatusRow is false)
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

        // inline status row
        // (toggle button, Y-max value, sensor name, current value) with its own dedicated toggle button
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

        // swaps the status row for a toggle-button-free variant (Y-max, sensor name, current value only, no panel
        // toggle, no switch UI); only takes effect while ShowStatusRow is also true
        // for consumers with nothing for the panel toggle to actually toggle, e.g. a read-only tile in a grid
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

        // what tapping the graph itself triggers;
        // None = no reaction (default for most consumers)
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

        // what tapping the toggle button in the status row triggers;
        // TogglePanel = default behavior
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

        // fallback sensor name shown in the not-found placeholder when ViewModel is null; has no effect when
        // ViewModel is set (the real SensorGraphViewModels own name is used instead)
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

        // fallback unit shown alongside PlaceholderSensorName
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

        // whether the small colored threshold badge is visually rendered when flyout mode is active; set to false
        // to keep GraphTapAction/ButtonTapAction opening the flyout without showing the badge itself
        // e.g. when a consumer only wants the tap-to-flyout behavior, not the separate indicator UI the badge represents
        // elsewhere
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

        // Y-axis scaling buttons (increase/decrease/auto) inside the control panel
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

        // threshold buttons (increase/decrease/enable/direction/color) inside the control panel
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

        // shows a small "sensor name (unit)" label directly inside the graph itself, top-left, in gray
        // (independent of ShowTitleRow/ShowStatusRow, e.g. for compact layouts with neither)
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

        // overrides the graphs line/section color for this specific instance, regardless of the global accent/custom
        // color setting;
        // Colors.Transparent (Alpha 0) = no override, since a real accent color is never fully transparent
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

        // how much history this graph shows, independent of the global Settings value; point count is derived from
        // this plus the current polling interval
        // NaN = no override, same sentinel pattern as ManualYMaxOverride below
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

        // Inherit = no override
        // (this sensors persisted/global IsAutoScaled state is used as-is)
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

        // pure visual pass-through to SensorGraphControl.ThresholdLabelAlwaysVisible
        // no ViewModel coupling, so this needs no override/decoupling logic; it never persists anywhere to begin
        // with
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

        // pure visual pass-through to SensorGraphControl.ShowCardBackground
        // no ViewModel coupling, e.g. for graphs embedded in a consumer that already draws its own card background
        // around this whole panel
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
                new PropertyMetadata(true));

        // pure visual pass-through to SensorGraphControl.IsHoverEnabled; default true keeps every existing consumer
        // unchanged, set to false for a purely decorative graph (no hover circle, no value label on pointer move)
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

        // fires whenever ViewModel itself changes, or any of the three override properties change; re-applies all of them
        // together so the final state is always correct regardless of the order XAML happens to set these attributes in
        private static void OnOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SensorPanelControl panel) return;

            panel.ApplyOverridesToViewModel();

            // x:Binds nested path down to SensorGraphControl.Values (ViewModel.SensorData) does not get
            // re-evaluated once ViewModel itself becomes null
            // it simply stops and leaves whatever was bound before untouched, which silently keeps showing another
            // sensors live data
            // Clear the chart explicitly here instead, since this callback is confirmed to fire correctly with
            // ViewModel==null
            if (e.Property == ViewModelProperty && e.NewValue == null)
            {
                panel.GraphControl.Values = new ObservableCollection<double?>();
            }

            if (e.Property == ViewModelProperty) panel.SyncSwitchSelection();

            // --- workaround: x:Bind function bindings only track the arguments own path, not what the function body reads ---
            // problem: GetYMaxOrPlaceholder/GetCurrentValueOrPlaceholder/GetCurrentValueColorOrDefault/
            // GetStatusRowTitleOrPlaceholder all take ViewModel itself rather than a dotted path into it, so they
            // correctly handle ViewModel being null; but that also means x:Bind only reruns them when ViewModel
            // itself gets swapped for a different instance, never when CurrentValueText/CurrentValueColor/
            // ActualYMaxText change on the very same instance, which is what actually happens on every sensor tick
            // fix: subscribe to the new ViewModels own PropertyChanged directly and force every x:Bind expression
            // in this control to refresh whenever it fires; unsubscribe from the old one first so a sensor
            // switched away from does not keep this control alive
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

            // a switch candidate can carry its own Y-axis max (e.g. Free Space scaling to the drives Total Space);
            // when the active sensor is such a candidate, that wins and also forces manual scaling, otherwise the
            // panel-level override applies exactly as before
            double? candidateYMax = GetActiveCandidateYMax();
            double? manualYMax = candidateYMax ?? panelYMax;
            if (candidateYMax.HasValue) isAutoScaled = false;

            ViewModel?.ApplyViewOverrides(graphTimeSpanSeconds, isAutoScaled, manualYMax);
        }

        // Y-axis max of whichever candidate matches the active ViewModel, or null if the active sensor is not a
        // switch candidate or that candidate has no override of its own
        private double? GetActiveCandidateYMax()
        {
            if (ViewModel == null || SwitchCandidates == null) return null;
            var active = SwitchCandidates.FirstOrDefault(c => c.SensorId == ViewModel.SensorId);
            return active?.YMaxOverride;
        }


        // === bindable helper surfaces ===

        // whether the graph chrome (chart row + its control buttons) should render; false when ViewModel is null, e.g.
        // this hardware instance does not report the requested sensor at all
        // the label row above it is a separate concern now, see GetTitleRowVisibility: the status row keeps showing
        // (with placeholder values) even while this is Collapsed
        private Visibility GetContentVisibility(SensorGraphViewModel viewModel) =>
            viewModel == null ? Visibility.Collapsed : Visibility.Visible;

        // title row only makes sense once a real sensor exists; unlike the status row it has no placeholder variant
        private Visibility GetTitleRowVisibility(bool showTitleRow, SensorGraphViewModel viewModel) =>
            showTitleRow && viewModel != null ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetNotFoundVisibility(SensorGraphViewModel viewModel) =>
            viewModel == null ? Visibility.Visible : Visibility.Collapsed;

        // PlaceholderSensorName/PlaceholderUnit are set by the consumer alongside ViewModel, since a null
        // ViewModel carries no name/unit of its own to fall back on
        private string FormatNotFoundMessage(string sensorName, string unit)
        {
            return string.IsNullOrEmpty(unit) ? $"{sensorName} sensor not found" : $"{sensorName} ({unit}) sensor not found";
        }

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // status row placeholder for when ViewModel is null; keeps the row at its usual layout position instead of
        // collapsing it, e.g. a start page tile for a sensor this hardware does not currently report
        private string GetTextOrPlaceholder(string value) => string.IsNullOrEmpty(value) ? "--" : value;

        // --- workaround: x:Bind skips function calls whose argument path runs through null ---
        // problem: when a function bindings argument is a multi-segment path like ViewModel.ActualYMaxText and
        // ViewModel is null, x:Bind does not call the function at all and leaves the target at its default; the
        // placeholder text below never showed up, the TextBlock just stayed empty
        // confirmed platform bug: https://github.com/microsoft/microsoft-ui-xaml/issues/2166
        // fix: pass ViewModel itself (a single, always-readable property, not a path through it) and do the
        // null-safe navigation inside the method body instead; GetContentVisibility/GetNotFoundVisibility right
        // above already used this exact pattern and always worked correctly
        private string GetYMaxOrPlaceholder(SensorGraphViewModel viewModel) => GetTextOrPlaceholder(viewModel?.ActualYMaxText);

        private string GetCurrentValueOrPlaceholder(SensorGraphViewModel viewModel) => GetTextOrPlaceholder(viewModel?.CurrentValueText);

        private Brush GetCurrentValueColorOrDefault(SensorGraphViewModel viewModel) => viewModel?.CurrentValueColor ?? DefaultTextColor.Resolve();

        private string GetStatusRowTitleOrPlaceholder(bool showUnit, SensorGraphViewModel viewModel) =>
            viewModel == null ? "--" : GetTextOrPlaceholder(GetStatusRowTitle(showUnit, viewModel.SensorName, viewModel.DisplayNameWithUnit));

        private Brush GetBrushOrDefault(Brush value) => value ?? DefaultTextColor.Resolve();

        // the two status row variants are mutually exclusive; compact wins whenever both ShowStatusRow and
        // ShowCompactStatusRow are set
        private Visibility GetStandardStatusRowVisibility(bool showStatusRow, bool showCompactStatusRow) =>
            showStatusRow && !showCompactStatusRow ? Visibility.Visible : Visibility.Collapsed;

        private Visibility GetCompactStatusRowVisibility(bool showStatusRow, bool showCompactStatusRow) =>
            showStatusRow && showCompactStatusRow ? Visibility.Visible : Visibility.Collapsed;

        // status row shows either the plain sensor name, or name+unit combined, depending on ShowUnitInStatusRow
        private string GetStatusRowTitle(bool showUnit, string name, string nameWithUnit)
        {
            return showUnit ? nameWithUnit : name;
        }

        // switch button and its plain-text fallback share the same cell, exactly one of the two is ever visible
        private Visibility GetSwitchButtonVisibility(ObservableCollection<SensorSwitchCandidate> candidates)
        {
            return IsSwitchUiActive(candidates) ? Visibility.Visible : Visibility.Collapsed;
        }

        private Visibility GetSwitchTextVisibility(ObservableCollection<SensorSwitchCandidate> candidates)
        {
            return IsSwitchUiActive(candidates) ? Visibility.Collapsed : Visibility.Visible;
        }

        // null (never wired up) never shows the switch UI; with exactly one candidate, ShowSwitchUiForSingleCandidate decides
        private bool IsSwitchUiActive(ObservableCollection<SensorSwitchCandidate> candidates)
        {
            if (candidates == null) return false;
            return candidates.Count > 1 || ShowSwitchUiForSingleCandidate;
        }

        // true whenever either tap gesture opens the flyout badge;
        // the compact badge and the full button-based control panel are mutually exclusive, so this single check
        // governs both
        private bool IsFlyoutModeActive(TapAction graphTapAction, TapAction buttonTapAction)
        {
            return graphTapAction == TapAction.ShowFlyout || buttonTapAction == TapAction.ShowFlyout;
        }

        // Y-axis and threshold controls both live inside the same toggleable control panel;
        // flyout mode replaces both entirely when active, so it forces this to Collapsed regardless of the individual
        // Show*Controls properties
        private Visibility GetYAxisControlsVisibility(bool showYAxisControls, TapAction graphTapAction, TapAction buttonTapAction, Visibility controlPanelVisibility)
        {
            return showYAxisControls && !IsFlyoutModeActive(graphTapAction, buttonTapAction) ? controlPanelVisibility : Visibility.Collapsed;
        }

        private Visibility GetThresholdControlsVisibility(bool showThresholdControls, TapAction graphTapAction, TapAction buttonTapAction, Visibility controlPanelVisibility)
        {
            return showThresholdControls && !IsFlyoutModeActive(graphTapAction, buttonTapAction) ? controlPanelVisibility : Visibility.Collapsed;
        }

        // the compact flyout badge only shows up when at least one tap gesture is actually configured to open it
        private Visibility GetThresholdFlyoutVisibility(TapAction graphTapAction, TapAction buttonTapAction)
        {
            return IsFlyoutModeActive(graphTapAction, buttonTapAction) ? Visibility.Visible : Visibility.Collapsed;
        }

        // combines the automatic per-sensor color (resolved via global accent/custom settings) with this instances
        // optional override
        // Alpha 0 on the override means "not set", since a real accent color is never fully transparent
        private Windows.UI.Color GetEffectiveGraphColor(Windows.UI.Color overrideColor, Windows.UI.Color autoColor)
        {
            return overrideColor.A == 0 ? autoColor : overrideColor;
        }

        // bool -> Opacity for the badge; Visibility stays governed by flyout-mode-active (so the control keeps its
        // layout position and ShowFlyout() keeps working), only the visual rendering is toggled here
        private double BoolToOpacity(bool value) => value ? 1.0 : 0.0;

        // translates the panels own simple ShowGraphCardBackground bool into SensorGraphControl.CardBackgroundOverride:
        // true - keeps the graphs normal themed background (no override, null)
        // false - hides it via an explicit fully
        // transparent override - external behavior of ShowGraphCardBackground stays exactly as before
        private Windows.UI.Color? BoolToCardBackgroundOverride(bool showBackground) =>
            showBackground ? null : Windows.UI.Color.FromArgb(0, 0, 0, 0);


        // === event handlers ===

        // see OnOverrideChanged: forces every x:Bind expression in this control to re-evaluate, since the
        // GetXOrPlaceholder functions take ViewModel itself and x:Bind does not track what they read off of it
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Bindings.Update();
        }

        private void GraphControl_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ExecuteTapAction(GraphTapAction);
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteTapAction(ButtonTapAction);
        }

        // keeps the closed comboboxs displayed text in sync with the active sensor
        private void SwitchCandidateComboBox_DropDownOpened(object sender, object e)
        {
            SyncSwitchSelection();
        }

        private void SwitchButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchCandidateComboBox.IsDropDownOpen = true;
        }

        // resolves the pick (builds its graph on first pick, cached after) and hands it to ViewModel
        private void SwitchCandidateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SwitchCandidateComboBox.SelectedItem is not SensorSwitchCandidate candidate) return;
            if (ViewModel != null && candidate.SensorId == ViewModel.SensorId) return;

            ViewModel = candidate.Resolve();
        }


        // === private helpers ===

        // runs whichever action a tap gesture is currently configured for
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

        private void SyncSwitchSelection()
        {
            if (ViewModel == null || SwitchCandidates == null) return;
            SwitchCandidateComboBox.SelectedItem = SwitchCandidates.FirstOrDefault(c => c.SensorId == ViewModel.SensorId);
        }
    }
}