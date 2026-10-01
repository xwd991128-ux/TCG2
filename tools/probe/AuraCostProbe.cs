using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【光环→变量→费用 运行期实测】两段：
    ///  A) 通用机制：手牌来源 + 手牌目标 → 施加；变量 → mana_ongoing；移除还原；幂等动作线重跑。
    ///  B) 复现用户真实卡（custom_ZJixIZiYOx4「熔岩人」+ 增益 buff_90f3ca8b）：
    ///     英雄已损失 1 点生命 → 光环动作线把该值写进变量 → 费用应 20-1=19。
    /// 触发：建 tools/aura_cost_flag.txt → 进 Play（AfterSceneLoad 自触发）→ 写 tools/aura_cost_result.tsv → 自动删标记。
    /// </summary>
    public class AuraCostProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/aura_cost_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/aura_cost_flag.txt"); } }

        /// <summary>常驻（**不要求先建标志**）：放进 Assets 后，Play 期间随时建 tools/aura_cost_flag.txt 都会立刻跑，
        /// 不用停 Play / 重进 Play；跑完自动删标志 → 可反复重跑（修 bug 时省掉"停-放标志-重进"三步）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            GameObject go = new GameObject("AuraCostProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<AuraCostProbe>();
        }

        /// <summary>计数用效果：验证"动作线被重跑了几次"</summary>
        private class CountingEffect : EffectData
        {
            public static int calls;
            public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
            {
                calls++;
            }
        }

        private int frames;
        private bool done;
        private int pass, fail;

        private void Update()
        {
            if (done)
            {
                //常驻：标志再次出现 → 复位重跑（Play 期间改完代码/数据不用重进 Play）
                if (File.Exists(FlagPath))
                {
                    done = false;
                    frames = 0;
                    pass = 0;
                    fail = 0;
                    Debug.Log("[光环费用实测] 检测到标志 → 重跑");
                }
                return;
            }
            frames++;
            if (frames < 120)
                return;   //等 DataLoader / 卡池 / 增益池装载
            done = true;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("结果\t断言\t说明");
            try { RunA(sb); } catch (Exception e) { Err(sb, "A", e); }
            try { RunB(sb); } catch (Exception e) { Err(sb, "B", e); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[光环费用实测] 完成：" + OutPath + "（PASS=" + pass + " FAIL=" + fail + "）");
        }

        private void Err(StringBuilder sb, string tag, Exception e)
        {
            sb.AppendLine("EXCEPTION" + tag + "\t" + e.GetType().Name + "\t"
                + (e.Message ?? "").Replace("\n", " ").Replace("\r", " "));
            Debug.LogError("[光环费用实测] " + tag + " 段异常: " + e);
        }

        private void Check(StringBuilder sb, string name, bool ok, string detail)
        {
            if (ok) pass++; else fail++;
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private static CardData AnyCard()
        {
            foreach (string id in new string[] { "imp", "fish", "coin", "crab_mana", "dragon_blue" })
            {
                CardData cd = CardData.Get(id);
                if (cd != null)
                    return cd;
            }
            return null;
        }

        // ==================== A) 通用机制 ====================
        private void RunA(StringBuilder sb)
        {
            Game g = new Game("probe_auracost_a", 2);
            GameLogic logic = new GameLogic(g);
            Player p0 = g.players[0];

            CardData cd = AnyCard();
            if (cd == null)
            {
                sb.AppendLine("SKIP\tA段：卡池未装载\t");
                return;
            }

            BuffData def = BuffPoolIO.New();
            def.title = "费用降低(实测)";
            def.duration = 0;
            def.props = new List<BuffProp>();
            BuffPropMod mod = new BuffPropMod(BuffModTarget.Cost, BuffModMode.Sub, 0);
            mod.value_source = "受伤次数";
            def.mods = new List<BuffPropMod> { mod };
            def.mods_migrated = true;

            Card src = Card.Create(cd, null, p0);
            if (src == null)
            {
                sb.AppendLine("SKIP\tA段：无法构造卡实例\t");
                return;
            }
            p0.cards_hand.Add(src);

            AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
            ab.id = "probe_auracost_ability";
            ab.trigger = AbilityTrigger.Ongoing;
            ab.aura_group = "ag_probe_auracost";
            ab.aura_buff = def.id;
            ab.target = AbilityTarget.AllCardsHand;
            ab.aura_repeat = true;
            CountingEffect.calls = 0;
            ab.effects = new EffectData[] { ScriptableObject.CreateInstance<CountingEffect>() };
            ab.conditions_target = new ConditionData[0];
            ab.conditions_trigger = new ConditionData[0];
            ab.filters_target = new FilterData[0];
            ab.status = new StatusData[0];
            ab.chain_abilities = new AbilityData[0];
            AbilityData.ability_list.Add(ab);
            AbilityData.ability_dict[ab.id] = ab;
            src.AddAbility(ab);

            logic.SyncAuraEffects();
            CardBuff got = BuffRuntime.GetAuraBuff(src, src.uid, ab.aura_group);
            Check(sb, "A1 手牌来源光环给自己施加", got != null,
                got != null ? ("buff=" + got.buff_id)
                            : ("未施加｜载体在手牌=" + p0.cards_hand.Contains(src)));

            int c1 = CountingEffect.calls;
            logic.SyncAuraEffects();
            int c2 = CountingEffect.calls;
            Check(sb, "A2 幂等动作线每次同步重跑", c1 >= 1 && c2 > c1,
                "第1次=" + c1 + " 第2次=" + c2 + "（aura_repeat=true）");

            if (got == null)
                return;

            BuffRuntime.SetPropValue(src, def.id, "受伤次数", 3);
            logic.UpdateOngoing();
            Check(sb, "A3 变量=3 → mana_ongoing=-3", src.mana_ongoing == -3,
                "mana_ongoing=" + src.mana_ongoing + " 费用=" + src.GetMana());

            BuffRuntime.SetPropValue(src, def.id, "受伤次数", 1);
            logic.UpdateOngoing();
            Check(sb, "A4 变量改 1 → mana_ongoing=-1（跟随）", src.mana_ongoing == -1,
                "mana_ongoing=" + src.mana_ongoing);

            BuffRuntime.RemoveBuffInstance(src, got);
            logic.UpdateOngoing();
            Check(sb, "A5 移除后 mana_ongoing=0", src.mana_ongoing == 0,
                "mana_ongoing=" + src.mana_ongoing);
        }

        // ==================== B) 复现用户的真实卡 ====================
        private void RunB(StringBuilder sb)
        {
            const string LAVA_ID = "custom_ZJixIZiYOx4";
            const string LAVA_BUFF = "buff_90f3ca8b";

            CardData lava = CardData.Get(LAVA_ID);
            if (lava == null)
            {
                sb.AppendLine("SKIP\tB段：找不到卡定义 " + LAVA_ID + "（该池未装载）\t");
                return;
            }
            BuffData bdef = BuffPoolIO.Get(LAVA_BUFF);
            if (bdef == null)
            {
                sb.AppendLine("SKIP\tB段：找不到增益 " + LAVA_BUFF + "\t");
                return;
            }

            Game g = new Game("probe_auracost_b", 2);
            GameLogic logic = new GameLogic(g);
            Player p0 = g.players[0];

            Card lavaCard = Card.Create(lava, null, p0);
            if (lavaCard == null)
            {
                sb.AppendLine("SKIP\tB段：无法构造熔岩人\t");
                return;
            }
            p0.cards_hand.Add(lavaCard);
            int base_mana = lavaCard.GetMana();

            //★夹具必须给玩家一个英雄：动作线的取值链是"卡牌拥有者 → 玩家英雄 → 最大/当前生命"，
            //  hero 为空时整条链取 0（实测：变量写成 0）。夹具这里按需补一个英雄卡。
            bool hero_made = false;
            if (p0.hero == null)
            {
                //★必须是**英雄类型**卡：图里对英雄卡读"生命/最大生命"要按所属玩家回退
                //  （HeroMaxHpForRead/CardHpForRead 以 CardData.type == Hero 判定）
                CardData heroData = null;
                foreach (CardData c2 in CardData.GetAll())
                {
                    if (c2 != null && c2.type == CardType.Hero)
                    {
                        heroData = c2;
                        break;
                    }
                }
                p0.hero = Card.Create(heroData != null ? heroData : AnyCard(), null, p0);
                hero_made = true;
            }
            //★夹具按**真实对局口径**：玩家血量存在 Player.hp/hp_max 上（伤害走 DamagePlayer），
            //  英雄**卡**自己的 hp/damage 不会随之变化 —— 这正是原实现算不出值的原因。
            p0.hp_max = 30;
            p0.hp = 29;
            sb.AppendLine("INFO\tB段夹具\t英雄卡=" + (p0.hero != null ? (hero_made ? "新建" : "已有") : "无")
                + " 玩家血量=" + p0.hp + "/" + p0.hp_max + "（血量在玩家身上，英雄卡字段不动）\t");

            logic.SyncAuraEffects();

            CardBuff lb = null;
            foreach (CardBuff cb in lavaCard.buffs)
            {
                if (cb != null && cb.buff_id == LAVA_BUFF)
                    lb = cb;
            }
            Check(sb, "B1 光环给自己施加了「费用降低」", lb != null,
                lb != null ? ("来源=" + lb.source_uid + " 分组=" + lb.source_group)
                           : ("未施加｜手牌数=" + p0.cards_hand.Count));

            int var_val = lb != null ? lb.GetProp("费用降低") : -999;
            Check(sb, "B2 动作线写入变量「费用降低」=已损失生命(1)", var_val == 1,
                "变量值=" + var_val + "（期望 1）");

            logic.UpdateOngoing();
            int now = lavaCard.GetMana();
            Check(sb, "B3 熔岩人费用 = " + base_mana + "-1 = " + (base_mana - 1), now == base_mana - 1,
                "当前费用=" + now + " mana_ongoing=" + lavaCard.mana_ongoing);

            //再掉 1 点血 → 费用应再降 1（验证按当前状态重算）
            p0.hp = 28;
            logic.SyncAuraEffects();
            logic.UpdateOngoing();
            int now2 = lavaCard.GetMana();
            Check(sb, "B4 再掉 1 点血 → 费用 = " + base_mana + "-2", now2 == base_mana - 2,
                "当前费用=" + now2 + " mana_ongoing=" + lavaCard.mana_ongoing);
        }
    }
}
