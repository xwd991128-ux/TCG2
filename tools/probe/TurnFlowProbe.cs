using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;   //★ GraphEventContext

namespace TcgEngine.Probe
{
    /// <summary>【回合流程：开局 / 回合开始结算 / 回合边界胜负 / 选下家 / 复位】
    /// 断言：
    /// ① 回合开始的毒伤把玩家打死 → **当场**判胜负（修复前：胜负判定在 StartTurn 之前就跑完了，
    ///    被毒死的玩家还能行动一整轮，hasEnded 仍为 false）
    /// ② 回合开始的**玩家毒伤**走统一伤害入口（发「伤害时/后」图事件；修复前是裸 `hp -=`）
    /// ③ 装备区的卡每回合也会复位 exhausted（修复前只复位英雄与战场卡 ⇒ 装备技能永久"点了没反应"）
    /// ④ 选下家：双方都带「失去 N 回合」时，不许把**当前玩家自己**的 skip 层数扣掉
    /// ⑤ 对照：战场卡的每回合复位不受影响
    /// ⑥ 开盘（StartGame）时新对局应清空对战记录（修复前 `turn_count<=0` 是死条件 ⇒ 记录残留）
    /// 触发：建 tools/turnflow_flag.txt → 进 Play → 写 tools/turnflow_result.tsv → 自动删标记。
    /// </summary>
    public class TurnFlowProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/turnflow_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/turnflow_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("TurnFlowProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<TurnFlowProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath)) { done = false; frames = 0; sb.Clear(); }
                return;
            }
            frames++;
            if (frames < 120)
                return;
            done = true;
            try { Run(); }
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体\t" + e.Message + "\t" + e.StackTrace); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[回合流程] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private static int CountActions(GameLogic logic, string action)
        {
            int n = 0;
            List<GraphEventContext> log = logic.GetEventLog();
            for (int i = 0; i < log.Count; i++)
                if (log[i].action == action) n++;
            return n;
        }

        /// <summary>建一局最小对局：双方有英雄、停在 Play、current_player=0</summary>
        private static Game NewGame(CardData heroData, string uid, out GameLogic logic)
        {
            Game g = new Game(uid, 2);
            g.state = GameState.Play;
            g.current_player = 0;
            logic = new GameLogic(g);
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);
            p0.hero = Card.Create(heroData, null, p0, uid + "_h0");
            p1.hero = Card.Create(heroData, null, p1, uid + "_h1");
            p0.hp_max = 30; p0.hp = 30;
            p1.hp_max = 30; p1.hp = 30;
            return g;
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            CardData heroData = null;
            List<CardData> chars = new List<CardData>();
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (heroData == null && c.type == CardType.Hero) heroData = c;
                if (c.type == CardType.Character) chars.Add(c);
            }
            if (heroData == null || chars.Count < 1)
            {
                sb.AppendLine("SKIP\t缺少英雄/角色卡定义\t");
                return;
            }

            //---- ①② 回合开始的毒伤：致死要当场判定 + 要走统一入口（发伤害图事件）----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_turn_a", out logic);
                Player p0 = g.GetPlayer(0);
                p0.hp = 3;
                p0.status.Add(new CardStatus(StatusType.Poisoned, 5, 0));   //毒 5 > 血 3
                int before = CountActions(logic, "OnAfterDamage");
                logic.StartTurn();
                bool ended = g.HasEnded();
                int after = CountActions(logic, "OnAfterDamage");
                Check("① 回合开始的毒伤致死 → 当场判胜负", ended,
                    "hp=" + p0.hp + " state=" + g.state + " hasEnded=" + ended + "（期望 True）");
                Check("② 玩家毒伤走统一伤害入口（发伤害图事件）", after > before,
                    "OnAfterDamage " + before + " → " + after + "（期望增加） hp=" + p0.hp);
            }

            //---- ③⑤ 每回合复位：装备区也要复位（战场卡作为对照）----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_turn_b", out logic);
                Player p0 = g.GetPlayer(0);
                Card board_card = Card.Create(chars[0], null, p0, "tf_board");
                p0.cards_board.Add(board_card);
                Card equip = Card.Create(chars[0], null, p0, "tf_equip");
                p0.cards_equip.Add(equip);
                board_card.exhausted = true;
                equip.exhausted = true;
                logic.StartTurn();
                Check("③ 装备区卡每回合复位 exhausted", !equip.exhausted,
                    "装备卡 exhausted=" + equip.exhausted + "（期望 False，即已复位）");
                Check("⑤ 对照：战场卡复位不受影响", !board_card.exhausted,
                    "战场卡 exhausted=" + board_card.exhausted + "（期望 False）");
            }

            //---- ④ 选下家：双方都带 skip 时不许扣当前玩家自己的层数 ----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_turn_c", out logic);
                g.GetPlayer(0).skip_turns = 1;
                g.GetPlayer(1).skip_turns = 1;
                int next = logic.NextPlayerId(0);
                Check("④ 全体被跳过时不误扣当前玩家 skip", g.GetPlayer(0).skip_turns == 1,
                    "返回下家=" + next + "（期望 0=保持当前玩家） 当前玩家剩余 skip=" + g.GetPlayer(0).skip_turns
                    + "（期望 1，不许被自己消耗掉） 对方剩余 skip=" + g.GetPlayer(1).skip_turns);
            }

            //---- ⑥ 开盘时新对局要清空对战记录 ----
            {
                GameLogic logic;
                Game g = NewGame(heroData, "probe_turn_d", out logic);
                BattleLogEntry old_entry = new BattleLogEntry();   //模拟"上一局残留"（记引用，好断言它被清掉）
                g.battle_log.Add(old_entry);
                g.state = GameState.Connecting;
                int before = g.battle_log.Count;
                bool threw = false;
                try { logic.StartGame(); }
                catch (Exception e) { threw = true; sb.AppendLine("SKIP\t⑥ StartGame 夹具异常（settings/deck 缺失）\t" + e.Message); }
                if (!threw)
                {
                    //★注意：开盘本身的**起手抽牌会合法写入新记录**，所以不能断言"总数=0"，
                    //  只断言"上一局那条已经不在列表里"。
                    bool old_gone = !g.battle_log.Contains(old_entry);
                    Check("⑥ 新对局开盘清掉上一局残留（起手抽牌的新记录保留）", old_gone,
                        "开盘前 " + before + " 条 → 开盘后 " + g.battle_log.Count + " 条（新记录来自起手抽牌）"
                        + " 上一局那条还在=" + !old_gone + "（期望 False）");
                }
            }
        }
    }
}
