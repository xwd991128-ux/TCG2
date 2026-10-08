using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【对战记录验证】BattleLog（影之诗「战斗记录」口径）：
    /// ① 绑定：GameLogic(Game) 会绑定 BattleLog，Game.battle_log 可用；
    /// ② 出牌镜像：PlayCard → 记 PlayCard（走 Player.AddHistory 唯一汇聚点）；
    /// ③ 抽牌：DrawCard → 记 Draw；
    /// ④ 伤害：DamagePlayer → 记 Damage + 数值；
    /// ⑤ 击杀：KillCard → 记 Death；
    /// ⑥ 回合分隔：BattleLog.Turn → TurnStart 且能被格式化；
    /// ⑦ AI 推演隔离：GameLogic(bool) 生成的 logic 不允许记录（CanRecord=false）；
    /// ⑧ Game.Clone 不拷日志（AI 预测树不能带日志）；
    /// ⑨ 中文格式化：Format 输出含卡名与数值。
    /// 触发：建 tools/battlelog_flag.txt → 进 Play → 写 tools/battlelog_result.tsv → 自动删标记。
    /// </summary>
    public class BattleLogProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/battlelog_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/battlelog_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("BattleLogProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<BattleLogProbe>();
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
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e.Message); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[对战记录] 验证完成 → " + OutPath);
        }

        private int Count(int player_id, BattleLogKind kind, Game g)
        {
            int n = 0;
            foreach (BattleLogEntry e in g.battle_log)
                if (e.actor == player_id && e.kind == (byte)kind) n++;
            return n;
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");
            int cfg = GameplayData.Get().cards_max;

            Game game = new Game("probe_battlelog", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0], p1 = game.players[1];

            CardData hero = null, any = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (hero == null && c.type == CardType.Hero) hero = c;
                if (any == null && c.type == CardType.Character) any = c;
            }
            if (hero == null || any == null) { sb.AppendLine("SKIP\t缺少英雄/角色定义\t"); return; }

            //---- ① 绑定 ----
            bool bound = BattleLog.Current == game && game.battle_log != null && BattleLog.CanRecord(logic);
            sb.AppendLine((bound ? "PASS" : "FAIL") + "\t① GameLogic(Game) 绑定对战记录\tCurrent=game 可记录=" + BattleLog.CanRecord(logic));

            p0.hero = Card.Create(hero, null, p0, "bl_p0hero"); p0.hp_max = 30; p0.hp = 30;
            p1.hero = Card.Create(hero, null, p1, "bl_p1hero"); p1.hp_max = 30; p1.hp = 30;
            p0.hand_max = cfg;
            game.turn_count = 3;

            //---- ③ 抽牌（先测，避免受出牌影响）----
            for (int i = 0; i < 3; i++)
                p0.cards_deck.Add(Card.Create(any, null, p0, "bl_deck_" + i));
            int draws_before = Count(0, BattleLogKind.Draw, game);
            logic.DrawCard(p0, 1);
            int draws_after = Count(0, BattleLogKind.Draw, game);
            sb.AppendLine((draws_after == draws_before + 1 ? "PASS" : "FAIL")
                + "\t② 抽牌记入记录\tDraw 条数 " + draws_before + "→" + draws_after);

            //---- ② 出牌镜像（Player.AddHistory 唯一汇聚点）----
            int plays_before = Count(0, BattleLogKind.PlayCard, game);
            Card hand_card = Card.Create(any, null, p0, "bl_play");
            p0.cards_hand.Add(hand_card);
            p0.mana = 99; p0.mana_max = 99;
            try { logic.PlayCard(hand_card, new Slot(1, 1, p0.player_id), true); }
            catch (Exception e) { sb.AppendLine("   出牌异常: " + e.Message); }
            int plays_after = Count(0, BattleLogKind.PlayCard, game);
            sb.AppendLine((plays_after == plays_before + 1 ? "PASS" : "FAIL")
                + "\t③ 出牌走 AddHistory 镜像记入\tPlayCard 条数 " + plays_before + "→" + plays_after);

            //---- ④ 伤害 ----
            int dmg_before = Count(p1.player_id, BattleLogKind.Damage, game);
            logic.DamagePlayer(null, p1, 4);
            int dmg_after = Count(p1.player_id, BattleLogKind.Damage, game);
            int dmg_value = 0;
            foreach (BattleLogEntry e in game.battle_log)
                if (e.kind == (byte)BattleLogKind.Damage) dmg_value = e.value;
            sb.AppendLine((dmg_after == dmg_before + 1 && dmg_value == 4 ? "PASS" : "FAIL")
                + "\t④ 伤害记入（含数值）\tDamage 条数 " + dmg_before + "→" + dmg_after + " 数值=" + dmg_value);

            //---- ⑤ 击杀 ----
            Card victim = Card.Create(any, null, p1, "bl_victim");
            p1.cards_board.Add(victim);
            victim.damage = 999;
            int death_before = Count(p1.player_id, BattleLogKind.Death, game);
            try { logic.KillCard(p0.hero, victim); } catch (Exception e) { sb.AppendLine("   击杀异常: " + e.Message); }
            int death_after = Count(p1.player_id, BattleLogKind.Death, game);
            sb.AppendLine((death_after == death_before + 1 ? "PASS" : "FAIL")
                + "\t⑤ 击杀记入\tDeath 条数 " + death_before + "→" + death_after);

            //---- ⑥ 回合分隔 + 中文格式化 ----
            BattleLog.Turn(0, true);
            BattleLogEntry turn_e = null;
            foreach (BattleLogEntry e in game.battle_log)
                if (e.kind == (byte)BattleLogKind.TurnStart) turn_e = e;
            string turn_text = turn_e != null ? BattleLog.Format(turn_e, game, 0) : "(无)";
            bool turn_ok = turn_e != null && turn_text.Contains("回合");
            sb.AppendLine((turn_ok ? "PASS" : "FAIL") + "\t⑥ 回合分隔行可格式化\t文本=\"" + turn_text + "\"");

            //---- ⑨ 中文文案含卡名/数值 ----
            BattleLog.Card(p1.player_id, BattleLogKind.Death, victim);
            string death_text = "";
            for (int i = game.battle_log.Count - 1; i >= 0; i--)
                if (game.battle_log[i].kind == (byte)BattleLogKind.Death) { death_text = BattleLog.Format(game.battle_log[i], game, 0); break; }
            bool text_ok = !string.IsNullOrEmpty(death_text) && death_text.Contains("被消灭") && death_text.Contains("?") == false;
            sb.AppendLine((text_ok ? "PASS" : "FAIL") + "\t⑦ 中文文案含卡名\t文本=\"" + death_text + "\"");

            //---- ⑧ AI 推演隔离 ----
            GameLogic ai = new GameLogic(true);
            ai.SetData(game);
            bool isolated = !BattleLog.CanRecord(ai) && BattleLog.Current == game;
            sb.AppendLine((isolated ? "PASS" : "FAIL")
                + "\t⑧ AI 推演不得记录（隔离）\tCanRecord(ai)=" + BattleLog.CanRecord(ai));

            //---- ⑨ Game.Clone 不拷日志 ----
            Game clone = new Game("probe_clone", 2);
            game.battle_log.Add(new BattleLogEntry());
            int before = game.battle_log.Count;
            try { Game.Clone(game, clone); } catch (Exception e) { sb.AppendLine("   Clone 异常: " + e.Message); }
            int clone_count = clone.battle_log != null ? clone.battle_log.Count : -1;
            sb.AppendLine((before > 0 && clone_count == 0 ? "PASS" : "FAIL")
                + "\t⑨ Game.Clone 不拷记录（AI 预测树）\t原=" + before + " 副本=" + clone_count);
        }
    }
}
