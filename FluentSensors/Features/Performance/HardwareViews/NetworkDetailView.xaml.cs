using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    public sealed partial class NetworkDetailView : UserControl
    {
        // === fields ===

        // below this width, the wide 3-graph layout (big Utilization graph + 2 stacked) switches to the narrow
        // layout (all 3 stacked equally)
        private const double NarrowGraphsLayoutThreshold = 700;
        private bool _isNarrowLayoutActive;


        // === constructor ===

        public NetworkDetailView()
        {
            InitializeComponent();

            // literal xaml children, already exist right after InitializeComponent, no need to wait for Loaded
            PerformanceGraphDefaults.ApplyTimeSpan(OverviewBlockGrid, PerformanceGraphDefaults.StandardTimeSpanSeconds);
        }


        // === bindable properties ===

        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Network).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Network).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Network).IconGlyph;


        // === dependency properties ===

        public LhmNetworkInstanceViewModel Network
        {
            get => (LhmNetworkInstanceViewModel)GetValue(NetworkProperty);
            set => SetValue(NetworkProperty, value);
        }

        public static readonly DependencyProperty NetworkProperty =
            DependencyProperty.Register(
                nameof(Network),
                typeof(LhmNetworkInstanceViewModel),
                typeof(NetworkDetailView),
                new PropertyMetadata(null, OnNetworkChanged));

        private static void OnNetworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NetworkDetailView view) view.Bindings.Update();
        }


        // === event handlers ===

        private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RecalculateOverviewHeight();
        }

        // recomputes OverviewBlockGrid.Height from the scroll viewers current size
        //
        // Called both by ContentScrollViewer_SizeChanged above and externally by PerformancePage after a nav
        // sidebar/info panel toggle, since that changes DetailHostGrids available size without necessarily firing
        // SizeChanged on this control quickly enough
        public void RecalculateOverviewHeight()
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double headerHeight = HeaderGrid.ActualHeight + ContentStackPanel.Spacing;
            double availableHeight = ContentScrollViewer.ActualHeight - verticalPadding - headerHeight;

            double horizontalPadding = ContentStackPanel.Padding.Left + ContentStackPanel.Padding.Right;
            TilesGrid.Measure(new Size(ContentScrollViewer.ActualWidth - horizontalPadding, double.PositiveInfinity));
            double tilesHeight = TilesGrid.DesiredSize.Height;

            double graphsMinHeight = _isNarrowLayoutActive ? NarrowGraphsPanel.MinHeight : WideGraphsGrid.MinHeight;

            double naturalMinHeight = graphsMinHeight + OverviewBlockGrid.RowSpacing + tilesHeight;
            OverviewBlockGrid.Height = Math.Max(availableHeight, naturalMinHeight);
        }

        private void GraphsAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _isNarrowLayoutActive = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            SetLayoutActive(WideGraphsGrid, NarrowGraphsPanel, _isNarrowLayoutActive);
        }


        // === private helpers ===

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem/fix: see GpuDetailView.xaml.cs SetLayoutActive for the full explanation
        private static void SetLayoutActive(FrameworkElement wideLayout, FrameworkElement narrowLayout, bool useNarrow)
        {
            wideLayout.Opacity = useNarrow ? 0 : 1;
            wideLayout.IsHitTestVisible = !useNarrow;

            narrowLayout.Opacity = useNarrow ? 1 : 0;
            narrowLayout.IsHitTestVisible = useNarrow;
        }
    }
}