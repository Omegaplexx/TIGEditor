using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TigEditor
{
    // Folder picking. Vista and newer get the modern dialog, the same one as
    // for files but in folder mode. XP keeps the old folder tree because
    // nothing else is available there.
    public static class FolderPicker
    {
        public static string Show(IWin32Window owner, string title, string initialPath)
        {
            if (Environment.OSVersion.Version.Major >= 6)
            {
                try
                {
                    string result = ShowVista(owner, title, initialPath);
                    return result;
                }
                catch
                {
                    // did not work, fall back
                }
            }
            return ShowLegacy(owner, title, initialPath);
        }

        private static string ShowLegacy(IWin32Window owner, string title, string initialPath)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = title;
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                    dlg.SelectedPath = initialPath;
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        private const uint FosPickFolders = 0x00000020;
        private const uint FosForceFileSystem = 0x00000040;
        private const uint SigdnFileSysPath = 0x80058000;

        private static string ShowVista(IWin32Window owner, string title, string initialPath)
        {
            IFileDialog dialog = (IFileDialog)new FileOpenDialogRcw();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem);
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                {
                    // The same IShellItem identifier: here it says what kind of
                    // item we want back for the given path.
                    Guid shellItemGuid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                    IShellItem start;
                    if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, ref shellItemGuid, out start) == 0)
                        dialog.SetFolder(start);
                }

                IntPtr handle = owner == null ? IntPtr.Zero : owner.Handle;
                if (dialog.Show(handle) != 0) return null;      // cancelled

                IShellItem item;
                dialog.GetResult(out item);
                IntPtr namePtr;
                item.GetDisplayName(SigdnFileSysPath, out namePtr);
                string path = Marshal.PtrToStringUni(namePtr);
                Marshal.FreeCoTaskMem(namePtr);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc,
            ref Guid riid, out IShellItem item);

        // The numbers below are COM identifiers fixed by Windows itself. COM has
        // no names: a class or an interface is asked for by its identifier, and
        // these are published by Microsoft. They must be copied exactly, and they
        // never change, which is why they are written here as constants.
        //
        // DC1C5A9C is the FileOpenDialog class, the modern open dialog.
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw { }

        // 42f85136 is IFileDialog, what that class is talked to through.
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
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
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        // 43826d1e is IShellItem, one item of the shell: a folder or a file.
        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
