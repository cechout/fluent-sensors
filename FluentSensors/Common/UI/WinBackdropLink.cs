using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using System;
using System.Runtime.InteropServices;
using WinRT;


namespace FluentSensors.Common.UI
{
    // the backdrop link:
    // a ContentExternalBackdropLink, a system backdrop target that lives in a visual instead of filling the whole
    // window, so the material can be sized and clipped to one element
    //
    // the class is implemented and activatable in the runtime we ship, but Windows App SDK 2.2.0 leaves it out of
    // the metadata (it is marked experimental), so there is no C# projection; this is a hand projection of the
    // four members we need, taken from the 2.1.7-experimental Microsoft.UI.winmd
    // https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.content.contentexternalbackdroplink
    //
    // closing the link off the UI thread is a silent fail-fast kill of the process, so the owner keeps this
    // instance rooted and only ever calls Close on the UI thread; the raw reference held here is what keeps the
    // link alive, so the managed wrappers handed out below never drop the last one
    internal sealed unsafe class WinBackdropLink
    {
        // === interop ===

        private const string RuntimeClassName = "Microsoft.UI.Content.ContentExternalBackdropLink";
        private const string ServerDll = "Microsoft.UI.Input.dll"; // the server the runtime manifest names

        // interface ids; vtable slots count after the six IInspectable ones
        private static readonly Guid StaticsIid = new("46cac6fb-bb51-510a-958d-e0eb4160f678"); // 6 Create
        private static readonly Guid LinkIid = new("1054bf83-b35b-5fde-8dd7-ac3bb3e6ce27"); // 6 DispatcherQueue, 7/8 BorderMode, 9 PlacementVisual
        private static readonly Guid SupportsBackdropIid = new("397dafe4-b6c2-5bb9-951d-f5707de8b7bc");
        private static readonly Guid ClosableIid = new("30d5a829-7fa4-4026-83bb-d75bae4ea99e"); // 6 Close
        private static readonly Guid CompositorIid = new("95213c13-c4cb-57de-b267-d21ab901ae38"); // the Create argument

        private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);


        // === fields ===

        private IntPtr _link; // IContentExternalBackdropLink, owned


        // === public api ===

        // the target to hand to a backdrop controller (AddSystemBackdropTarget)
        public ICompositionSupportsSystemBackdrop Target { get; }

        // the visual the material is drawn into; the owner parents, sizes and clips it
        public Visual PlacementVisual { get; }

        public DispatcherQueue DispatcherQueue { get; }

        // how the material treats its own edge; Inherit 0, Soft 1, Hard 2
        public CompositionBorderMode BorderMode
        {
            get
            {
                ThrowIfClosed();
                int mode;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(_link, 7))(_link, &mode));
                return (CompositionBorderMode)mode;
            }
            set
            {
                ThrowIfClosed();
                Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int>)VTable(_link, 8))(_link, (int)value));
            }
        }

        // where the activation came from, for the log; "RoGetActivationFactory" or the server dll fallback
        public string ActivationRoute { get; }

        public bool IsClosed => _link == IntPtr.Zero;

        // creates a link on the compositors thread (the UI thread); throws on any failure, the caller falls back
        public static WinBackdropLink Create(Compositor compositor)
        {
            IntPtr statics = GetStatics(out string route);
            IntPtr compositorAbi = IntPtr.Zero;
            IntPtr compositorInspectable = IntPtr.Zero;
            IntPtr link = IntPtr.Zero;

            try
            {
                compositorInspectable = MarshalInspectable<object>.FromManaged(compositor);
                Check(Marshal.QueryInterface(compositorInspectable, in CompositorIid, out compositorAbi));

                IntPtr created;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VTable(statics, 6))(statics, compositorAbi, &created));

                // the default interface comes back; ask for it by id anyway, so a changed default fails loudly here
                int hr = Marshal.QueryInterface(created, in LinkIid, out link);
                Marshal.Release(created);
                Check(hr);

                var result = new WinBackdropLink(link, route);
                link = IntPtr.Zero;
                return result;
            }
            finally
            {
                if (link != IntPtr.Zero) Marshal.Release(link);
                if (compositorAbi != IntPtr.Zero) Marshal.Release(compositorAbi);
                if (compositorInspectable != IntPtr.Zero) Marshal.Release(compositorInspectable);
                Marshal.Release(statics);
            }
        }

        // UI thread only; closes the link for good, the managed wrappers above go dead with it
        public void Close()
        {
            if (_link == IntPtr.Zero) return;

            if (Marshal.QueryInterface(_link, in ClosableIid, out IntPtr closable) >= 0)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, int>)VTable(closable, 6))(closable);
                Marshal.Release(closable);
            }

            Marshal.Release(_link);
            _link = IntPtr.Zero;
        }


        // === construction ===

        private WinBackdropLink(IntPtr link, string route)
        {
            _link = link;
            ActivationRoute = route;

            IntPtr queue;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(_link, 6))(_link, &queue));
            DispatcherQueue = DispatcherQueue.FromAbi(queue);
            if (queue != IntPtr.Zero) Marshal.Release(queue);

            IntPtr visual;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(_link, 9))(_link, &visual));
            PlacementVisual = Visual.FromAbi(visual);
            if (visual != IntPtr.Zero) Marshal.Release(visual);

            Check(Marshal.QueryInterface(_link, in SupportsBackdropIid, out IntPtr supports));
            Target = MarshalInterface<ICompositionSupportsSystemBackdrop>.FromAbi(supports);
            Marshal.Release(supports);
        }

        // the regfree manifest normally resolves the class; the server dll is asked directly only if it does not
        private static IntPtr GetStatics(out string route)
        {
            Check(WindowsCreateString(RuntimeClassName, RuntimeClassName.Length, out IntPtr className));

            try
            {
                int hr = RoGetActivationFactory(className, in StaticsIid, out IntPtr statics);
                if (hr >= 0)
                {
                    route = "RoGetActivationFactory";
                    return statics;
                }
                if (hr != REGDB_E_CLASSNOTREG) Check(hr);

                IntPtr module = LoadLibraryW(ServerDll);
                if (module == IntPtr.Zero) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                IntPtr export = GetProcAddress(module, "DllGetActivationFactory");
                if (export == IntPtr.Zero) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                IntPtr factory;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)export)(className, &factory));

                hr = Marshal.QueryInterface(factory, in StaticsIid, out statics);
                Marshal.Release(factory);
                Check(hr);

                route = ServerDll;
                return statics;
            }
            finally
            {
                WindowsDeleteString(className);
            }
        }

        private static IntPtr VTable(IntPtr instance, int slot) => (*(IntPtr**)instance)[slot];

        private static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        private void ThrowIfClosed()
        {
            if (_link == IntPtr.Zero) throw new ObjectDisposedException(nameof(WinBackdropLink));
        }
    }
}
