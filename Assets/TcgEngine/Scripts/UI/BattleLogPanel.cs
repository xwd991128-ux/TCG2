using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
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
        private TMP_Text title;
        private readonly List<GameObject> rows = new List<GameObject>();
        private int tab = 0;              //0=战斗记录 1=已使用卡牌 2=被破坏随从 3=对战信息
        private int last_count = -1;
        private int last_tab = -1;
        private float auto_scroll_timer;

        public static bool Enabled = true;   //设置里可关

        private void Awake()
        {
            _instance = this;
            canvas_go = EnsureCanvas();
            BuildToggle();
            BuildPanel();
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
        }

        public void Toggle()
        {
            if (!Enabled || root == null)
                return;
            bool open = !root.activeSelf;
            root.SetActive(open);
            if (toggle_btn != null)
                toggle_btn.SetActive(!open);
            last_count = -1;   //强制刷新
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

            //滚动区
            GameObject sv = new GameObject("Scroll", typeof(RectTransform));
            sv.transform.SetParent(root.transform, false);
            RectTransform svrt = sv.GetComponent<RectTransform>();
            svrt.anchorMin = new Vector2(0f, 0f);
            svrt.anchorMax = new Vector2(1f, 1f);
            svrt.pivot = new Vector2(0.5f, 0.5f);
            svrt.anchoredPosition = new Vector2(0f, -48f);
            svrt.sizeDelta = new Vector2(-16f, -108f);

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
                MakeText(go.transform, "T", "", 20f, UITheme.TextBody, TextAlignmentOptions.Left)
                    .rectTransform.sizeDelta = new Vector2(0f, 30f);
                rows.Add(go);
            }
            return rows[index];
        }

        private void SetRow(int index, string text, Color color, float height = 30f)
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
        }

        private void Refresh()
        {
            Game g = GameClient.Get().GetGameData();
            int my_id = GameClient.Get().GetPlayerID();
            if (g == null)
                return;

            title.text = tab == 0 ? "战斗记录" : tab == 1 ? "已使用的卡牌" : tab == 2 ? "被破坏的随从" : "对战信息";
            int used = 0;

            if (tab == 0)
            {
                List<BattleLogEntry> log = g.battle_log;
                int start = log != null ? Mathf.Max(0, log.Count - BattleLog.ShowRecent) : 0;
                for (int i = start; log != null && i < log.Count; i++)
                {
                    BattleLogEntry e = log[i];
                    Color c = UITheme.TextBody;
                    if (e.kind == (byte)BattleLogKind.Damage) c = new Color(1f, 0.45f, 0.4f);
                    else if (e.kind == (byte)BattleLogKind.Heal) c = new Color(0.5f, 1f, 0.6f);
                    else if (e.kind == (byte)BattleLogKind.TurnStart) c = UITheme.TextTitle;
                    else if (e.kind == (byte)BattleLogKind.Death) c = UITheme.TextDim;
                    bool turn_row = e.kind == (byte)BattleLogKind.TurnStart;
                    SetRow(used++, BattleLog.Format(e, g, my_id), c, turn_row ? 34f : 30f);
                }
                if (used == 0)
                    SetRow(used++, "（本局还没有记录）", UITheme.TextDim);
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
                            SetRow(used++, "  " + name + "  ×" + kv.Value, UITheme.TextBody);
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
                        SetRow(used++, "  " + side + "「" + name + "」（第 " + e.turn + " 回合）", UITheme.TextDim);
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
    }
}
