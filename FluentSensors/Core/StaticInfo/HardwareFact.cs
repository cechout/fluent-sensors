using System.ComponentModel;


namespace FluentSensors.Core.StaticInfo
{
    // one fact of an info panel, a label above its value; shown by FactListControl
    // the label of a fact read from an LHM sensor is the sensor name, never translated
    public class HardwareFact : INotifyPropertyChanged
    {
        public HardwareFact(string name, string valueText = "-")
        {
            Name = name;
            _valueText = string.IsNullOrWhiteSpace(valueText) ? "-" : valueText;
        }

        public string Name { get; }

        private string _valueText;
        public string ValueText
        {
            get => _valueText;
            set
            {
                if (_valueText == value) return;
                _valueText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
