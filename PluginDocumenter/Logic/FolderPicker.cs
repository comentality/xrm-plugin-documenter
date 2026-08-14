using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PluginDocumenter.Logic
{
    /// <summary>
    /// Folder picker built on the Vista-era IFileOpenDialog, i.e. the same Explorer window
    /// users see everywhere else, with a path bar, search and the usual navigation pane.
    /// WinForms' <see cref="FolderBrowserDialog"/> on .NET Framework is still the old
    /// SHBrowseForFolder tree, so it is only kept here as a fallback.
    /// </summary>
    internal static class FolderPicker
    {
        /// <summary>Returns the picked folder, or null when the user cancelled.</summary>
        public static string Pick(IWin32Window owner, string title, string initialFolder)
        {
            try
            {
                return PickVista(owner, title, initialFolder);
            }
            catch (Exception)
            {
                // Anything odd about the shell (older OS, blocked COM) falls back to the old dialog.
                return PickLegacy(title, initialFolder);
            }
        }

        private static string PickVista(IWin32Window owner, string title, string initialFolder)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);

                var start = ExistingFolder(initialFolder);
                if (start != null)
                {
                    object item;
                    if (SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out item) == 0)
                    {
                        dialog.SetFolder((IShellItem)item);
                    }
                }

                var hwnd = owner != null ? owner.Handle : IntPtr.Zero;
                if (dialog.Show(hwnd) != 0)
                {
                    return null; // cancelled
                }

                IShellItem result;
                dialog.GetResult(out result);
                string path;
                result.GetDisplayName(SIGDN_FILESYSPATH, out path);
                Marshal.ReleaseComObject(result);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        private static string PickLegacy(string title, string initialFolder)
        {
            using (var dialog = new FolderBrowserDialog { Description = title })
            {
                var start = ExistingFolder(initialFolder);
                if (start != null)
                {
                    dialog.SelectedPath = start;
                }

                return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        private static string ExistingFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var full = Path.GetFullPath(path.Trim());
                return Directory.Exists(full) ? full : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindingContext,
            [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out object item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        /// <summary>
        /// IModalWindow + IFileDialog + IFileOpenDialog flattened into one declaration: the
        /// slots have to appear in vtable order, and the ones this tool never calls are
        /// declared with blind arguments just to keep the layout right.
        /// </summary>
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // IModalWindow
            [PreserveSig]
            int Show(IntPtr parent);

            // IFileDialog
            void SetFileTypes(uint fileTypes, IntPtr filterSpec);
            void SetFileTypeIndex(uint fileType);
            void GetFileTypeIndex(out uint fileType);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem place, int order);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close([MarshalAs(UnmanagedType.Error)] int result);
            void SetClientGuid(ref Guid client);
            void ClearClientData();
            void SetFilter(IntPtr filter);

            // IFileOpenDialog
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindingContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint name, [MarshalAs(UnmanagedType.LPWStr)] out string displayName);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
