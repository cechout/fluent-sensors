using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Common.Sensors;
using FluentSensors.Persistence.Services;

namespace FluentSensors.Features.TaskbarWidget
{
    // Keep the graph alive when its compact card is selected so switching style does not discard history.
    public sealed class TaskbarSensorSlot : UserControl
    {
        private readonly Grid _root = new();
        private readonly TextBlock _label = new() { FontSize = 8, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _value = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None };
        private readonly Rectangle _level = new() { VerticalAlignment = VerticalAlignment.Bottom };
        private readonly SolidColorBrush _levelBrush = new();
        private readonly Border _card;
        private readonly Border _track;
        private SensorPanelControl? _graph;
        private SensorGraphViewModel? _subscribed;
        private bool _loaded;
        private bool _measureQueued;
        private string? _measuredText;
        private sealed class CardMeasure { internal int Width; }
        private static readonly ConditionalWeakTable<SensorGraphViewModel, CardMeasure> CardWidths = new();
        internal static event Action? PreferredWidthChanged;

        public TaskbarSensorSlot()
        {
            IsHitTestVisible = false;
            ActualThemeChanged += (_, _) => Refresh();
            var content = new Grid { ColumnSpacing = 3 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_label);
            text.Children.Add(_value);
            content.Children.Add(text);
            _track = new Border { CornerRadius = new CornerRadius(1), Background = Resource("ControlStrongFillColorDefaultBrush"), Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Stretch, Child = _level };
            _track.SizeChanged += (_, _) => RefreshValue();
            Grid.SetColumn(_track, 1);
            _level.Fill = _levelBrush;
            content.Children.Add(_track);
            // SensorPanelControl leaves its collapsed header row's 3-DIP gap above the graph.
            _card = new Border { CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(4, 1, 4, 1), Background = Resource("CardBackgroundFillColorDefaultBrush"), Child = content };
            _root.Children.Add(_card);
            Content = _root;
            Loaded += (_, _) =>
            {
                _loaded = true;
                Subscribe();
                SensorSelectionService.Instance.TaskbarSensorPlacementChanged += QueueRefresh;
                SettingsService.Instance.TaskbarGraphWidthChanged += OnWidthChanged;
                Refresh();
            };
            Unloaded += (_, _) =>
            {
                _loaded = false;
                Unsubscribe();
                SensorSelectionService.Instance.TaskbarSensorPlacementChanged -= QueueRefresh;
                SettingsService.Instance.TaskbarGraphWidthChanged -= OnWidthChanged;
            };
        }

        private Brush Resource(string key)
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
            bool light = ActualTheme == ElementTheme.Light;
            return new SolidColorBrush(key == "AccentFillColorDefaultBrush" ? Windows.UI.Color.FromArgb(255, 53, 204, 219)
                : key == "ControlStrongFillColorDefaultBrush" ? Windows.UI.Color.FromArgb(255, 100, 104, 108)
                : light ? Windows.UI.Color.FromArgb(180, 235, 235, 235) : Windows.UI.Color.FromArgb(180, 55, 58, 60));
        }
        public SensorGraphViewModel ViewModel
        {
            get => (SensorGraphViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(SensorGraphViewModel), typeof(TaskbarSensorSlot), new PropertyMetadata(null, OnViewModelChanged));
        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var slot = (TaskbarSensorSlot)d;
            slot.Unsubscribe();
            slot._measuredText = null;
            if (slot._graph != null) slot._root.Children.Remove(slot._graph);
            var vm = slot.ViewModel;
            if (vm == null) return;
            slot._graph = new SensorPanelControl
            {
                ViewModel = vm,
                IsHitTestVisible = false,
                IsGraphHoverEnabled = false,
                ShowGraphCardBackground = vm.IsCardBackgroundVisible,
                ShowGraphCardBorder = false,
                ShowGraphLabel = true,
                ShowStatusRow = false,
                ShowThresholdControls = false,
                ShowThresholdFlyoutIndicator = false,
                ShowYAxisControls = false,
                ThresholdLabelAlwaysVisible = false,
                UseGraphColorCardBackground = true
            };
            slot._root.Children.Insert(0, slot._graph);
            if (slot._loaded) slot.Subscribe();
            slot.Refresh();
        }
        internal static int WidthFor(SensorGraphViewModel sensor) => SensorSelectionService.Instance.IsCompactTaskbarSensor(sensor.SensorId)
            ? Math.Max(sensor.SensorType == "Throughput" ? 76 : 46, CardWidths.TryGetValue(sensor, out var measured) ? measured.Width : 0)
            : SettingsService.Instance.TaskbarGraphWidthDip;

        // Fit the complete live value and unit. The window uses the same measured width when
        // positioning the row; updates wait until the current UI callback has finished.
        private void QueueMeasure()
        {
            if (!_loaded || _measureQueued || ViewModel == null || !SensorSelectionService.Instance.IsCompactTaskbarSensor(ViewModel.SensorId)) return;
            string text = _label.Text + "\n" + _value.Text;
            if (_measuredText == text) return;
            _measureQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _measureQueued = false;
                if (!_loaded || ViewModel == null || !SensorSelectionService.Instance.IsCompactTaskbarSensor(ViewModel.SensorId)) return;
                _measuredText = _label.Text + "\n" + _value.Text;
                var available = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
                _label.Measure(available);
                _value.Measure(available);
                int required = (int)Math.Ceiling(Math.Max(_label.DesiredSize.Width, _value.DesiredSize.Width)) + 17;
                var measured = CardWidths.GetValue(ViewModel, _ => new CardMeasure());
                if (required == measured.Width) return;
                measured.Width = required;
                double width = WidthFor(ViewModel);
                if (Width == width) return;
                Width = width;
                PreferredWidthChanged?.Invoke();
            });
        }
        private void Subscribe()
        {
            _subscribed = ViewModel;
            if (_subscribed != null) _subscribed.PropertyChanged += OnSensorChanged;
        }
        private void Unsubscribe()
        {
            if (_subscribed != null) _subscribed.PropertyChanged -= OnSensorChanged;
            _subscribed = null;
        }
        private void OnSensorChanged(object? sender, PropertyChangedEventArgs e) => RefreshValue();
        private void OnWidthChanged(int width) => Refresh();
        private void QueueRefresh() => DispatcherQueue.TryEnqueue(() => { if (_loaded) Refresh(); });
        private void Refresh()
        {
            if (ViewModel == null) return;
            bool compact = SensorSelectionService.Instance.IsCompactTaskbarSensor(ViewModel.SensorId);
            Width = WidthFor(ViewModel);
            _card.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            _card.Background = Resource("CardBackgroundFillColorDefaultBrush");
            if (_graph != null)
            {
                _graph.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                SensorGraphRenderingGate.SetActive(_graph, !compact && _loaded);
            }
            RefreshValue();
        }
        private void RefreshValue()
        {
            var vm = ViewModel;
            if (vm == null) return;
            // Match the live graph: the sensor threshold overrides its base graph color when breached.
            _levelBrush.Color = vm.IsThresholdColorActive ? vm.Threshold.Color : vm.GraphColor.ForTheme(ActualTheme == ElementTheme.Dark);
            _label.Text = vm.SensorType == "Temperature" ? vm.SensorName.Contains("Max", StringComparison.OrdinalIgnoreCase) ? "MAX" : vm.SensorName.Contains("Average", StringComparison.OrdinalIgnoreCase) ? "AVG" : vm.HardwareKind == HardwareGroupKind.Cpu ? "CPU" : "TMP" : vm.SensorType == "Throughput"
                ? vm.SensorName.Contains("Upload", StringComparison.OrdinalIgnoreCase) ? "UP" : "DOWN"
                : vm.SensorName.Contains("Memory", StringComparison.OrdinalIgnoreCase) ? "RAM"
                : vm.SensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase) ? "GPU" : "CPU";
            _value.Text = vm.CurrentValueText;
            double raw = vm.SensorData.LastOrDefault() ?? 0;
            if (vm.CurrentValueText != "-" && vm.SensorType is "Temperature" or "Load" or "Level")
                _value.Text = raw.ToString("0", CultureInfo.CurrentCulture) + (vm.SensorType == "Temperature" ? "°C" : "%");
            QueueMeasure();
            if (_graph != null) _graph.ShowGraphCardBackground = vm.IsCardBackgroundVisible;
            double maximum = vm.SensorType is "Temperature" or "Load" or "Level" ? 100 : Math.Max(1, vm.SensorData.DefaultIfEmpty(1).Max() ?? 1);
            _level.Height = _track.ActualHeight * Math.Clamp(raw / maximum, 0, 1);
        }
    }
}
