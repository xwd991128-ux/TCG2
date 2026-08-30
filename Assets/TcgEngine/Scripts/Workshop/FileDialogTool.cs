using System;
using System.Reflection;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// PC 平台原生文件对话框（反射调用 System.Windows.Forms，避免编译期依赖）
    /// 仅在 Windows 桌面（编辑器/玩家）可用；其他平台返回 null，由调用方降级处理
    /// </summary>
    public static class FileDialogTool
    {
        private static bool inited = false;
        private static bool available = false;
        private static Type open_type;
        private static Type save_type;
        private static Type folder_type;

        private static void Init()
        {
            if (inited)
                return;
            inited = true;

            try
            {
                if (!IsWindows())
                    return;

                Assembly asm = Assembly.Load("System.Windows.Forms");
                if (asm == null)
                    return;

                open_type = asm.GetType("System.Windows.Forms.OpenFileDialog");
                save_type = asm.GetType("System.Windows.Forms.SaveFileDialog");
                folder_type = asm.GetType("System.Windows.Forms.FolderBrowserDialog");
                available = open_type != null && save_type != null && folder_type != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("文件对话框不可用: " + e.Message);
                available = false;
            }
        }

        private static bool IsWindows()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                || Application.platform == RuntimePlatform.WindowsPlayer;
        }

        /// <summary>打开多选文件对话框，返回文件路径数组（取消/不可用返回 null）</summary>
        public static string[] OpenFiles(string title, string filter, bool multiselect = true)
        {
            Init();
            if (!available)
                return null;

            try
            {
                object dialog = Activator.CreateInstance(open_type);
                SetProperty(dialog, "Title", title);
                SetProperty(dialog, "Filter", filter);
                SetProperty(dialog, "Multiselect", multiselect);
                SetProperty(dialog, "CheckFileExists", true);
                SetProperty(dialog, "RestoreDirectory", true);
                if (!ShowOk(dialog))
                    return null;
                return (string[])GetProperty(dialog, "FileNames");
            }
            catch (Exception e)
            {
                Debug.LogWarning("打开文件对话框失败: " + e.Message);
                return null;
            }
        }

        /// <summary>打开保存文件对话框，返回选择的文件路径（取消/不可用返回 null）</summary>
        public static string SaveFile(string title, string filter, string defaultName)
        {
            Init();
            if (!available)
                return null;

            try
            {
                object dialog = Activator.CreateInstance(save_type);
                SetProperty(dialog, "Title", title);
                SetProperty(dialog, "Filter", filter);
                SetProperty(dialog, "FileName", defaultName);
                SetProperty(dialog, "OverwritePrompt", true);
                SetProperty(dialog, "RestoreDirectory", true);
                if (!ShowOk(dialog))
                    return null;
                return (string)GetProperty(dialog, "FileName");
            }
            catch (Exception e)
            {
                Debug.LogWarning("保存文件对话框失败: " + e.Message);
                return null;
            }
        }

        /// <summary>打开文件夹选择对话框，返回目录路径（取消/不可用返回 null）</summary>
        public static string SelectFolder(string title)
        {
            Init();
            if (!available)
                return null;

            try
            {
                object dialog = Activator.CreateInstance(folder_type);
                SetProperty(dialog, "Description", title);
                SetProperty(dialog, "ShowNewFolderButton", true);
                if (!ShowOk(dialog))
                    return null;
                return (string)GetProperty(dialog, "SelectedPath");
            }
            catch (Exception e)
            {
                Debug.LogWarning("选择文件夹对话框失败: " + e.Message);
                return null;
            }
        }

        private static bool ShowOk(object dialog)
        {
            object result = CallMethod(dialog, "ShowDialog");
            //DialogResult.OK == 1
            if (result is Enum)
                return Convert.ToInt32(result) == 1;
            return result != null && result.ToString() == "OK";
        }

        private static void SetProperty(object obj, string name, object val)
        {
            PropertyInfo prop = obj.GetType().GetProperty(name);
            if (prop != null && prop.CanWrite)
                prop.SetValue(obj, val, null);
        }

        private static object GetProperty(object obj, string name)
        {
            PropertyInfo prop = obj.GetType().GetProperty(name);
            if (prop != null && prop.CanRead)
                return prop.GetValue(obj, null);
            return null;
        }

        private static object CallMethod(object obj, string name)
        {
            MethodInfo method = obj.GetType().GetMethod(name, Type.EmptyTypes);
            if (method != null)
                return method.Invoke(obj, null);
            return null;
        }
    }
}
