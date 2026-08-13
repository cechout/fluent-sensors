namespace FluentSensors.Persistence.Models
{
    // one persisted sensor-switch choice, keyed externally by "{hardwareName}|{category}"
    public class SensorSwitchState
    {
        public string SelectedSensorId { get; set; }
    }
}
