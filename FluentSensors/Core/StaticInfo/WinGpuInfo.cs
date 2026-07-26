namespace FluentSensors.Core.StaticInfo
{
    // static facts about a GPU;
    // VRAM total/type/bus width are deliberately NOT here; VRAM total is already available live via LHM, and
    // type/bus width would need vendor SDKs (NVAPI/ADL), out of scope for now
    // Win32_VideoController.AdapterRAM is also deliberately not used: its a known-broken 32-bit field that overflows/
    // wraps for cards with 4GB+ VRAM
    public record WinGpuInfo(
        string Name,
        string DriverVersion,
        string PnpDeviceId
    );
}