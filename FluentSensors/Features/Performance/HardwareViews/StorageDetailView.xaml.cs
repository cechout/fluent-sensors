using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // self-contained storage detail view: total activity (big) + read/write rate stacked, same shape as CPU,
    // including the wide/narrow switch
    public sealed partial class StorageDetailView : UserControl
    {
        // === fields ===

        // below this width, the wide 3-graph layout (big Activity graph + 2 stacked) switches to the narrow
        // layout (all 3 stacked equally)
        private const double NarrowGraphsLayoutThreshold = 600;


        // === constructor ===

        public StorageDetailView()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        public LhmStorageInstanceViewModel Storage
        {
            get => (LhmStorageInstanceViewModel)GetValue(StorageProperty);
            set => SetValue(StorageProperty, value);
        }

        public static readonly DependencyProperty StorageProperty =
            DependencyProperty.Register(
                nameof(Storage),
                typeof(LhmStorageInstanceViewModel),
                typeof(StorageDetailView),
                new PropertyMetadata(null, OnStorageChanged));

        // kept as a safety net: PerformancePage caches one permanent view per hardware instance and sets Storage
        // exactly once, so this should never fire with a changing value in practice
        // but if that ever changes, this forces the x:Binds to re-evaluate instead of silently going stale
        private static void OnStorageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StorageDetailView view) view.Bindings.Update();
        }


        // === event handlers ===

        // keeps the overview block at least as tall as the visible viewport (so its graphs can stretch to fill
        // it), but lets it grow past that, and let the ScrollViewer take over once its natural minimum height
        // no longer fits
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