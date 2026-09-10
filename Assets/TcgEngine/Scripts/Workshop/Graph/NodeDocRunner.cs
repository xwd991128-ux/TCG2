using System.Collections.Generic;
using TcgEngine.Gameplay;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// NodeDoc(zmcs) 图执行器 v1：在真实对局（GameLogic）里，从匹配的入口事件(Event)出发，
    /// 沿动作线(Flow)收集可达的 NodeDoc 动作节点，并按其 defineId 执行到 TCG2 游戏逻辑上。
    /// 第一批（垂直闭环）：202001 造成伤害、202016 消灭、治疗目标卡牌类动作。
    /// 第二批：控制流（212005 重复直到/212006 停止/212007 跳过）、临时变量（212004/112007）、
    /// 112010 按条件选值、集合运算（创建/添加/反转/打乱/排序/前X个/随机X个/首末元素/求和/最值/均值/属性映射）、
    /// 卡牌行动（沉默/丢弃/复制/变形/获得控制权/移回手牌/洗入牌库/置入墓地）、玩家灵力（201008/201010）。
    /// 第三批：卡牌定义家族（103002/103008 取定义、103010/11/12 定义属性、103024 类型判断、103017/103018 宣言/遗言）、
    /// 玩家查询（101005 当前回合玩家、101014/101015 灵力、101018/109008 回合数、101019 先手）、
    /// 112008 X到Y随机整数、112009 是否不存在。
    /// 第四批：定义集合通道（103001 获取所有卡牌定义；111004/111008/111024/111025 感知定义集合；
    /// 202003/202004/202005/202029 的定义口支持取值线，可"随机召唤/变形任意定义"）。
    /// 说明：仍有部分节点未接入（护甲/信仰对决/卡牌定义/快照/事件家族等），
    /// 未支持的 NodeDoc 动作会打日志跳过，不影响图保存与其余动作执行。
    /// </summary>
    public static class NodeDocRunner
    {
        /// <summary>
        /// 执行当前触发事件下的 NodeDoc 动作。
        /// </summary>
        /// <param name="logic">真实对局逻辑</param>
        /// <param name="graph">规则图</param>
        /// <param name="caster">施法/触发卡（图上下文：自身）</param>
        /// <param name="target_card">法术选中的卡牌目标（PlayTarget），可能为空</param>
        /// <param name="target_player">选中的玩家目标，可能为空</param>
        /// <param name="trigger_action">入口事件 action（如 OnPlay/StartOfTurn/ButtonClicked），空=执行第一个事件节点</param>
        /// <param name="giver">增益图上下文：施加者（添加/移除增益的卡），无则 null</param>
        /// <param name="buff">增益图上下文：本增益定义（供 buff 端口取值）</param>
        /// <param name="duration">增益图上下文：当前剩余持续回合</param>
        /// <param name="button_id">按钮图上下文：匹配「点击按钮时/点击按钮后」事件节点的 button_id 字段</param>
        /// <returns>实际执行的 NodeDoc 动作数</returns>
        public static int Run(GameLogic logic, GraphData graph, Card caster,
            Card target_card, Player target_player, string trigger_action,
            Card giver = null, BuffData buff = null, int duration = 0, string button_id = null)
        {
            int count = 0;
            if (logic == null || graph == null || caster == null)
                return 0;

            ctx_giver = giver;
            ctx_buff = buff;
            ctx_duration = duration;

            //能力触发链（战吼/亡语/关键词/按钮等，非事件广播）：cur_event 为空时合成一个事件上下文，
            //供「当前事件(108001)」/「获取变量(108002)」读取；链结束还原，避免污染外层广播。
            GraphEventContext prev_event = cur_event;
            bool synth_event = cur_event == null;
            if (synth_event)
            {
                cur_event = new GraphEventContext();
                cur_event.action = trigger_action;
                cur_event.phase = GraphEventPhase.After;
                cur_event.card = caster;
                cur_event.source_card = target_card;
                cur_event.player = target_player != null ? target_player : PlayerOf(logic, caster);
                cur_event.value = duration;
            }

            loops.Clear();   //防御：清掉上次 Run 可能残留的循环上下文
            temp_vars.Clear();
            try
            {
                foreach (GraphNode ev in graph.nodes)
                {
                    if (ev == null || ev.type != GraphNodeType.Event)
                        continue;
                    if (!string.IsNullOrEmpty(trigger_action) && ev.action != trigger_action)
                        continue;
                    if (!string.IsNullOrEmpty(button_id) &&
                        GraphRuntime.GetFieldString(ev, "button_id", "") != button_id)
                        continue;
                    if (!IsEventConditionMet(logic, graph, ev, caster, target_card, target_player))
                        continue;   //事件入口「条件」输入口：连线布尔为假则本入口不触发（事件筛选）

                    //入口「标签列表」写入当前事件上下文（战吼/亡语等自定义标签，供图内判断）
                    if (cur_event != null)
                    {
                        string tt = GraphRuntime.GetFieldString(ev, "tags", "");
                        if (string.IsNullOrEmpty(tt))
                            tt = GraphRuntime.GetFieldString(ev, "tag_list", "");
                        if (!string.IsNullOrEmpty(tt))
                            cur_event.tags = tt;
                    }

                    int executed = 0;
                    WalkNode(logic, graph, ev, caster, target_card, target_player, new HashSet<string>(), ref executed);
                    count += executed;
                }
                return count;
            }
            finally
            {
                if (synth_event)
                    cur_event = prev_event;
                loops.Clear();
                temp_vars.Clear();
            }
        }

        /// <summary>
        /// 执行按钮点击：先沿「点击按钮时(ButtonClicked)」分支，再沿「点击按钮后(ButtonClickedAfter)」分支。
        /// 模拟点击（TriggerButtonEffect 动作）同样调用本方法（完整两段）。</summary>
        /// <param name="logic">真实对局逻辑</param>
        /// <param name="graph">全局按钮图（一图多按钮共享）</param>
        /// <param name="hero">点击玩家的英雄卡（图上下文：自身）</param>
        /// <param name="button_id">被点击的按钮 id</param>
        /// <returns>实际执行的 NodeDoc 动作数</returns>
        public static int RunButtonClick(GameLogic logic, GraphData graph, Card hero, string button_id)
        {
            if (logic == null || graph == null || hero == null || string.IsNullOrEmpty(button_id))
                return 0;
            int count = 0;
            count += Run(logic, graph, hero, null, null, "ButtonClicked", button_id: button_id);
            count += Run(logic, graph, hero, null, null, "ButtonClickedAfter", button_id: button_id);
            return count;
        }

        /// <summary>
        /// 执行一次图事件广播下的规则图（由 GameLogic.EmitGraphEvent 对每个匹配宿主调用）。
        /// 广播期间把 ctx 设为当前事件上下文，供事件入口的 subject/source/value 端口与
        /// 「阻止本事件」「修改事件值」动作读写；嵌套广播自动保存/恢复外层上下文。</summary>
        public static int RunEvent(GameLogic logic, GraphData graph, Card host, GraphEventContext ctx)
        {
            if (logic == null || graph == null || host == null || ctx == null)
                return 0;
            GraphEventContext prev = cur_event;
            cur_event = ctx;
            try
            {
                return Run(logic, graph, host, ctx.card, ctx.player, ctx.action);
            }
            finally
            {
                cur_event = prev;
            }
        }

        /// <summary>
        /// 事件入口「条件(cond)」输入口的筛选求值：无连线=放行；连线的布尔源为假=该入口本次不触发。
        /// 专用求值：卡牌相等/卡牌归属/玩家归属（需事件上下文，读 cur_event/caster 判定"己方/敌方"）；
        /// 其余布尔源（常量/102032 卡牌类型判断等）走通用条件链。</summary>
        private static bool IsEventConditionMet(GameLogic logic, GraphData graph, GraphNode ev,
            Card caster, Card target_card, Player target_player)
        {
            if (graph == null || ev == null)
                return true;
            GraphPin pin = graph.GetPinByName(ev.id, "cond");
            if (pin == null)
                return true;
            GraphLink link = graph.GetIncomingLink(ev.id, pin.id);
            if (link == null)
                return true;
            GraphNode src = graph.GetNode(link.from_node);
            if (src == null)
                return true;
            if (string.IsNullOrEmpty(src.category))
            {
                if (src.type == GraphNodeType.Value)
                    return GraphRuntime.EvaluateValue(graph, src) != 0;
                return true;
            }
            switch (src.action)
            {
                case "EFCardEquals":   //两张卡是同一张（比较 入口 self/subject 等）
                {
                    Card a = ResolveInputCard(logic, graph, src, "cardA", caster, target_card, target_player);
                    Card b = ResolveInputCard(logic, graph, src, "cardB", caster, target_card, target_player);
                    return a != null && a == b;
                }
                case "EFCardOwner":    //卡牌归属：己方(事件主体玩家侧)/敌方
                {
                    Card c = ResolveInputCard(logic, graph, src, "card", caster, target_card, target_player);
                    if (c == null)
                        return false;
                    bool enemy = GraphRuntime.GetFieldString(src, "side", "己方") == "敌方";
                    Player owner = (cur_event != null && cur_event.player != null) ? cur_event.player : PlayerOf(logic, caster);
                    bool own = owner != null && c.player_id == owner.player_id;
                    return enemy ? !own : own;
                }
                case "EFPlayerOwner":  //玩家归属：己方(事件主体玩家侧)/敌方
                {
                    Player p = ResolveInputPlayer(logic, graph, src, "player", caster, target_player);
                    if (p == null)
                        return false;
                    bool enemy = GraphRuntime.GetFieldString(src, "side", "己方") == "敌方";
                    Player owner = (cur_event != null && cur_event.player != null) ? cur_event.player : PlayerOf(logic, caster);
                    bool own = owner != null && p.player_id == owner.player_id;
                    return enemy ? !own : own;
                }
                default:
                    return EvaluateConditionNode(logic, graph, src, caster, target_card);
            }
        }

        /// <summary>白名单动作（用于旧图类型残留提示）</summary>
        private static bool IsSupportedAction(string action)
        {
            switch (action)
            {
                case "202001":
                case "202016":
                case "202013":
                case "202039":
                case "202047":
                case "202041":
                case "212001":
                case "212002":
                case "202003":
                case "202004":
                case "210001":
                case "201003":
                case "202037":
                case "210002":
                case "206001":
                case "206002":
                case "206003":
                case "212005":
                case "212006":
                case "212007":
                case "212004":
                case "112007":
                case "112010":
                case "111001":
                case "111002":
                case "111009":
                case "111013":
                case "111014":
                case "111015":
                case "111016":
                case "111017":
                case "111024":
                case "111025":
                case "111026":
                case "111028":
                case "111031":
                case "111032":
                case "202015":
                case "202038":
                case "202044":
                case "202029":
                case "202028":
                case "202005":
                case "210003":
                case "210004":
                case "210005":
                case "201008":
                case "201010":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>循环上下文：212002 重复动作的 repeatTime 输出按当前迭代序号取值（支持嵌套，按节点 id 匹配）</summary>
        private class LoopCtx
        {
            public string node_id;
            public int iter;
        }
        private static readonly List<LoopCtx> loops = new List<LoopCtx>();

        /// <summary>循环控制标记（212006 停止=break / 212007 跳过=continue，作用于最内层循环）</summary>
        private static bool loop_break;
        private static bool loop_continue;

        /// <summary>临时变量（212004 设置 / 112007 读取；v1 全图扁平作用域，不实现 zmcs 的分支/循环子作用域）</summary>
        private static readonly Dictionary<string, object> temp_vars = new Dictionary<string, object>();

        /// <summary>增益图上下文（Run 可选参数带入，事件环境变量 giver/buff/duration 端口解析用）</summary>
        private static Card ctx_giver;
        private static BuffData ctx_buff;
        private static int ctx_duration;

        /// <summary>当前图事件上下文（RunEvent 广播中非空；事件入口 subject/source/value 端口与 阻止本事件/修改事件值 动作读取）</summary>
        private static GraphEventContext cur_event;

        /// <summary>「触发按钮效果」动作的递归深度计数（防按钮图自环死循环，上限 8 层）</summary>
        private static int trigger_depth;

        /// <summary>正在逐元素求值的 111012 筛选节点 id（防条件链回环引用自身导致无限递归）</summary>
        private static readonly HashSet<string> evaluating_filters = new HashSet<string>();

        /// <summary>沿执行流(Flow)边走边执行（与 CardPoolIO 编译逻辑同规）：
        /// 分支动作(212001)求值真值只走选中分支；重复动作(212002)逐次展开循环体（内联执行，repeatTime 随迭代变化）；
        /// 取值线（如 repeatTime 的 Int32 输出）不沿走；引脚找不到的连线（旧图残留）跳过。</summary>
        private static void WalkNode(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player, HashSet<string> visited, ref int executed)
        {
            if (node == null || !visited.Add(node.id))
                return;

            if (node.type != GraphNodeType.Action && !string.IsNullOrEmpty(node.category) && IsSupportedAction(node.action))
            {
                //白名单动作被存成了取值类型 → 旧图残留（分类规则修复前保存的），提示重拖
                Debug.LogWarning("[NodeDoc] 节点 " + node.action + "(" + node.title + ") 是旧的取值类型，未接入执行线，请删除后从节点库重新拖入并保存卡牌");
            }

            if (node.type != GraphNodeType.Action || string.IsNullOrEmpty(node.category))
            {
                WalkFlowOutputs(logic, graph, node, null, caster, target_card, target_player, visited, ref executed);
                return;
            }

            if (node.action == "212002" || node.action == "212005")
            {
                //防自环：循环体走回自身时跳过（当前正在按该节点迭代）
                if (loops.Exists(l => l.node_id == node.id))
                    return;
                if (node.action == "212002")
                    ExecuteLoopNode(logic, graph, node, caster, target_card, target_player, visited, ref executed);
                else
                    ExecuteLoopUntilNode(logic, graph, node, caster, target_card, target_player, visited, ref executed);
                return;
            }

            if (node.action == "212006" || node.action == "212007")
            {
                //停止/跳过重复动作：只设置标记，不再沿出口继续（循环体剩余部分被截断）
                if (loops.Count > 0)
                {
                    if (node.action == "212006") loop_break = true;
                    else loop_continue = true;
                    Debug.Log("[NodeDoc] " + (node.action == "212006" ? "停止重复动作(break)" : "跳过重复动作(continue)"));
                }
                else
                    Debug.LogWarning("[NodeDoc] " + node.action + " 不在循环体内，已忽略");
                return;
            }

            ExecuteAction(logic, graph, node, caster, target_card, target_player);
            executed++;
            WalkFlowOutputs(logic, graph, node, null, caster, target_card, target_player, visited, ref executed);
        }

        /// <summary>沿节点的 Flow 输出连线继续走。only_pin 非空时只走该短名出口（循环体的 动作 口 / 续接的 out 口）</summary>
        private static void WalkFlowOutputs(GameLogic logic, GraphData graph, GraphNode node, string only_pin,
            Card caster, Card target_card, Player target_player, HashSet<string> visited, ref int executed)
        {
            bool is_branch = node.type == GraphNodeType.Action && node.action == "212001";
            string chosen_branch = null;
            if (is_branch)
            {
                bool is_true = GetBoolInput(logic, graph, node, "isTrue", caster, target_card, target_player,
                    GraphRuntime.GetFieldString(node, "isTrue", "true") == "true");
                chosen_branch = is_true ? "thenAction" : "elseAction";
                Debug.Log("[NodeDoc] 分支动作 isTrue=" + is_true + " → 走 " + (is_true ? "动作" : "否则动作"));
            }
            foreach (GraphLink link in graph.GetOutgoing(node.id))
            {
                GraphPin out_pin = graph.GetPin(node.id, link.from_pin);
                //连线引用的引脚找不到（旧图残留）不跟——否则条件短路会让两个分支都执行
                if (out_pin == null)
                    continue;
                if (out_pin.type != NodeValueType.Flow && out_pin.type != NodeValueType.None)
                    continue;   //取值线（如 212002 的 repeatTime）不沿走
                //被动效果入口的 生效/失效动作 出口为预留（状态切换语义未接入），不驱动执行
                if (node.action == "PassiveEffect" && (out_pin.name == "enable" || out_pin.name == "disable"))
                    continue;
                //分支节点：只走选中的分支口 + 通用续接口(out)
                if (is_branch && out_pin.name != chosen_branch && out_pin.name != "out")
                    continue;
                if (only_pin != null && out_pin.name != only_pin)
                    continue;
                GraphNode next = graph.GetNode(link.to_node);
                if (next != null)
                    WalkNode(logic, graph, next, caster, target_card, target_player, visited, ref executed);
            }
        }

        /// <summary>执行 212002 重复动作：count 次迭代，每次从 动作 分支口展开循环体（全新 visited，允许按迭代重放），
        /// 循环结束后沿 out 续接口继续执行链（沿用外层 visited 防环）。</summary>
        private static void ExecuteLoopNode(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player, HashSet<string> visited, ref int executed)
        {
            int count = GetIntInput(logic, graph, node, "count", caster, target_card, target_player,
                GraphRuntime.GetFieldInt(node, "count", 0));
            for (int i = 1; i <= count; i++)
            {
                Debug.Log("[NodeDoc] 重复动作 第 " + i + "/" + count + " 次");
                loops.Add(new LoopCtx { node_id = node.id, iter = i });
                WalkFlowOutputs(logic, graph, node, "action", caster, target_card, target_player, new HashSet<string>(), ref executed);
                loops.RemoveAt(loops.Count - 1);
                if (ConsumeLoopFlags()) break;      //212006 停止：结束整个循环
            }
            WalkFlowOutputs(logic, graph, node, "out", caster, target_card, target_player, visited, ref executed);
        }

        /// <summary>消费循环控制标记。返回 true=应结束整个循环（break）；false=继续下一轮。
        /// continue（跳过本轮）语义已由 212007 节点不再沿出口走实现，这里只清标记。</summary>
        private static bool ConsumeLoopFlags()
        {
            if (loop_break)
            {
                loop_break = false;
                return true;
            }
            if (loop_continue)
            {
                loop_continue = false;
                Debug.Log("[NodeDoc] 跳过本轮剩余部分（continue）");
            }
            return false;
        }

        /// <summary>执行 212005 重复动作直到：条件口为假时反复展开循环体，直到条件成立或达到 maxRepeatTime
        /// （0=不限次数，硬上限 1000 次防死循环）；repeatTime 输出当前迭代序号，支持 212006/212007。</summary>
        private static void ExecuteLoopUntilNode(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player, HashSet<string> visited, ref int executed)
        {
            int max = GetIntInput(logic, graph, node, "maxRepeatTime", caster, target_card, target_player,
                GraphRuntime.GetFieldInt(node, "maxRepeatTime", 0));
            int i = 0;
            while (max <= 0 || i < max)
            {
                if (i >= 1000)
                {
                    Debug.LogWarning("[NodeDoc] 212005 重复动作直到超过硬上限 1000 次，强制结束（检查条件口是否接对）");
                    break;
                }
                //条件口无连线视为成立（立即退出），避免无限空转
                bool cond = GetBoolInput(logic, graph, node, "condition", caster, target_card, target_player, true);
                if (cond)
                    break;
                i++;
                Debug.Log("[NodeDoc] 重复动作直到 第 " + i + " 次（上限 " + (max > 0 ? max.ToString() : "不限") + "）");
                loops.Add(new LoopCtx { node_id = node.id, iter = i });
                WalkFlowOutputs(logic, graph, node, "action", caster, target_card, target_player, new HashSet<string>(), ref executed);
                loops.RemoveAt(loops.Count - 1);
                if (ConsumeLoopFlags()) break;
            }
            WalkFlowOutputs(logic, graph, node, "out", caster, target_card, target_player, visited, ref executed);
        }

        /// <summary>整数输入口取值：内置值节点走 GraphRuntime 求值；NodeDoc 取值节点走 ResolveNodeInt；无连线用字段默认</summary>
        private static int GetIntInput(GameLogic logic, GraphData graph, GraphNode act, string pin_name,
            Card caster, Card target_card, Player target_player, int def)
        {
            if (graph != null && act != null)
            {
                GraphPin pin = graph.GetPinByName(act.id, pin_name);
                if (pin != null)
                {
                    GraphLink link = graph.GetIncomingLink(act.id, pin.id);
                    if (link != null)
                    {
                        GraphNode src = graph.GetNode(link.from_node);
                        if (src != null && string.IsNullOrEmpty(src.category) && src.type == GraphNodeType.Value)
                            return GraphRuntime.EvaluateValue(graph, src);
                        if (src != null && src.type == GraphNodeType.Event)
                        {
                            //事件入口整数口：pos=事件主体所在牌堆位置；其余口（value）=事件数值
                            GraphPin op = graph.GetPin(link.from_node, link.from_pin);
                            string on = op != null ? op.name : link.from_pin;
                            if (on == "pos")
                                return EventCardPos(logic);
                            return cur_event != null ? cur_event.value : ctx_duration;
                        }
                        if (src != null && !string.IsNullOrEmpty(src.category))
                        {
                            //212002/212005 循环节点的 重复次数 输出口：按循环上下文返回当前迭代序号（1..n）
                            if (src.action == "212002" || src.action == "212005")
                            {
                                GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                                if (out_pin != null && out_pin.name == "repeatTime")
                                {
                                    LoopCtx ctx = loops.Find(l => l.node_id == link.from_node);
                                    if (ctx != null)
                                        return ctx.iter;
                                }
                            }
                            int? v = ResolveNodeInt(logic, graph, src, caster, target_card, target_player);
                            if (v != null)
                                return v.Value;
                        }
                        return def;     //其他来源（NodeDoc 取值节点暂未支持整数输出）
                    }
                }
            }
            return GraphRuntime.GetFieldInt(act, pin_name, def);
        }

        /// <summary>求值输出整数的 NodeDoc 取值节点（按来源节点 action 分发）；不是整数来源返回 null。
        /// 111004 元素数量 / 102027 卡牌属性 / 106004 增益属性 / 111014 求和 / 111015 最小 / 111016 最大 /
        /// 111017 平均 / 112007 临时变量 / 112010 按条件选值。</summary>
        private static int? ResolveNodeInt(GameLogic logic, GraphData graph, GraphNode src,
            Card caster, Card target_card, Player target_player)
        {
            //事件节点整数输出：图事件广播中=事件数值(value)；增益触发图（无广播）=剩余回合(duration)
            if (src.type == GraphNodeType.Event)
                return cur_event != null ? cur_event.value : ctx_duration;
            switch (src.action)
            {
                case "111004":
                {
                    //定义集合通道优先（103001 全量定义等），否则回退卡牌集合
                    List<CardData> defs = ResolveValueDefines(logic, graph, src, "collection", caster, target_card, target_player);
                    if (defs.Count > 0)
                        return defs.Count;
                    return ResolveValueCards(logic, graph, src, caster, target_card, target_player).Count;
                }
                case "102027":
                {
                    //获取卡牌属性：卡牌口按取值线解析，属性名下拉（攻击/生命/法力费用）
                    Card c = ResolveInputCard(logic, graph, src, "card", caster, target_card, target_player);
                    return c != null ? GetCardProp(c, GraphRuntime.GetFieldString(src, "propName", "攻击")) : (int?)null;
                }
                case "102004":   //获取属性（卡牌版，属性名可自由填；已知名走实际值，未知名按攻击）
                {
                    Card c = ResolveInputCard(logic, graph, src, "card", caster, target_card, target_player);
                    return c != null ? GetCardProp(c, GraphRuntime.GetFieldString(src, "propName", "攻击")) : (int?)null;
                }
                case "103020":   //获取卡牌定义属性（攻击/生命/法力费用）
                {
                    CardData d = ResolveInputDefine(logic, graph, src, "card", caster, target_card, target_player);
                    return d != null ? DefineProp(d, GraphRuntime.GetFieldString(src, "propName", "攻击")) : (int?)null;
                }
                case "106004":
                {
                    //获取增益属性：buff_id + 属性名 → 读 Buff 实例 props（无 buff_id 回退旧路径读原生加成状态）
                    Card c = ResolveInputCard(logic, graph, src, "card", caster, target_card, target_player);
                    if (c == null)
                        return null;
                    string prop = GraphRuntime.GetFieldString(src, "prop", "攻击加成");
                    string buff_id = GraphRuntime.GetFieldString(src, "buff_id", "");
                    if (!string.IsNullOrEmpty(buff_id))
                        return BuffRuntime.GetPropValue(c, buff_id, prop);
                    return GetBuffProp(c, prop);
                }
                case "111014":
                case "111015":
                case "111016":
                case "111017":
                {
                    List<int> vals = ResolveIntCollection(logic, graph, src, caster, target_card, target_player);
                    if (vals.Count == 0)
                        return 0;
                    switch (src.action)
                    {
                        case "111015":
                        {
                            int min = vals[0];
                            foreach (int v in vals) if (v < min) min = v;
                            return min;
                        }
                        case "111016":
                        {
                            int max = vals[0];
                            foreach (int v in vals) if (v > max) max = v;
                            return max;
                        }
                        case "111017":
                        {
                            long sum = 0;
                            foreach (int v in vals) sum += v;
                            return (int)(sum / vals.Count);
                        }
                        default:
                        {
                            long sum = 0;
                            foreach (int v in vals) sum += v;
                            return (int)sum;
                        }
                    }
                }
                case "112007":
                {
                    object v = GetTempVar(src);
                    if (v is int i)
                        return i;
                    if (v != null && int.TryParse(v.ToString(), out int parsed))
                        return parsed;
                    return null;
                }
                case "101014":   //获取当前灵力值
                {
                    Player p = ResolveValuePlayer(logic, graph, src, caster, target_player);
                    return p != null ? p.mana : (int?)null;
                }
                case "101015":   //获取灵力上限
                {
                    Player p = ResolveValuePlayer(logic, graph, src, caster, target_player);
                    return p != null ? p.mana_max : (int?)null;
                }
                case "101018":   //获取玩家的当前回合数（v1 简化：返回全局回合数，未按玩家拆分）
                {
                    return logic != null ? logic.GameData.turn_count : (int?)null;
                }
                case "109008":   //获取当前回合数
                    return logic != null ? logic.GameData.turn_count : (int?)null;
                case "112008":   //X到Y之间的随机整数（含两端）
                {
                    int x = GraphRuntime.GetFieldInt(src, "countX", 0);
                    int y = GraphRuntime.GetFieldInt(src, "countY", 0);
                    return Random.Range(Mathf.Min(x, y), Mathf.Max(x, y) + 1);
                }
                case "103010":   //获取卡牌定义花费
                {
                    CardData d = ResolveValueDefine(logic, graph, src, caster, target_card, target_player);
                    return d != null ? d.mana : (int?)null;
                }
                case "103011":   //获取卡牌定义攻击力
                {
                    CardData d = ResolveValueDefine(logic, graph, src, caster, target_card, target_player);
                    return d != null ? d.attack : (int?)null;
                }
                case "103012":   //获取卡牌定义生命值
                {
                    CardData d = ResolveValueDefine(logic, graph, src, caster, target_card, target_player);
                    return d != null ? d.hp : (int?)null;
                }
                case "112010":
                {
                    object v = SelectConditionalValue(logic, graph, src, caster, target_card, target_player);
                    if (v is int i)
                        return i;
                    if (v != null && int.TryParse(v.ToString(), out int parsed))
                        return parsed;
                    return null;
                }
                case "102005":   //获取卡牌花费
                case "102006":   //获取卡牌攻击力
                case "102007":   //获取卡牌最大生命值
                case "102008":   //获取卡牌当前生命值
                {
                    Card c = ResolveInputCard(logic, graph, src, "card", caster, target_card, target_player);
                    if (c == null)
                        return null;
                    switch (src.action)
                    {
                        case "102005": return c.GetMana();
                        case "102006": return c.GetAttack();
                        case "102007": return c.GetHPMax();
                        default:       return c.GetHP();
                    }
                }
                case "101002":   //获取玩家属性（Object 输出，v1 支持整数字段：生命/最大生命/灵力/灵力上限/击杀数）
                {
                    Player p = ResolveValuePlayer(logic, graph, src, caster, target_player);
                    if (p == null)
                        return null;
                    switch (GraphRuntime.GetFieldString(src, "propName", "生命"))
                    {
                        case "最大生命":
                        case "最大生命值":
                            return p.hp_max;
                        case "灵力":
                        case "灵力值":
                        case "法力":
                        case "法力值":
                            return p.mana;
                        case "灵力上限":
                        case "最大灵力":
                            return p.mana_max;
                        case "击杀数":
                            return p.kill_count;
                        default:
                            return p.hp;
                    }
                }
                case "111034":   //获取元素在集合中的位置（0 起；找不到返回 -1）
                {
                    List<Card> col = ResolveValueCards(logic, graph, src, caster, target_card, target_player, "array");
                    Card el = ResolveInputCard(logic, graph, src, "element", caster, target_card, target_player);
                    return el != null ? col.IndexOf(el) : -1;
                }
                case "112003":   //整数常量：取字段值
                    return GraphRuntime.GetFieldInt(src, "value", 0);
                case "102019":   //获取护甲值（卡牌上的 Armor 状态值）
                {
                    Card c = ResolveInputCard(logic, graph, src, "card", caster, target_card, target_player);
                    return c != null ? c.GetStatusValue(StatusType.Armor) : (int?)null;
                }
                case "108002":   //获取变量（数值）
                    return EventVarInt(src);
                default:
                    return null;    //不是整数来源
            }
        }

        /// <summary>布尔输入口取值：内置值/条件节点走 GraphRuntime；NodeDoc 节点走条件求值（112005 逻辑运算等）；无连线用字段默认</summary>
        private static bool GetBoolInput(GameLogic logic, GraphData graph, GraphNode act, string pin_name,
            Card caster, Card target_card, Player target_player, bool def)
        {
            if (graph != null && act != null)
            {
                GraphPin pin = graph.GetPinByName(act.id, pin_name);
                if (pin != null)
                {
                    GraphLink link = graph.GetIncomingLink(act.id, pin.id);
                    if (link != null)
                    {
                        GraphNode src = graph.GetNode(link.from_node);
                        if (src != null)
                        {
                            //事件环境变量：has_target（有无目标）——分支动作「有目标→A / 无目标→B」的判定来源
                            if (src.type == GraphNodeType.Event)
                            {
                                GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                                string out_name = out_pin != null ? out_pin.name : link.from_pin;
                                if (out_name == "has_target" || out_name == "hasTarget")
                                    return target_card != null;
                                return def;
                            }
                            if (string.IsNullOrEmpty(src.category))
                            {
                                if (src.type == GraphNodeType.Value)
                                    return GraphRuntime.EvaluateValue(graph, src) != 0;
                                if (src.type == GraphNodeType.Condition)
                                    return GraphRuntime.EvaluateCondition(graph, src);
                            }
                            else
                            {
                                if (src.action == "112007")
                                {
                                    object v = GetTempVar(src);
                                    if (v is bool b)
                                        return b;
                                    if (v is int i)
                                        return i != 0;
                                    return def;
                                }
                                if (src.action == "112010")
                                {
                                    object v = SelectConditionalValue(logic, graph, src, caster, target_card, target_player);
                                    if (v is bool b)
                                        return b;
                                    if (v is int i)
                                        return i != 0;
                                    return def;
                                }
                                return EvaluateConditionNode(logic, graph, src, caster, target_card);
                            }
                            return def;
                        }
                    }
                }
            }
            string s = GraphRuntime.GetFieldString(act, pin_name, def ? "true" : "false");
            return s == "true" || s == "1";
        }

        /// <summary>读取 112007 临时变量（按节点的 variableName 字段）</summary>
        private static object GetTempVar(GraphNode node)
        {
            string name = GraphRuntime.GetFieldString(node, "variableName", "");
            return !string.IsNullOrEmpty(name) && temp_vars.TryGetValue(name, out object v) ? v : null;
        }

        /// <summary>求值 112010 根据条件选择值：isTrue 为真取 value 口，否则取 elseValue 口</summary>
        private static object SelectConditionalValue(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            bool is_true = GetBoolInput(logic, graph, node, "isTrue", caster, target_card, target_player,
                GraphRuntime.GetFieldString(node, "isTrue", "true") == "true");
            return GetObjectInput(logic, graph, node, is_true ? "value" : "elseValue",
                caster, target_card, target_player);
        }

        /// <summary>Object 型输入口通用取值（212004 的 值 口 / 112010 的分支值口）：
        /// 按来源节点类型依次尝试 临时变量/按条件选值/集合/卡牌/玩家/整数；无连线返回字段常量字符串。</summary>
        private static object GetObjectInput(GameLogic logic, GraphData graph, GraphNode act, string pin_name,
            Card caster, Card target_card, Player target_player)
        {
            if (graph != null && act != null)
            {
                GraphPin pin = graph.GetPinByName(act.id, pin_name);
                if (pin != null)
                {
                    GraphLink link = graph.GetIncomingLink(act.id, pin.id);
                    if (link != null)
                    {
                        GraphNode src = graph.GetNode(link.from_node);
                        if (src != null)
                        {
                            if (src.type == GraphNodeType.Event)
                            {
                                //事件环境变量（触发器节点输出口 → Object 口）：自身/目标/有无目标/己方/敌方玩家；
                                //增益触发节点另有 施加者/增益定义/剩余回合（Run 可选参数带入的增益图上下文）
                                GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                                string out_name = out_pin != null ? out_pin.name : link.from_pin;
                                if (out_name == "self")
                                    return caster;
                                if (out_name == "card" || out_name == "subject")
                                    return cur_event != null ? (cur_event.card ?? caster) : caster;
                                if (out_name == "source")
                                    return cur_event != null ? cur_event.source_card : null;
                                if (out_name == "value")
                                    return cur_event != null ? (object)cur_event.value : null;
                                if (out_name == "target" || out_name == "target_card")
                                    return target_card;
                                if (out_name == "giver")
                                    return ctx_giver;
                                if (out_name == "buff" || out_name == "buff_id")
                                    return ctx_buff;
                                if (out_name == "duration")
                                    return ctx_duration;
                                if (out_name == "player")
                                    return target_player ?? PlayerOf(logic, caster);
                                if (out_name == "enemy")
                                    return OpponentOf(logic, caster);
                                if (out_name == "has_target" || out_name == "hasTarget")
                                    return target_card != null;
                                return null;
                            }
                            if (string.IsNullOrEmpty(src.category))
                            {
                                if (src.type == GraphNodeType.Value)
                                    return GraphRuntime.EvaluateValue(graph, src);
                                return null;
                            }
                            if (src.action == "108002")
                                return EventVarObject(src, caster, target_card);
                            if (src.action == "103023")   //获取卡牌定义的类型（返回中文类型名，可直接与卡牌类型字段比较）
                            {
                                CardData d = ResolveInputDefine(logic, graph, src, "cardDefine", caster, target_card, target_player);
                                return d != null ? CardTypeName(d.type) : null;
                            }
                            if (src.action == "103020")   //获取卡牌定义属性（Object 通道）
                            {
                                CardData d = ResolveInputDefine(logic, graph, src, "card", caster, target_card, target_player);
                                return d != null ? (object)DefineProp(d, GraphRuntime.GetFieldString(src, "propName", "攻击")) : null;
                            }
                            if (src.action == "102004")   //获取属性（Object 通道）
                            {
                                Card oc = ResolveInputCard(logic, graph, src, "card", caster, target_card, target_player);
                                return oc != null ? (object)GetCardProp(oc, GraphRuntime.GetFieldString(src, "propName", "攻击")) : null;
                            }
                            if (src.action == "112007")
                                return GetTempVar(src);
                            if (src.action == "112010")
                                return SelectConditionalValue(logic, graph, src, caster, target_card, target_player);
                            List<Card> col = ResolveCollectionNode(logic, graph, src, caster, target_card, target_player);
                            if (col != null)
                                return col;
                            Card c = ResolveValueCard(logic, graph, src, caster, target_card, target_player);
                            if (c != null)
                                return c;
                            Player p = ResolvePlayerOutput(logic, graph, src, caster, target_player);
                            if (p != null)
                                return p;
                            int? i = ResolveNodeInt(logic, graph, src, caster, target_card, target_player);
                            if (i != null)
                                return i.Value;
                        }
                    }
                }
            }
            return GraphRuntime.GetFieldString(act, pin_name, "");   //无连线：字段常量（字符串形式）
        }

        /// <summary>按 defineId 把 NodeDoc 动作落到 TCG2 游戏逻辑（v1 白名单）</summary>
        private static void ExecuteAction(GameLogic logic, GraphData graph, GraphNode act, Card caster,
            Card target_card, Player target_player)
        {
            switch (act.action)
            {
                case "202001":   //造成伤害（目标/伤害源/数值支持取值线，来自入口节点输出或常量）
                {
                    int value = GetIntInput(logic, graph, act, "value", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "value", 1));
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    Card source = ResolveInputCard(logic, graph, act, "source", caster, target_card, target_player) ?? caster;
                    if (tcard != null)
                        logic.DamageCard(source ?? caster, tcard, value, true);
                    else
                    {
                        Player tplayer = ResolveInputPlayer(logic, graph, act, "card", caster, target_player);
                        if (tplayer != null)
                            logic.DamagePlayer(source ?? caster, tplayer, value);
                    }
                    break;
                }
                case "202041":   //造成伤害或法伤（卡池主流伤害动作）：伤害+加成、多目标、是否法伤（法伤无视护甲/免疫/践踏/吸血）
                {
                    int value = GetIntInput(logic, graph, act, "damage", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "damage", 0))
                              + GetIntInput(logic, graph, act, "damage2", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "damage2", 0));
                    bool spell = GetBoolInput(logic, graph, act, "damagetype", caster, target_card, target_player,
                        GraphRuntime.GetFieldString(act, "damagetype", "false") == "true");
                    Card source = ResolveInputCard(logic, graph, act, "damageSource", caster, target_card, target_player) ?? caster;
                    List<Card> targets = ResolveInputCards(logic, graph, act, "targets", caster, target_card);
                    Debug.Log("[NodeDoc] 202041 造成伤害 value=" + value + " spell=" + spell + " 目标数=" + targets.Count
                        + (targets.Count > 0 ? " 首目标=" + targets[0].CardData?.id : (target_player != null ? " 玩家目标=p" + target_player.player_id : " 无目标")));
                    if (targets.Count > 0)
                    {
                        foreach (Card t in targets)
                            logic.DamageCard(source, t, value, spell);
                    }
                    else if (target_player != null)
                    {
                        logic.DamagePlayer(source, target_player, value);   //能力目标是玩家（如目标类型=英雄）
                    }
                    break;
                }
                case "202016":   //消灭（对取值线解析出的目标卡）
                {
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (tcard != null)
                    {
                        if (logic.GameData.IsOnBoard(tcard))
                            logic.KillCard(caster, tcard);
                        else
                            logic.DiscardCard(tcard);
                    }
                    break;
                }
                case "202013":   //治疗目标卡牌
                case "202039":
                case "202047":
                {
                    int value = GetIntInput(logic, graph, act, "value", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "value", 1));
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (tcard != null)
                        logic.HealCard(tcard, value);
                    else
                    {
                        Player tplayer = ResolveInputPlayer(logic, graph, act, "card", caster, target_player);
                        if (tplayer != null)
                            logic.HealPlayer(tplayer, value);
                    }
                    break;
                }
                case "212001":   //分支动作：控制节点，分支选择在 ReachableActions 遍历时处理
                    break;
                case "210001":   //简单抽牌：使玩家抽一张（卡库顶；手牌满时 TCG2 不抽也不爆牌，与 zmcs 爆牌语义有差异）
                {
                    Player player = ResolveInputPlayer(logic, graph, act, "player", caster, target_player)
                        ?? PlayerOf(logic, caster);     //玩家口无连线：默认施法卡所属玩家
                    if (player == null)
                    {
                        Debug.LogWarning("[NodeDoc] 210001 简单抽牌失败：无目标玩家");
                        break;
                    }
                    logic.DrawCard(player, 1);
                    Debug.Log("[NodeDoc] 210001 简单抽牌 → 玩家 p" + player.player_id + "（手牌 " + player.cards_hand.Count + "）");
                    break;
                }
                case "201003":   //抽目标卡牌：把取值线解析出的目标卡从其拥有者的卡库抽到手牌（检索类动作）
                {
                    Card tcard = ResolveInputCard(logic, graph, act, "targetCard", caster, target_card, target_player);
                    if (tcard == null)
                    {
                        Debug.LogWarning("[NodeDoc] 201003 抽目标卡牌失败：无目标卡");
                        break;
                    }
                    Player owner = logic.GameData.GetPlayer(tcard.player_id);
                    if (owner == null || !owner.cards_deck.Contains(tcard))
                    {
                        Debug.LogWarning("[NodeDoc] 201003 抽目标卡牌失败：目标卡 " + tcard.CardData?.id + " 不在拥有者卡库中");
                        break;
                    }
                    owner.cards_deck.Remove(tcard);
                    if (owner.cards_hand.Count < GameplayData.Get().cards_max)
                    {
                        owner.cards_hand.Add(tcard);
                        logic.TriggerPlayerCardsAbilityType(owner, AbilityTrigger.OnDraw);
                        Debug.Log("[NodeDoc] 201003 抽目标卡牌 " + tcard.CardData?.id + " → 玩家 p" + owner.player_id);
                    }
                    else
                    {
                        owner.cards_discard.Add(tcard);     //手牌满：直接进墓地（爆牌）
                        Debug.Log("[NodeDoc] 201003 抽目标卡牌 " + tcard.CardData?.id + " 手牌满 → 爆牌进墓地");
                    }
                    break;
                }
                case "206002":   //移除增益：buff_id 引用 BuffData 移除实例（含原生状态重建）；
                                 //无 buff_id 时回退旧 prop 下拉（攻击/生命/全部）移除加成状态
                {
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (tcard == null)
                    {
                        Debug.LogWarning("[NodeDoc] 206002 移除增益失败：无目标卡");
                        break;
                    }
                    string buff_id = GraphRuntime.GetFieldString(act, "buff_id", "");
                    if (!string.IsNullOrEmpty(buff_id))
                    {
                        if (!BuffRuntime.HasBuff(tcard, buff_id))
                        {
                            Debug.LogWarning("[NodeDoc] 206002 移除增益：卡牌无该增益 buff_id=\"" + buff_id + "\"");
                            break;
                        }
                        //增益图「移除增益时/后」事件（移除前/后各触发一次，防递归由 RunGraph 深度限制兜底）
                        BuffData bdef2 = BuffPoolIO.Get(buff_id);
                        int left = BuffRuntime.GetRemainingDuration(tcard, buff_id);
                        BuffRuntime.RunGraph(logic, tcard, bdef2, "OnBuffRemoving", caster, left);
                        BuffRuntime.RemoveBuff(tcard, buff_id);
                        BuffRuntime.RunGraph(logic, tcard, bdef2, "OnBuffRemoved", caster, 0);
                        Debug.Log("[NodeDoc] 206002 移除增益 " + tcard.CardData?.id + " buff_id=" + buff_id);
                        break;
                    }
                    string prop = GraphRuntime.GetFieldString(act, "prop", "全部");
                    if (prop != "生命")
                        tcard.RemoveStatus(StatusType.AddAttack);
                    if (prop != "攻击")
                        tcard.RemoveStatus(StatusType.AddHP);
                    Debug.Log("[NodeDoc] 206002 移除增益 " + tcard.CardData?.id + " prop=" + prop);
                    break;
                }
                case "206003":   //设置增益属性：buff_id + 属性名 + 值 → 写 Buff 实例（原生映射同步）；
                                 //无 buff_id 时回退旧逻辑（直接改加成状态）
                {
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (tcard == null)
                    {
                        Debug.LogWarning("[NodeDoc] 206003 设置增益属性失败：无目标卡");
                        break;
                    }
                    int value = GetIntInput(logic, graph, act, "value", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "value", 0));
                    string prop = GraphRuntime.GetFieldString(act, "prop", "攻击加成");
                    string buff_id = GraphRuntime.GetFieldString(act, "buff_id", "");
                    if (!string.IsNullOrEmpty(buff_id))
                    {
                        if (!BuffRuntime.HasBuff(tcard, buff_id))
                        {
                            Debug.LogWarning("[NodeDoc] 206003 设置增益属性：卡牌无该增益 buff_id=\"" + buff_id + "\"");
                            break;
                        }
                        BuffRuntime.SetPropValue(tcard, buff_id, prop, value);
                        Debug.Log("[NodeDoc] 206003 设置增益属性 " + tcard.CardData?.id + " " + buff_id + "." + prop + "=" + value);
                        break;
                    }
                    StatusType type = prop == "生命加成" ? StatusType.AddHP : StatusType.AddAttack;
                    CardStatus st = tcard.GetStatus(type);
                    if (st == null)
                    {
                        //没有现成状态则按值创建（持续回合类属性则先确保状态存在，值按 1 兜底）
                        if (prop == "持续回合")
                        {
                            tcard.AddStatus(StatusType.AddAttack, 0, value);
                            tcard.AddStatus(StatusType.AddHP, 0, value);
                            st = null;
                        }
                        else
                            tcard.AddStatus(type, value, 0);
                    }
                    else if (prop == "持续回合")
                    {
                        tcard.GetStatus(StatusType.AddAttack).duration = value;
                        CardStatus hp_st = tcard.GetStatus(StatusType.AddHP);
                        if (hp_st != null)
                            hp_st.duration = value;
                    }
                    else
                    {
                        st.value = value;
                    }
                    Debug.Log("[NodeDoc] 206003 设置增益属性 " + tcard.CardData?.id + " " + prop + "=" + value);
                    break;
                }
                case "210002":   //卡牌置入战场：把取值线解析出的卡牌逐张移到拥有者一侧的空位（走 PlayCard，会触发入场/打出，不扣费）
                {
                    List<Card> cards = ResolveInputCards(logic, graph, act, "cards", caster, target_card);
                    foreach (Card c in cards)
                    {
                        if (c == null)
                            continue;
                        Player owner = logic.GameData.GetPlayer(c.player_id);
                        if (owner == null)
                            continue;
                        Slot slot = FindEmptySlot(logic, owner);
                        if (slot == default)
                        {
                            Debug.LogWarning("[NodeDoc] 210002 卡牌置入战场失败：玩家 p" + owner.player_id + " 战场无空位（" + c.CardData?.id + "）");
                            continue;
                        }
                        logic.PlayCard(c, slot, true);
                        Debug.Log("[NodeDoc] 210002 卡牌置入战场 " + c.CardData?.id + " → 玩家 p" + owner.player_id + " " + slot);
                    }
                    break;
                }
                case "206001":   //添加增益：buff_id 引用 BuffData（BuffPoolIO 池）→ 施加实例（攻击/生命加成映射原生状态）；
                                 //无 buff_id 时回退旧字段（attack_add/hp_add/duration 数值加成）兼容旧图
                {
                    List<Card> targets = ResolveInputCards(logic, graph, act, "cards", caster, target_card);
                    string buff_id = GraphRuntime.GetFieldString(act, "buff_id", "");
                    if (!string.IsNullOrEmpty(buff_id))
                    {
                        BuffData bdef = BuffPoolIO.Get(buff_id);
                        if (bdef == null)
                        {
                            Debug.LogWarning("[NodeDoc] 206001 添加增益失败：BuffData 不存在 buff_id=\"" + buff_id + "\"");
                            break;
                        }
                        int dur = GraphRuntime.GetFieldInt(act, "duration", 0);
                        if (dur == 0)
                            dur = bdef.duration;
                        foreach (Card t in targets)
                        {
                            if (t == null)
                                continue;
                            //增益图「添加增益时/后」事件（防递归：嵌套触发由 BuffRuntime.RunGraph 深度限制兜底）
                            BuffRuntime.RunGraph(logic, t, bdef, "OnBuffAdding", caster, dur);
                            BuffRuntime.AddBuff(t, bdef, dur);
                            BuffRuntime.RunGraph(logic, t, bdef, "OnBuffAdded", caster, dur);
                        }
                        Debug.Log("[NodeDoc] 206001 添加增益 " + bdef.title + " 目标数=" + targets.Count + " dur=" + dur);
                        break;
                    }
                    int atk = GraphRuntime.GetFieldInt(act, "attack_add", 0);
                    int hp = GraphRuntime.GetFieldInt(act, "hp_add", 0);
                    int dur2 = GraphRuntime.GetFieldInt(act, "duration", 0);
                    foreach (Card t in targets)
                    {
                        if (t == null)
                            continue;
                        if (atk != 0)
                            t.AddStatus(StatusType.AddAttack, atk, dur2);
                        if (hp != 0)
                            t.AddStatus(StatusType.AddHP, hp, dur2);
                        if (atk == 0 && hp == 0)
                            Debug.LogWarning("[NodeDoc] 206001 添加增益：攻击/生命加成都是 0，未生效");
                    }
                    Debug.Log("[NodeDoc] 206001 添加增益 atk=" + atk + " hp=" + hp + " dur=" + dur2 + " 目标数=" + targets.Count);
                    break;
                }
                case "202037":   //设置卡牌属性：直接设置基础值（攻击/生命/法力费用；常驻修正保留，实值随 Get* 重算）
                {
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (tcard == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202037 设置卡牌属性失败：无目标卡");
                        break;
                    }
                    int value = GetIntInput(logic, graph, act, "value", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "value", 0));
                    string prop = GraphRuntime.GetFieldString(act, "propName", "攻击");
                    switch (prop)
                    {
                        case "生命":
                            tcard.hp = Mathf.Max(value, 0);
                            break;
                        case "法力":
                        case "法力费用":
                            tcard.mana = Mathf.Max(value, 0);
                            break;
                        default:    //攻击
                            tcard.attack = Mathf.Max(value, 0);
                            break;
                    }
                    Debug.Log("[NodeDoc] 202037 设置卡牌属性 " + tcard.CardData?.id + " " + prop + "=" + value);
                    break;
                }
                case "202003":   //创建衍生卡并置入战场（卡牌定义口 v1 为卡牌 id 字段或定义取值线；自动找该玩家一侧第一个空位）
                case "202004":   //创建衍生卡并置入手牌（手牌满则不创建并警告）
                {
                    Player player = ResolveInputPlayer(logic, graph, act, "player", caster, target_player)
                        ?? PlayerOf(logic, caster);     //玩家口无连线：默认施法卡所属玩家
                    string define_id = GraphRuntime.GetFieldString(act, "cardDefine", "");
                    CardData define = !string.IsNullOrEmpty(define_id) ? CardData.Get(define_id) : null;
                    if (define == null)     //定义口有取值线时按线解析（如 103001+111008 随机全量定义）
                        define = ResolveInputDefine(logic, graph, act, "cardDefine", caster, target_card, target_player);
                    if (player == null || define == null)
                    {
                        Debug.LogWarning("[NodeDoc] " + act.action + " 创建衍生卡失败："
                            + (player == null ? "无目标玩家" : "卡牌定义不存在（cardDefine=\"" + define_id + "\"）"));
                        break;
                    }
                    if (act.action == "202003")
                    {
                        Slot slot = FindEmptySlot(logic, player);
                        if (slot == default)
                        {
                            Debug.LogWarning("[NodeDoc] 202003 创建衍生卡失败：玩家 p" + player.player_id + " 战场无空位");
                            break;
                        }
                        Card created = logic.SummonCard(player, define, VariantData.GetDefault(), slot);
                        Debug.Log("[NodeDoc] 202003 创建衍生卡 " + define.id + " → 战场 " + slot + (created != null ? " 成功" : " 失败"));
                    }
                    else
                    {
                        if (player.cards_hand.Count >= GameplayData.Get().cards_max)
                        {
                            Debug.LogWarning("[NodeDoc] 202004 创建衍生卡失败：玩家 p" + player.player_id + " 手牌已满");
                            break;
                        }
                        Card created = logic.SummonCardHand(player, define, VariantData.GetDefault());
                        Debug.Log("[NodeDoc] 202004 创建衍生卡 " + define.id + " → 手牌（现有 " + player.cards_hand.Count + " 张）");
                    }
                    break;
                }
                case "212004":   //设置临时变量：变量名(String 字段) + 值口(Object，集合/卡牌/玩家/整数/常量均可)
                {
                    string var_name = GraphRuntime.GetFieldString(act, "variableName", "");
                    if (string.IsNullOrEmpty(var_name))
                    {
                        Debug.LogWarning("[NodeDoc] 212004 设置临时变量失败：变量名为空");
                        break;
                    }
                    temp_vars[var_name] = GetObjectInput(logic, graph, act, "value", caster, target_card, target_player);
                    Debug.Log("[NodeDoc] 212004 设置临时变量 " + var_name);
                    break;
                }
                case "202015":   //沉默：v1=清空卡牌身上所有状态/特性/持续效果（TCG2 无逐效果沉默概念）
                {
                    List<Card> targets = ResolveInputCards(logic, graph, act, "cards", caster, target_card);
                    foreach (Card t in targets)
                    {
                        if (t == null)
                            continue;
                        foreach (CardStatus st in t.GetAllStatus())
                            t.RemoveStatus(st.type);
                        t.traits.Clear();
                        t.ClearOngoing();
                        Debug.Log("[NodeDoc] 202015 沉默 " + t.CardData?.id);
                    }
                    break;
                }
                case "202028":   //获得控制权：目标卡移交指定玩家（v1 仅转移归属，卡所在区域不变）
                {
                    Player player = ResolveInputPlayer(logic, graph, act, "player", caster, target_player)
                        ?? PlayerOf(logic, caster);
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (player == null || tcard == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202028 获得控制权失败：" + (player == null ? "无目标玩家" : "无目标卡"));
                        break;
                    }
                    logic.ChangeOwner(tcard, player);
                    Debug.Log("[NodeDoc] 202028 获得控制权 " + tcard.CardData?.id + " → 玩家 p" + player.player_id);
                    break;
                }
                case "202029":   //变形为卡牌定义：卡牌口 + 卡牌定义（id 字段或定义取值线） + isreset 忽略（v1 不重置）
                {
                    Card tcard = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (tcard == null)
                    {
                        //未指定目标卡时默认变自身（"战吼变自己"的常见用法）
                        Debug.LogWarning("[NodeDoc] 202029 变形：未指定目标卡，默认变形为自身（如要变别人请把「卡牌」口连到目标卡）");
                        tcard = caster;
                    }
                    string define_id = GraphRuntime.GetFieldString(act, "define", "");
                    CardData define = !string.IsNullOrEmpty(define_id) ? CardData.Get(define_id) : null;
                    if (define == null)
                        define = ResolveInputDefine(logic, graph, act, "define", caster, target_card, target_player);
                    if (tcard == null || define == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202029 变形失败：" + (tcard == null ? "无目标卡" : "卡牌定义不存在（define=\"" + define_id + "\"）"));
                        break;
                    }
                    Card transformed = logic.TransformCard(tcard, define);
                    Debug.Log("[NodeDoc] 202029 变形 " + tcard.CardData?.id + " → " + define.id + (transformed != null ? " 成功" : " 失败"));
                    break;
                }
                case "202038":   //丢弃卡牌：hands 口的卡逐张进墓地
                {
                    List<Card> cards = ResolveInputCards(logic, graph, act, "hands", caster, target_card);
                    foreach (Card c in cards)
                    {
                        if (c != null)
                            logic.DiscardCard(c);
                    }
                    Debug.Log("[NodeDoc] 202038 丢弃卡牌 " + cards.Count + " 张");
                    break;
                }
                case "202044":   //复制卡牌：按目标牌堆下拉（手牌/战场/牌库）复制一份
                {
                    Card src = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (src == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202044 复制卡牌失败：无目标卡");
                        break;
                    }
                    Player owner = logic.GameData.GetPlayer(src.player_id);
                    string pile = GraphRuntime.GetFieldString(act, "targetPile", "手牌");
                    Card copy = null;
                    if (pile == "战场")
                    {
                        Slot slot = FindEmptySlot(logic, owner);
                        if (slot == default)
                            Debug.LogWarning("[NodeDoc] 202044 复制卡牌失败：战场无空位");
                        else
                            copy = logic.SummonCopy(owner, src, slot);
                    }
                    else if (pile == "牌库")
                    {
                        copy = logic.AddCardDeck(owner, src.CardData, src.VariantData);
                        logic.ShuffleDeck(owner.cards_deck);
                    }
                    else
                    {
                        copy = logic.SummonCopyHand(owner, src);
                    }
                    Debug.Log("[NodeDoc] 202044 复制卡牌 " + src.CardData?.id + " → " + pile + (copy != null ? " 成功" : " 失败"));
                    break;
                }
                case "202005":   //创建衍生卡并洗入牌库（卡牌定义口 v1 为卡牌 id 字段或定义取值线）
                {
                    Player player = ResolveInputPlayer(logic, graph, act, "player", caster, target_player)
                        ?? PlayerOf(logic, caster);
                    string define_id = GraphRuntime.GetFieldString(act, "cardDefine", "");
                    CardData define = !string.IsNullOrEmpty(define_id) ? CardData.Get(define_id) : null;
                    if (define == null)
                        define = ResolveInputDefine(logic, graph, act, "cardDefine", caster, target_card, target_player);
                    if (player == null || define == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202005 创建衍生卡失败："
                            + (player == null ? "无目标玩家" : "卡牌定义不存在（cardDefine=\"" + define_id + "\"）"));
                        break;
                    }
                    Card created = logic.AddCardDeck(player, define, VariantData.GetDefault());
                    logic.ShuffleDeck(player.cards_deck);
                    Debug.Log("[NodeDoc] 202005 创建衍生卡 " + define.id + " → 洗入牌库" + (created != null ? " 成功" : " 失败"));
                    break;
                }
                case "210003":   //卡牌移回手牌：从当前位置移回拥有者手牌（手牌满则跳过并警告）
                {
                    List<Card> cards = ResolveInputCards(logic, graph, act, "cards", caster, target_card);
                    foreach (Card c in cards)
                    {
                        if (c == null)
                            continue;
                        Player owner = logic.GameData.GetPlayer(c.player_id);
                        if (owner == null)
                            continue;
                        if (owner.cards_hand.Count >= GameplayData.Get().cards_max)
                        {
                            Debug.LogWarning("[NodeDoc] 210003 卡牌移回手牌失败：玩家 p" + owner.player_id + " 手牌已满（" + c.CardData?.id + "）");
                            continue;
                        }
                        owner.RemoveCardFromAllGroups(c);
                        owner.cards_hand.Add(c);
                        logic.TriggerPlayerCardsAbilityType(owner, AbilityTrigger.OnDraw);
                        Debug.Log("[NodeDoc] 210003 卡牌移回手牌 " + c.CardData?.id + " → 玩家 p" + owner.player_id);
                    }
                    break;
                }
                case "210004":   //卡牌洗入牌库：top=false 洗入全牌库，top=true 置于牌库顶
                {
                    List<Card> cards = ResolveInputCards(logic, graph, act, "cards", caster, target_card);
                    bool top = GetBoolInput(logic, graph, act, "top", caster, target_card, target_player,
                        GraphRuntime.GetFieldString(act, "top", "false") == "true");
                    foreach (Card c in cards)
                    {
                        if (c == null)
                            continue;
                        Player owner = logic.GameData.GetPlayer(c.player_id);
                        if (owner == null)
                            continue;
                        owner.RemoveCardFromAllGroups(c);
                        if (top)
                            owner.cards_deck.Insert(0, c);
                        else
                            owner.cards_deck.Add(c);
                        Debug.Log("[NodeDoc] 210004 卡牌洗入牌库 " + c.CardData?.id + " → 玩家 p" + owner.player_id + (top ? "（牌库顶）" : ""));
                    }
                    if (!top)
                    {
                        HashSet<int> owner_ids = new HashSet<int>();   //逐拥有者洗一次
                        foreach (Card c in cards)
                        {
                            if (c == null || !owner_ids.Add(c.player_id))
                                continue;
                            Player owner = logic.GameData.GetPlayer(c.player_id);
                            if (owner != null)
                                logic.ShuffleDeck(owner.cards_deck);
                        }
                    }
                    break;
                }
                case "210005":   //卡牌置入墓地（走 DiscardCard，场上卡会触发死亡）
                {
                    List<Card> cards = ResolveInputCards(logic, graph, act, "cards", caster, target_card);
                    foreach (Card c in cards)
                    {
                        if (c != null)
                            logic.DiscardCard(c);
                    }
                    Debug.Log("[NodeDoc] 210005 卡牌置入墓地 " + cards.Count + " 张");
                    break;
                }
                case "201008":   //增加当前灵力值（不超过灵力上限）
                {
                    Player player = ResolveInputPlayer(logic, graph, act, "player", caster, target_player)
                        ?? PlayerOf(logic, caster);
                    int count = GetIntInput(logic, graph, act, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "count", 0));
                    if (player != null)
                    {
                        player.mana = Mathf.Clamp(player.mana + count, 0, player.mana_max);
                        Debug.Log("[NodeDoc] 201008 增加当前灵力值 p" + player.player_id + " +" + count + " → " + player.mana + "/" + player.mana_max);
                    }
                    break;
                }
                case "201010":   //增加灵力上限（本回合当前灵力同步增加）
                {
                    Player player = ResolveInputPlayer(logic, graph, act, "player", caster, target_player)
                        ?? PlayerOf(logic, caster);
                    int count = GetIntInput(logic, graph, act, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "count", 0));
                    if (player != null)
                    {
                        player.mana_max = Mathf.Max(player.mana_max + count, 0);
                        player.mana = Mathf.Clamp(player.mana + count, 0, player.mana_max);
                        Debug.Log("[NodeDoc] 201010 增加灵力上限 p" + player.player_id + " +" + count + " → " + player.mana + "/" + player.mana_max);
                    }
                    break;
                }
                case "202014":   //禁锢（zmcs）= 施加 Freezing：回合末 Freezing值≥HP 判死
                {
                    Card tc = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (tc == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202014 禁锢失败：无目标卡");
                        break;
                    }
                    tc.AddStatus(StatusType.Freezing, 1, 0);
                    Debug.Log("[NodeDoc] 202014 禁锢 " + tc.CardData?.id);
                    break;
                }
                case "202040":   //封印（zmcs）= 施加 Silenced（禁用能力）
                {
                    List<Card> cards = ResolveInputCards(logic, graph, act, "cards", caster, target_card);
                    int n = 0;
                    foreach (Card tc in cards)
                    {
                        if (tc != null)
                        {
                            tc.AddStatus(StatusType.Silenced, 1, 0);
                            n++;
                        }
                    }
                    Debug.Log("[NodeDoc] 202040 封印 " + n + " 张");
                    break;
                }
                case "202018":   //增加护甲（护甲=StatusType.Armor，加在玩家英雄上）
                case "202019":   //增加固定数量护甲
                {
                    Player pl = ResolveInputPlayer(logic, graph, act, "player", caster, target_player) ?? PlayerOf(logic, caster);
                    int count = GetIntInput(logic, graph, act, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "count", 0));
                    if (pl == null || pl.hero == null)
                    {
                        Debug.LogWarning("[NodeDoc] " + act.action + " 护甲失败：无玩家/英雄");
                        break;
                    }
                    pl.hero.AddStatus(StatusType.Armor, Mathf.Max(count, 0), 0);
                    Debug.Log("[NodeDoc] " + act.action + " 护甲 +" + count + " → " + pl.hero.CardData?.id);
                    break;
                }
                case "202020":   //失去护甲
                {
                    Player pl = ResolveInputPlayer(logic, graph, act, "player", caster, target_player) ?? PlayerOf(logic, caster);
                    int count = GetIntInput(logic, graph, act, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "count", 0));
                    if (pl == null || pl.hero == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202020 失去护甲失败：无玩家/英雄");
                        break;
                    }
                    Card h = pl.hero;
                    int cur = h.GetStatusValue(StatusType.Armor);
                    h.RemoveStatus(StatusType.Armor);
                    if (cur > count)
                        h.AddStatus(StatusType.Armor, cur - count, 0);
                    Debug.Log("[NodeDoc] 202020 护甲 -" + count + " → " + Mathf.Max(cur - count, 0));
                    break;
                }
                case "201001":   //设置当前灵力值（=mana，钳到 [0, mana_max]）
                {
                    Player pl = ResolveInputPlayer(logic, graph, act, "player", caster, target_player) ?? PlayerOf(logic, caster);
                    int v = GetIntInput(logic, graph, act, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "count", 0));
                    if (pl != null)
                    {
                        pl.mana = Mathf.Clamp(v, 0, pl.mana_max);
                        Debug.Log("[NodeDoc] 201001 设置当前灵力值 → " + pl.mana + "/" + pl.mana_max);
                    }
                    break;
                }
                case "201009":   //设置灵力上限（=mana_max）
                {
                    Player pl = ResolveInputPlayer(logic, graph, act, "player", caster, target_player) ?? PlayerOf(logic, caster);
                    int v = GetIntInput(logic, graph, act, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(act, "count", 0));
                    if (pl != null)
                    {
                        pl.mana_max = Mathf.Max(v, 0);
                        pl.mana = Mathf.Min(pl.mana, pl.mana_max);
                        Debug.Log("[NodeDoc] 201009 设置灵力上限 → " + pl.mana + "/" + pl.mana_max);
                    }
                    break;
                }
                case "210007":   //卡牌装备到道具栏（=装备区）：EquipCard(英雄, 装备)
                {
                    Player pl = ResolveInputPlayer(logic, graph, act, "player", caster, target_player) ?? PlayerOf(logic, caster);
                    Card eq = ResolveInputCard(logic, graph, act, "card", caster, target_card, target_player);
                    if (pl == null || pl.hero == null || eq == null)
                    {
                        Debug.LogWarning("[NodeDoc] 210007 装备失败：无玩家/英雄/装备");
                        break;
                    }
                    logic.EquipCard(pl.hero, eq);
                    Debug.Log("[NodeDoc] 210007 装备 " + eq.CardData?.id + " → 英雄");
                    break;
                }
                case "202006":   //创建衍生卡并装备到道具栏
                {
                    Player pl = ResolveInputPlayer(logic, graph, act, "player", caster, target_player) ?? PlayerOf(logic, caster);
                    string define_id = GraphRuntime.GetFieldString(act, "cardDefine", "");
                    CardData define = !string.IsNullOrEmpty(define_id) ? CardData.Get(define_id) : null;
                    if (define == null)
                        define = ResolveInputDefine(logic, graph, act, "cardDefine", caster, target_card, target_player);
                    if (pl == null || pl.hero == null || define == null)
                    {
                        Debug.LogWarning("[NodeDoc] 202006 创建装备失败：" + (pl == null ? "无玩家" : define == null ? "卡牌定义不存在" : "无英雄"));
                        break;
                    }
                    Card created = logic.SummonCardHand(pl, define, VariantData.GetDefault());
                    if (created != null)
                        logic.EquipCard(pl.hero, created);
                    Debug.Log("[NodeDoc] 202006 创建并装备 " + define.id);
                    break;
                }
                case "200001":   //使玩家获胜：直接结束对局，该玩家为赢家
                {
                    Player w = ResolveInputPlayer(logic, graph, act, "players", caster, target_player) ?? PlayerOf(logic, caster);
                    if (w == null)
                    {
                        Debug.LogWarning("[NodeDoc] 200001 使玩家获胜失败：无目标玩家");
                        break;
                    }
                    logic.EndGame(w.player_id);
                    Debug.Log("[NodeDoc] 200001 使玩家获胜 p" + w.player_id);
                    break;
                }
                case "200002":   //使玩家失败：结束对局，其余玩家获胜
                {
                    Player l = ResolveInputPlayer(logic, graph, act, "losers", caster, target_player) ?? PlayerOf(logic, caster);
                    if (l == null)
                    {
                        Debug.LogWarning("[NodeDoc] 200002 使玩家失败失败：无目标玩家");
                        break;
                    }
                    Player winner = null;
                    foreach (Player pl in logic.GameData.players)
                    {
                        if (pl != null && pl != l)
                        {
                            winner = pl;
                            break;
                        }
                    }
                    if (winner != null)
                    {
                        logic.EndGame(winner.player_id);
                        Debug.Log("[NodeDoc] 200002 使玩家失败 p" + l.player_id + "（获胜 p" + winner.player_id + "）");
                    }
                    else
                        Debug.LogWarning("[NodeDoc] 200002 使玩家失败：找不到可获胜的对手");
                    break;
                }
                case "208002":   //阻止事件（zmcs）：等价「阻止本事件」，仅「X 时」有效
                {
                    if (cur_event == null)
                        Debug.LogWarning("[NodeDoc] 208002 阻止事件：当前无事件上下文，忽略");
                    else if (cur_event.phase == GraphEventPhase.After)
                        Debug.LogWarning("[NodeDoc] 208002 阻止事件：仅对「X 时」事件有效");
                    else
                        cur_event.cancelled = true;
                    break;
                }
                case "208001":   //设置变量（zmcs）：按变量名写回当前事件上下文
                {
                    if (cur_event == null)
                    {
                        Debug.LogWarning("[NodeDoc] 208001 设置变量：当前无事件上下文，忽略");
                        break;
                    }
                    string vn = GraphRuntime.GetFieldString(act, "varName", "数值");
                    if (vn == "数值")
                        cur_event.value = GetIntInput(logic, graph, act, "value", caster, target_card, target_player,
                            GraphRuntime.GetFieldInt(act, "value", 0));
                    else if (vn == "来源")
                        cur_event.source_card = ResolveInputCard(logic, graph, act, "value", caster, target_card, target_player);
                    else if (vn == "玩家")
                        cur_event.player = ResolveInputPlayer(logic, graph, act, "value", caster, target_player);
                    else    //卡牌
                        cur_event.card = ResolveInputCard(logic, graph, act, "value", caster, target_card, target_player);
                    Debug.Log("[NodeDoc] 208001 设置变量 " + vn);
                    break;
                }
                case "BlockEvent":   //阻止本事件：仅「X 时」广播内有效——取消本次原动作（先到先得，后续监听者不再收到）
                {
                    if (cur_event == null)
                        Debug.LogWarning("[NodeDoc] 阻止本事件：当前不在事件广播中，忽略");
                    else if (cur_event.phase == GraphEventPhase.After)
                        Debug.LogWarning("[NodeDoc] 阻止本事件：仅对「X 时」事件有效（「X 后」动作已执行完，无法阻止）");
                    else
                        cur_event.cancelled = true;
                    break;
                }
                case "SetEventValue":   //修改事件值：覆盖当前事件数值（如伤害量改 0=免伤）；仅 Before 事件生效
                {
                    if (cur_event == null)
                        Debug.LogWarning("[NodeDoc] 修改事件值：当前不在事件广播中，忽略");
                    else if (cur_event.phase == GraphEventPhase.After)
                        Debug.LogWarning("[NodeDoc] 修改事件值：仅对「X 时」事件有效（「X 后」数值已只读）");
                    else
                        cur_event.value = GetIntInput(logic, graph, act, "value", caster, target_card, target_player,
                            GraphRuntime.GetFieldInt(act, "value", 0));
                    break;
                }
                case "TriggerButtonEffect":   //触发按钮效果：模拟完整点击（先「时」后「后」），递归深度受限防自环
                {
                    string bid = GraphRuntime.GetFieldString(act, "button_id", "");
                    if (string.IsNullOrEmpty(bid))
                        break;
                    if (trigger_depth < 8)
                    {
                        trigger_depth++;
                        try
                        {
                            RunButtonClick(logic, graph, caster, bid);
                        }
                        finally
                        {
                            trigger_depth--;
                        }
                    }
                    else
                        Debug.LogWarning("[NodeDoc] 触发按钮效果 超过递归深度上限 8 层，已停止（检查按钮图是否自环）");
                    break;
                }
                default:
                    Debug.LogWarning("[NodeDoc] 未支持的 NodeDoc 动作，已跳过: " + act.action + " (" + act.title + ")");
                    break;
            }
        }

        /// <summary>Card 型输入口取值：有取值线时按来源解析——
        /// 入口事件节点的 目标卡牌1/目标卡牌 → 能力选中目标；卡牌 → 施法卡自身；
        /// 111012 筛选 → 求值筛选结果取第一张；102013/102015/102017 角色集合 → 取第一张；
        /// 其余来源暂不解析返回 null。无连线 → 能力选中目标（旧行为）。</summary>
        private static Card ResolveInputCard(GameLogic logic, GraphData graph, GraphNode act, string pin_name,
            Card caster, Card target_card, Player target_player)
        {
            if (graph != null && act != null)
            {
                GraphPin pin = graph.GetPinByName(act.id, pin_name);
                if (pin != null)
                {
                    GraphLink link = graph.GetIncomingLink(act.id, pin.id);
                    if (link != null)
                    {
                        GraphNode src = graph.GetNode(link.from_node);
                        if (src != null && src.type == GraphNodeType.Event)
                        {
                            //事件入口（触发器/增益/按钮/入口/事件分类）输出口：self/card→宿主(施法卡)、target→选中目标、
                            //subject/source→事件上下文主体/来源、giver→增益施加者。
                            //注意：事件节点也带 category，必须先于 ResolveValueCard 判定，否则事件输出口一律被当未知取值节点解析为空
                            GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                            string out_name = out_pin != null ? out_pin.name : link.from_pin;
                            if (out_name == "self")
                                return caster;
                            if (out_name == "card" || out_name == "subject")
                                return cur_event != null ? (cur_event.card ?? caster) : caster;
                            if (out_name == "source")
                                return cur_event != null ? cur_event.source_card : null;
                            if (out_name == "target" || out_name == "target_card")
                                return target_card;
                            if (out_name == "giver")
                                return ctx_giver;
                            return null;    //玩家口/未知口不是卡牌
                        }
                        if (src != null && !string.IsNullOrEmpty(src.category))
                            return ResolveValueCard(logic, graph, src, caster, target_card, target_player);
                        return null;        //其他来源暂不支持
                    }
                }
            }
            return target_card;             //无连线：沿用能力选中目标
        }

        /// <summary>Card 数组输入口取值（多目标）：来源可以是 111012 筛选（全部匹配）、
        /// 102013/102015/102017 角色集合（全部）、入口事件的目标/自身（单个）、
        /// 无连线 → 能力选中目标（单个，可能为空）。</summary>
        private static List<Card> ResolveInputCards(GameLogic logic, GraphData graph, GraphNode act, string pin_name,
            Card caster, Card target_card)
        {
            List<Card> result = new List<Card>();
            if (graph != null && act != null)
            {
                GraphPin pin = graph.GetPinByName(act.id, pin_name);
                if (pin != null)
                {
                    GraphLink link = graph.GetIncomingLink(act.id, pin.id);
                    if (link != null)
                    {
                        GraphNode src = graph.GetNode(link.from_node);
                        if (src != null && !string.IsNullOrEmpty(src.category))
                        {
                            List<Card> col = ResolveCollectionNode(logic, graph, src, caster, target_card, null);
                            if (col != null)
                                return col;
                            Card one = ResolveValueCard(logic, graph, src, caster, target_card, null);
                            if (one != null)
                                result.Add(one);    //单卡来源（这张卡牌/玩家英雄/随机元素…）
                            return result;
                        }
                        Card single = ResolveInputCard(logic, graph, act, pin_name, caster, target_card, null);
                        if (single != null)
                            result.Add(single);
                        return result;
                    }
                }
            }
            if (target_card != null)
                result.Add(target_card);
            return result;
        }

        /// <summary>求值 111012「筛选」：对 集合 输入口求值得到候选卡列表，再过 条件 输入口。
        /// v1 条件仅支持静态布尔（常量/值节点）；依赖「元素」输出口的逐元素条件暂不支持（视为全通过）。</summary>
        private static List<Card> EvaluateFilter(GameLogic logic, GraphData graph, GraphNode filter,
            Card caster, Card target_card)
        {
            List<Card> result = new List<Card>();

            //集合输入口
            GraphPin arr_pin = graph.GetPinByName(filter.id, "array");
            GraphLink arr_link = arr_pin != null ? graph.GetIncomingLink(filter.id, arr_pin.id) : null;
            if (arr_link != null)
            {
                GraphNode src = graph.GetNode(arr_link.from_node);
                if (src != null && !string.IsNullOrEmpty(src.category))
                {
                    List<Card> sub = ResolveCollectionNode(logic, graph, src, caster, target_card, null);
                    if (sub != null)
                        result.AddRange(sub);       //筛选可级联/集合来源统一分发
                }
                else if (src != null && src.type == GraphNodeType.Event)
                {
                    Card c = ResolveInputCard(logic, graph, filter, "array", caster, target_card, null);
                    if (c != null)
                        result.Add(c);
                }
            }
            else
            {
                Card c = target_card;       //无集合连线：退化用能力选中目标
                if (c != null)
                    result.Add(c);
            }

            //条件输入口：NodeDoc 条件节点（102032/112005…）逐元素求值，
            //条件节点卡牌口连本节点「元素」输出口时绑定当前遍历卡牌（与定义版同语义）；
            //旧内置 Value 节点按静态布尔（为假 → 空集合）
            GraphPin cond_pin = graph.GetPinByName(filter.id, "condition");
            GraphLink cond_link = cond_pin != null ? graph.GetIncomingLink(filter.id, cond_pin.id) : null;
            if (cond_link != null)
            {
                GraphNode cond_src = graph.GetNode(cond_link.from_node);
                if (cond_src != null)
                {
                    if (string.IsNullOrEmpty(cond_src.category))
                    {
                        if (cond_src.type == GraphNodeType.Value && GraphRuntime.EvaluateValue(graph, cond_src) == 0)
                            result.Clear();         //静态条件为假 → 空集合
                    }
                    else
                    {
                        List<Card> candidates = new List<Card>(result);
                        result = new List<Card>();
                        evaluating_filters.Add(filter.id);   //防条件链回环引用自身
                        try
                        {
                            foreach (Card c in candidates)
                                if (EvaluateConditionNode(logic, graph, cond_src, caster, target_card, filter, null, c))
                                    result.Add(c);
                        }
                        finally { evaluating_filters.Remove(filter.id); }
                    }
                }
            }
            return result;
        }

        /// <summary>求值集合"截断"节点：111029 取开头连续满足条件的元素 / 111030 跳过开头连续满足条件的取剩余。
        /// 与 111012 同语义：condition 输入口连条件节点，逐元素以本节点「元素」输出口绑定当前元素。</summary>
        private static List<Card> EvaluateCollectionUntil(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player, bool take_head)
        {
            List<Card> result = new List<Card>();
            if (logic == null || graph == null || node == null)
                return result;
            if (evaluating_filters.Contains(node.id))     //条件链回环引用自身 → 断开
                return result;
            List<Card> src = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "array");
            GraphPin cond_pin = graph.GetPinByName(node.id, "condition");
            GraphLink cond_link = cond_pin != null ? graph.GetIncomingLink(node.id, cond_pin.id) : null;
            if (cond_link == null)
                return take_head ? src : result;         //无条件 → 111029 恒取全部 / 111030 恒空
            GraphNode cond_src = graph.GetNode(cond_link.from_node);
            if (cond_src == null)
                return take_head ? src : result;
            evaluating_filters.Add(node.id);
            try
            {
                if (take_head)
                {
                    foreach (Card c in src)
                    {
                        if (c == null)
                            continue;
                        bool ok = EvaluateConditionNode(logic, graph, cond_src, caster, target_card, node, null, c);
                        if (!ok)
                            break;                       //第一个不满足 → 截断
                        result.Add(c);
                    }
                    return result;
                }
                List<Card> rest = new List<Card>();
                bool passed = false;
                foreach (Card c in src)
                {
                    if (c == null)
                        continue;
                    bool ok = EvaluateConditionNode(logic, graph, cond_src, caster, target_card, node, null, c);
                    if (!ok)
                        passed = true;
                    if (passed)
                        rest.Add(c);
                }
                return rest;
            }
            finally { evaluating_filters.Remove(node.id); }
        }
        private static List<Card> EvaluateCardCollection(GameLogic logic, GraphNode node, Card caster, Card target_card)
        {
            List<Card> result = new List<Card>();
            if (logic == null || caster == null)
                return result;
            int self = caster.player_id;
            bool enemy = node.action == "102016" || node.action == "102017";
            bool all = node.action == "102013";
            bool minions_only = node.action == "102014" || node.action == "102016";   //仆从版集合不含英雄
            for (int p = 0; p < logic.GameData.players.Length; p++)
            {
                Player player = logic.GameData.players[p];
                if (player == null)
                    continue;
                bool is_enemy = player.player_id != self;
                if (!all && is_enemy != enemy)
                    continue;
                foreach (Card c in player.cards_board)
                {
                    if (c != null && logic.GameData.IsOnBoard(c))
                        result.Add(c);
                }
                if (!minions_only && player.hero != null)
                    result.Add(player.hero);
            }
            return result;
        }

        /// <summary>求值任意 NodeDoc 集合来源节点为卡牌列表（集中分发）：
        /// 111012 筛选 / 102013 全体角色 / 102015 友方角色 / 102017 敌方角色 / 102014 友方随从 / 102016 敌方随从 /
        /// 101008 玩家牌库 / 101006 玩家手牌 / 101017 获取牌堆(牌库/手牌/墓地)；未知来源返回 null。</summary>
        private static List<Card> ResolveCollectionNode(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            if (node == null || logic == null)
                return null;
            switch (node.action)
            {
                case "111012":
                    return EvaluateFilter(logic, graph, node, caster, target_card);
                case "111029":
                    return EvaluateCollectionUntil(logic, graph, node, caster, target_card, target_player, true);
                case "111030":
                    return EvaluateCollectionUntil(logic, graph, node, caster, target_card, target_player, false);
                case "102013":
                case "102014":
                case "102015":
                case "102016":
                case "102017":
                    return EvaluateCardCollection(logic, node, caster, target_card);
                case "101008":
                case "101006":
                {
                    Player p = ResolveValuePlayer(logic, graph, node, caster, target_player);
                    if (p == null)
                        return new List<Card>();
                    return new List<Card>(node.action == "101008" ? p.cards_deck : p.cards_hand);
                }
                case "101017":
                {
                    Player p = ResolveValuePlayer(logic, graph, node, caster, target_player);
                    if (p == null)
                        return new List<Card>();
                    string pile = GraphRuntime.GetFieldString(node, "pileName", "牌库");
                    if (pile == "手牌")
                        return new List<Card>(p.cards_hand);
                    if (pile == "墓地" || pile == "弃牌")
                        return new List<Card>(p.cards_discard);
                    return new List<Card>(p.cards_deck);
                }
                case "111001":   //创建集合：elements 口的单张卡（v1 仅支持卡牌元素）
                {
                    Card el = ResolveInputCard(logic, graph, node, "elements", caster, target_card, target_player);
                    return el != null ? new List<Card> { el } : new List<Card>();
                }
                case "111002":   //向集合添加元素：array 集合 + elements 单卡
                {
                    List<Card> list = ResolveArrayCards(logic, graph, node, caster, target_card, target_player);
                    Card el = ResolveInputCard(logic, graph, node, "elements", caster, target_card, target_player);
                    if (el != null)
                        list.Add(el);
                    return list;
                }
                case "111009":   //反转集合
                {
                    List<Card> list = ResolveArrayCards(logic, graph, node, caster, target_card, target_player);
                    list.Reverse();
                    return list;
                }
                case "111013":   //排序：v1 按属性下拉（攻击/生命/法力费用）+ 升降序字段（zmcs 原为条件表达式排序键）
                {
                    List<Card> list = ResolveArrayCards(logic, graph, node, caster, target_card, target_player);
                    bool desc = GetBoolInput(logic, graph, node, "descending", caster, target_card, target_player,
                        GraphRuntime.GetFieldString(node, "descending", "false") == "true");
                    string prop = GraphRuntime.GetFieldString(node, "prop", "攻击");
                    list.Sort((a, b) => desc
                        ? GetCardProp(b, prop).CompareTo(GetCardProp(a, prop))
                        : GetCardProp(a, prop).CompareTo(GetCardProp(b, prop)));
                    return list;
                }
                case "111026":   //获取集合内前X个元素
                {
                    List<Card> list = ResolveArrayCards(logic, graph, node, caster, target_card, target_player);
                    int count = GetIntInput(logic, graph, node, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(node, "count", 0));
                    return list.GetRange(0, Mathf.Clamp(count, 0, list.Count));
                }
                case "111028":   //获取集合内的随机X个元素
                {
                    List<Card> list = ResolveArrayCards(logic, graph, node, caster, target_card, target_player);
                    int count = GetIntInput(logic, graph, node, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(node, "count", 0));
                    List<Card> pool = new List<Card>(list);
                    List<Card> picked = new List<Card>();
                    while (picked.Count < count && pool.Count > 0)
                    {
                        int idx = Random.Range(0, pool.Count);
                        picked.Add(pool[idx]);
                        pool.RemoveAt(idx);
                    }
                    return picked;
                }
                case "111032":   //打乱集合内元素顺序
                {
                    List<Card> list = ResolveArrayCards(logic, graph, node, caster, target_card, target_player);
                    for (int i = list.Count - 1; i > 0; i--)
                    {
                        int j = Random.Range(0, i + 1);
                        Card tmp = list[i];
                        list[i] = list[j];
                        list[j] = tmp;
                    }
                    return list;
                }
                case "112007":   //获取临时变量：值为卡牌集合时按集合使用
                {
                    return GetTempVar(node) as List<Card> ?? new List<Card>();
                }
                case "112010":   //根据条件选择值：分支结果为集合/单卡时按集合使用
                {
                    object v = SelectConditionalValue(logic, graph, node, caster, target_card, target_player);
                    if (v is List<Card> lc)
                        return lc;
                    if (v is Card cc)
                        return new List<Card> { cc };
                    return new List<Card>();
                }
                case "101007":   //获取玩家的墓地卡牌
                {
                    Player p = ResolveValuePlayer(logic, graph, node, caster, target_player);
                    return p != null ? new List<Card>(p.cards_discard) : new List<Card>();
                }
                case "102012":   //获取所有仆从（全场战场随从，不含英雄）
                {
                    List<Card> r = new List<Card>();
                    if (logic == null)
                        return r;
                    foreach (Player pl in logic.GameData.players)
                    {
                        if (pl == null)
                            continue;
                        foreach (Card c in pl.cards_board)
                        {
                            if (c != null && logic.GameData.IsOnBoard(c))
                                r.Add(c);
                        }
                    }
                    return r;
                }
                case "111010":   //集合去重（保序）
                {
                    return DistinctCards(ResolveValueCards(logic, graph, node, caster, target_card, target_player, "array"));
                }
                case "111011":   //集合相减：array 中不在 excepted 里的元素（保留 array 顺序与去重语义同 zmcs）
                {
                    List<Card> a = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "array");
                    List<Card> b = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "excepted");
                    List<Card> r = new List<Card>();
                    foreach (Card c in a)
                    {
                        if (c != null && !b.Contains(c))
                            r.Add(c);
                    }
                    return r;
                }
                case "111018":   //集合相加（直接拼接，不去重）
                {
                    List<Card> first = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "first");
                    List<Card> second = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "second");
                    List<Card> r = new List<Card>(first);
                    foreach (Card c in second)
                    {
                        if (c != null)
                            r.Add(c);
                    }
                    return r;
                }
                case "111019":   //获取并集（两集合并集，去重）
                {
                    List<Card> first = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "first");
                    List<Card> second = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "second");
                    List<Card> r = DistinctCards(first);
                    foreach (Card c in second)
                    {
                        if (c != null && !r.Contains(c))
                            r.Add(c);
                    }
                    return r;
                }
                case "111020":   //获取交集（两集合交集，去重）
                {
                    List<Card> first = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "first");
                    List<Card> second = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "second");
                    List<Card> r = new List<Card>();
                    foreach (Card c in first)
                    {
                        if (c != null && second.Contains(c) && !r.Contains(c))
                            r.Add(c);
                    }
                    return r;
                }
                case "111027":   //获取集合内前X个元素之外的元素（去掉前 count 个）
                {
                    List<Card> list = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "array");
                    int count = GetIntInput(logic, graph, node, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(node, "count", 0));
                    if (count <= 0)
                        return list;
                    if (count >= list.Count)
                        return new List<Card>();
                    return list.GetRange(count, list.Count - count);
                }
                default:
                    return null;    //未知集合来源
            }
        }

        /// <summary>卡牌列表去重（按引用、保序）</summary>
        private static List<Card> DistinctCards(List<Card> list)
        {
            List<Card> r = new List<Card>();
            if (list == null)
                return r;
            foreach (Card c in list)
            {
                if (c != null && !r.Contains(c))
                    r.Add(c);
            }
            return r;
        }

        /// <summary>把中文/英文牌堆名归一为内部名（手牌/牌库/墓地/战场/装备/奥秘；未知返回原样）
        /// TCG2 无 延迟区/暂存区/技能区/道具栏，输入这些名字恒不在任何牌堆（判假），不强行映射到现有区域。</summary>
        private static string PileNormalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "战场";
            switch (s)
            {
                case "牌库":
                case "卡组":
                case "deck":
                    return "牌库";
                case "手牌":
                case "hand":
                    return "手牌";
                case "墓地":
                case "弃牌":
                case "坟墓":
                case "discard":
                    return "墓地";
                case "战场":
                case "场上":
                case "出战区":
                case "board":
                    return "战场";
                case "装备":
                case "装备区":
                case "equip":
                    return "装备";
                case "奥秘":
                case "secret":
                    return "奥秘";
                default:
                    return s;
            }
        }

        /// <summary>查询某卡当前所在牌堆的内部名（遍历双方各区域；不在任何区域返回空串）</summary>
        private static string GetCardPileName(GameLogic logic, Card c)
        {
            if (logic == null || c == null)
                return "";
            foreach (Player pl in logic.GameData.players)
            {
                if (pl == null)
                    continue;
                if (pl.cards_hand != null && pl.cards_hand.Contains(c)) return "手牌";
                if (pl.cards_deck != null && pl.cards_deck.Contains(c)) return "牌库";
                if (pl.cards_board != null && pl.cards_board.Contains(c)) return "战场";
                if (pl.cards_discard != null && pl.cards_discard.Contains(c)) return "墓地";
                if (pl.cards_equip != null && pl.cards_equip.Contains(c)) return "装备";
                if (pl.cards_secret != null && pl.cards_secret.Contains(c)) return "奥秘";
            }
            return "";
        }

        /// <summary>取列表第一张</summary>
        private static Card FirstCard(List<Card> list)
        {
            return (list != null && list.Count > 0) ? list[0] : null;
        }

        /// <summary>把 Card 型输入口当玩家目标解析（入口 玩家 口 → 能力选中玩家 / 施法卡所属玩家）</summary>
        private static Player ResolveInputPlayer(GameLogic logic, GraphData graph, GraphNode act, string pin_name,
            Card caster, Player target_player)
        {
            if (graph != null && act != null)
            {
                GraphPin pin = graph.GetPinByName(act.id, pin_name);
                if (pin != null)
                {
                    GraphLink link = graph.GetIncomingLink(act.id, pin.id);
                    if (link != null)
                    {
                        GraphNode src = graph.GetNode(link.from_node);
                        if (src != null && !string.IsNullOrEmpty(src.category))
                            return ResolvePlayerOutput(logic, graph, src, caster, target_player);
                        if (src != null && src.type == GraphNodeType.Event)
                        {
                            GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                            string out_name = out_pin != null ? out_pin.name : link.from_pin;
                            if (out_name == "player")
                                return target_player ?? PlayerOf(logic, caster);
                            return null;
                        }
                    }
                }
            }
            return target_player;
        }

        /// <summary>施法卡的所属玩家</summary>
        private static Player PlayerOf(GameLogic logic, Card caster)
        {
            return (logic != null && caster != null) ? logic.GameData.GetPlayer(caster.player_id) : null;
        }

        /// <summary>施法卡所属玩家的对手（双人局：另一方）</summary>
        private static Player OpponentOf(GameLogic logic, Card caster)
        {
            Player p = PlayerOf(logic, caster);
            if (p == null)
                return null;
            int other = p.player_id == 0 ? 1 : 0;
            return logic.GameData.GetPlayer(other);
        }

        /// <summary>106004 属性名 → 卡牌身上的加成状态值（无该状态返回 0）</summary>
        private static int GetBuffProp(Card card, string prop)
        {
            CardStatus atk = card.GetStatus(StatusType.AddAttack);
            CardStatus hp = card.GetStatus(StatusType.AddHP);
            switch (prop)
            {
                case "生命加成":
                    return hp != null ? hp.value : 0;
                case "攻击加成持续":
                    return atk != null ? atk.duration : 0;
                case "生命加成持续":
                    return hp != null ? hp.duration : 0;
                default:
                    return atk != null ? atk.value : 0;
            }
        }

        /// <summary>找玩家一侧第一个空位（无空位返回 default(Slot)）</summary>
        private static Slot FindEmptySlot(GameLogic logic, Player player)
        {
            foreach (Slot s in Slot.GetAll(player.player_id))
            {
                if (logic != null && player != null && logic.GameData.GetSlotCard(s) == null)
                    return s;
            }
            return default;
        }

        /// <summary>102027 属性名 → 运行时卡实际值（含常驻修正；未知属性名按攻击处理）</summary>
        private static int GetCardProp(Card card, string prop)
        {
            switch (prop)
            {
                case "生命":
                    return card.GetHP();
                case "法力":
                case "法力费用":
                    return card.GetMana();
                default:
                    return card.GetAttack();
            }
        }

        /// <summary>103020 属性名 → 卡牌定义（CardData）上的基础数值（攻击/生命/法力费用；未知按攻击）</summary>
        private static int DefineProp(CardData d, string prop)
        {
            if (d == null)
                return 0;
            switch (prop)
            {
                case "生命":
                    return d.hp;
                case "法力":
                case "法力费用":
                    return d.mana;
                default:
                    return d.attack;
            }
        }

        /// <summary>103006 卡牌定义是否带指定关键词（id 或标题）</summary>
        private static bool DefineHasKeyword(CardData d, string want)
        {
            if (d == null || d.keywords == null || string.IsNullOrEmpty(want))
                return false;
            foreach (KeywordData k in d.keywords)
            {
                if (KeywordMatches(k, want))
                    return true;
            }
            return false;
        }

        /// <summary>102003 运行时卡是否带指定关键词（含运行时附加的关键词；id 或标题均可）</summary>
        private static bool KeywordHas(Card card, string want)
        {
            if (card == null || string.IsNullOrEmpty(want))
                return false;
            if (card.HasKeyword(want))
                return true;
            if (card.CardData != null && card.CardData.keywords != null)
            {
                foreach (KeywordData k in card.CardData.keywords)
                {
                    if (KeywordMatches(k, want))
                        return true;
                }
            }
            return false;
        }

        /// <summary>关键词匹配：编辑器存 KeywordData.id（也兼容手填标题）</summary>
        private static bool KeywordMatches(KeywordData k, string want)
        {
            if (k == null || string.IsNullOrEmpty(want))
                return false;
            if (k.id == want)
                return true;
            return !string.IsNullOrEmpty(k.title) && k.title == want;
        }

        /// <summary>103023 CardType → 中文类型名（与 102032 的卡牌类型下拉同一套口径）</summary>
        private static string CardTypeName(CardType t)
        {
            switch (t)
            {
                case CardType.Character: return "随从";
                case CardType.Spell: return "法术";
                case CardType.Hero: return "英雄";
                case CardType.Artifact: return "神器";
                case CardType.Equipment: return "装备";
                case CardType.Secret: return "奥秘";
                default: return t.ToString();
            }
        }

        // ---------------- NodeDoc 取值节点求值（取值线来源） ----------------

        /// <summary>求值 Card 输出的 NodeDoc 取值节点：102001 这张卡牌（施法卡自身）/ 101004 玩家英雄 /
        /// 111008 随机元素 / 111012 筛选 / 102013/15/17 角色集合；未知节点返回 null。</summary>
        private static Card ResolveValueCard(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            if (node == null)
                return null;
            switch (node.action)
            {
                case "102001":
                    return caster;
                case "101004":
                {
                    Player p = ResolveValuePlayer(logic, graph, node, caster, target_player);
                    return p != null ? p.hero : null;
                }
                case "111008":
                {
                    List<Card> col = ResolveValueCards(logic, graph, node, caster, target_card, target_player);
                    return col.Count > 0 ? col[Random.Range(0, col.Count)] : null;
                }
                case "111007":   //获取第X个元素：集合口 + 元素位置(Int32，可取值线/循环节点)
                {
                    List<Card> col = ResolveValueCards(logic, graph, node, caster, target_card, target_player);
                    int idx = GetIntInput(logic, graph, node, "index", caster, target_card, target_player, 0);
                    return (idx >= 0 && idx < col.Count) ? col[idx] : null;
                }
                case "111012":  //筛选：条件链回环引用自身时断开（返回无卡），避免无限递归
                    if (evaluating_filters.Contains(node.id))
                        return null;
                    return FirstCard(ResolveCollectionNode(logic, graph, node, caster, target_card, target_player));
                case "102013":
                case "102014":
                case "102015":
                case "102016":
                case "102017":
                case "101008":
                case "101006":
                case "101017":
                case "111001":
                case "111002":
                case "111009":
                case "111013":
                case "111026":
                case "111028":
                case "111032":
                case "112010":
                    return FirstCard(ResolveCollectionNode(logic, graph, node, caster, target_card, target_player));
                case "111024":   //获取第一个元素
                    return FirstCard(ResolveArrayCards(logic, graph, node, caster, target_card, target_player));
                case "111025":   //获取最后一个元素
                {
                    List<Card> list = ResolveArrayCards(logic, graph, node, caster, target_card, target_player);
                    return list.Count > 0 ? list[list.Count - 1] : null;
                }
                case "112007":   //获取临时变量：值为卡牌时按卡牌使用
                    return GetTempVar(node) as Card;
                case "108001":   //当前事件（宽松：取其事件主体卡）
                    return cur_event != null ? (cur_event.card ?? caster) : caster;
                case "108002":   //获取变量（卡牌/来源/目标）
                    return EventVarCard(node, caster, target_card);
                default:
                    return null;    //未知取值节点暂不支持
            }
        }

        /// <summary>事件主体卡在当前所在牌堆中的位置（slot/第几张）；无事件或找不到返回 -1</summary>
        private static int EventCardPos(GameLogic logic)
        {
            Card c = cur_event != null ? cur_event.card : null;
            if (logic == null || c == null)
                return -1;
            Player p = logic.GameData.GetPlayer(c.player_id);
            if (p == null)
                return -1;
            if (p.cards_board.Contains(c)) return p.cards_board.IndexOf(c);
            if (p.cards_hand.Contains(c)) return p.cards_hand.IndexOf(c);
            if (p.cards_deck.Contains(c)) return p.cards_deck.IndexOf(c);
            if (p.cards_discard.Contains(c)) return p.cards_discard.IndexOf(c);
            if (p.cards_equip.Contains(c)) return p.cards_equip.IndexOf(c);
            if (p.cards_secret.Contains(c)) return p.cards_secret.IndexOf(c);
            return -1;
        }

        /// <summary>事件家族：按「变量名」从当前事件上下文取 Object（标签/数值/卡牌/玩家/来源）</summary>
        private static object EventVarObject(GraphNode node, Card caster, Card target_card)
        {
            string name = GraphRuntime.GetFieldString(node, "varName", "卡牌");
            if (name == "标签")
                return cur_event != null ? cur_event.tags : null;
            if (name == "数值")
                return cur_event != null ? (object)cur_event.value : null;
            return EventVarCard(node, caster, target_card);
        }

        /// <summary>事件家族：按「变量名」从当前事件上下文取卡牌（卡牌/来源/目标）</summary>
        private static Card EventVarCard(GraphNode node, Card caster, Card target_card)
        {
            string name = GraphRuntime.GetFieldString(node, "varName", "卡牌");
            if (name == "来源")
                return cur_event != null ? cur_event.source_card : null;
            if (name == "目标")
                return target_card ?? (cur_event != null ? cur_event.card : null);
            return cur_event != null ? (cur_event.card ?? caster) : caster;
        }

        /// <summary>事件家族：按「变量名」从当前事件上下文取玩家</summary>
        private static Player EventVarPlayer(GameLogic logic, GraphNode node, Card caster, Player target_player)
        {
            string name = GraphRuntime.GetFieldString(node, "varName", "玩家");
            if (name == "玩家")
                return (cur_event != null && cur_event.player != null) ? cur_event.player : (target_player ?? PlayerOf(logic, caster));
            //其余变量名（卡牌/来源/数值）落到卡牌拥有者
            Card c = EventVarCard(node, caster, null);
            Player owner = c != null ? PlayerOf(logic, c) : null;
            return owner ?? PlayerOf(logic, caster);
        }

        /// <summary>事件家族：按「变量名」从当前事件上下文取整数（数值）</summary>
        private static int? EventVarInt(GraphNode node)
        {
            string name = GraphRuntime.GetFieldString(node, "varName", "数值");
            if (name != "数值")
                return null;
            return cur_event != null ? cur_event.value : 0;
        }

        /// <summary>求值 CardDefine 输出的 NodeDoc 取值节点（zmcs 卡牌定义 ≈ TCG2 CardData）：
        /// 103002 获取卡牌定义（cardRef 字段填卡牌 id）/ 103008 获取单张卡牌的定义；
        /// 未知节点返回 null。</summary>
        private static CardData ResolveValueDefine(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            if (node == null)
                return null;
            switch (node.action)
            {
                case "103002":
                {
                    string id = GraphRuntime.GetFieldString(node, "cardRef", "");
                    return !string.IsNullOrEmpty(id) ? CardData.Get(id) : null;
                }
                case "103008":
                {
                    Card c = ResolveInputCard(logic, graph, node, "card", caster, target_card, target_player);
                    return c != null ? c.CardData : null;
                }
                case "112007":
                    return GetTempVar(node) as CardData;
                case "112010":
                    return SelectConditionalValue(logic, graph, node, caster, target_card, target_player) as CardData;
                case "111008":   //获取集合中的随机元素：上游为定义集合时返回随机定义
                {
                    List<CardData> defs = ResolveValueDefines(logic, graph, node, "collection", caster, target_card, target_player);
                    return defs.Count > 0 ? defs[Random.Range(0, defs.Count)] : null;
                }
                case "111024":   //获取第一个元素：上游为定义集合时返回首个定义
                {
                    List<CardData> defs = ResolveValueDefines(logic, graph, node, "collection", caster, target_card, target_player);
                    return defs.Count > 0 ? defs[0] : null;
                }
                case "111025":   //获取最后一个元素：上游为定义集合时返回末个定义
                {
                    List<CardData> defs = ResolveValueDefines(logic, graph, node, "collection", caster, target_card, target_player);
                    return defs.Count > 0 ? defs[defs.Count - 1] : null;
                }
                default:
                    return null;    //未知取值节点暂不支持
            }
        }

        /// <summary>CardDefine 型输入口取值（动作节点的定义口）：上游 NodeDoc 定义节点 → ResolveValueDefine；
        /// 无连线返回 null（调用方回退到字段常量）。</summary>
        private static CardData ResolveInputDefine(GameLogic logic, GraphData graph, GraphNode act, string pin_name,
            Card caster, Card target_card, Player target_player)
        {
            if (graph != null && act != null)
            {
                GraphPin pin = graph.GetPinByName(act.id, pin_name);
                if (pin != null)
                {
                    GraphLink link = graph.GetIncomingLink(act.id, pin.id);
                    if (link != null)
                    {
                        GraphNode src = graph.GetNode(link.from_node);
                        if (src != null && !string.IsNullOrEmpty(src.category))
                            return ResolveValueDefine(logic, graph, src, caster, target_card, target_player);
                    }
                }
            }
            return null;
        }

        /// <summary>条件节点定义口取值（定义筛选上下文版）：定义口的线若来自筛选节点(elem_filter)的
        /// 「元素」输出口 → 返回当前遍历元素 cur_define；否则按普通定义口解析。</summary>
        private static CardData ResolveInputDefineFiltered(GameLogic logic, GraphData graph, GraphNode node, string pin_name,
            GraphNode elem_filter, CardData cur_define, Card caster, Card target_card, Player target_player)
        {
            if (graph != null && node != null && elem_filter != null)
            {
                GraphPin pin = graph.GetPinByName(node.id, pin_name);
                GraphLink link = pin != null ? graph.GetIncomingLink(node.id, pin.id) : null;
                if (link != null && link.from_node == elem_filter.id)
                {
                    GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                    string out_name = out_pin != null ? out_pin.name : link.from_pin;
                    if (out_name == "element")
                        return cur_define;
                }
            }
            return ResolveInputDefine(logic, graph, node, pin_name, caster, target_card, target_player);
        }

        /// <summary>条件节点卡牌口取值（卡牌筛选上下文版）：卡牌口的线若来自筛选节点(elem_filter)的
        /// 「元素」输出口 → 返回当前遍历卡牌 cur_elem；否则按普通卡牌口解析。</summary>
        private static Card ResolveInputCardFiltered(GameLogic logic, GraphData graph, GraphNode node, string pin_name,
            GraphNode elem_filter, Card cur_elem, Card caster, Card target_card, Player target_player)
        {
            if (graph != null && node != null && elem_filter != null)
            {
                GraphPin pin = graph.GetPinByName(node.id, pin_name);
                GraphLink link = pin != null ? graph.GetIncomingLink(node.id, pin.id) : null;
                if (link != null && link.from_node == elem_filter.id)
                {
                    GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                    string out_name = out_pin != null ? out_pin.name : link.from_pin;
                    if (out_name == "element")
                        return cur_elem;
                }
            }
            return ResolveInputCard(logic, graph, node, pin_name, caster, target_card, target_player);
        }

        /// <summary>求值 111012「筛选」（定义集合版）：array 口候选定义逐个过 condition 口条件子图，
        /// 条件节点定义口连本节点「元素」输出口时绑定当前遍历定义（zmcs 原语义）。</summary>
        private static List<CardData> EvaluateDefineFilter(GameLogic logic, GraphData graph, GraphNode filter,
            Card caster, Card target_card, Player target_player)
        {
            List<CardData> result = new List<CardData>();

            List<CardData> candidates = new List<CardData>();
            GraphPin arr_pin = graph.GetPinByName(filter.id, "array");
            GraphLink arr_link = arr_pin != null ? graph.GetIncomingLink(filter.id, arr_pin.id) : null;
            if (arr_link != null)
            {
                GraphNode src = graph.GetNode(arr_link.from_node);
                if (src != null && !string.IsNullOrEmpty(src.category))
                    candidates = EvaluateDefineArray(logic, graph, src, caster, target_card, target_player);
            }

            GraphPin cond_pin = graph.GetPinByName(filter.id, "condition");
            GraphLink cond_link = cond_pin != null ? graph.GetIncomingLink(filter.id, cond_pin.id) : null;
            GraphNode cond_src = cond_link != null ? graph.GetNode(cond_link.from_node) : null;
            foreach (CardData def in candidates)
            {
                if (def == null)
                    continue;
                if (cond_src == null || EvaluateConditionNode(logic, graph, cond_src, caster, target_card, filter, def))
                    result.Add(def);
            }
            return result;
        }

        /// <summary>求值任意 NodeDoc 节点为定义集合（第五批定义通道，与卡牌集合通道并行）：
        /// 103001 全量定义 / 111012 定义筛选 / 111002 添加 / 111009 反转 / 111026 前X / 111028 随机X / 111032 打乱；
        /// 其余节点尝试按单定义解析（103002/103008/111008/111024/111025/112007…）→ 0~1 元素列表。</summary>
        private static List<CardData> EvaluateDefineArray(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            List<CardData> result = new List<CardData>();
            if (node == null)
                return result;
            switch (node.action)
            {
                case "103001":
                    result.AddRange(CardData.card_list);
                    return result;
                case "111012":
                    return EvaluateDefineFilter(logic, graph, node, caster, target_card, target_player);
                case "111002":   //向集合添加元素：array 集合 + elements 单定义
                {
                    result = DefineArrayInput(logic, graph, node, caster, target_card, target_player);
                    CardData el = ResolveInputDefine(logic, graph, node, "elements", caster, target_card, target_player);
                    if (el != null)
                        result.Add(el);
                    return result;
                }
                case "111009":   //反转集合
                {
                    result = DefineArrayInput(logic, graph, node, caster, target_card, target_player);
                    result.Reverse();
                    return result;
                }
                case "111026":   //获取集合内前X个元素
                {
                    result = DefineArrayInput(logic, graph, node, caster, target_card, target_player);
                    int count = GetIntInput(logic, graph, node, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(node, "count", 0));
                    return result.GetRange(0, Mathf.Clamp(count, 0, result.Count));
                }
                case "111028":   //获取集合内的随机X个元素
                {
                    result = DefineArrayInput(logic, graph, node, caster, target_card, target_player);
                    int count = GetIntInput(logic, graph, node, "count", caster, target_card, target_player,
                        GraphRuntime.GetFieldInt(node, "count", 0));
                    List<CardData> pool = new List<CardData>(result);
                    List<CardData> picked = new List<CardData>();
                    while (picked.Count < count && pool.Count > 0)
                    {
                        int idx = Random.Range(0, pool.Count);
                        picked.Add(pool[idx]);
                        pool.RemoveAt(idx);
                    }
                    return picked;
                }
                case "111032":   //打乱集合内元素顺序
                {
                    result = DefineArrayInput(logic, graph, node, caster, target_card, target_player);
                    for (int i = result.Count - 1; i > 0; i--)
                    {
                        int j = Random.Range(0, i + 1);
                        CardData tmp = result[i];
                        result[i] = result[j];
                        result[j] = tmp;
                    }
                    return result;
                }
                default:
                {
                    CardData one = ResolveValueDefine(logic, graph, node, caster, target_card, target_player);
                    if (one != null)
                        result.Add(one);
                    return result;
                }
            }
        }

        /// <summary>求值定义集合运算节点的 array 输入口（定义版 ResolveArrayCards）</summary>
        private static List<CardData> DefineArrayInput(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            List<CardData> result = new List<CardData>();
            GraphPin pin = graph.GetPinByName(node.id, "array");
            GraphLink link = pin != null ? graph.GetIncomingLink(node.id, pin.id) : null;
            if (link == null)
                return result;
            GraphNode src = graph.GetNode(link.from_node);
            if (src != null && !string.IsNullOrEmpty(src.category))
                result = EvaluateDefineArray(logic, graph, src, caster, target_card, target_player);
            return result;
        }

        /// <summary>求值定义为集合的取值线（定义集合通道）：上游走 EvaluateDefineArray 集中分发——
        /// 103001 全量定义 / 111012 定义筛选 / 111026/111028/111032 等集合运算 / 单定义节点（103002/103008…）；
        /// 上游不是定义来源返回空列表。
        /// 与卡牌集合通道并行：调用方先试本方法，空了再回退卡牌集合。</summary>
        private static List<CardData> ResolveValueDefines(GameLogic logic, GraphData graph, GraphNode node,
            string pin_name, Card caster, Card target_card, Player target_player)
        {
            List<CardData> result = new List<CardData>();
            if (graph == null || node == null)
                return result;
            GraphPin pin = graph.GetPinByName(node.id, pin_name);
            GraphLink link = pin != null ? graph.GetIncomingLink(node.id, pin.id) : null;
            if (link == null)
                return result;
            GraphNode src = graph.GetNode(link.from_node);
            if (src == null || string.IsNullOrEmpty(src.category))
                return result;
            return EvaluateDefineArray(logic, graph, src, caster, target_card, target_player);
        }

        /// <summary>求值集合型输入口为卡牌列表：来源走 ResolveCollectionNode 集中分发；无连线/未知来源返回空列表。
        /// pin_name 默认 collection，集合运算家族（111013/111026 等）的输入口名为 array。</summary>
        private static List<Card> ResolveValueCards(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player, string pin_name = "collection")
        {
            List<Card> result = new List<Card>();
            if (graph == null || node == null)
                return result;
            GraphPin pin = graph.GetPinByName(node.id, pin_name);
            GraphLink link = pin != null ? graph.GetIncomingLink(node.id, pin.id) : null;
            if (link == null)
                return result;
            GraphNode src = graph.GetNode(link.from_node);
            if (src == null || string.IsNullOrEmpty(src.category))
                return result;
            List<Card> sub = ResolveCollectionNode(logic, graph, src, caster, target_card, target_player);
            if (sub != null)
                result.AddRange(sub);
            else
            {
                Card one = ResolveValueCard(logic, graph, src, caster, target_card, target_player);
                if (one != null)
                    result.Add(one);
            }
            return result;
        }

        /// <summary>求值集合运算节点的 array 输入口为卡牌列表：上游 NodeDoc 集合/单卡节点，
        /// 内置/入口来源退化用能力选中目标。</summary>
        private static List<Card> ResolveArrayCards(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            List<Card> result = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "array");
            if (result.Count == 0 && target_card != null && graph.GetPinByName(node.id, "array") != null
                && graph.GetIncomingLink(node.id, graph.GetPinByName(node.id, "array").id) == null)
                result.Add(target_card);    //array 无连线：退化用选中目标
            return result;
        }

        /// <summary>求值整数集合：111031 属性映射（卡牌集合×属性下拉→整数列表）；
        /// 111014~111017 的上游为 111031 或卡牌集合（用本节点 prop 字段映射）。</summary>
        private static List<int> ResolveIntCollection(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Card target_card, Player target_player)
        {
            List<int> result = new List<int>();
            if (node == null || graph == null)
                return result;
            if (node.action == "111031")
            {
                List<Card> cards = ResolveValueCards(logic, graph, node, caster, target_card, target_player, "array");
                string prop = GraphRuntime.GetFieldString(node, "prop", "攻击");
                foreach (Card c in cards)
                {
                    if (c != null)
                        result.Add(GetCardProp(c, prop));
                }
                return result;
            }
            //111014~111017：上游整数集合（111031）或卡牌集合×本节点 prop
            GraphPin pin = graph.GetPinByName(node.id, "array");
            GraphLink link = pin != null ? graph.GetIncomingLink(node.id, pin.id) : null;
            if (link == null)
                return result;
            GraphNode src = graph.GetNode(link.from_node);
            if (src == null)
                return result;
            if (!string.IsNullOrEmpty(src.category) && src.action == "111031")
                return ResolveIntCollection(logic, graph, src, caster, target_card, target_player);
            List<Card> up = !string.IsNullOrEmpty(src.category)
                ? (ResolveCollectionNode(logic, graph, src, caster, target_card, target_player)
                   ?? new List<Card>())
                : (target_card != null ? new List<Card> { target_card } : new List<Card>());
            string p = GraphRuntime.GetFieldString(node, "prop", "攻击");
            foreach (Card c in up)
            {
                if (c != null)
                    result.Add(GetCardProp(c, p));
            }
            return result;
        }

        /// <summary>求值取值节点「玩家」输入口的来源玩家：连入口「玩家」口 → 能力选中玩家（兜底施法卡所属玩家）；
        /// 连 NodeDoc 取值节点 → ResolvePlayerOutput；无连线 → 施法卡所属玩家。</summary>
        private static Player ResolveValuePlayer(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Player target_player)
        {
            if (graph == null || node == null)
                return PlayerOf(logic, caster);
            GraphPin pin = graph.GetPinByName(node.id, "player");
            GraphLink link = pin != null ? graph.GetIncomingLink(node.id, pin.id) : null;
            if (link != null)
            {
                GraphNode src = graph.GetNode(link.from_node);
                if (src != null && !string.IsNullOrEmpty(src.category))
                    return ResolvePlayerOutput(logic, graph, src, caster, target_player);
                if (src != null && src.type == GraphNodeType.Event)
                {
                    GraphPin out_pin = graph.GetPin(link.from_node, link.from_pin);
                    string out_name = out_pin != null ? out_pin.name : link.from_pin;
                    if (out_name == "player")
                        return (cur_event != null && cur_event.player != null) ? cur_event.player : (target_player ?? PlayerOf(logic, caster));
                    if (out_name == "enemy")
                        return OpponentOf(logic, caster);
                    return null;
                }
            }
            return PlayerOf(logic, caster);
        }

        /// <summary>求值 Player 输出的 NodeDoc 取值节点：102010 获取卡牌拥有者 / 101003 获取玩家对手；未知返回 null。</summary>
        private static Player ResolvePlayerOutput(GameLogic logic, GraphData graph, GraphNode node,
            Card caster, Player target_player)
        {
            if (node == null || logic == null)
                return null;
            switch (node.action)
            {
                case "102010":
                {
                    Card c = ResolveInputCard(logic, graph, node, "card", caster, null, target_player);
                    return c != null ? logic.GameData.GetPlayer(c.player_id) : null;
                }
                case "101003":
                {
                    Player p = ResolveValuePlayer(logic, graph, node, caster, target_player);
                    if (p == null)
                        return null;
                    int other = p.player_id == 0 ? 1 : 0;   //双人局：对手就是另一方
                    return logic.GameData.GetPlayer(other);
                }
                case "101005":   //获取当前回合的玩家
                    return logic.GameData.GetPlayer(logic.GameData.current_player);
                case "108001":   //当前事件：取事件玩家
                    return (cur_event != null && cur_event.player != null) ? cur_event.player : PlayerOf(logic, caster);
                case "108002":   //获取变量（玩家）
                    return EventVarPlayer(logic, node, caster, target_player);
                default:
                    return null;
            }
        }

        // ---------------- 目标条件求值（zmcs「目标1条件」机制，ConditionGraphTarget 调用） ----------------

        /// <summary>求值入口节点「目标1条件」输入口连着的条件链，逐候选目标调用。
        /// v1 支持：内置布尔值节点（常量/比较）+ NodeDoc 102032 卡牌类型判断；未知节点视为通过。</summary>
        public static bool EvaluateTargetCondition(GraphData graph, string entry_id, Card caster, Card target_card)
        {
            if (graph == null || string.IsNullOrEmpty(entry_id))
                return true;
            GraphNode ev = graph.GetNode(entry_id);
            GraphPin pin = graph.GetPinByName(entry_id, "targetCondition");
            if (ev == null || pin == null)
                return true;
            GraphLink link = graph.GetIncomingLink(entry_id, pin.id);
            if (link == null)
                return true;
            //logic 传 null（目标条件在选目标阶段求值，无 GameLogic 上下文；111005 包含这类需要集合求值的条件此时恒 false）
            return EvaluateConditionNode(null, graph, graph.GetNode(link.from_node), caster, target_card);
        }

        /// <summary>求值单个条件节点（候选目标以 target_card 代入）；
        /// 定义筛选时 elem_filter+cur_define 提供 111012「元素」输出口的逐元素绑定，
        /// 卡牌筛选时 cur_elem 提供当前遍历卡牌（条件节点卡牌口连「元素」输出口时生效）。</summary>
        private static bool EvaluateConditionNode(GameLogic logic, GraphData graph, GraphNode node, Card caster, Card target_card,
            GraphNode elem_filter = null, CardData cur_define = null, Card cur_elem = null)
        {
            if (node == null)
                return true;
            if (string.IsNullOrEmpty(node.category))
            {
                if (node.type == GraphNodeType.Value)
                    return GraphRuntime.EvaluateValue(graph, node) != 0;
                return true;
            }
            switch (node.action)
            {
                case "112001":  //布尔常量
                {
                    return GraphRuntime.GetFieldString(node, "value", "false") == "true";
                }
                case "-14":     //是否为法术牌
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    return c != null && c.CardData != null && c.CardData.type == CardType.Spell;
                }
                case "102028":  //卡牌是否是陷阱（奥秘 Secret）
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    return c != null && c.CardData != null && c.CardData.type == CardType.Secret;
                }
                case "101012":  //玩家是否装备道具（装备区非空）
                {
                    Player p = logic != null ? ResolveInputPlayer(logic, graph, node, "player", caster, null) : null;
                    return p != null && p.cards_equip != null && p.cards_equip.Count > 0;
                }
                case "102032":  //卡牌类型判断：卡牌口 + 卡牌类型(枚举字段) → 真值
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    return c != null && CardTypeMatches(c, GraphRuntime.GetFieldString(node, "type", ""));
                }
                case "102003":  //卡牌关键词判断：卡牌口 + 关键词 → 真值（编辑器下拉存 KeywordData.id；手填标题也认）
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    return KeywordHas(c, GraphRuntime.GetFieldString(node, "select", ""));
                }
                case "103006":  //卡牌定义关键词判断：定义口 + 关键词 → 真值
                {
                    CardData d = ResolveInputDefineFiltered(logic, graph, node, "card", elem_filter, cur_define, caster, target_card, null);
                    return DefineHasKeyword(d, GraphRuntime.GetFieldString(node, "select", ""));
                }
                case "103014":  //是否为衍生牌（TCG2：未进构筑池的牌即衍生牌）
                {
                    CardData d = ResolveInputDefineFiltered(logic, graph, node, "cardDefine", elem_filter, cur_define, caster, target_card, null);
                    return d != null && !d.deckbuilding;
                }
                case "103021":  //卡牌定义是否是陷阱（TCG2 陷阱=奥秘 Secret）
                {
                    CardData d = ResolveInputDefineFiltered(logic, graph, node, "cardDefine", elem_filter, cur_define, caster, target_card, null);
                    return d != null && d.type == CardType.Secret;
                }
                case "111022":  //是否所有元素都满足条件：集合 + 条件回调 → 布尔（空集合恒真）
                case "111023":  //是否有任意元素满足条件：集合 + 条件回调 → 布尔（空集合恒假）
                {
                    if (logic == null || graph == null)
                        return node.action == "111022";
                    if (evaluating_filters.Contains(node.id))
                        return node.action == "111022";        //条件链回环 → all 放行 / any 拒绝
                    List<Card> col = ResolveValueCards(logic, graph, node, caster, target_card, null, "array");
                    if (col.Count == 0)
                        return node.action == "111022";
                    GraphPin cpin = graph.GetPinByName(node.id, "condition");
                    GraphLink clink = cpin != null ? graph.GetIncomingLink(node.id, cpin.id) : null;
                    GraphNode csrc = clink != null ? graph.GetNode(clink.from_node) : null;
                    if (csrc == null)
                        return true;                            //无条件 → 恒真（zmcs 空条件视为真）
                    bool all = node.action == "111022";
                    evaluating_filters.Add(node.id);
                    try
                    {
                        foreach (Card c in col)
                        {
                            if (c == null)
                                continue;
                            bool ok = EvaluateConditionNode(logic, graph, csrc, caster, target_card, node, null, c);
                            if (all && !ok)
                                return false;
                            if (!all && ok)
                                return true;
                        }
                        return all;
                    }
                    finally { evaluating_filters.Remove(node.id); }
                }
                case "112005":  //逻辑运算：值口(布尔) + 运算符(且/或/非)；v1 单输入，且/或等价直通
                {
                    bool v = GetBoolInput(logic, graph, node, "value", caster, target_card, null,
                        GraphRuntime.GetFieldString(node, "value", "true") == "true");
                    string op = GraphRuntime.GetFieldString(node, "operator", "且");
                    if (op == "非")
                        return !v;
                    return v;
                }
                case "111005":  //包含：集合口 + 元素口 → 布尔（v1 元素按卡牌解析；logic 为空时集合恒空）
                {
                    List<Card> col = logic != null
                        ? ResolveValueCards(logic, graph, node, caster, target_card, null)
                        : new List<Card>();
                    Card el = ResolveInputCardFiltered(logic, graph, node, "element", elem_filter, cur_elem, caster, target_card, null);
                    return el != null && col.Contains(el);
                }
                case "101019":  //玩家是否是先手（v1 取值节点按布尔用途在此求值）
                {
                    Player p = logic != null ? ResolveValuePlayer(logic, graph, node, caster, null) : null;
                    return p != null && p.player_id == logic.GameData.first_player;
                }
                case "112009":  //是否不存在：值口解析结果为 null → 真（无连线时解析为字段常量字符串，视为存在）
                {
                    object v = GetObjectInput(logic, graph, node, "value", caster, target_card, null);
                    return v == null;
                }
                case "103024":  //卡牌定义类型判断：定义口(card) + 卡牌类型(枚举字段)
                {
                    CardData d = ResolveInputDefineFiltered(logic, graph, node, "card", elem_filter, cur_define, caster, target_card, null);
                    return d != null && DefineTypeMatches(d, GraphRuntime.GetFieldString(node, "type", ""));
                }
                case "103017":  //卡牌定义具有宣言（v1=定义带有 打出时(OnPlay) 能力）
                {
                    CardData d = ResolveInputDefineFiltered(logic, graph, node, "card", elem_filter, cur_define, caster, target_card, null);
                    return d != null && DefineHasTrigger(d, AbilityTrigger.OnPlay);
                }
                case "103018":  //卡牌定义具有遗言（v1=定义带有 死亡时(OnDeath) 能力）
                {
                    CardData d = ResolveInputDefineFiltered(logic, graph, node, "card", elem_filter, cur_define, caster, target_card, null);
                    return d != null && DefineHasTrigger(d, AbilityTrigger.OnDeath);
                }
                case "106003":  //是否具有增益：卡牌口 + buff_id(字段，BuffPoolIO 池) → 布尔
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    string buff_id = GraphRuntime.GetFieldString(node, "buff_id", "");
                    return c != null && !string.IsNullOrEmpty(buff_id) && BuffRuntime.HasBuff(c, buff_id);
                }
                case "102018":  //是否濒死：卡牌当前生命 <= 0（checkKilled"待摧毁"TCG2 无对应，忽略）
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    return c != null && c.GetHP() <= 0;
                }
                case "102021":  //卡牌具有宣言（v1=卡牌定义带 打出时(OnPlay) 能力，TCG2 无独立宣言概念）
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    return c != null && c.CardData != null && DefineHasTrigger(c.CardData, AbilityTrigger.OnPlay);
                }
                case "102022":  //卡牌具有遗言（v1=卡牌定义带 死亡时(OnDeath) 能力，TCG2 无独立遗言概念）
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    return c != null && c.CardData != null && DefineHasTrigger(c.CardData, AbilityTrigger.OnDeath);
                }
                case "105001":  //卡牌所在牌堆判断（过时版，同 105005）
                case "105005":  //卡牌所在牌堆判断：卡牌口 + 牌堆名下拖字段 → 布尔
                {
                    Card c = ResolveInputCardFiltered(logic, graph, node, "card", elem_filter, cur_elem, caster, target_card, null);
                    if (c == null)
                        return false;
                    string want = PileNormalize(GraphRuntime.GetFieldString(node, "pileName", "战场"));
                    return GetCardPileName(logic, c) == want;
                }
                case "111021":  //是否是某集合的子集：集合(set) 的所有元素都包含在 subset 中
                {
                    if (logic == null)
                        return false;
                    List<Card> a = ResolveValueCards(logic, graph, node, caster, target_card, null, "set");
                    List<Card> b = ResolveValueCards(logic, graph, node, caster, target_card, null, "subset");
                    foreach (Card x in a)
                    {
                        if (x != null && !b.Contains(x))
                            return false;
                    }
                    return true;
                }
                case "111033":  //集合内容是否相同（ignoreOrder 真=忽略顺序比较）
                {
                    if (logic == null)
                        return false;
                    List<Card> a = ResolveValueCards(logic, graph, node, caster, target_card, null, "arrayA");
                    List<Card> b = ResolveValueCards(logic, graph, node, caster, target_card, null, "arrayB");
                    bool ignore = GetBoolInput(logic, graph, node, "ignoreOrder", caster, target_card, null,
                        GraphRuntime.GetFieldString(node, "ignoreOrder", "false") == "true");
                    if (a.Count != b.Count)
                        return false;
                    if (!ignore)
                    {
                        for (int i = 0; i < a.Count; i++)
                        {
                            if (a[i] != b[i])
                                return false;
                        }
                        return true;
                    }
                    foreach (Card x in a)
                    {
                        if (x != null && !b.Contains(x))
                            return false;
                    }
                    return true;
                }
                default:
                    return true;    //未知条件节点 v1 放行（逐步扩充）
            }
        }

        /// <summary>中文卡牌类型名 → TCG2 CardType 匹配（随从/法术/英雄/神器/装备/奥秘；空=不限）</summary>
        private static bool CardTypeMatches(Card card, string type_name)
        {
            return card != null && card.CardData != null && DefineTypeMatches(card.CardData, type_name);
        }

        /// <summary>中文卡牌类型名 → CardType 匹配（卡牌定义版）</summary>
        private static bool DefineTypeMatches(CardData define, string type_name)
        {
            if (define == null || string.IsNullOrEmpty(type_name))
                return true;
            CardType t;
            switch (type_name)
            {
                case "随从":
                case "角色":
                    t = CardType.Character;
                    break;
                case "法术":
                    t = CardType.Spell;
                    break;
                case "英雄":
                    t = CardType.Hero;
                    break;
                case "神器":
                    t = CardType.Artifact;
                    break;
                case "装备":
                    t = CardType.Equipment;
                    break;
                case "奥秘":
                    t = CardType.Secret;
                    break;
                default:
                    return true;
            }
            return define.type == t;
        }

        /// <summary>卡牌定义是否带有指定触发时机的能力（宣言=OnPlay / 遗言=OnDeath）</summary>
        private static bool DefineHasTrigger(CardData define, AbilityTrigger trigger)
        {
            if (define == null || define.abilities == null)
                return false;
            foreach (AbilityData a in define.abilities)
            {
                if (a != null && a.trigger == trigger)
                    return true;
            }
            return false;
        }
    }
}
