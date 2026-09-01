using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// Windows 原生"现代"文件对话框（IFileOpenDialog / IFileSaveDialog）。
    /// 直接调用系统新版资源管理器样式的对话框，替代老式 System.Windows.Forms 对话框。
    /// 仅在 Windows 桌面（编辑器/玩家）可用；其他平台返回 null，由 FileDialogTool 回退旧实现。
    /// </summary>
    public static class ModernFileDialog
    {
        /// <summary>上一次调用是否因"用户取消"而返回 null。
        /// FileDialogTool 据此判断：取消则不再回退弹老式对话框（避免连续弹出两个对话框）。</summary>
        public static bool LastShowCancelled = false;

        public static bool IsWindows()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                || Application.platform == RuntimePlatform.WindowsPlayer;
        }

        /// <summary>打开多选文件对话框，返回文件路径数组（取消/不可用返回 null）</summary>
        public static string[] OpenFiles(string title, string filter, bool multiselect, string initialDir)
        {
            LastShowCancelled = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return WinOpenFiles(title, filter, multiselect, initialDir);
#else
            return null;
#endif
        }

        /// <summary>打开保存文件对话框，返回文件路径（取消/不可用返回 null）</summary>
        public static string SaveFile(string title, string filter, string defaultName, string initialDir)
        {
            LastShowCancelled = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return WinSaveFile(title, filter, defaultName, initialDir);
#else
            return null;
#endif
        }

        /// <summary>打开文件夹选择对话框，返回目录路径（取消/不可用返回 null）</summary>
        public static string SelectFolder(string title, string initialDir)
        {
            LastShowCancelled = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return WinSelectFolder(title, initialDir);
#else
            return null;
#endif
        }

        // ==================== Windows 实现 ====================

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN

        private const string FILTER_SEP = "|";

        private static string[] WinOpenFiles(string title, string filter, bool multiselect, string initialDir)
        {
            try
            {
                var dlg = (IFileOpenDialogNative)new FileOpenDialogCoclass();
                COMDLG_FILTERSPEC[] specs = ParseFilter(filter);
                if (specs.Length > 0)
                    dlg.SetFileTypes((uint)specs.Length, specs);
                dlg.SetTitle(title ?? "选择文件");

                FILEOPENDIALOGOPTIONS opts = FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                    | FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST
                    | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST;
                if (multiselect)
                    opts |= FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT;
                dlg.SetOptions(opts);

                if (dlg.Show(GetForegroundWindow()) != 0)
                {
                    LastShowCancelled = true;
                    return null;   //取消
                }
                LastShowCancelled = false;

                List<string> result = new List<string>();
                if (multiselect)
                {
                    if (dlg.GetResults(out IShellItemArrayNative array) != 0)
                        return null;
                    array.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (array.GetItemAt(i, out IShellItemNative item) == 0 && item != null)
                        {
                            if (item.GetDisplayName(SIGDN.FILESYSPATH, out string p) == 0 && !string.IsNullOrEmpty(p))
                                result.Add(p);
                        }
                    }
                }
                else
                {
                    if (dlg.GetResult(out IShellItemNative item) != 0)
                        return null;
                    if (item != null && item.GetDisplayName(SIGDN.FILESYSPATH, out string p) == 0 && !string.IsNullOrEmpty(p))
                        result.Add(p);
                }
                return result.Count > 0 ? result.ToArray() : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("现代文件对话框(打开)失败: " + e.Message);
                return null;
            }
        }

        private static string WinSaveFile(string title, string filter, string defaultName, string initialDir)
        {
            try
            {
                var dlg = (IFileSaveDialogNative)new FileSaveDialogCoclass();
                COMDLG_FILTERSPEC[] specs = ParseFilter(filter);
                if (specs.Length > 0)
                    dlg.SetFileTypes((uint)specs.Length, specs);
                dlg.SetTitle(title ?? "保存文件");
                if (!string.IsNullOrEmpty(defaultName))
                    dlg.SetFileName(defaultName);

                FILEOPENDIALOGOPTIONS opts = FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                    | FILEOPENDIALOGOPTIONS.FOS_OVERWRITEPROMPT
                    | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST;
                dlg.SetOptions(opts);

                if (dlg.Show(GetForegroundWindow()) != 0)
                {
                    LastShowCancelled = true;
                    return null;
                }
                LastShowCancelled = false;
                if (dlg.GetResult(out IShellItemNative item) != 0)
                    return null;
                if (item != null && item.GetDisplayName(SIGDN.FILESYSPATH, out string p) == 0 && !string.IsNullOrEmpty(p))
                    return p;
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("现代文件对话框(保存)失败: " + e.Message);
                return null;
            }
        }

        private static string WinSelectFolder(string title, string initialDir)
        {
            try
            {
                var dlg = (IFileOpenDialogNative)new FileOpenDialogCoclass();
                dlg.SetTitle(title ?? "选择文件夹");
                FILEOPENDIALOGOPTIONS opts = FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS
                    | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                    | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST;
                dlg.SetOptions(opts);

                if (dlg.Show(GetForegroundWindow()) != 0)
                {
                    LastShowCancelled = true;
                    return null;
                }
                LastShowCancelled = false;
                if (dlg.GetResult(out IShellItemNative item) != 0)
                    return null;
                if (item != null && item.GetDisplayName(SIGDN.FILESYSPATH, out string p) == 0 && !string.IsNullOrEmpty(p))
                    return p;
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("现代文件夹对话框失败: " + e.Message);
                return null;
            }
        }

        /// <summary>解析 "名称|*.png;*.jpg" 过滤器字符串为 COMDLG_FILTERSPEC[]</summary>
        private static COMDLG_FILTERSPEC[] ParseFilter(string filter)
        {
            List<COMDLG_FILTERSPEC> list = new List<COMDLG_FILTERSPEC>();
            if (string.IsNullOrEmpty(filter))
                return list.ToArray();

            string[] parts = filter.Split(new[] { FILTER_SEP }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                string name = parts[i].Trim();
                string spec = parts[i + 1].Trim();
                if (spec.IndexOf(';') >= 0)
                {
                    string[] exts = spec.Split(';');
                    spec = string.Join(";", exts);
                }
                list.Add(new COMDLG_FILTERSPEC { pszName = name, pszSpec = spec });
            }
            if (list.Count == 0)
                list.Add(new COMDLG_FILTERSPEC { pszName = "所有文件", pszSpec = "*.*" });
            return list.ToArray();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // ---------------- COM 接口定义 ----------------

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        public class FileOpenDialogCoclass { }

        [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
        public class FileSaveDialogCoclass { }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
        }

        [Flags]
        public enum FILEOPENDIALOGOPTIONS : uint
        {
            FOS_OVERWRITEPROMPT = 0x00000002,
            FOS_STRICTFILETYPES = 0x00000004,
            FOS_NOCHANGEDIR = 0x00000008,
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_ALLNONSTORAGEITEMS = 0x00000080,
            FOS_NOVALIDATE = 0x00000100,
            FOS_ALLOWMULTISELECT = 0x00000200,
            FOS_PATHMUSTEXIST = 0x00000800,
            FOS_FILEMUSTEXIST = 0x00001000,
            FOS_CREATEPROMPT = 0x00002000,
        }

        public enum SIGDN : uint
        {
            NORMALDISPLAY = 0x00000000,
            PARENTRELATIVEPARSING = 0x80018001,
            DESKTOPABSOLUTEPARSING = 0x80028000,
            PARENTRELATIVEEDITING = 0x80031001,
            DESKTOPABSOLUTEEDITING = 0x8004c000,
            FILESYSPATH = 0x80058000,
            URL = 0x80068000,
        }

        public enum FDAP : int
        {
            FDAP_BOTTOM = 0,
            FDAP_TOP = 1,
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemNative
        {
            [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IShellItemNative psi, int hint, out int piOrder);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemArrayNative
        {
            [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetAttributes(int attribFlags, int sfgaoMask, out int psfgaoAttribs);
            [PreserveSig] int GetCount(out uint pdwNumItems);
            [PreserveSig] int GetItemAt(uint dwIndex, [MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int EnumItems([MarshalAs(UnmanagedType.Interface)] out object ppenumItems);
        }

        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileOpenDialogNative
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr hwnd);
            // IFileDialog
            [PreserveSig] int SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] COMDLG_FILTERSPEC[] rgFilterSpec);
            [PreserveSig] int SetFileTypeIndex(uint iFileType);
            [PreserveSig] int GetFileTypeIndex(out uint piFileType);
            [PreserveSig] int Advise([MarshalAs(UnmanagedType.Interface)] object pfde, out uint pdwCookie);
            [PreserveSig] int Unadvise(uint dwCookie);
            [PreserveSig] int SetOptions(FILEOPENDIALOGOPTIONS fos);
            [PreserveSig] int GetOptions(out FILEOPENDIALOGOPTIONS pfos);
            [PreserveSig] int SetDefaultFolder([MarshalAs(UnmanagedType.Interface)] IShellItemNative psi);
            [PreserveSig] int SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItemNative psi);
            [PreserveSig] int GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszFilename);
            [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            [PreserveSig] int GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int AddPlace([MarshalAs(UnmanagedType.Interface)] IShellItemNative psi, FDAP fdap);
            [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszExt);
            [PreserveSig] int Close(int hr);
            [PreserveSig] int SetClientGuid(ref Guid guid);
            [PreserveSig] int ClearClientData();
            [PreserveSig] int SetFilter([MarshalAs(UnmanagedType.Interface)] object pFilter);
            // IFileOpenDialog
            [PreserveSig] int GetResults([MarshalAs(UnmanagedType.Interface)] out IShellItemArrayNative ppenum);
            [PreserveSig] int GetSelectedItems([MarshalAs(UnmanagedType.Interface)] out IShellItemArrayNative ppsai);
        }

        [ComImport, Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileSaveDialogNative
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr hwnd);
            // IFileDialog
            [PreserveSig] int SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] COMDLG_FILTERSPEC[] rgFilterSpec);
            [PreserveSig] int SetFileTypeIndex(uint iFileType);
            [PreserveSig] int GetFileTypeIndex(out uint piFileType);
            [PreserveSig] int Advise([MarshalAs(UnmanagedType.Interface)] object pfde, out uint pdwCookie);
            [PreserveSig] int Unadvise(uint dwCookie);
            [PreserveSig] int SetOptions(FILEOPENDIALOGOPTIONS fos);
            [PreserveSig] int GetOptions(out FILEOPENDIALOGOPTIONS pfos);
            [PreserveSig] int SetDefaultFolder([MarshalAs(UnmanagedType.Interface)] IShellItemNative psi);
            [PreserveSig] int SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItemNative psi);
            [PreserveSig] int GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszFilename);
            [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            [PreserveSig] int GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItemNative ppsi);
            [PreserveSig] int AddPlace([MarshalAs(UnmanagedType.Interface)] IShellItemNative psi, FDAP fdap);
            [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszExt);
            [PreserveSig] int Close(int hr);
            [PreserveSig] int SetClientGuid(ref Guid guid);
            [PreserveSig] int ClearClientData();
            [PreserveSig] int SetFilter([MarshalAs(UnmanagedType.Interface)] object pFilter);
        }
#endif
    }
}
