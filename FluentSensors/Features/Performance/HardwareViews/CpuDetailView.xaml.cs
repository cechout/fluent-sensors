using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
using FluentSensors.Controls.SensorGraph;
using FluentSensors.Diagnostics;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // the CPU detail view:
    // everything shown for a selected CPU, its Overall/All Threads toggle bar included
    public sealed partial class CpuDetailView : UserControl
    {
        // === fields ===

        // below it the wide layout (a big graph beside two stacked) turns narrow (all three stacked)
        private const double NarrowGraphsLayoutThreshold = 700;
        private bool _isNarrowLayoutActive;
        private bool _allThreadsTimeSpanHookAttached;

        // between the two core groups, when both show
        private const double CoreGroupSpacing = 24;


        // === constructor ===

        public CpuDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.BindTimeSpan(OverviewBlockGrid, PerformanceGraphKind.Standard);
            HardwareIconColorBinding.Bind(this, () => Bindings.Update());
        }


        // === bindable properties ===

        // the graph colour of this view, from HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Cpu).IconGlyph;

        // header icon colour, re-read by HardwareIconColorBinding (the graph colour stays)
        public SolidColorBrush GroupIconBrush => HardwareGroupInfo.GetIconBrush(HardwareGroupKind.Cpu);


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

        // sizes the shown section, Overview or All Threads; also from PerformancePage after a panel toggle, whose size
        // change can reach this control late
        public void RecalculateOverviewHeight()
        {
            if (Cpu == null) return;

            if (Cpu.IsShowingAllThreads)
            {
                OverviewBlockGrid.Height = 0;

                // x:Load="False", FindName builds it on the first visit
                FindName(nameof(AllThreadsGrid));
                AllThreadsGrid.Height = double.NaN;

                if (!_allThreadsTimeSpanHookAttached)
                {
                    _allThreadsTimeSpanHookAttached = true;
                    PerformanceGraphDefaults.BindTimeSpan(AllThreadsGrid, PerformanceGraphKind.Extended);
                }
            }
            else
            {
                UpdateOverviewHeight();

                // null until All Threads was shown once
                if (AllThreadsGrid != null) AllThreadsGrid.Height = 0;
            }
        }

        // only the shown section renders; also after PerformancePage.ActivateCurrentDetailViewRendering woke the
        // whole view, not only on a resize
        public void SyncSectionRenderingGate()
        {
            if (Cpu == null) return;

            SensorGraphRenderingGate.SetActive(OverviewBlockGrid, !Cpu.IsShowingAllThreads);

            // null until All Threads was shown once
            if (AllThreadsGrid != null)
            {
                SensorGraphRenderingGate.SetActive(AllThreadsGrid, Cpu.IsShowingAllThreads);
            }

            // the walk woke the hidden layout of Wide and Narrow too; back down to the shown one
            if (!Cpu.IsShowingAllThreads)
            {
                SensorGraphRenderingGate.SetActive(_isNarrowLayoutActive ? WideGraphsGrid : NarrowGraphsPanel, false);
            }
        }

        // the overview block at least as tall as the viewport, so its graphs stretch; past its minimum (graphs plus
        // tiles and static info) the ScrollViewer takes over
        private void UpdateOverviewHeight()
        {
            // read off the live tree, see OverviewBlockSizing
            double contentWidth = OverviewBlockSizing.ContentWidth(ContentScrollViewer, ContentStackPanel, OverviewBlockGrid);
            TilesAndStaticInfoGrid.Measure(new Size(contentWidth, double.PositiveInfinity));
            double tilesAndStaticInfoHeight = TilesAndStaticInfoGrid.DesiredSize.Height;

            double graphsMinHeight = _isNarrowLayoutActive ? NarrowGraphsPanel.MinHeight : WideGraphsGrid.MinHeight;

            double naturalMinHeight = graphsMinHeight + OverviewBlockGrid.RowSpacing + tilesAndStaticInfoHeight;

            OverviewBlockGrid.Height = OverviewBlockSizing.Height(
                ContentScrollViewer, ContentStackPanel, OverviewBlockGrid, naturalMinHeight);
        }

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem and fix: see GpuDetailView.SetLayoutActive, also for the conditions on the render gate
        private void SetLayoutActive(FrameworkElement wideLayout, FrameworkElement narrowLayout, bool useNarrow)
        {
            wideLayout.Opacity = useNarrow ? 0 : 1;
            wideLayout.IsHitTestVisible = !useNarrow;

            narrowLayout.Opacity = useNarrow ? 1 : 0;
            narrowLayout.IsHitTestVisible = useNarrow;

            if (!IsHitTestVisible || Cpu == null || Cpu.IsShowingAllThreads) return;

            SensorGraphRenderingGate.SetActive(wideLayout, !useNarrow);
            SensorGraphRenderingGate.SetActive(narrowLayout, useNarrow);
        }

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        // above the second core group, 0 without the first one; see the workaround on AllThreadsGrid in the XAML
        private Thickness GroupSpacingMargin(bool showSplit) => showSplit ? new Thickness(0, CoreGroupSpacing, 0, 0) : new Thickness(0);
    }
}
