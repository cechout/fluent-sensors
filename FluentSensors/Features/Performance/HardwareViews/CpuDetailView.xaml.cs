using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
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
        private bool _isNarrowLayoutActive;


        // === constructor ===

        public CpuDetailView()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        // overview graph color (TotalLoad, MaxTemperature, PackagePower); single source of truth in
        // HardwareGroupInfo
        // the per-core Temperature/Clock graphs in CpuCoreCellTemplate deliberately keep their own fixed colors
        // instead, to stay visually distinguishable from each other within one dense cell
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).Color;

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
            UpdatePageContentHeight();
        }

        private void ShowAllThreads_Click(object sender, RoutedEventArgs e)
        {
            if (Cpu != null) Cpu.IsShowingAllThreads = true;
            UpdatePageContentHeight();
        }

        private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageContentHeight();
        }

        private void GraphsAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _isNarrowLayoutActive = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            SetLayoutActive(WideGraphsGrid, NarrowGraphsPanel, _isNarrowLayoutActive);
            UpdatePageContentHeight();
        }


        // === private helpers ===

        private void UpdatePageContentHeight()
        {
            if (Cpu == null) return;

            if (Cpu.IsShowingAllThreads)
            {
                OverviewBlockGrid.Height = 0;
                AllThreadsGrid.Height = double.NaN;
            }
            else
            {
                UpdateOverviewHeight();
                AllThreadsGrid.Height = 0;
            }
        }

        // keeps the overview block at least as tall as the visible viewport (so its graphs can stretch to fill
        // it), but lets it grow past that, and let the ScrollViewer take over once its natural minimum height
        // (graph MinHeight + tiles/static info) no longer fits
        private void UpdateOverviewHeight()
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double availableHeight = ContentScrollViewer.ActualHeight - verticalPadding;

            TilesAndStaticInfoGrid.Measure(new Size(ContentScrollViewer.ActualWidth, double.PositiveInfinity));
            double tilesAndStaticInfoHeight = TilesAndStaticInfoGrid.DesiredSize.Height;

            double graphsMinHeight = _isNarrowLayoutActive ? NarrowGraphsPanel.MinHeight : WideGraphsGrid.MinHeight;

            double naturalMinHeight = graphsMinHeight + OverviewBlockGrid.RowSpacing + tilesAndStaticInfoHeight;

            OverviewBlockGrid.Height = Math.Max(availableHeight, naturalMinHeight);
        }

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