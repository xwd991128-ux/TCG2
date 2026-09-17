using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;   //新增按钮栏用 TMP（旧版 uGUI Text 中文会发糊/缺字）
using TcgEngine.Client;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// Main UI script for all the game scene UI
    /// </summary>

    public class GameUI : MonoBehaviour
    {
        public Canvas game_canvas;
        public Canvas panel_canvas;
        public Canvas top_canvas;
        public UIPanel menu_panel;
        public Text quit_btn;

        [Header("Turn Area")]
        public Text turn_count;
        public Text turn_timer;
        public Button end_turn_button;
        public Animator timeout_animator;
        public AudioClip timeout_audio;

        [Header("Battle Buttons")]
        public RectTransform battle_btn_container;   // 自定义按钮容器（编辑器工具生成，屏幕右侧）
        public GameObject battle_btn_template;        // 按钮模板（隐藏，运行时实例化）

        private float selector_timer = 0f;
        private float end_turn_timer = 0f;
        private int prev_time_val = 0;

        private static GameUI instance;

        void Awake()
        {
            instance = this;

            if (game_canvas.worldCamera == null)
                game_canvas.worldCamera = Camera.main;
            if (panel_canvas.worldCamera == null)
                panel_canvas.worldCamera = Camera.main;
            if (top_canvas.worldCamera == null)
                top_canvas.worldCamera = Camera.main;
        }

        private void Start()
        {
            GameClient.Get().onGameStart += OnGameStart;
            GameClient.Get().onNewTurn += OnNewTurn;
            LoadPanel.Get().Show(true);
            BlackPanel.Get().Show(true);
            BlackPanel.Get().Hide();

            if (quit_btn != null)
                quit_btn.text = GameClient.game_settings.IsOnlinePlayer() ? "Resign" : "Quit";

            RefreshBattleButtons();
        }

        /// <summary>根据全局按钮配置（BattleButtonIO）动态生成战斗界面自定义按钮（模板实例化）。
        /// 每次进入战斗界面重新加载配置，确保按钮编辑器保存的修改立即生效。</summary>
        private void RefreshBattleButtons()
        {
            if (battle_btn_container == null || battle_btn_template == null)
                return;
            BattleButtonIO.LoadAll();
            //清空容器非模板子对象（模板本身保留）
            for (int i = battle_btn_container.childCount - 1; i >= 0; i--)
            {
                GameObject go = battle_btn_container.GetChild(i).gameObject;
                if (go != battle_btn_template)
                    Destroy(go);
            }
            List<BattleButtonData> list = BattleButtonIO.GetAll();
            for (int i = 0; i < list.Count; i++)
            {
                BattleButtonData b = list[i];
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                GameObject inst = Instantiate(battle_btn_template, battle_btn_container);
                inst.name = "BattleBtn_" + b.id;
                inst.SetActive(true);
                Text t = inst.GetComponentInChildren<Text>(true);
                if (t != null)
                    t.text = b.title;
                Button btn = inst.GetComponent<Button>();
                if (btn == null)
                    btn = inst.AddComponent<Button>();
                string bid = b.id;
                btn.onClick.AddListener(() => OnClickBattleButton(bid));
            }

            //「按钮」栏（左侧方块展开/收起 + 一排方形按钮）是本局按钮栏的唯一显示入口：
            //开局为空（Game.battle_buttons 为空列表），右侧旧的"按钮池全量列表"因此隐藏，避免与局内增删打架。
            if (battle_btn_container != null && battle_btn_container.gameObject.activeSelf)
                battle_btn_container.gameObject.SetActive(false);
            EnsureBattleBar();
        }

        /// <summary>点击自定义战斗按钮：走服务器（GameClient.SendBattleButton → 服务端 PressBattleButton：
        /// 校验 + 执行按钮图 + **当场结算并 RefreshAll**）。连接未就绪时打日志，避免"点了没反应还查不出原因"。</summary>
        private void OnClickBattleButton(string button_id)
        {
            GameClient client = GameClient.Get();
            if (client == null)
            {
                Debug.LogWarning("[按钮栏] 点击被忽略：GameClient 不存在");
                return;
            }
            if (!client.IsReady())
            {
                Debug.LogWarning("[按钮栏] 点击被忽略：连接未就绪（等对局开始后再点）");
                return;
            }
            client.SendBattleButton(button_id);   //★ 一发即走，服务端立即执行（见 GameLogic.PressBattleButton）
        }

        // ==================== 对战界面「按钮」栏：左侧方块展开/收起 + 向右一排方形按钮 ====================
        // 数据 = buttons.json（BattleButtonIO）；点方块 = 与右侧列表同一套触发（GameClient.SendBattleButton）。
        // 位置由 bar_offset 决定（anchor = 左侧中部）：默认略低于屏幕中线，落在上下两排卡牌之间的空档，
        // 避免遮住对面玩家的卡牌与卡上数值；收起时只留小方块且不画整条背景（见 ApplyBarExpanded）。

        [Header("按钮栏（可展开；留空则运行时自建）")]
        public RectTransform bar_root;          //整行（左侧方块 + 右侧按钮排）
        public RectTransform bar_content;       //方形按钮容器
        public Button bar_toggle;               //左侧方块（展开/收起开关）
        public Vector2 bar_offset = new Vector2(24f, -40f);  //相对屏幕左侧中部（0.5 高度）的偏移；负值=往下。运行时会再套一条"安全线"（见 ApplyBarExpanded），保证不遮对面卡牌
        public float bar_square_size = 60f;     //方形按钮边长
        private bool bar_expanded = false;      //默认收起（先进战斗只看到方块）
        private string bar_signature;           //本局按钮栏快照（用于检测「增加/删除按钮」生效后重建）
        private float bar_check_timer;          //快照检查节流计时（0.25s 一次，替代原「每帧 string.Join+ToArray」）
        private int last_turn_count = int.MinValue;    //回合数脏检查（原每帧 "Turn "+n 拼接）
        private int last_turn_seconds = int.MinValue;  //倒计时秒数脏检查（原每帧 Mathf.RoundToInt().ToString()）
        private bool bar_diag_pending;          //待做一次"谁吃掉了按钮栏射线"的诊断（悬浮/点击没反应的排查用）
        private bool bar_diag_done;
        private float connecting_stuck_timer;   //卡在 Connecting 的累计时间（超 8 秒打一次诊断）
        private bool connecting_diag_logged;

        /// <summary>构建「按钮」栏（幂等）：方块文字随状态变（按钮 →/←），默认收起。</summary>
        private void EnsureBattleBar()
        {
            if (bar_root != null && bar_content != null && bar_toggle != null)
            {
                ApplyBarExpanded();
                RefreshBattleBar();
                if (!bar_diag_done)
                    bar_diag_pending = true;      //复用的按钮栏：也补一次射线诊断
                return;
            }

            Transform parent = (battle_btn_container != null && battle_btn_container.parent != null)
                ? battle_btn_container.parent
                : (game_canvas != null ? game_canvas.transform : transform);

            GameObject row_go = new GameObject("BattleButtonBar", typeof(RectTransform), typeof(Image));
            bar_root = row_go.GetComponent<RectTransform>();
            bar_root.SetParent(parent, false);
            bar_root.anchorMin = new Vector2(0f, 0.5f);
            bar_root.anchorMax = new Vector2(0f, 0.5f);
            bar_root.pivot = new Vector2(0f, 0.5f);
            bar_root.anchoredPosition = bar_offset;
            bar_root.sizeDelta = new Vector2(820f, 84f);
            Image row_bg = row_go.GetComponent<Image>();
            row_bg.color = new Color(0f, 0f, 0f, 0.35f);
            row_bg.raycastTarget = false;

            ScrollRect sr = row_go.AddComponent<ScrollRect>();       //按钮多时可横向滚动，保证能容纳全部
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 26f;

            GameObject view_go = new GameObject("Viewport", typeof(RectTransform));
            RectTransform view = view_go.GetComponent<RectTransform>();
            view.SetParent(bar_root, false);
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(92f, 2f);                   //左端留给方块；竖向只留 2：整条 64 高正好容纳 60 的方块
            view.offsetMax = new Vector2(-6f, -2f);
            view_go.AddComponent<RectMask2D>();
            sr.viewport = view;

            GameObject content_go = new GameObject("Content", typeof(RectTransform));
            bar_content = content_go.GetComponent<RectTransform>();
            bar_content.SetParent(view, false);
            bar_content.anchorMin = new Vector2(0f, 0f);
            bar_content.anchorMax = new Vector2(0f, 1f);
            bar_content.pivot = new Vector2(0f, 0.5f);
            bar_content.anchoredPosition = Vector2.zero;
            bar_content.sizeDelta = Vector2.zero;
            HorizontalLayoutGroup hlg = content_go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(2, 2, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;      //★ 否则方块上的 LayoutElement 被忽略（正方形会跑掉）
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            ContentSizeFitter csf = content_go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = bar_content;

            //左侧方块：展开/收起
            GameObject tg_go = new GameObject("BarToggle", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform tg_rt = tg_go.GetComponent<RectTransform>();
            tg_rt.SetParent(bar_root, false);
            tg_rt.anchorMin = new Vector2(0f, 0.5f);
            tg_rt.anchorMax = new Vector2(0f, 0.5f);
            tg_rt.pivot = new Vector2(0f, 0.5f);
            tg_rt.anchoredPosition = new Vector2(6f, 0f);
            tg_rt.sizeDelta = new Vector2(76f, 62f);
            Image tg_img = tg_go.GetComponent<Image>();
            tg_img.color = new Color(0.35f, 0.35f, 0.40f, 0.95f);
            bar_toggle = tg_go.GetComponent<Button>();
            bar_toggle.targetGraphic = tg_img;
            bar_toggle.onClick.AddListener(ToggleBattleBar);

            TextMeshProUGUI tg_txt = MakeBarText(tg_go.transform, "按钮 →", 22);
            tg_txt.alignment = TMPro.TextAlignmentOptions.Center;

            ApplyBarExpanded();
            RefreshBattleBar();
            if (!bar_diag_done)
                bar_diag_pending = true;          //下一帧做一次射线诊断（首帧矩形未定，必须等一帧）
            Debug.Log("[战斗按钮栏] 构建完成：左侧方块(展开/收起) + 一排方形按钮（buttons.json）；"
                + "收起时不画整条背景、整行缩到方块大小；位置由 bar_offset=" + bar_offset + " 决定（默认落在上下两排卡牌之间的空档）");
        }

        /// <summary>诊断「按钮栏收不到鼠标」：在方块/按钮中心做 UI 射线采样，把最上层对象与它的层级打出来。
        /// 若最上层不是按钮栏自己（父链里也没有 Selectable/输入框/滚动区/拖拽层）→ 判定为"隐形遮挡物"，
        /// 关掉它的 raycastTarget 让按钮恢复可交互（与工作台那套排查思路一致）。只在构建后跑一次。</summary>
        private void DiagnoseBarBlockers()
        {
            bar_diag_done = true;
            if (EventSystem.current == null || bar_root == null)
                return;
            List<RectTransform> probes = new List<RectTransform>();
            if (bar_toggle != null && bar_toggle.transform is RectTransform trt)
                probes.Add(trt);
            if (bar_content != null)
            {
                for (int i = 0; i < bar_content.childCount; i++)
                {
                    if (bar_content.GetChild(i) is RectTransform crt)
                        probes.Add(crt);
                }
            }
            for (int i = 0; i < probes.Count; i++)
            {
                RectTransform probe = probes[i];
                if (probe == null || !probe.gameObject.activeInHierarchy)
                    continue;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, probe.TransformPoint(probe.rect.center));
                PointerEventData ped = new PointerEventData(EventSystem.current) { position = screen };
                List<RaycastResult> hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, hits);
                if (hits.Count == 0)
                {
                    Debug.LogWarning("[按钮栏] 射线诊断：" + probe.name + " 处没有命中任何 UI（可能被隐藏/在遮罩外）");
                    continue;
                }
                GameObject top = hits[0].gameObject;
                if (top == probe.gameObject || top.transform.IsChildOf(bar_root))
                    continue;   //正常：命中的就是按钮栏自己
                Debug.LogWarning("[按钮栏] 射线诊断：" + probe.name + " 被 " + PathOf(top)
                    + " 挡住了（最上层不是按钮栏）");
                if (!HasInteractive(top))
                {
                    Graphic g = top.GetComponent<Graphic>();
                    if (g != null)
                    {
                        g.raycastTarget = false;
                        Debug.LogWarning("[按钮栏] 已把隐形遮挡物 " + PathOf(top) + " 的 raycastTarget 关掉（它没有任何交互组件）");
                    }
                }
            }
        }

        /// <summary>该对象自身或父链上是否有交互组件（有则不能擅自关它的射线）</summary>
        private static bool HasInteractive(GameObject go)
        {
            for (Transform t = go != null ? go.transform : null; t != null; t = t.parent)
            {
                if (t.GetComponent<Selectable>() != null) return true;
                if (t.GetComponent<TMP_InputField>() != null) return true;
                if (t.GetComponent<ScrollRect>() != null) return true;
                if (t.GetComponent<EventTrigger>() != null) return true;
            }
            return false;
        }

        /// <summary>层级路径（排查日志用）</summary>
        private static string PathOf(GameObject go)
        {
            string path = go != null ? go.name : "(null)";
            Transform cur = go != null ? go.transform.parent : null;   //GameObject 没有 parent，父级在 transform 上
            int depth = 0;
            while (cur != null && depth < 6)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
                depth++;
            }
            return path;
        }

        private void ToggleBattleBar()
        {
            bar_expanded = !bar_expanded;
            ApplyBarExpanded();
        }

        /// <summary>箭头与可见性随状态变：收起=「按钮 →」（点了向右展开），展开=「按钮 ←」（点了收起）。
        /// ★收起时：① 内容隐藏；② **整条背景去掉**（半透明黑条会压住对面卡牌，用户要求收起不要它）；
        /// ③ 整行缩到只有方块大小 —— 否则一个看不见的大矩形继续占住卡牌区域（也会挡射线）。
        /// 位置统一由 `bar_offset` 决定（默认落在上下两排卡牌之间的空档），Inspector 里可随时改。</summary>
        private void ApplyBarExpanded()
        {
            if (bar_content != null)
                bar_content.gameObject.SetActive(bar_expanded);
            if (bar_toggle != null)
            {
                TextMeshProUGUI t = bar_toggle.GetComponentInChildren<TextMeshProUGUI>(true);
                if (t != null)
                    t.text = bar_expanded ? "按钮 ←" : "按钮 →";
            }
            if (bar_root != null)
            {
                float bar_h = bar_expanded ? 64f : 66f;      //展开 64 = 方块 60 + 内边距（收起只留方块）
                //★ 安全位置：屏幕中线两侧就是「对面卡牌行 / 我方卡牌行」，整条必须压到中线以下，
                //   否则一定遮住对面卡牌的数值（用户已反馈两次）。Inspector 里调得更低照样生效；
                //   调得更高会被这条安全线挡住（也能兜住场景里残留的旧 bar_offset）。
                float safe_y = -(bar_h * 0.5f + 4f);
                float y = Mathf.Min(bar_offset.y, safe_y);
                bar_root.anchoredPosition = new Vector2(bar_offset.x, y);
                bar_root.sizeDelta = new Vector2(bar_expanded ? 820f : 84f, bar_h);
                Image bg = bar_root.GetComponent<Image>();
                if (bg != null)
                    bg.enabled = bar_expanded;                                                //收起：整条透明背景彻底不画
            }
        }

        /// <summary>按 buttons.json 重建方形按钮（幂等；先脱层再 Destroy，避免延迟销毁造成的重影）</summary>
        private void RefreshBattleBar()
        {
            if (bar_content == null)
                return;
            for (int i = bar_content.childCount - 1; i >= 0; i--)
            {
                Transform c = bar_content.GetChild(i);
                if (c == null)
                    continue;
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
            //★ 数据源 = **本局按钮栏**（Game.battle_buttons）：开局为空，由「增加按钮/删除按钮」节点在局中增删，
            //   不写 buttons.json → 不影响其他对局。按钮的显示信息（名称/描述/背景）仍来自按钮池定义。
            Player me = GameClient.Get() != null ? GameClient.Get().GetPlayer() : null;   //GetPlayer 已做空值保护（返回 null = 未开局）
            List<string> ids = (me != null && me.battle_buttons != null)
                ? new List<string>(me.battle_buttons)
                : new List<string>();
            bar_signature = string.Join(",", ids);
            for (int i = 0; i < ids.Count; i++)
            {
                BattleButtonData b = BattleButtonIO.Get(ids[i]);
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                CreateBattleSquare(b);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(bar_content);
        }

        /// <summary>一个方形按钮：正方形；有背景图（按钮参数里选的）就贴图，没有则纯色 + 名称；点击走 SendBattleButton</summary>
        private void CreateBattleSquare(BattleButtonData b)
        {
            float size = Mathf.Max(32f, bar_square_size);
            GameObject go = new GameObject("Square_" + b.id, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(bar_content, false);
            rt.sizeDelta = new Vector2(size, size);          //正方形
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = size;
            le.preferredWidth = size;
            le.minHeight = size;
            le.preferredHeight = size;

            Image img = go.GetComponent<Image>();
            Sprite bg = CardPoolIO.LoadArt(b.background);    //与卡图同目录（Workshop/Art）
            if (bg != null)
            {
                img.sprite = bg;
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(0.35f, 0.35f, 0.40f, 0.95f);
            }

            TextMeshProUGUI txt = MakeBarText(rt, string.IsNullOrEmpty(b.title) ? b.id : b.title, 16);
            txt.alignment = TMPro.TextAlignmentOptions.Center;
            txt.enableWordWrapping = false;
            txt.overflowMode = TMPro.TextOverflowModes.Ellipsis;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            string bid = b.id;
            btn.onClick.AddListener(() => OnClickBattleButton(bid));   //★ 与右侧列表同一套触发

            //悬浮显示说明：鼠标一移上去就弹（不限时间）；文案 = 按钮描述（未填描述则退回显示按钮名，不会"什么都不弹"）
            EventTrigger et = go.GetComponent<EventTrigger>();
            if (et == null)
                et = go.AddComponent<EventTrigger>();
            et.triggers.Clear();
            string btitle = string.IsNullOrEmpty(b.title) ? b.id : b.title;
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowBarTip(rt, btitle, b.desc));
            et.triggers.Add(enter);
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideBarTip());
            et.triggers.Add(exit);
        }

        // ---- 悬浮提示：鼠标移到按钮方块上时弹出「按钮描述」 ----
        private RectTransform tip_root;
        private TextMeshProUGUI tip_text;

        private void EnsureBarTooltip()
        {
            if (tip_root != null)
                return;
            //提示挂到**画布根**（不是按钮栏的父级）：否则如果按钮栏的父级被裁剪/层级压住，提示会"弹了看不见"
            Transform parent = BarTipParent();
            GameObject go = new GameObject("BarTooltip", typeof(RectTransform), typeof(Image));
            tip_root = go.GetComponent<RectTransform>();
            tip_root.SetParent(parent, false);
            tip_root.pivot = new Vector2(0.5f, 0f);
            tip_root.sizeDelta = new Vector2(320f, 40f);
            Image bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);
            bg.raycastTarget = false;                     //提示不吃点击，避免挡住按钮
            tip_text = MakeBarText(tip_root, "", 18);
            tip_text.enableWordWrapping = true;
            tip_root.gameObject.SetActive(false);
        }

        /// <summary>提示的挂载父级：整屏画布根 > 按钮栏父级 > 本对象</summary>
        private Transform BarTipParent()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.rootCanvas != null)
                return canvas.rootCanvas.transform;
            return bar_root != null && bar_root.parent != null ? bar_root.parent : transform;
        }

        /// <summary>在目标控件上方弹出说明。
        /// ★位置用 RectTransformUtility 从"目标的锚点上方"换算到提示父级坐标（世界坐标直写 position 在
        /// 不同 Canvas 模式/父级下有偏差，会导致提示跑到屏幕外 → 看起来"没弹"）。
        /// 文案：有描述用描述，没描述退回按钮名；高度按行数算（原来固定 40 高，多行会被裁掉）。
        /// 命中时打一条日志，便于区分"事件没触发"和"触发了但没画出来"。</summary>
        private void ShowBarTip(RectTransform target, string title, string text)
        {
            string body = string.IsNullOrEmpty(text) ? title : text;
            if (string.IsNullOrEmpty(body))
                return;
            EnsureBarTooltip();
            if (tip_root == null || tip_text == null)
                return;
            tip_text.text = body;
            float w = Mathf.Clamp(160f + body.Length * 13f, 220f, 480f);
            int lines = Mathf.Max(1, Mathf.CeilToInt(body.Length * 13f / Mathf.Max(120f, w - 20f)));
            tip_root.sizeDelta = new Vector2(w, 20f + lines * 22f);
            tip_root.gameObject.SetActive(true);

            if (target != null && tip_root.parent is RectTransform host)
            {
                Canvas canvas = host.GetComponentInParent<Canvas>();
                Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera : null;
                Vector3 world = target.TransformPoint(new Vector3(0f, target.rect.height * 0.5f + 12f, 0f));
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(host, screen, cam, out Vector2 local))
                    tip_root.anchoredPosition = local;      //pivot(0.5,0) → 底部中心落在这里
            }
            tip_root.SetAsLastSibling();
            Debug.Log("[按钮栏] 悬浮说明：" + body);
        }

        private void HideBarTip()
        {
            if (tip_root != null)
                tip_root.gameObject.SetActive(false);
        }

        /// <summary>栏内文字：TMP + 全项目字体管线（旧版 Text 中文会发糊/缺字）</summary>
        private TextMeshProUGUI MakeBarText(Transform parent, string text, int size)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(3f, 3f);
            rt.offsetMax = new Vector2(-3f, -3f);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyFont(t);
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        void Update()
        {
            GameClient client = GameClient.Get();
            if (client == null)
                return;                     //还没进对局/客户端未就绪：本帧什么都不做（避免 Update 里 NRE 把后面逻辑全掐掉）

            //按钮栏射线自检（构建后一帧跑一次）：定位"悬浮/点击没反应"时谁挡在上面
            if (bar_diag_pending)
            {
                bar_diag_pending = false;
                DiagnoseBarBlockers();
            }

            Game data = client.GetGameData();
			bool is_connecting = data == null || data.state == GameState.Connecting;
            //★ 断线面板：条件必须是「不在连接中 且 **未就绪**」。
            //  （曾经漏掉 `!`：正常对局中（已就绪）也会显示 ConnectionPanel —— 它是全屏 UIPanel，
            //   blocksRaycasts=true 会把「开局替换」的 OK 等所有控件全吃掉，只剩它自己的 QUIT 能点。）
            bool connection_lost = !is_connecting && !client.IsReady();
            ConnectionPanel.Get().SetVisible(connection_lost);

            //加载遮罩（"Connecting to server..."）：只按「本局是否已开始」决定，并且**必须在下面 `!IsReady()` 提前 return 之前**执行——
            //否则客户端一旦未就绪，GameUI.Start() 里 Show(true) 过的遮罩就永远停在屏幕上挡住所有操作。
            bool game_started = data != null && data.HasStarted();
            LoadPanel.Get().SetVisible(!game_started);

            //Menu
            if (Input.GetKeyDown(KeyCode.Escape))
                menu_panel.Toggle();

            //本局按钮栏变化（「增加/删除按钮」节点生效）→ 重建按钮栏
            //  性能：原写法每帧 string.Join + ToArray（1 帧 2 次字符串/数组分配），而按钮增删是低频事件 →
            //  改为每 0.25 秒检查一次，且不再 ToArray 中转（HUD 观感无差别，每帧分配降到约 0）。
            bar_check_timer += Time.deltaTime;
            if (bar_check_timer >= 0.25f)
            {
                bar_check_timer = 0f;
                Player bar_player = client.GetPlayer();        //未开局时返回 null（GetPlayer 已做空值保护）
                string bar_now = (bar_player != null && bar_player.battle_buttons != null)
                    ? string.Join(",", bar_player.battle_buttons)
                    : "";
                if (bar_now != bar_signature)
                {
                    EnsureBattleBar();
                    RefreshBattleBar();
                }
            }

            //卡在开局诊断：在 Connecting 停留超过 8 秒 → 一次性打出"到底缺什么"（避免只看到 Connecting 干等）
            if (!game_started && data != null && data.state == GameState.Connecting)
            {
                connecting_stuck_timer += Time.deltaTime;
                if (!connecting_diag_logged && connecting_stuck_timer > 8f)
                {
                    connecting_diag_logged = true;
                    string info = "";
                    if (data.players != null)
                    {
                        foreach (Player p in data.players)
                        {
                            if (p == null)
                                continue;
                            info += "  p" + p.player_id + "(ai=" + p.is_ai + ", ready=" + p.ready
                                + ", connected=" + p.IsConnected() + ", 卡组=" + (p.cards_deck != null ? p.cards_deck.Count : 0) + ")";
                        }
                    }
                    Debug.LogError("[对战] 已在 Connecting 停留 8 秒：IsReady=" + client.IsReady() + "，玩家：" + info
                        + " → 常见原因：服务端开局建卡抛异常（看 Console 更早的报错）、对手(AI)未 ready、卡组为空");
                }
            }
            else
            {
                connecting_stuck_timer = 0f;
            }

            if (!client.IsReady())
                return;

            bool yourturn = client.IsYourTurn();
            end_turn_button.interactable = yourturn && end_turn_timer > 1f;
            end_turn_timer += Time.deltaTime;
            selector_timer += Time.deltaTime;

            //Timer（★ 脏检查：原来每帧字符串拼接 + TMP 赋值）
            if (data.turn_count != last_turn_count)
            {
                last_turn_count = data.turn_count;
                turn_count.text = "Turn " + last_turn_count.ToString();
            }
            int turn_sec = Mathf.RoundToInt(data.turn_timer);
            if (turn_sec != last_turn_seconds)
            {
                last_turn_seconds = turn_sec;
                turn_timer.text = turn_sec.ToString();
            }
            turn_timer.enabled = data.turn_timer > 0f;
            turn_timer.enabled = data.turn_timer < 999f;

            //Simulate timer
            if (data.state == GameState.Play && data.turn_timer > 0f)
                data.turn_timer -= Time.deltaTime;

            //Timer warning
            if (data.state == GameState.Play)
            {
                int val = Mathf.RoundToInt(data.turn_timer);
                int tick_val = 10;
                if (val < prev_time_val && val <= tick_val)
                    PulseFX();
                prev_time_val = val;
            }

            //Show selector panels
            foreach (SelectorPanel panel in SelectorPanel.GetAll())
            {
                bool should_show = panel.ShouldShow();
                if (should_show != panel.IsVisible() && selector_timer > 1f)
                {
                    selector_timer = 0f;
                    panel.SetVisible(should_show);

                    if (should_show)
                    {
                        AbilityData ability = AbilityData.Get(data.selector_ability_id);
                        Card caster = data.GetCard(data.selector_caster_uid);
                        panel.Show(ability, caster);
                    }
                }
            }

            //Hide
            if (!yourturn && data.phase != GamePhase.Mulligan)
            {
                SelectorPanel.HideAll();
            }

        }

        private void PulseFX()
        {
            timeout_animator?.SetTrigger("pulse");
            AudioTool.Get().PlaySFX("time", timeout_audio, 1f);
        }

        private void OnGameStart()
        {
            
        }

        private void OnNewTurn(int player_id)
        {
            CardSelector.Get().Hide();
            SelectTargetUI.Get().Hide();
        }

        public void OnClickNextTurn()
        {
            if (!Tutorial.Get().CanDo(TutoEndTrigger.EndTurn))
                return;

            GameClient.Get().EndTurn();
            end_turn_timer = 0f; //Disable button immediately (dont wait for refresh)
        }

        public void OnClickRestart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void OnClickMenu()
        {
            menu_panel.Show();
        }

        public void OnClickBack()
        {
            menu_panel.Hide();
        }

        public void OnClickQuit()
        {
            bool online = GameClient.game_settings.IsOnlinePlayer();
            bool ended = GameClient.Get().HasEnded();
            if (online && !ended)
                GameClient.Get().Resign();
            else
                StartCoroutine(QuitRoutine("Menu"));
            menu_panel.Hide();
        }

        private IEnumerator QuitRoutine(string scene)
        {
            BlackPanel.Get().Show();
            AudioTool.Get().FadeOutMusic("music");
            AudioTool.Get().FadeOutSFX("ambience");
            AudioTool.Get().FadeOutSFX("ending_sfx");
            TcgEngine.Audio.BgmManager.Stop(0.8f);   //退出对战：BGM 淡出（下个场景的界面会按配置接上）

            yield return new WaitForSeconds(1f);

            GameClient.Get().Disconnect();
            SceneNav.GoTo(scene);
        }

        public void OnClickSwapObserve()
        {
            int other = GameClient.Get().GetPlayerID() == 0 ? 1 : 0;
            GameClient.Get().SetObserverMode(other);
        }

        public static bool IsUIOpened()
        {
            return CardSelector.Get().IsVisible() || EndGamePanel.Get().IsVisible();
        }

        public static bool IsOverUI()
        {
            //return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
            eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
            return results.Count > 0;
        }

        public static bool IsOverUILayer(string sorting_layer)
        {
            return IsOverUILayer(SortingLayer.NameToID(sorting_layer));
        }

        public static bool IsOverUILayer(int sorting_layer)
        {
            //return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
            eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
            int count = 0;
            foreach (RaycastResult result in results)
            {
                if (result.sortingLayer == sorting_layer)
                    count++;
            }
            return count > 0;
        }

        public static bool IsOverRectTransform(Canvas canvas, RectTransform rect)
        {
            PointerEventData pevent = new PointerEventData(EventSystem.current);
            pevent.position = Input.mousePosition;

            List<RaycastResult> results = new List<RaycastResult>();
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            raycaster.Raycast(pevent, results);

            foreach (RaycastResult result in results)
            {
                if (result.gameObject.transform == rect || result.gameObject.transform.IsChildOf(rect))
                    return true;
            }
            return false;
        }

        public static Vector2 MouseToRectPos(Canvas canvas, RectTransform rect, Vector2 screen_pos)
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
            {
                Vector2 anchor_pos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen_pos, canvas.worldCamera, out anchor_pos);
                return anchor_pos;
            }
            else
            {
                Vector2 anchor_pos = screen_pos - new Vector2(rect.position.x, rect.position.y);
                anchor_pos = new Vector2(anchor_pos.x / rect.lossyScale.x, anchor_pos.y / rect.lossyScale.y);
                return anchor_pos;
            }
        }

        public static Vector3 MouseToWorld(Vector2 mouse_pos, float distance = 10f)
        {
            Camera cam = GameCamera.Get() != null ? GameCamera.GetCamera() : Camera.main;
            Vector3 wpos = cam.ScreenToWorldPoint(new Vector3(mouse_pos.x, mouse_pos.y, distance));
            return wpos;
        }

        public static string FormatNumber(int value)
        {
            return string.Format("{0:#,0}", value);
        }

        public static GameUI Get()
        {
            return instance;
        }
    }
}
