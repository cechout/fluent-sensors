using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

            // literal xaml children, already exist right after InitializeComponent, no need to wait for Loaded
            PerformanceGraphDefaults.ApplyTimeSpan(OverviewBlockGrid, PerformanceGraphDefaults.StandardTimeSpanSeconds);
        }


        // === bindable properties ===

        // graph color for every SensorPanelControl in this view; single source of truth in HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).Color;

        // header
        public string GroupLabel => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).Label;
        public string GroupIconGlyph => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).IconGlyph;


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

            double naturalMinHeight = GraphsAreaGrid.MinHeight + OverviewBlockGrid.RowSpacing + tilesHeight;
            OverviewBlockGrid.Height = Math.Max(availableHeight, naturalMinHeight);
        }
    }
}