using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    public sealed partial class GpuDetailView : UserControl
    {
        // === fields ===

        private const double NarrowGraphsLayoutThreshold = 600;


        // === constructor ===

        public GpuDetailView()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        public LhmGpuInstanceViewModel Gpu
        {
            get => (LhmGpuInstanceViewModel)GetValue(GpuProperty);
            set => SetValue(GpuProperty, value);
        }

        public static readonly DependencyProperty GpuProperty =
            DependencyProperty.Register(
                nameof(Gpu),
                typeof(LhmGpuInstanceViewModel),
                typeof(GpuDetailView),
                new PropertyMetadata(null, OnGpuChanged));

        private static void OnGpuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GpuDetailView view) view.Bindings.Update();
        }


        // === event handlers ===

        private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double availableHeight = e.NewSize.Height - verticalPadding;

            TilesGrid.Measure(new Size(ContentScrollViewer.ActualWidth, double.PositiveInfinity));
            double tilesHeight = TilesGrid.DesiredSize.Height;

            double row1MinHeight = WideGraphsGrid1.Visibility == Visibility.Visible ? WideGraphsGrid1.MinHeight : NarrowGraphsPanel1.MinHeight;
            double row2MinHeight = WideGraphsGrid2.Visibility == Visibility.Visible ? WideGraphsGrid2.MinHeight : NarrowGraphsPanel2.MinHeight;

            double naturalMinHeight = row1MinHeight + row2MinHeight + (OverviewBlockGrid.RowSpacing * 2) + tilesHeight;
            OverviewBlockGrid.Height = Math.Max(availableHeight, naturalMinHeight);
        }

        private void GraphsAreaGrid1_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool useNarrow = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            WideGraphsGrid1.Visibility = useNarrow ? Visibility.Collapsed : Visibility.Visible;
            NarrowGraphsPanel1.Visibility = useNarrow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void GraphsAreaGrid2_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool useNarrow = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            WideGraphsGrid2.Visibility = useNarrow ? Visibility.Collapsed : Visibility.Visible;
            NarrowGraphsPanel2.Visibility = useNarrow ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}