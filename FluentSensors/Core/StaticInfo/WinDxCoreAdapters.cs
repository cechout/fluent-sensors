using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace FluentSensors.Core.StaticInfo
{
    // one hardware adapter as DXCore lists it; the pci ids tell it apart
    public record WinDxCoreAdapter(uint VendorId, uint DeviceId, bool IsIntegrated);

    // integrated or discrete, per adapter:
    // DXCore carries the answer of the driver for every gpu; the kernel hybrid flags (WinDisplayTopology.HybridRole)
    // only know the two halves of a hybrid pair and say nothing about a desktop apu
    // https://learn.microsoft.com/en-us/windows/win32/dxcore/dxcore_interface/ne-dxcore_interface-dxcoreadapterproperty
    public static partial class WinDxCoreAdapters
    {
        // === fields ===

        private static readonly Guid FactoryId = new("78ee5945-c36e-4b13-a669-005dd11c0f06"); // IDXCoreAdapterFactory
        private static readonly Guid AdapterListId = new("526c7776-40e9-459b-b711-f32ad76dfc28"); // IDXCoreAdapterList
        private static readonly Guid AdapterId = new("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e"); // IDXCoreAdapter

        // DXCORE_ADAPTER_ATTRIBUTE_D3D12_GRAPHICS; the D3D11_GRAPHICS list leaves out the integrated half of a hybrid
        // pair
        private static readonly Guid Direct3D12GraphicsAttribute = new("0c9ece4d-2f6e-4f01-8c96-e89e331b47b1");

        // DXCoreAdapterProperty
        private const uint PropertyHardwareId = 3; // four uint: vendor, device, subsystem, revision
        private const uint PropertyIsHardware = 11;
        private const uint PropertyIsIntegrated = 12;

        // vtable slots behind the three of IUnknown
        private const int SlotRelease = 2;
        private const int SlotCreateAdapterList = 3; // IDXCoreAdapterFactory
        private const int SlotGetAdapter = 3; // IDXCoreAdapterList
        private const int SlotGetAdapterCount = 4;
        private const int SlotIsPropertySupported = 5; // IDXCoreAdapter
        private const int SlotGetProperty = 6;

        private static List<WinDxCoreAdapter>? _adapters;
        private static readonly object _lock = new();


        // === public api ===

        // null when DXCore knows no hardware adapter with these pci ids, or does not answer the property
        public static bool? IsIntegrated(uint vendorId, uint deviceId)
        {
            lock (_lock)
            {
                _adapters ??= QueryAdapters();

                foreach (var adapter in _adapters)
                {
                    if (adapter.VendorId == vendorId && adapter.DeviceId == deviceId) return adapter.IsIntegrated;
                }
                return null;
            }
        }


        // === private helpers ===

        // every hardware adapter that answers IsIntegrated; empty when DXCore is missing or fails
        private static unsafe List<WinDxCoreAdapter> QueryAdapters()
        {
            var result = new List<WinDxCoreAdapter>();

            void* factory = null;
            void* list = null;

            try
            {
                Guid factoryId = FactoryId;
                if (DXCoreCreateAdapterFactory(&factoryId, &factory) != 0) return result;

                Guid attribute = Direct3D12GraphicsAttribute;
                Guid listId = AdapterListId;
                var createAdapterList = (delegate* unmanaged[Stdcall]<void*, uint, Guid*, Guid*, void**, int>)Slot(factory, SlotCreateAdapterList);
                if (createAdapterList(factory, 1, &attribute, &listId, &list) != 0) return result;

                var getAdapterCount = (delegate* unmanaged[Stdcall]<void*, uint>)Slot(list, SlotGetAdapterCount);
                var getAdapter = (delegate* unmanaged[Stdcall]<void*, uint, Guid*, void**, int>)Slot(list, SlotGetAdapter);

                uint count = getAdapterCount(list);
                for (uint index = 0; index < count; index++)
                {
                    void* adapter = null;
                    Guid adapterId = AdapterId;
                    if (getAdapter(list, index, &adapterId, &adapter) != 0) continue;

                    try
                    {
                        // the software rasterizer is in the list too
                        if (ReadBool(adapter, PropertyIsHardware) != true) continue;

                        bool? isIntegrated = ReadBool(adapter, PropertyIsIntegrated);
                        if (isIntegrated == null) continue;

                        uint* hardwareId = stackalloc uint[4];
                        if (!ReadProperty(adapter, PropertyHardwareId, hardwareId, 4 * sizeof(uint))) continue;

                        result.Add(new WinDxCoreAdapter(hardwareId[0], hardwareId[1], isIntegrated.Value));
                    }
                    finally
                    {
                        Release(adapter);
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // no dxcore.dll; every gpu stays unanswered
            }
            finally
            {
                if (list != null) Release(list);
                if (factory != null) Release(factory);
            }

            return result;
        }

        private static unsafe bool? ReadBool(void* adapter, uint property)
        {
            byte value = 0;
            return ReadProperty(adapter, property, &value, 1) ? value != 0 : null;
        }

        private static unsafe bool ReadProperty(void* adapter, uint property, void* buffer, int size)
        {
            var isPropertySupported = (delegate* unmanaged[Stdcall]<void*, uint, byte>)Slot(adapter, SlotIsPropertySupported);
            if (isPropertySupported(adapter, property) == 0) return false;

            var getProperty = (delegate* unmanaged[Stdcall]<void*, uint, nuint, void*, int>)Slot(adapter, SlotGetProperty);
            return getProperty(adapter, property, (nuint)size, buffer) == 0;
        }

        private static unsafe void* Slot(void* comObject, int slot) => (*(void***)comObject)[slot];

        private static unsafe void Release(void* comObject)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)Slot(comObject, SlotRelease))(comObject);
        }


        // === win32 api imports ===

        [LibraryImport("dxcore.dll")]
        private static unsafe partial int DXCoreCreateAdapterFactory(Guid* interfaceId, void** factory);
    }
}
