namespace FluentSensors.Persistence.Models
{
    // one switch choice, keyed by "{hardwareName}|{category}"
    public class SensorSwitchState
    {
        public string SelectedSensorId { get; set; }
    }
}
