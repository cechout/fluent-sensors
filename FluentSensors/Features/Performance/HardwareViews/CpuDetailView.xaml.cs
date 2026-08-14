using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
using FluentSensors.Diagnostics;
using FluentSensors.Features.Performance;
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
        private const double NarrowGraphsLayoutThreshold = 700;
        private bool _isNarrowLayoutActive;
        private bool _allThreadsTimeSpanHookAttached;

        // vertical gap between the "Cores With Threads" and "Cores Without Threads" groups
        // (only applied when both are shown)
        private const double CoreGroupSpacing = 24;


        // === constructor ===

        public CpuDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.ApplyTimeSpan(OverviewBlockGrid, PerformanceGraphDefaults.StandardTimeSpanSeconds);
        }


        // === bindable properties ===

        // cpu graphs color (TotalLoad, MaxTemperature, PackagePower); single source of truth in
        // HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).Color;

        // same color as HardwareColor, wrapped as a Brush (for hardware icon?)
        //public SolidColorBrush HardwareColorBrush => new(HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).Color);

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).IconGlyph;


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

        private static void OnCpuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CpuDetailView view) view.Bindings.Update();
        }


        // === event handlers ===

        private void ShowOverall_Click(object sender, RoutedEventArgs e)
        {
            if (Cpu != null) Cpu.IsShowingAllThreads = false;
            RecalculateOverviewHeight();
            SyncSectionRenderingGate();
        }

        private void ShowAllThreads_Click(object sender, RoutedEventArgs e)
        {
            if (Cpu != null) Cpu.IsShowingAllThreads = true;
            RecalculateOverviewHeight();
            SyncSectionRenderingGate();
        }

        private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RecalculateOverviewHeight();
        }

        private void GraphsAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _isNarrowLayoutActive = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            SetLayoutActive(WideGraphsGrid, NarrowGraphsPanel, _isNarrowLayoutActive);
            RecalculateOverviewHeight();
        }


        // === private helpers ===

        // recomputes whichever section (Overview or All Threads) is currently shown
        //
        // Called both by the handlers above and externally by PerformancePage after a nav sidebar/info panel toggle,
        // since that changes DetailHostGrids available size without necessarily firing SizeChanged on this control
        // quickly enough
        public void RecalculateOverviewHeight()
        {
            if (Cpu == null) return;

            if (Cpu.IsShowingAllThreads)
            {
                OverviewBlockGrid.Height = 0;

                // AllThreadsGrid is x:Load="False"; FindName forces it into the tree the first time All Threads is
                // actually selected, and is a cheap no-op on every call after that
                FindName(nameof(AllThreadsGrid));
                AllThreadsGrid.Height = double.NaN;

                if (!_allThreadsTimeSpanHookAttached)
                {
                    _allThreadsTimeSpanHookAttached = true;
                    AllThreadsGrid.Loaded += (s, e) =>
                        PerformanceGraphDefaults.ApplyTimeSpan(AllThreadsGrid, PerformanceGraphDefaults.CpuThreadTimeSpanSeconds);
                }
            }
            else
            {
                UpdateOverviewHeight();

                // still null if All Threads was never selected this session; nothing to size in that case
                if (AllThreadsGrid != null) AllThreadsGrid.Height = 0;
            }
        }

        // keeps only the currently shown section (Overview or All Threads) actually rendering; the other ones
        // graphs get gated off exactly like a whole hidden detail view does
        public void SyncSectionRenderingGate()
        {
            if (Cpu == null) return;

            PerformanceGraphDefaults.SetGraphsRenderingActive(OverviewBlockGrid, !Cpu.IsShowingAllThreads);

            // still null if All Threads was never selected this session; nothing to gate in that case
            if (AllThreadsGrid != null)
            {
                PerformanceGraphDefaults.SetGraphsRenderingActive(AllThreadsGrid, Cpu.IsShowingAllThreads);
            }
        }

        // keeps the overview block at least as tall as the visible viewport (so its graphs can stretch to fill it),
        // but lets it grow past that, and let the ScrollViewer take over once its natural minimum height
        // (graph MinHeight + tiles/static info) no longer fits
        private void UpdateOverviewHeight()
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double headerHeight = HeaderGrid.ActualHeight + ContentStackPanel.Spacing;
            double availableHeight = ContentScrollViewer.ActualHeight - verticalPadding - headerHeight;

            double horizontalPadding = ContentStackPanel.Padding.Left + ContentStackPanel.Padding.Right;
            TilesAndStaticInfoGrid.Measure(new Size(ContentScrollViewer.ActualWidth - horizontalPadding, double.PositiveInfinity));
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

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // gap above the second core group, collapses to 0 together with the group above it instead of leaving a stray
        // RowSpacing-style gap, see the workaround note on AllThreadsGrid in the XAML
        private Thickness GroupSpacingMargin(bool showSplit) => showSplit ? new Thickness(0, CoreGroupSpacing, 0, 0) : new Thickness(0);
    }
}