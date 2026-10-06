using System;
using System.Collections.Generic;
using System.Text;
using TcgEngine.Client;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 战斗内调试控制台（人机 / 卡牌编辑器模拟测试 / AI 自动对战）。
    ///
    /// 交互：战斗中按 **反引号 `** 从**屏幕底部**弹出抽屉（再按一次或 Esc 收起）。
    /// 输入指令回车执行；结果由服务端经 `GameClient.onServerMsg` 回传，在这里滚动显示并着色。
    /// ↑/↓ 翻历史，Tab 补全指令名/卡 id。
    ///
    /// 落点说明（为什么这样接）：
    /// - **不改场景/预制体**：用 RuntimeInitializeOnLoadMethod 在场景加载后自建一个常驻对象，
    ///   战斗就绪时才把面板挂到战斗画布（GameUI 下 sortingLayer="UI"、sortingOrder 最大的 Canvas）；
    /// - **不新建 Canvas**：新建 Canvas 默认 sortingLayer="Default" + Layer 0，会被排到 "UI" 层后面
    ///   或被相机剔除（项目里踩过的坑），所以只做普通子物体 + layer 对齐；
    /// - 与聊天框的 Return 冲突：ChatUI.Update 里加了 `if (DevConsoleUI.IsOpen) return;`。
    /// </summary>
    public class DevConsoleUI : MonoBehaviour
    {
        // ---- 对外状态（ChatUI 等要用来避让） ----
        public static bool IsOpen { get; private set; }
        public static DevConsoleUI Instance { get; private set; }

        private const int MaxLines = 240;                 //日志最多保留多少行
        private const float PanelHeightRatio = 0.34f;     //占屏幕高度比例
        private const float HeaderHeight = 24f;
        private const float InputHeight = 30f;

        // ---- 运行时 UI ----
        private GameObject root;
        private RectTransform panel;
        private TMP_Text log_text;
        private ScrollRect scroll;
        private RectTransform content;
        private TMP_InputField input;
        private Canvas host_canvas;
        private bool built;

        // ---- 状态 ----
        private readonly List<string> lines = new List<string>();
        private readonly List<string> history = new List<string>();
        private int history_index = -1;
        private bool echo_to_unity_log;
        private string last_log_text = "";

        // ================= 生命周期 =================

        /// <summary>确保存在控制台载体（幂等，可反复调；场景加载后 / 进战斗时都会调）。
        ///
        /// ★不要用静态 bool 当"只创建一次"的标记：项目若关了域重载（Enter Play Mode Options 里
        /// 不勾 Reload Domain），静态字段会**跨 Play 会话残留** → 第二次进 Play 时引导直接 return，
        /// 控制台再也不出现（用户实报"按 ` 没反应"的真凶）。
        /// 这里改成按"活着的实例 / 场景里已存在的对象"判断。</summary>
        public static void EnsureExists()
        {
            if (Instance != null)
                return;                                    //已有活着的实例（跨场景常驻）
            if (GameObject.Find("DevConsoleBootstrap") != null)
                return;                                    //对象已在场景里（Awake 马上会设置 Instance）
            GameObject go = new GameObject("DevConsoleBootstrap");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<DevConsoleUI>();
        }

        private void Awake()
        {
            Instance = this;
            IsOpen = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            UnhookClient();
        }

        private void OnEnable()
        {
            HookClient();
        }

        private void OnDisable()
        {
            UnhookClient();
        }

        private GameClient hooked_client;

        private void HookClient()
        {
            GameClient c = GameClient.Get();
            if (c != null && hooked_client != c)
            {
                UnhookClient();
                hooked_client = c;
                hooked_client.onServerMsg += OnServerMsg;
            }
        }

        private void UnhookClient()
        {
            if (hooked_client != null)
            {
                hooked_client.onServerMsg -= OnServerMsg;
                hooked_client = null;
            }
        }

        private void Update()
        {
            GameClient client = GameClient.Get();
            if (client == null || !client.IsReady() || client.GetGameData() == null || GameUI.Get() == null)
            {
                //不在战斗里：收起并允许下次进战斗重建
                if (IsOpen)
                    Close();
                if (root == null)
                    built = false;
                return;
            }
            HookClient();

            Canvas canvas = FindBattleCanvas();
            if (canvas == null)
                return;
            if (canvas != host_canvas)
            {
                //换了画布（重开一局/场景重建）：旧面板挂在旧画布上已被销毁，这里重建
                host_canvas = canvas;
                built = false;
                root = null;
                panel = null;
                input = null;
                log_text = null;
                scroll = null;
                content = null;
            }

            if (!built)
            {
                //自建 UI 万一抛异常，绝不允许"静默失效"（否则表现就是"按反引号没反应"，无从查起）
                try
                {
                    Build();
                }
                catch (Exception e)
                {
                    built = true;      //避免每帧重试刷屏
                    Debug.LogError("[控制台] 面板创建失败（本局不可用）：" + e);
                }
            }

            //反引号开关（回退旧输入系统，与 ChatUI 的 Return 一致）
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                if (IsOpen)
                    Close();
                else
                    Open();
                return;
            }

            if (!IsOpen)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            HandleConsoleKeys();
        }

        // ================= 开关 =================

        private void Open()
        {
            if (panel == null)
                return;
            IsOpen = true;
            panel.gameObject.SetActive(true);
            FocusInput();
        }

        private void Close()
        {
            IsOpen = false;
            if (panel != null)
                panel.gameObject.SetActive(false);
            if (EventSystem.current != null && input != null && EventSystem.current.currentSelectedGameObject == input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void FocusInput()
        {
            if (input == null)
                return;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
        }

        // ================= 构建 UI =================

        private Canvas FindBattleCanvas()
        {
            GameUI ui = GameUI.Get();
            if (ui == null)
                return null;
            Canvas best = null;
            Canvas[] all = ui.GetComponentsInChildren<Canvas>(false);   //只要激活中的（挂到隐藏 Canvas 下会看不见）
            for (int i = 0; i < all.Length; i++)
            {
                Canvas c = all[i];
                if (c == null)
                    continue;
                //取 sortingOrder 最大的那个（GameUI 下是 TopCanvas），控制台挂它下面才盖得住战斗 UI
                if (best == null || c.sortingOrder >= best.sortingOrder)
                    best = c;
            }
            if (best == null)
                best = ui.GetComponentInParent<Canvas>();
            return best;
        }

        private void Build()
        {
            if (host_canvas == null)
                return;
            built = true;

            RectTransform canvas_rt = host_canvas.GetComponent<RectTransform>();
            float canvas_h = canvas_rt != null && canvas_rt.rect.height > 1f ? canvas_rt.rect.height : 540f;

            root = new GameObject("DevConsole", typeof(RectTransform), typeof(Image));
            RectTransform root_rt = root.GetComponent<RectTransform>();
            root_rt.SetParent(host_canvas.transform, false);
            root_rt.anchorMin = new Vector2(0f, 0f);
            root_rt.anchorMax = new Vector2(1f, 0f);
            root_rt.pivot = new Vector2(0.5f, 0f);
            root_rt.anchoredPosition = Vector2.zero;
            root_rt.sizeDelta = new Vector2(0f, Mathf.Max(150f, canvas_h * PanelHeightRatio));
            root_rt.SetAsLastSibling();
            panel = root_rt;

            Image bg = root.GetComponent<Image>();
            bg.color = new Color(0.03f, 0.03f, 0.05f, 0.92f);
            bg.raycastTarget = true;      //挡住下层的战斗点击（只挡抽屉这一块）

            //标题
            TMP_Text header = MakeText(root_rt, "Header", "调试控制台 · 反引号收起 · help 看指令表 · ↑↓ 历史 · Tab 补全", 14,
                TextAlignmentOptions.Left, new Color(0.75f, 0.85f, 1f, 1f));
            RectTransform hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.anchoredPosition = new Vector2(0f, -2f);
            hrt.sizeDelta = new Vector2(-16f, HeaderHeight);

            //输入行（先建，底部对齐）
            RectTransform input_row = MakeRect(root_rt, "InputRow");
            input_row.anchorMin = new Vector2(0f, 0f);
            input_row.anchorMax = new Vector2(1f, 0f);
            input_row.pivot = new Vector2(0.5f, 0f);
            input_row.anchoredPosition = new Vector2(0f, 4f);
            input_row.sizeDelta = new Vector2(-16f, InputHeight);

            TMP_Text prompt = MakeText(input_row, "Prompt", ">", 16, TextAlignmentOptions.Left, new Color(0.6f, 1f, 0.7f, 1f));
            prompt.raycastTarget = false;
            RectTransform prt = prompt.rectTransform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(14f, 0f);

            RectTransform input_area = MakeRect(input_row, "InputArea");
            input_area.anchorMin = new Vector2(0f, 0f);
            input_area.anchorMax = new Vector2(1f, 1f);
            input_area.pivot = new Vector2(0.5f, 0.5f);
            input_area.offsetMin = new Vector2(18f, 0f);
            input_area.offsetMax = new Vector2(0f, 0f);
            input = MakeInput(input_area);

            //日志区（滚动）
            RectTransform log_area = MakeRect(root_rt, "LogArea");
            log_area.anchorMin = new Vector2(0f, 0f);
            log_area.anchorMax = new Vector2(1f, 1f);
            log_area.offsetMin = new Vector2(8f, InputHeight + 8f);
            log_area.offsetMax = new Vector2(-8f, -(HeaderHeight + 4f));

            scroll = log_area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            RectTransform viewport = MakeRect(log_area, "Viewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            Image vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1f, 1f, 1f, 0.02f);
            vimg.raycastTarget = true;                       //让滚轮能用
            viewport.gameObject.AddComponent<RectMask2D>();

            content = MakeRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            log_text = content.gameObject.AddComponent<TextMeshProUGUI>();
            log_text.text = "";
            log_text.fontSize = 15;
            log_text.alignment = TextAlignmentOptions.TopLeft;
            log_text.color = Color.white;
            log_text.raycastTarget = false;
            log_text.enableWordWrapping = true;
            log_text.overflowMode = TextOverflowModes.Overflow;
            log_text.richText = true;
            UIFonts.ApplyFont(log_text);

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            //layer 对齐画布（含子物体）：整套 UI 在 Layer 5(UI)，运行时新建默认 Layer 0
            int layer = host_canvas.gameObject.layer;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                all[i].gameObject.layer = layer;

            panel.gameObject.SetActive(false);
            if (lines.Count == 0)
                Append("INFO 战斗控制台就绪：输入 help 看指令表（人机/模拟对局可用，联机禁用）");
            else
                RefreshLog();
        }

        private static RectTransform MakeRect(RectTransform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, string text, int size,
            TextAlignmentOptions align, Color color)
        {
            RectTransform rt = MakeRect(parent, name);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            UIFonts.ApplyFont(t);
            return t;
        }

        /// <summary>单行输入框（与工作台 MakeCustomInput 同款建法：Text Area + TmpInputUtil 兜底）</summary>
        private TMP_InputField MakeInput(RectTransform parent)
        {
            GameObject go = new GameObject("Input", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image bg = go.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.10f);

            GameObject area_go = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            RectTransform area = area_go.GetComponent<RectTransform>();
            area.SetParent(rt, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(6f, 4f);
            area.offsetMax = new Vector2(-6f, -4f);

            TMP_Text text = MakeText(area, "Text", "", 16, TextAlignmentOptions.Left, Color.white);
            text.richText = false;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.caretColor = Color.white;
            field.selectionColor = new Color(0.4f, 0.7f, 1f, 0.4f);
            field.onSubmit.AddListener(OnSubmit);
            TmpInputUtil.Write(field, "");
            TmpInputUtil.Guard(field);
            return field;
        }

        // ================= 输入处理 =================

        private void HandleConsoleKeys()
        {
            if (input == null)
                return;

            //历史
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                StepHistory(1);
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                StepHistory(-1);
                return;
            }
            //补全
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                CompleteInput();
                return;
            }
            //点到别处失焦后，敲字自动回到输入框
            if (!input.isFocused && Input.inputString.Length > 0)
                FocusInput();
        }

        private void StepHistory(int dir)
        {
            if (history.Count == 0)
                return;
            if (history_index < 0 || history_index > history.Count)
                history_index = history.Count;
            history_index = Mathf.Clamp(history_index - dir, 0, history.Count);
            string value = history_index < history.Count ? history[history_index] : "";
            TmpInputUtil.Write(input, value);
            input.MoveTextEnd(false);
        }

        private void CompleteInput()
        {
            string cur = TmpInputUtil.Read(input);
            if (cur == null)
                cur = "";
            int space = cur.IndexOf(' ');
            string head = space < 0 ? cur : cur.Substring(0, space);

            //指令名补全
            if (space < 0)
            {
                string[] names = { "help", "state", "cards", "give", "giveall", "mana", "manamax", "manatotal",
                    "hp", "dmg", "heal", "atk", "hpcard", "armor", "draw", "discard", "shuffle", "spawn",
                    "kill", "ability", "trigger", "preview", "endturn", "endgame", "own", "log", "clear" };
                string hit = null;
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].StartsWith(head, StringComparison.OrdinalIgnoreCase))
                    {
                        if (hit != null)
                            return;            //多个候选不补，避免乱改
                        hit = names[i];
                    }
                }
                if (hit != null)
                {
                    TmpInputUtil.Write(input, hit + " ");
                    input.MoveTextEnd(false);
                }
                return;
            }

            //卡 id / 标题补全（give / spawn 的第二个参数）
            string cmd = head.ToLowerInvariant();
            if ((cmd == "give" || cmd == "spawn") && space >= 0)
            {
                string arg = cur.Substring(space + 1);
                if (arg.Contains(" "))
                    return;
                List<CardData> all = CardData.GetAll();
                string hit = null;
                for (int i = 0; i < all.Count; i++)
                {
                    CardData c = all[i];
                    if (c == null || string.IsNullOrEmpty(c.id))
                        continue;
                    bool match = c.id.StartsWith(arg, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(c.title) && c.title.StartsWith(arg, StringComparison.OrdinalIgnoreCase));
                    if (match)
                    {
                        if (hit != null)
                            return;
                        hit = c.id;
                    }
                }
                if (hit != null)
                {
                    TmpInputUtil.Write(input, head + " " + hit + " ");
                    input.MoveTextEnd(false);
                }
            }
        }

        private void OnSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                FocusInput();
                return;
            }
            string cmd = text.Trim();
            history.Add(cmd);
            history_index = history.Count;
            TmpInputUtil.Write(input, "");
            Append("> " + cmd);

            if (HandleLocalCommand(cmd))
            {
                FocusInput();
                return;
            }

            GameClient client = GameClient.Get();
            if (client != null)
                client.SendDevCommand(cmd);
            else
                Append("ERR 未连接对局");
            FocusInput();
        }

        /// <summary>本地指令（不需要服务端）：clear / log / own</summary>
        private bool HandleLocalCommand(string cmd)
        {
            string[] parts = cmd.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return true;
            string head = parts[0].ToLowerInvariant();

            if (head == "clear")
            {
                lines.Clear();
                RefreshLog();
                return true;
            }
            if (head == "log")
            {
                bool on = parts.Length > 1 && parts[1].ToLowerInvariant() == "on";
                echo_to_unity_log = on;
                Append("OK 控制台是否同时写入 Unity Console：" + (on ? "开" : "关"));
                return true;
            }
            if (head == "own")
            {
                if (parts.Length < 3 || parts[1].ToLowerInvariant() != "give")
                {
                    Append("ERR 用法：own give <卡id|标题> [数量=2]");
                    return true;
                }
                int count = 2;
                if (parts.Length > 3 && !int.TryParse(parts[3], out count))
                    count = 2;
                count = Mathf.Clamp(count, 1, 99);
                CardData card = ResolveCardLocal(parts[2]);
                if (card == null)
                {
                    Append("ERR 找不到卡「" + parts[2] + "」（用 cards 关键词 找 id）");
                    return true;
                }
                Authenticator auth = Authenticator.Get();
                UserData udata = auth != null ? auth.UserData : null;
                if (udata == null)
                {
                    Append("ERR 玩家数据未就绪");
                    return true;
                }
                VariantData variant = VariantData.GetDefault();
                string vid = variant != null ? variant.id : "";
                int before = udata.GetCardQuantity(card.id, vid);
                udata.AddCard(card.id, vid, count);
                Append("OK 收藏 " + (string.IsNullOrEmpty(card.title) ? card.id : card.title) + "："
                    + before + " → " + udata.GetCardQuantity(card.id, vid) + " 张（构筑界面可用）");
                SaveUserAsync(auth);
                return true;
            }
            return false;
        }

        private async void SaveUserAsync(Authenticator auth)
        {
            try
            {
                await auth.SaveUserData();
                Append("INFO 收藏已保存");
            }
            catch (Exception e)
            {
                Append("WARN 收藏保存失败：" + e.Message);
            }
        }

        private static CardData ResolveCardLocal(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            CardData exact = CardData.Get(token);
            if (exact != null)
                return exact;
            List<CardData> all = CardData.GetAll();
            CardData title_hit = null;
            int prefix = 0;
            for (int i = 0; i < all.Count; i++)
            {
                CardData c = all[i];
                if (c == null || string.IsNullOrEmpty(c.id))
                    continue;
                if (c.id.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    prefix++;
                if (title_hit == null && !string.IsNullOrEmpty(c.title)
                    && (c.title == token || c.title.Contains(token)))
                    title_hit = c;
            }
            if (prefix == 1)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    CardData c = all[i];
                    if (c != null && !string.IsNullOrEmpty(c.id)
                        && c.id.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
            return title_hit;
        }

        // ================= 日志 =================

        private void OnServerMsg(string msg)
        {
            if (!string.IsNullOrEmpty(msg))
                Append(msg);
        }

        private void Append(string msg)
        {
            if (string.IsNullOrEmpty(msg))
                return;
            string[] split = msg.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < split.Length; i++)
                lines.Add(split[i]);
            if (lines.Count > MaxLines)
                lines.RemoveRange(0, lines.Count - MaxLines);
            RefreshLog();

            if (echo_to_unity_log)
                Debug.Log("[控制台] " + msg);
        }

        private void RefreshLog()
        {
            if (log_text == null)
                return;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append('\n');
                sb.Append(Colorize(lines[i]));
            }
            string text = sb.ToString();
            if (text == last_log_text)
                return;
            last_log_text = text;
            log_text.text = text;

            //滚到最底
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                scroll.verticalNormalizedPosition = 0f;
            }
        }

        private static string Colorize(string line)
        {
            if (line == null)
                return "";
            if (line.StartsWith(DevCommandExecutor.PrefixErr, StringComparison.Ordinal))
                return "<color=#ff8a8a>" + Escape(line.Substring(DevCommandExecutor.PrefixErr.Length)) + "</color>";
            if (line.StartsWith(DevCommandExecutor.PrefixOk, StringComparison.Ordinal))
                return "<color=#8dff9e>" + Escape(line.Substring(DevCommandExecutor.PrefixOk.Length)) + "</color>";
            if (line.StartsWith(DevCommandExecutor.PrefixWarn, StringComparison.Ordinal))
                return "<color=#ffd479>" + Escape(line.Substring(DevCommandExecutor.PrefixWarn.Length)) + "</color>";
            if (line.StartsWith(DevCommandExecutor.PrefixInfo, StringComparison.Ordinal))
                return "<color=#c9d4e6>" + Escape(line.Substring(DevCommandExecutor.PrefixInfo.Length)) + "</color>";
            if (line.StartsWith(">", StringComparison.Ordinal))
                return "<color=#7fd3ff>" + Escape(line) + "</color>";
            return Escape(line);
        }

        /// <summary>卡牌标题/描述里可能带 &lt; &gt;（富文本标签），直接塞进 TMP 会被当标签解析 → 用 noparse 包住</summary>
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0)
                return s;
            return "<noparse>" + s + "</noparse>";
        }
    }

    /// <summary>控制台引导：不改场景/预制体，场景加载后自建一个常驻对象；战斗就绪时才显示。</summary>
    public static class DevConsoleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            DevConsoleUI.EnsureExists();
        }
    }
}
