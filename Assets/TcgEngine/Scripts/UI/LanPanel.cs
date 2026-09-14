using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;   //GameClientLAN / GameClient

namespace TcgEngine.UI
{
    /// <summary>
    /// 局域网对战大厅（房主直连）：两个标签「开房」/「加入」。
    ///
    /// 为什么运行时自建 UI：与 MusicLibraryPanel / BgmConfigPanel / VFXEditorPopup 同一套做法，
    /// 复用 UIPanel 弹层范式 + UITheme/UIFactory/UIFonts 令牌，**不需要在场景里拖绑任何引用**
    /// （主菜单入口按钮也是运行时注入，见 EnsureMenuEntry；也可以在场景里手放按钮绑 MainMenu.OnClickLan()）。
    ///
    /// 流程全部委托给 GameClientLAN（内部调 TcgNetwork.StartHost / StartClient），本类只管界面与输入校验。
    /// </summary>
    public class LanPanel : UIPanel
    {
        private const float PANEL_W = 760f;
        private const float PANEL_H = 520f;
        public const string MENU_BUTTON_NAME = "LanBtn";

        private RectTransform panel_rect;
        private RectTransform page_host;
        private RectTransform page_join;
        private Button tab_host;
        private Button tab_join;
        private TMP_Text txt_ips;
        private TMP_Text txt_port;
        private TMP_Text txt_port_join;
        private TMP_Text txt_deck;
        private TMP_Text txt_identity;
        private TMP_Text txt_status;
        private TMP_InputField input_host_ip;
        private TMP_Text tab_host_label;
        private TMP_Text tab_join_label;

        private bool built;
        private bool host_page = true;

        private static LanPanel instance;

        // ---------------- 打开 ----------------

        public static LanPanel Get()
        {
            if (instance == null)
                instance = FindObjectOfType<LanPanel>(true);
            return instance;
        }

        public static LanPanel Create(Transform context)
        {
            if (instance != null)
                return instance;
            GameObject go = new GameObject("LanPanel", typeof(RectTransform), typeof(CanvasGroup));
            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            go.transform.SetParent(canvas != null ? canvas.transform : context, false);
            UIFactory.SetStretch(go.GetComponent<RectTransform>());
            go.transform.SetAsLastSibling();
            instance = go.AddComponent<LanPanel>();
            instance.EnsureBuilt();
            instance.Hide(true);
            return instance;
        }

        public static void Open(Transform host)
        {
            LanPanel panel = Get();
            if (panel == null)
                panel = Create(host != null ? host : FindMenuCanvas());
            panel.Show();
        }

        /// <summary>主菜单入口按钮：运行时注入（幂等），不依赖重跑场景生成工具</summary>
        public static void EnsureMenuEntry()
        {
            if (GameObject.Find(MENU_BUTTON_NAME) != null)
                return;

            Transform host = FindMenuCanvas();
            if (host == null)
            {
                Debug.LogWarning("[局域网] 未找到主菜单首页（HomePanel/Canvas），跳过入口按钮创建；" +
                    "可在场景里手工加按钮并绑定 MainMenu.OnClickLan()");
                return;
            }

            GameObject go = new GameObject(MENU_BUTTON_NAME, typeof(RectTransform));
            go.transform.SetParent(host, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(150f, 38f);
            //放在右上角按钮簇的「第二行」（音乐库/界面BGM配置在下 y=-18，本按钮 y=-64），避免与既有按钮横向抢占位置
            rt.anchoredPosition = new Vector2(-24f, -64f);

            Image img = go.AddComponent<Image>();
            img.color = new Color(0.5f, 0.85f, 0.75f, 0.34f);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);
            btn.onClick.AddListener(() => Open(host));

            TMP_Text t = MakeLabel("Text", go.transform, "局域网对战", UITheme.FontBody, Color.white, TextAlignmentOptions.Center);
            UIFactory.SetStretch(t.rectTransform);
        }

        private static Transform FindMenuCanvas()
        {
            HomePanel home = Object.FindObjectOfType<HomePanel>(true);
            if (home != null)
                return home.transform;
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            return canvas != null ? canvas.transform : null;
        }

        // ---------------- 显示/隐藏 ----------------

        public override void Show(bool instant = false)
        {
            EnsureBuilt();
            base.Show(instant);
            SetHostPage(host_page);
            RefreshInfo();
            SetStatus("");
        }

        // ---------------- 刷新 ----------------

        private void RefreshInfo()
        {
            if (txt_port != null)
                txt_port.text = GameClientLAN.GetPort().ToString();
            if (txt_port_join != null)
                txt_port_join.text = GameClientLAN.GetPort().ToString();

            if (txt_ips != null)
            {
                List<string> ips = GameClientLAN.GetLocalIPv4();
                string s = "";
                for (int i = 0; i < ips.Count && i < 4; i++)
                    s += (i > 0 ? "\n" : "") + ips[i];
                if (ips.Count > 4)
                    s += "\n…（共 " + ips.Count + " 个，试不通就换一个；同机双开用 127.0.0.1）";
                txt_ips.text = s;
            }

            if (txt_deck != null)
            {
                UserDeckData deck = GameClient.player_settings != null ? GameClient.player_settings.deck : null;
                string tid = deck != null ? deck.tid : "";
                DeckData d = string.IsNullOrEmpty(tid) ? null : DeckData.Get(tid);
                string name = d != null && !string.IsNullOrEmpty(d.title) ? d.title : tid;
                txt_deck.text = string.IsNullOrEmpty(name) ? "（未选择：启动时会回退测试卡组）" : name;
            }

            if (txt_identity != null)
                txt_identity.text = GameClientLAN.GetIdentityName();
        }

        private void SetStatus(string msg)
        {
            if (txt_status != null)
                txt_status.text = msg;
        }

        private void SetHostPage(bool is_host)
        {
            host_page = is_host;
            if (page_host != null)
                page_host.gameObject.SetActive(is_host);
            if (page_join != null)
                page_join.gameObject.SetActive(!is_host);
            if (tab_host_label != null)
                tab_host_label.color = is_host ? Color.white : UITheme.TextDim;
            if (tab_join_label != null)
                tab_join_label.color = is_host ? UITheme.TextDim : Color.white;
        }

        // ---------------- 操作 ----------------

        private void OnClickHost()
        {
            string error;
            if (!GameClientLAN.StartHost(out error))
            {
                SetStatus(error);
                return;
            }
            SetStatus("已开房，正在进入对局…（请把上面的 IP 告诉对方）");
            Hide();
        }

        private void OnClickJoin()
        {
            string ip = input_host_ip != null ? input_host_ip.text : "";
            string error;
            if (!GameClientLAN.StartJoin(ip, out error))
            {
                SetStatus(error);
                return;
            }
            SetStatus("正在连接房主 " + ip.Trim() + " …（连接不上会提示并退回大厅）");
            Hide();
        }

        private void OnClickCopyIp()
        {
            List<string> ips = GameClientLAN.GetLocalIPv4();
            if (ips.Count == 0)
                return;
            GUIUtility.systemCopyBuffer = ips[0];
            SetStatus("已复制本机 IP：" + ips[0] + "（发给对方，让他填在「加入」页）");
        }

        /// <summary>同机双开：换一个本地点名身份，避免两个实例 user_id 相同（都被认成玩家0，永远不开局）</summary>
        private void OnClickSwitchIdentity()
        {
            string error;
            if (!GameClientLAN.SwitchTestIdentity(out error))
            {
                SetStatus(error);
                return;
            }
            RefreshInfo();
            SetStatus("本机身份已切到「" + GameClientLAN.GetIdentityName()
                + "」——同机双开时请让两个窗口的身份不同（两边各点一次即可），再开房/加入。");
        }

        // ---------------- UI 构建 ----------------

        private void EnsureBuilt()
        {
            if (built)
                return;
            built = true;

            //遮罩（点空白关闭）
            Image mask = UIFactory.CreateImage("Mask", transform, UITheme.MaskPopup);
            UIFactory.SetStretch(mask.rectTransform);
            mask.raycastTarget = true;
            Button mask_btn = mask.gameObject.AddComponent<Button>();
            mask_btn.transition = Selectable.Transition.None;
            mask_btn.onClick.AddListener(() => Hide());

            //面板
            GameObject panel_go = new GameObject("Panel", typeof(RectTransform));
            panel_go.transform.SetParent(transform, false);
            panel_rect = panel_go.GetComponent<RectTransform>();
            panel_rect.anchorMin = new Vector2(0.5f, 0.5f);
            panel_rect.anchorMax = new Vector2(0.5f, 0.5f);
            panel_rect.pivot = new Vector2(0.5f, 0.5f);
            panel_rect.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            panel_rect.anchoredPosition = Vector2.zero;
            Image pbg = panel_go.AddComponent<Image>();
            pbg.color = UITheme.BgPopup;

            //标题 + 关闭
            TMP_Text title = MakeLabel("Title", panel_rect, "局域网对战", UITheme.FontPageTitle, UITheme.TextTitle, TextAlignmentOptions.Left);
            At(title.rectTransform, 22f, 16f, 400f, 40f);
            Button close = MakeButton("Close", panel_rect, "×", UITheme.CtrlWeak, 26);
            At(close.GetComponent<RectTransform>(), PANEL_W - 56f, 16f, 40f, 36f);
            close.onClick.AddListener(() => Hide());

            //标签页
            tab_host = MakeButton("TabHost", panel_rect, "开房（我是房主）", UITheme.Ctrl, UITheme.FontButton);
            At(tab_host.GetComponent<RectTransform>(), 22f, 66f, 240f, 44f);
            tab_host.onClick.AddListener(() => SetHostPage(true));
            tab_host_label = tab_host.GetComponentInChildren<TMP_Text>(true);

            tab_join = MakeButton("TabJoin", panel_rect, "加入（输房主 IP）", UITheme.Ctrl, UITheme.FontButton);
            At(tab_join.GetComponent<RectTransform>(), 270f, 66f, 240f, 44f);
            tab_join.onClick.AddListener(() => SetHostPage(false));
            tab_join_label = tab_join.GetComponentInChildren<TMP_Text>(true);

            //当前卡组 + 本机身份（同机双开要看的/要换的就是这个身份）
            TMP_Text deck_lb = MakeLabel("DeckLabel", panel_rect, "使用卡组", UITheme.FontSmall, UITheme.TextDim, TextAlignmentOptions.Left);
            At(deck_lb.rectTransform, 22f, 124f, 70f, 26f);
            txt_deck = MakeLabel("DeckName", panel_rect, "", UITheme.FontBody, UITheme.TextBody, TextAlignmentOptions.Left);
            At(txt_deck.rectTransform, 96f, 124f, 326f, 26f);

            TMP_Text id_lb = MakeLabel("IdLabel", panel_rect, "本机身份", UITheme.FontSmall, UITheme.TextDim, TextAlignmentOptions.Left);
            At(id_lb.rectTransform, 428f, 124f, 70f, 26f);
            txt_identity = MakeLabel("Identity", panel_rect, "", UITheme.FontBody, UITheme.Accent, TextAlignmentOptions.Left);
            At(txt_identity.rectTransform, 502f, 124f, 132f, 26f);

            Button switch_btn = MakeButton("BtnSwitchId", panel_rect, "换个身份", UITheme.Ctrl, UITheme.FontSmall);
            At(switch_btn.GetComponent<RectTransform>(), 640f, 120f, 98f, 32f);
            switch_btn.onClick.AddListener(OnClickSwitchIdentity);

            //---- 开房页 ----
            //两个页签的容器：铺满面板（与 panel_rect 同一坐标系，子控件用 At 按"面板左上角"摆放）
            page_host = UIFactory.CreateRect("PageHost", panel_rect);
            UIFactory.SetStretch(page_host);

            TMP_Text ips_lb = MakeLabel("IpsLabel", page_host, "本机 IP（把其中一个告诉对方）", UITheme.FontSmall, UITheme.TextDim, TextAlignmentOptions.Left);
            At(ips_lb.rectTransform, 22f, 160f, 420f, 26f);
            txt_ips = MakeLabel("Ips", page_host, "", UITheme.FontBody, UITheme.Accent, TextAlignmentOptions.TopLeft);
            txt_ips.enableWordWrapping = true;
            At(txt_ips.rectTransform, 22f, 190f, 460f, 92f);

            TMP_Text port_lb = MakeLabel("PortLabel", page_host, "端口", UITheme.FontSmall, UITheme.TextDim, TextAlignmentOptions.Left);
            At(port_lb.rectTransform, 22f, 292f, 60f, 26f);
            txt_port = MakeLabel("Port", page_host, "", UITheme.FontBody, UITheme.TextBody, TextAlignmentOptions.Left);
            At(txt_port.rectTransform, 84f, 292f, 120f, 26f);

            Button host_btn = MakeButton("BtnHost", page_host, "开房并进入对局", new Color(0.45f, 0.85f, 0.5f, 0.45f), UITheme.FontButton);
            At(host_btn.GetComponent<RectTransform>(), 22f, 330f, 240f, 48f);
            host_btn.onClick.AddListener(OnClickHost);

            Button copy_btn = MakeButton("BtnCopy", page_host, "复制 IP", UITheme.CtrlStrong, UITheme.FontButton);
            At(copy_btn.GetComponent<RectTransform>(), 274f, 330f, 150f, 48f);
            copy_btn.onClick.AddListener(OnClickCopyIp);

            TMP_Text host_hint = MakeLabel("HostHint", page_host,
                "说明：点「开房」后本机成为服务器（玩家 0），对方在「加入」页填上面任意一个 IP 即可进来；\n" +
                "对方加入后自动开始对局。连不通时：检查两台机器是否在同一局域网、Windows 防火墙是否放行该端口。\n" +
                "同机双开：两个窗口请各点一次「换个身份」（否则两边账号同名 → 都被认成玩家0，对局开不起来）。",
                UITheme.FontSmall, UITheme.Hint, TextAlignmentOptions.TopLeft);
            host_hint.enableWordWrapping = true;
            At(host_hint.rectTransform, 22f, 384f, PANEL_W - 44f, 64f);

            //---- 加入页 ----
            page_join = UIFactory.CreateRect("PageJoin", panel_rect);
            UIFactory.SetStretch(page_join);

            TMP_Text ip_lb = MakeLabel("JoinIpLabel", page_join, "房主 IP", UITheme.FontSmall, UITheme.TextDim, TextAlignmentOptions.Left);
            At(ip_lb.rectTransform, 22f, 160f, 200f, 26f);
            input_host_ip = MakeInput("HostIp", page_join, "例如 192.168.1.23");
            At(input_host_ip.GetComponent<RectTransform>(), 22f, 192f, 340f, 44f);
            input_host_ip.onSubmit.AddListener(_ => OnClickJoin());

            TMP_Text join_port_lb = MakeLabel("JoinPortLabel", page_join, "端口", UITheme.FontSmall, UITheme.TextDim, TextAlignmentOptions.Left);
            At(join_port_lb.rectTransform, 22f, 250f, 60f, 26f);
            txt_port_join = MakeLabel("JoinPort", page_join, "", UITheme.FontBody, UITheme.TextBody, TextAlignmentOptions.Left);
            At(txt_port_join.rectTransform, 84f, 250f, 200f, 26f);

            Button join_btn = MakeButton("BtnJoin", page_join, "加入对局", new Color(0.45f, 0.75f, 0.95f, 0.45f), UITheme.FontButton);
            At(join_btn.GetComponent<RectTransform>(), 22f, 290f, 240f, 48f);
            join_btn.onClick.AddListener(OnClickJoin);

            TMP_Text join_hint = MakeLabel("JoinHint", page_join,
                "说明：填写房主机器上显示的 IP（也可以填主机名，同机测试填 127.0.0.1）；端口与房主一致（取自 NetworkData.port）。\n" +
                "同机双开：两个窗口请各点一次「换个身份」；WebGL 不能开房，但可以做房客。",
                UITheme.FontSmall, UITheme.Hint, TextAlignmentOptions.TopLeft);
            join_hint.enableWordWrapping = true;
            At(join_hint.rectTransform, 22f, 352f, PANEL_W - 44f, 64f);

            //状态条
            txt_status = MakeLabel("Status", panel_rect, "", UITheme.FontSmall, UITheme.Accent, TextAlignmentOptions.Left);
            txt_status.enableWordWrapping = true;
            At(txt_status.rectTransform, 22f, PANEL_H - 58f, PANEL_W - 44f, 46f);

            SetHostPage(true);
            UIFonts.ApplyResolved(gameObject);
        }

        // ---------------- 小工具 ----------------

        /// <summary>以"面板左上角"为基准摆放（x 向右、y 向下）</summary>
        private static void At(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private static TMP_Text MakeLabel(string name, Transform parent, string text, int size, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyFont(t);
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static Button MakeButton(string name, Transform parent, string label, Color color, int size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            UITheme.ApplyButtonColors(btn);
            if (!string.IsNullOrEmpty(label))
            {
                TMP_Text t = MakeLabel("Text", go.transform, label, size, Color.white, TextAlignmentOptions.Center);
                UIFactory.SetStretch(t.rectTransform);
            }
            return btn;
        }

        /// <summary>TMP 输入框（与规则编辑器内联输入框同一建法，不依赖 legacy Font）</summary>
        private static TMP_InputField MakeInput(string name, Transform parent, string placeholder)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Image bg = go.GetComponent<Image>();
            bg.color = UITheme.FieldBg;

            TMP_Text ph = MakeLabel("Placeholder", rt, placeholder, UITheme.FontBody, UITheme.Placeholder, TextAlignmentOptions.Left);
            Stretch(ph.rectTransform, 12f, 8f, 4f, 4f);

            TMP_Text txt = MakeLabel("Text", rt, "", UITheme.FontBody, UITheme.TextBody, TextAlignmentOptions.Left);
            txt.enableWordWrapping = false;
            txt.overflowMode = TextOverflowModes.Truncate;
            Stretch(txt.rectTransform, 12f, 8f, 4f, 4f);

            TMP_InputField inp = go.GetComponent<TMP_InputField>();
            inp.targetGraphic = bg;
            inp.textComponent = txt;
            inp.placeholder = ph;
            inp.lineType = TMP_InputField.LineType.SingleLine;
            inp.characterLimit = 64;
            return inp;
        }

        private static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }
}
