using System;
using UnityEngine;
using TcgEngine.Workshop;   // FileDialogTool / ModernFileDialog（项目已有的文件对话框封装）

namespace TcgEngine.UI
{
    /// <summary>
    /// 本地文件对话框的薄封装（把实现收在一处，方便日后整体替换成 StandaloneFileBrowser 等）。
    ///
    /// 本版**不引入 StandaloneFileBrowser**，直接复用项目已有的 TcgEngine.Workshop.FileDialogTool：
    /// 它先走 Win32 现代对话框（ModernFileDialog 的 IFileOpenDialog COM），取消/不可用时才回退到
    /// 反射调用的 System.Windows.Forms.OpenFileDialog。
    /// 理由：① 零新增第三方包，无网络下载与授权风险；② 不需要处理插件的 .meta / .gitignore；
    ///      ③ 该封装已在项目里用于选卡图、选音效、选文件夹，行为与打包配置已被验证。
    ///
    /// 平台支持矩阵：
    /// - Windows 编辑器 / Windows 独立版：本地文件导入 ✔
    /// - macOS / Linux：✘（FileDialogTool 判定为不可用）→ 只能用「图库」
    /// - Android / iOS / WebGL：运行时没有通用文件对话框 → 只能用「图库」
    /// </summary>
    public static class FileBrowserBridge
    {
        /// <summary>图片过滤器（FileDialogTool 约定格式：显示名|通配符）</summary>
        public const string IMAGE_FILTER = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";

        /// <summary>音频过滤器（音效DIY/选择音效共用；格式以项目已支持解码的为准）</summary>
        public const string AUDIO_FILTER = "音频文件 (*.wav;*.ogg;*.mp3;*.aif;*.aiff)|*.wav;*.ogg;*.mp3;*.aif;*.aiff";

        private static readonly string[] SUPPORTED_EXT = { ".png", ".jpg", ".jpeg", ".bmp" };
        private static readonly string[] SUPPORTED_AUDIO_EXT = { ".wav", ".ogg", ".mp3", ".aif", ".aiff" };

        /// <summary>当前平台是否支持本地文件对话框</summary>
        public static bool IsSupported
        {
            get { return ModernFileDialog.IsWindows(); }
        }

        /// <summary>平台支持矩阵说明（用于界面提示与交付说明）</summary>
        public static string SupportMatrix
        {
            get { return "本地文件导入仅 Windows 编辑器/独立版可用；Android / iOS / WebGL / macOS / Linux 请使用「从图库选择」。"; }
        }

        /// <summary>
        /// 打开图片选择对话框，返回单个文件路径；用户取消或平台不支持时返回 null。
        ///
        /// 注意：对话框是**同步阻塞**的原生窗口，期间主线程不跑帧。
        /// 为免对局在弹框期间继续推进，这里把 Time.timeScale 临时置 0，返回前恢复原值。
        /// </summary>
        public static string OpenImageFile(string title = "选择图片")
        {
            return OpenFile(string.IsNullOrEmpty(title) ? "选择图片" : title, IMAGE_FILTER);
        }

        /// <summary>打开音频选择对话框（音效DIY导入素材用），返回单个文件路径；取消/不支持返回 null。</summary>
        public static string OpenAudioFile(string title = "选择音频")
        {
            return OpenFile(string.IsNullOrEmpty(title) ? "选择音频" : title, AUDIO_FILTER);
        }

        /// <summary>通用文件对话框（同步阻塞：期间把 Time.timeScale 置 0，返回前原样恢复）</summary>
        private static string OpenFile(string title, string filter)
        {
            if (!IsSupported)
            {
                Debug.LogWarning("本地文件导入：当前平台不支持本地文件对话框。" + SupportMatrix);
                return null;
            }

            float prev_scale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;   //弹框期间冻结对局；用 prev_scale 原样恢复，避免覆盖项目自身设置
                string[] files = FileDialogTool.OpenFiles(title, filter, false);
                if (files == null || files.Length == 0)
                    return null;
                return files[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning("打开文件对话框失败：" + e.Message);
                return null;
            }
            finally
            {
                Time.timeScale = prev_scale;
            }
        }

        /// <summary>扩展名是否受支持（png/jpg/jpeg/bmp，忽略大小写）</summary>
        public static bool IsSupportedExtension(string path)
        {
            return HasExtension(path, SUPPORTED_EXT);
        }

        /// <summary>音频扩展名是否受支持（wav/ogg/mp3/aif/aiff，忽略大小写）</summary>
        public static bool IsSupportedAudioExtension(string path)
        {
            return HasExtension(path, SUPPORTED_AUDIO_EXT);
        }

        private static bool HasExtension(string path, string[] allow)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string ext = System.IO.Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return false;
            ext = ext.ToLowerInvariant();
            for (int i = 0; i < allow.Length; i++)
            {
                if (allow[i] == ext)
                    return true;
            }
            return false;
        }
    }
}
