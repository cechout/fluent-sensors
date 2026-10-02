using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // the GPU detail view:
    // everything shown for a selected GPU, its Overall/Extended toggle bar included
    public sealed partial class GpuDetailView : UserControl
    {
        // === fields ===

        // below it the wide layout (a big graph beside two stacked) turns narrow (all three stacked)
        private const double NarrowGraphsLayoutThreshold = 700;
        private bool _isNarrowLayoutActive;
        private bool _extendedTimeSpanHookAttached;


        // === constructor ===

        public GpuDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.BindTimeSpan(OverviewBlockGrid, PerformanceGraphKind.Standard);
            HardwareIconColorBinding.Bind(this, () => Bindings.Update());
        }


        // === bindable properties ===

        // the graph colour of this view, from HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).IconGlyph;

        // header icon colour, re-read by HardwareIconColorBinding (the graph colour stays)
        public SolidColorBrush GroupIconBrush => HardwareGroupInfo.GetIconBrush(HardwareGroupKind.Gpu);


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

        // after a drag the content column fills the rest again, and the width goes to the view model, which every
        // hardware view sizes its info panel from
        private void InfoPanelSplitter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            double width = InfoPanelColumn.ActualWidth;
            InfoPanelColumn.Width = new GridLength(width);
            ContentColumn.Width = new GridLength(1, GridUnitType.Star);
            PerformanceViewModel.Instance.InfoPanelWidth = width;
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

        // sizes the shown section, Overview or Extended; also from PerformancePage after a panel toggle, whose size
        // change can reach this control late
        public void RecalculateOverviewHeight()
        {
            if (Gpu == null) return;

            if (Gpu.IsShowingExtended)
            {
                OverviewBlockGrid.Height = 0;

                // x:Load="False", FindName builds it on the first visit
                FindName(nameof(ExtendedGrid));
                ExtendedGrid.Height = double.NaN;

                if (!_extendedTimeSpanHookAttached)
                {
                    _extendedTimeSpanHookAttached = true;
                    PerformanceGraphDefaults.BindTimeSpan(ExtendedGrid, PerformanceGraphKind.Extended);
                }
            }
            else
            {
                UpdateOverviewHeight();

                // null until Extended was shown once
                if (ExtendedGrid != null) ExtendedGrid.Height = 0;
            }
        }

        // mirrors CpuDetailView.SyncSectionRenderingGate
        public void SyncSectionRenderingGate()
        {
            if (Gpu == null) return;

            SensorGraphRenderingGate.SetActive(OverviewBlockGrid, !Gpu.IsShowingExtended);

            // null until Extended was shown once
            if (ExtendedGrid != null)
            {
                SensorGraphRenderingGate.SetActive(ExtendedGrid, Gpu.IsShowingExtended);
            }

            // the walk woke the hidden layout of Wide and Narrow too; back down to the shown one
            if (!Gpu.IsShowingExtended)
            {
                SensorGraphRenderingGate.SetActive(_isNarrowLayoutActive ? WideGraphsGrid : NarrowGraphsPanel, false);
            }
        }

        // the overview block at least as tall as the viewport, so its graphs stretch; past its minimum (graphs plus
        // tiles) the ScrollViewer takes over
        private void UpdateOverviewHeight()
        {
            // read off the live tree, see OverviewBlockSizing
            double contentWidth = OverviewBlockSizing.ContentWidth(ContentScrollViewer, ContentStackPanel, OverviewBlockGrid);
            TilesGrid.Measure(new Size(contentWidth, double.PositiveInfinity));
            double tilesHeight = TilesGrid.DesiredSize.Height;

            double graphsMinHeight = _isNarrowLayoutActive ? NarrowGraphsPanel.MinHeight : WideGraphsGrid.MinHeight;

            double naturalMinHeight = graphsMinHeight + OverviewBlockGrid.RowSpacing + tilesHeight;

            OverviewBlockGrid.Height = OverviewBlockSizing.Height(
                ContentScrollViewer, ContentStackPanel, OverviewBlockGrid, naturalMinHeight);
        }

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem: the root cause of PerformancePage.UpdateDetailView
        // fix: neither layout is ever Collapsed, they toggle Opacity and IsHitTestVisible
        // the render gate goes along (Opacity 0 does not stop rendering), but only while this view is selected and
        // shows Overview: DetailHostGrid stacks every view, so a width change reaches all of them;
        // SyncSectionRenderingGate catches up later
        private void SetLayoutActive(FrameworkElement wideLayout, FrameworkElement narrowLayout, bool useNarrow)
        {
            wideLayout.Opacity = useNarrow ? 0 : 1;
            wideLayout.IsHitTestVisible = !useNarrow;

            narrowLayout.Opacity = useNarrow ? 1 : 0;
            narrowLayout.IsHitTestVisible = useNarrow;

            if (!IsHitTestVisible || Gpu == null || Gpu.IsShowingExtended) return;

            SensorGraphRenderingGate.SetActive(wideLayout, !useNarrow);
            SensorGraphRenderingGate.SetActive(narrowLayout, useNarrow);
        }
    }
}
