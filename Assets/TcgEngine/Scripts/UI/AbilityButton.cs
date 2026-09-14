using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;

namespace TcgEngine.UI
{
    /// <summary>
    /// Ability button on a BoardCard, let you activate abilities
    /// </summary>

    public class AbilityButton : MonoBehaviour
    {
        public Text text;
        public Image focus_highlight;

        private Card card;
        private AbilityData iability;

        private CanvasGroup canvas_group;
        private float target_alpha = 0f;
        private bool focus = false;
        private bool nextfocus = false;
        private bool interactable = false;

        private static List<AbilityButton> button_list = new List<AbilityButton>();

        void Awake()
        {
            button_list.Add(this);
            canvas_group = GetComponent<CanvasGroup>();
            canvas_group.alpha = 0f;
            if (focus_highlight != null)
                focus_highlight.enabled = false;
        }

        private void OnDestroy()
        {
            button_list.Remove(this);
        }

        void Update()
        {
            canvas_group.alpha = Mathf.MoveTowards(canvas_group.alpha, target_alpha, 5f * Time.deltaTime);
            focus = nextfocus;

            if (focus_highlight != null && IsVisible())
                focus_highlight.enabled = focus && IsInteractable();   //"可用高亮"必须等于"现在真能发动"，否则会出现"看着可用、点了没反应"
        }

        public void SetAbility(Card card, AbilityData iability)
        {
            this.card = card;
            this.iability = iability;
            text.text = iability.title;
            if (this.iability.mana_cost > 0)
                text.text += " (" + this.iability.mana_cost + ")";
            canvas_group.interactable = true;
            canvas_group.blocksRaycasts = true;
            target_alpha = 1f;
        }

        public void SetInteractable(bool interact)
        {
            interactable = interact;
        }

        public void Hide()
        {
            if (canvas_group == null)
                canvas_group = GetComponent<CanvasGroup>();

            this.card = null;
            this.iability = null;
            canvas_group.interactable = false;
            canvas_group.blocksRaycasts = false;
            target_alpha = 0f;
        }

        public void OnClick()
        {
            if (card == null || iability == null)
                return;
            if (!Tutorial.Get().CanDo(TutoEndTrigger.CastAbility, card))
                return;
            if (!IsInteractable())
            {
                WarningText.ShowText(RefuseReason(card, iability));   //给出原因，不再静默丢弃
                return;
            }
            GameClient.Get().CastAbility(card, iability);
            PlayerControls.Get().UnselectAll();
        }

        public AbilityData GetAbility()
        {
            return iability;
        }

        public bool IsVisible()
        {
            return canvas_group.alpha > 0.5f;
        }

        /// <summary>按钮是否"现在真的能发动"：基础可发动状态（BoardCard 传入的 Game.CanCastAbility）
        /// **且** 处于自己的行动回合（= 服务端 ReceiveCastCardAbility 的准入条件）。
        /// 旧实现只判 CanCastAbility，不含回合/选择器状态 → 按钮会亮但点下去被服务端静默丢弃（"看着可用、点了没反应"）。</summary>
        public bool IsInteractable()
        {
            return interactable && IsVisible() && IsActionTurnNow();
        }

        /// <summary>是否处于自己的行动回合（与服务端一致的判定：current_player 是自己 && Play && Main && selector==None）</summary>
        private static bool IsActionTurnNow()
        {
            GameClient gc = GameClient.Get();
            if (gc == null || !gc.IsReady())
                return false;
            Game gdata = gc.GetGameData();
            Player player = gc.GetPlayer();
            return gdata != null && player != null && gdata.IsPlayerActionTurn(player);
        }

        /// <summary>
        /// 不能发动的原因（点击时用 WarningText 显示，避免"点了完全没反应"）。
        /// 判定次序与 Game.CanCastAbility / Player.CanPayAbility 一致，便于一眼定位是配置问题还是费用问题。
        /// 返回 null = 可以发动。
        /// </summary>
        public static string RefuseReason(Card card, AbilityData ability)
        {
            if (card == null || ability == null)
                return "无法使用";
            GameClient gc = GameClient.Get();
            if (gc == null || !gc.IsReady())
                return "对局尚未就绪";
            Game gdata = gc.GetGameData();
            Player me = gc.GetPlayer();
            if (gdata == null || me == null)
                return "无法使用";
            if (!gdata.IsPlayerActionTurn(me))
                return gdata.IsPlayerSelectorTurn(me) ? "请先完成目标选择" : "不是你的行动回合";
            if (ability.exhaust && card.exhausted)
                return "本卡本回合已行动（可用「复原技能」刷新）";
            Player owner = gdata.GetPlayer(card.player_id);
            if (owner != null && owner.mana < ability.mana_cost)
                return "灵力不足（需要 " + ability.mana_cost + "，当前 " + owner.mana + "）";
            if (!gdata.CanCastAbility(card, ability))
                return "发动条件不满足（检查「发动条件」连线/每回合一次/沉默等）";
            return null;
        }

        public void MouseEnter()
        {
            focus = true;
            nextfocus = true;
        }

        public void MouseExit()
        {
            nextfocus = false; //Keep it focused 1 more frame to work on mobile
        }

        public static AbilityButton GetFocus(Vector3 pos, float range = 999f)
        {
            AbilityButton nearest = null;
            float min_dist = range;
            foreach (AbilityButton button in button_list)
            {
                float dist = (button.transform.position - pos).magnitude;
                if (button.focus && button.IsVisible() && dist < min_dist)
                {
                    min_dist = dist;
                    nearest = button;
                }
            }
            return nearest;
        }

        public static AbilityButton GetNearest(Vector3 pos, float range = 999f)
        {
            AbilityButton nearest = null;
            float min_dist = range;
            foreach (AbilityButton button in button_list)
            {
                float dist = (button.transform.position - pos).magnitude;
                if (dist < min_dist)
                {
                    min_dist = dist;
                    nearest = button;
                }
            }
            return nearest;
        }

    }
}
