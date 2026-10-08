using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【光环「生效区域」= 牌库/墓地 时是否真的生效】
    /// 旧实现：CollectAuraGrants 只扫 **英雄/战场/装备区/手牌**，而「生效区域」判定 IsCardInZone
    /// 与目标侧 AddAuraTargetsByName **都支持** 牌库/墓地/奥秘区/暂存区 → 两侧**不对称**：
    /// "作用区域=墓地"配得出效果，但"生效区域=墓地/牌库"的载体**永远采集不到 → 光环静默失效**。
    /// 断言：⓪ 正向对照（战场，修复前就支持）——先证明夹具真的接上了；
    ///       ① 载体在墓地 + 生效区域=墓地 → 生效；② 载体在牌库 + 生效区域=牌库 → 生效；
    ///       ③ 对照：载体在墓地但生效区域=战场 → 不生效（证明不是"无脑全生效"）。
    /// ★夹具配方必须与 tools/probe/AuraRuntimeProbe.cs 一致：BuffPoolIO.New() 造增益 +
    ///   AbilityData 登记进全局能力表 + card.AddAbility(ab)。否则 GetAbilities() 按 id 还原成 null，
    ///   光环压根不会被采集 —— 那时"不生效"类断言会全部**假 PASS**（本次就踩过）。
    /// 触发：建 tools/aura_zone_flag.txt → 进 Play → 写 tools/aura_zone_result.tsv → 自动删标记。
    /// </summary>
    public class AuraZoneSourceProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/aura_zone_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/aura_zone_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("AuraZoneSourceProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<AuraZoneSourceProbe>();
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
            Debug.Log("[光环生效区域] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        /// <summary>造"具名增益型光环"能力（与 CardPoolIO.BuildAuraBuffAbility 产物同形态），并登记进全局能力表</summary>
        private static AbilityData MakeAura(string group, string buff_id, string live_zone, string target_zone)
        {
            AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
            ab.id = "probe_aura_az_" + group;
            ab.trigger = AbilityTrigger.Ongoing;
            ab.aura_group = group;
            ab.aura_buff = buff_id;
            ab.aura_zone = live_zone;            //载体必须处于的区域
            ab.aura_target_zone = target_zone;   //被施加增益的卡所在区域
            ab.aura_repeat = false;
            ab.effects = new EffectData[0];
            ab.conditions_target = new ConditionData[0];
            ab.conditions_trigger = new ConditionData[0];
            ab.filters_target = new FilterData[0];
            ab.status = new StatusData[0];
            ab.chain_abilities = new AbilityData[0];
            //★登记进全局能力表（Card.GetAbilities() 缓存为空时按 id 走 AbilityData.Get() 还原；
            //  不登记就还原成 null → 光环静默跳过）
            AbilityData.ability_list.Add(ab);
            AbilityData.ability_dict[ab.id] = ab;
            return ab;
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            Game game = new Game("probe_aura_zone", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0];

            CardData cd = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Character) { cd = c; break; }
            if (cd == null)
            {
                sb.AppendLine("SKIP\t缺少角色卡定义（卡池未装载）\t");
                return;
            }

            //增益定义（必须走 New()：BuffPoolIO 按 id 索引，New 里已登记；自造 id 会让光环施加被静默跳过）
            BuffData def = BuffPoolIO.New();
            def.title = "光环生效区域探针增益";
            def.props = new List<BuffProp> { new BuffProp(BuffRuntime.ATK_KEY, 2) };
            def.duration = 0;

            //---- ⓪ 正向对照：战场载体 + 战场目标（修复前就支持的路径）----
            string g0 = "probe_az_board";
            Card src0 = Card.Create(cd, null, p0);
            Card tgt0 = Card.Create(cd, null, p0);
            p0.cards_board.Add(src0);
            p0.cards_board.Add(tgt0);
            src0.AddAbility(MakeAura(g0, def.id, "战场", "战场"));
            logic.SyncPassiveEffects();
            CardBuff b0 = BuffRuntime.GetAuraBuff(tgt0, src0.uid, g0);
            Check("⓪ 正向对照：战场载体 → 战场目标生效", b0 != null,
                "增益=" + (b0 != null ? b0.buff_id : "null")
                + "（这一条是夹具自检：它 FAIL 说明夹具没接上，后面所有\"不生效\"断言都不可信）");

            //---- ① 载体在墓地 + 生效区域=墓地 ----
            string g1 = "probe_az_discard";
            Card src1 = Card.Create(cd, null, p0);
            Card tgt1 = Card.Create(cd, null, p0);
            p0.cards_discard.Add(src1);
            p0.cards_discard.Add(tgt1);
            src1.AddAbility(MakeAura(g1, def.id, "墓地", "墓地"));
            logic.SyncPassiveEffects();
            CardBuff b1 = BuffRuntime.GetAuraBuff(tgt1, src1.uid, g1);
            Check("① 载体在墓地 + 生效区域=墓地 → 生效", b1 != null,
                "增益=" + (b1 != null ? b1.buff_id : "null")
                + "（旧实现：扫源不含墓地 → 永远采集不到，光环静默失效）");

            //---- ② 载体在牌库 + 生效区域=牌库 ----
            string g2 = "probe_az_deck";
            Card src2 = Card.Create(cd, null, p0);
            Card tgt2 = Card.Create(cd, null, p0);
            p0.cards_deck.Add(src2);
            p0.cards_deck.Add(tgt2);
            src2.AddAbility(MakeAura(g2, def.id, "牌库", "牌库"));
            logic.SyncPassiveEffects();
            CardBuff b2 = BuffRuntime.GetAuraBuff(tgt2, src2.uid, g2);
            Check("② 载体在牌库 + 生效区域=牌库 → 生效", b2 != null,
                "增益=" + (b2 != null ? b2.buff_id : "null"));

            //---- ③ 对照：载体在墓地但生效区域=战场 → 不该生效 ----
            string g3 = "probe_az_wrong";
            Card src3 = Card.Create(cd, null, p0);
            Card tgt3 = Card.Create(cd, null, p0);
            p0.cards_discard.Add(src3);
            p0.cards_discard.Add(tgt3);
            src3.AddAbility(MakeAura(g3, def.id, "战场", "墓地"));
            logic.SyncPassiveEffects();
            CardBuff b3 = BuffRuntime.GetAuraBuff(tgt3, src3.uid, g3);
            Check("③ 对照：生效区域=战场但载体在墓地 → 不生效", b3 == null,
                "误施加=" + (b3 != null) + "（必须 False；配合 ⓪ 才有意义）");
        }
    }
}
