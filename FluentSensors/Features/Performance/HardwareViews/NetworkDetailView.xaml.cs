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
    public sealed partial class NetworkDetailView : UserControl
    {
        // === fields ===

        // below it the wide layout (a big graph beside two stacked) turns narrow (all three stacked)
        private const double NarrowGraphsLayoutThreshold = 700;
        private bool _isNarrowLayoutActive;


        // === constructor ===

        public NetworkDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.BindTimeSpan(OverviewBlockGrid, PerformanceGraphKind.Standard);
            HardwareIconColorBinding.Bind(this, () => Bindings.Update());
        }


        // === bindable properties ===

        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Network).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Network).Label;

        // header icon colour, re-read by HardwareIconColorBinding (the graph colour stays)
        public SolidColorBrush GroupIconBrush => HardwareGroupInfo.GetIconBrush(HardwareGroupKind.Network);


        // === dependency properties ===

        public LhmNetworkInstanceViewModel Network
        {
            get => (LhmNetworkInstanceViewModel)GetValue(NetworkProperty);
            set => SetValue(NetworkProperty, value);
        }

        public static readonly DependencyProperty NetworkProperty =
            DependencyProperty.Register(
                nameof(Network),
                typeof(LhmNetworkInstanceViewModel),
                typeof(NetworkDetailView),
                new PropertyMetadata(null, OnNetworkChanged));

        private static void OnNetworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NetworkDetailView view) view.Bindings.Update();
        }


        // === event handlers ===

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

        // OverviewBlockGrid.Height from the scroll viewer size; also from PerformancePage after a panel toggle, whose
        // size change can reach this control late
        public void RecalculateOverviewHeight()
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

        private void GraphsAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _isNarrowLayoutActive = e.NewSize.Width < NarrowGraphsLayoutThreshold;
            SetLayoutActive(WideGraphsGrid, NarrowGraphsPanel, _isNarrowLayoutActive);
        }


        // === public methods ===

        // back down to the shown layout after PerformancePage.ActivateCurrentDetailViewRendering woke the whole view
        // (like CpuDetailView, without the section split)
        public void SyncLayoutRenderingGate()
        {
            SensorGraphRenderingGate.SetActive(_isNarrowLayoutActive ? WideGraphsGrid : NarrowGraphsPanel, false);
        }


        // === private helpers ===

        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem and fix: see GpuDetailView.SetLayoutActive; without a section split
        // IsHitTestVisible alone gates the render
        private void SetLayoutActive(FrameworkElement wideLayout, FrameworkElement narrowLayout, bool useNarrow)
        {
            wideLayout.Opacity = useNarrow ? 0 : 1;
            wideLayout.IsHitTestVisible = !useNarrow;

            narrowLayout.Opacity = useNarrow ? 1 : 0;
            narrowLayout.IsHitTestVisible = useNarrow;

            if (!IsHitTestVisible) return;

            SensorGraphRenderingGate.SetActive(wideLayout, !useNarrow);
            SensorGraphRenderingGate.SetActive(narrowLayout, useNarrow);
        }
    }
}
