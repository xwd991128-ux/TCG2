using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;

namespace TcgEngine.Probe
{
    /// <summary>【Effects 旧式效果的"失败路径"必须留痕】
    /// 背景：规则图动作节点已有 60+ 处 "[NodeDoc] … 失败：…" 告警，但这套旧式 EffectData 没跟上同一约定：
    /// 有的是**直接 NRE**（更响），有的是**静默 return**（"技能放了个寂寞"，日志一句没有）。
    /// 断言（全部用 Application.logMessageReceived 抓告警）：
    /// ① 对 null 玩家造成伤害 → 不许抛异常，且要有告警（修复前：DamagePlayer 直接 NRE）
    /// ② 抽指定类型：牌库为空 → 要有告警（修复前无声）
    /// ③ 抽指定类型：牌库里没有该类型 → 要有告警（修复前无声）
    /// ④ Exile 目标为空 → 要有告警（修复前无声）
    /// ⑤ 授予英雄能力：该玩家没有英雄 → 要有告警（修复前无声）
    /// ⑥ 替换英雄能力：该玩家没有英雄 → 要有告警（修复前无声）
    /// ⑦ 摧毁装备：目标不是装备且没有附着装备 → 要有告警（修复前无声）
    /// ⑧ 对照：正常的造成伤害 / 正常的摧毁装备 → **不该**出现失败告警（防"到处乱告警"）
    /// 触发：建 tools/effectfail_flag.txt → 进 Play → 写 tools/effectfail_result.tsv → 自动删标记。
    /// </summary>
    public class EffectFailPathProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/effectfail_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/effectfail_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("EffectFailPathProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<EffectFailPathProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();
        private readonly List<string> warns = new List<string>();

        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                warns.Add((msg ?? "") + "|" + type);
        }

        private bool HasWarn(string keyword)
        {
            for (int i = 0; i < warns.Count; i++)
                if (warns[i].Contains(keyword))
                    return true;
            return false;
        }

        private string WarnSample()
        {
            if (warns.Count == 0)
                return "（0 条告警）";
            string s = warns[0];
            return "（" + warns.Count + " 条，首条：" + (s.Length > 70 ? s.Substring(0, 70) + "…" : s) + "）";
        }

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
            Debug.Log("[效果失败路径] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            CardData heroData = null;
            CardData equipData = null;
            List<CardData> chars = new List<CardData>();
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (heroData == null && c.type == CardType.Hero) heroData = c;
                if (equipData == null && c.IsEquipment()) equipData = c;
                if (c.type == CardType.Character) chars.Add(c);
            }
            if (heroData == null || chars.Count < 2)
            {
                sb.AppendLine("SKIP\t缺少英雄/角色卡定义\t");
                return;
            }

            Game g = new Game("probe_effectfail", 2);
            g.state = GameState.Play;
            g.current_player = 0;
            GameLogic logic = new GameLogic(g);
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);
            p0.hero = Card.Create(heroData, null, p0, "ef_h0");
            p1.hero = Card.Create(heroData, null, p1, "ef_h1");
            p0.hp_max = 30; p0.hp = 30;

            Card caster = Card.Create(chars[0], null, p0, "ef_caster");
            p0.cards_board.Add(caster);

            //★必须给一个**真的 AbilityData**：EffectDamage 会读 ability.value，
            //  传 null 的话 NRE 出在"读 ability"上，就分不清"目标为 null 没守卫"这条了。
            AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.value = 3;

            //---- ① 对 null 玩家造成伤害 ----
            {
                EffectDamage eff = ScriptableObject.CreateInstance<EffectDamage>();
                warns.Clear();
                bool threw = false;
                string ex = "";
                try { eff.DoEffect(logic, ability, caster, (Player)null); }
                catch (Exception e) { threw = true; ex = e.GetType().Name + ": " + e.Message; }
                Check("① 对 null 玩家造成伤害：不抛异常 + 有告警", !threw && HasWarn("[效果]"),
                    "抛异常=" + threw + (threw ? "（" + ex + "）" : "") + " 有[效果]告警=" + HasWarn("[效果]") + WarnSample());
            }

            //---- ② 抽指定类型：牌库为空 ----
            {
                EffectDrawType eff = ScriptableObject.CreateInstance<EffectDrawType>();
                eff.card_type = CardType.Spell;
                p0.cards_deck.Clear();
                warns.Clear();
                eff.DoEffect(logic, ability, caster, p0);
                Check("② 检索抽牌：牌库为空要有告警", HasWarn("[抽牌]"),
                    "有[抽牌]告警=" + HasWarn("[抽牌]") + " 牌库=" + p0.cards_deck.Count + WarnSample());
            }

            //---- ③ 抽指定类型：牌库里没有该类型 ----
            {
                EffectDrawType eff = ScriptableObject.CreateInstance<EffectDrawType>();
                eff.card_type = CardType.Hero;                 //牌库里放的是 Character，一定不匹配
                Card deckcard = Card.Create(chars[1], null, p0, "ef_deckcard");
                p0.cards_deck.Clear();
                p0.cards_deck.Add(deckcard);
                warns.Clear();
                eff.DoEffect(logic, ability, caster, p0);
                Check("③ 检索抽牌：牌库无该类型要有告警", HasWarn("[抽牌]"),
                    "有[抽牌]告警=" + HasWarn("[抽牌]") + " 牌库=" + p0.cards_deck.Count + WarnSample());
            }

            //---- ④ Exile 目标为空 ----
            {
                EffectExile eff = ScriptableObject.CreateInstance<EffectExile>();
                warns.Clear();
                bool threw = false;
                try { eff.DoEffect(logic, ability, caster, (Card)null); }
                catch (Exception e) { threw = true; }
                Check("④ Exile 目标为空要有告警", !threw && HasWarn("[效果]"),
                    "抛异常=" + threw + " 有[效果]告警=" + HasWarn("[效果]") + WarnSample());
            }

            //---- ⑤ 授予英雄能力：该玩家没有英雄 ----
            {
                EffectAddHeroAbility eff = ScriptableObject.CreateInstance<EffectAddHeroAbility>();
                Card hero_backup = p1.hero;
                p1.hero = null;
                warns.Clear();
                bool threw = false;
                try { eff.DoEffect(logic, ability, caster, p1); }
                catch (Exception e) { threw = true; }
                p1.hero = hero_backup;
                Check("⑤ 授予英雄能力：无英雄要有告警", !threw && HasWarn("[效果]"),
                    "抛异常=" + threw + " 有[效果]告警=" + HasWarn("[效果]") + WarnSample());
            }

            //---- ⑥ 替换英雄能力：该玩家没有英雄 ----
            {
                EffectSetHeroAbility eff = ScriptableObject.CreateInstance<EffectSetHeroAbility>();
                Card hero_backup = p1.hero;
                p1.hero = null;
                warns.Clear();
                bool threw = false;
                try { eff.DoEffect(logic, ability, caster, p1); }
                catch (Exception e) { threw = true; }
                p1.hero = hero_backup;
                Check("⑥ 替换英雄能力：无英雄要有告警", !threw && HasWarn("[效果]"),
                    "抛异常=" + threw + " 有[效果]告警=" + HasWarn("[效果]") + WarnSample());
            }

            //---- ⑦ 摧毁装备：目标不是装备且没有附着装备 ----
            {
                EffectDestroyEquip eff = ScriptableObject.CreateInstance<EffectDestroyEquip>();
                Card plain = Card.Create(chars[1], null, p1, "ef_plain");
                p1.cards_board.Add(plain);                     //进战场，但不是装备、也没有 equipped_uid
                plain.equipped_uid = null;
                warns.Clear();
                bool threw = false;
                try { eff.DoEffect(logic, ability, caster, plain); }
                catch (Exception e) { threw = true; }
                Check("⑦ 摧毁装备：没有可摧毁的装备要有告警", !threw && HasWarn("[效果]"),
                    "抛异常=" + threw + " 有[效果]告警=" + HasWarn("[效果]") + WarnSample());
            }

            //---- ⑧ 对照：正常伤害 / 正常摧毁装备 不该出现失败告警 ----
            {
                Card victim = Card.Create(chars[1], null, p1, "ef_victim");
                p1.cards_board.Add(victim);
                victim.hp = 20; victim.damage = 0;
                warns.Clear();
                ScriptableObject.CreateInstance<EffectDamage>().DoEffect(logic, ability, caster, victim);  //正常打 3 点
                int hp_after = victim.GetHP();
                bool no_fail_warn = !HasWarn("[效果]");

                bool equip_ok = true;
                string equip_note = "（本卡池没有装备卡 → 跳过装备对照）";
                if (equipData != null)
                {
                    Card real_equip = Card.Create(equipData, null, p1, "ef_equip");
                    p1.cards_equip.Add(real_equip);
                    warns.Clear();
                    ScriptableObject.CreateInstance<EffectDestroyEquip>().DoEffect(logic, ability, caster, real_equip);
                    bool gone = !g.IsEquipped(real_equip);
                    bool quiet = !HasWarn("[效果]");
                    equip_ok = gone && quiet;
                    equip_note = "装备卡=" + equipData.id + " 已被摧毁=" + gone + " 正常路径无告警=" + quiet;
                }

                Check("⑧ 对照：正常路径不产生失败告警", no_fail_warn && hp_after == 17 && equip_ok,
                    "正常伤害后 HP=" + hp_after + "（期望 17） 无[效果]告警=" + no_fail_warn + " " + equip_note);
            }
        }
    }
}
