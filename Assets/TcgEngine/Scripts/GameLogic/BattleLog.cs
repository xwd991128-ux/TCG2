using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Gameplay
{
    /// <summary>对战记录的事件类型（影之诗「战斗记录」口径：只记玩家能理解的结果，不记内部状态）。</summary>
    public enum BattleLogKind : byte
    {
        TurnStart = 1,
        TurnEnd = 2,
        PlayCard = 3,
        Summon = 4,
        Move = 5,
        Attack = 6,
        Damage = 7,
        Heal = 8,
        BuffAdd = 9,
        BuffRemove = 10,
        StatusAdd = 11,
        Death = 12,
        Draw = 13,
        Discard = 14,
        Ability = 15,
        Secret = 16,
        Win = 17,
        Lose = 18,
    }

    /// <summary>一条对战记录。**只存 id/uid/数值**，中文文案在客户端拼（同步体积小）。
    /// 放在 Game 上 ⇒ 随对局同步（客户端 / 观战 / 断线重连自动一致）。</summary>
    [System.Serializable]
    public class BattleLogEntry
    {
        public int seq;             // 递增序号（排序/去重/重连补齐靠它）
        public int turn;            // 第几回合
        public int actor;           // 行动方 player_id（-1 = 系统/回合分隔）
        public byte kind;           // BattleLogKind
        public string card_id;      // 主体卡定义（显示名字/图用）
        public string card_uid;     // 主体卡实例（可能已离场，仅做定位）
        public string target_uid;   // 客体卡实例
        public int target_id = -1;  // 客体玩家
        public string ability_id;
        public int value;           // 伤害/治疗/增减值（0 也要记：能看出"打了没掉血"这种异常）
        public int value2;
    }

    /// <summary>
    /// 对战记录门面（**唯一埋点入口**）。
    /// 约定：
    ///  ① 只记"真实对局"——AI 推演（is_ai_predict）期间一律不记（否则日志会被预测分支刷爆）；
    ///  ② 日志挂在 Game 上、`Game.Clone` **不拷贝**（AI 每个预测节点复制一份会爆内存）；
    ///  ③ 上限 MaxKeep 条（超出丢最旧），面板只显示最近 ShowRecent 条（对齐影之诗的"最近 30 次行动"）。
    /// </summary>
    public static class BattleLog
    {
        /// <summary>后台保留上限（防内存爆；面板显示更少）</summary>
        public const int MaxKeep = 400;

        /// <summary>面板默认显示条数（影之诗「战斗记录」只存最近 30 次行动）</summary>
        public const int ShowRecent = 30;

        /// <summary>当前权威对局（由 GameLogic 绑定；AI 推演的 logic 不会绑上来）</summary>
        private static Game bound;

        /// <summary>全局递增序号（排序/去重用）</summary>
        private static int seq_counter;

        public static Game Current { get { return bound; } }

        public static void Bind(Game g)
        {
            if (g == null)
                return;
            bound = g;
            if (g.battle_log == null)
                g.battle_log = new List<BattleLogEntry>();
        }

        /// <summary>开新对局：清空（GameLogic 开战处调用）</summary>
        public static void Reset(Game g)
        {
            if (g == null)
                return;
            if (g.battle_log == null)
                g.battle_log = new List<BattleLogEntry>();
            g.battle_log.Clear();
            seq_counter = 0;
        }

        public static bool CanRecord(GameLogic logic)
        {
            return logic != null && !logic.IsAIPredict && bound != null;
        }

        /// <summary>记一条（内部统一走这里）</summary>
        public static void Record(BattleLogEntry e)
        {
            Game g = bound;
            if (g == null || e == null)
                return;
            if (g.battle_log == null)
                g.battle_log = new List<BattleLogEntry>();
            g.battle_log.Add(e);
            while (g.battle_log.Count > MaxKeep)
                g.battle_log.RemoveAt(0);
        }

        public static BattleLogEntry New(int actor, BattleLogKind kind)
        {
            Game g = bound;
            BattleLogEntry e = new BattleLogEntry();
            e.seq = ++seq_counter;
            e.turn = g != null ? g.turn_count : 0;
            e.actor = actor;
            e.kind = (byte)kind;
            return e;
        }

        /// <summary>便捷重载：动作类（主体卡 / 客体卡）</summary>
        public static void Card(int actor, BattleLogKind kind, Card card, Card target = null, int value = 0)
        {
            BattleLogEntry e = New(actor, kind);
            if (card != null) { e.card_id = card.card_id; e.card_uid = card.uid; }
            if (target != null) { e.target_uid = target.uid; }
            e.value = value;
            Record(e);
        }

        /// <summary>便捷重载：以玩家为目标（如 攻击玩家 / 受到伤害）</summary>
        public static void TargetPlayer(int actor, BattleLogKind kind, string card_id, string card_uid, Player target, int value = 0)
        {
            BattleLogEntry e = New(actor, kind);
            e.card_id = card_id;
            e.card_uid = card_uid;
            e.target_id = target != null ? target.player_id : -1;
            e.value = value;
            Record(e);
        }

        public static void Turn(int player_id, bool start)
        {
            Record(New(player_id, start ? BattleLogKind.TurnStart : BattleLogKind.TurnEnd));
        }

        /// <summary>动作历史镜像：Player.AddHistory 内部调用（全项目所有真实动作的唯一汇聚点）。</summary>
        public static void MirrorHistory(Player p, ActionHistory h)
        {
            if (p == null || h == null || bound == null)
                return;
            BattleLogEntry e = New(p.player_id, MapKind(h.type));
            e.card_id = h.card_id;
            e.card_uid = h.card_uid;
            e.target_uid = h.target_uid;
            e.target_id = h.target_id;
            e.ability_id = h.ability_id;
            Record(e);
        }

        private static BattleLogKind MapKind(ushort action)
        {
            if (action == GameAction.PlayCard) return BattleLogKind.PlayCard;
            if (action == GameAction.Attack) return BattleLogKind.Attack;
            if (action == GameAction.AttackPlayer) return BattleLogKind.Attack;
            if (action == GameAction.Move) return BattleLogKind.Move;
            if (action == GameAction.CastAbility) return BattleLogKind.Ability;
            if (action == GameAction.SecretTriggered) return BattleLogKind.Secret;
            if (action == GameAction.EndTurn) return BattleLogKind.TurnEnd;
            return BattleLogKind.Ability;
        }

        // ---------------- 文案（中文，客户端拼） ----------------

        private static string CardTitle(string card_id, string fallback = "?")
        {
            if (string.IsNullOrEmpty(card_id))
                return fallback;
            CardData cd = CardData.Get(card_id);
            return cd != null && !string.IsNullOrEmpty(cd.title) ? cd.title : card_id;
        }

        private static string PlayerName(Game g, int player_id)
        {
            if (player_id < 0)
                return "系统";
            Player p = g != null ? g.GetPlayer(player_id) : null;
            if (p != null && !string.IsNullOrEmpty(p.username))
                return p.username;
            return "玩家" + player_id;
        }

        /// <summary>一条记录 → 中文文案（影之诗风格：主体 + 动作 + 客体 + 数值）</summary>
        public static string Format(BattleLogEntry e, Game g, int my_id)
        {
            if (e == null)
                return "";
            BattleLogKind kind = (BattleLogKind)e.kind;
            string who = e.actor == my_id ? "我方" : (e.actor < 0 ? "" : "对方");
            string me = CardTitle(e.card_id);
            string tgt = !string.IsNullOrEmpty(e.target_uid) ? "「" + CardTitle(CardUidToId(g, e.target_uid), "随从") + "」"
                       : (e.target_id >= 0 ? "「" + PlayerName(g, e.target_id) + "」" : "");

            switch (kind)
            {
                case BattleLogKind.TurnStart: return "── 第 " + e.turn + " 回合 · " + who + " ──";
                case BattleLogKind.TurnEnd: return who + " 结束回合";
                case BattleLogKind.PlayCard: return who + " 使用「" + me + "」";
                case BattleLogKind.Summon: return who + " 召唤「" + me + "」";
                case BattleLogKind.Move: return who + " 移动「" + me + "」";
                case BattleLogKind.Attack: return who + "「" + me + "」攻击" + tgt;
                case BattleLogKind.Damage: return Victim(e, g) + " 受到 " + e.value + " 点伤害";
                case BattleLogKind.Heal: return Victim(e, g) + " 恢复 " + e.value + " 点生命";
                case BattleLogKind.BuffAdd: return who + "「" + me + "」获得增益";
                case BattleLogKind.BuffRemove: return who + "「" + me + "」失去增益";
                case BattleLogKind.StatusAdd: return who + "「" + me + "」获得状态";
                case BattleLogKind.Death: return "「" + me + "」被消灭";
                case BattleLogKind.Draw: return who + " 抽了 " + Mathf.Max(e.value, 1) + " 张牌";
                case BattleLogKind.Discard: return who + " 弃掉「" + me + "」";
                case BattleLogKind.Ability: return who + " 发动「" + me + "」的能力";
                case BattleLogKind.Secret: return who + " 的秘术触发";
                case BattleLogKind.Win: return who + " 获胜";
                case BattleLogKind.Lose: return who + " 失败";
            }
            return who + " " + me;
        }

        /// <summary>受害者显示名：玩家目标用玩家名，卡目标用卡名（卡可能已离场 → 回落到 uid 查表）</summary>
        private static string Victim(BattleLogEntry e, Game g)
        {
            if (e.target_id >= 0)
                return "「" + PlayerName(g, e.target_id) + "」";
            string id = !string.IsNullOrEmpty(e.card_id) ? e.card_id : CardUidToId(g, e.card_uid);
            return "「" + CardTitle(id, "?") + "」";
        }

        /// <summary>卡实例 uid → 卡定义 id（卡可能已离场，取不到就返回 null）</summary>
        public static string CardUidToId(Game g, string uid)
        {
            if (g == null || string.IsNullOrEmpty(uid))
                return null;
            Card c = g.GetCard(uid);
            return c != null ? c.card_id : null;
        }
    }
}
