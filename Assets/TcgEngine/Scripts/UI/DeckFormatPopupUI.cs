using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;

namespace TcgEngine.UI
{
    /// <summary>
    /// 对局前「构筑规则」选择弹框：选构筑环境（标准/乱斗）+ 勾选可选自定义规则。
    ///
    /// 规则数据与判定全部来自数据层（DeckFormatData / DeckValidator），本类**只做界面**：
    ///   · 卡组在当前「环境 + 勾选」下不合法时，「开始」不可点，并把中文错误**逐条**列在下方；
    ///   · 点「开始」才把选择写进 GameClient.game_settings（deck_format_id / deck_optional_rules），
    ///     随开局设置一起发给服务端 —— 保证客户端与服务端用同一套规则校验。
    ///
    /// 外观/交互全部走 PopupSkin（= 项目规格：行高 UITheme.RowH、字号 token、主题色、折行自适应、图标关闭按钮）。
    /// 本类里**不出现**任何字号/行高/颜色的散值，也不手算底部元素坐标（用 LayoutGroup 排版，杜绝压字）。
    ///
    /// 生命周期与 VFXEditorPopup 一致：静态 instance + Create(Transform) 幂等 + EnsureBuilt 只建一次 + 常驻复用。
    /// </summary>
    public class DeckFormatPopupUI : UIPanel
    {
        private const float PanelWidth = 780f;
        private const float MaxPanelHeight = 860f;
        private const float MinPanelHeight = 520f;

        private static DeckFormatPopupUI instance;

        private bool built;
        private RectTransform rt_panel;
        private RectTransform list_root;
        private TMP_Text txt_status;
        private Button btn_confirm;
        private Button btn_back;

        private UserDeckData deck;
        private UserDeckData ai_deck;
        private Action on_confirm;
        private DeckFormatData cur_format;                 //null = 用 GameplayData 默认（标准行为）
        private readonly List<string> toggled = new List<string>();

        //============================ 生命周期 ============================

        /// <summary>取（首次则建）弹框实例；context 用于定位所在 Canvas（不内部 Find）。</summary>
        public static DeckFormatPopupUI Create(Transform context)
        {
            if (instance != null)
                return instance;

            Canvas canvas = context != null ? context.GetComponentInParent<Canvas>() : null;
            Transform parent = canvas != null ? canvas.transform : context;
            RectTransform root = PopupSkin.CreateRootLayer(parent, "DeckFormatPopup", 1);
            instance = root.gameObject.AddComponent<DeckFormatPopupUI>();
            instance.EnsureBuilt();
            instance.Hide(true);
            return instance;
        }

        /// <summary>打开弹框（无对手卡组场景，例如自定义房）</summary>
        public void Open(UserDeckData target_deck, Action confirm)
        {
            Open(target_deck, null, confirm);
        }

        /// <summary>打开弹框：deck 为要校验的玩家卡组；点「开始」时回调 on_confirm（选择已写入对局设置）。</summary>
        public void Open(UserDeckData target_deck, UserDeckData target_ai_deck, Action confirm)
        {
            EnsureBuilt();
            deck = target_deck;
            ai_deck = target_ai_deck;
            on_confirm = confirm;
            cur_format = DefaultFormat();
            toggled.Clear();
            Rebuild();
            Show();
            UIFonts.ApplyResolved(gameObject);
        }

        protected override void Update()
        {
            base.Update();      //★ 必须调用：UIPanel 的淡入淡出在这里推进

            if (IsVisible() && PopupSkin.EscPressed())
                Hide();         //Esc 与点遮罩同义：取消本次开局

            PopupSkin.AnimateScale(rt_panel, IsVisible());
        }

        //============================ 构建 ============================

        private void EnsureBuilt()
        {
            if (built)
                return;

            //清掉上次构建中途失败留下的半成品（它会挡住点击，且字段为 null 时永远无法重建）
            Transform stale = transform.Find("Panel");
            if (stale != null)
                Destroy(stale.gameObject);

            Button mask = PopupSkin.CreateMask((RectTransform)transform);
            mask.onClick.AddListener(() => Hide());     //纯选择弹层：点遮罩 = 取消（底部状态行会写清这一点）

            float h = Mathf.Clamp(Screen.height - 120f, MinPanelHeight, MaxPanelHeight);
            rt_panel = PopupSkin.CreatePanel((RectTransform)transform, "Panel", PanelWidth, h);

            Button close;
            PopupSkin.CreateTitleRow(rt_panel, "构筑规则", out close);
            close.onClick.AddListener(() => Hide());

            list_root = PopupSkin.CreateScroll(rt_panel, PopupSkin.FooterH);

            RectTransform row = PopupSkin.CreateFooter(rt_panel, PopupSkin.FooterH, out txt_status);
            btn_back = PopupSkin.CreateFooterButton(row, "返回调整", UITheme.BtnWNormal, OnClickBackToEdit);
            btn_confirm = PopupSkin.CreateFooterButton(row, "开始", UITheme.BtnWNormal, OnClickConfirm);

            built = true;   //全部构建成功后才置位
        }

        //============================ 刷新 ============================

        /// <summary>重建整份列表。只在打开/用户操作时调用，不每帧跑。</summary>
        private void Rebuild()
        {
            PopupSkin.ClearList(list_root);
            BuildFormatRows();
            BuildRuleRows();
            BuildResultRows();
            RefreshButtons();
        }

        private void BuildFormatRows()
        {
            PopupSkin.CreateSection(list_root, "构筑环境（点一行即选中）");

            DeckFormatData std = DeckFormatData.GetStandard();
            if (std == null)
            {
                //没有环境资产时明确给一行：它对应 GameplayData 的默认 30/2，弹框不至于空着
                bool on = cur_format == null;
                PopupSkin.CreateOptionRow(list_root, "标准（默认：主卡 30 / 同名 ≤2）", on, false, () =>
                {
                    cur_format = null;
                    toggled.Clear();
                    Rebuild();
                });
                return;
            }

            //★ 默认选中的那一项必须排在最前：按 Resources.LoadAll 的顺序它常被排在末尾，
            //  玩家一打开只看到一堆乱斗，会以为"没有标准/不知道该选哪儿"。
            CreateFormatRow(std);
            foreach (DeckFormatData f in DeckFormatData.GetAll())
            {
                if (f == null || f == std)
                    continue;
                CreateFormatRow(f);
            }
        }

        private void CreateFormatRow(DeckFormatData f)
        {
            DeckFormatData captured = f;
            bool on = cur_format == captured && cur_format != null;
            string label = FormatLabel(f) + "　" + FormatLimitText(f) + (on ? "　（当前选中）" : "");
            PopupSkin.CreateOptionRow(list_root, label, on, false, () =>
            {
                cur_format = captured;
                toggled.Clear();     //换环境后旧的勾选不再适用
                Rebuild();
            });
        }

        private void BuildRuleRows()
        {
            if (cur_format == null || cur_format.optional_modifiers == null || cur_format.optional_modifiers.Count == 0)
                return;

            PopupSkin.CreateSection(list_root, "可选规则（勾选后生效）");
            foreach (DeckModifier mod in cur_format.optional_modifiers)
            {
                if (mod == null || string.IsNullOrEmpty(mod.id))
                    continue;
                DeckModifier captured = mod;
                bool on = toggled.Contains(captured.id);
                PopupSkin.CreateOptionRow(list_root, RuleLabel(captured), on, true, () =>
                {
                    if (on)
                        toggled.Remove(captured.id);
                    else
                        toggled.Add(captured.id);
                    Rebuild();
                });
            }
        }

        /// <summary>
        /// 校验结果：**玩家与对手分开两块**列。
        /// 之前把「（对手）」的错误混进玩家同一列，玩家会看到"不合法"却找不到自己哪里错（实测就是这个观感）。
        /// </summary>
        private void BuildResultRows()
        {
            List<DeckError> mine = deck != null ? DeckValidator.Validate(deck, cur_format, toggled) : NoDeckErrors();
            List<DeckError> theirs = AiErrors();

            PopupSkin.CreateSection(list_root, "校验结果");

            PopupSkin.CreateInfoRow(list_root, mine.Count == 0 ? "你的卡组：符合当前构筑规则" : "你的卡组：不合法（" + mine.Count + " 条）",
                mine.Count == 0 ? UITheme.Accent : UITheme.Danger);
            foreach (DeckError e in mine)
                PopupSkin.CreateInfoRow(list_root, "· " + e.message, UITheme.Danger);

            if (ai_deck != null || theirs.Count > 0)
            {
                PopupSkin.CreateInfoRow(list_root, theirs.Count == 0 ? "对手（AI）卡组：符合当前构筑规则" : "对手（AI）卡组：不合法（" + theirs.Count + " 条）",
                    theirs.Count == 0 ? UITheme.Accent : UITheme.Danger);
                foreach (DeckError e in theirs)
                    PopupSkin.CreateInfoRow(list_root, "· " + e.message, UITheme.Danger);
            }
        }

        private void RefreshButtons()
        {
            int mine = deck != null ? DeckValidator.Validate(deck, cur_format, toggled).Count : 1;
            int theirs = AiErrors().Count;
            bool ok = mine == 0 && theirs == 0;

            //禁用态的视觉交给 Selectable 自带的 disabled 过渡（项目统一做法），不再自己改底色
            if (btn_confirm != null)
                btn_confirm.interactable = ok;
            if (btn_back != null)
                btn_back.interactable = !ok;      //合法时没有"要调整的东西"

            if (txt_status == null)
                return;
            txt_status.color = ok ? UITheme.TextDim : UITheme.Danger;
            txt_status.text = ok
                ? "卡组合法，可以开始；点遮罩或 × 可取消本次开局"
                : "卡组不合法，「开始」已禁用 —— 点「返回调整」直接跳回组卡界面并定位出错卡";
        }

        //============================ 错误来源 ============================

        private List<DeckError> NoDeckErrors()
        {
            List<DeckError> list = new List<DeckError>();
            list.Add(new DeckError("还没有选择卡组 —— 请先在组卡界面选一副卡组。"));
            return list;
        }

        /// <summary>
        /// 对手（AI）卡组也要按同一套规则校验：否则服务端会拒绝它（玩家只看到"一直连不上"）。
        /// 单机下 AI 卡组由玩家自选，挡住它比让它拖死开局更友好。
        /// </summary>
        private List<DeckError> AiErrors()
        {
            if (ai_deck == null)
                return new List<DeckError>();      //没传对手卡组（自定义房）：不当作错误
            return DeckValidator.Validate(ai_deck, cur_format, toggled);
        }

        //============================ 操作 ============================

        private void OnClickConfirm()
        {
            if (btn_confirm != null && !btn_confirm.interactable)
                return;
            //空串而不是 null：裸 string 为 null 会让开局卡在 Connecting（Phase 2 踩过）
            GameClient.game_settings.deck_format_id = cur_format != null ? cur_format.id : "";
            GameClient.game_settings.deck_optional_rules = DeckRuleToggles.Join(toggled);
            Hide();
            if (on_confirm != null)
                on_confirm();
        }

        /// <summary>「返回调整」：切到组卡面板 → 打开该卡组的编辑视图 → 定位到第一条"能落到具体卡"的错误</summary>
        private void OnClickBackToEdit()
        {
            UserDeckData target = deck;
            string tid = FirstErrorCardTid();
            Hide();

            CollectionPanel panel = CollectionPanel.Get();
            if (panel == null)
            {
                Debug.LogWarning("[构筑规则] 找不到组卡面板，无法跳转。");
                return;
            }
            if (!TabButton.ActivatePanel(panel))
                panel.Show();     //没绑到选项卡（或不同组）时退化为直接显示
            panel.OpenDeckForEdit(target);
            if (!string.IsNullOrEmpty(tid))
                panel.FocusDeckCard(tid);
        }

        /// <summary>第一条"能定位到具体卡"的错误（整组级错误没有 card_tid，跳过）</summary>
        private string FirstErrorCardTid()
        {
            if (deck == null)
                return null;
            List<DeckError> errors = DeckValidator.Validate(deck, cur_format, toggled);
            foreach (DeckError e in errors)
            {
                if (e != null && !string.IsNullOrEmpty(e.card_tid))
                    return e.card_tid;
            }
            return null;
        }

        /// <summary>
        /// 默认环境：只认「标准环境资产」，没有就返回 null（= 走 GameplayData 的默认 30/2）。
        /// ★ 这里**绝不能**退化成"取列表第一个"：那会让玩家一打开弹框就默默处在某个乱斗里
        ///   （例如"主卡 40 张"），他自己的 30 张卡组反而显示"不合法"，非常难理解。
        /// </summary>
        private static DeckFormatData DefaultFormat()
        {
            return DeckFormatData.GetStandard();
        }

        private static string FormatLabel(DeckFormatData f)
        {
            return string.IsNullOrEmpty(f.title) ? f.id : f.title;
        }

        private static string FormatLimitText(DeckFormatData f)
        {
            List<string> parts = new List<string> { "主卡 " + f.deck_size };
            if (f.max_copies > 0)
                parts.Add("同名 ≤" + f.max_copies);
            if (f.max_legendary > 0)
                parts.Add("传说 ≤" + f.max_legendary);
            return string.Join("｜", parts.ToArray());
        }

        private static string RuleLabel(DeckModifier mod)
        {
            string title = string.IsNullOrEmpty(mod.title) ? mod.id : mod.title;
            return string.IsNullOrEmpty(mod.desc) ? title : title + " —— " + mod.desc;
        }
    }
}
