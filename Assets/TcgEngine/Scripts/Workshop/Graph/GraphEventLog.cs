using System.Collections.Generic;
using UnityEngine;
using TcgEngine;

namespace TcgEngine.Workshop
{
    /// <summary>一条「事件记录」：规则图可回查的历史事件（109002/109003/109004/109005/109006/109007/109009 的数据载体）。
    /// 设计规则（2026-09，用户确认"先设计轻量结构再实现"）：
    ///  · 记什么：事件类型(action)、来源卡/目标卡、关联玩家、数值，以及**来源/目标卡的属性快照**（hp/attack/mana/damage/card_id/uid）；
    ///  · 存多久：每局最多 <see cref="GraphEventLog.CAPACITY"/> 条（环形，超出丢最旧）；换局自动清空；
    ///  · 能否回查：支持"本局全部 / 本回合 / 前 X~Y 回合"三种范围，以及按类型判断与变量读取。</summary>
    public class GraphEventRecord
    {
        public int turn;              //发生时的回合数（Game.turn_count）
        public string action;         //事件类型（入口 action，如 OnPlay / StartOfTurn / DealDamage）
        public int player_id;         //关联玩家（目标玩家优先，其次来源卡拥有者）
        public int value;             //事件数值（伤害量/层数/持续回合等，无则 0）
        public float time;            //记录时间（真实秒，用于排查顺序）
        public Card source;           //来源卡**快照**（施法/触发卡）
        public Card target;           //目标卡**快照**（可能为空）

        public GraphEventRecord Clone()
        {
            GraphEventRecord r = new GraphEventRecord();
            r.turn = turn; r.action = action; r.player_id = player_id; r.value = value; r.time = time;
            r.source = source; r.target = target;
            return r;
        }
    }

    /// <summary>轻量事件记录表（静态单例，按对局自动清空）。见 <see cref="GraphEventRecord"/> 的设计规则。</summary>
    public static class GraphEventLog
    {
        public const int CAPACITY = 512;

        private static readonly List<GraphEventRecord> records = new List<GraphEventRecord>();
        private static Game last_game = null;          //按 Game 实例身份判定"是否换局"
        private static int debug_logged = 0;           //每局只打前几条（便于运行时确认记录已生效，不刷屏）

        /// <summary>换局检测：Game 实例变化 → 清空历史</summary>
        public static void NoteGame(Game game)
        {
            if (game == null || game == last_game)
                return;
            last_game = game;
            records.Clear();
            debug_logged = 0;
        }

        public static void Clear()
        {
            records.Clear();
            debug_logged = 0;
        }

        public static List<GraphEventRecord> All()
        {
            return records;
        }

        /// <summary>记录一次事件（来源/目标卡会**复制快照**，之后卡牌变化不影响历史）</summary>
        public static void Record(int turn, string action, Card source, Card target, int player_id, int value)
        {
            GraphEventRecord r = new GraphEventRecord();
            r.turn = turn;
            r.action = action ?? "";
            r.player_id = player_id;
            r.value = value;
            r.time = Time.realtimeSinceStartup;
            r.source = Snapshot(source);
            r.target = Snapshot(target);
            records.Add(r);
            if (records.Count > CAPACITY)
                records.RemoveAt(0);

            if (debug_logged < 3)
            {
                debug_logged++;
                Debug.Log("[事件记录] 已记录 #" + records.Count + " 类型=" + r.action + " 回合=" + r.turn
                    + " 来源=" + (source != null ? CardName(source) : "无") + " 目标=" + (target != null ? CardName(target) : "无"));
            }
        }

        /// <summary>卡牌快照（只拷运行时状态）。注意：`CardData` 是**只读属性**（由 card_id 解析），
        /// 不能赋值 —— 用同一个 card_id 构造即会解析到同一定义（实测 CS0200 踩过）。</summary>
        public static Card Snapshot(Card c)
        {
            if (c == null)
                return null;
            Card s = new Card(c.card_id, c.uid, c.player_id);   //构造函数签名 (card_id, uid, player_id)（实测 CS7036 踩过）
            s.variant_id = c.variant_id;
            s.slot = c.slot;
            s.exhausted = c.exhausted;
            s.damage = c.damage;
            s.hp = c.hp;
            s.attack = c.attack;
            s.mana = c.mana;
            return s;
        }

        public static string CardName(Card c)
        {
            if (c == null)
                return "无";
            if (c.CardData != null && !string.IsNullOrEmpty(c.CardData.title))
                return c.CardData.title;
            return string.IsNullOrEmpty(c.card_id) ? "?" : c.card_id;
        }

        /// <summary>本回合的事件记录</summary>
        public static List<GraphEventRecord> OfTurn(int turn)
        {
            List<GraphEventRecord> out_list = new List<GraphEventRecord>();
            for (int i = 0; i < records.Count; i++)
                if (records[i].turn == turn)
                    out_list.Add(records[i]);
            return out_list;
        }

        /// <summary>前 X~Y 回合内的事件记录（farther=较早的回合数，nearer=较近的回合数；含端点）
        /// 例：当前第 5 回合，farther=3 nearer=1 → 第 2、3、4 回合；farther=nearer=2 → 只查第 3 回合。</summary>
        public static List<GraphEventRecord> OfRange(int farther, int nearer, int cur_turn)
        {
            if (farther < nearer)
            {
                int t = farther; farther = nearer; nearer = t;      //容错：写反了也认
            }
            int from = cur_turn - farther;
            int to = cur_turn - nearer;
            List<GraphEventRecord> out_list = new List<GraphEventRecord>();
            for (int i = 0; i < records.Count; i++)
                if (records[i].turn >= from && records[i].turn <= to)
                    out_list.Add(records[i]);
            return out_list;
        }

        /// <summary>事件类型与引用值是否匹配：直接比 action；再试中文入口名别名</summary>
        public static bool TypeMatches(string action, string reference)
        {
            if (string.IsNullOrEmpty(reference))
                return true;                                     //没给类型 = 不筛选
            if (string.Equals(action, reference, System.StringComparison.OrdinalIgnoreCase))
                return true;
            string mapped = ActionOfName(reference);
            return mapped != null && string.Equals(action, mapped, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>入口中文名 → action 别名表（常见入口；未收录返回 null）</summary>
        public static string ActionOfName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            switch (name.Trim())
            {
                case "打出时": case "使用时": case "打出": return "OnPlay";
                case "死亡时": case "阵亡时": return "OnDeath";
                case "击杀时": return "OnKill";
                case "回合开始时": return "StartOfTurn";
                case "回合结束时": return "EndOfTurn";
                case "受到伤害时": return "OnDamaged";
                case "造成伤害时": return "OnDealDamage";
                case "攻击时": return "OnAttack";
                case "治疗时": return "OnHeal";
                case "召唤时": case "入场时": return "OnSummon";
                case "点击按钮时": return "ButtonClicked";
                case "激活效果时": return "ActivateEffect";
            }
            return null;
        }
    }
}
