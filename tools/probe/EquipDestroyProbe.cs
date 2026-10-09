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
    /// <summary>【"消灭/摧毁"必须走统一入口（含装备卡）】
    /// 断言：
    /// ① 装备区里的卡被"消灭"（EffectDestroy）→ 发的是「死亡时/后」（修复前发「弃牌时/后」）
    /// ② 装备卡被消灭 → 计入击杀数（修复前 0：DiscardCard 不记击杀）
    /// ③ 装备卡被消灭后：不再在装备区、已进墓地
    /// ④ 对照：手牌里的卡被"消灭" → 仍是「弃置」语义（不进战场就不是死亡，不能误发死亡事件）
    /// 触发：建 tools/equipdestroy_flag.txt → 进 Play → 写 tools/equipdestroy_result.tsv → 自动删标记。
    /// </summary>
    public class EquipDestroyProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/equipdestroy_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/equipdestroy_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("EquipDestroyProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<EquipDestroyProbe>();
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
            Debug.Log("[装备消灭] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private static void CountEvents(GameLogic logic, int from, out int death, out int discard, out string names)
        {
            death = 0; discard = 0;
            List<GraphEventContext> log = logic.GetEventLog();
            List<string> seen = new List<string>();
            for (int i = from; i < log.Count; i++)
            {
                string a = log[i].action;
                seen.Add(a);
                if (a == "OnBeforeDeath" || a == "OnAfterDeath") death++;
                else if (a == "OnBeforeDiscard" || a == "OnAfterDiscard") discard++;
            }
            names = string.Join(",", seen.ToArray());
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            List<CardData> chars = new List<CardData>();
            CardData heroData = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (heroData == null && c.type == CardType.Hero) heroData = c;
                if (c.type == CardType.Character) chars.Add(c);
            }
            if (heroData == null || chars.Count < 3)
            {
                sb.AppendLine("SKIP\t缺少英雄 / 至少 3 张角色卡定义\t");
                return;
            }

            Game g = new Game("probe_equipdestroy", 2);
            g.state = GameState.Play;
            g.current_player = 0;
            GameLogic logic = new GameLogic(g);
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);
            p0.hero = Card.Create(heroData, null, p0, "eq_h0");
            p1.hero = Card.Create(heroData, null, p1, "eq_h1");

            //施法者（p0 的场上随从）
            Card caster = Card.Create(chars[0], null, p0, "eq_caster");
            p0.cards_board.Add(caster);

            //被消灭的装备卡（p1 的装备区，并且挂在英雄身上 → IsEquipped 为真）
            Card equip = Card.Create(chars[1], null, p1, "eq_item");
            p1.cards_equip.Add(equip);
            p1.hero.equipped_uid = equip.uid;

            EffectDestroy eff = ScriptableObject.CreateInstance<EffectDestroy>();

            int log0 = logic.GetEventLog().Count;
            int kills0 = p0.kill_count;
            eff.DoEffect(logic, null, caster, equip);

            int death1, discard1;
            string names1;
            CountEvents(logic, log0, out death1, out discard1, out names1);
            Check("① 装备卡被消灭 → 发「死亡时/后」", death1 == 2 && discard1 == 0,
                "死亡事件=" + death1 + "（期望 2） 弃牌事件=" + discard1 + "（期望 0）｜事件=" + names1);
            Check("② 装备卡被消灭 → 计入击杀数", p0.kill_count == kills0 + 1,
                "kill_count " + kills0 + " → " + p0.kill_count + "（期望 +1）");
            Check("③ 装备卡已离开装备区并进墓地", !g.IsEquipped(equip) && g.IsInDiscard(equip),
                "仍在装备区=" + g.IsEquipped(equip) + "（期望 False） 在墓地=" + g.IsInDiscard(equip) + "（期望 True）");

            //---- ④ 对照：手牌里的卡被"消灭" → 仍是弃置语义（没进战场就不能当死亡）----
            Card hand = Card.Create(chars[2], null, p1, "eq_hand");
            p1.cards_hand.Add(hand);
            int log2 = logic.GetEventLog().Count;
            eff.DoEffect(logic, null, caster, hand);
            int death2, discard2;
            string names2;
            CountEvents(logic, log2, out death2, out discard2, out names2);
            Check("④ 对照：手牌里的卡被消灭 → 弃置（不误发死亡）", death2 == 0 && discard2 == 2 && g.IsInDiscard(hand),
                "死亡事件=" + death2 + "（期望 0） 弃牌事件=" + discard2 + "（期望 2） 在墓地=" + g.IsInDiscard(hand)
                + "｜事件=" + names2);
        }
    }
}
