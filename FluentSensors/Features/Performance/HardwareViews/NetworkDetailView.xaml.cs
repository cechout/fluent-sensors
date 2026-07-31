using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    public sealed partial class NetworkDetailView : UserControl
    {
        // === fields ===

        private const double NarrowGraphsLayoutThreshold = 600;
        private bool _isNarrowLayoutActive;


        // === constructor ===

        public NetworkDetailView()
        {
            InitializeComponent();
        }


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
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double availableHeight = e.NewSize.Height - verticalPadding;

            TilesGrid.Measure(new Size(ContentScrollViewer.ActualWidth, double.PositiveInfinity));
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