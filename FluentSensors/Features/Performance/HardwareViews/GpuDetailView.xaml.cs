using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // self-contained GPU detail view: everything shown once a GPU nav item is selected, including its own
    // Overall/Extended toggle bar
    public sealed partial class GpuDetailView : UserControl
    {
        // === fields ===

        // below this width, the wide 3-graph layout (big Load graph + 2 stacked) switches to the narrow layout
        // (all 3 stacked equally)
        private const double NarrowGraphsLayoutThreshold = 700;
        private bool _isNarrowLayoutActive;
        private bool _extendedTimeSpanHookAttached;


        // === constructor ===

        public GpuDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.ApplyTimeSpan(OverviewBlockGrid, PerformanceGraphDefaults.StandardTimeSpanSeconds);
        }


        // === bindable properties ===

        // graph color for every SensorPanelControl in this view; single source of truth in HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).IconGlyph;


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

        private void ShowOverall_Click(object sender, RoutedEventArgs e)
        {
            if (Gpu != null) Gpu.IsShowingExtended = false;
            RecalculateOverviewHeight();
            SyncSectionRenderingGate();
        }

        private void ShowExtended_Click(object sender, RoutedEventArgs e)
        {
            if (Gpu != null) Gpu.IsShowingExtended = true;
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

        // recomputes whichever section (Overview or Extended) is currently shown
        //
        // Called both by the handlers above and externally by PerformancePage after a nav sidebar/info panel toggle,
        // since that changes DetailHostGrids available size without necessarily firing SizeChanged on this control
        // quickly enough
        public void RecalculateOverviewHeight()
        {
            if (Gpu == null) return;

            if (Gpu.IsShowingExtended)
            {
                OverviewBlockGrid.Height = 0;

                // ExtendedGrid is x:Load="False"; FindName forces it into the tree the first time Extended is
                // actually selected, and is a cheap no-op on every call after that
                FindName(nameof(ExtendedGrid));
                ExtendedGrid.Height = double.NaN;

                if (!_extendedTimeSpanHookAttached)
                {
                    _extendedTimeSpanHookAttached = true;
                    ExtendedGrid.Loaded += (s, e) =>
                        PerformanceGraphDefaults.ApplyTimeSpan(ExtendedGrid, PerformanceGraphDefaults.GpuExtendedTimeSpanSeconds);
                }
            }
            else
            {
                UpdateOverviewHeight();

                // still null if Extended was never selected this session; nothing to size in that case
                if (ExtendedGrid != null) ExtendedGrid.Height = 0;
            }
        }

        // mirrors CpuDetailView.SyncSectionRenderingGate
        public void SyncSectionRenderingGate()
        {
            if (Gpu == null) return;

            PerformanceGraphDefaults.SetGraphsRenderingActive(OverviewBlockGrid, !Gpu.IsShowingExtended);

            // still null if Extended was never selected this session; nothing to gate in that case
            if (ExtendedGrid != null)
            {
                PerformanceGraphDefaults.SetGraphsRenderingActive(ExtendedGrid, Gpu.IsShowingExtended);
            }
        }

        // keeps the overview block at least as tall as the visible viewport (so its graphs can stretch to fill it),
        // but lets it grow past that, and let the ScrollViewer take over once its natural minimum height
        // (graph MinHeight + tiles) no longer fits
        private void UpdateOverviewHeight()
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

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem: same root cause as PerformancePage.xaml.cs UpdateDetailView (see that comment for the full
        // explanation)
        // fix: never Collapse either layout, toggle Opacity + IsHitTestVisible instead
        private static void SetLayoutActive(FrameworkElement wideLayout, FrameworkElement narrowLayout, bool useNarrow)
        {
            wideLayout.Opacity = useNarrow ? 0 : 1;
            wideLayout.IsHitTestVisible = !useNarrow;

            narrowLayout.Opacity = useNarrow ? 1 : 0;
            narrowLayout.IsHitTestVisible = useNarrow;
        }
    }
}
