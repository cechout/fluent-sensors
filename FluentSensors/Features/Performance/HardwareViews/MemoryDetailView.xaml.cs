using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using Windows.Foundation;

using FluentSensors.Common.Sensors;
using FluentSensors.Features.Performance;
using FluentSensors.Features.Performance.Lhm;


namespace FluentSensors.Features.Performance.HardwareViews
{
    // the RAM detail view:
    // physical and virtual memory used, their y-axis maximum from Memory.RoundedTotalMemory and
    // Memory.VirtualMemoryTotal, and the temperature of a module where one reports it
    public sealed partial class MemoryDetailView : UserControl
    {
        // === fields ===

        // the graphs area at its smallest, two graphs and three
        private const double GraphsMinHeight = 300;
        private const double GraphsMinHeightWithTemperature = 430;


        // === constructor ===

        public MemoryDetailView()
        {
            InitializeComponent();

            PerformanceGraphDefaults.BindTimeSpan(OverviewBlockGrid, PerformanceGraphKind.Standard);
            HardwareIconColorBinding.Bind(this, () => Bindings.Update());
        }


        // === bindable properties ===

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
            if (d is not MemoryDetailView view) return;

            if (e.OldValue is LhmMemoryInstanceViewModel oldMemory) oldMemory.PropertyChanged -= view.OnMemoryPropertyChanged;
            if (e.NewValue is LhmMemoryInstanceViewModel newMemory) newMemory.PropertyChanged += view.OnMemoryPropertyChanged;

            view.Bindings.Update();
            view.ApplyModuleTemperatureLayout();
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

        private void OnMemoryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LhmMemoryInstanceViewModel.HasModuleTemperature)) ApplyModuleTemperatureLayout();
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


        // === private helpers ===

        // the third graph and its tile take room once a module reports a temperature
        // --- workaround: SensorGraphControl permanently blank after Collapsed + Unload/Reload ---
        // problem and fix: see GpuDetailView.SetLayoutActive; the panel stays in the tree and hides by a row without
        // height, Opacity and IsHitTestVisible
        private void ApplyModuleTemperatureLayout()
        {
            bool hasTemperature = Memory?.HasModuleTemperature == true;

            // half as tall as a memory graph, like the stacked graphs beside the big one on the cpu and gpu tab
            ModuleTemperatureRow.Height = hasTemperature ? new GridLength(0.5, GridUnitType.Star) : new GridLength(0);
            ModuleTemperaturePanel.Opacity = hasTemperature ? 1 : 0;
            ModuleTemperaturePanel.IsHitTestVisible = hasTemperature;
            ModuleTemperatureTile.Visibility = hasTemperature ? Visibility.Visible : Visibility.Collapsed;

            GraphsAreaGrid.MinHeight = hasTemperature ? GraphsMinHeightWithTemperature : GraphsMinHeight;
            RecalculateOverviewHeight();
        }
    }
}
