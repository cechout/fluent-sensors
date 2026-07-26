namespace FluentSensors.Core.StaticInfo
{
    public record WinStorageDriveInfo(
        string FriendlyName, // from MSFT_PhysicalDisk if available, falls back to Win32_DiskDrive.Model
        string SerialNumber,
        string FirmwareRevision,
        string BusType, // e.g. "NVMe", "SATA", "USB"
        ulong SizeBytes,
        string PnpDeviceId
    );
}