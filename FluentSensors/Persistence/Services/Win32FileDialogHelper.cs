using System;
using System.Runtime.InteropServices;


namespace FluentSensors.Persistence.Services
{
    // the win32 file dialogs:
    // the WinRT pickers throw COMException 0x80004005 in an elevated process, as documented ("not designed to be used
    // in an elevated app"), and this app always runs elevated:
    // https://learn.microsoft.com/en-us/uwp/api/windows.storage.pickers.filesavepicker
    // https://github.com/microsoft/WindowsAppSDK/issues/2504
    // https://github.com/microsoft/WindowsAppSDK/issues/2731
    // the classic COM dialogs (IFileSaveDialog, IFileOpenDialog) run in-process, the documented fallback, by
    // ComImport rather than CsWin32
    public static class Win32FileDialogHelper
    {
        // === public api ===

        // synchronous wrappers; the picked path or null (cancelled or failed), the COM object released right after

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


        // the open dialog in folder mode (FOS_PICKFOLDERS); a missing initialFolder falls
        // back to the last-used location
        public static string PickFolder(IntPtr ownerHwnd, string title, string initialFolder)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                dialog.SetTitle(title);

                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FOS_PICKFOLDERS);

                if (!string.IsNullOrEmpty(initialFolder))
                {
                    try
                    {
                        var guid = IID_IShellItem;
                        if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref guid, out var startItem) == 0)
                        {
                            dialog.SetFolder(startItem);
                            Marshal.ReleaseComObject(startItem);
                        }
                    }
                    catch { /* only the starting location, a failure here still leaves a usable dialog */ }
                }

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

        // the subset of shobjidl_core.h for single-file save and open plus folder picking, by
        // hand to stay self-contained

        // the _FILEOPENDIALOGOPTIONS flag for a folder browser
        private const uint FOS_PICKFOLDERS = 0x00000020;

        private static Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

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
            // its own members after the IFileDialog ones are never called, so not declared
        }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog : IFileDialog
        {
            // GetResults and GetSelectedItems (multi-select) are not needed
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
