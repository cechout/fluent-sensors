using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance.Lhm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // self-contained RAM detail view: single Used graph, Y-max driven by Memory.RoundedTotalMemory
    public sealed partial class MemoryDetailView : UserControl
    {
        // === constructor ===

        public MemoryDetailView()
        {
            InitializeComponent();
        }


        // === dependency properties ===

        // graph color for every SensorPanelControl in this view; single source of truth in HardwareGroupInfo
        public Windows.UI.Color HardwareColor => HardwareGroupInfo.GetProfile(HardwareGroupKind.Ram).Color;

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

        // kept as a safety net: PerformancePage caches one permanent view per hardware instance and sets Memory
        // exactly once, so this should never fire with a changing value in practice
        // but if that ever changes, this forces the x:Binds to re-evaluate instead of silently going stale
        private static void OnMemoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MemoryDetailView view) view.Bindings.Update();
        }


        // === event handlers ===

        // keeps the overview block at least as tall as the visible viewport (so its graph can stretch to fill
        // it), but lets it grow past that, and let the ScrollViewer take over once its natural minimum height
        // no longer fits
        private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double verticalPadding = ContentStackPanel.Padding.Top + ContentStackPanel.Padding.Bottom;
            double availableHeight = e.NewSize.Height - verticalPadding;

            TilesGrid.Measure(new Size(ContentScrollViewer.ActualWidth, double.PositiveInfinity));
            double tilesHeight = TilesGrid.DesiredSize.Height;

            double naturalMinHeight = GraphsAreaGrid.MinHeight + OverviewBlockGrid.RowSpacing + tilesHeight;
            OverviewBlockGrid.Height = Math.Max(availableHeight, naturalMinHeight);
        }
    }
}