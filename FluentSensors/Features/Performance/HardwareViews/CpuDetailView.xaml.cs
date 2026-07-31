using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // self-contained CPU detail view: everything shown once a CPU nav item is selected, including its own
    // Overall/All-Threads toggle bar
    public sealed partial class CpuDetailView : UserControl
    {
        // === fields ===

        // below this width, the wide 3-graph layout (big Load graph + 2 stacked) switches to the narrow layout
        // (all 3 stacked equally)
        private const double NarrowGraphsLayoutThreshold = 600;


        // === constructor ===

        public CpuDetailView()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        public LhmCpuInstanceViewModel Cpu
        {
            get => (LhmCpuInstanceViewModel)GetValue(CpuProperty);
            set => SetValue(CpuProperty, value);
        }

        public static readonly DependencyProperty CpuProperty =
            DependencyProperty.Register(
                nameof(Cpu),
                typeof(LhmCpuInstanceViewModel),
                typeof(CpuDetailView),
                new PropertyMetadata(null, OnCpuChanged));

        // kept as a safety net: PerformancePage caches one permanent view per hardware instance and sets Cpu
        // exactly once, so this should never fire with a changing value in practice
        // but if that ever changes, this forces the x:Binds to re-evaluate instead of silently going stale
        private static void OnCpuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CpuDetailView view) view.Bindings.Update();
        }


        // === event handlers ===

        private void ShowOverall_Click(object sender, RoutedEventArgs e)
        {
            if (Cpu != null) Cpu.IsShowingAllThreads = false;
        }

        private void ShowAllThreads_Click(object sender, RoutedEventArgs e)
        {
            if (Cpu != null) Cpu.IsShowingAllThreads = true;
        }

        // keeps the overview block at least as tall as the visible viewport (so its graphs can stretch to fill
        // it), but lets it grow past that, and let the ScrollViewer take over once its natural minimum height
        // (graph MinHeight + tiles/static info) no longer fits
        private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double availableHeight = e.NewSize.Height - verticalPadding;

            // the tiles/static info row has no Star rows in it, just plain Auto content (TextBlocks)
            // so asking it directly for its natural height via Measure is fully reliable
            TilesAndStaticInfoGrid.Measure(new Size(ContentScrollViewer.ActualWidth, double.PositiveInfinity));
            double tilesAndStaticInfoHeight = TilesAndStaticInfoGrid.DesiredSize.Height;

            // the graphs areas own minimum is exactly whatever MinHeight you set on the currently active
            // layout (Wide or Narrow)
            // reading it back here means you only ever have to change it in one place
            double graphsMinHeight = WideGraphsGrid.Visibility == Visibility.Visible
                ? WideGraphsGrid.MinHeight
                : NarrowGraphsPanel.MinHeight;

            double naturalMinHeight = graphsMinHeight + OverviewBlockGrid.RowSpacing + tilesAndStaticInfoHeight;

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