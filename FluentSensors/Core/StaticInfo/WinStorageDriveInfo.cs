using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // one volume with a drive letter on a drive
    public record WinVolumeInfo(
        string Letter, // "C:"
        string FileSystem,
        ulong SizeBytes,
        ulong FreeBytes
    );

    public record WinStorageDriveInfo(
        string FriendlyName, // MSFT_PhysicalDisk, else Win32_DiskDrive.Model
        string SerialNumber,
        string FirmwareRevision,
        string BusType, // "NVMe", "SATA", "USB"
        ulong SizeBytes,
        string PnpDeviceId,

        // from MSFT_PhysicalDisk; raw, see HardwareInfoFormatter.FormatMediaType and FormatHealthStatus
        uint? MediaType,
        uint? SpindleSpeedRpm, // 0 for a drive without platters
        uint? HealthStatus,

        // from MSFT_Disk, null when Windows lists no disk for the serial; see HardwareInfoFormatter.FormatPartitionStyle
        uint? PartitionStyle,
        uint? PartitionCount,
        bool IsBootDisk, // holds the running Windows
        bool IsSystemDisk, // holds the boot loader
        IReadOnlyList<WinVolumeInfo> Volumes,

        // the link of the controller the drive hangs on; null for SATA and USB, and for NVMe behind Intel VMD
        WinPciLink? PciLink,

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
        ulong? FlushLatencyMaxMs,

        int DiskNumber = -1 // Win32_DiskDrive.Index, the number Disk Management shows
    );
}
