using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using WinRT;


namespace FluentSensors.WidgetProvider
{
    // the COM server the Widgets board starts with -RegisterProcessAsComServer:
    // registers the class factory under the class id of the package manifest and stays until the board ends the process
    public static partial class Program
    {
        // === fields ===

        // the class id of com:Class and CreateInstance in Package.appxmanifest
        private static readonly Guid ProviderClassId = new("8F1C6E2A-3B4D-4E5F-9A7B-2C8D1E0F4A63");

        private const uint ClassContextLocalServer = 4;
        private const uint ClassFlagsMultipleUse = 1;


        // === entry ===

        [MTAThread]
        private static int Main(string[] args)
        {
            // started by hand: nothing to serve
            if (Array.IndexOf(args, "-RegisterProcessAsComServer") < 0) return 0;

            try
            {
                ThrowIfFailed(CoInitializeEx(0, 0));

                var factory = new WidgetProviderFactory();
                nint factoryPointer = new StrategyBasedComWrappers().GetOrCreateComInterfaceForObject(factory, CreateComInterfaceFlags.None);
                ThrowIfFailed(CoRegisterClassObject(in ProviderClassId, factoryPointer, ClassContextLocalServer, ClassFlagsMultipleUse, out uint cookie));

                AppPipe.Start();
                Thread.Sleep(Timeout.Infinite);

                CoRevokeClassObject(cookie);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("fatal " + ex);
                return 1;
            }
        }


        // === win32 api imports ===

        private static void ThrowIfFailed(int hresult)
        {
            if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
        }

        [LibraryImport("ole32.dll")]
        private static partial int CoInitializeEx(nint reserved, uint coInit);

        [LibraryImport("ole32.dll")]
        private static partial int CoRegisterClassObject(in Guid classId, nint unknown, uint context, uint flags, out uint cookie);

        [LibraryImport("ole32.dll")]
        private static partial int CoRevokeClassObject(uint cookie);
    }


    [GeneratedComInterface]
    [Guid("00000001-0000-0000-C000-000000000046")]
    internal partial interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(nint outer, in Guid interfaceId, out nint result);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool doLock);
    }

    // hands the board the one provider instance, as whatever interface it asks for
    [GeneratedComClass]
    internal partial class WidgetProviderFactory : IClassFactory
    {
        private const int ClassNoAggregation = unchecked((int)0x80040110);

        public int CreateInstance(nint outer, in Guid interfaceId, out nint result)
        {
            result = 0;
            if (outer != 0) return ClassNoAggregation;

            try
            {
                nint inspectable = MarshalInspectable<BoardWidgetProvider>.FromManaged(BoardWidgetProvider.Instance);
                int hresult = Marshal.QueryInterface(inspectable, in interfaceId, out result);
                Marshal.Release(inspectable);
                return hresult;
            }
            catch (Exception ex)
            {
                Log.Write("CreateInstance failed " + ex);
                return ex.HResult;
            }
        }

        public int LockServer(bool doLock) => 0;
    }
}
