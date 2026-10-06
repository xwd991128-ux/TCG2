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
    /// 【英雄技能 → 使用卡牌后】用**真实卡池数据**端到端验证（③④ 各用一局，互不污染）：
    /// ① 用户的卡（custom_szVEcbUN「新卡 22」）编译产物：触发器是不是 OnAfterPlay、图在不在；
    /// ② 内置英雄卡自带的「起动式效果入口」能力（= 本项目的"英雄技能"）；
    /// ③ A 局：直接调 EmitActivateAfter（英雄技能结算后）→ 应广播「使用卡牌后」→ 该卡攻击力 1→3；
    /// ④ B 局：真实路径 CastAbility → SelectCard 选目标 → 结算 → 该卡攻击力 1→3；
    /// ⑤ 后台线程（AI 推演线程）调用不得抛 UnityException（未预热时返回 null）；
    /// ⑥ 主线程调用可正常建出并缓存。
    /// 触发：建 tools/heroskill_flag.txt → 进 Play → 写 tools/heroskill_result.txt → 自动删标记。
    /// </summary>
    public class HeroSkillProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/heroskill_result.txt"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/heroskill_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("HeroSkillProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<HeroSkillProbe>();
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
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[英雄技能] 验证完成 → " + OutPath);
        }

        /// <summary>一局的运行环境（两个用例各建一局，避免互相把卡加到 3 再被二次触发）</summary>
        private class Fixture
        {
            public Game game;
            public GameLogic logic;
            public Player p0;
            public Player p1;
            public Card hero;
            public Card minion;
            public Card enemy;
            public AbilityData heroAb;
        }

        private Fixture Setup(CardData ud, CardData heroDef, AbilityData heroAb, string tag)
        {
            Fixture f = new Fixture();
            f.game = new Game("probe_heroskill_" + tag, 2);
            f.game.state = GameState.Play;
            f.logic = new GameLogic(f.game);
            f.p0 = f.game.players[0];
            f.p1 = f.game.players[1];
            f.p0.hp_max = 30; f.p0.hp = 30; f.p0.mana_max = 20; f.p0.mana = 20;
            f.p1.hp_max = 30; f.p1.hp = 30;
            f.hero = Card.Create(heroDef, null, f.p0, "hs_hero_" + tag);
            f.p1.hero = Card.Create(heroDef, null, f.p1, "hs_ehero_" + tag);
            f.heroAb = heroAb;

            f.minion = Card.Create(ud, null, f.p0, "hs_minion_" + tag);
            f.minion.slot = new Slot(1, 1, f.p0.player_id);
            f.p0.cards_board.Add(f.minion);

            f.enemy = Card.Create(ud, null, f.p1, "hs_enemy_" + tag);
            f.enemy.slot = new Slot(1, 1, f.p1.player_id);
            f.p1.cards_board.Add(f.enemy);
            return f;
        }

        private void Run()
        {
            // ---- ① 用户的卡：编译产物 ----
            CardData ud = CardData.Get("custom_szVEcbUN");
            sb.AppendLine("① 用户卡 custom_szVEcbUN = " + (ud == null ? "未注册（卡池没导入？）"
                : (ud.title + " type=" + ud.type + " 能力数=" + (ud.abilities != null ? ud.abilities.Length : -1))));
            if (ud != null && ud.abilities != null)
                foreach (AbilityData a in ud.abilities)
                {
                    if (a == null) { sb.AppendLine("   （空能力槽）"); continue; }
                    sb.AppendLine("   能力 id=" + a.id + " trigger=" + a.trigger + " target=" + a.target
                        + " effects=" + (a.effects != null ? a.effects.Length : -1));
                    if (a.effects != null)
                        foreach (EffectData eff in a.effects)
                        {
                            EffectRunGraph rg = eff as EffectRunGraph;
                            sb.AppendLine(rg != null
                                ? "      EffectRunGraph trigger_action=" + rg.trigger_action
                                  + " graph=" + (rg.graph != null ? ("有 节点" + rg.graph.nodes.Count + " 连线" + rg.graph.links.Count) : "null")
                                : "      effect=" + (eff != null ? eff.GetType().Name : "null"));
                        }
                }

            // ---- ② 英雄卡与其起动式能力 ----
            List<CardData> heroes = new List<CardData>();
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Hero)
                    heroes.Add(c);
            sb.AppendLine("② 英雄卡数=" + heroes.Count);

            CardData heroDef = null;
            AbilityData heroAb = null;
            if (heroes.Count > 0)
            {
                heroDef = heroes[0];
                sb.AppendLine("   英雄=" + heroDef.id + "/" + heroDef.title
                    + " skills=" + (heroDef.skills != null ? heroDef.skills.Count : -1)
                    + " 能力数=" + (heroDef.abilities != null ? heroDef.abilities.Length : -1));
                if (heroDef.abilities != null)
                    foreach (AbilityData a in heroDef.abilities)
                    {
                        if (a == null) continue;
                        sb.AppendLine("     能力 id=" + a.id + " trigger=" + a.trigger + " target=" + a.target);
                        if (heroAb == null && a.trigger == AbilityTrigger.Activate)
                            heroAb = a;
                    }
            }
            if (ud == null || heroDef == null || heroAb == null)
            {
                sb.AppendLine("SKIP：缺用户卡/英雄卡/英雄起动式能力");
                return;
            }

            MethodInfo resolve = typeof(GameLogic).GetMethod("ResolveSkillDefForUse",
                BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo emit = typeof(GameLogic).GetMethod("EmitActivateAfter",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // ---- ③ A 局：直接广播「使用卡牌后」----
            Fixture fa = Setup(ud, heroDef, heroAb, "a");
            try
            {
                CardData def = resolve != null ? resolve.Invoke(fa.logic, new object[] { fa.hero, heroAb }) as CardData : null;
                sb.AppendLine("   技能卡上下文定义=" + (def == null ? "null（❌ 英雄技能没被当成牌）"
                    : (def.id + " type=" + def.type + " title=" + def.title)));
            }
            catch (Exception e) { sb.AppendLine("   ResolveSkillDefForUse 异常: " + e.Message); }

            int b3 = fa.minion.GetAttack();
            try { emit.Invoke(fa.logic, new object[] { fa.hero, heroAb }); }
            catch (Exception e) { sb.AppendLine("   EmitActivateAfter 异常: " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
            fa.logic.UpdateOngoing();
            int a3 = fa.minion.GetAttack();
            sb.AppendLine((a3 == 3 ? "PASS" : "FAIL")
                + "\t③ A局直接广播「使用卡牌后」→ 该卡攻击力 " + b3 + "→3\t实际=" + b3 + "→" + a3
                + " 增益数=" + (fa.minion.buffs != null ? fa.minion.buffs.Count : -1));

            // 再触发一次：同一张卡只应维持 3（"设置为3"必须幂等）
            try { emit.Invoke(fa.logic, new object[] { fa.hero, heroAb }); } catch { }
            fa.logic.UpdateOngoing();
            sb.AppendLine((fa.minion.GetAttack() == 3 ? "PASS" : "FAIL")
                + "\t③b 再次触发（同一增益二次施加）→ 攻击力仍是 3\t实际=" + fa.minion.GetAttack());

            // ---- ④ B 局：真实路径 CastAbility → SelectCard ----
            Fixture fb = Setup(ud, heroDef, heroAb, "b");
            int b4 = fb.minion.GetAttack();
            try
            {
                fb.logic.CastAbility(fb.hero, fb.heroAb);
                sb.AppendLine("④ CastAbility 后 selector=" + fb.game.selector + " 攻击=" + fb.minion.GetAttack());
                int guard = 0;
                while (fb.game.selector != SelectorType.None && guard++ < 6)
                    fb.logic.SelectCard(fb.enemy);
                sb.AppendLine("   选完目标 selector=" + fb.game.selector);
            }
            catch (Exception ex)
            {
                sb.AppendLine("④ CastAbility/SelectCard 异常: "
                    + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
            }
            fb.logic.UpdateOngoing();
            int a4 = fb.minion.GetAttack();
            sb.AppendLine((a4 == 3 ? "PASS" : "FAIL")
                + "\t④ B局真机路径（CastAbility→选目标→结算）→ 攻击力 " + b4 + "→3\t实际=" + b4 + "→" + a4
                + " 增益数=" + (fb.minion.buffs != null ? fb.minion.buffs.Count : -1));

            List<GraphEventContext> log = fb.logic.GetEventLog();
            string acts = "";
            for (int i = Mathf.Max(0, log.Count - 10); i < log.Count; i++)
                acts += (log[i] != null ? log[i].action : "?") + " | ";
            sb.AppendLine("   最近事件: " + acts);

            // ---- ⑤ 后台线程（AI 推演线程）调用不得抛异常 ----
            FieldInfo fdefs = typeof(GameLogic).GetField("hero_skill_synth_defs",
                BindingFlags.NonPublic | BindingFlags.Instance);
            object dict = fdefs != null ? fdefs.GetValue(fb.logic) : null;
            MethodInfo clear = dict != null ? dict.GetType().GetMethod("Clear") : null;
            if (clear != null) clear.Invoke(dict, null);   //模拟"未预热"

            string bg = "未执行";
            System.Threading.Thread th = new System.Threading.Thread(() =>
            {
                try
                {
                    CardData d = resolve != null ? resolve.Invoke(fb.logic, new object[] { fb.hero, fb.heroAb }) as CardData : null;
                    bg = d == null ? "OK（返回 null，未在后台线程建 SO）" : ("❌ 后台线程竟然建出了 " + d.id);
                }
                catch (Exception e)
                {
                    bg = "❌ 抛异常：" + (e.InnerException != null ? e.InnerException.Message : e.Message);
                }
            });
            th.Start();
            th.Join();
            sb.AppendLine((bg.StartsWith("OK") ? "PASS" : "FAIL") + "\t⑤ 后台线程调用（AI 推演线程）\t" + bg);

            // ---- ⑥ 主线程可正常建出并缓存 ----
            try
            {
                CardData d2 = resolve != null ? resolve.Invoke(fb.logic, new object[] { fb.hero, fb.heroAb }) as CardData : null;
                sb.AppendLine((d2 != null ? "PASS" : "FAIL") + "\t⑥ 主线程调用可正常建出并缓存\t"
                    + (d2 != null ? d2.id + " type=" + d2.type : "null"));
            }
            catch (Exception e) { sb.AppendLine("FAIL\t⑥ 主线程调用异常：" + e.Message); }
        }
    }
}
