namespace FluentSensors.Core.StaticInfo
{
    public record WinStorageDriveInfo(
        string FriendlyName, // MSFT_PhysicalDisk, else Win32_DiskDrive.Model
        string SerialNumber,
        string FirmwareRevision,
        string BusType, // "NVMe", "SATA", "USB"
        ulong SizeBytes,
        string PnpDeviceId,

        // from MSFT_StorageReliabilityCounter (GetRelated on the matching MSFT_PhysicalDisk), the Storport view
        // of SMART across vendor quirks
        // not from LHM, which misreads some of these on certain Samsung NVMe drives (issue
        // #455), worse behind Intel VMD
        // null when the disk has no counter object (some controllers and drivers expose none)
        uint? TemperatureCelsius,
        uint? TemperatureMaxCelsius,

        // percent, 100 = the estimated wear limit reached (per the Microsoft docs)
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
        string ManufactureDate, // "" when not reported
        ulong? ReadLatencyMaxMs,
        ulong? WriteLatencyMaxMs,
        ulong? FlushLatencyMaxMs
    );
}
