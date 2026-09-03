namespace FluentSensors.Core.StaticInfo
{
    public record WinStorageDriveInfo(
        string FriendlyName, // from MSFT_PhysicalDisk if available, falls back to Win32_DiskDrive.Model
        string SerialNumber,
        string FirmwareRevision,
        string BusType, // e.g. "NVMe", "SATA", "USB"
        ulong SizeBytes,
        string PnpDeviceId,

        // everything below comes from MSFT_StorageReliabilityCounter (via ManagementObject.GetRelated on the
        // matching MSFT_PhysicalDisk)
        // Windows/Storports own abstraction over the drives raw SMART/health data, sourced correctly regardless
        // of vendor/controller quirks; deliberately not sourced from LibreHardwareMonitor, which is documented to
        // misread some of these exact values on certain Samsung NVMe drives (GitHub issue #455), worse again behind
        // an Intel VMD controller HasReliabilityData is false when no counter object exists for this disk at all
        // (some controllers/drivers dont expose one)
        // Every field below is only meaningful when its true, and stays 0/"" otherwise
        uint? TemperatureCelsius,
        uint? TemperatureMaxCelsius,

        // percentage, 100 = the drives estimated wear limit has been reached; per Microsofts own docs
        uint? WearPercent,

        uint? PowerOnHours,
        ulong? ReadErrorsTotal,
        ulong? ReadErrorsCorrected,
        ulong? ReadErrorsUncorrected,
        ulong? WriteErrorsTotal,
        ulong? WriteErrorsCorrected,
        ulong? WriteErrorsUncorrected,
        uint? StartStopCycleCount,
        uint? StartStopCycleCountMax,
        uint? LoadUnloadCycleCount,
        uint? LoadUnloadCycleCountMax,
        string ManufactureDate, // stays a plain string; "" already means "not reported" for a string field
        ulong? ReadLatencyMaxMs,
        ulong? WriteLatencyMaxMs,
        ulong? FlushLatencyMaxMs
    );
}
