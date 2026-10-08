using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【Card.Clone 增益实例深拷验证】
    /// 背景：AI 预测树用 Card.Clone/CloneNew 复制战场状态。旧实现把 keywords/status/ongoing/
    /// passive_groups/custom_props 都拷了，**唯独漏了 card.buffs（增益实例）** ⇒ 预测副本身上没有增益：
    ///   ① 遗言/亡语不跑（BuffRuntime.TriggerCarrierDeath 读的就是 card.buffs，还按 stack 层数跑）；
    ///   ② 任何一次 ReapplyNative 按空 buffs 重建 → 抹掉增益派生的状态（攻血加成凭空消失）；
    ///   ③ SyncAuraEffects 认不出"这份光环已存在" → 在副本上当首次重新施加（重复广播/特效）。
    /// 断言：① 增益条数与字段逐项一致；② 是独立副本（改副本不影响源，也不是同一个 List/实例）；
    ///       ③ 光环来源字段（source_uid/source_group）保留；④ 无增益的卡克隆不炸。
    /// 触发：建 tools/buffclone_flag.txt → 进 Play → 写 tools/buff_clone_result.tsv → 自动删标记。
    /// </summary>
    public class BuffCloneProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/buff_clone_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/buffclone_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("BuffCloneProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<BuffCloneProbe>();
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
            Debug.Log("[增益克隆] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            CardData cd = null;
            CardData heroData = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (heroData == null && c.type == CardType.Hero) heroData = c;
                if (cd == null && c.type == CardType.Character) cd = c;
            }
            if (cd == null)
            {
                sb.AppendLine("SKIP\t缺少角色卡定义\t");
                return;
            }

            Game game = new Game("probe_buffclone", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0];
            p0.hero = heroData != null ? Card.Create(heroData, null, p0, "bc_hero") : null;
            p0.hp_max = 30; p0.hp = 30;

            //---- 夹具：造一个内存增益定义（不依赖卡池），施加到卡上 ----
            Card src = Card.Create(cd, null, p0, "bc_src");
            p0.cards_board.Add(src);
            BuffData def = new BuffData();     //★BuffData 是 JSON 普通类（不是 ScriptableObject），不能用 CreateInstance
            def.id = "probe_buff_clone";
            def.title = "探针增益";
            def.props = new List<BuffProp> { new BuffProp { key = BuffRuntime.ATK_KEY, value = 2 } };
            CardBuff applied = BuffRuntime.AddBuff(logic, src, def, 3);
            if (applied != null)
            {
                applied.stack = 3;                     //层数（遗言按层数跑，必须跟着走）
                applied.source_uid = "bc_aura_src";    //模拟"光环给的"：撤源差分靠这两个字段
                applied.source_group = "pg_probe_1";
            }
            Check("① 前置：源卡上有增益实例", applied != null && src.buffs != null && src.buffs.Count > 0,
                "增益数=" + (src.buffs != null ? src.buffs.Count : -1) + " buff_id=" + (applied != null ? applied.buff_id : "null"));

            //---- ② 克隆必须带上增益实例 ----
            Card clone = Card.CloneNew(src);
            int n_src = src.buffs != null ? src.buffs.Count : 0;
            int n_clone = clone.buffs != null ? clone.buffs.Count : 0;
            Check("② 克隆带上增益实例（AI 预测树）", n_clone == n_src && n_clone > 0,
                "源=" + n_src + " 副本=" + n_clone + "（旧实现=0：遗言不跑 + 重算抹掉增益派生状态）");

            //---- ③ 字段逐项一致（含光环来源与层数）----
            bool same = false;
            string diff = "无实例";
            if (n_clone > 0 && n_src > 0)
            {
                CardBuff a = src.buffs[0], b = clone.buffs[0];
                same = a.buff_id == b.buff_id && a.duration == b.duration && a.permanent == b.permanent
                    && a.stack == b.stack && a.source_uid == b.source_uid && a.source_group == b.source_group
                    && a.props != null && b.props != null && a.props.Count == b.props.Count;
                diff = "id=" + b.buff_id + " 持续=" + b.duration + " 层数=" + b.stack
                    + " 来源=" + (b.source_uid ?? "null") + "/" + (b.source_group ?? "null")
                    + " 属性条数=" + (b.props != null ? b.props.Count : -1);
            }
            Check("③ 字段逐项一致（含 层数/光环来源/属性）", same, diff + "（期望与源一致）");

            //---- ④ 独立副本：改副本不能影响源 ----
            bool independent = false;
            string ind_detail = "无实例";
            if (n_clone > 0 && n_src > 0)
            {
                clone.buffs[0].duration = 99;
                clone.buffs[0].stack = 9;
                if (clone.buffs[0].props != null && clone.buffs[0].props.Count > 0)
                    clone.buffs[0].props[0].value = 999;
                independent = src.buffs[0].duration != 99 && src.buffs[0].stack != 9
                    && (src.buffs[0].props == null || src.buffs[0].props.Count == 0 || src.buffs[0].props[0].value != 999)
                    && !ReferenceEquals(src.buffs, clone.buffs) && !ReferenceEquals(src.buffs[0], clone.buffs[0]);
                ind_detail = "源 持续=" + src.buffs[0].duration + " 层数=" + src.buffs[0].stack
                    + " 首属性=" + (src.buffs[0].props != null && src.buffs[0].props.Count > 0 ? src.buffs[0].props[0].value.ToString() : "无")
                    + " 同一List=" + ReferenceEquals(src.buffs, clone.buffs);
            }
            Check("④ 是独立副本（改副本不影响源）", independent, ind_detail + "（期望源仍是 3/3/2，非同一容器）");

            //---- ⑤ 负面对照：无增益的卡克隆后是空表、不是 null ----
            Card plain = Card.Create(cd, null, p0, "bc_plain");
            Card plain_clone = Card.CloneNew(plain);
            Check("⑤ 对照：无增益的卡克隆不炸", plain_clone != null && plain_clone.buffs != null && plain_clone.buffs.Count == 0,
                "buffs=" + (plain_clone != null && plain_clone.buffs != null ? plain_clone.buffs.Count.ToString() : "null"));
        }
    }
}
