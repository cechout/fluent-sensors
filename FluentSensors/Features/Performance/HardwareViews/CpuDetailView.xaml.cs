using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // self-contained CPU detail view:
    // everything shown once a CPU nav item is selected, including its own Overall/All-Threads toggle bar;
    // instantiated by HardwareDetailTemplateSelector whenever the selected nav items Target is an
    // LhmCpuInstanceViewModel
    public sealed partial class CpuDetailView : UserControl
    {
        // === fields ===

        // below this width, the wide 3-graph layout (big Load graph + 2 stacked) switches to the narrow layout
        // (all 3 stacked equally) 
        private const double NarrowGraphsLayoutThreshold = 400;


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
                new PropertyMetadata(null));


        // === event handlers ===

        private void ShowOverall_Click(object sender, RoutedEventArgs e)
        {
            if (Cpu != null) Cpu.IsShowingAllThreads = false;
        }

        private void ShowAllThreads_Click(object sender, RoutedEventArgs e)
        {
            if (Cpu != null) Cpu.IsShowingAllThreads = true;
        }

        private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double availableHeight = e.NewSize.Height - verticalPadding;

            // the tiles/static info row has no Star rows in it, just plain Auto content (TextBlocks) - so asking
            // it directly for its natural height via Measure is fully reliable, no ScrollViewer-infinite-height
            // guessing involved here at all
            TilesAndStaticInfoGrid.Measure(new Size(ContentScrollViewer.ActualWidth, double.PositiveInfinity));
            double tilesAndStaticInfoHeight = TilesAndStaticInfoGrid.DesiredSize.Height;

            // the graphs area's own minimum is exactly whatever MinHeight you set on the currently active
            // layout (Wide or Narrow) - reading it back here means you only ever have to change it in one place
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