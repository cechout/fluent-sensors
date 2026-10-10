namespace FluentSensors.Core.StaticInfo
{
    // something LibreHardwareMonitorLib needs from the machine (the PawnIO driver, a system-wide .NET runtime)
    public enum WinPrerequisiteState
    {
        Installed,
        Missing,

        // installed while the app runs; LHM looked once at startup and built its hardware without it
        RestartRequired
    }
}
