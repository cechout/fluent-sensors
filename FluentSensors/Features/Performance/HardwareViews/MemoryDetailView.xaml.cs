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
    // self-contained RAM detail view: single Used graph, Y-max driven by Memory.RoundedTotalMemory
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

        // graph color for every SensorPanelControl in this view; single source of truth in HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).IconGlyph;

        // header icon colour, follows the hardware icon colour setting; HardwareIconColorBinding in the
        // constructor is what re-reads it, the graph colour above is deliberately not part of that
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

        // the splitter rewrites both column widths while it drags; once it lets go, the content column goes back to
        // filling the rest, and the info panel width goes to the view model, which every hardware view sizes its own
        // info panel column from
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

        // recomputes OverviewBlockGrid.Height from the scroll viewers current size
        //
        // Called both by ContentScrollViewer_SizeChanged above and externally by PerformancePage after a nav
        // sidebar/info panel toggle, since that changes DetailHostGrids available size without necessarily firing
        // SizeChanged on this control quickly enough
        public void RecalculateOverviewHeight()
        {
            // everything around the block is read off the live tree, see OverviewBlockSizing
            double contentWidth = OverviewBlockSizing.ContentWidth(ContentScrollViewer, ContentStackPanel, OverviewBlockGrid);
            TilesGrid.Measure(new Size(contentWidth, double.PositiveInfinity));
            double tilesHeight = TilesGrid.DesiredSize.Height;

            double naturalMinHeight = GraphsAreaGrid.MinHeight + OverviewBlockGrid.RowSpacing + tilesHeight;
            OverviewBlockGrid.Height = OverviewBlockSizing.Height(
                ContentScrollViewer, ContentStackPanel, OverviewBlockGrid, naturalMinHeight);
        }
    }
}
