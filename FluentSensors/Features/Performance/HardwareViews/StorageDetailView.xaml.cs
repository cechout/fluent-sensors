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
    // the storage detail view:
    // total activity big, read and write rate stacked, the CPU shape with its wide and narrow switch
    public sealed partial class StorageDetailView : UserControl
    {
        // === fields ===

        // below it the wide layout (a big graph beside two stacked) turns narrow (all three stacked)
        private const double NarrowGraphsLayoutThreshold = 700;
        private bool _isNarrowLayoutActive;
        private bool _extendedTimeSpanHookAttached;


        // === constructor ===

        public StorageDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.BindTimeSpan(OverviewBlockGrid, PerformanceGraphKind.Standard);
            HardwareIconColorBinding.Bind(this, () => Bindings.Update());
        }


        // === bindable properties ===

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Storage).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Storage).IconGlyph;

        // header icon colour, re-read by HardwareIconColorBinding (the graph colour stays)
        public SolidColorBrush GroupIconBrush => HardwareGroupInfo.GetIconBrush(HardwareGroupKind.Storage);


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

        private static void OnStorageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StorageDetailView view) view.Bindings.Update();
        }


        // === binding helpers ===

        private Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;


        // === event handlers ===

        private void ShowOverall_Click(object sender, RoutedEventArgs e)
        {
            if (Storage != null) Storage.IsShowingExtended = false;
            RecalculateOverviewHeight();
            SyncLayoutRenderingGate();
        }

        private void ShowExtended_Click(object sender, RoutedEventArgs e)
        {
            if (Storage != null) Storage.IsShowingExtended = true;
            RecalculateOverviewHeight();
            SyncLayoutRenderingGate();
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

        // sizes the shown section, Overview or Extended (like GpuDetailView); also from PerformancePage after a panel
        // toggle, whose size change can reach this control late
        public void RecalculateOverviewHeight()
        {
            if (Storage != null && Storage.IsShowingExtended)
            {
                OverviewBlockGrid.Height = 0;

                // x:Load="False", FindName builds it on the first visit
                FindName(nameof(ExtendedGrid));
                ExtendedGrid.Height = double.NaN;

                if (!_extendedTimeSpanHookAttached)
                {
                    _extendedTimeSpanHookAttached = true;
                    PerformanceGraphDefaults.BindTimeSpan(ExtendedGrid, PerformanceGraphKind.Standard);
                }
                return;
            }

            // null until Extended was shown once
            if (ExtendedGrid != null) ExtendedGrid.Height = 0;

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
        // the section split works like GpuDetailView.SyncSectionRenderingGate
        public void SyncLayoutRenderingGate()
        {
            bool isShowingExtended = Storage != null && Storage.IsShowingExtended;

            SensorGraphRenderingGate.SetActive(OverviewBlockGrid, !isShowingExtended);

            // null until Extended was shown once
            if (ExtendedGrid != null)
            {
                SensorGraphRenderingGate.SetActive(ExtendedGrid, isShowingExtended);
            }

            // the walk woke the hidden layout of Wide and Narrow too; back down to the shown one
            if (!isShowingExtended)
            {
                SensorGraphRenderingGate.SetActive(_isNarrowLayoutActive ? WideGraphsGrid : NarrowGraphsPanel, false);
            }
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

            if (!IsHitTestVisible || (Storage != null && Storage.IsShowingExtended)) return;

            SensorGraphRenderingGate.SetActive(wideLayout, !useNarrow);
            SensorGraphRenderingGate.SetActive(narrowLayout, useNarrow);
        }
    }
}
