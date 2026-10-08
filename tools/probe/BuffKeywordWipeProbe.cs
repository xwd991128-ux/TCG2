using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【增益重算会不会误清"非增益来源"的关键词状态】
    /// 与 BuffNativeWipeProbe 同一类：ReapplyNative 以前按**全部关键词定义**逐个 RemoveStatus 来清关键词状态，
    /// 再按 card.keywords 以**值 0** 补回。于是规则图「添加关键词」节点（EffectAddKeyword：写 keywords + 叠加状态值，
    /// 例如"法术伤害+3"）挂的状态，会被任何一次增益增删**抹成 0**。
    /// 修法：Card.buff_native_keywords 记账"增益挂的关键词状态"，重算只清这些。
    /// 断言：① 非增益来源的关键词状态（值 3）在施加增益后仍在；② 撤增益后仍在；
    ///       ③ 回归：增益挂的关键词状态在增益移除后必须消失（记账不能把"该清的"漏掉）；
    ///       ④ 对照：0 值关键词状态（非增益来源）重算后仍在。
    /// 触发：建 tools/buffkw_flag.txt → 进 Play → 写 tools/buff_kw_result.tsv → 自动删标记。
    /// </summary>
    public class BuffKeywordWipeProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/buff_kw_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/buffkw_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("BuffKeywordWipeProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<BuffKeywordWipeProbe>();
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
            Debug.Log("[增益关键词误清] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private static int Val(Card c, StatusType st)
        {
            return c.HasStatus(st) ? c.GetStatusValue(st) : -1;
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            //找一个"绑定了状态"的关键词定义（ReapplyNative 清的就是这些状态）
            KeywordData kw = null;
            List<KeywordData> kws = KeywordData.GetAll();
            for (int i = 0; kws != null && i < kws.Count; i++)
                if (kws[i] != null && kws[i].status_type != StatusType.None && !string.IsNullOrEmpty(kws[i].id))
                { kw = kws[i]; break; }
            if (kw == null)
            {
                sb.AppendLine("SKIP\t没有「绑定状态」的关键词定义\t");
                return;
            }

            Game game = new Game("probe_buffkw", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0];

            CardData cd = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Character) { cd = c; break; }
            if (cd == null)
            {
                sb.AppendLine("SKIP\t缺少角色卡定义\t");
                return;
            }

            BuffData def = BuffPoolIO.New();
            def.title = "探针触发用增益";
            def.props = new List<BuffProp> { new BuffProp(BuffRuntime.ATK_KEY, 1) };
            def.duration = 0;

            //---- ① 非增益来源的关键词状态（模拟规则图「添加关键词」：写 keywords + 叠加状态值 3）----
            Card a = Card.Create(cd, null, p0);
            if (!a.keywords.Contains(kw.id))
                a.keywords.Add(kw.id);
            a.AddStatus(kw.status_type, 3, 0);
            int v0 = Val(a, kw.status_type);
            BuffRuntime.AddBuff(logic, a, def, 0);          //任何一次增益增删都会触发 ReapplyNative
            int v1 = Val(a, kw.status_type);
            Check("① 非增益来源的关键词状态值(+3)在增益重算后仍在", v0 == 3 && v1 == 3,
                "关键词=" + kw.id + " 状态=" + kw.status_type + " 重算前=" + v0 + " 重算后=" + v1
                + "（旧实现：按 keywords 以值 0 补回 → 变 0）");

            //---- ② 撤掉增益后仍在 ----
            BuffRuntime.RemoveBuff(a, def.id);
            int v2 = Val(a, kw.status_type);
            Check("② 撤增益后关键词状态值(+3)仍在", v2 == 3, "撤增益后=" + v2 + "（期望 3）");

            //---- ③ 回归：增益挂的关键词状态，在增益移除后必须消失（记账不能漏清自己那份）----
            Card b = Card.Create(cd, null, p0);
            BuffData kdef = BuffPoolIO.New();
            kdef.title = "探针关键词增益";
            kdef.mods = new List<BuffPropMod>();
            BuffPropMod mod = new BuffPropMod(BuffModTarget.Keyword, BuffModMode.Add, 0);
            mod.enum_id = kw.id;
            kdef.mods.Add(mod);
            BuffRuntime.AddBuff(logic, b, kdef, 0);
            bool has_after_add = b.HasStatus(kw.status_type);
            BuffRuntime.RemoveBuff(b, kdef.id);
            bool has_after_remove = b.HasStatus(kw.status_type);
            Check("③ 回归：增益挂的关键词状态随增益消失", has_after_add && !has_after_remove,
                "加增益后状态=" + has_after_add + "（期望 True） 撤增益后=" + has_after_remove + "（期望 False）");

            //---- ④ 对照：**0 值**的非增益关键词状态同样不该被清（模拟规则图挂"关键词但值 0"）----
            Card c2 = Card.Create(cd, null, p0);
            c2.AddStatus(kw.status_type, 0, 0);
            bool before0 = c2.HasStatus(kw.status_type);
            BuffRuntime.AddBuff(logic, c2, def, 0);         //触发重算
            bool after0 = c2.HasStatus(kw.status_type);
            Check("④ 对照：0 值关键词状态（非增益来源）重算后仍在", before0 && after0,
                "关键词=" + kw.id + " 重算前=" + before0 + " 重算后=" + after0
                + "（期望都 True；旧实现按值 0 补回后是否残留取决于另一条路径，正是要盯的点）");
        }
    }
}
