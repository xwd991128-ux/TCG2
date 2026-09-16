using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.VFX;          // VFXRuntime（增益施加时播放配置的特效）
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

            //增益特效（编辑器「增益参数 → 特效」配置的 VFXConfig）：施加时播放一次。
            //表现层失败绝不影响对局逻辑（与规则图节点特效同规：整体 try/catch 吞掉）。
            if (define.vfx != null && define.vfx.HasFrames)
            {
                try
                {
                    VFXRuntime.Trigger(define.vfx, card, card, null, "buff_add");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[增益] 特效播放失败（已忽略）: " + e.Message);
                }
            }
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
                if (define == null)
                    continue;
                RunGraph(logic, card, define, trigger_action, null, b.permanent ? 0 : b.duration);
            }
        }

        /// <summary>执行增益效果图（入口事件=trigger_action，上下文：自身=card、施加者=giver、增益定义=define、剩余回合=duration）。
        /// ★**逐张执行 define.graphs 的所有页**：编辑器约束「1 个效果只允许 1 个触发」→ 增益有多个触发时必须分页
        /// （如"每回合开始"一页、"移除增益后"一页），所以这里必须把所有页都跑一遍，
        /// 否则放在第 2 页及以后的触发**永远不会触发**（旧实现只跑 graphs[0] = 兼容字段 graph）。
        /// 防递归：嵌套增益图执行深度超过 MAX_GRAPH_DEPTH 时静默跳过（如「添加增益后」图里又添加同增益）。</summary>
        public static int RunGraph(GameLogic logic, Card card, BuffData define,
            string trigger_action, Card giver, int duration)
        {
            if (logic == null || card == null || define == null)
                return 0;
            List<CardEffectData> graphs = define.EnsureGraphs();
            int total = 0;
            for (int i = 0; i < graphs.Count; i++)
            {
                if (graphs[i] == null || graphs[i].graph == null)
                    continue;
                total += RunSingleGraph(logic, card, graphs[i].graph, define, trigger_action, giver, duration);
            }
            return total;
        }

        /// <summary>执行单张增益图（含防递归深度限制）</summary>
        private static int RunSingleGraph(GameLogic logic, Card card, GraphData g, BuffData define,
            string trigger_action, Card giver, int duration)
        {
            if (g == null)
                return 0;
            if (graph_depth >= MAX_GRAPH_DEPTH)
                return 0;
            graph_depth++;
            try
            {
                return NodeDocRunner.Run(logic, g, card, null, null,
                    trigger_action, giver, define, duration);
            }
            finally
            {
                graph_depth--;
            }
        }

        /// <summary>将实例的属性修改落到卡上：
        /// ① 旧字段 props（攻击/生命加成）继续生效（历史资源/旧图行为不变）；
        /// ② 新的「属性修改规则」(BuffData.mods)：数值型→原生状态（攻击/生命/护甲/花费），
        ///    枚举型→运行时种族/关键词表（Card.buff_added_*/buff_removed_*）。</summary>
        private static void ApplyNative(Card card, CardBuff buff)
        {
            if (buff == null || card == null)
                return;
            int duration = buff.permanent ? 0 : buff.duration;

            //① 旧字段 props（兼容）
            int atk = buff.GetProp(ATK_KEY);
            int hp = buff.GetProp(HP_KEY);
            if (atk != 0)
                card.AddStatus(StatusType.AddAttack, atk, duration);
            if (hp != 0)
                card.AddStatus(StatusType.AddHP, hp, duration);

            //② 属性修改规则
            BuffData define = buff.BuffData;
            if (define == null)
                return;
            foreach (BuffPropMod m in define.EnsureMods())
                ApplyModRule(card, buff, m, duration);

            //自定义属性的**初始值**：写进该卡的增益实例属性（Key 与属性修改的实例覆盖同口径），
            //这样「引用属性」以及节点的「获取增益属性」都能读到它。
            if (buff != null)
            {
                foreach (BuffCustomProp cp in define.EnsureCustomPropDefs())
                {
                    if (cp == null || string.IsNullOrEmpty(cp.name))
                        continue;
                    string key = InstanceKey(cp.name);
                    if (!string.IsNullOrEmpty(key))
                        buff.SetProp(key, cp.InitInt());
                }
            }
        }

        /// <summary>落一条属性修改规则到卡上（实例覆盖优先于定义）</summary>
        private static void ApplyModRule(Card card, CardBuff buff, BuffPropMod m, int duration)
        {
            if (card == null || m == null || string.IsNullOrEmpty(m.target))
                return;

            //枚举型：种族
            if (m.target == BuffModTarget.Trait)
            {
                if (string.IsNullOrEmpty(m.enum_id))
                    return;
                if (BuffModMode.IsRemove(m.mode))          //"移除 / 移除属性"两种写法都认
                    AddUnique(card.buff_removed_traits, m.enum_id);
                else
                    AddUnique(card.buff_added_traits, m.enum_id);
                return;
            }

            //枚举型：关键词（原生机制关键词同时转成状态，复用引擎原有判定）
            if (m.target == BuffModTarget.Keyword)
            {
                if (string.IsNullOrEmpty(m.enum_id))
                    return;
                if (BuffModMode.IsRemove(m.mode))
                {
                    AddUnique(card.buff_removed_keywords, m.enum_id);
                    return;
                }
                AddUnique(card.buff_added_keywords, m.enum_id);
                KeywordData kw = KeywordData.Get(m.enum_id);
                if (kw != null && kw.status_type != StatusType.None)
                    card.AddStatus(kw.status_type, 0, duration);
                return;
            }

            //数值型：花费 / 攻击 / 生命 / 护甲 / 自定义参数
            StatusType st = StatusOf(m.target);
            string ikey = InstanceKey(m.target);

            //实例覆盖（节点「设置增益属性」在运行时改写过的值）：按"设置为该值"处理，且只影响这一张卡
            if (buff != null && ikey != null && HasProp(buff, ikey))
            {
                int want = buff.GetProp(ikey);
                int delta = want - GetTargetValue(card, m.target);
                if (delta != 0 && st != StatusType.None)
                    card.AddStatus(st, delta, duration);
                return;
            }

            //两种写法（减少 / 减少属性、设置为 / 设置为属性）都按同一语义处理
            int v = m.mode == BuffModMode.Reference ? GetSourceValue(card, m.value_source) : m.value;
            if (BuffModMode.IsSub(m.mode))
                v = -Mathf.Abs(v);
            if (BuffModMode.IsSet(m.mode))
                v = v - GetTargetValue(card, m.target);   //"设置为 X" → 用 (X-当前值) 的增量实现
            if (v == 0)
                return;
            if (st != StatusType.None)
                card.AddStatus(st, v, duration);
        }

        /// <summary>目标属性 → 原生状态（自定义参数无原生状态，返回 None 只写实例属性）</summary>
        private static StatusType StatusOf(string target)
        {
            if (target == BuffModTarget.Attack) return StatusType.AddAttack;
            if (target == BuffModTarget.HP) return StatusType.AddHP;
            if (target == BuffModTarget.Armor) return StatusType.Armor;
            if (target == BuffModTarget.Cost) return StatusType.AddManaCost;
            return StatusType.None;
        }

        /// <summary>目标属性 → 实例属性 key（通用属性名与旧字段同源：攻击加成/生命加成）</summary>
        private static string InstanceKey(string target)
        {
            if (target == BuffModTarget.Attack) return ATK_KEY;
            if (target == BuffModTarget.HP) return HP_KEY;
            return target;
        }

        private static bool HasProp(CardBuff buff, string key)
        {
            foreach (BuffProp p in buff.props)
            {
                if (p != null && p.key == key)
                    return true;
            }
            return false;
        }

        private static void AddUnique(List<string> list, string id)
        {
            if (list != null && !string.IsNullOrEmpty(id) && !list.Contains(id))
                list.Add(id);
        }

        /// <summary>目标属性当前值（"设置为"/"引用属性"按增量实现时用）</summary>
        public static int GetTargetValue(Card card, string target)
        {
            if (card == null)
                return 0;
            if (target == BuffModTarget.Attack) return card.GetAttack();
            if (target == BuffModTarget.HP) return card.GetHPMax();
            if (target == BuffModTarget.Cost) return card.GetMana();
            if (target == BuffModTarget.Armor) return card.GetStatusValue(StatusType.Armor);
            return 0;   //自定义参数无原生值（读实例）
        }

        /// <summary>「引用属性」的数值来源：读目标卡另一个属性的当前值（未知名字按自定义参数读实例属性）</summary>
        private static int GetSourceValue(Card card, string source)
        {
            if (card == null || string.IsNullOrEmpty(source))
                return 0;
            if (source == BuffModTarget.Attack || source == BuffModTarget.HP
                || source == BuffModTarget.Cost || source == BuffModTarget.Armor)
                return GetTargetValue(card, source);
            foreach (CardBuff b in card.buffs)
            {
                int pv = b.GetProp(source);
                if (pv != 0)
                    return pv;
            }
            return 0;
        }

        /// <summary>清空由增益属性修改产生的可变量，按当前全部 buff 重新施加。
        /// 只清理 buff_* 四个专用列表与四个原生状态，不动其它系统用的 ongoing_* / status，
        /// 因此移除单个增益不会误清别的来源（叠加语义保持正确）。</summary>
        private static void ReapplyNative(Card card)
        {
            if (card == null)
                return;
            card.RemoveStatus(StatusType.AddAttack);
            card.RemoveStatus(StatusType.AddHP);
            card.RemoveStatus(StatusType.Armor);
            card.RemoveStatus(StatusType.AddManaCost);
            card.buff_added_traits.Clear();
            card.buff_removed_traits.Clear();
            card.buff_removed_keywords.Clear();
            card.buff_added_keywords.Clear();

            //关键词原生状态（风怒/冲锋…）先按"全部关键词定义"清一遍，稍后按最终关键词表统一重加，
            //避免"增益附加的关键词状态"在增益消失后残留。
            List<KeywordData> kw_all = KeywordData.GetAll();
            for (int i = 0; i < kw_all.Count; i++)
            {
                KeywordData k = kw_all[i];
                if (k != null && k.status_type != StatusType.None)
                    card.RemoveStatus(k.status_type);
            }

            if (card.buffs != null)
            {
                for (int i = 0; i < card.buffs.Count; i++)
                    ApplyNative(card, card.buffs[i]);
            }

            //卡自身带的基础关键词状态（上面被一并清掉）→ 补回（被增益移除的除外）
            for (int i = 0; i < card.keywords.Count; i++)
            {
                string kid = card.keywords[i];
                if (card.buff_removed_keywords.Contains(kid))
                    continue;
                KeywordData k = KeywordData.Get(kid);
                if (k != null && k.status_type != StatusType.None)
                    card.AddStatus(k.status_type, 0, 0);
            }
        }

        /// <summary>公开入口：属性修改（含实例覆盖值）改过之后重算该卡的原生状态。
        /// 节点「设置增益属性」/ 增益属性面板改值后调用（非每帧，只有变更时才走）。</summary>
        public static void Reapply(Card card)
        {
            ReapplyNative(card);
        }

        /// <summary>按目标属性名写增益实例属性（做 key 映射：攻击→攻击加成、生命→生命加成）+ 重算原生状态。
        /// 节点「设置增益属性」(206003) 用：目标名与面板「目标属性」同口径；未知名字按自定义参数原样存
        /// （供规则图 106004 读取，行为与旧版一致）。</summary>
        public static bool SetPropByTarget(Card card, string buff_id, string target, int value)
        {
            if (card == null || string.IsNullOrEmpty(target))
                return false;
            CardBuff buff = string.IsNullOrEmpty(buff_id) ? null : GetBuff(card, buff_id);
            if (buff == null)
                return false;
            buff.SetProp(InstanceKey(target), value);
            ReapplyNative(card);
            return true;
        }

        /// <summary>运行时设置某增益某个属性修改的值（节点「设置增益属性」，值来自端口取值）。
        /// 写的是**实例属性**（按卡独立，不影响增益定义/其它卡），随后重算原生状态。
        /// target 与面板「目标属性」同口径（花费/攻击/生命/护甲/关键词/种族/自定义参数）。</summary>
        public static bool SetTargetValue(Card card, string buff_id, string target, int value)
        {
            if (card == null || string.IsNullOrEmpty(target))
                return false;
            CardBuff buff = string.IsNullOrEmpty(buff_id) ? null : GetBuff(card, buff_id);
            if (buff == null)
                return false;
            if (target == BuffModTarget.Keyword || target == BuffModTarget.Trait)
                return false;                      //枚举型不支持"设置数值"（用 附加/移除）
            string key = InstanceKey(target);
            buff.SetProp(key, value);
            ReapplyNative(card);
            return true;
        }
    }
}
