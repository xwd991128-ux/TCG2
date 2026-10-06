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
    /// 【攻击限制 / 全局入口 运行期实测】断言（全部是"新机制"的核心行为，差分/冒烟探针覆盖不到）：
    ///   A1 接了限制口的卡把图登记成"整局规则"
    ///   A2 带限制的卡打英雄 → 被拒，且原因来自入口的「拒绝提示」
    ///   A3 **另一张没有规则的卡**打英雄 → 同样被拒（整局生效 / 发动主体不受限 —— 这是用户口径的核心）
    ///   A4 打随从 → 允许（限制内容是"只能攻击随从"）
    ///   A5 判定幂等、无异常（连调三次一致）
    ///   B1 没有 GameLogic 的 Game（客户端快照副本语义）也能判定且不抛异常
    ///   B2 作用范围语义：缺字段=仅本卡；显式"全部卡牌"=全局
    ///   B3 旧数据（缺字段）**不进**全局入口表（保护内置 155 卡的迁移图，否则会互相触发）
    /// 触发：建 tools/attack_rule_flag.txt → 进 Play（常驻：Play 中建成即跑）→ 写 tools/attack_rule_result.tsv → 自动删标记。
    /// </summary>
    public class AttackRuleProbe : MonoBehaviour
    {
        private const string GLOBAL_CARD = "custom_3xOuLtJe";   // 用户池里带"整局攻击限制"的卡（新卡 25）

        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/attack_rule_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/attack_rule_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            GameObject go = new GameObject("AttackRuleProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<AttackRuleProbe>();
        }

        private int frames;
        private bool done;
        private int pass, fail;

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath))   // 常驻：标志再次出现 → 复位重跑（Play 期间改代码不用重进）
                {
                    done = false;
                    frames = 0;
                    pass = 0;
                    fail = 0;
                    Debug.Log("[攻击规则实测] 检测到标志 → 重跑");
                }
                return;
            }
            frames++;
            if (frames < 120)
                return;   // 等卡池装载
            done = true;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("结果\t断言\t说明");
            try { Run(sb); }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION\t运行\t" + e.GetType().Name + "\t" + (e.Message ?? "").Replace("\n", " "));
                Debug.LogError("[攻击规则实测] 异常: " + e);
            }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[攻击规则实测] 完成：PASS=" + pass + " FAIL=" + fail + " → " + OutPath);
        }

        private void Check(StringBuilder sb, string name, bool ok, string detail)
        {
            if (ok) pass++; else fail++;
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private void Run(StringBuilder sb)
        {
            // ---------- 夹具：一局两玩家；p0 放"带规则的卡 + 普通卡"，p1 放普通卡 ----------
            Game g = new Game("probe_attackrule", 2);
            GameLogic logic = new GameLogic(g);
            Player p0 = g.players[0];
            Player p1 = g.players[1];

            CardData rule_cd = CardData.Get(GLOBAL_CARD);
            CardData plain_cd = AnyPlainCharacter(rule_cd);
            if (rule_cd == null || plain_cd == null)
            {
                sb.AppendLine("SKIP\t卡池未装载（" + GLOBAL_CARD + " / 普通随从）\t");
                return;
            }

            Card atk_rule = Card.Create(rule_cd, null, p0);
            Card atk_plain = Card.Create(plain_cd, null, p0);
            Card def_plain = Card.Create(plain_cd, null, p1);
            if (atk_rule == null || atk_plain == null || def_plain == null)
            {
                sb.AppendLine("SKIP\t无法构造卡实例\t");
                return;
            }
            atk_rule.exhausted = false;
            atk_plain.exhausted = false;
            def_plain.exhausted = false;
            p0.cards_board.Add(atk_rule);
            p0.cards_board.Add(atk_plain);
            p1.cards_board.Add(def_plain);

            // A1 整局规则登记
            Check(sb, "A1 接了限制口 → 登记为整局攻击规则", CardPoolIO.GlobalAttackGraphs.Count >= 1,
                "GlobalAttackGraphs=" + CardPoolIO.GlobalAttackGraphs.Count);

            // A2 带限制的卡打英雄被拒（原因必须可读）
            string why = null;
            bool ok_hero = g.CanAttackTarget(atk_rule, p1, false, out why);
            Check(sb, "A2 带限制的卡打英雄被拒 + 有原因", !ok_hero && !string.IsNullOrEmpty(why),
                "can=" + ok_hero + " reason=" + (why ?? "(空)"));

            // A3 另一张"没有规则"的卡打英雄也被拒（发动主体不受限 = 整局生效）
            string why2 = null;
            bool other_hero = g.CanAttackTarget(atk_plain, p1, false, out why2);
            Check(sb, "A3 其它卡打英雄同样被拒（整局生效）", !other_hero,
                "can=" + other_hero + " reason=" + (why2 ?? "(空)"));

            // A4 打随从允许
            string why3 = null;
            bool ok_card = g.CanAttackTarget(atk_rule, def_plain, false, out why3);
            Check(sb, "A4 打随从允许", ok_card, "can=" + ok_card + " reason=" + (why3 ?? "(空)"));

            // A5 幂等 + 无异常
            bool a = g.CanAttackTarget(atk_rule, p1);
            bool b = g.CanAttackTarget(atk_rule, p1);
            bool c = g.CanAttackTarget(atk_rule, p1);
            Check(sb, "A5 判定幂等（连调三次一致）", a == b && b == c, "三次=" + a + "/" + b + "/" + c);

            // B1 客户端快照副本（Game 上没有 GameLogic）也要能判定、不抛异常
            Game client_copy = new Game("probe_attackrule_client", 2);
            Card c_atk = Card.Create(rule_cd, null, client_copy.players[0]);
            bool client_reject = false;
            string cerr = "";
            try
            {
                client_copy.players[0].cards_board.Add(c_atk);
                c_atk.exhausted = false;
                string w = null;
                client_reject = !client_copy.CanAttackTarget(c_atk, client_copy.players[1], false, out w);
            }
            catch (Exception e) { cerr = e.GetType().Name + ":" + e.Message; }
            Check(sb, "B1 无逻辑上下文（客户端副本）可判定且不抛异常", client_reject && string.IsNullOrEmpty(cerr),
                "被拒=" + client_reject + (string.IsNullOrEmpty(cerr) ? "" : (" 异常=" + cerr)));

            // B2 作用范围语义
            GraphNode legacy = new GraphNode();
            legacy.type = GraphNodeType.Event;
            legacy.action = "OnAttack";
            GraphNode global = new GraphNode();
            global.type = GraphNodeType.Event;
            global.action = "OnAttack";
            global.fields.Add(new FieldCustomData { name = "scope", value = "全部卡牌" });
            bool legacy_global = CardPoolIO.IsGlobalScopeEntry(legacy);
            bool global_global = CardPoolIO.IsGlobalScopeEntry(global);
            Check(sb, "B2 缺作用范围=仅本卡 / 显式全部卡牌=全局", !legacy_global && global_global,
                "缺字段=" + legacy_global + " 显式=" + global_global);

            // B3 旧数据不进全局入口表（保护内置卡迁移图）
            List<CardPoolIO.GlobalEntry> gl = CardPoolIO.GetGlobalEntries(AbilityTrigger.OnBeforeDefend);
            Check(sb, "B3 旧数据未进全局入口表（保护内置卡）", gl == null || gl.Count == 0,
                "OnBeforeDefend 全局入口=" + (gl == null ? 0 : gl.Count));

            // B4 判定只读：判完不能改状态（UI 每次选卡都会调它）
            int before = atk_rule.status != null ? atk_rule.status.Count : 0;
            g.CanAttackTarget(atk_rule, p1);
            g.CanAttackTarget(atk_plain, def_plain);
            int after = atk_rule.status != null ? atk_rule.status.Count : 0;
            Check(sb, "B4 判定是只读的（状态数不变）", before == after, "before=" + before + " after=" + after);
        }

        private static CardData AnyPlainCharacter(CardData exclude)
        {
            foreach (string id in new string[] { "imp", "fish", "coin", "zombie" })
            {
                CardData cd = CardData.Get(id);
                if (cd != null && cd != exclude && cd.IsCharacter())
                    return cd;
            }
            return null;
        }
    }
}
