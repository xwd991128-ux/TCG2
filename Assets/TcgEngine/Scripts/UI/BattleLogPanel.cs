using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TcgEngine.Client;
using TcgEngine.Gameplay;

namespace TcgEngine.UI
{
    /// <summary>
    /// 对战记录面板（影之诗「对战记录」口径）：
    ///   入口 = 左侧一个纸图案按钮 → 打开**覆盖式面板**；
    ///   面板内 4 个分页（左右切换）：① 战斗记录 ② 已使用的卡牌 ③ 被破坏的随从 ④ 对战信息。
    /// 数据来自 Game.battle_log（随对局同步，见 BattleLog.cs）。
    /// ★运行时自建（不改场景）：字体一律走 UIFonts；只在战斗场景 Game 里创建。
    /// </summary>
    public class BattleLogPanel : MonoBehaviour
    {
        // ---------------- 创建（只在对战场景） ----------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            UnityEngine.SceneManagement.Scene sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (sc.name != "Game")
                return;   //只在战斗场景自建；菜单场景不建
            if (Get() != null)
                return;
            GameObject go = new GameObject("BattleLogPanel");
            go.AddComponent<BattleLogPanel>();
        }

        private static BattleLogPanel _instance;
        public static BattleLogPanel Get() { return _instance; }

        // ---------------- 状态 ----------------

        private GameObject canvas_go;     //自建画布（面板与入口按钮都挂在它下面）
        private GameObject root;          //覆盖面板根
        private GameObject toggle_btn;    //左侧入口按钮
        private RectTransform content;    //当前分页的内容容器
        private ScrollRect scroll;
        private RectTransform scroll_rt;  //滚动区（筛选条出现/隐藏时，上下边界要跟着让出 40px）
        private TMP_Text title;
        private readonly List<GameObject> rows = new List<GameObject>();
        private int tab = 0;              //0=战斗记录 1=已使用卡牌 2=被破坏随从 3=对战信息
        private int last_count = -1;
        private int last_tab = -1;
        private float auto_scroll_timer;

        // ---------------- 筛选（只作用于「战斗记录」页：噪音最多的那页） ----------------
        private int filter = 0;                                  //0=全部 1=只看我方 2=只看关键
        private GameObject filter_bar;
        private readonly Image[] filter_btns = new Image[3];
        private static readonly string[] FilterNames = { "全部", "只看我方", "只看关键" };

        // ---------------- 悬停看卡（浮在记录面板右侧，不挡任何点击） ----------------
        private GameObject art_root;
        private Image art_image;
        private TMP_Text art_name;
        private TMP_Text art_text;
        private BattleLogRow hover_row;      //当前指针所在行（由 BattleLogRow 回调设置）
        private string art_card_id;          //已显示的卡（脏检查：换卡才重设 sprite/文本）

        public static bool Enabled = true;   //设置里可关

        private void Awake()
        {
            _instance = this;
            canvas_go = EnsureCanvas();
            BuildToggle();
            BuildPanel();
            BuildArtPanel();
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                Toggle();

            if (root == null || !root.activeSelf)
                return;

            Game g = GameClient.Get() != null && GameClient.Get().IsReady() ? GameClient.Get().GetGameData() : null;
            int count = g != null && g.battle_log != null ? g.battle_log.Count : 0;

            //脏检查：只有"条数/分页"变化才重建（行对象复用，不每帧拼字符串）
            if (count != last_count || tab != last_tab)
            {
                last_count = count;
                last_tab = tab;
                Refresh();
                auto_scroll_timer = 0.1f;   //刚刷新 → 下帧滚到底
            }

            if (auto_scroll_timer > 0f)
            {
                auto_scroll_timer -= Time.unscaledDeltaTime;
                if (scroll != null && tab == 0)
                    scroll.verticalNormalizedPosition = 0f;   //滚到底部（最新）
            }

            UpdateHoverArt();
        }

        public void Toggle()
        {
            if (!Enabled || root == null)
                return;
            bool open = !root.activeSelf;
            root.SetActive(open);
            if (toggle_btn != null)
                toggle_btn.SetActive(!open);
            if (!open)
                ClearHoverArt();     //◀ 关面板时收起"悬停看卡"浮层（它挂在面板外，不关会留在屏幕上）
            last_count = -1;   //强制刷新
        }

        // ---------------- 悬停看卡（行回调 + 浮层刷新） ----------------

        /// <summary>行指针进入（由 BattleLogRow 调用）</summary>
        internal void OnRowEnter(BattleLogRow row)
        {
            hover_row = row;
            art_card_id = null;      //强制下一帧重设（换行换卡）
            UpdateHoverArt();
        }

        /// <summary>行指针离开（由 BattleLogRow 调用）</summary>
        internal void OnRowExit(BattleLogRow row)
        {
            if (hover_row == row)
                hover_row = null;
            UpdateHoverArt();
        }

        private void ClearHoverArt()
        {
            hover_row = null;
            art_card_id = null;
            if (art_root != null && art_root.activeSelf)
                art_root.SetActive(false);
        }

        /// <summary>把"当前悬停行"的卡显示到右侧浮层（全图 + 名称 + 该行文案 + 卡牌说明）。
        /// 为什么不复用战斗场景的 CardPreviewUI：那个弹层被"手牌/场上卡"的焦点链独占，
        /// 且位置不由本面板控制；记录面板自带一个浮层最省事，也保证不会被面板挡住。</summary>
        private void UpdateHoverArt()
        {
            if (art_root == null)
                return;
            if (root == null || !root.activeSelf || hover_row == null || !hover_row.gameObject.activeInHierarchy)
            {
                if (art_root.activeSelf)
                    art_root.SetActive(false);
                return;
            }

            string cid = hover_row.card_id;
            if (string.IsNullOrEmpty(cid))
            {
                if (art_root.activeSelf)
                    art_root.SetActive(false);
                return;      //该行没有卡可看（回合分隔行/统计行）
            }

            if (art_card_id != cid)
            {
                art_card_id = cid;
                CardData cd = CardData.Get(cid);
                Sprite art = cd != null ? cd.GetFullArt(null) : null;
                art_image.sprite = art;
                art_image.enabled = art != null;
                art_name.text = cd != null && !string.IsNullOrEmpty(cd.title) ? cd.title : cid;

                //正文 = 该行文案（行里可能被挤成一行）+ 卡牌说明/关键词（复用 CardPreviewUI 的取文口径）
                string body = hover_row.text ?? "";
                if (cd != null)
                {
                    string cdesc = cd.GetDesc();
                    string kdesc = cd.GetKeywordsDescText();
                    if (!string.IsNullOrWhiteSpace(cdesc))
                        body += "\n\n" + cdesc;
                    if (!string.IsNullOrWhiteSpace(kdesc))
                        body += "\n" + kdesc;
                }
                art_text.text = body;
            }

            if (!art_root.activeSelf)
                art_root.SetActive(true);
        }

        // ---------------- 构建 ----------------

        private static TMP_Text MakeText(Transform parent, string name, string txt, float size, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = txt;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            UIFonts.ApplyFont(t);          //★必须走项目字体管线
            return t;
        }


        private void BuildToggle()
        {
            //★入口：左侧中部一个"记录"按钮（影之诗是左侧纸图案打开记录界面）
            GameObject go = new GameObject("BattleLogToggle", typeof(RectTransform));
            go.transform.SetParent(canvas_go.transform, false);
            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.09f, 0.11f, 0.16f, 0.92f);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(Toggle);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(8f, 0f);
            rt.sizeDelta = new Vector2(64f, 84f);

            MakeText(go.transform, "Icon", "记\n录", 22f, UITheme.TextTitle, TextAlignmentOptions.Center)
                .rectTransform.sizeDelta = new Vector2(64f, 84f);
            toggle_btn = go;
        }

        private GameObject EnsureCanvas()
        {
            Canvas c = GetComponentInParent<Canvas>();
            if (c != null)
                return c.gameObject;
            GameObject go = new GameObject("BattleLogCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;              //盖在战斗 HUD 之上
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private void BuildPanel()
        {
            Transform parent = canvas_go != null ? canvas_go.transform : transform;

            root = new GameObject("BattleLogRoot", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(620f, 0f);

            Image bg = root.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.11f, 0.96f);

            //标题 + 关闭
            title = MakeText(root.transform, "Title", "对战记录", 26f, UITheme.TextTitle, TextAlignmentOptions.Left);
            RectTransform trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -8f);
            trt.sizeDelta = new Vector2(-120f, 40f);

            GameObject close = new GameObject("Close", typeof(RectTransform));
            close.transform.SetParent(root.transform, false);
            Image cb = close.AddComponent<Image>();
            cb.color = new Color(0.35f, 0.12f, 0.12f, 0.95f);
            Button cbtn = close.AddComponent<Button>();
            cbtn.targetGraphic = cb;
            cbtn.onClick.AddListener(Toggle);
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-8f, -8f);
            crt.sizeDelta = new Vector2(48f, 40f);
            MakeText(close.transform, "X", "×", 24f, UITheme.TextBody, TextAlignmentOptions.Center)
                .rectTransform.sizeDelta = new Vector2(48f, 40f);

            //导出按钮（排查用：把整份记录写到 persistentDataPath/Workshop/battle_log_*.txt）
            GameObject exp = new GameObject("Export", typeof(RectTransform));
            exp.transform.SetParent(root.transform, false);
            Image eb = exp.AddComponent<Image>();
            eb.color = new Color(0.12f, 0.22f, 0.16f, 0.95f);
            Button ebtn = exp.AddComponent<Button>();
            ebtn.targetGraphic = eb;
            ebtn.onClick.AddListener(() =>
            {
                Game gg = GameClient.Get() != null && GameClient.Get().IsReady() ? GameClient.Get().GetGameData() : null;
                BattleLog.ExportToFile(gg, GameClient.Get().GetPlayerID());
            });
            RectTransform ert = exp.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(1f, 1f);
            ert.anchorMax = new Vector2(1f, 1f);
            ert.pivot = new Vector2(1f, 1f);
            ert.anchoredPosition = new Vector2(-8f, -52f);
            ert.sizeDelta = new Vector2(104f, 36f);
            MakeText(exp.transform, "ET", "导出", 20f, UITheme.TextBody, TextAlignmentOptions.Center)
                .rectTransform.sizeDelta = new Vector2(104f, 36f);

            //分页按钮（左右切换，同影之诗）
            string[] tabs = { "战斗记录", "已使用卡牌", "被破坏随从", "对战信息" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                GameObject tb = new GameObject("Tab" + i, typeof(RectTransform));
                tb.transform.SetParent(root.transform, false);
                Image tib = tb.AddComponent<Image>();
                tib.color = new Color(0.14f, 0.16f, 0.22f, 0.95f);
                Button tbtn = tb.AddComponent<Button>();
                tbtn.targetGraphic = tib;
                tbtn.onClick.AddListener(() => { tab = idx; last_count = -1; });
                RectTransform tbrt = tb.GetComponent<RectTransform>();
                tbrt.anchorMin = new Vector2(0f, 1f);
                tbrt.anchorMax = new Vector2(0f, 1f);
                tbrt.pivot = new Vector2(0f, 1f);
                tbrt.anchoredPosition = new Vector2(8f + i * 150f, -52f);
                tbrt.sizeDelta = new Vector2(146f, 36f);
                MakeText(tb.transform, "T", tabs[i], 20f, UITheme.TextBody, TextAlignmentOptions.Center)
                    .rectTransform.sizeDelta = new Vector2(146f, 36f);
            }

            //筛选条（只用于「战斗记录」页；切到别的页会隐藏 —— 不做"看得见但没用"的控件）
            filter_bar = new GameObject("Filters", typeof(RectTransform));
            filter_bar.transform.SetParent(root.transform, false);
            RectTransform fbrt = filter_bar.GetComponent<RectTransform>();
            fbrt.anchorMin = new Vector2(0f, 1f);
            fbrt.anchorMax = new Vector2(1f, 1f);
            fbrt.pivot = new Vector2(0.5f, 1f);
            fbrt.anchoredPosition = new Vector2(0f, -92f);
            fbrt.sizeDelta = new Vector2(-16f, 32f);
            for (int i = 0; i < FilterNames.Length; i++)
            {
                int idx = i;
                GameObject fb2 = new GameObject("Filter" + i, typeof(RectTransform));
                fb2.transform.SetParent(filter_bar.transform, false);
                Image fib = fb2.AddComponent<Image>();
                fib.color = new Color(0.14f, 0.16f, 0.22f, 0.95f);
                Button fbtn = fb2.AddComponent<Button>();
                fbtn.targetGraphic = fib;
                fbtn.onClick.AddListener(() => { filter = idx; last_count = -1; });   //强制重建
                RectTransform frt = fb2.GetComponent<RectTransform>();
                frt.anchorMin = new Vector2(0f, 0.5f);
                frt.anchorMax = new Vector2(0f, 0.5f);
                frt.pivot = new Vector2(0f, 0.5f);
                frt.anchoredPosition = new Vector2(8f + i * 104f, 0f);
                frt.sizeDelta = new Vector2(100f, 32f);
                MakeText(fb2.transform, "FT", FilterNames[i], 19f, UITheme.TextBody, TextAlignmentOptions.Center)
                    .rectTransform.sizeDelta = new Vector2(100f, 32f);
                filter_btns[i] = fib;
            }

            //滚动区（顶部让出标题/分页/筛选条：48 + 36 + 40）
            GameObject sv = new GameObject("Scroll", typeof(RectTransform));
            sv.transform.SetParent(root.transform, false);
            RectTransform svrt = sv.GetComponent<RectTransform>();
            svrt.anchorMin = new Vector2(0f, 0f);
            svrt.anchorMax = new Vector2(1f, 1f);
            svrt.pivot = new Vector2(0.5f, 0.5f);
            svrt.anchoredPosition = new Vector2(0f, -70f);
            svrt.sizeDelta = new Vector2(-16f, -148f);
            scroll_rt = svrt;

            scroll = sv.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(sv.transform, false);
            RectTransform vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vrt;

            GameObject cont = new GameObject("Content", typeof(RectTransform));
            cont.transform.SetParent(viewport.transform, false);
            content = cont.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup vlg = cont.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;      //★项目经验：行高必须由 LayoutElement/内容驱动，否则一页只剩几条
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 2f;
            ContentSizeFitter fitter = cont.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
        }

        /// <summary>"悬停看卡"浮层：贴在记录面板右侧（面板 620 宽 → 浮层从 628 起）。
        /// ★所有图形 raycastTarget=false：它只是"看"的，绝不允许吞掉鼠标事件（否则会挡住战场操作）。
        /// ★挂在 canvas_go 而不是 root 下：关面板时由 Toggle()/ClearHoverArt() 显式收起。</summary>
        private void BuildArtPanel()
        {
            Transform parent = canvas_go != null ? canvas_go.transform : transform;

            art_root = new GameObject("BattleLogArt", typeof(RectTransform));
            art_root.transform.SetParent(parent, false);
            RectTransform rt = art_root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(628f, 0f);
            rt.sizeDelta = new Vector2(300f, 470f);

            Image bg = art_root.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.11f, 0.96f);
            bg.raycastTarget = false;

            GameObject img = new GameObject("Art", typeof(RectTransform));
            img.transform.SetParent(art_root.transform, false);
            art_image = img.AddComponent<Image>();
            art_image.raycastTarget = false;
            art_image.preserveAspect = true;
            RectTransform irt = img.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 1f);
            irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0f, -8f);
            irt.sizeDelta = new Vector2(-16f, 300f);

            art_name = MakeText(art_root.transform, "Name", "", 22f, UITheme.TextTitle, TextAlignmentOptions.Center);
            RectTransform nrt = art_name.rectTransform;
            nrt.anchorMin = new Vector2(0f, 1f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(0.5f, 1f);
            nrt.anchoredPosition = new Vector2(0f, -316f);
            nrt.sizeDelta = new Vector2(-16f, 32f);

            art_text = MakeText(art_root.transform, "Body", "", 17f, UITheme.TextBody, TextAlignmentOptions.TopLeft);
            RectTransform trt2 = art_text.rectTransform;
            trt2.anchorMin = new Vector2(0f, 0f);
            trt2.anchorMax = new Vector2(1f, 1f);
            trt2.pivot = new Vector2(0.5f, 1f);
            trt2.anchoredPosition = new Vector2(0f, -354f);
            trt2.sizeDelta = new Vector2(-20f, -362f);

            art_root.SetActive(false);
        }

        // ---------------- 内容刷新 ----------------

        private GameObject GetRow(int index)
        {
            while (rows.Count <= index)
            {
                GameObject go = new GameObject("Row" + rows.Count, typeof(RectTransform));
                go.transform.SetParent(content, false);
                LayoutElement le = go.AddComponent<LayoutElement>();
                le.minHeight = 30f;
                le.preferredHeight = 30f;
                //★透明高亮底：raycastTarget 打开才能收到指针事件（行才能"悬停看卡"）。
                //  颜色 alpha=0 也能命中（Image 默认不做 alpha 命中测试）；悬停时由 BattleLogRow 提亮。
                //  放在 ScrollRect 子节点上不会影响拖动：拖动事件会沿父级找到 ScrollRect 的 IDragHandler。
                Image hit = go.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
                hit.raycastTarget = true;
                BattleLogRow row = go.AddComponent<BattleLogRow>();
                row.panel = this;
                MakeText(go.transform, "T", "", 20f, UITheme.TextBody, TextAlignmentOptions.Left)
                    .rectTransform.sizeDelta = new Vector2(0f, 30f);
                rows.Add(go);
            }
            return rows[index];
        }

        private void SetRow(int index, string text, Color color, float height = 30f, string card_id = null)
        {
            GameObject go = GetRow(index);
            go.SetActive(true);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            TMP_Text t = go.GetComponentInChildren<TMP_Text>();
            t.rectTransform.sizeDelta = new Vector2(0f, height);
            if (t.text != text)
                t.text = text;
            if (t.color != color)
                t.color = color;

            BattleLogRow row = go.GetComponent<BattleLogRow>();
            row.card_id = card_id;
            row.text = text;
            if (hover_row == row)
                art_card_id = null;   //行内容变了（行对象复用）→ 让浮层下一帧重设，别显示上一行的卡
        }

        private void Refresh()
        {
            //★空值保护：Update 里本来就判了 GameClient.Get() != null，这里却直接解引用 —— 同一条路径两种写法。
            //  面板是在场景加载后自建的，若本帧 GameClient 还没 Awake（或对战结束被销毁），Refresh 会直接 NRE。
            GameClient client = GameClient.Get();
            Game g = client != null ? client.GetGameData() : null;
            int my_id = client != null ? client.GetPlayerID() : 0;
            if (g == null)
                return;

            title.text = tab == 0 ? "战斗记录" : tab == 1 ? "已使用的卡牌" : tab == 2 ? "被破坏的随从" : "对战信息";

            //筛选条只在「战斗记录」页出现；出现时滚动区让出 40px（否则会留一条空缝）
            if (filter_bar != null)
                filter_bar.SetActive(tab == 0);
            if (scroll_rt != null)
            {
                bool f = tab == 0;
                scroll_rt.anchoredPosition = new Vector2(0f, f ? -70f : -48f);
                scroll_rt.sizeDelta = new Vector2(-16f, f ? -148f : -108f);
            }
            for (int i = 0; i < filter_btns.Length; i++)
            {
                if (filter_btns[i] != null)
                    filter_btns[i].color = i == filter
                        ? new Color(0.22f, 0.30f, 0.42f, 0.98f)    //选中
                        : new Color(0.14f, 0.16f, 0.22f, 0.95f);
            }

            int used = 0;

            if (tab == 0)
            {
                List<BattleLogEntry> log = g.battle_log;
                //筛选：**先筛再截"最近 N 条"** —— 若先截再筛，"只看我方"时被对方刷屏会把我方记录整段挤掉
                List<BattleLogEntry> shown = new List<BattleLogEntry>();
                if (log != null)
                {
                    foreach (BattleLogEntry e in log)
                    {
                        if (PassFilter(e, filter, my_id))
                            shown.Add(e);
                    }
                }
                int start = Mathf.Max(0, shown.Count - BattleLog.ShowRecent);
                for (int i = start; i < shown.Count; i++)
                {
                    BattleLogEntry e = shown[i];
                    Color c = UITheme.TextBody;
                    if (e.kind == (byte)BattleLogKind.Damage) c = new Color(1f, 0.45f, 0.4f);
                    else if (e.kind == (byte)BattleLogKind.Heal) c = new Color(0.5f, 1f, 0.6f);
                    else if (e.kind == (byte)BattleLogKind.TurnStart) c = UITheme.TextTitle;
                    else if (e.kind == (byte)BattleLogKind.Death) c = UITheme.TextDim;
                    bool turn_row = e.kind == (byte)BattleLogKind.TurnStart;
                    SetRow(used++, BattleLog.Format(e, g, my_id), c, turn_row ? 34f : 30f, RowCardId(g, e));
                }
                if (used == 0)
                    SetRow(used++, filter == 0 ? "（本局还没有记录）" : "（当前筛选下没有记录）", UITheme.TextDim);
            }
            else if (tab == 1)
            {
                //已使用的卡牌：双方分别聚合（对齐影之诗「已使用的卡牌」页）
                for (int p = 0; p < g.players.Length; p++)
                {
                    Player pl = g.players[p];
                    Dictionary<string, int> count = new Dictionary<string, int>();
                    if (pl != null && g.battle_log != null)
                    {
                        foreach (BattleLogEntry e in g.battle_log)
                        {
                            if (e.actor != pl.player_id) continue;
                            if (e.kind != (byte)BattleLogKind.PlayCard && e.kind != (byte)BattleLogKind.Ability) continue;
                            if (string.IsNullOrEmpty(e.card_id)) continue;
                            count[e.card_id] = count.ContainsKey(e.card_id) ? count[e.card_id] + 1 : 1;
                        }
                    }
                    SetRow(used++, p == my_id ? "── 我方 ──" : "── 对方 ──", UITheme.TextTitle, 34f);
                    if (count.Count == 0)
                    {
                        SetRow(used++, "  （无）", UITheme.TextDim);
                    }
                    else
                    {
                        foreach (KeyValuePair<string, int> kv in count)
                        {
                            CardData cd = CardData.Get(kv.Key);
                            string name = cd != null && !string.IsNullOrEmpty(cd.title) ? cd.title : kv.Key;
                            SetRow(used++, "  " + name + "  ×" + kv.Value, UITheme.TextBody, 30f, kv.Key);
                        }
                    }
                }
            }
            else if (tab == 2)
            {
                //被破坏的随从
                if (g.battle_log != null)
                {
                    for (int i = g.battle_log.Count - 1; i >= 0; i--)   //最新在前
                    {
                        BattleLogEntry e = g.battle_log[i];
                        if (e.kind != (byte)BattleLogKind.Death) continue;
                        CardData cd = CardData.Get(e.card_id);
                        string name = cd != null && !string.IsNullOrEmpty(cd.title) ? cd.title : e.card_id;
                        string side = e.actor == my_id ? "我方" : "对方";
                        SetRow(used++, "  " + side + "「" + name + "」（第 " + e.turn + " 回合）", UITheme.TextDim, 30f, e.card_id);
                    }
                }
                if (used == 0)
                    SetRow(used++, "  （本局还没有随从被破坏）", UITheme.TextDim);
            }
            else
            {
                //对战信息：统计（对齐影之诗「对战信息」页）
                int plays = 0, attacks = 0, kills = 0, dmg = 0, heal = 0, draws = 0;
                if (g.battle_log != null)
                {
                    foreach (BattleLogEntry e in g.battle_log)
                    {
                        if (e.kind == (byte)BattleLogKind.PlayCard) plays++;
                        else if (e.kind == (byte)BattleLogKind.Attack) attacks++;
                        else if (e.kind == (byte)BattleLogKind.Death) kills++;
                        else if (e.kind == (byte)BattleLogKind.Damage) dmg += e.value;
                        else if (e.kind == (byte)BattleLogKind.Heal) heal += e.value;
                        else if (e.kind == (byte)BattleLogKind.Draw) draws++;
                    }
                }
                SetRow(used++, "  回合数：" + g.turn_count, UITheme.TextBody);
                SetRow(used++, "  出牌次数：" + plays, UITheme.TextBody);
                SetRow(used++, "  攻击次数：" + attacks, UITheme.TextBody);
                SetRow(used++, "  随从被破坏：" + kills, UITheme.TextBody);
                SetRow(used++, "  累计伤害：" + dmg, UITheme.TextBody);
                SetRow(used++, "  累计治疗：" + heal, UITheme.TextBody);
                SetRow(used++, "  抽牌次数：" + draws, UITheme.TextBody);
                SetRow(used++, "  记录条数：" + (g.battle_log != null ? g.battle_log.Count : 0)
                    + "（面板显示最近 " + BattleLog.ShowRecent + " 条）", UITheme.TextDim);
            }

            for (int i = used; i < rows.Count; i++)   //多余行隐藏（行对象复用，不销毁）
                rows[i].SetActive(false);
        }

        /// <summary>该条记录能"挂着看"的卡定义 id：主体卡定义 → 主体实例 → 客体实例，都取不到返回 null。
        /// （实例可能已离场，CardUidToId 内部对 null/取不到都安全返回 null）</summary>
        private static string RowCardId(Game g, BattleLogEntry e)
        {
            if (e == null)
                return null;
            if (!string.IsNullOrEmpty(e.card_id))
                return e.card_id;
            string id = BattleLog.CardUidToId(g, e.card_uid);
            if (string.IsNullOrEmpty(id))
                id = BattleLog.CardUidToId(g, e.target_uid);
            return id;
        }

        /// <summary>「战斗记录」页的筛选谓词（**唯一定义处**，Refresh 与探针都走它）。
        /// filter：0=全部 1=只看我方 2=只看关键。</summary>
        public static bool PassFilter(BattleLogEntry e, int filter, int my_id)
        {
            if (e == null)
                return false;
            if (filter == 1 && e.actor != my_id)
                return false;      //只看我方
            if (filter == 2 && !IsKeyEvent(e))
                return false;      //只看关键
            return true;
        }

        /// <summary>「只看关键」的口径：出牌/召唤/攻击/伤害/治疗/破坏/胜负；
        /// 滤掉回合分隔、抽牌、移动、增益/状态细节这些噪音（它们才是刷屏主因）。</summary>
        private static bool IsKeyEvent(BattleLogEntry e)
        {
            switch ((BattleLogKind)e.kind)
            {
                case BattleLogKind.PlayCard:
                case BattleLogKind.Summon:
                case BattleLogKind.Attack:
                case BattleLogKind.Damage:
                case BattleLogKind.Heal:
                case BattleLogKind.Death:
                case BattleLogKind.Win:
                case BattleLogKind.Lose:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>对战记录的一行：只负责"指针进入/离开"上报 + 极淡的悬停高亮。
    /// ★行对象由面板复用（不销毁，只 SetActive），所以 card_id/text 每次 SetRow 都会被改写。
    /// ★单独的顶层类（不是嵌套类）：Unity 更稳，且文件名不必与类名一致。</summary>
    public class BattleLogRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public BattleLogPanel panel;
        public string card_id;      //该行可"挂着看"的卡定义（无则 null）
        public string text;         //该行完整文案（浮层正文用；行内可能被挤成一行）

        private Image hit;

        private void Awake()
        {
            hit = GetComponent<Image>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (hit != null)
                hit.color = new Color(1f, 1f, 1f, 0.07f);   //有反馈，又不抢文本
            if (panel != null)
                panel.OnRowEnter(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (hit != null)
                hit.color = new Color(1f, 1f, 1f, 0f);
            if (panel != null)
                panel.OnRowExit(this);
        }

        private void OnDisable()
        {
            //行被隐藏（换页/刷新）时，若它正是悬停行 → 通知面板收起浮层，别把浮层留在屏幕上
            if (hit != null)
                hit.color = new Color(1f, 1f, 1f, 0f);
            if (panel != null)
                panel.OnRowExit(this);
        }
    }
}
