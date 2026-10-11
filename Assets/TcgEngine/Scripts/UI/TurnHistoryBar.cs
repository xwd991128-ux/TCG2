using System.Collections;
using System.Collections.Generic;
using TcgEngine.Client;
using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// History bar shows all the previous moved perform by a player this turn
    /// </summary>

    public class TurnHistoryBar : MonoBehaviour
    {
        public bool is_opponent;
        public TurnHistoryLine[] history_lines;

        /// <summary>★是否显示这条"已使用卡牌"方块记录（左下 = 我方 / 右上 = 对手）。
        /// 置 false = **整排不显示**。为什么是"整排"而不是"只去掉图"：每格的内容
        /// **就是卡图本身**（TurnHistoryLine.card_img = icard.GetFullArt()），
        /// 去掉图只剩一个空方框，没有意义。用户要求：不要显示左下角/右上角那排使用记录卡图。
        /// 信息不丢：同一批动作在「战斗记录」面板里有（文案同源 BattleLog.FormatHistory）。
        /// 想改成"保留格子但显示卡名/类型"？把本开关置回 true，并让 TurnHistoryLine.SetLine 不设 sprite 即可。</summary>
        public static bool ShowPlayedCardArt = false;

        void Update()
        {
            if (!GameClient.Get().IsReady())
                return;

            //★关掉时直接隐藏整排（每帧兜底：格子是被 SetLine 逐个激活的，不主动关会残留）
            if (!ShowPlayedCardArt)
            {
                if (history_lines != null)
                {
                    for (int i = 0; i < history_lines.Length; i++)
                    {
                        if (history_lines[i] != null && history_lines[i].gameObject.activeSelf)
                            history_lines[i].gameObject.SetActive(false);
                    }
                }
                return;
            }

            int player_id = is_opponent ? GameClient.Get().GetOpponentPlayerID() : GameClient.Get().GetPlayerID();
            Game data = GameClient.Get().GetGameData();
            Player player = data.GetPlayer(player_id);

            if (player != null && player.history_list != null)
            {
                int index = 0;
                foreach (ActionHistory order in player.history_list)
                {
                    if (index < history_lines.Length)
                    {
                        history_lines[index].SetLine(order);
                        index++;
                    }
                }

                while (index < history_lines.Length)
                {
                    history_lines[index].Hide();
                    index++;
                }
            }
        }
    }
}
