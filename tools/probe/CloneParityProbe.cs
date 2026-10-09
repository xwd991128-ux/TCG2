using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;   //★ CardBuff / BuffProp 在这个命名空间（Card.cs 同样 using 了它）

namespace TcgEngine.Probe
{
    /// <summary>【AI 预测树克隆字段一致性（反射全字段比对）】
    /// 这个项目已因"Clone 漏拷字段"踩过 5 次坑（skip_turns / mana_max_total / battle_buttons / hand_max / traits），
    /// 靠肉眼看 Clone 方法迟早再漏 —— 本探针用**反射**把 Card / Player / Game 的每个实例字段逐个比对，
    /// 只有"白名单（已声明且写明理由的刻意差异）"之外出现不一致才算 FAIL。
    ///
    /// 断言：
    /// ① 未知不一致 = 0（附未知字段清单；已知差异单独列出并注明理由）
    /// ② 语义：Player.traits 里的**疲劳层数**在克隆里保留（规则图 201013/201015 读的就是它）
    /// ③ 语义："能否再行动 / 是否已生效"的关键表一致（cards_attacked / ability_played / buffs / passive_groups）
    /// ④ 克隆与原局**完全解耦**（改克隆的血量/疲劳/区域，原件不受影响）
    /// ⑤ Game.battle_log 是**刻意不拷**（克隆为空），不是漏拷
    /// 触发：建 tools/clone_flag.txt → 进 Play → 写 tools/clone_result.tsv → 自动删标记。
    /// </summary>
    public class CloneParityProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/clone_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/clone_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("CloneParityProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<CloneParityProbe>();
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
            Debug.Log("[克隆一致性] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        // ================= 反射比对 =================

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>不参与"自动填充"的字段：身份字段（改了会改语义）、缓存字段、以及本探针手工构造的集合</summary>
        private static readonly Dictionary<string, HashSet<string>> NoAuto = new Dictionary<string, HashSet<string>>
        {
            { "Card", new HashSet<string> { "card_id", "uid", "player_id", "data", "vdata", "hash", "abilities_data",
                "traits", "ongoing_traits", "status", "ongoing_status", "buffs", "custom_props", "buff_native_keywords" } },
            { "Player", new HashSet<string> { "player_id", "cards_all", "hero", "cards_deck", "cards_hand", "cards_board",
                "cards_equip", "cards_discard", "cards_secret", "cards_temp", "traits", "ongoing_traits",
                "status", "ongoing_status", "history_list" } },
            { "Game", new HashSet<string> { "players", "settings", "battle_log" } },
        };

        /// <summary>**已声明的刻意差异**（键 = 类型名.字段名）。出现在这里不算失败，但会在报告里逐条列出。</summary>
        private static readonly Dictionary<string, string> AllowedDiff = new Dictionary<string, string>
        {
            { "Player.username", "身份信息，AI 预测不需要（Player.Clone 内已注释）" },
            { "Player.avatar", "身份信息，同上" },
            { "Player.cardback", "纯展示（卡背），同上" },
            { "Player.deck", "纯展示（卡组名），同上" },
            { "Player.connected", "网络状态，预测无关" },
            { "Player.ready", "网络状态，预测无关" },
            { "Player.current_turn", "只写不读：全工程仅 StartTurn/开局赋值，无读方（对战记录实际用 Game.turn_count）" },
            { "Card.hash", "缓存（惰性哈希）" },
            { "Card.data", "缓存（按 card_id 惰性查表）" },
            { "Card.vdata", "缓存（按 variant_id 惰性查表）" },
            { "Game.battle_log", "刻意不拷：AI 每个预测节点都克隆会爆内存/掉帧（Game.cs 字段注释已声明）" },
            { "Game.attack_rule_error_logged", "诊断开关（[NonSerialized] private：规则求值报错只报一次），预测副本不同步无影响" },
            { "Game.last_Added_To_Deck", "只写不读：全工程仅 AddCardDeck 赋值，无读方；将来若有规则读它，本探针会立刻报出来" },
        };

        private static void Populate(object obj)
        {
            if (obj == null)
                return;
            HashSet<string> skip;
            NoAuto.TryGetValue(obj.GetType().Name, out skip);
            foreach (FieldInfo f in obj.GetType().GetFields(Flags))
            {
                if (f.IsStatic || f.IsInitOnly)
                    continue;
                if (skip != null && skip.Contains(f.Name))
                    continue;
                Type t = f.FieldType;
                try
                {
                    if (t == typeof(int)) f.SetValue(obj, 137);
                    else if (t == typeof(bool)) f.SetValue(obj, true);
                    else if (t == typeof(float)) f.SetValue(obj, 3.5f);
                    else if (t == typeof(string)) f.SetValue(obj, "s_" + f.Name);
                    else if (t.IsEnum)
                    {
                        Array vals = Enum.GetValues(t);
                        if (vals.Length > 1) f.SetValue(obj, vals.GetValue(vals.Length - 1));
                    }
                    else if (t == typeof(List<string>))
                    {
                        var l = f.GetValue(obj) as List<string>;
                        if (l != null && l.Count == 0) l.Add("l1_" + f.Name);
                    }
                    else if (t == typeof(int[])) f.SetValue(obj, new int[] { 3, 4 });
                    else if (t == typeof(string[])) f.SetValue(obj, new string[] { "a1", "b1" });
                }
                catch { }
            }
        }

        private static string Desc(object o)
        {
            if (o == null) return "null";
            string s = o.ToString();
            return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
        }

        /// <summary>比较两个字段值：一致返回 null，否则返回"人看得懂"的差异描述。
        /// 引用类型按 Clone 的语义比：Card 比 uid、Player 比 id、CardStatus/CardTrait/BuffProp/CardBuff 比值、
        /// 集合逐元素比、其余对象比引用（Clone 本就是浅拷）。</summary>
        private static string Diff(object a, object b, string path)
        {
            if (ReferenceEquals(a, b)) return null;
            if (a == null || b == null) return path + "：一方为 null（" + Desc(a) + " vs " + Desc(b) + "）";

            Card ca = a as Card, cb = b as Card;
            if (ca != null && cb != null)
                return ca.uid == cb.uid ? null : path + "：Card uid " + ca.uid + " ≠ " + cb.uid;

            Player pa = a as Player, pb = b as Player;
            if (pa != null && pb != null)
                return pa.player_id == pb.player_id ? null : path + "：Player id 不同";

            CardStatus sa = a as CardStatus, sb2 = b as CardStatus;
            if (sa != null && sb2 != null)
                return (sa.type == sb2.type && sa.value == sb2.value && sa.duration == sb2.duration && sa.permanent == sb2.permanent)
                    ? null : path + "：CardStatus(" + sa.type + "," + sa.value + "," + sa.duration + "," + sa.permanent + ") ≠ ("
                        + sb2.type + "," + sb2.value + "," + sb2.duration + "," + sb2.permanent + ")";

            CardTrait ta = a as CardTrait, tb = b as CardTrait;
            if (ta != null && tb != null)
                return (ta.id == tb.id && ta.value == tb.value) ? null
                    : path + "：CardTrait(" + ta.id + "=" + ta.value + ") ≠ (" + tb.id + "=" + tb.value + ")";

            BuffProp ba = a as BuffProp, bb = b as BuffProp;
            if (ba != null && bb != null)
                return (ba.key == bb.key && ba.value == bb.value) ? null
                    : path + "：BuffProp(" + ba.key + "=" + ba.value + ") ≠ (" + bb.key + "=" + bb.value + ")";

            //增益实例（Card.Clone 是深拷 → 逐字段比；漏拷 buff_id/source_uid 会让光环重复施加、遗言失效）
            CardBuff ca2 = a as CardBuff, cb2 = b as CardBuff;
            if (ca2 != null && cb2 != null)
            {
                if (ca2.buff_id != cb2.buff_id || ca2.duration != cb2.duration || ca2.permanent != cb2.permanent
                    || ca2.stack != cb2.stack || ca2.source_uid != cb2.source_uid || ca2.source_group != cb2.source_group)
                    return path + "：CardBuff(" + ca2.buff_id + "," + ca2.duration + "," + ca2.stack + "," + ca2.source_uid + ") ≠ ("
                        + cb2.buff_id + "," + cb2.duration + "," + cb2.stack + "," + cb2.source_uid + ")";
                int np = ca2.props != null ? ca2.props.Count : 0;
                int np2 = cb2.props != null ? cb2.props.Count : 0;
                if (np != np2)
                    return path + "：CardBuff.props 长度 " + np + " ≠ " + np2;
                for (int i = 0; i < np; i++)
                {
                    string d2 = Diff(ca2.props[i], cb2.props[i], path + ".props[" + i + "]");
                    if (d2 != null)
                        return d2;
                }
                return null;
            }

            HashSet<string> ha = a as HashSet<string>, hb = b as HashSet<string>;
            if (ha != null && hb != null)
                return ha.Count == hb.Count && ha.SetEquals(hb) ? null
                    : path + "：HashSet 内容不同（" + ha.Count + " vs " + hb.Count + "）";

            IDictionary da = a as IDictionary, db = b as IDictionary;
            if (da != null && db != null)
                return da.Count == db.Count ? null : path + "：字典条目数 " + da.Count + " ≠ " + db.Count;

            IList la = a as IList, lb = b as IList;
            if (la != null && lb != null)
            {
                if (la.Count != lb.Count)
                    return path + "：列表长度 " + la.Count + " ≠ " + lb.Count;
                for (int i = 0; i < la.Count; i++)
                {
                    string d = Diff(la[i], lb[i], path + "[" + i + "]");
                    if (d != null)
                        return d;
                }
                return null;
            }

            if (a is UnityEngine.Object)
                return ReferenceEquals(a, b) ? null : path + "：UnityEngine.Object 引用不同";

            Type at = a.GetType();
            if (!at.IsPrimitive && !at.IsEnum && at != typeof(string) && !at.IsValueType)
                return null;   //其它引用类型：Clone 语义就是浅拷，比引用已在上方 ReferenceEquals 判过

            return a.Equals(b) ? null : path + "：" + Desc(a) + " ≠ " + Desc(b);
        }

        private readonly List<string> bad = new List<string>();
        private readonly List<string> allowed = new List<string>();
        private readonly HashSet<string> allowed_keys = new HashSet<string>();   //★去重：同一字段在多张卡/两个玩家上会重复命中
        private int fields_checked;

        private void CompareFields(string label, object src, object dst)
        {
            if (src == null || dst == null)
                return;
            Type type = src.GetType();
            foreach (FieldInfo f in type.GetFields(Flags))
            {
                if (f.IsStatic)
                    continue;
                fields_checked++;
                string d = Diff(f.GetValue(src), f.GetValue(dst), label + "." + f.Name);
                if (d == null)
                    continue;
                string key = type.Name + "." + f.Name;
                if (AllowedDiff.ContainsKey(key))
                {
                    if (allowed_keys.Add(key))
                        allowed.Add(key + "（" + AllowedDiff[key] + "）");
                }
                else
                    bad.Add(d);
            }
        }

        // ================= 夹具与断言 =================

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            List<CardData> chars = new List<CardData>();
            CardData heroData = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (heroData == null && c.type == CardType.Hero) heroData = c;
                if (c.type == CardType.Character) chars.Add(c);
            }
            if (heroData == null || chars.Count < 7)
            {
                sb.AppendLine("SKIP\t缺少英雄 / 至少 7 张角色卡定义\t");
                return;
            }

            Game g = new Game("probe_clone", 2);
            g.state = GameState.Play;
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);

            p0.hero = Card.Create(heroData, null, p0, "cl_h0");
            p1.hero = Card.Create(heroData, null, p1, "cl_h1");

            //每个区域都放一张卡：漏拷任何一个区域都会被反射比对抓到
            Card[] zone_cards = new Card[7];
            for (int i = 0; i < 7; i++)
                zone_cards[i] = Card.Create(chars[i], null, p0, "cl_z" + i);
            p0.cards_board.Add(zone_cards[0]);
            p0.cards_hand.Add(zone_cards[1]);
            p0.cards_deck.Add(zone_cards[2]);
            p0.cards_discard.Add(zone_cards[3]);
            p0.cards_equip.Add(zone_cards[4]);
            p0.cards_secret.Add(zone_cards[5]);
            p0.cards_temp.Add(zone_cards[6]);

            //卡的状态：状态 / 种族 / 关键词 / 持久移除关键词 / 被动生效分组 / 自定义属性 / 增益实例
            Card c0 = zone_cards[0];
            c0.damage = 3;
            c0.attack = 2;
            c0.hp = 6;
            c0.exhausted = true;
            c0.slot = new Slot(3, 1, 0);
            c0.AddStatus(StatusType.Armor, 2, 3);
            c0.AddTrait("probe_t", 2);
            c0.keywords.Add("probe_kw");
            c0.removed_keywords.Add("probe_rm");
            c0.buff_added_keywords.Add("probe_bkw");
            c0.buff_native_atk = 4;
            c0.keywords.Add("probe_kw2");
            c0.passive_groups.Add("pg_probe");
            c0.custom_props.Add(new BuffProp { key = "cp_probe", value = 4 });
            c0.equipped_uid = "cl_eq";
            CardBuff buff = new CardBuff();
            buff.buff_id = "probe_buff";
            buff.duration = 2;
            buff.permanent = false;
            buff.stack = 1;
            buff.source_uid = "cl_z1";          //光环来源
            buff.source_group = "ag_probe";
            buff.props = new List<BuffProp> { new BuffProp { key = "bk", value = 5 } };
            c0.buffs.Add(buff);

            //玩家状态：疲劳层数（规则图 201013/201015 读它）/ 状态 / 按钮栏
            p0.AddTrait("probe_fatigue", 3);
            p0.AddOngoingTrait("probe_ongoing", 2);
            p0.status.Add(new CardStatus(StatusType.Poisoned, 2, 3));
            p0.ongoing_status.Add(new CardStatus(StatusType.Poisoned, 1, 1));
            p0.battle_buttons.Add("btn_probe");
            g.cards_attacked.Add("cl_z0");
            g.ability_played.Add("ab_probe");
            g.battle_log.Add(new BattleLogEntry());

            //自动填充其余标量字段（刻意差异字段也在其中，用来证明它们"确实不同步"）
            Populate(g);
            Populate(p0);
            Populate(p1);
            foreach (Card c in g.GetPlayer(0).cards_all.Values)
                Populate(c);

            //★断言用到的一切数值，都必须在 Populate 之后取快照：写死期望必然踩坑（上一版就踩了）
            int src_trait = p0.GetTraitValue("probe_fatigue");
            int src_ongoing = p0.GetTraitValue("probe_ongoing");
            int src_damage = c0.damage;
            int src_bnatk = c0.buff_native_atk;
            int src_board = p0.cards_board.Count;
            int src_pstatus = p0.status.Count;

            //================ 克隆 ================
            Game copy = Game.CloneNew(g);

            //---- ① 反射全字段比对 ----
            CompareFields("Game", g, copy);
            CompareFields("Player[0]", g.GetPlayer(0), copy.GetPlayer(0));
            CompareFields("Player[1]", g.GetPlayer(1), copy.GetPlayer(1));
            foreach (KeyValuePair<string, Card> kv in g.GetPlayer(0).cards_all)
            {
                Card dc;
                if (!copy.GetPlayer(0).cards_all.TryGetValue(kv.Key, out dc) || dc == null)
                {
                    bad.Add("Player[0].cards_all[" + kv.Key + "]：克隆里缺失");
                    continue;
                }
                CompareFields("Card[" + kv.Key + "]", kv.Value, dc);
            }
            Check("① 反射全字段比对：未知不一致 = 0", bad.Count == 0,
                "检查字段 " + fields_checked + " 个；未知不一致 " + bad.Count + " 个"
                + (bad.Count > 0 ? " → " + string.Join(" | ", bad.ToArray()) : "")
                + "；已知差异 " + allowed.Count + " 项（" + string.Join("、", allowed.ToArray()) + "）");

            //---- ② 疲劳层数（Player.traits）必须随克隆保留 ----
            int cp_trait = copy.GetPlayer(0).GetTraitValue("probe_fatigue");
            int cp_ongoing = copy.GetPlayer(0).GetTraitValue("probe_ongoing");
            Check("② 疲劳层数(Player.traits)随克隆保留", cp_trait == src_trait && cp_trait == 3 && cp_ongoing == src_ongoing && cp_ongoing == 2,
                "原件 fatigue=" + src_trait + " 克隆 fatigue=" + cp_trait + "（期望 3） 克隆 ongoing=" + cp_ongoing + "（期望 2）");

            //---- ③ 关键的"能否再行动 / 是否已生效"表一致 ----
            Card cc0 = copy.GetPlayer(0).cards_all["cl_z0"];
            bool key_ok = copy.cards_attacked.SetEquals(g.cards_attacked)
                && copy.ability_played.SetEquals(g.ability_played)
                && cc0.buffs.Count == 1 && cc0.buffs[0].buff_id == "probe_buff" && cc0.buffs[0].source_uid == "cl_z1"
                && cc0.passive_groups.Count == 1 && cc0.passive_groups[0] == "pg_probe"
                && cc0.removed_keywords.Count == 1 && cc0.status.Count == 1
                && cc0.buff_native_atk == src_bnatk && cc0.custom_props.Count == 1;
            Check("③ 已攻击/已用能力/增益/被动分组 等关键表一致", key_ok,
                "cards_attacked=" + copy.cards_attacked.Count + " ability_played=" + copy.ability_played.Count
                + " buffs=" + cc0.buffs.Count + " source_uid=" + (cc0.buffs.Count > 0 ? cc0.buffs[0].source_uid : "-")
                + " passive_groups=" + cc0.passive_groups.Count + " removed_keywords=" + cc0.removed_keywords.Count
                + " buff_native_atk=" + cc0.buff_native_atk + "（原件 " + src_bnatk + "）");

            //---- ④ 解耦：改克隆不许影响原件 ----
            copy.GetPlayer(0).AddTrait("probe_fatigue", 100);
            cc0.damage = 999;
            cc0.passive_groups.Add("pg_hacked");
            copy.GetPlayer(0).cards_board.Clear();
            copy.GetPlayer(0).status.Add(new CardStatus(StatusType.Poisoned, 9, 9));
            bool decouple = g.GetPlayer(0).GetTraitValue("probe_fatigue") == src_trait
                && c0.damage == src_damage && c0.passive_groups.Count == 1
                && g.GetPlayer(0).cards_board.Count == src_board && g.GetPlayer(0).status.Count == src_pstatus;
            Check("④ 克隆与原局完全解耦（改克隆不影响原件）", decouple,
                "原件 fatigue=" + g.GetPlayer(0).GetTraitValue("probe_fatigue") + "（期望 " + src_trait + "） 卡 damage=" + c0.damage
                + "（期望 " + src_damage + "） 战场=" + g.GetPlayer(0).cards_board.Count + "（期望 " + src_board
                + "） 玩家状态=" + g.GetPlayer(0).status.Count + "（期望 " + src_pstatus + "）");

            //---- ⑤ battle_log 是刻意不拷 ----
            Check("⑤ battle_log 刻意不拷（非漏拷）", g.battle_log.Count == 1 && copy.battle_log != null && copy.battle_log.Count == 0,
                "原件 " + g.battle_log.Count + " 条 / 克隆 " + copy.battle_log.Count + " 条（期望 1 / 0）");
        }
    }
}
