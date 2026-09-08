using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine
{
    /// <summary>
    /// 增益（Buff）运行时核心：施加/移除/查询/持续回合/效果图执行。
    /// 攻击加成/生命加成属性自动映射原生 StatusType.AddAttack/AddHP（经 UpdateOngoing 累加进
    /// attack_ongoing/hp_ongoing 参与真实战斗），其余自定义属性存 CardBuff.props 供规则图读写。
    /// 效果图（BuffData.graph）由 NodeDocRunner 解释执行，入口为「增益触发」事件节点
    /// （添加时/后、移除时/后、每回合开始/结束），上下文：自身=携带增益的卡、施加者、增益定义、剩余回合。
    /// </summary>
    public static class BuffRuntime
    {
        public const string ATK_KEY = "攻击加成";
        public const string HP_KEY = "生命加成";

        private const int MAX_GRAPH_DEPTH = 8;   //增益图嵌套执行深度上限（防「增益图内再添加同增益」死循环）
        private static int graph_depth;

        /// <summary>施加增益：已存在同 id → 属性叠加、持续取 max；否则新建实例并映射原生状态。</summary>
        public static CardBuff AddBuff(Card card, BuffData define, int duration)
        {
            if (card == null || define == null)
                return null;
            CardBuff existing = GetBuff(card, define.id);
            if (existing != null)
            {
                if (define.props != null)
                {
                    foreach (BuffProp p in define.props)
                        existing.SetProp(p.key, existing.GetProp(p.key) + p.value);
                }
                if (duration > 0)
                    existing.duration = duration > existing.duration ? duration : existing.duration;
                else
                    existing.permanent = true;
                ReapplyNative(card);
                return existing;
            }
            List<BuffProp> props = new List<BuffProp>();
            if (define.props != null)
            {
                foreach (BuffProp p in define.props)
                    props.Add(new BuffProp(p.key, p.value));
            }
            CardBuff buff = new CardBuff(define.id, props, duration);
            card.buffs.Add(buff);
            ApplyNative(card, buff);
            return buff;
        }

        /// <summary>移除增益：清实例并重建原生状态（防止多增益叠加时误清）</summary>
        public static void RemoveBuff(Card card, string buff_id)
        {
            if (card == null || string.IsNullOrEmpty(buff_id))
                return;
            for (int i = card.buffs.Count - 1; i >= 0; i--)
            {
                if (card.buffs[i].buff_id == buff_id)
                    card.buffs.RemoveAt(i);
            }
            ReapplyNative(card);
        }

        public static bool HasBuff(Card card, string buff_id)
        {
            return GetBuff(card, buff_id) != null;
        }

        public static CardBuff GetBuff(Card card, string buff_id)
        {
            if (card == null || string.IsNullOrEmpty(buff_id))
                return null;
            foreach (CardBuff b in card.buffs)
            {
                if (b.buff_id == buff_id)
                    return b;
            }
            return null;
        }

        /// <summary>读增益属性（先读实例 props；实例不存在或属性缺失时读定义默认值）</summary>
        public static int GetPropValue(Card card, string buff_id, string key)
        {
            CardBuff buff = GetBuff(card, buff_id);
            if (buff != null && !string.IsNullOrEmpty(key))
            {
                foreach (BuffProp p in buff.props)
                {
                    if (p.key == key)
                        return p.value;
                }
            }
            return 0;
        }

        /// <summary>取实例剩余持续回合（0=永久）</summary>
        public static int GetRemainingDuration(Card card, string buff_id)
        {
            CardBuff buff = GetBuff(card, buff_id);
            if (buff == null)
                return 0;
            return buff.permanent ? 0 : buff.duration;
        }

        /// <summary>写增益属性（含原生映射同步）</summary>
        public static void SetPropValue(Card card, string buff_id, string key, int value)
        {
            CardBuff buff = GetBuff(card, buff_id);
            if (buff == null || string.IsNullOrEmpty(key))
                return;
            buff.SetProp(key, value);
            ReapplyNative(card);
        }

        /// <summary>回合更新：duration 递减，到期移除前/后触发效果图「移除增益时/后」事件
        /// （GameLogic.StartTurn 对场上/手牌所有卡调用，logic 为触发移除事件传参）</summary>
        public static void UpdateBuffDurations(GameLogic logic, Card card)
        {
            if (card == null || card.buffs == null || card.buffs.Count == 0)
                return;
            bool changed = false;
            for (int i = card.buffs.Count - 1; i >= 0; i--)
            {
                CardBuff b = card.buffs[i];
                if (!b.permanent && b.duration > 0)
                {
                    b.duration--;
                    if (b.duration <= 0)
                    {
                        BuffData define = BuffPoolIO.Get(b.buff_id);
                        int left = Mathf.Max(b.duration, 0);
                        RunGraph(logic, card, define, "OnBuffRemoving", null, left);
                        card.buffs.RemoveAt(i);
                        RunGraph(logic, card, define, "OnBuffRemoved", null, 0);
                        changed = true;
                    }
                }
            }
            if (changed)
                ReapplyNative(card);
        }

        /// <summary>每回合事件触发（玩家版）：遍历玩家场上/手牌卡，执行带效果图的增益「每回合开始/结束」（GameLogic.StartTurn/EndTurn 调用）</summary>
        public static void TriggerTurnBuff(GameLogic logic, Player player, string trigger_action)
        {
            if (player == null)
                return;
            for (int i = player.cards_board.Count - 1; i >= 0; i--)
                TriggerTurnBuff(logic, player.cards_board[i], trigger_action);
            for (int i = player.cards_hand.Count - 1; i >= 0; i--)
                TriggerTurnBuff(logic, player.cards_hand[i], trigger_action);
        }

        /// <summary>每回合事件触发（单卡版）：执行该卡所有带效果图的增益「每回合开始/结束」；
        /// 倒序遍历防止效果图内移除增益时改列表</summary>
        public static void TriggerTurnBuff(GameLogic logic, Card card, string trigger_action)
        {
            if (card == null || card.buffs == null)
                return;
            for (int i = card.buffs.Count - 1; i >= 0; i--)
            {
                CardBuff b = card.buffs[i];
                BuffData define = BuffPoolIO.Get(b.buff_id);
                if (define == null || define.graph == null)
                    continue;
                RunGraph(logic, card, define, trigger_action, null, b.permanent ? 0 : b.duration);
            }
        }

        /// <summary>执行增益效果图（入口事件=trigger_action，上下文：自身=card、施加者=giver、增益定义=define、剩余回合=duration）。
        /// 防递归：嵌套增益图执行深度超过 MAX_GRAPH_DEPTH 时静默跳过（如「添加增益后」图里又添加同增益）。</summary>
        public static int RunGraph(GameLogic logic, Card card, BuffData define,
            string trigger_action, Card giver, int duration)
        {
            if (logic == null || card == null || define == null || define.graph == null)
                return 0;
            if (graph_depth >= MAX_GRAPH_DEPTH)
                return 0;
            graph_depth++;
            try
            {
                return NodeDocRunner.Run(logic, define.graph, card, null, null,
                    trigger_action, giver, define, duration);
            }
            finally
            {
                graph_depth--;
            }
        }

        /// <summary>将实例的攻击/生命加成映射为原生状态（叠加语义）</summary>
        private static void ApplyNative(Card card, CardBuff buff)
        {
            if (buff == null || card == null)
                return;
            int atk = buff.GetProp(ATK_KEY);
            int hp = buff.GetProp(HP_KEY);
            if (atk != 0)
                card.AddStatus(StatusType.AddAttack, atk, buff.permanent ? 0 : buff.duration);
            if (hp != 0)
                card.AddStatus(StatusType.AddHP, hp, buff.permanent ? 0 : buff.duration);
        }

        /// <summary>清空原生攻击/生命加成状态，按当前全部 buff 重新施加（防止移除单增益误清其他叠加）</summary>
        private static void ReapplyNative(Card card)
        {
            if (card == null)
                return;
            card.RemoveStatus(StatusType.AddAttack);
            card.RemoveStatus(StatusType.AddHP);
            if (card.buffs == null)
                return;
            foreach (CardBuff b in card.buffs)
                ApplyNative(card, b);
        }
    }
}
