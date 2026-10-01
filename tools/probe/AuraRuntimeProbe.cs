using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【光环（具名增益）运行期实测】自建 Game/GameLogic（不依赖起局），验证 5 条断言：
    ///   1) 目标在「作用区域」→ 光环给它施加一份**带来源标记**的增益实例
    ///   2) 目标离开「作用区域」→ 该来源的实例被**精确移除**
    ///   3) 载体离场 → 它给的光环增益被移除
    ///   4) 手动施加的**同名**增益（无来源标记）不会被光环移除误删
    ///   5) 「生效区域」限制：载体不在生效区域 → 不给任何目标施加
    /// 判定口径与编译侧一致：能力形如 CardPoolIO.BuildAuraBuffAbility 的产物
    /// （trigger=Ongoing + aura_group/aura_buff/aura_zone/aura_target_zone）。
    /// 触发：建 tools/aura_probe_flag.txt → 进 Play 一次 → 写 tools/aura_probe_result.tsv → 自动删标记。
    /// </summary>
    public class AuraRuntimeProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/aura_probe_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/aura_probe_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath))
                return;
            GameObject go = new GameObject("AuraRuntimeProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<AuraRuntimeProbe>();
        }

        private int frames;
        private bool done;
        private int pass, fail;

        private void Update()
        {
            if (done)
                return;
            frames++;
            if (frames < 90)
                return;   //等 DataLoader/卡池/增益池装载完
            done = true;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("结果\t断言\t说明");
            try { Run(sb); }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION\t" + e.GetType().Name + "\t" + (e.Message ?? "").Replace("\n", " ").Replace("\r", " "));
                Debug.LogError("[光环实测] 异常: " + e);
            }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[光环实测] 完成：" + OutPath + "（PASS=" + pass + " FAIL=" + fail + "）");
        }

        private void Check(StringBuilder sb, string name, bool ok, string detail)
        {
            if (ok) pass++; else fail++;
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private void Run(StringBuilder sb)
        {
            Game g = new Game("probe_aura", 2);
            GameLogic logic = new GameLogic(g);   //★构造函数需要 Game（内部同时建好 game_data 与 resolve_queue）
            Player p0 = g.players[0];

            //增益定义（运行时新建，进增益池）
            //★ 必须用 New() 生成的 id：BuffPoolIO 以 buff_dict 按 id 索引（New 里已登记），
            //  这里若改写 def.id 会导致 BuffPoolIO.Get() 取不到 → 光环施加被静默跳过（实测踩过）。
            BuffData def = BuffPoolIO.New();
            def.title = "光环实测增益";
            def.props = new List<BuffProp> { new BuffProp(BuffRuntime.ATK_KEY, 3) };
            def.duration = 0;

            CardData cd = CardData.Get("imp");
            if (cd == null)
            {
                sb.AppendLine("SKIP\t卡牌定义 imp 不存在（卡池未装载）\t");
                return;
            }
            Card src = Card.Create(cd, null, p0);
            Card tgt = Card.Create(cd, null, p0);
            if (src == null || tgt == null)
            {
                sb.AppendLine("SKIP\t无法构造卡实例\t");
                return;
            }
            p0.cards_board.Add(src);
            p0.cards_board.Add(tgt);

            //光环能力（= CardPoolIO.BuildAuraBuffAbility 的产物形态）
            AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
            ab.id = "probe_aura_ability";
            ab.trigger = AbilityTrigger.Ongoing;
            ab.aura_group = "ag_probe_aura";
            ab.aura_buff = def.id;
            ab.aura_zone = "战场";          //生效区域 = 载体需在战场
            ab.aura_target_zone = "战场";   //作用区域 = 被施加的卡需在战场
            ab.effects = new EffectData[0];
            ab.conditions_target = new ConditionData[0];
            ab.conditions_trigger = new ConditionData[0];
            ab.filters_target = new FilterData[0];
            ab.status = new StatusData[0];
            ab.chain_abilities = new AbilityData[0];
            //★登记进全局能力表：Card.GetAbilities() 在缓存为空时按 id 走 AbilityData.Get() 还原，
            //  不登记就还原成 null（当初被动线探针同因踩过）。
            AbilityData.ability_list.Add(ab);
            AbilityData.ability_dict[ab.id] = ab;
            src.AddAbility(ab);

            //1) 目标在作用区域 → 施加
            logic.SyncAuraEffects();
            CardBuff got = BuffRuntime.GetAuraBuff(tgt, src.uid, ab.aura_group);
            Check(sb, "进入作用区域施加", got != null,
                got != null ? ("buff=" + got.buff_id + " src=" + got.source_uid)
                            : ("未施加｜能力数=" + src.GetAbilities().Count
                               + " 目标buffs=" + tgt.buffs.Count
                               + " 目标在战场=" + p0.cards_board.Contains(tgt)
                               + " 载具在战场=" + p0.cards_board.Contains(src)
                               + " 定义可查=" + (BuffPoolIO.Get(def.id) != null)
                               + " aura_group=" + ab.aura_group + " aura_buff=" + ab.aura_buff));

            //2) 目标离开作用区域（战场→手牌）→ 精确移除
            p0.cards_board.Remove(tgt);
            p0.cards_hand.Add(tgt);
            logic.SyncAuraEffects();
            Check(sb, "离开作用区域移除", BuffRuntime.GetAuraBuff(tgt, src.uid, ab.aura_group) == null,
                "移到手牌后不应再带该来源的增益");

            //3) 目标回战场；再手动加一份**同名**增益 → 载体离场只删光环那份
            p0.cards_hand.Remove(tgt);
            p0.cards_board.Add(tgt);
            logic.SyncAuraEffects();
            CardBuff manual = BuffRuntime.AddBuff(logic, tgt, def, 0);   //无来源标记 = 手动施加
            Check(sb, "手动同名增益就位", manual != null && !manual.IsFromAura,
                "source_uid=" + (manual != null ? (manual.source_uid ?? "null") : "null"));
            p0.cards_board.Remove(src);                                  //载体离场
            logic.SyncAuraEffects();
            Check(sb, "载体离场移除光环增益", BuffRuntime.GetAuraBuff(tgt, src.uid, ab.aura_group) == null, "光环那份应被移除");
            Check(sb, "手动同名增益不被误删", BuffRuntime.HasBuff(tgt, def.id), "手动那份必须保留");

            //4) 生效区域限制：载体在手牌（生效区域=战场）→ 不施加
            p0.cards_hand.Add(src);   //载体离开战场
            logic.SyncAuraEffects();
            Check(sb, "载体不在生效区域则不施加", BuffRuntime.GetAuraBuff(tgt, src.uid, ab.aura_group) == null,
                "载体在手牌 → 不应施加");
        }

        private static void SetField(object o, string name, object v)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null)
                f.SetValue(o, v);
        }
    }
}
