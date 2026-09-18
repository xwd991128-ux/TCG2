using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using TcgEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// Base class for UI panels that can be hidden or shown, with a fade-in fade-out effect
    /// </summary>

    [RequireComponent(typeof(CanvasGroup))]
    public class UIPanel : MonoBehaviour
    {
        public float display_speed = 4f;

        public UnityAction onShow;
        public UnityAction onHide;

        protected CanvasGroup canvas_group;
        protected bool visible;

        protected virtual void Awake()
        {
            canvas_group = GetComponent<CanvasGroup>();
            canvas_group.alpha = 0f;
            visible = false;
        }

        protected virtual void Start()
        {

        }

        protected virtual void Update()
        {

            float add = visible ? display_speed : -display_speed;
            float alpha = Mathf.Clamp01(canvas_group.alpha + add * Time.deltaTime);
            canvas_group.alpha = alpha;

            if (!visible && alpha < 0.01f)
                AfterHide();
        }

        public virtual void Toggle(bool instant = false)
        {
            if (IsVisible())
                Hide(instant);
            else
                Show(instant);
        }

        public virtual void Show(bool instant = false)
        {
            visible = true;
            gameObject.SetActive(true);

            //显示页恢复输入（与 Hide 对称）
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = true;
                canvas_group.interactable = true;
            }

            if (instant || display_speed < 0.01f)
                canvas_group.alpha = 1f;

            //场景 BGM：界面自动切歌（只有 BgmManager 映射表里的界面会触发；弹窗/选择器自动忽略）
            TcgEngine.Audio.BgmManager.NotifyPanelShown(GetType().Name);

            if (onShow != null)
                onShow.Invoke();

            //★ 出口按钮自愈：**每次 Show 同步校验一次**（幂等）。
            //  不能用协程延时：本项目有页面的 UIPanel 组件处于 disabled（见 MainMenu.ForceHideModalPanels 的注释），
            //  协程不会推进 → 自愈永远不执行（实测 FilterPanel 的 FilterCloseBtn 就是这样漏修的）。
            //  "等 Start() 注册监听"的问题改用**组件判定**规避：挂了 HomeReturnButton 的按钮视为已接好。
            RepairExitButtons();
        }

        public virtual void Hide(bool instant = false)
        {
            visible = false;
            if (instant || display_speed < 0.01f)
                canvas_group.alpha = 0f;

            //★ 关键：隐藏的页面必须同时停止接收射线。
            //只把 alpha 归零时，全屏页仍会以 blocksRaycasts=true 吃掉下面页面（规则编辑器等）的所有点击 ——
            //表现就是"界面看得见、所有按钮和输入框都点不动"（HomePanel 的注释也记过同类现象）。
            if (canvas_group != null)
            {
                canvas_group.blocksRaycasts = false;
                canvas_group.interactable = false;
            }

            if (onHide != null)
                onHide.Invoke();
        }

        public void SetVisible(bool visi)
        {
            if (!visible && visi)
                Show();
            else if (visible && !visi)
                Hide();
        }

        public virtual void AfterHide()
        {
            gameObject.SetActive(false);
            HandleBlankScreen();       //★ 我彻底隐藏后如果屏幕上已无任何页面 → 回首页（否则用户看到纯黑）
        }

        private bool _returning_home;

        /// <summary>【上一页】由打开方登记：本页隐藏后回到这里。
        /// 为什么需要：有些页是"**宿主先 Hide() 再打开它**"的（卡牌编辑器的变量配置 → 关键词/增益编辑器），
        /// 这类页裸 Hide() 之后身后空无一页 → 黑屏（用户实报"关键词编辑器退出黑屏"）。
        /// 只用于"返回上一层"；切组/切模块时不要设置（会和 TabButton 的互斥切换打架）。</summary>
        public UIPanel return_to;

        /// <summary>【黑屏兜底】本页若是"首页模块页"（由 TabButton 组互斥切进来的），
        /// 它的「返回」按钮往往是裸 `Hide()`：同组页面在切入时已被 `SetAll(group,false)` 全部隐藏，
        /// 于是隐藏本页后**屏幕上没有任何页面** —— 玩家看到的就是黑屏（点击无反应、只能强退）。
        /// 这里在"我彻底隐藏"之后判定一次，若确实空了就回首页（这才是点「返回」的期望）。
        /// 放在 `AfterHide` 而不是 `Hide` 的原因：调用方常有"Hide 本页 + 立刻 Show 下一页"的组合
        /// （如 卡池管理 → 编辑 → 卡牌编辑器），淡出要几帧，等淡完再看，下一页已经在了，不会误判。</summary>
        private void HandleBlankScreen()
        {
            if (_returning_home)
                return;
            if (TabButton.IsSwitchingGroups)
                return;                                     //正在切组：属于"显式导航"，不回溯上一页

            //★ 优先"返回上一页"：宿主（卡牌编辑器）在进入变量编辑器时已被 Hide()，
            //  这类页裸 Hide() 之后什么都没有 → 回到登记的上一页（而不是空白/黑屏）。
            //  这是打开方登记的**显式契约**，所以不再加"是否还有别的页面可见"的旁证判断
            //  （否则旁边残留一个页面就会把回溯吞掉 —— 实测 keyword 退出时被残留的规则编辑器挡住）。
            if (return_to != null && return_to != this && return_to.gameObject.scene.IsValid())
            {
                UIPanel target = return_to;
                return_to = null;                           //只用一次，避免来回弹
                Debug.Log("[导航] " + Name() + " 已隐藏 → 返回上一页 " + target.GetType().Name);
                target.Show();
                return;
            }

            if (!IsTabGroupPage())
                return;                                     //弹层/子页不管（它们的父页仍在）

            string why;
            if (TabButton.IsSwitchingGroups)
                why = "TabButton 正在切组（切换流程会自己显示新页）";
            else
            {
                HomePanel home = HomePanel.Get();
                List<string> group_vis = VisibleGroupPages();
                List<string> root_vis = VisibleRootPages();
                if (home == null)
                    why = "没有 HomePanel（非主菜单场景）";
                else if (home.gameObject == gameObject)
                    why = "本页就是首页";
                else if (home.IsVisible())
                    why = "首页已可见（其它可见：" + Join(root_vis) + "）";
                else if (group_vis.Count > 0)
                    why = "同组还有其它页面可见：" + Join(group_vis);
                else if (root_vis.Count > 0)
                    why = "画布上还有其它根级页面可见：" + Join(root_vis);
                else
                {
                    _returning_home = true;
                    Debug.Log("[导航] " + Name() + " 隐藏后屏幕上没有任何页面（模块页互斥隐藏的副作用）→ 自动回首页（原表现：黑屏）");
                    home.ReturnHome();
                    _returning_home = false;
                    return;
                }
            }

            //诊断：本页是"模块页"但没触发兜底 → 打印原因（弹层/子页不会走到这里，不会刷屏）
            Debug.Log("[导航-诊断] " + Name() + " 黑屏兜底未触发：" + why);
        }

        private string Name()
        {
            return GetType().Name + "@" + gameObject.name;
        }

        /// <summary>本页是否是某个 TabButton 的 ui_panel（= 主菜单模块页）。只缓存"是"的结果。
        /// 注意：用 `Resources.FindObjectsOfTypeAll<TabButton>()` 而不是 `TabButton.GetAll()`
        /// —— 后者只收录 Awake 跑过的（在**未激活**层级里的 TabButton 不会注册，比如被隐藏的旧顶部导航栏）。</summary>
        private int _is_group_page;
        private bool IsTabGroupPage()
        {
            if (_is_group_page == 1)
                return true;
            TabButton[] all = Resources.FindObjectsOfTypeAll<TabButton>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].ui_panel == this)
                {
                    _is_group_page = 1;
                    return true;
                }
            }
            return false;
        }

        private bool AnyGroupPageVisible()
        {
            return VisibleGroupPages().Count > 0;
        }

        private List<string> VisibleGroupPages()
        {
            List<string> found = new List<string>();
            TabButton[] all = Resources.FindObjectsOfTypeAll<TabButton>();
            for (int i = 0; i < all.Length; i++)
            {
                TabButton b = all[i];
                if (b == null || b.ui_panel == null || b.ui_panel == this || !b.ui_panel.IsVisible())
                    continue;
                //★ 只认**根级**页面：TabButton 也用在"页内子块"上（如 CardZoomPanel/Box/TradeArea/BuyArea 组=card_trade），
                //  父页隐藏时子块的 visible 仍为 true → 会误判成"屏幕上还有页面"从而吞掉黑屏兜底（实测踩过）。
                if (!IsRootLevelPanel(b.ui_panel))
                    continue;
                found.Add(b.ui_panel.GetType().Name + "@" + b.ui_panel.gameObject.name + "(组=" + b.group + ")");
            }
            return found;
        }

        /// <summary>除我之外，画布层级上是否还有可见的"页面"（刚被 Show 出来的卡牌编辑器/规则编辑器等）。
        /// 只认**根级**页面（父物体是 UICanvas/UICanvasTop 之类），避免把 FilterPanel/EditDeck 这类嵌套子块算进来。</summary>
        private bool AnyRootPageVisible()
        {
            return VisibleRootPages().Count > 0;
        }

        private List<string> VisibleRootPages()
        {
            List<string> found = new List<string>();
            UIPanel[] panels = Resources.FindObjectsOfTypeAll<UIPanel>();
            for (int i = 0; i < panels.Length; i++)
            {
                UIPanel p = panels[i];
                if (p == null || p == this)
                    continue;
                if (!p.gameObject.scene.IsValid())
                    continue;                                   //prefab 资产
                if (!p.IsVisible() || !p.gameObject.activeInHierarchy)
                    continue;
                if (p.GetType() == typeof(BlackPanel))
                    continue;                                   //黑幕不算页面
                if (IsRootLevelPanel(p))
                    found.Add(p.GetType().Name + "@" + p.gameObject.name);
            }
            return found;
        }

        private static string Join(List<string> list)
        {
            return list.Count == 0 ? "无" : string.Join("、", list.ToArray());
        }

        private static bool IsRootLevelPanel(UIPanel p)
        {
            Transform t = p.transform;
            if (t.parent == null)
                return true;
            string pn = t.parent.name;
            return pn == "UICanvas" || pn == "UICanvasTop" || pn.StartsWith("Canvas");
        }

        public bool IsVisible()
        {
            return visible;
        }

        public bool IsFullyVisible()
        {
            return visible && canvas_group.alpha > 0.99f;
        }

        public float GetAlpha()
        {
            return canvas_group.alpha;
        }

        // ==================== 退出出口（导航自愈） ====================
        // 设计前提：本基类是"全屏 + CanvasGroup.blocksRaycasts"的页面，**一旦某页没有任何调用 Hide() 的入口，
        // 玩家就只能强退游戏**（2026-09 审计：SoloPanel / AdventurePanel 完全没有出口，BuffPanel / 卖出重复卡 /
        // 加入码 / 金币对战 / 初始卡组 只有"确认"没有"取消"，一堆页面点进去出不来）。
        // 所以这里提供统一自愈：需要出口的页面在 Awake/Show 里调一次 EnsureExitButton() 即可。

        /// <summary>本页是否已有"看起来是出口"的按钮。只扫本页直属子树（跳过子 UIPanel 的子树，
        /// 否则会把弹层/子页面的 × 误判成自己的）。</summary>
        public bool HasExitButton()
        {
            Button[] btns = GetComponentsInChildren<Button>(true);
            //★ 两遍扫描：**先按名字**（Close/Back/Cancel/Return…），**再按文字**（×/返回/取消…）。
            //  原因：节点行/列表行上的"✕ 删除该行"按钮会被文字规则命中，
            //  若只扫一遍就会用它冒充页面出口（实测 GraphEditorPanel 的 BtnDel"✕" 顶掉了真正的 CloseBtn）。
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < btns.Length; i++)
                {
                    Button b = btns[i];
                    if (b == null)
                        continue;
                    if (FindOwningPanel(b) != this)
                        continue;              //属于子面板的按钮不算本页出口
                    bool by_name = LooksLikeExitByName(b);
                    if (pass == 0 && by_name)
                        return true;
                    if (pass == 1 && !by_name && LooksLikeExitByText(b))
                        return true;
                }
            }
            return false;
        }

        private static bool LooksLikeExitByName(Button b)
        {
            string n = b.gameObject.name.ToLowerInvariant();
            //★ 先排除"名字里恰好含 back/return 但语义无关"的按钮（都实测踩过）：
            //  · Cardback      —— "选择卡背"（含 back）
            //  · Lib_OnBeforeTurnStart / 节点库按钮 —— "befo**return**start" 含 return！
            //    （宽松匹配会把它当出口，轻则漏判"本页没有出口"，重则把 Hide() 挂到节点按钮上）
            if (n.Contains("cardback") || n.Contains("card_back"))
                return false;
            if (n.StartsWith("lib_") || n.Contains("node") || n.Contains("beforeturn"))
                return false;

            //只认明确的"出口词"，不再用裸 back/return 做包含匹配
            return n.Contains("close") || n.Contains("cancel")
                || n.Contains("backbtn") || n.Contains("btnback")
                || n.Contains("_back") || n.Contains("back_")
                || n.Contains("backzone") || n.Contains("backarea")
                || n.Contains("exitbtn") || n.Contains("returnbtn") || n.Contains("goback")
                || n.Contains("home");
        }

        private static bool LooksLikeExitByText(Button b)
        {
            TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
            if (t == null)
                return false;
            string s = (t.text ?? "").Trim();
            return s == "×" || s == "X" || s == "✕" || s == "关闭" || s == "返回" || s == "取消" || s == "返回首页";
        }

        /// <summary>向上找最近的 UIPanel 归属（**手写向上遍历**，不要用 GetComponentInParent：
        /// 它的默认重载**会跳过未激活对象**，而本页的关闭按钮常常是未激活的子物体 →
        /// 会误判成"没有出口"→ 重复补建一个 → 页面上出现两个 ×（skill 坑 12 的成因之一）。</summary>
        private static UIPanel FindOwningPanel(Component c)
        {
            Transform t = c != null ? c.transform : null;
            while (t != null)
            {
                UIPanel p = t.GetComponent<UIPanel>();
                if (p != null)
                    return p;
                t = t.parent;
            }
            return null;
        }

        private static bool LooksLikeExit(Button b)
        {
            return LooksLikeExitByName(b) || LooksLikeExitByText(b);
        }

        /// <summary>运行时确保本页有退出出口（**幂等**：已有出口按钮则原样返回 null，不重复补建 → 避免"两个 ×"）。
        /// label 默认「返回」；on_exit 为空时点击 = Hide()（回到下层页面）。
        /// 样式对齐项目规范：TMP + UIFonts 字体管线 + UITheme 配色（CtrlStrong 即"返回键等主要操作"的既定底色）。</summary>
        public Button EnsureExitButton(string label = "返回", UnityAction on_exit = null, bool top_left = true)
        {
            if (HasExitButton())
                return null;

            RectTransform parent = transform as RectTransform;
            if (parent == null)
                return null;

            GameObject go = new GameObject("AutoExitBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(top_left ? 0f : 1f, 1f);
            rt.anchorMax = rt.anchorMin;
            rt.pivot = new Vector2(top_left ? 0f : 1f, 1f);
            rt.anchoredPosition = new Vector2(top_left ? 24f : -24f, -20f);
            rt.sizeDelta = new Vector2(116f, 44f);

            Image img = go.GetComponent<Image>();
            img.color = UITheme.CtrlStrong;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = btn.colors;
            cb.normalColor = UITheme.BtnTintNormal;
            cb.highlightedColor = UITheme.BtnTintHighlight;
            cb.pressedColor = UITheme.BtnTintPressed;
            btn.colors = cb;

            GameObject tgo = new GameObject("Text", typeof(RectTransform));
            RectTransform trt = tgo.GetComponent<RectTransform>();
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            TextMeshProUGUI txt = tgo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = 20f;
            txt.color = UITheme.TextTitle;
            txt.raycastTarget = false;
            UIFonts.ApplyFont(txt);            //★ 必须走项目字体管线（否则字体不一致/发糊/缺字）

            UnityAction act = on_exit != null ? on_exit : (UnityAction)(() => Hide());
            btn.onClick.AddListener(act);
            go.transform.SetAsLastSibling();   //保证在页面内容之上，能点到

            Debug.Log("[导航] " + GetType().Name + " 场景未提供退出按钮 → 已在"
                + (top_left ? "左上角" : "右上角") + "补建「" + label + "」（原实现没有任何退出路径）");
            return btn;
        }

        /// <summary>出口按钮自愈（幂等）：修正"点了没反应"的两类场景绑定问题 ——
        ///  ① 出口按钮**完全没有监听**（死按钮）；
        ///  ② 出口按钮的 Inspector 监听指向**别的面板**（例如把 PackZoomPanel 的 CloseArea 绑到了别人的 Hide，
        ///     点击后关掉的是另一个页面、当前页纹丝不动 —— 实测确认过）。
        /// 只处理**本页直属子物体**上的出口按钮，避免误改列表行里的"✕ 删除本行"。
        /// 判定用的运行时委托读取见 GetRuntimeListeners（Unity 的运行时监听在 InvokableCallList.m_RuntimeCalls 里）。</summary>
        public int RepairExitButtons()
        {
            int repaired = 0;
            Button[] btns = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < btns.Length; i++)
            {
                Button b = btns[i];
                if (b == null)
                    continue;
                if (FindOwningPanel(b) != this)
                    continue;                       //属于子面板的按钮不算
                string lname = b.gameObject.name.ToLowerInvariant();
                if (lname.Contains("del"))
                    continue;                       //列表行/节点上的"✕ 删除"不是页面出口
                if (!LooksLikeExitByName(b))
                    continue;                       //只修**按名字**判定为出口的（够保守，覆盖 Close/CloseArea/BackBtn/CloseBtn/HomeReturnBtn/Cancel…）
                if (IsExitWiredToThis(b))
                    continue;

                b.onClick.AddListener(() => Hide());
                repaired++;
                Debug.Log("[导航] " + GetType().Name + " 的出口按钮「" + b.gameObject.name
                    + "」没有指向本页（场景漏绑或绑错对象）→ 已补上 Hide()（点击即可关闭本页）");
            }
            return repaired;
        }

        /// <summary>该出口按钮是否**确实会关闭本页**：Inspector 监听的目标是本页，或存在运行时监听。</summary>
        private bool IsExitWiredToThis(Button b)
        {
            //挂了 HomeReturnButton 的（"返回首页"按钮）：它的监听在 Start() 里注册，此刻还看不到，
            //但它一定会接好 —— 不能因此判定为死按钮去叠加 Hide()（那会变成"只隐藏、不回首页"→ 黑屏）。
            if (b.GetComponent<HomeReturnButton>() != null)
                return true;

            try
            {
                int pc = b.onClick.GetPersistentEventCount();
                for (int i = 0; i < pc; i++)
                {
                    Object target = b.onClick.GetPersistentTarget(i);
                    if (target == (Object)this)
                        return true;
                }
            }
            catch { }

            //代码里 AddListener 的（含 HomeReturnButton 在 Start() 注册的）：只要有运行时监听就认为已接好
            return GetRuntimeListeners(b.onClick).Count > 0;
        }

        /// <summary>读 UnityEvent 的**运行时**监听（AddListener 注册的）。
        /// 注意：Unity 把它们放在 `UnityEventBase.m_Calls`（InvokableCallList）的
        /// `m_RuntimeCalls` 列表里 —— 直接读 m_Calls 是空的（踩过）。</summary>
        internal static List<System.Delegate> GetRuntimeListeners(UnityEventBase evt)
        {
            List<System.Delegate> result = new List<System.Delegate>();
            if (evt == null)
                return result;
            try
            {
                FieldInfo fCalls = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
                object calls = fCalls != null ? fCalls.GetValue(evt) : null;
                if (calls == null)
                    return result;

                FieldInfo fRuntime = calls.GetType().GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic);
                System.Collections.IList list = fRuntime != null ? fRuntime.GetValue(calls) as System.Collections.IList : null;
                if (list == null)
                    return result;

                for (int i = 0; i < list.Count; i++)
                {
                    object call = list[i];
                    if (call == null)
                        continue;
                    FieldInfo fDel = call.GetType().GetField("Delegate", BindingFlags.Instance | BindingFlags.NonPublic);
                    System.Delegate d = fDel != null ? fDel.GetValue(call) as System.Delegate : null;
                    if (d != null)
                        result.Add(d);
                }
            }
            catch { }
            return result;
        }
    }
}