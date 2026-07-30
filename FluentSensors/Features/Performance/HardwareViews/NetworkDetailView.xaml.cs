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

            double graphsMinHeight = WideGraphsGrid.Visibility == Visibility.Visible
                ? WideGraphsGrid.MinHeight
                : NarrowGraphsPanel.MinHeight;

            double naturalMinHeight = graphsMinHeight + OverviewBlockGrid.RowSpacing + tilesHeight;
            OverviewBlockGrid.Height = Math.Max(availableHeight, naturalMinHeight);
        }

        private void GraphsAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool useNarrow = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            WideGraphsGrid.Visibility = useNarrow ? Visibility.Collapsed : Visibility.Visible;
            NarrowGraphsPanel.Visibility = useNarrow ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}