using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 全局 TMP 字体统一入口：全项目**唯一**的字体解析与套用管线。
    ///
    /// 背景：此前字体逻辑有两套——本类一套、GraphEditorPanel 内自建一套
    /// （CreateOsFontAsset / TmpFontCandidates / ApplyNodeFont / FontCovers），
    /// 且项目里同时存在两种规格的字体资产（场景烘焙的 SimHei_TMP 与运行时现做的动态字体），
    /// 于是常见"这个页面正常、那个页面方块"。现统一到本类：
    ///   · ResolveFont()            解析出一份「确定能渲染中文」的字体，并记为全局 font_asset
    ///   · Apply / ApplyResolved    批量套到某个根节点下的所有 TMP 文本与输入框
    ///   · ApplyFont(TMP_Text)      单个文本套用（节点、端口等运行时新建的文本走这里）
    ///
    /// 解析优先级：外部已设置且合格的 font_asset → 本会话已成功的缓存 → 用项目字体文件现做动态字体
    /// → 项目里已有的中文 TMP 字体资产 → TMP 默认字体（可能不含中文字形，仅保证不崩）。
    /// </summary>
    public static class UIFonts
    {
        /// <summary>全局字体资产；外部设置它即可统一切换全项目 UI 字体</summary>
        public static TMP_FontAsset font_asset;

        /// <summary>
        /// 探测句：涵盖节点标题/说明里最常用的汉字（含曾经显示为方块的：开始/生效/获取/所有/随机/筛选等）。
        /// 候选字体必须能渲染其中全部字符才算合格，从根上避免"半个词是方块"的缺字体面。
        /// </summary>
        public const string FontProbe = "规则编辑器回合开始结束生效获取友方敌方所有随机筛选目标卡牌属性攻击生命法力值抽牌治疗召唤伤害创建衍生并置入战场简单玩家触发条件类型判断打击消灭√×";

        /// <summary>
        /// 符号探测句：界面实际会渲染的符号（下拉箭头 / 勾选框 / 方向 / 播放停止 等）。
        /// 只取 GB2312 符号区里的常见符号 —— 中文字体一般都有；但**静态烘焙**的字体资产（只烤了部分字）
        /// 往往缺这些符号，表现就是"中文正常、箭头与勾选框全变方块"。
        /// 约定：缺符号**不算不可用**（避免把能显示中文的字体全否掉），但会被降级为次选。
        /// </summary>
        public const string SymbolProbe = "▼▲□■●○◆◇←→√×";

        private static TMP_FontAsset m_resolved;                        //本会话已解析成功的字体（与 font_asset 同步）
        private static TMP_FontAsset m_dynamic;                         //现做的动态中文字体缓存
        private static readonly HashSet<string> m_bad_fonts = new HashSet<string>();   //判定为坏/缺字的字体资产名

        // ==================== 套用 ====================

        /// <summary>把 font_asset 应用到 root 及其子物体（含未激活）上的所有 TMP 文本与输入框内部文本</summary>
        public static void Apply(GameObject root)
        {
            if (root == null || font_asset == null)
                return;
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
                SafeSetFont(texts[i], font_asset);
        }

        /// <summary>
        /// 给运行时新建的界面统一套字体：先解析出可用字体再套。
        /// 与 Apply 的区别是不强依赖外部先设置 font_asset，适合「弹框自建 UI」这种自举场景。
        /// </summary>
        public static void ApplyResolved(GameObject root)
        {
            if (root == null)
                return;
            TMP_FontAsset f = ResolveFont();
            if (f == null)
                return;
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
                SafeSetFont(texts[i], f);
        }

        /// <summary>
        /// 给单个 TMP 文本套用统一字体（节点、端口、按钮文字等运行时新建文本走这里）。
        /// 字体在 ResolveFont 阶段已统一验证过能渲染中文，这里不再逐文本探测——逐文本探测
        /// 代价很高（会尝试动态补字），而且会把"某个文本里有冷僻字"误判成"字体不可用"。
        /// </summary>
        public static void ApplyFont(TMP_Text text)
        {
            if (text == null)
                return;
            TMP_FontAsset f = ResolveFont();
            if (f == null || text.font == f)
                return;

            TMP_FontAsset original = text.font;   //全失败时恢复用（防 setter 半途抛异常留下 font/材质不一致的残缺态）
            try
            {
                text.font = f;
            }
            catch (System.Exception e)
            {
                m_bad_fonts.Add(f.name);
                m_resolved = null;               //下次重新解析，避开这份坏资产
                try
                {
                    if (text.fontSharedMaterial == null && original != null)
                        text.font = original;
                }
                catch (System.Exception) { }
                Debug.LogWarning("UIFonts：字体「" + f.name + "」赋值失败，已回退：" + e.Message);
            }
        }

        /// <summary>字体赋值异常保护：坏字体资产赋值时 TMP 会抛 UnassignedReferenceException，跳过即可</summary>
        public static void SafeSetFont(TMP_Text text, TMP_FontAsset font)
        {
            if (text == null || font == null || text.font == font)
                return;
            try { text.font = font; }
            catch (System.Exception e) { Debug.LogWarning("UIFonts：字体「" + font.name + "」赋值失败，已跳过：" + e.Message); }
        }

        // ==================== 解析 ====================

        /// <summary>旧名保留：语义等价于 ResolveFont()（历史调用点仍在用这个名字）</summary>
        public static TMP_FontAsset GetChineseFont()
        {
            return ResolveFont();
        }

        /// <summary>
        /// 解析出一份「确定能渲染中文」的 TMP 字体，并记为全局 font_asset。
        /// 结果会缓存，重复调用不会再做字体探测（这点很关键：本方法在运行时被每个新建文本调用）。
        /// </summary>
        public static TMP_FontAsset ResolveFont()
        {
            bool explicit_changed = font_asset != null && font_asset != m_resolved;

            //① 本会话已解析成功、且没有新的显式设置：直接复用（不再探测——本方法被每个新建文本调用）
            if (!explicit_changed && m_resolved != null)
            {
                font_asset = m_resolved;
                return m_resolved;
            }

            //② 收集候选（按优先级）：外部显式设置 → 现做动态字体 → 项目已有中文 TMP 资产
            List<TMP_FontAsset> pool = new List<TMP_FontAsset>();
            if (font_asset != null && IsUsable(font_asset))
                pool.Add(font_asset);

            TMP_FontAsset dyn = CreateDynamicChineseFont();   //48pt/2048 图盘/多页，按需增长不会缺字
            if (IsUsable(dyn) && !pool.Contains(dyn))
                pool.Add(dyn);

            List<TMP_FontAsset> candidates = TmpFontCandidates();
            for (int i = 0; i < candidates.Count; i++)
            {
                TMP_FontAsset fa = candidates[i];
                if (IsUsable(fa) && !pool.Contains(fa))
                    pool.Add(fa);
            }

            //③ 优先选"符号也齐"的候选：避免中文正常、下拉箭头/勾选框却全是方块
            TMP_FontAsset pick = null;
            for (int i = 0; i < pool.Count; i++)
            {
                if (IsSymbolComplete(pool[i]))
                {
                    pick = pool[i];
                    break;
                }
            }
            if (pick == null && pool.Count > 0)
            {
                pick = pool[0];   //都不齐：退回第一个中文可用者（不比改动前差）
                Debug.LogWarning("UIFonts：所有候选字体都缺界面符号（" + SymbolProbe + "），已选「" + pick.name
                    + "」——下拉箭头/勾选框可能显示为方块；请给该字体补图集，或改用一个完整的中文字体。");
            }
            if (pick != null)
            {
                m_resolved = pick;
                font_asset = pick;
                return pick;
            }

            //④ 全部失败：退回 TMP 默认字体（可能不含中文字形，但界面照常工作、不崩）
            try { return TMP_Settings.defaultFontAsset; }
            catch { return null; }
        }

        /// <summary>字体是否还覆盖界面用到的符号（缺符号 → 降级为次选，但不判为不可用）</summary>
        public static bool IsSymbolComplete(TMP_FontAsset fa)
        {
            return fa != null && !m_bad_fonts.Contains(fa.name) && FontCovers(fa, SymbolProbe);
        }

        /// <summary>可用性判定：非空、未被判定为坏资产、且能渲染探测句</summary>
        public static bool IsUsable(TMP_FontAsset fa)
        {
            if (fa == null || m_bad_fonts.Contains(fa.name))
                return false;
            return FontCovers(fa, FontProbe);
        }

        /// <summary>实测字体能否渲染给定文本：动态字体会当场补字，补不齐（图盘满/资产损坏）即判不合格</summary>
        public static bool FontCovers(TMP_FontAsset fa, string text)
        {
            if (fa == null || string.IsNullOrEmpty(text))
                return true;
            try
            {
                uint[] missing;
                fa.HasCharacters(text, out missing, false, true);
                return missing == null || missing.Length == 0;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 项目里可用的中文字体资产候选（按优先级）：
        /// ① UITheme 登记的项目字体资产；② 名字含 STSONG 的；③ 场景里已实际渲染在用的字体；
        /// ④ 其余（TMP 内置 LiberationSans 排最后）。
        /// </summary>
        public static List<TMP_FontAsset> TmpFontCandidates()
        {
            List<TMP_FontAsset> list = new List<TMP_FontAsset>();
            TMP_FontAsset main = LoadProjectFontAsset(UITheme.TmpFontPath);
            if (main != null)
                list.Add(main);

            List<TMP_FontAsset> others = new List<TMP_FontAsset>();
            foreach (TMP_FontAsset fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (fa == null || list.Contains(fa))
                    continue;
                if (fa.name.Contains("STSONG"))
                    list.Add(fa);
                else
                    others.Add(fa);
            }

            //场景中已在使用的字体资产（有 TMP 文本引用且非空）优先
            foreach (TextMeshProUGUI t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (t == null || t.font == null || list.Contains(t.font) || others.Contains(t.font))
                    continue;
                others.Insert(0, t.font);
            }

            others.Sort((a, b) =>
                (a.name.Contains("LiberationSans") ? 0 : 1) - (b.name.Contains("LiberationSans") ? 0 : 1));
            foreach (TMP_FontAsset fa in others)
            {
                if (!list.Contains(fa))
                    list.Add(fa);
            }
            return list;
        }

        // ==================== 内部 ====================

        /// <summary>加载项目字体资产（打包环境没有 AssetDatabase，退回场景引用/运行时动态字体）</summary>
        private static TMP_FontAsset LoadProjectFontAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
#else
            return null;
#endif
        }

        /// <summary>
        /// 用项目导入的中文字体现做一份动态 TMP 字体资产（图盘按需增长、不会缺字）。
        /// 编辑器下必须用导入的字体文件——OS 动态字体没有资产路径，TMP 动态补字会全部失败（整体变方块）；
        /// 打包环境退回 OS 字体。
        /// </summary>
        private static TMP_FontAsset CreateDynamicChineseFont()
        {
            if (m_dynamic != null)
                return m_dynamic;
            try
            {
                Font f = null;
#if UNITY_EDITOR
                string[] asset_candidates = {
                    UITheme.FontPath,
                    "Assets/TcgEngine/Fonts/MSYH.TTC",
                    "Assets/TcgEngine/Fonts/STSONG.TTF",
                    "Assets/TcgEngine/Fonts/SIMSUN.TTC",
                };
                for (int i = 0; i < asset_candidates.Length; i++)
                {
                    f = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(asset_candidates[i]);
                    if (f != null)
                        break;
                }
#endif
                if (f == null)
                    f = Font.CreateDynamicFontFromOSFont(new string[]
                        { "SimHei", "Microsoft YaHei", "微软雅黑", "SimSun", "宋体", "STSong", "华文宋体" }, 24);
                if (f == null)
                    return null;

                //不能用单参重载：其默认 90pt 采样 + 1024 图盘、一页只装得下几十个字，很快塞满出方块。
                //48pt 采样 + 2048 图盘 + 多页支持：一页可容纳两千余字，超出自动加页
                TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(f, 48, 9,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (fa == null)
                    return null;

                fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                m_dynamic = fa;
                Debug.Log("UIFonts：已用「" + f.name + "」现做动态中文字体（48pt/2048/多页）");
                return fa;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("UIFonts：动态中文字体创建失败，退回默认字体：" + e.Message);
                return null;
            }
        }
    }
}
