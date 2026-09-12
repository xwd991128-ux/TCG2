using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;
using TcgEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// Box that appears when using the SelectTarget ability target.
    /// 多目标（顺序逐槽选择）时显示「已选 x/N：当前目标槽要求」，并提供一个运行时生成的「跳过此目标」按钮
    /// （不改预制体/场景：按钮按需在本面板下创建；无合法候选的槽由服务端自动跳过，不需要按钮）。
    /// </summary>

    public class SelectTargetUI : SelectorPanel
    {
        public Text title;
        public Text desc;

        private static SelectTargetUI _instance;

        private Button skip_button;     //多目标：跳过当前槽（与"取消整次施法"不同）
        private Text skip_label;

        protected override void Awake()
        {
            _instance = this;
            base.Awake();
        }

        protected override void Update()
        {
            base.Update();

            Game game = GameClient.Get().GetGameData();
            if (game != null && game.selector == SelectorType.None)
            {
                Hide();
                SetSkipVisible(false);
                return;
            }

            RefreshMultiInfo(game);
        }

        public override void Show(AbilityData ability, Card caster)
        {
            if (ability != null && title != null)
                this.title.text = ability.title;
            //this.desc.text = ability.desc;
            Show();
            RefreshMultiInfo(GameClient.Get().GetGameData());
        }

        /// <summary>多目标：刷新「已选 x/N + 当前槽要求」文案，并显示/隐藏跳过按钮</summary>
        private void RefreshMultiInfo(Game game)
        {
            AbilityData ability = game != null ? AbilityData.Get(game.selector_ability_id) : null;
            bool multi = ability != null && ability.HasTargetSlots();
            SetSkipVisible(multi);
            if (!multi)
                return;

            int total = game.SelectSlotTotal();
            int done = game.SelectSlotDone();
            int node_slot = game.CurrentSelectSlotNode();
            AbilityTargetSlot slot = ability.GetTargetSlot(node_slot);

            if (title != null)
                title.text = (ability.title ?? "请选择目标") + "（已选 " + done + "/" + total + "）";
            if (desc != null)
                desc.text = "目标" + node_slot + "：" + (slot != null ? slot.side + slot.label : "");
        }

        public void OnClickClose()
        {
            GameClient.Get().CancelSelection();
        }

        public void OnClickSkipTarget()
        {
            GameClient.Get().SkipTarget();
        }

        private void SetSkipVisible(bool visible)
        {
            if (visible)
                EnsureSkipButton();
            if (skip_button != null && skip_button.gameObject.activeSelf != visible)
                skip_button.gameObject.SetActive(visible);
        }

        /// <summary>运行时生成「跳过此目标」按钮（挂在面板底部；复用面板字体，避免改预制体/场景）</summary>
        private void EnsureSkipButton()
        {
            if (skip_button != null)
                return;

            GameObject go = new GameObject("SkipTargetButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 8f);
            rt.sizeDelta = new Vector2(200f, 44f);

            Image img = go.GetComponent<Image>();
            img.color = new Color(0.22f, 0.24f, 0.30f, 0.95f);

            skip_button = go.GetComponent<Button>();
            skip_button.targetGraphic = img;
            skip_button.onClick.AddListener(OnClickSkipTarget);

            GameObject txt = new GameObject("Label", typeof(RectTransform), typeof(Text));
            txt.transform.SetParent(go.transform, false);
            RectTransform trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            skip_label = txt.GetComponent<Text>();
            skip_label.text = "跳过此目标";
            skip_label.alignment = TextAnchor.MiddleCenter;
            skip_label.color = Color.white;
            skip_label.fontSize = 22;
            skip_label.raycastTarget = false;
            if (title != null)
                skip_label.font = title.font;   //复用面板字体（避免默认 Arial 无中文字形）
        }

        public override bool ShouldShow()
        {
            Game data = GameClient.Get().GetGameData();
            int player_id = GameClient.Get().GetPlayerID();
            return data.selector == SelectorType.SelectTarget && data.selector_player_id == player_id;
        }

        public static SelectTargetUI Get()
        {
            return _instance;
        }
    }
}
