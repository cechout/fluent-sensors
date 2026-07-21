using System;
using System.Runtime.InteropServices;


namespace FluentSensors.Persistence.Services
{
    // drop-in replacement for the WinRT FileSavePicker/FileOpenPicker (Windows.Storage.Pickers), which crash with
    // COMException 0x80004005 when the calling process runs elevated
    // confirmed as expected behavior, not a bug, Microsofts own docs state these APIs "are not designed to be used in an
    // elevated app": https://learn.microsoft.com/en-us/uwp/api/windows.storage.pickers.filesavepicker
    // matching crash reports: https://github.com/microsoft/WindowsAppSDK/issues/2504,
    // https://github.com/microsoft/WindowsAppSDK/issues/2731
    //
    // since this app always needs admin rights for LibreHardwareMonitor, the WinRT pickers can never be used here
    // these classic Win32 COM dialogs (IFileSaveDialog/IFileOpenDialog) run in-process instead of going through a broker, so
    // process elevation does not affect them
    // this is also Microsofts own recommended fallback for elevated apps, just hand-rolled here via ComImport instead of the
    // CsWin32 source generator they suggest
    public static class Win32FileDialogHelper
    {
        // === public api ===

        // thin synchronous wrappers around the native dialogs; both return the picked path, or null if the user
        // cancelled or the dialog failed, and release the COM object again immediately after use

        // returns the picked file path, or null if the user cancelled (or the dialog failed)
        public static string PickSaveFile(IntPtr ownerHwnd, string title, string suggestedFileName, string filterName, string filterExtension)
        {
            var dialog = (IFileSaveDialog)new FileSaveDialogRCW();
            try
            {
                dialog.SetTitle(title);
                dialog.SetFileName(suggestedFileName);
                dialog.SetDefaultExtension(filterExtension.TrimStart('.'));

                var filters = new[] { new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = $"*.{filterExtension.TrimStart('.')}" } };
                dialog.SetFileTypes(1, filters);
                dialog.SetFileTypeIndex(1);

                if (dialog.Show(ownerHwnd) != 0) return null; // non-zero HRESULT: user cancelled or dialog failed

                dialog.GetResult(out var item);
                item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        public static string PickOpenFile(IntPtr ownerHwnd, string title, string filterName, string filterExtension)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                dialog.SetTitle(title);

                var filters = new[] { new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = $"*.{filterExtension.TrimStart('.')}" } };
                dialog.SetFileTypes(1, filters);
                dialog.SetFileTypeIndex(1);

                if (dialog.Show(ownerHwnd) != 0) return null; // non-zero HRESULT: user cancelled or dialog failed

                dialog.GetResult(out var item);
                item.GetDisplayName(SIGDN.FILESYSPATH, out var path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }


        // === com interop declarations ===

        // manual COM interop instead of the CsWin32 source generator Microsofts docs suggest, to keep this
        // self-contained
        // minimal subset of shobjidl_core.h; just enough surface for a single-file save/open dialog, not a general-purpose
        // wrapper (no multi-select, no folder picking, no custom places)

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileSaveDialog : IFileDialog
        {
            // IFileSaveDialog adds SetSaveAsItem/SetProperties/etc. after the inherited IFileDialog members; not
            // declared here since we never call them, we only need the base members above
        }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog : IFileDialog
        {
            // IFileOpenDialog adds GetResults/GetSelectedItems (multi-select); not needed here either
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
        }

        private enum SIGDN : uint
        {
            FILESYSPATH = 0x80058000
        }

        [ComImport, Guid("c0b4e2f3-ba21-4773-8dba-335ec946eb8b")] // CLSID_FileSaveDialog
        private class FileSaveDialogRCW { }

        [ComImport, Guid("dc1c5a9c-e88a-4dde-a5a1-60f82a20aef7")] // CLSID_FileOpenDialog
        private class FileOpenDialogRCW { }
    }
}