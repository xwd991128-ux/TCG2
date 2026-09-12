using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// UI 控件工厂：全项目唯一的 uGUI 控件创建入口。
    ///
    /// 背景：原先 <c>CreateRect / CreateImage / CreateText / CreateButton / SetStretch</c> 这几个方法
    /// 在 8 个生成工具（CardEditorBuilder / GraphEditorBuilder / BuffPanelBuilder / BattleButtonBuilder /
    /// KeywordPanelBuilder / CardPoolPanelBuilder / CardFilterBuilder / MainMenuRebuildBuilder）里
    /// 被逐字复制了 8 份，任何视觉调整都要改 8 处、且极易漏改。
    ///
    /// 本类**等价搬迁**这些实现（P0 阶段不改变任何观感），并统一从这里取 <see cref="UITheme"/> 令牌。
    /// 只负责「建控件 + 套主题」，不做布局——锚点、尺寸、布局组仍由调用方按各页面需要设置。
    ///
    /// 用法：各生成工具保留同名私有方法作为一行转发即可，调用点无需改动。
    /// </summary>
    public static class UIFactory
    {
        /// <summary>建一个只有 RectTransform 的空壳（UI 容器的基础）</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>铺满父级</summary>
        public static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>建一个纯色 Image（不接收射线，需要点击的由调用方自行开）</summary>
        public static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>
        /// 建一个全屏页面遮罩：用 UITheme.MaskPage 铺满父级、不拦截射线（页面内容叠在它上层）。
        /// 卡牌编辑器 / 规则编辑器 / 关键词管理三个全屏页原先各自内联 new Color(0,0,0,0.9f/0.92f)，
        /// 现统一走这里，保证页面级黑幕浓度完全一致（P2 统一项）。
        /// </summary>
        public static Image CreatePageMask(string name, Transform parent)
        {
            Image img = CreateImage(name, parent, UITheme.MaskPage);
            img.raycastTarget = false;
            SetStretch(img.rectTransform);
            return img;
        }

        /// <summary>
        /// 建一段 uGUI Text（遗留体系，新代码请优先用 TMP）。
        /// <paramref name="overflow"/>：文本超出矩形时是否溢出显示。默认 true（与多数生成工具一致）；
        /// 关键字管理页原实现不设置溢出模式（即默认 Wrap/Truncate），转发时传 false 以保持完全一致。
        /// </summary>
        public static Text CreateText(string name, Transform parent, string text, Font font, int size, Color color,
                                      TextAnchor align, bool overflow = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            Text txt = go.GetComponent<Text>();
            txt.text = text;
            txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = FontStyle.Normal;
            txt.alignment = align;
            txt.color = color;
            txt.raycastTarget = false;
            if (overflow)
            {
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
            }
            return txt;
        }

        /// <summary>
        /// 建一个按钮：半透明底色 Image + 居中文字 + 统一的悬停/按下过渡。
        /// 名字里含 CloseBtn / ReturnBtn / ExitBtn 时，用 exit.png 图标代替文字（沿用原各生成工具的约定）。
        /// </summary>
        /// <param name="exit_icon">关闭图标；传 null 时在编辑器下自动加载，便于调用点零改动</param>
        public static Button CreateButton(string name, Transform parent, string label, Font font, int size, Color bg_color,
                                          Sprite exit_icon = null)
        {
            Image img = CreateImage(name, parent, bg_color);
            Button btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);

            // 取消/关闭/返回按钮：用 exit.png 图标替换文字
            if (IsExitNamed(name))
            {
                Sprite icon = exit_icon != null ? exit_icon : LoadExitIcon();
                if (icon != null)
                {
                    img.sprite = icon;
                    img.type = Image.Type.Simple;
                    img.color = Color.white;
                    img.raycastTarget = true;
                }
                //注意：与原实现一致——命名命中时不再创建文字标签（若图标资源缺失会得到一个空按钮）。
                //这是既有行为，P0 保持等价，留给 P2 一并修正。
                return btn;
            }

            Text txt = CreateText("Text", img.transform, label, font, size, Color.white, TextAnchor.MiddleCenter);
            SetStretch(txt.rectTransform);
            return btn;
        }

        /// <summary>
        /// 建一段 TMP 文本（TextMeshProUGUI）。字体由调用方显式传入，保持与各页面现有字体解析逻辑解耦。
        /// </summary>
        public static TMP_Text CreateTmpText(string name, Transform parent, string text, int size, Color color,
                                            TextAlignmentOptions align, TMP_FontAsset font, bool word_wrap = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TMP_Text txt = go.GetComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = FontStyles.Normal;
            txt.alignment = align;
            txt.color = color;
            txt.raycastTarget = false;
            txt.enableWordWrapping = word_wrap;
            txt.overflowMode = TextOverflowModes.Overflow;
            return txt;
        }

        /// <summary>
        /// 建一个旧版 uGUI 输入框（半透明底 + 占位文本 + 显示文本），取值统一走 UITheme 令牌
        /// （底色 FieldBg、占位色 Placeholder、字号 FontBody、内边距 GapLg/GapXs 且左右上下对称）。
        /// 长文本统一截断：uGUI 输入框的标准配置（水平 Wrap + 垂直 Truncate），不会溢出框外。
        /// 只建控件本身，RectTransform 的锚点/尺寸由调用方按各页面布局设置。
        /// </summary>
        /// <param name="multiline">是否多行（回车换行；单行文本纵向居中，多行顶部对齐）</param>
        public static InputField CreateLegacyInputField(string name, Transform parent, string placeholder,
            Font font, bool multiline = false)
        {
            const float pad_x = UITheme.GapLg;   // 12
            const float pad_y = UITheme.GapXs;   // 4

            RectTransform rt = CreateRect(name, parent);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = UITheme.FieldBg;

            //overflow:false → uGUI 输入框标准配置（Wrap + Truncate）
            Text placeholder_txt = CreateText("Placeholder", rt, placeholder, font, UITheme.FontBody,
                UITheme.Placeholder, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, false);
            RectTransform ph_rt = placeholder_txt.rectTransform;
            ph_rt.anchorMin = Vector2.zero;
            ph_rt.anchorMax = Vector2.one;
            ph_rt.offsetMin = new Vector2(pad_x, pad_y);
            ph_rt.offsetMax = new Vector2(-pad_x, -pad_y);

            Text display = CreateText("Text", rt, "", font, UITheme.FontBody, UITheme.TextBody,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, false);
            RectTransform d_rt = display.rectTransform;
            d_rt.anchorMin = Vector2.zero;
            d_rt.anchorMax = Vector2.one;
            d_rt.offsetMin = new Vector2(pad_x, pad_y);
            d_rt.offsetMax = new Vector2(-pad_x, -pad_y);

            InputField input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = display;
            input.placeholder = placeholder_txt;
            if (multiline)
                input.lineType = InputField.LineType.MultiLineNewline;
            return input;
        }

        // ==================== 内部 ====================

        /// <summary>关闭 / 返回 / 退出类按钮的命名约定</summary>
        private static bool IsExitNamed(string name)
        {
            return !string.IsNullOrEmpty(name)
                   && (name.Contains("CloseBtn") || name.Contains("ReturnBtn") || name.Contains("ExitBtn"));
        }

        //exit.png 缓存：原实现每次点击式创建都重新 Load，这里缓存一次（同一资产，行为等价）
        private static Sprite m_exit_icon;
        private static bool m_exit_icon_loaded;

        private static Sprite LoadExitIcon()
        {
            if (m_exit_icon_loaded)
                return m_exit_icon;
            m_exit_icon_loaded = true;
#if UNITY_EDITOR
            m_exit_icon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(UITheme.ExitIconPath);
#endif
            return m_exit_icon;
        }
    }
}
