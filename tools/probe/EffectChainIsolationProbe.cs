using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;

namespace TcgEngine.Probe
{
    /// <summary>【能力链：单个效果出错不许中断整条链】
    /// 背景：审计查出 20+ 个旧式 Effect 在"目标为 null"时**直接解引用抛 NRE**；而
    /// `AbilityData.DoEffects(...)` 是 `foreach { effect?.DoEffect(...) }` —— 一个抛出，
    /// **后面所有效果（以及调用方后续结算）全部不执行**。逐个 Effect 加守卫既改不完、也挡不住以后新写的。
    /// 工程既有哲学（图事件 EmitGraphEvent / 规则图 NodeDoc）都是"异常隔离 + 留痕，坏效果不影响对局"，
    /// 这一层应该同口径。
    ///
    /// 断言：
    /// ① 链首一个"必抛"的自定义效果 → 不向外抛异常、留告警、**链上后面的效果照常执行**
    /// ② 真实的旧式效果（EffectAddStatus 目标为 null）→ 同样不许把链打断
    /// ③ 对照：整条链都正常时 → 效果生效且**不产生** `[效果]` 失败告警（防"到处乱告警"）
    /// 触发：建 tools/effectchain_flag.txt → 进 Play → 写 tools/effectchain_result.tsv → 自动删标记。
    /// </summary>
    public class EffectChainIsolationProbe : MonoBehaviour
    {
        /// <summary>探针自用的"必抛"效果：模拟旧式 Effect 在目标为 null 时直接解引用</summary>
        private class ThrowingEffect : EffectData
        {
            public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
            {
                throw new NullReferenceException("（探针）模拟旧式 Effect 目标为 null 直接解引用");
            }
        }

        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/effectchain_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/effectchain_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("EffectChainIsolationProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<EffectChainIsolationProbe>();
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

        private bool HasWarn(string kw)
        {
            for (int i = 0; i < warns.Count; i++)
                if (warns[i].Contains(kw)) return true;
            return false;
        }

        private string WarnSample()
        {
            if (warns.Count == 0) return "（0 条告警）";
            string s = warns[0];
            return "（" + warns.Count + " 条，首条：" + (s.Length > 80 ? s.Substring(0, 80) + "…" : s) + "）";
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
            Debug.Log("[效果链隔离] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
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
            if (heroData == null || chars.Count < 2)
            {
                sb.AppendLine("SKIP\t缺少英雄/角色卡定义\t");
                return;
            }

            Game g = new Game("probe_effectchain", 2);
            g.state = GameState.Play;
            g.current_player = 0;
            GameLogic logic = new GameLogic(g);
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);
            p0.hero = Card.Create(heroData, null, p0, "ec_h0");
            p1.hero = Card.Create(heroData, null, p1, "ec_h1");
            Card caster = Card.Create(chars[0], null, p0, "ec_caster");
            p0.cards_board.Add(caster);

            //"链上后面的效果"：对 victim 造成 3 点伤害（能不能执行 = 链有没有被前面的异常打断）
            Card victim = Card.Create(chars[1], null, p1, "ec_victim");
            p1.cards_board.Add(victim);
            victim.hp = 20; victim.damage = 0;

            //---- ① 链首放一个"必抛"效果 ----
            {
                AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
                ab.value = 3;
                ab.effects = new EffectData[]
                {
                    ScriptableObject.CreateInstance<ThrowingEffect>(),
                    ScriptableObject.CreateInstance<EffectDamage>(),
                };
                victim.hp = 20; victim.damage = 0;
                warns.Clear();
                bool threw = false;
                string ex = "";
                try { ab.DoEffects(logic, caster, victim); }
                catch (Exception e) { threw = true; ex = e.GetType().Name; }
                int hp = victim.GetHP();
                Check("① 链首效果抛异常：不向外抛 + 留告警 + 后续效果照常执行", !threw && hp == 17 && HasWarn("[效果]"),
                    "向外抛异常=" + threw + (threw ? "(" + ex + ")" : "") + " victim HP=" + hp
                    + "（期望 17 ⇒ 后面的 EffectDamage 执行了） 有[效果]告警=" + HasWarn("[效果]") + WarnSample());
            }

            //---- ② 真实的旧式效果（EffectAddStatus）目标为 null ----
            {
                AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
                ab.value = 3;
                ab.effects = new EffectData[]
                {
                    ScriptableObject.CreateInstance<EffectAddStatus>(),   //旧式效果：目标为 null 时会解引用
                    ScriptableObject.CreateInstance<EffectDamage>(),
                };
                victim.hp = 20; victim.damage = 0;
                warns.Clear();
                bool threw = false;
                string ex = "";
                try { ab.DoEffects(logic, caster, (Card)null); }        //目标为 null
                catch (Exception e) { threw = true; ex = e.GetType().Name; }
                Check("② 真实旧效果 + null 目标：不许把链打断", !threw,
                    "向外抛异常=" + threw + (threw ? "(" + ex + ")" : "") + WarnSample());
            }

            //---- ③ 对照：整条链都正常 → 效果生效且不产生 [效果] 告警 ----
            {
                AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
                ab.value = 3;
                ab.effects = new EffectData[] { ScriptableObject.CreateInstance<EffectDamage>() };
                victim.hp = 20; victim.damage = 0;
                warns.Clear();
                ab.DoEffects(logic, caster, victim);
                int hp = victim.GetHP();
                Check("③ 对照：正常链生效且无失败告警", hp == 17 && !HasWarn("[效果]"),
                    "victim HP=" + hp + "（期望 17） 无[效果]告警=" + !HasWarn("[效果]") + WarnSample());
            }
        }
    }
}
