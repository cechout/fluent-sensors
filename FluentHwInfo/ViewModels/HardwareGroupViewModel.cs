using FluentHwInfo.Services;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FluentHwInfo.ViewModels
{
    public class HardwareGroupViewModel : INotifyPropertyChanged
    {
        // fields
        public string HardwareName { get; set; } = "Hardware Name not provided"; // header of expander
        public ObservableCollection<SensorRowViewModel> Sensors { get; set; } // content of expander
        public ObservableCollection<SensorRowViewModel> HiddenSensors { get; set; } // sensors hidden from the main list

        // true as soon as at least one sensor sits in the hidden list; lets the UI grey out or hide the "Show Hidden Sensors" button
        public bool HasHiddenSensors => HiddenSensors.Count > 0;
        public Visibility HiddenPanelVisibility => HasHiddenSensors ? Visibility.Visible : Visibility.Collapsed;


        // constructor
        public HardwareGroupViewModel()
        {
            // initializes the empty lists for this specific hardware
            Sensors = new ObservableCollection<SensorRowViewModel>();
            HiddenSensors = new ObservableCollection<SensorRowViewModel>();
        }


        // adds a newly discovered sensor into the correct list based on its persisted hidden state, and notifies bound UI
        // immediately so the "Show Hidden Sensors" button reflects it without waiting for a manual hide/restore action
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
        }


        // moves every currently checked sensor from the main list into the hidden list
        public void HideSelectedSensors()
        {
            var selectedSensors = Sensors.Where(s => s.IsSelected).ToList();

            foreach (var sensor in selectedSensors)
            {
                sensor.IsSelected = false;
                sensor.IsHidden = true;
                SensorStateService.Instance.SetHidden(sensor.Id, true);

                if (SettingsService.Instance.HideSensorsCompletely)
                {
                    Sensors.Remove(sensor);

                    // find the first hidden sensor that originally came after this one, insert right before it
                    var insertBeforeSensor = HiddenSensors.FirstOrDefault(s => s.SortOrder > sensor.SortOrder);

                    if (insertBeforeSensor != null)
                    {
                        HiddenSensors.Insert(HiddenSensors.IndexOf(insertBeforeSensor), sensor);
                    }
                    else
                    {
                        // no later sensor found; place at the very end
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
        }


        // moves every currently checked sensor from the hidden list back into the main list
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

                HiddenSensors.Remove(sensor);

                // find the first sensor in the visible list that originally came after this one, insert right before it
                var insertBeforeSensor = Sensors.FirstOrDefault(s => s.SortOrder > sensor.SortOrder);

                if (insertBeforeSensor != null)
                {
                    Sensors.Insert(Sensors.IndexOf(insertBeforeSensor), sensor);
                }
                else
                {
                    // no later sensor found; place at the very end
                    Sensors.Add(sensor);
                }
            }

            OnPropertyChanged(nameof(HasHiddenSensors));
            OnPropertyChanged(nameof(HiddenPanelVisibility));
        }


        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}