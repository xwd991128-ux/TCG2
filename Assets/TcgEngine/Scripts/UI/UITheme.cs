using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// UI 设计令牌（Design Tokens）：全项目唯一的颜色 / 字号 / 尺寸 / 间距 / 过渡取值来源。
    ///
    /// 定位：这是 Unity uGUI 项目，没有 CSS，视觉值原本散落在各处（8 份 Builder 里逐字复制、
    /// GraphEditorPanel 里约 55 处内联 new Color(...)、两处运行时字号归一化 hack），
    /// 本类把这些值收敛成一份可引用的常量表。
    ///
    /// 使用约定：
    ///   1. 新代码一律引用令牌，不要再写 new Color(...) / 魔法数字；
    ///   2. 「颜色」区分 Theme（视觉主题，参与统一改版）与 Semantic（功能语义色，冻结不动）；
    ///   3. 本类属运行时程序集（Assets 下无 asmdef），Scripts/Editor 下的生成工具可直接引用。
    ///
    /// 阶段说明：P0 完成「登记 + 等价搬迁」；P1 统一字体管线（UIFonts）；
    /// P2 统一全屏页遮罩（MaskPage）、页标题（FontPageTitle）与运行时字号归一化；
    /// P3 统一其余页标题 / 状态条字号（FontStatus）与表单控件取值（FieldBg / Placeholder / FontBody），
    /// 并把重复的输入框实现迁入 UIFactory；控件库与其余散值仍按 P4 逐步替换。
    /// </summary>
    public static class UITheme
    {
        // ==================== 资源路径（各 Builder 顶部同名常量指向这里） ====================

        /// <summary>页面主字体（标准 TTF 中文字体，Unity 可直接导入）</summary>
        public const string FontPath = "Assets/TcgEngine/Fonts/SimHei.ttf";

        /// <summary>主字体缺失时的回退字体</summary>
        public const string FontFallbackPath = "Assets/TcgEngine/Fonts/OpenSans-Bold.ttf";

        /// <summary>关闭 / 返回 / 退出按钮用的图标</summary>
        public const string ExitIconPath = "Assets/TcgEngine/Sprites/UI/exit.png";

        /// <summary>项目里烘焙好的中文 TMP 字体资产（由规则编辑器生成工具创建，UIFonts 解析时优先复用）</summary>
        public const string TmpFontPath = "Assets/TcgEngine/Fonts/SimHei_TMP.asset";

        // ==================== 遮罩与底色 ====================

        /// <summary>页面级遮罩：全屏页盖在别的页之上时的黑幕（CardEditorBuilder/GraphEditorBuilder 原为 0.90）</summary>
        public static readonly Color MaskPage = new Color(0f, 0f, 0f, 0.90f);

        /// <summary>弹层遮罩（RichTextPopupUI / ImageClipPopupUI / 规则编辑器弹层一致）</summary>
        public static readonly Color MaskPopup = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>内容区块底色（全项目众数）</summary>
        public static readonly Color BgBlock = new Color(0f, 0f, 0f, 0.35f);

        /// <summary>滚动视口底</summary>
        public static readonly Color BgViewport = new Color(1f, 1f, 1f, 0.03f);

        /// <summary>规则编辑器画布底（工作台语义，刻意比区块底更暗更偏冷）</summary>
        public static readonly Color BgCanvas = new Color(0.05f, 0.06f, 0.09f, 0.6f);

        /// <summary>弹层面板底</summary>
        public static readonly Color BgPopup = new Color(0.12f, 0.12f, 0.15f, 1f);

        // ==================== 控件底 ====================

        /// <summary>常规按钮 / 控件底</summary>
        public static readonly Color Ctrl = new Color(1f, 1f, 1f, 0.18f);

        /// <summary>弱化控件底（列表行、次级容器）</summary>
        public static readonly Color CtrlWeak = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>强控件底（返回键等主要操作）</summary>
        public static readonly Color CtrlStrong = new Color(1f, 1f, 1f, 0.25f);

        /// <summary>输入框底</summary>
        public static readonly Color FieldBg = new Color(1f, 1f, 1f, 0.15f);

        /// <summary>分隔线（通常用作 1px 高的 Image）</summary>
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.06f);

        // ==================== 文字色 ====================

        /// <summary>页标题 / 区块标题（全项目一致）</summary>
        public static readonly Color TextTitle = new Color(0.76f, 1f, 0.99f, 1f);

        /// <summary>正文</summary>
        public static readonly Color TextBody = Color.white;

        /// <summary>次要说明文字</summary>
        public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.75f);

        /// <summary>提示 / 帮助文字</summary>
        public static readonly Color Hint = new Color(0.80f, 0.85f, 0.90f, 1f);

        /// <summary>输入框占位文字</summary>
        public static readonly Color Placeholder = new Color(1f, 1f, 1f, 0.40f);

        // ==================== 语义色（功能相关，改版时冻结，不要为了"统一"而改动） ====================

        /// <summary>错误 / 删除 / 危险</summary>
        public static readonly Color Danger = new Color(1f, 0.55f, 0.55f, 1f);

        /// <summary>高亮 / 警告 / 正在运行</summary>
        public static readonly Color Accent = new Color(1f, 0.85f, 0.30f, 1f);

        /// <summary>品牌紫 #7c5cff（动作连线 / 选中态）</summary>
        public static readonly Color Brand = new Color(0.486f, 0.361f, 1f, 1f);

        // ==================== 分类色（原 CardEditorPanel 顶部的 Col* 常量） ====================

        public static readonly Color CatBlue = new Color(0.5f, 0.78f, 1f, 0.4f);
        public static readonly Color CatGreen = new Color(0.5f, 0.9f, 0.6f, 0.4f);
        public static readonly Color CatRed = new Color(1f, 0.6f, 0.6f, 0.4f);
        public static readonly Color CatGold = new Color(1f, 0.85f, 0.6f, 0.4f);
        public static readonly Color CatPurple = new Color(0.78f, 0.62f, 1f, 0.4f);
        public static readonly Color CatPink = new Color(1f, 0.62f, 0.85f, 0.4f);

        // ==================== 字号（P2/P3 起统一替换；括号内为替换前的散值） ====================

        /// <summary>页标题（原：卡牌编辑器 40 / 规则编辑器 34 / 关键词管理 28 / 卡池管理 42 / 筛选 34）</summary>
        public const int FontPageTitle = 36;

        /// <summary>区块标题（原：28 / 24，运行时曾被归一化成 20；P2 起归一化统一取此值）</summary>
        public const int FontSection = 24;

        /// <summary>按钮文字（原：24 / 22 / 20）</summary>
        public const int FontButton = 22;

        /// <summary>状态条 / 底部提示（原：各页 20 / 卡池管理 22）</summary>
        public const int FontStatus = 20;

        /// <summary>正文</summary>
        public const int FontBody = 16;

        /// <summary>次要说明</summary>
        public const int FontSmall = 15;

        /// <summary>极小标注</summary>
        public const int FontTiny = 13;

        // ==================== 按钮尺寸 ====================

        /// <summary>标准按钮高（卡牌编辑器操作栏）</summary>
        public const float BtnH = 46f;

        /// <summary>小一号按钮高（规则编辑器顶栏）</summary>
        public const float BtnHSm = 44f;

        /// <summary>图标按钮边长（返回键，热区下限，不要为了"统一"缩小）</summary>
        public const float BtnIcon = 56f;

        /// <summary>短按钮宽</summary>
        public const float BtnWShort = 110f;

        /// <summary>常规按钮宽</summary>
        public const float BtnWNormal = 130f;

        /// <summary>宽按钮宽</summary>
        public const float BtnWWide = 160f;

        /// <summary>列表行高</summary>
        public const float RowH = 44f;

        // ==================== 间距阶梯（一律取这四档） ====================

        public const float GapXs = 4f;
        public const float GapSm = 6f;
        public const float GapMd = 8f;
        public const float GapLg = 12f;

        /// <summary>区块内边距（左/右/上/下）</summary>
        public static RectOffset PadBlock()
        {
            return new RectOffset(10, 10, 6, 6);
        }

        /// <summary>列表容器内边距（左/右 GapLg、上/下 GapMd）</summary>
        public static RectOffset PadList()
        {
            return new RectOffset((int)GapLg, (int)GapLg, (int)GapMd, (int)GapMd);
        }

        // ==================== 过渡动画 ====================

        /// <summary>与 UIPanel.display_speed 的默认值保持一致</summary>
        public const float FadeSpeed = 4f;

        /// <summary>按钮过渡常规态</summary>
        public static readonly Color BtnTintNormal = Color.white;

        /// <summary>按钮过渡悬停态</summary>
        public static readonly Color BtnTintHighlight = new Color(1.25f, 1.25f, 1.25f, 1f);

        /// <summary>按钮过渡按下态</summary>
        public static readonly Color BtnTintPressed = new Color(0.7f, 0.7f, 0.7f, 1f);

        /// <summary>按钮过渡时长</summary>
        public const float BtnFadeDuration = 0.1f;

        /// <summary>
        /// 把按钮过渡态套到指定 Button 上（保留 selected/disabled 等其它字段，只覆盖这四个）。
        /// 与各 Builder 里内联的「读 → 改 → 写」写法完全等价。
        /// </summary>
        public static void ApplyButtonColors(UnityEngine.UI.Button btn)
        {
            if (btn == null)
                return;
            UnityEngine.UI.ColorBlock colors = btn.colors;
            colors.normalColor = BtnTintNormal;
            colors.highlightedColor = BtnTintHighlight;
            colors.pressedColor = BtnTintPressed;
            colors.fadeDuration = BtnFadeDuration;
            btn.colors = colors;
        }
    }
}
