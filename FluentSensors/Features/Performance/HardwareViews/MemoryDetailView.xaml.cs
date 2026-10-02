using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // the RAM detail view:
    // one Used graph, its y-axis maximum from Memory.RoundedTotalMemory
    public sealed partial class MemoryDetailView : UserControl
    {
        // === constructor ===

        public MemoryDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.BindTimeSpan(OverviewBlockGrid, PerformanceGraphKind.Standard);
            HardwareIconColorBinding.Bind(this, () => Bindings.Update());
        }


        // === bindable properties ===

        // the graph colour of this view, from HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).IconGlyph;

        // header icon colour, re-read by HardwareIconColorBinding (the graph colour stays)
        public SolidColorBrush GroupIconBrush => HardwareGroupInfo.GetIconBrush(HardwareGroupKind.Ram);


        // === dependency properties ===

        public LhmMemoryInstanceViewModel Memory
        {
            get => (LhmMemoryInstanceViewModel)GetValue(MemoryProperty);
            set => SetValue(MemoryProperty, value);
        }

        public static readonly DependencyProperty MemoryProperty =
            DependencyProperty.Register(
                nameof(Memory),
                typeof(LhmMemoryInstanceViewModel),
                typeof(MemoryDetailView),
                new PropertyMetadata(null, OnMemoryChanged));

        private static void OnMemoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MemoryDetailView view) view.Bindings.Update();
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

            double naturalMinHeight = GraphsAreaGrid.MinHeight + OverviewBlockGrid.RowSpacing + tilesHeight;
            OverviewBlockGrid.Height = OverviewBlockSizing.Height(
                ContentScrollViewer, ContentStackPanel, OverviewBlockGrid, naturalMinHeight);
        }
    }
}
