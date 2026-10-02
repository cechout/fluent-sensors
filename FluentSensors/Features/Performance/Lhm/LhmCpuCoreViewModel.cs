using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

using FluentSensors.Controls.SensorGraph;


namespace FluentSensors.Features.Performance.Lhm
{
    // one cpu core:
    // a physical core with its threads (1 without SMT) and its Temperature and Clock graphs once
    // LhmCpuInstanceViewModel matched them
    // TemperatureLabel and ClockLabel keep the raw LHM label ("P-Core", "E-Core"), which Load names lack; two fields,
    // the families could disagree, not displayed yet
    public class LhmCpuCoreViewModel : INotifyPropertyChanged
    {
        // === constructor ===

        public LhmCpuCoreViewModel(bool hasThreads)
        {
            HasThreads = hasThreads;
            Threads = new ObservableCollection<SensorGraphViewModel>();
        }


        // === bindable properties ===

        public bool HasThreads { get; }

        // one per "CPU Core #N[ Thread #M]" Load sensor of this core
        public ObservableCollection<SensorGraphViewModel> Threads { get; }

        private SensorGraphViewModel _temperature;
        public SensorGraphViewModel Temperature
        {
            get => _temperature;
            set { _temperature = value; OnPropertyChanged(); }
        }

        private SensorGraphViewModel _clock;
        public SensorGraphViewModel Clock
        {
            get => _clock;
            set { _clock = value; OnPropertyChanged(); }
        }

        private string _temperatureLabel;
        public string TemperatureLabel
        {
            get => _temperatureLabel;
            set { _temperatureLabel = value; OnPropertyChanged(); }
        }

        private string _clockLabel;
        public string ClockLabel
        {
            get => _clockLabel;
            set { _clockLabel = value; OnPropertyChanged(); }
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
