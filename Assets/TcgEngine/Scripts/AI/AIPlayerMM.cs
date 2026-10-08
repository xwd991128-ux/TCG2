using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine.AI
{
    /// <summary>
    /// AI player using the MinMax AI algorithm
    /// </summary>

    public class AIPlayerMM : AIPlayer
    {
        private AILogic ai_logic;

        private bool is_playing = false;

        public AIPlayerMM(GameLogic gameplay, int id, int level)
        {
            this.gameplay = gameplay;
            player_id = id;
            ai_level = Mathf.Clamp(level, 1, 10);
            ai_logic = AILogic.Create(id, ai_level);
        }

        public override void Update()
        {
            Game game_data = gameplay.GetGameData();
            Player player = game_data.GetPlayer(player_id);

            if (!is_playing && game_data.IsPlayerTurn(player))
            {
                is_playing = true;
                TimeTool.StartCoroutine(AiTurn());
            }

            if (!is_playing && game_data.IsPlayerMulliganTurn(player))
            {
                SkipMulligan();
            }

            if (!game_data.IsPlayerTurn(player) && ai_logic.IsRunning())
                Stop();
        }

        /// <summary>连续"动作执行了但局面毫无变化"的次数（= 动作被引擎拒绝/空操作）。</summary>
        private int no_progress_streak = 0;

        /// <summary>局面指纹：识别"AI 执行了动作却什么都没发生"。动作合法一定改变局面（手牌/战场/血/灵力/已行动标记
        /// 至少变一个）；指纹不变 = 这个动作被引擎拒了，AI 再算一百次也还是同一个结果。
        /// 实测出处：AI 连续两次 attack_player fish（第一次就被拒）+ 之后 12 次搜索局面完全不变（Nodes 恒定 102）→ 对局卡死。</summary>
        private string StateSignature(Game g)
        {
            if (g == null)
                return "null";
            Player me = g.GetPlayer(player_id);
            Player op = g.GetOpponentPlayer(player_id);
            int exhausted = 0;
            if (me != null && me.cards_board != null)
            {
                foreach (Card c in me.cards_board)
                {
                    if (c != null && c.exhausted)
                        exhausted++;
                }
            }
            return g.turn_count + "|" + g.current_player + "|" + g.state
                + "|" + (me != null ? me.cards_hand.Count + "," + me.cards_board.Count + "," + me.mana + "," + me.hp : "-")
                + "|" + (op != null ? op.cards_hand.Count + "," + op.cards_board.Count + "," + op.hp : "-")
                + "|" + exhausted;
        }

        private IEnumerator AiTurn()
        {
            yield return new WaitForSeconds(0.3f);

            Game game_data = gameplay.GetGameData();
            string sig_before = StateSignature(game_data);
            ai_logic.RunAI(game_data);

            while (ai_logic.IsRunning())
            {
                yield return new WaitForSeconds(0.05f);
            }

            AIAction best = ai_logic.GetBestAction();

            if (best != null)
            {
                Debug.Log("Execute AI Action: " + best.GetText(game_data) + "\n" + ai_logic.GetNodePath());

                ExecuteAction(best);

                //★防卡兜底 ①：执行完局面指纹没变 ⇒ 这次动作被拒了。连续 2 次就结束回合，
                //  保证对局一定能往前走（宁可少打一手，也不能整局卡死在 AI 回合）。
                if (StateSignature(gameplay.GetGameData()) == sig_before)
                {
                    no_progress_streak++;
                    Debug.LogWarning("[AI] 动作无任何效果（局面未变）：" + best.GetText(game_data)
                        + "｜第 " + no_progress_streak + " 次（多半被引擎拒绝，如攻击被规则/状态挡下）");
                    if (no_progress_streak >= 2)
                    {
                        Debug.LogWarning("[AI] 连续 2 次无效动作 → 强制结束回合（防对局卡死）");
                        no_progress_streak = 0;
                        ai_logic.ClearMemory();
                        yield return new WaitForSeconds(0.2f);
                        EndTurn();
                        is_playing = false;
                        yield break;
                    }
                }
                else
                {
                    no_progress_streak = 0;
                }
            }
            else
            {
                //★防卡兜底 ②：AI 一个动作都没选出来 ⇒ 直接结束回合。
                //  否则 AIPlayerMM.Update 会每帧重开一次搜索，永远停在 AI 回合（用户实报"对面卡住"）。
                Debug.LogWarning("[AI] 未选出任何动作 → 结束回合（防对局卡死）");
                no_progress_streak = 0;
                ai_logic.ClearMemory();
                yield return new WaitForSeconds(0.2f);
                EndTurn();
                is_playing = false;
                yield break;
            }

            ai_logic.ClearMemory();

            yield return new WaitForSeconds(0.2f);
            is_playing = false;
        }

        private void Stop()
        {
            ai_logic.Stop();
            is_playing = false;
        }

        //----------

        private void ExecuteAction(AIAction action)
        {
            if (!CanPlay())
                return;

            if (action.type == GameAction.PlayCard)
            {
                PlayCard(action.card_uid, action.slot);
            }

            if (action.type == GameAction.Attack)
            {
                AttackCard(action.card_uid, action.target_uid);
            }

            if (action.type == GameAction.AttackPlayer)
            {
                AttackPlayer(action.card_uid, action.target_player_id);
            }

            if (action.type == GameAction.Move)
            {
                MoveCard(action.card_uid, action.slot);
            }

            if (action.type == GameAction.CastAbility)
            {
                CastAbility(action.card_uid, action.ability_id);
            }

            if (action.type == GameAction.SelectCard)
            {
                SelectCard(action.target_uid);
            }

            if (action.type == GameAction.SelectPlayer)
            {
                SelectPlayer(action.target_player_id);
            }

            if (action.type == GameAction.SelectSlot)
            {
                SelectSlot(action.slot);
            }

            if (action.type == GameAction.SelectChoice)
            {
                SelectChoice(action.value);
            }

            if (action.type == GameAction.SelectCost)
            {
                SelectCost(action.value);
            }

            if (action.type == GameAction.SelectMulligan)
            {
                SkipMulligan();
            }

            if (action.type == GameAction.CancelSelect)
            {
                CancelSelect();
            }

            if (action.type == GameAction.SkipTarget)
            {
                SkipTarget();
            }

            if (action.type == GameAction.EndTurn)
            {
                EndTurn();
            }

            if (action.type == GameAction.Resign)
            {
                Resign();
            }
        }

        private void PlayCard(string card_uid, Slot slot)
        {
            Game game_data = gameplay.GetGameData();
            Card card = game_data.GetCard(card_uid);
            if (card != null)
            {
                gameplay.PlayCard(card, slot);
            }
        }

        private void MoveCard(string card_uid, Slot slot)
        {
            Game game_data = gameplay.GetGameData();
            Card card = game_data.GetCard(card_uid);
            if (card != null)
            {
                gameplay.MoveCard(card, slot); 
            }
        }

        private void AttackCard(string attacker_uid, string target_uid)
        {
            Game game_data = gameplay.GetGameData();
            Card card = game_data.GetCard(attacker_uid);
            Card target = game_data.GetCard(target_uid);
            if (card != null && target != null)
            {
                gameplay.AttackTarget(card, target);
            }
        }

        private void AttackPlayer(string attacker_uid, int target_player_id)
        {
            Game game_data = gameplay.GetGameData();
            Card card = game_data.GetCard(attacker_uid);
            if (card != null)
            {
                Player oplayer = game_data.GetPlayer(target_player_id);
                gameplay.AttackPlayer(card, oplayer);
            }
        }

        private void CastAbility(string caster_uid, string ability_id)
        {
            Game game_data = gameplay.GetGameData();
            Card caster = game_data.GetCard(caster_uid);
            AbilityData iability = AbilityData.Get(ability_id);
            if (caster != null && iability != null)
            {
                gameplay.CastAbility(caster, iability);
            }
        }

        private void SelectCard(string target_uid)
        {
            Game game_data = gameplay.GetGameData();
            Card target = game_data.GetCard(target_uid);
            if (target != null)
            {
                gameplay.SelectCard(target);
            }
        }

        private void SelectPlayer(int tplayer_id)
        {
            Game game_data = gameplay.GetGameData();
            Player target = game_data.GetPlayer(tplayer_id);
            if (target != null)
            {
                gameplay.SelectPlayer(target);
            }
        }

        private void SelectSlot(Slot slot)
        {
            if (slot != Slot.None)
            {
                gameplay.SelectSlot(slot);
            }
        }

        private void SelectChoice(int choice)
        {
            gameplay.SelectChoice(choice);
        }

        private void SelectCost(int cost)
        {
            gameplay.SelectCost(cost);
        }

        private void CancelSelect()
        {
            if (CanPlay())
            {
                gameplay.CancelSelection();
            }
        }

        /// <summary>多目标：跳过当前目标槽（不取消整次施法）</summary>
        private void SkipTarget()
        {
            if (CanPlay())
            {
                gameplay.SkipCurrentSelectSlot();
            }
        }

        private void SkipMulligan()
        {
            string[] cards = new string[0];
            SelectMulligan(cards);
        }

        private void SelectMulligan(string[] cards)
        {
            Game game_data = gameplay.GetGameData();
            Player player = game_data.GetPlayer(player_id);
            gameplay.Mulligan(player, cards);
        }

        private void EndTurn()
        {
            if (CanPlay())
            {
                gameplay.EndTurn();
            }
        }

        private void Resign()
        {
            int other = player_id == 0 ? 1 : 0;
            gameplay.EndGame(other);
        }

    }

}
