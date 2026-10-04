using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using FluentSensors.Common.Localization;
using FluentSensors.Persistence.Services;
using FluentSensors.Common.Sensors;
using FluentSensors.Core;
using FluentSensors.Controls.SensorRow;


namespace FluentSensors.Features.Sensors
{
    public class HardwareGroupViewModel : INotifyPropertyChanged
    {
        // === fields ===

        public string HardwareName { get; set; } = "Hardware Name not provided"; // expander description

        // the raw LHM name; HardwareName is a display name (it differs for storage and network), so
        // outside callers match on this
        public string LhmHardwareName { get; set; } = "";
        public string GroupLabel { get; set; } = "Hardware"; // expander header
        public string IconGlyph { get; set; } = ""; // from HardwareGroupInfo

        // kept for the rows, which colour their widget and taskbar graphs by it
        public HardwareGroupKind Kind { get; set; } = HardwareGroupKind.Other;

        // follows the icon colour setting, see RefreshIconBrush
        private SolidColorBrush _iconBrush;
        public SolidColorBrush IconBrush
        {
            get => _iconBrush ??= HardwareGroupInfo.GetIconBrush(Kind);
            private set { _iconBrush = value; OnPropertyChanged(); }
        }

        public void RefreshIconBrush() => IconBrush = HardwareGroupInfo.GetIconBrush(Kind);
        public ObservableCollection<SensorRowViewModel> Sensors { get; set; } // expander content
        public ObservableCollection<SensorRowViewModel> HiddenSensors { get; set; }

        // for the "Show Hidden Sensors" button
        public bool HasHiddenSensors => HiddenSensors.Count > 0;
        public Visibility HiddenPanelVisibility => HasHiddenSensors ? Visibility.Visible : Visibility.Collapsed;

        // shown/total on the expander header; (by IsHidden, HideSensorsCompletely=false leaves
        // a hidden sensor in Sensors)
        public string SensorCountText => AppStrings.Format("Sensors_GroupCount", ShownSensorCount, TotalSensorCount);
        public string SensorCountName => AppStrings.Format("Sensors_GroupCountName", ShownSensorCount, TotalSensorCount);
        private int ShownSensorCount => Sensors.Count(s => !s.IsHidden);
        private int TotalSensorCount => Sensors.Count + HiddenSensors.Count;

        // the sensors page expander
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        // the hidden sensors window expander, apart so neither side moves the other
        private bool _isExpandedInHiddenWindow;
        public bool IsExpandedInHiddenWindow
        {
            get => _isExpandedInHiddenWindow;
            set
            {
                if (_isExpandedInHiddenWindow != value)
                {
                    _isExpandedInHiddenWindow = value;
                    OnPropertyChanged();
                }
            }
        }


        // === constructor ===

        public HardwareGroupViewModel()
        {
            Sensors = new ObservableCollection<SensorRowViewModel>();
            HiddenSensors = new ObservableCollection<SensorRowViewModel>();
        }


        // a new sensor into its list by its saved hidden state, notified right away for the
        // "Show Hidden Sensors" button
        public void AddDiscoveredSensor(SensorRowViewModel sensor, bool isHidden)
        {
            if (isHidden)
            {
                HiddenSensors.Add(sensor);
                OnPropertyChanged(nameof(HasHiddenSensors));
                OnPropertyChanged(nameof(HiddenPanelVisibility));
            }
            else
            {
                Sensors.Add(sensor);
            }

            NotifySensorCountChanged();
        }


        // every checked sensor into the hidden list
        public void HideSelectedSensors()
        {
            var selectedSensors = Sensors.Where(s => s.IsSelected).ToList();

            foreach (var sensor in selectedSensors)
            {
                sensor.IsSelected = false;
                sensor.IsHidden = true;
                SensorStateService.Instance.SetHidden(sensor.Id, true);
                HardwareMonitorService.Instance.AddExcludedSensor(sensor.Id); // recorded only, the skip is disabled

                if (SettingsService.Instance.HideSensorsCompletely)
                {
                    Sensors.Remove(sensor);

                    // before the first hidden sensor that came after it
                    var insertBeforeSensor = HiddenSensors.FirstOrDefault(s => s.SortOrder > sensor.SortOrder);

                    if (insertBeforeSensor != null)
                    {
                        HiddenSensors.Insert(HiddenSensors.IndexOf(insertBeforeSensor), sensor);
                    }
                    else
                    {
                        // none later; at the end
                        HiddenSensors.Add(sensor);
                    }
                }
                else
                {
                    sensor.IsDisabled = true;
                }
            }

            OnPropertyChanged(nameof(HasHiddenSensors));
            OnPropertyChanged(nameof(HiddenPanelVisibility));
            NotifySensorCountChanged();
        }


        // every checked hidden sensor back into the main list
        public void RestoreSelectedHiddenSensors()
        {
            var selectedSensors = HiddenSensors.Where(s => s.IsSelected).ToList();

            foreach (var sensor in selectedSensors)
            {
                sensor.IsSelected = false;
                sensor.IsHidden = false;
                sensor.IsDisabled = false;
                sensor.ResetMinMax();
                SensorStateService.Instance.SetHidden(sensor.Id, false);
                HardwareMonitorService.Instance.RemoveExcludedSensor(sensor.Id);

                HiddenSensors.Remove(sensor);

                // before the first visible sensor that came after it
                var insertBeforeSensor = Sensors.FirstOrDefault(s => s.SortOrder > sensor.SortOrder);

                if (insertBeforeSensor != null)
                {
                    Sensors.Insert(Sensors.IndexOf(insertBeforeSensor), sensor);
                }
                else
                {
                    // none later; at the end
                    Sensors.Add(sensor);
                }
            }

            OnPropertyChanged(nameof(HasHiddenSensors));
            OnPropertyChanged(nameof(HiddenPanelVisibility));
            NotifySensorCountChanged();
        }

        private void NotifySensorCountChanged()
        {
            OnPropertyChanged(nameof(SensorCountText));
            OnPropertyChanged(nameof(SensorCountName));
        }


        // === INotifyPropertyChanged implementation ===

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
