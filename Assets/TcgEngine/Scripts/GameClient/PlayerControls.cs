using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Client;
using UnityEngine.Events;
using TcgEngine.UI;

namespace TcgEngine.Client
{
    /// <summary>
    /// Script that contain main controls for clicking on cards, attacking, activating abilities
    /// Holds the currently selected card and will send action to GameClient on click release
    /// </summary>

    public class PlayerControls : MonoBehaviour
    {
        private BoardCard selected_card = null;

        private static PlayerControls instance;

        void Awake()
        {
            instance = this;
        }

        void Update()
        {
            if (!GameClient.Get().IsReady())
                return;

            if (Input.GetMouseButtonDown(1))
                UnselectAll();

            if (selected_card != null)
            {
                if (Input.GetMouseButtonUp(0))
                {
                    ReleaseClick();
                    UnselectAll();
                }
            }
        }

        public void SelectCard(BoardCard bcard)
        {
            Game gdata = GameClient.Get().GetGameData();
            Player player = GameClient.Get().GetPlayer();
            Card card = bcard.GetFocusCard();

            if (gdata.IsPlayerSelectorTurn(player) && gdata.selector == SelectorType.SelectTarget)
            {
                if (!Tutorial.Get().CanDo(TutoEndTrigger.SelectTarget, card))
                    return;

                //Target selector, select this card
                GameClient.Get().SelectCard(card);
            }
            else if (gdata.IsPlayerActionTurn(player) && card.player_id == player.player_id)
            {
                //Start dragging card
                selected_card = bcard;
            }
        }

        public void SelectCardRight(BoardCard card)
        {
            if (!Input.GetMouseButton(0))
            {
                //Nothing on right-click
            }
        }

        private void ReleaseClick()
        {
            bool yourturn = GameClient.Get().IsYourTurn();

            if (yourturn && selected_card != null)
            {
                Card card = selected_card.GetCard();
                Vector3 wpos = GameBoard.Get().RaycastMouseBoard();
                BSlot tslot = BSlot.GetNearest(wpos);
                Card target = tslot?.GetSlotCard(wpos);
                AbilityButton ability = AbilityButton.GetFocus(wpos, 1f);

                //技能按钮优先且**独占**本次点击：可用=发动；不可用=给出原因后结束。
                //旧写法是「不可用就继续往下走」，于是这次点击会被当成攻击/移动送出：
                //服务端再按自己的准入规则静默拒绝 → 表现就是"点了技能完全没反应"，甚至误触攻击。
                if (ability != null)
                {
                    if (!Tutorial.Get().CanDo(TutoEndTrigger.CastAbility, card))
                        return;

                    if (ability.IsInteractable())
                        GameClient.Get().CastAbility(card, ability.GetAbility());
                    else
                        WarningText.ShowText(AbilityButton.RefuseReason(card, ability.GetAbility()));
                    return;
                }
                else if (tslot is BoardSlotPlayer)
                {
                    if (!Tutorial.Get().CanDo(TutoEndTrigger.AttackPlayer, card))
                        return;

                    if (card.exhausted)
                        WarningText.ShowExhausted();
                    else
                        GameClient.Get().AttackPlayer(card, tslot.GetPlayer());
                }
                else if (target != null && target.uid != card.uid && target.player_id != card.player_id)
                {
                    if (!Tutorial.Get().CanDo(TutoEndTrigger.Attack, card) && !Tutorial.Get().CanDo(TutoEndTrigger.Attack, target))
                        return;

                    if (card.exhausted)
                        WarningText.ShowExhausted();
                    else
                        GameClient.Get().AttackTarget(card, target);
                }
                else if (tslot != null && tslot is BoardSlot)
                {
                    if (!Tutorial.Get().CanDo(TutoEndTrigger.Move, tslot.GetSlot()))
                        return;

                    GameClient.Get().Move(card, tslot.GetSlot());
                }
            }
            else if (selected_card != null && !yourturn)
            {
                //选了卡却不在自己的行动回合：给一次提示，而不是"点了完全没反应"
                WarningText.ShowNotYourTurn();
            }
        }

        public void UnselectAll()
        {
            selected_card = null;
        }

        public BoardCard GetSelected()
        {
            return selected_card;
        }

        public static PlayerControls Get()
        {
            return instance;
        }
    }
}