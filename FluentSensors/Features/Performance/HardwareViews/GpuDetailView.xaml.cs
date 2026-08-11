using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Performance.Lhm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Features.Performance.HardwareViews
{
    public sealed partial class GpuDetailView : UserControl
    {
        // === fields ===

        private const double NarrowGraphsLayoutThreshold = 700;

        // tracks which layout is active per row; Visibility can no longer be queried for this (see workaround
        // comment on SetLayoutActive below), so this replaces the previous
        // WideGraphsGridN.Visibility == Visible checks
        private bool _isNarrowLayout1Active;
        private bool _isNarrowLayout2Active;


        // === constructor ===

        public GpuDetailView()
        {
            InitializeComponent();

            // literal xaml children, already exist right after InitializeComponent, no need to wait for Loaded
            PerformanceGraphDefaults.ApplyTimeSpan(OverviewBlockGrid, PerformanceGraphDefaults.StandardTimeSpanSeconds);
        }


        // === dependency properties ===

        // graph color for every SensorPanelControl in this view; single source of truth in HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Gpu).IconGlyph;

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
            RecalculateOverviewHeight();
        }

        // recomputes OverviewBlockGrid.Height from the scroll viewers current size
        //
        // Called both by ContentScrollViewer_SizeChanged above and externally by PerformancePage after a nav
        // sidebar/info panel toggle, since that changes DetailHostGrids available size without necessarily firing
        // SizeChanged on this control quickly enough
        public void RecalculateOverviewHeight()
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double headerHeight = HeaderGrid.ActualHeight + ContentStackPanel.Spacing;
            double availableHeight = ContentScrollViewer.ActualHeight - verticalPadding - headerHeight;

            double horizontalPadding = ContentStackPanel.Padding.Left + ContentStackPanel.Padding.Right;
            TilesGrid.Measure(new Size(ContentScrollViewer.ActualWidth - horizontalPadding, double.PositiveInfinity));
            double tilesHeight = TilesGrid.DesiredSize.Height;

            double row1MinHeight = _isNarrowLayout1Active ? NarrowGraphsPanel1.MinHeight : WideGraphsGrid1.MinHeight;
            double row2MinHeight = _isNarrowLayout2Active ? NarrowGraphsPanel2.MinHeight : WideGraphsGrid2.MinHeight;

            double naturalMinHeight = row1MinHeight + row2MinHeight + (OverviewBlockGrid.RowSpacing * 2) + tilesHeight;
            OverviewBlockGrid.Height = Math.Max(availableHeight, naturalMinHeight);
        }

        private void GraphsAreaGrid1_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _isNarrowLayout1Active = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            SetLayoutActive(WideGraphsGrid1, NarrowGraphsPanel1, _isNarrowLayout1Active);
        }

        private void GraphsAreaGrid2_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _isNarrowLayout2Active = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            SetLayoutActive(WideGraphsGrid2, NarrowGraphsPanel2, _isNarrowLayout2Active);
        }


        // === private helpers ===

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem: same root cause as PerformancePage.xaml.cs UpdateDetailView (see that comment for the full
        // explanation)
        // A SensorGraphControl that is Visibility.Collapsed when its parent page unloads and reloads never recovers,
        // even once made Visible again with a real size later; the Wide/Narrow layout switch hits this exact same
        // trap one level deeper than the outer detail-view switch, since whichever layout is not currently active
        // is normally the one thats Collapsed
        // fix: same pattern as the outer fix; never Collapse either layout, toggle Opacity + IsHitTestVisible
        // instead; both layouts now always occupy their full measured space (they already overlap in the same
        // Grid cell, so this doesnt change the visible arrangement), just one of them is invisible/non-interactive
        private static void SetLayoutActive(FrameworkElement wideLayout, FrameworkElement narrowLayout, bool useNarrow)
        {
            wideLayout.Opacity = useNarrow ? 0 : 1;
            wideLayout.IsHitTestVisible = !useNarrow;

            narrowLayout.Opacity = useNarrow ? 1 : 0;
            narrowLayout.IsHitTestVisible = useNarrow;
        }
    }
}