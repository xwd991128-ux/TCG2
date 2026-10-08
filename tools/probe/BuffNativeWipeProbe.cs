using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【增益重算会不会误清"非增益来源"的原生状态】
    /// 背景：BuffRuntime.ReapplyNative 在每次增益增删时**无条件** RemoveStatus(AddAttack/AddHP/Armor/AddManaCost)，
    /// 再按 card.buffs 重新叠加。但这几个状态也可以来自**非增益**途径（规则图「添加状态」「添加关键词」节点 =
    /// EffectAddStatus/EffectAddKeyword 直接 card.AddStatus），重算时无法区分 → 会被一起清掉且不再补回。
    /// ★断言必须落在**状态本身**（HasStatus/GetStatusValue）上，不能只看 GetAttack()：
    ///   GetAttack() 读的是 attack_ongoing，而它只在 UpdateOngoingCards 时折叠状态 —— 不收敛时数值恒为原值，
    ///   会让"加状态/加增益"整组断言全 FAIL（本探针第一版就因此得出完全错误的结论）。
    /// 实测结论（2026-10-09）：② PASS（加增益时 3+1 累加）、**③ FAIL（撤增益把非增益那 3 点一起清掉）**
    ///   ⇒ 两侧口径不一致，是真实缺陷（待定修法：重算只回滚"增益自己贡献的那部分"）。
    /// 触发：建 tools/buffnative_flag.txt → 进 Play → 写 tools/buff_native_result.tsv → 自动删标记。
    /// </summary>
    public class BuffNativeWipeProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/buff_native_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/buffnative_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("BuffNativeWipeProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<BuffNativeWipeProbe>();
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
            Debug.Log("[增益误清] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            Game game = new Game("probe_buffnative", 2);
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

            BuffData def = BuffPoolIO.New();     //必须 New()：BuffPoolIO 按 id 索引
            def.title = "探针攻击加成增益";
            def.props = new List<BuffProp> { new BuffProp(BuffRuntime.ATK_KEY, 1) };
            def.duration = 0;

            //---- ① 非增益来源的原生状态（模拟规则图「添加状态」节点）----
            Card a = Card.Create(cd, null, p0);
            a.AddStatus(StatusType.AddAttack, 3, 0);
            int st0 = a.HasStatus(StatusType.AddAttack) ? a.GetStatusValue(StatusType.AddAttack) : -1;
            Check("① 非增益来源的 +3 攻击加成（状态）可读", st0 == 3,
                "AddAttack 状态值=" + st0 + "（期望 3）");

            //---- ② 施加一个增益（内部会 ReapplyNative）→ 非增益那 +3 不该被清 ----
            BuffRuntime.AddBuff(logic, a, def, 0);
            int st1 = a.HasStatus(StatusType.AddAttack) ? a.GetStatusValue(StatusType.AddAttack) : -1;
            Check("② 施加增益后 非增益的 +3 仍在（不该被重算误清）", st1 >= 3,
                "AddAttack 状态值=" + st1 + "（旧实现：被 ReapplyNative 无条件清成 增益那份=1，"
                + "非增益的 +3 凭空消失且不再补回）");

            //---- ③ 撤掉增益 → 非增益那份仍在 ----
            BuffRuntime.RemoveBuff(a, def.id);
            int st2 = a.HasStatus(StatusType.AddAttack) ? a.GetStatusValue(StatusType.AddAttack) : -1;
            Check("③ 撤增益后 非增益的 +3 仍在", st2 >= 3, "AddAttack 状态值=" + st2 + "（期望 3）");

            //---- ④ 对照：纯增益的 AddAttack 状态 加=1 / 撤=0（证明重算确实在清，只是"清过头"）----
            Card b = Card.Create(cd, null, p0);
            int bs0 = b.HasStatus(StatusType.AddAttack) ? b.GetStatusValue(StatusType.AddAttack) : 0;
            BuffRuntime.AddBuff(logic, b, def, 0);
            int bs1 = b.HasStatus(StatusType.AddAttack) ? b.GetStatusValue(StatusType.AddAttack) : 0;
            BuffRuntime.RemoveBuff(b, def.id);
            int bs2 = b.HasStatus(StatusType.AddAttack) ? b.GetStatusValue(StatusType.AddAttack) : 0;
            Check("④ 对照：纯增益 加=1 / 撤=0（重算确在清）", bs0 == 0 && bs1 == 1 && bs2 == 0,
                "基础=" + bs0 + " 加增益=" + bs1 + "（期望1） 撤增益=" + bs2 + "（期望0）");
        }
    }
}
