using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Profiling;
using TcgEngine.Workshop;

namespace TcgEngine.Gameplay
{
    /// <summary>
    /// Execute and resolves game rules and logic
    /// </summary>

    public class GameLogic
    {
        public UnityAction onGameStart;
        public UnityAction<Player> onGameEnd;          //Winner

        public UnityAction onTurnStart;
        public UnityAction onTurnPlay;
        public UnityAction onTurnEnd;

        public UnityAction<Card, Slot> onCardPlayed;
        public UnityAction<Card, Slot> onCardSummoned;
        public UnityAction<Card, Slot> onCardMoved;
        public UnityAction<Card> onCardTransformed;
        public UnityAction<Card> onCardDiscarded;
        public UnityAction<int> onCardDrawn;
        public UnityAction<int> onRollValue;

        public UnityAction<AbilityData, Card> onAbilityStart;
        public UnityAction<AbilityData, Card, Card> onAbilityTargetCard;  //Ability, Caster, Target
        public UnityAction<AbilityData, Card, Player> onAbilityTargetPlayer;
        public UnityAction<AbilityData, Card, Slot> onAbilityTargetSlot;
        public UnityAction<AbilityData, Card> onAbilityEnd;

        public UnityAction<Card, Card> onAttackStart;  //Attacker, Defender
        public UnityAction<Card, Card> onAttackEnd;     //Attacker, Defender
        public UnityAction<Card, Player> onAttackPlayerStart;
        public UnityAction<Card, Player> onAttackPlayerEnd;

        public UnityAction<Card, int> onCardDamaged;
        public UnityAction<Card, int> onCardHealed;
        public UnityAction<Player, int> onPlayerDamaged;
        public UnityAction<Player, int> onPlayerHealed;

        public UnityAction<Card, Card> onSecretTrigger;    //Secret, Triggerer
        public UnityAction<Card, Card> onSecretResolve;    //Secret, Triggerer

        public UnityAction onRefresh;

        private Game game_data;

        private ResolveQueue resolve_queue;
        private bool is_ai_predict = false;

        private System.Random random = new System.Random();

        private ListSwap<Card> card_array = new ListSwap<Card>();
        private ListSwap<Player> player_array = new ListSwap<Player>();
        private ListSwap<Slot> slot_array = new ListSwap<Slot>();
        private ListSwap<CardData> card_data_array = new ListSwap<CardData>();
        private List<Card> cards_to_clear = new List<Card>();

        /// <summary>顺序逐槽多目标的选择结果：图槽号 → 选中的卡（结算期间有效；槽被跳过=null）。
        /// 由 FinishMultiTarget 写入、EffectRunGraph 读取，交给 NodeDocRunner 供入口「目标卡牌N」输出口取值。</summary>
        private Dictionary<int, Card> multi_target_results;

        // ---------------- 起动式（Activate）触发：时 / 后 ----------------
        // 「起动时触发」= OnBeforeActivate：任意玩家发动起动式能力（英雄技能/卡牌主动技/装备主动技）
        //   之前广播（可「阻止本事件」＝本次发动取消、不扣灵力）；value=本次灵力费用。
        // 「起动后触发」= OnAfterActivate：发动结算后广播（灵力已扣、exhausted 已生效、效果已结算）；
        //   该入口可配「延迟(毫秒)」/「等待事件」，两者任一满足即执行一次（由 Update 统一消费，跨回合保留）。
        private const string ACTIVATE_BEFORE = "OnBeforeActivate";
        private const string ACTIVATE_AFTER = "OnAfterActivate";

        /// <summary>该事件入口是否支持"延迟 / 等待事件后执行"（当前＝「起动后」入口）</summary>
        public static bool IsDelayableEntry(string action)
        {
            return action == ACTIVATE_AFTER;
        }

        /// <summary>一条待执行的延后触发：事件 action + 宿主卡 + 规则图 + 条件（延迟时长 / 等待事件；任一满足即执行一次）。</summary>
        private class PendingTrigger
        {
            public string action;       //入队时的事件 action（如 OnAfterActivate）
            public Card host;
            public GraphData graph;
            public string label;        //诊断用：action + 宿主卡
            public string config;       //诊断用：触发条件摘要（延迟/等待事件）
            public bool has_delay;      //是否配置了「延迟(毫秒)」
            public float remain;        //剩余秒数
            public bool delay_done;
            public bool has_wait;       //是否配置了「等待事件」
            public string wait_event;   //等待的事件 action（如 OnAfterDamage）
            public bool wait_done;
            public bool IsReady { get { return (has_delay && delay_done) || (has_wait && wait_done); } }
        }

        private readonly List<PendingTrigger> pending_triggers = new List<PendingTrigger>();
        private float game_time;        //对局内累计时间（秒，Update 累加；仅用于日志/延迟结算）

        public GameLogic(bool is_ai)
        {
            //is_instant ignores all gameplay delays and process everything immediately, needed for AI prediction
            resolve_queue = new ResolveQueue(null, is_ai);
            is_ai_predict = is_ai;
        }

        public GameLogic(Game game)
        {
            game_data = game;
            resolve_queue = new ResolveQueue(game, false);
        }

        public virtual void SetData(Game game)
        {
            game_data = game;
            resolve_queue.SetData(game);
        }

        public virtual void Update(float delta)
        {
            resolve_queue.Update(delta);
            game_time += delta;          //对局内累计时间（延迟判定与日志用）
            TickPendingTriggers(delta);
        }

        // ---------------- 触发待执行队列（延迟 / 等待事件；当前服务「起动后」）----------------

        /// <summary>消费待执行队列：延迟到点 或 等待的事件已到达 → 执行一次并出队。
        /// 统一在此处（而非广播上下文中）执行，避免嵌套进外层事件；异常已被隔离，绝不影响主流程。</summary>
        private void TickPendingTriggers(float delta)
        {
            if (pending_triggers.Count == 0)
                return;
            for (int i = pending_triggers.Count - 1; i >= 0; i--)
            {
                PendingTrigger p = pending_triggers[i];
                if (p == null || p.host == null || p.graph == null)
                {
                    pending_triggers.RemoveAt(i);   //宿主/图已失效（离场清理等）→ 丢弃，不报错
                    continue;
                }
                if (p.has_delay && !p.delay_done)
                {
                    p.remain -= delta;
                    if (p.remain <= 0f)
                    {
                        p.delay_done = true;
                        GameLog.Log("[起动触发] 延迟结束 " + p.label + " 条件=" + p.config
                            + "（对局时间 " + game_time.ToString("0.00") + "s）");
                    }
                }
                if (!p.IsReady)
                    continue;
                pending_triggers.RemoveAt(i);
                RunPendingTrigger(p, p.has_delay && p.delay_done ? "延迟到点" : "等待的事件已到达");
            }
        }

        /// <summary>标记待执行项等待的事件已到达（由 EmitGraphEvent 调用）。实际执行留到 Update，
        /// 这样"等事件"触发不会嵌在别的广播上下文里，也不参与该次事件的阻止/改值。</summary>
        private void MarkPendingWaitEvent(string action)
        {
            if (string.IsNullOrEmpty(action) || pending_triggers.Count == 0)
                return;
            for (int i = 0; i < pending_triggers.Count; i++)
            {
                PendingTrigger p = pending_triggers[i];
                if (p != null && p.has_wait && !p.wait_done && p.wait_event == action)
                {
                    p.wait_done = true;
                    GameLog.Log("[起动触发] 等待的事件已到达 " + p.label + " ← " + action);
                }
            }
        }

        /// <summary>入队一条延后触发（AI 预测实例不入队：预测只算结果，不产生业务/表现副作用）。</summary>
        private void EnqueuePendingTrigger(string action, Card host, GraphData graph, int delay_ms, string wait_event_label)
        {
            if (is_ai_predict || host == null || graph == null || string.IsNullOrEmpty(action))
                return;
            bool has_wait = !string.IsNullOrEmpty(wait_event_label) && wait_event_label != "无";
            string ev = has_wait ? MapWaitEventLabel(wait_event_label) : null;
            if (has_wait && string.IsNullOrEmpty(ev))
            {
                Debug.LogWarning("[起动触发] 无法识别的等待事件「" + wait_event_label + "」→ 该项按仅延迟处理");
                has_wait = false;
            }
            bool has_delay = delay_ms > 0;
            if (!has_delay && !has_wait)
                return;   //无延迟也无等待事件：保持"立即执行"，不入队
            string host_id = host.CardData != null ? host.CardData.id : "?";
            PendingTrigger p = new PendingTrigger
            {
                action = action,
                host = host,
                graph = graph,
                has_delay = has_delay,
                remain = delay_ms / 1000f,
                delay_done = false,
                has_wait = has_wait,
                wait_event = ev,
                wait_done = false,
                config = (has_delay ? ("延迟" + delay_ms + "ms") : "")
                         + (has_delay && has_wait ? " 或 " : "")
                         + (has_wait ? ("等待「" + wait_event_label + "」") : ""),
                label = action + " card=" + host_id,
            };
            pending_triggers.Add(p);
            GameLog.Log("[起动触发] 已入队：" + p.label + " 条件=" + p.config
                + "（就绪后在 Update 中执行一次；对局结束/重开会清空）");
        }

        /// <summary>「等待事件」下拉的中文标签 → 事件 action 名（与 EmitGraphEvent 广播名一致）</summary>
        private static string MapWaitEventLabel(string label)
        {
            switch (label)
            {
                case "打出牌": return "OnBeforePlay";
                case "伤害后": return "OnAfterDamage";
                case "治疗后": return "OnAfterHeal";
                case "死亡后": return "OnAfterDeath";
                case "装备后": return "OnAfterEquip";
                case "抽卡后": return "OnAfterDraw";
                case "回合开始后": return "OnAfterTurnStart";
                case "回合结束后": return "OnAfterTurnEnd";
                case "自己起动后": return ACTIVATE_AFTER;
                default: return null;
            }
        }

        /// <summary>执行一条待执行项：合成独立的事件上下文（不是"当前广播事件"），异常隔离。</summary>
        private void RunPendingTrigger(PendingTrigger p, string reason)
        {
            GraphEventContext ctx = new GraphEventContext();
            ctx.action = p.action;
            ctx.phase = GraphEventPhase.After;
            ctx.card = p.host;
            ctx.player = game_data != null ? game_data.GetPlayer(p.host.player_id) : null;
            ctx.value = 0;
            ctx.turn = game_data != null ? game_data.turn_count : 0;
            try
            {
                GameLog.Log("[起动触发] 执行 " + p.label + "（" + reason + "；条件=" + p.config + "）");
                NodeDocRunner.RunEvent(this, p.graph, p.host, ctx);
            }
            catch (System.Exception e)
            {
                //图异常必须隔离：否则会中断事件广播，毁掉整局
                Debug.LogError("[起动触发] " + p.label + " 执行异常，已隔离（不影响对局）: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>清空待执行队列（新对局/对局结束时调用）</summary>
        private void ResetPendingTriggers()
        {
            pending_triggers.Clear();
            game_time = 0f;
        }

        //----- Turn Phases ----------

        public virtual void StartGame()
        {
            if (game_data.state == GameState.GameEnded)
                return;

            //新对局：清空上一次对局遗留的延后触发队列（起动后 的 延迟/等待）
            ResetPendingTriggers();

            //图事件通知「对战开始时」
            EmitNotify("OnBeforeGameStart", null, null);

            //Choose first player
            game_data.state = GameState.Play;
            game_data.first_player = random.NextDouble() < 0.5 ? 0 : 1;
            game_data.current_player = game_data.first_player;
            game_data.turn_count = 1;

            //Adventure settings
            bool should_mulligan = GameplayData.Get().mulligan;
            LevelData level = game_data.settings.GetLevel();
            if (level != null)
            {
                if (level != null && level.first_player == LevelFirst.Player)
                    game_data.first_player = 0;
                if (level != null && level.first_player == LevelFirst.AI)
                    game_data.first_player = 1;
                game_data.current_player = game_data.first_player;
                should_mulligan = level.mulligan;
            }

            //Init each players
            foreach (Player player in game_data.players)
            {
                //Puzzle level deck
                DeckPuzzleData pdeck = DeckPuzzleData.Get(player.deck);

                //Hp / mana
                player.hp_max = pdeck != null ? pdeck.start_hp : GameplayData.Get().hp_start;
                player.hp = player.hp_max;
                player.mana_max = pdeck != null ? pdeck.start_mana : GameplayData.Get().mana_start;
                //三套灵力体系之"最大灵力值"：开局取配置硬顶 GameplayData.mana_max（该配置从此刻起只作"开局最大灵力值"，
                //不再参与每回合 clamp）；并保证不低于开局上限，避免出现"上限 > 最大"的自相矛盾
                player.mana_max_total = Mathf.Max(GameplayData.Get().mana_max, player.mana_max);
                if (game_data.settings != null && game_data.settings.test_full_mana)
                    player.mana_max = player.mana_max_total;   //模拟测试：开局双方法力直接为上限
                player.mana = player.mana_max;

                //Draw starting cards
                int dcards = pdeck != null ? pdeck.start_cards : GameplayData.Get().cards_start;
                DrawCard(player, dcards);

                //Add coin second player
                bool is_random = level == null || level.first_player == LevelFirst.Random;
                if (is_random && player.player_id != game_data.first_player && GameplayData.Get().second_bonus != null)
                {
                    Card card = Card.Create(GameplayData.Get().second_bonus, VariantData.GetDefault(), player);
                    player.cards_hand.Add(card);
                }
            }

            //Start state
            RefreshData();
            onGameStart?.Invoke();

            //图事件通知「对战开始后」（玩家开战点：mulligan / 首回合前）
            EmitNotify("OnAfterGameStart", null, null);

            if(should_mulligan)
                GoToMulligan();
            else
                StartTurn();
        }

        public virtual void StartTurn()
        {
            if (game_data.state == GameState.GameEnded)
                return;

            ClearTurnData();
            game_data.phase = GamePhase.StartTurn;
            RefreshData();
            onTurnStart?.Invoke();

            Player player = game_data.GetActivePlayer();

            //图事件通知「回合开始时」
            EmitNotify("OnBeforeTurnStart", null, player);

            


            //Cards draw
            if (game_data.turn_count > 1 || player.player_id != game_data.first_player)
            {
                DrawCard(player, GameplayData.Get().cards_per_turn);
            }

            //Mana：灵力上限按 mana_per_turn 增长，最多涨到"最大灵力值"（挂在玩家身上的硬顶，可被节点改写）
            if (player.mana_max_total <= 0)
                player.mana_max_total = Mathf.Max(GameplayData.Get().mana_max, player.mana_max);   //旧存档/未走开局时兜底
            player.mana_max += GameplayData.Get().mana_per_turn;
            player.mana_max = Mathf.Min(player.mana_max, player.mana_max_total);
            player.mana = player.mana_max;

            //Overload - 每点过载使英雄失去1点法力值
            if (player.hero != null && player.hero.HasStatus(StatusType.Overload))
            {
                int overload_value = player.hero.GetStatusValue(StatusType.Overload);
                player.mana = Mathf.Max(0, player.mana - overload_value);
            }

            //Turn timer and history
            game_data.turn_timer = GameplayData.Get().turn_duration;
            player.history_list.Clear();

            //Player poison
            if (player.HasStatus(StatusType.Poisoned))
                player.hp -= player.GetStatusValue(StatusType.Poisoned);

            if (player.hero != null)
                player.hero.Refresh();

            //Refresh Cards and Status Effects
            for (int i = player.cards_board.Count - 1; i >= 0; i--)
            {
                Card card = player.cards_board[i];

                if (!card.HasStatus(StatusType.Sleep))
                    card.Refresh();


                if (card.HasStatus(StatusType.Poisoned))
                    DamageCard(card, card.GetStatusValue(StatusType.Poisoned));

                //Burning - lose 1 hp, decrease value by 1
                if (card.HasStatus(StatusType.Burning))
                {
                    //Deal 1 damage
                    DamageCard(card, 1);
                    
                    //Decrease burning value by 1
                    CardStatus burningStatus = card.GetStatus(StatusType.Burning);
                    if (burningStatus != null)
                    {
                        burningStatus.value -= 1;
                        if (burningStatus.value <= 0)
                            card.RemoveStatus(StatusType.Burning);
                    }
                    
                    //Also check ongoing status
                    CardStatus burningOngoing = card.GetOngoingStatus(StatusType.Burning);
                    if (burningOngoing != null)
                    {
                        burningOngoing.value -= 1;
                        if (burningOngoing.value <= 0)
                        {
                            //Remove from ongoing_status list
                            for (int j = card.ongoing_status.Count - 1; j >= 0; j--)
                            {
                                if (card.ongoing_status[j].type == StatusType.Burning)
                                    card.ongoing_status.RemoveAt(j);
                            }
                        }
                    }
                }
            }

            //增益持续回合递减：场上/手牌卡上的 Buff 到期自动移除（含原生状态重建）
            for (int i = player.cards_board.Count - 1; i >= 0; i--)
                BuffRuntime.UpdateBuffDurations(this, player.cards_board[i]);
            for (int i = player.cards_hand.Count - 1; i >= 0; i--)
                BuffRuntime.UpdateBuffDurations(this, player.cards_hand[i]);

            //增益图「每回合开始」事件：当前回合玩家场上/手牌卡的增益效果图触发
            BuffRuntime.TriggerTurnBuff(this, player, "OnBuffTurnStart");

            //Ongoing Abilities
            UpdateOngoing();

            //StartTurn Abilities
            TriggerPlayerCardsAbilityType(player, AbilityTrigger.StartOfTurn);
            TriggerPlayerSecrets(player, AbilityTrigger.StartOfTurn);

            //图事件通知「回合开始后」（本回合处理完成，进入主阶段前）
            EmitNotify("OnAfterTurnStart", null, player);

            resolve_queue.AddCallback(StartMainPhase);
            resolve_queue.ResolveAll(0.2f);
        }

        public virtual void StartNextTurn()
        {
            if (game_data.state == GameState.GameEnded)
                return;

            Player player = game_data.GetPlayer(game_data.current_player);

            //---- 额外回合（HeroNewTurn）：value = 剩余额外回合数 ----
            //契约（与节点/效果侧一致）：AddStatus 时 duration=0 → permanent，因此不会被 EndTurn 里的
            //ReduceStatusDurations 递减，只有这里会消耗它。
            //  · value > 0  → 消耗 1 层，current_player 不变（当前玩家原地再来一个完整回合）；
            //  · value <= 0 → 视为残留/脏数据：清除状态并正常换人（防止出现无限回合锁局）。
            bool extra_turn = false;
            if (player != null && player.HasStatus(StatusType.HeroNewTurn))
            {
                int left = player.GetStatusValue(StatusType.HeroNewTurn);
                if (left > 0)
                {
                    extra_turn = true;
                    player.AddStatus(StatusType.HeroNewTurn, -1, 0);   //消耗 1 层（duration=0 保持 permanent）
                    if (player.GetStatusValue(StatusType.HeroNewTurn) <= 0)
                        player.RemoveStatus(StatusType.HeroNewTurn);   //用完即清，不留残留
                    Debug.Log("[回合] p" + player.player_id + " 触发额外回合（剩余额外回合 "
                        + player.GetStatusValue(StatusType.HeroNewTurn) + "）");
                }
                else
                {
                    player.RemoveStatus(StatusType.HeroNewTurn);
                    Debug.LogWarning("[回合] p" + player.player_id + " 的 HeroNewTurn 值为 " + left
                        + "（<=0）→ 视为残留状态，已清除并正常换人");
                }
            }

            if (!extra_turn)
                game_data.current_player = NextPlayerId(game_data.current_player);

            if (game_data.current_player == game_data.first_player)
                game_data.turn_count++;

            CheckForWinner();
            StartTurn();
        }

        /// <summary>下一个行动玩家 id：**跳过**带「失去 N 个回合」标记的玩家（每跳过一个消耗 1 层标记）。
        /// 保护：全部玩家都被跳过时保持原玩家并告警（避免死循环 / 双方都不行动导致卡死）。</summary>
        public virtual int NextPlayerId(int from_id)
        {
            int nb = game_data.settings != null ? Mathf.Max(1, game_data.settings.nb_players) : 1;
            int id = from_id;
            for (int i = 0; i < nb; i++)
            {
                id = (id + 1) % nb;
                Player p = game_data.GetPlayer(id);
                if (p == null)
                    continue;
                if (p.skip_turns > 0)
                {
                    p.skip_turns--;
                    Debug.Log("[回合] 跳过玩家 p" + id + " 的回合（剩余跳过 " + p.skip_turns + " 个）");
                    continue;
                }
                return id;
            }
            Debug.LogWarning("[回合] 所有玩家的回合都被跳过 → 保持当前玩家 p" + from_id
                + "（异常保护：请检查「失去下一回合」的施加次数）");
            return from_id;
        }

        // ---------------- 回合控制：逻辑层唯一实现（节点与 Effect 都调这里，避免逻辑散落） ----------------

        /// <summary>结束当前回合。走 resolve_queue（AddCallback + ResolveAll）而不是直接调 EndTurn：
        /// 保证当前效果结算链跑完再切回合，且不会在 resolve 回调里重入回合流程。</summary>
        public virtual bool RequestEndTurn()
        {
            if (game_data.state == GameState.GameEnded)
                return false;
            if (game_data.phase != GamePhase.Main)
            {
                Debug.LogWarning("[回合] 结束回合被忽略：当前阶段为 " + game_data.phase + "（仅主阶段可结束回合）");
                return false;
            }
            NextStep();   //取消选择 + resolve_queue.AddCallback(EndTurn) + ResolveAll()
            return true;
        }

        /// <summary>使玩家获得额外回合：加 count 层 HeroNewTurn（value=剩余额外回合数，duration=0=permanent）。
        /// 该玩家本回合结束时会被 StartNextTurn 消耗 1 层并原地再来一个回合。</summary>
        public virtual void GiveExtraTurns(Player player, int count)
        {
            if (player == null || count <= 0)
                return;
            player.AddStatus(StatusType.HeroNewTurn, count, 0);
            Debug.Log("[回合] p" + player.player_id + " 获得额外回合 ×" + count
                + "（累计剩余 " + player.GetStatusValue(StatusType.HeroNewTurn) + "）");
        }

        /// <summary>使玩家失去接下来的 count 个回合（StartNextTurn 选下家时跳过并消耗标记）</summary>
        public virtual void SkipNextTurns(Player player, int count)
        {
            if (player == null || count <= 0)
                return;
            player.skip_turns += count;
            Debug.Log("[回合] p" + player.player_id + " 将失去接下来 " + count
                + " 个回合（累计 " + player.skip_turns + "）");
        }

        public virtual void StartMainPhase()
        {
            if (game_data.state == GameState.GameEnded)
                return;

            game_data.phase = GamePhase.Main;

            //图事件通知「主阶段开始时」：此时 phase 已是 Main —— 主阶段才能做的事（如「结束回合」）在这里就能生效，
            //但界面尚未刷新（onTurnPlay/RefreshData 之后才是「主阶段开始后」）。
            EmitNotify("OnBeforeMainPhase", null, game_data.GetActivePlayer());

            onTurnPlay?.Invoke();
            RefreshData();

            //图事件通知「主阶段开始后」
            EmitNotify("OnAfterMainPhase", null, game_data.GetActivePlayer());
        }

        public virtual void EndTurn()
        {
            if (game_data.state == GameState.GameEnded)
                return;
            if (game_data.phase != GamePhase.Main)
                return;

            game_data.selector = SelectorType.None;
            game_data.phase = GamePhase.EndTurn;

            Player active_player = game_data.GetActivePlayer();

            //图事件通知「回合结束时」（开始处理结束结算前；不可阻止，避免回合永远无法结束）
            EmitNotify("OnBeforeTurnEnd", null, active_player);

            //Reduce status effects with duration
            foreach (Player aplayer in game_data.players)
            {
                aplayer.ReduceStatusDurations();
                foreach (Card card in aplayer.cards_board)
                    card.ReduceStatusDurations();
                foreach (Card card in aplayer.cards_equip)
                    card.ReduceStatusDurations();

                //Regenerate
                foreach (Card card in aplayer.cards_board)
                {
                    if (card.HasStatus(StatusType.Regenerate))
                        HealCard(card, card.hp);
                }
            }

            //增益图「每回合结束」事件：当前回合玩家场上/手牌卡的增益效果图触发
            BuffRuntime.TriggerTurnBuff(this, active_player, "OnBuffTurnEnd");

            //Doomed - kill at end of turn (only for active player's cards)
            List<Card> doomed_cards = new List<Card>();
            foreach (Card card in active_player.cards_board)
            {
                if (card.HasStatus(StatusType.Doomed))
                    doomed_cards.Add(card);
            }
            foreach (Card card in doomed_cards)
            {
                KillCard(null, card);
            }

            //Freezing - kill if freezing value >= hp at end of turn
            List<Card> frozen_cards = new List<Card>();
            foreach (Player aplayer in game_data.players)
            {
                foreach (Card card in aplayer.cards_board)
                {
                    if (card.HasStatus(StatusType.Freezing))
                    {
                        int freezing_value = card.GetStatusValue(StatusType.Freezing);
                        int current_hp = card.GetHP();
                        if (freezing_value >= current_hp)
                        {
                            frozen_cards.Add(card);
                        }
                    }
                }
            }
            foreach (Card card in frozen_cards)
            {
                KillCard(null, card);
            }

            //End of turn abilities
            TriggerPlayerCardsAbilityType(active_player, AbilityTrigger.EndOfTurn);



            onTurnEnd?.Invoke();
            RefreshData();

            //图事件通知「回合结束后」
            EmitNotify("OnAfterTurnEnd", null, active_player);

            resolve_queue.AddCallback(StartNextTurn);
            resolve_queue.ResolveAll(0.2f);
        }

        //End game with winner
        public virtual void EndGame(int winner)
        {
            if (game_data.state != GameState.GameEnded)
            {
                Player winner_player = game_data.GetPlayer(winner);

                //图事件通知「游戏结束时」
                EmitNotify("OnBeforeGameEnd", null, winner_player);

                game_data.state = GameState.GameEnded;
                game_data.phase = GamePhase.None;
                game_data.selector = SelectorType.None;
                game_data.current_player = winner; //Winner player
                resolve_queue.Clear();
                ResetPendingTriggers();     //对局结束：丢弃尚未到点的延后触发，避免结束后再执行
                onGameEnd?.Invoke(winner_player);
                RefreshData();

                //图事件通知「游戏结束后」
                EmitNotify("OnAfterGameEnd", null, winner_player);
            }
        }

        //Progress to the next step/phase 
        public virtual void NextStep()
        {
            if (game_data.state == GameState.GameEnded)
                return;

            if (game_data.phase == GamePhase.Mulligan)
            {
                StartTurn();
                return;
            }

            CancelSelection();

            //Add to resolve queue in case its still resolving
            resolve_queue.AddCallback(EndTurn);
            resolve_queue.ResolveAll();
        }

        /// <summary>玩家点击战斗界面自定义按钮：仅自己回合可点，执行全局按钮图对应按钮分支。
        /// 由 GameServer.ReceiveBattleButton 调用（走服务器校验，防回合外操作）。</summary>
        public virtual void PressBattleButton(Player player, string button_id)
        {
            if (player == null || string.IsNullOrEmpty(button_id))
                return;
            if (game_data.state != GameState.Play)
                return;
            if (!game_data.IsPlayerTurn(player))
                return;   //仅自己回合可点
            //只允许点"本局自己的按钮栏里确实有"的按钮（按钮栏是每个玩家各自的局内临时列表；开局为空 → 什么都点不了）
            if (player.battle_buttons == null || !player.battle_buttons.Contains(button_id))
            {
                Debug.LogWarning("[按钮栏] 拒绝点击：本局按钮栏里没有 " + button_id);
                return;
            }
            BattleButtonConfig cfg = BattleButtonIO.GetConfig();
            if (cfg == null)
                return;
            Card hero = player.hero;
            if (hero == null)
                return;
            //多张按钮图：逐张执行（每张图内部各自匹配「点击按钮时/后」的 button_id 分支）
            System.Collections.Generic.List<CardEffectData> graphs = cfg.EnsureGraphs();
            int ran = 0;
            for (int i = 0; i < graphs.Count; i++)
            {
                if (graphs[i] == null || graphs[i].graph == null)
                    continue;
                ran += NodeDocRunner.RunButtonClick(this, graphs[i].graph, hero, button_id);
            }

            //★ 点完立刻结算并同步（否则"点了没反应"，要等下一个操作/回合结束才一起结算）：
            //   ① UpdateOngoing：把图里挂起的伤害/持续效果真正落到 hp/属性（DamageCard 只累加 card.damage）
            //   ② ResolveAll：跑完按钮图排队的能力/攻击回调
            //   ③ RefreshData：onRefresh → GameServer.RefreshAll，把新状态立刻推给客户端
            UpdateOngoing();
            resolve_queue.ResolveAll();
            RefreshData();
            Debug.Log("[按钮栏] 执行按钮图：" + button_id + "（图 " + graphs.Count + " 张，NodeDoc 动作 " + ran + " 个）→ 已立即结算并同步");
        }

        // ---------------- 局内按钮栏（增加 / 删除按钮节点调这里；不写 buttons.json） ----------------

        /// <summary>给指定玩家的按钮栏在第 pos 个位置插入按钮（1 起算；pos&lt;=0 或超出 = 追加到最后），后面依次顺延。</summary>
        public virtual void AddBattleButton(Player player, string button_id, int pos)
        {
            if (player == null || string.IsNullOrEmpty(button_id))
                return;
            if (player.battle_buttons == null)
                player.battle_buttons = new List<string>();
            int idx = (pos <= 0 || pos > player.battle_buttons.Count) ? player.battle_buttons.Count : pos - 1;
            player.battle_buttons.Insert(idx, button_id);
            RefreshData();
            Debug.Log("[按钮栏] p" + player.player_id + " 增加按钮 " + button_id
                + " → 第 " + (idx + 1) + " 位（共 " + player.battle_buttons.Count + " 个）");
        }

        /// <summary>删除指定玩家按钮栏第 pos 个按钮（1 起算；pos&lt;=0 或超出 = 第一个），后面依次前移。</summary>
        public virtual void RemoveBattleButton(Player player, int pos)
        {
            if (player == null || player.battle_buttons == null || player.battle_buttons.Count == 0)
                return;
            int idx = (pos <= 0 || pos > player.battle_buttons.Count) ? 0 : pos - 1;
            string removed = player.battle_buttons[idx];
            player.battle_buttons.RemoveAt(idx);
            RefreshData();
            Debug.Log("[按钮栏] p" + player.player_id + " 删除第 " + (idx + 1) + " 个按钮 " + removed
                + "（剩余 " + player.battle_buttons.Count + " 个）");
        }

        // ---------------- 图事件广播（EventContext，全场监听 + 时/后） ----------------

        private const int EVENT_MAX_DEPTH = 16;         //图事件广播递归深度上限（防 伤害后→再伤害→… 死循环）
        private static int event_depth;                  //跨 GameLogic 实例共享的递归计数（AI 预测实例不广播）
        private GraphEventContext event_ctx;             //当前广播上下文（保留旧值用于嵌套返回）
        private readonly List<GraphEventContext> event_log = new List<GraphEventContext>();   //事件日志（108005~108016 / 109xxx 查询用）
        private const int EVENT_LOG_MAX = 512;           //日志上限：超出丢弃最旧（避免长对局无限增长）

        /// <summary>当前图事件上下文（广播中非空；NodeDocRunner 读取用）</summary>
        public GraphEventContext EventCtx { get { return event_ctx; } }

        // ---- 事件日志查询（事件家族 108005~108016 / 事件记录家族 109xxx） ----

        /// <summary>本局已发生的事件日志（按广播先后顺序；含父/子链、回合、重复次数）</summary>
        public List<GraphEventContext> GetEventLog() { return event_log; }

        /// <summary>指定回合发生的事件</summary>
        public List<GraphEventContext> GetTurnEvents(int turn)
        {
            List<GraphEventContext> result = new List<GraphEventContext>();
            foreach (GraphEventContext e in event_log)
                if (e.turn == turn)
                    result.Add(e);
            return result;
        }

        /// <summary>按日志索引区间 [from, to] 取事件（闭区间；自动裁剪越界）</summary>
        public List<GraphEventContext> GetRangeEvents(int from, int to)
        {
            List<GraphEventContext> result = new List<GraphEventContext>();
            if (event_log.Count == 0)
                return result;
            int a = from < 0 ? 0 : from;
            int b = to >= event_log.Count ? event_log.Count - 1 : to;
            for (int i = a; i <= b; i++)
                result.Add(event_log[i]);
            return result;
        }

        /// <summary>
        /// 广播一次图事件：按固定顺序触发所有"在场图宿主"（额外宿主 extra_host 优先，如被打出的牌自身）。
        /// Before（「时」）：某宿主图执行「阻止本事件」后立即停止后续广播（先到先得），返回 true；
        /// After（「后」）：不检查取消，全部宿主广播完返回 false。
        /// </summary>
        public bool EmitGraphEvent(GraphEventContext ctx, Card extra_host = null)
        {
            if (ctx == null || string.IsNullOrEmpty(ctx.action) || game_data == null)
                return false;
            if (is_ai_predict)
                return false;   //AI 预测只算结果，不广播图事件
            if (event_depth >= EVENT_MAX_DEPTH)
            {
                Debug.LogWarning("[图事件] 广播深度超限，丢弃: " + ctx.action + "（检查图是否存在事件循环）");
                return false;
            }

            event_depth++;
            GraphEventContext prev = event_ctx;
            event_ctx = ctx;
            //事件日志与父子链（108005~108016 / 109xxx）：父=外层广播上下文；回合=当前回合；
            //主体卡「广播前」快照（108008 用 Card.CloneNew 深克隆）
            ctx.parent = prev;
            if (ctx.turn <= 0)
                ctx.turn = game_data.turn_count;
            if (prev != null)
                prev.children.Add(ctx);
            if (ctx.card != null)
                ctx.card_before = Card.CloneNew(ctx.card);
            event_log.Add(ctx);
            if (event_log.Count > EVENT_LOG_MAX)
                event_log.RemoveRange(0, event_log.Count - EVENT_LOG_MAX);
            try
            {
                //延后触发项若正等待本事件到达 → 标记（实际执行留到 Update，避免嵌套进本次广播）
                MarkPendingWaitEvent(ctx.action);

                //宿主收集与排序（与「事件入口预览」共用同一套规则）
                List<Card> hosts = CollectEventHosts(extra_host, ctx.action);
                int extra_count = extra_host != null ? 1 : 0;
                for (int i = 0; i < hosts.Count; i++)
                {
                    FireEventHost(hosts[i], ctx, i < extra_count);
                    if (ctx.phase == GraphEventPhase.Before && ctx.cancelled)
                        break;  //先到先得：某宿主阻止后，后续监听者不再收到本事件
                }
                //主体卡「广播后」快照（108009 用）
                if (ctx.card != null)
                    ctx.card_after = Card.CloneNew(ctx.card);
                //★玩家自定义事件节点（DIY「事件声明」）：XX时 / XX后 两段编排随同一次广播执行
                //  （内部按 listen_action 匹配 + 异常隔离，坏图不影响对局；不配置监听事件的定义不参与）
                NodeDocRunner.RunCustomEventDefs(this, ctx);
                return ctx.cancelled;
            }
            finally
            {
                event_ctx = prev;
                event_depth--;
            }
        }

        /// <summary>纯通知事件（回合/对局类：对战开始结束、回合开始结束）。发出即广播，返回值/阻止被忽略——
        /// 这类事件不可被「阻止本事件」取消（否则会造成对局永久锁死）。</summary>
        private void EmitNotify(string action, Card card, Player player)
        {
            if (string.IsNullOrEmpty(action) || game_data == null)
                return;
            GraphEventContext ctx = new GraphEventContext();
            ctx.action = action;
            ctx.phase = GraphEventPhase.Before;
            ctx.card = card;
            ctx.player = player;
            ctx.value = 0;
            EmitGraphEvent(ctx);
        }

        /// <summary>收集一次广播的宿主（固定顺序）：事件主体卡(extra_host，任意牌堆都能响应)优先 →
        /// 双方 英雄/战场/装备区/奥秘区/手牌/牌库/墓地；再按入口「优先级」降序做稳定排序。
        /// 抽成独立方法：让「事件入口预览」（只读、可独立测试）与真实广播共用同一套宿主规则。</summary>
        private List<Card> CollectEventHosts(Card extra_host, string event_action)
        {
            List<Card> hosts = new List<Card>();
            if (game_data == null)
                return hosts;
            if (extra_host != null)
                hosts.Add(extra_host);
            foreach (Player p in game_data.players)
            {
                if (p == null)
                    continue;
                if (p.hero != null && !hosts.Contains(p.hero))
                    hosts.Add(p.hero);
                foreach (Card c in p.cards_board)
                    if (c != null && !hosts.Contains(c))
                        hosts.Add(c);
                foreach (Card c in p.cards_equip)
                    if (c != null && !hosts.Contains(c))
                        hosts.Add(c);
                foreach (Card c in p.cards_secret)
                    if (c != null && !hosts.Contains(c))
                        hosts.Add(c);
                foreach (Card c in p.cards_hand)
                    if (c != null && !hosts.Contains(c))
                        hosts.Add(c);
                foreach (Card c in p.cards_deck)
                    if (c != null && !hosts.Contains(c))
                        hosts.Add(c);
                foreach (Card c in p.cards_discard)
                    if (c != null && !hosts.Contains(c))
                        hosts.Add(c);
            }
            int extra_count = extra_host != null ? 1 : 0;
            //同事件多宿主：按入口「优先级」降序（额外宿主=事件主体卡固定最先）；同优先级保持收集顺序
            if (!string.IsNullOrEmpty(event_action) && hosts.Count > extra_count + 1)
            {
                List<Card> rest = hosts.GetRange(extra_count, hosts.Count - extra_count);
                rest.Sort((a, b) => HostEventPriority(b, event_action).CompareTo(HostEventPriority(a, event_action)));
                hosts.RemoveRange(extra_count, hosts.Count - extra_count);
                hosts.AddRange(rest);
            }
            return hosts;
        }

        /// <summary>取图里第一个匹配该 action 的事件入口节点（无则 null）</summary>
        private static GraphNode FindEntryNode(GraphData graph, string action)
        {
            if (graph == null || graph.nodes == null)
                return null;
            foreach (GraphNode n in graph.nodes)
            {
                if (n != null && n.type == GraphNodeType.Event && n.action == action)
                    return n;
            }
            return null;
        }

        /// <summary>
        /// 【可独立测试】只读预览：本次对局里 action（如 <see cref="ACTIVATE_BEFORE"/>/<see cref="ACTIVATE_AFTER"/>）
        /// 会命中哪些宿主入口、按什么顺序执行、参数是什么。不执行任何动作、不改任何状态，
        /// 也不会入队/触发，可在对局任意时刻调用（Console / 自动化测试）验证节点配置是否正确。
        /// </summary>
        public string PreviewEventTriggers(string action)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[事件触发] 预览 ").Append(action).Append("：");
            if (game_data == null)
            {
                sb.Append("无对局数据");
                return sb.ToString();
            }
            List<Card> hosts = CollectEventHosts(null, action);
            int n = 0;
            for (int i = 0; i < hosts.Count; i++)
            {
                Card host = hosts[i];
                if (host == null || host.HasStatus(StatusType.Silenced))
                    continue;   //被沉默的卡不响应图事件（与 FireEventHost 同规）
                string zone = GetCardZone(host);
                foreach (AbilityData ab in host.GetAbilities())
                {
                    if (ab == null || ab.trigger.ToString() != action)
                        continue;
                    foreach (EffectData eff in ab.effects)
                    {
                        EffectRunGraph rg = eff as EffectRunGraph;
                        if (rg == null || rg.graph == null)
                            continue;
                        if (!string.IsNullOrEmpty(rg.trigger_action) && rg.trigger_action != action)
                            continue;
                        if (!EntryZoneAllows(rg.graph, action, zone))
                            continue;   //入口「生效区域」不含宿主所在牌堆 → 不会触发
                        GraphNode entry = FindEntryNode(rg.graph, action);
                        n++;
                        sb.Append("\n  #").Append(n).Append(' ')
                          .Append(host.CardData != null ? host.CardData.id : "?")
                          .Append("（").Append(string.IsNullOrEmpty(zone) ? "不在牌堆" : zone).Append('）')
                          .Append(" 优先级=").Append(entry != null ? GraphRuntime.GetFieldInt(entry, "priority", 0) : 0)
                          .Append(" 生效区域=").Append(entry != null ? GraphRuntime.GetFieldString(entry, "zones", "(未设置→按默认)") : "(无入口节点)");
                        if (entry != null && IsDelayableEntry(action))
                        {
                            int d = GraphRuntime.GetFieldInt(entry, "delay_ms", 0);
                            string w = GraphRuntime.GetFieldString(entry, "wait_event", "无");
                            sb.Append(" 延迟=").Append(d).Append("ms 等待事件=").Append(string.IsNullOrEmpty(w) ? "无" : w);
                        }
                    }
                }
            }
            if (n == 0)
                sb.Append("（无匹配入口 → 检查：生效区域是否含宿主所在牌堆 / 图的入口 action 是否为 ").Append(action)
                  .Append(" / 该卡是否引用了这张图 / 卡是否被沉默）");
            sb.Append("\n  延后待执行队列 = ").Append(pending_triggers.Count).Append(" 项");
            return sb.ToString();
        }

        /// <summary>宿主对某事件入口的优先级：读该宿主匹配事件入口节点的 priority 字段，取最高值（无则 int.MinValue）</summary>
        private int HostEventPriority(Card host, string event_action)
        {
            int best = int.MinValue;
            if (host == null || string.IsNullOrEmpty(event_action))
                return best;
            foreach (AbilityData ab in host.GetAbilities())
            {
                if (ab == null || ab.trigger.ToString() != event_action)
                    continue;
                foreach (EffectData eff in ab.effects)
                {
                    EffectRunGraph rg = eff as EffectRunGraph;
                    if (rg == null || rg.graph == null || rg.graph.nodes == null)
                        continue;
                    foreach (GraphNode ev in rg.graph.nodes)
                    {
                        if (ev == null || ev.type != GraphNodeType.Event || ev.action != event_action)
                            continue;
                        int pr = GraphRuntime.GetFieldInt(ev, "priority", 0);
                        if (pr > best)
                            best = pr;
                    }
                }
            }
            return best;
        }

        /// <summary>触发单个宿主：能力 trigger 匹配且含 EffectRunGraph 的事件图才交给 NodeDocRunner 执行。
        /// is_extra=事件主体卡（任意牌堆都可响应）；普通宿主按其事件入口「生效牌堆」字段过滤当前所在区域。</summary>
        private void FireEventHost(Card host, GraphEventContext ctx, bool is_extra)
        {
            if (host == null || ctx == null)
                return;
            if (host.HasStatus(StatusType.Silenced))
                return;     //被沉默的卡不响应图事件
            string zone = GetCardZone(host);   //宿主当前所在牌堆（空=不在任何已知牌堆）

            foreach (AbilityData ab in host.GetAbilities())
            {
                if (ab == null || ab.trigger.ToString() != ctx.action)
                    continue;
                foreach (EffectData eff in ab.effects)
                {
                    EffectRunGraph rg = eff as EffectRunGraph;
                    if (rg == null || rg.graph == null)
                        continue;
                    if (!string.IsNullOrEmpty(rg.trigger_action) && rg.trigger_action != ctx.action)
                        continue;
                    if (!is_extra && !EntryZoneAllows(rg.graph, ctx.action, zone))
                        continue;   //入口「生效堆」不含宿主当前所在牌堆 → 该宿主不响应
                    //定位入口节点：写「标签列表」；「启动后触发」还要读 延迟/等待事件 参数
                    GraphNode entry = FindEntryNode(rg.graph, ctx.action);
                    if (entry != null)
                    {
                        string tags = GraphRuntime.GetFieldString(entry, "tags", "");
                        if (string.IsNullOrEmpty(tags))
                            tags = GraphRuntime.GetFieldString(entry, "tag_list", "");
                        if (!string.IsNullOrEmpty(tags))
                            ctx.tags = tags;
                    }

                    //可延后的入口（起动后）：配了 延迟(毫秒) 或 等待事件 → 入队延后执行（两者任一满足即执行一次）；
                    //未配置则保持"发动结算后立即同步执行"的默认行为
                    if (entry != null && IsDelayableEntry(ctx.action))
                    {
                        int delay_ms = GraphRuntime.GetFieldInt(entry, "delay_ms", 0);
                        string wait_label = GraphRuntime.GetFieldString(entry, "wait_event", "无");
                        if (delay_ms > 0 || (!string.IsNullOrEmpty(wait_label) && wait_label != "无"))
                        {
                            EnqueuePendingTrigger(ctx.action, host, rg.graph, delay_ms, wait_label);
                            continue;
                        }
                    }

                    //起动式入口（起动时/起动后）：异常隔离——一个坏图不允许中断能力发动或整次广播
                    if (ctx.action == ACTIVATE_BEFORE || ctx.action == ACTIVATE_AFTER)
                    {
                        try
                        {
                            NodeDocRunner.RunEvent(this, rg.graph, host, ctx);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError("[起动触发] " + (host.CardData != null ? host.CardData.id : "?")
                                + " 的 " + ctx.action + " 图执行异常，已隔离（不影响对局）: " + e.Message);
                        }
                    }
                    else
                    {
                        NodeDocRunner.RunEvent(this, rg.graph, host, ctx);
                    }
                }
            }
        }

        /// <summary>卡牌当前所在牌堆（英雄/战场/装备区/奥秘区/手牌/牌库/墓地）；不在任一牌堆返回空</summary>
        private string GetCardZone(Card c)
        {
            if (c == null || game_data == null)
                return "";
            foreach (Player p in game_data.players)
            {
                if (p == null)
                    continue;
                if (p.hero == c)
                    return "英雄";
                if (p.cards_board.Contains(c))
                    return "战场";
                if (p.cards_equip.Contains(c))
                    return "装备区";
                if (p.cards_secret.Contains(c))
                    return "奥秘区";
                if (p.cards_hand.Contains(c))
                    return "手牌";
                if (p.cards_deck.Contains(c))
                    return "牌库";
                if (p.cards_discard.Contains(c))
                    return "墓地";
            }
            return "";
        }

        /// <summary>事件入口「生效牌堆」字段过滤：字段含「任意」或字段为空（旧图）→ 放行；
        /// 否则宿主当前所在牌堆必须在勾选列表内才响应</summary>
        private bool EntryZoneAllows(GraphData graph, string action, string zone)
        {
            if (graph == null || graph.nodes == null)
                return true;
            foreach (GraphNode ev in graph.nodes)
            {
                if (ev == null || ev.type != GraphNodeType.Event || ev.action != action)
                    continue;
                string zones = GraphRuntime.GetFieldString(ev, "zones", "");
                if (string.IsNullOrEmpty(zones))
                    zones = EventZoneDefault(action);   //旧图无字段：按事件默认
                if (zones.Contains("任意"))
                    return true;
                if (string.IsNullOrEmpty(zone))
                    return false;
                foreach (string z in zones.Split(';'))
                {
                    if (z == zone)
                        return true;
                }
                return false;
            }
            return true;
        }

        /// <summary>事件默认生效牌堆（入口节点无 zones 字段的旧图；新图由编辑器预设写入默认值）</summary>
        private static string EventZoneDefault(string action)
        {
            switch (action)
            {
                case "OnBeforePlay":
                case "OnBeforeDamage":
                case "OnAfterDamage":
                case "OnAfterDraw":
                case "OnBeforeHeal":
                case "OnAfterHeal":
                case "OnBeforeTransform":
                case "OnAfterTransform":
                case "OnBeforeEquip":
                case "OnAfterEquip":
                case "OnBeforeDeath":
                case "OnAfterDeath":
                case "OnBeforeDiscard":
                case "OnAfterDiscard":
                case "OnBeforeGameStart":
                case "OnAfterGameStart":
                case "OnBeforeGameEnd":
                case "OnAfterGameEnd":
                case "OnBeforeTurnStart":
                case "OnAfterTurnStart":
                case "OnBeforeTurnEnd":
                case "OnAfterTurnEnd":
                case "OnBeforeActivate":   //起动时：载体=英雄/场上卡/装备卡
                case "OnAfterActivate":    //起动后：同上
                    return "英雄;战场;装备区";
                default:
                    return "任意";
            }
        }

        //Check if a player is winning the game, if so end the game
        //Change or edit this function for a new win condition
        protected virtual void CheckForWinner()
        {
            int count_alive = 0;
            Player alive = null;
            foreach (Player player in game_data.players)
            {
                if (!player.IsDead())
                {
                    alive = player;
                    count_alive++;
                }
            }

            if (count_alive == 0)
            {
                EndGame(-1); //Everyone is dead, Draw
            }
            else if (count_alive == 1)
            {
                EndGame(alive.player_id); //Player win
            }
        }

        protected virtual void ClearTurnData()
        {
            game_data.selector = SelectorType.None;
            game_data.selector_slot_index = 0;
            game_data.selector_slot_nodes = null;
            game_data.selector_selected_uids = null;
            multi_target_results = null;
            resolve_queue.Clear();
            card_array.Clear();
            player_array.Clear();
            slot_array.Clear();
            card_data_array.Clear();
            game_data.last_played = null;
            game_data.last_destroyed = null;
            game_data.last_target = null;
            game_data.last_summoned = null;
            game_data.ability_triggerer = null;
            game_data.selected_value = 0;
            game_data.ability_played.Clear();
            game_data.cards_attacked.Clear();
        }

        //--- Setup ------

        //Set deck using a Deck in Resources
        public virtual void SetPlayerDeck(Player player, DeckData deck)
        {
            player.cards_all.Clear();
            player.cards_deck.Clear();
            player.deck = deck.id;
            player.hero = null;

            VariantData variant = VariantData.GetDefault();
            if (deck.hero != null)
            {
                Card hero_card = Card.Create(deck.hero, variant, player);   //CardData 为空时返回 null（不再 NRE）
                if (hero_card != null)
                    player.hero = hero_card;
                else
                    Debug.LogError("[Game] 英雄卡创建失败（hero 定义缺失）：" + deck.hero.id);
            }

            foreach (CardData card in deck.cards)
            {
                if (card == null)
                    continue;
                Card acard = Card.Create(card, variant, player);            //同上：创建失败返回 null → 跳过该张
                if (acard != null)
                    player.cards_deck.Add(acard);
                else
                    Debug.LogError("[Game] 卡组里有创建失败的卡（已跳过，卡组会少一张）：" + card.id);
            }

            DeckPuzzleData puzzle = deck as DeckPuzzleData;

            //Board cards
            if (puzzle != null)
            {
                foreach (DeckCardSlot card in puzzle.board_cards)
                {
                    Card acard = Card.Create(card.card, variant, player);
                    acard.slot = new Slot(card.slot, Slot.GetP(player.player_id));
                    player.cards_board.Add(acard);
                }
            }

            //Shuffle deck
            if (puzzle == null || !puzzle.dont_shuffle_deck)
                ShuffleDeck(player.cards_deck);
        }

        //Set deck using custom deck in save file or database
        public virtual void SetPlayerDeck(Player player, UserDeckData deck)
        {
            player.cards_all.Clear();
            player.cards_deck.Clear();
            player.deck = deck.tid;
            player.hero = null;

            if (deck.hero != null)
            {
                CardData hdata = CardData.Get(deck.hero.tid);
                VariantData hvariant = VariantData.Get(deck.hero.variant);
                if (hdata != null && hvariant != null)
                    player.hero = Card.Create(hdata, hvariant, player);
                else
                    Debug.LogWarning("[Game] 卡组英雄解析失败：tid=" + deck.hero.tid + " → 玩家 p" + player.player_id
                        + " 将没有英雄卡（「获取玩家英雄」类节点与英雄伤害/治疗都会失效）");
            }

            foreach (UserCardData card in deck.cards)
            {
                CardData icard = CardData.Get(card.tid);
                VariantData variant = VariantData.Get(card.variant);
                if (icard == null || variant == null)
                {
                    //静默跳过会导致「整副卡组为空 → 开局即判负」，这里必须留痕便于排查
                    Debug.LogWarning("[Game] 组卡跳过无效卡：tid=" + card.tid + " variant=" + card.variant +
                        "（卡牌数据未注册/已删除，或变体不存在）→ 玩家 p" + player.player_id);
                    continue;
                }
                {
                    for (int i = 0; i < card.quantity; i++)
                    {
                        Card acard = Card.Create(icard, variant, player);
                        player.cards_deck.Add(acard);
                    }
                }
            }

            //Shuffle deck
            ShuffleDeck(player.cards_deck);

            //空卡组会在开局按「手牌/战场/牌库全空=死亡」立即判负（Player.IsDead），这里显式报出来
            if (player.cards_deck.Count == 0)
                Debug.LogError("[Game] 玩家 p" + player.player_id + " 卡组为空（卡组没配卡或全部卡牌无效）→ 开局将立即判负");
        }

        //---- Gameplay Actions --------------

        public virtual void PlayCard(Card card, Slot slot, bool skip_cost = false)
        {
            if (game_data.CanPlayCard(card, slot, skip_cost))
            {
                Player player = game_data.GetPlayer(card.player_id);

                //图事件「使用卡牌时」（全场监听；可阻止=取消本次打出，不扣费、无后续；value=当前费用供读取/修改）
                if (!skip_cost)
                {
                    GraphEventContext pctx = new GraphEventContext();
                    pctx.action = "OnBeforePlay";
                    pctx.phase = GraphEventPhase.Before;
                    pctx.card = card;
                    pctx.source_card = card;
                    pctx.player = player;
                    pctx.value = card.GetMana();
                    if (EmitGraphEvent(pctx, card))
                        return;
                }

                //Cost
                if (!skip_cost)
                    player.PayMana(card);

                //Play card
                player.RemoveCardFromAllGroups(card);

                //Add to board
                CardData icard = card.CardData;
                if (icard.IsBoardCard())
                {
                    
                    player.cards_board.Add(card);
                    card.slot = slot;
                    card.AddStatus(StatusType.SummonDisorder, 0, 1);
                    //card.AddStatus(StatusType.disorder, 1, 1);
                    //Debug.Log(card.GetOngoingStatus(StatusType.Haste));
                    //Debug.Log(card.GetOngoingStatus(StatusType.Fury));
                    //Debug.Log(card.GetOngoingStatus(StatusType.disorder));


                }
                else if (icard.IsEquipment())
                {
                    Card bearer = game_data.GetSlotCard(slot);
                    EquipCard(bearer, card);
                    //card.exhausted = true;
                }
                else if (icard.IsSecret())
                {
                    player.cards_secret.Add(card);
                }
                else
                {
                    player.cards_discard.Add(card);
                    card.slot = slot; //Save slot in case spell has PlayTarget
                }

                //History
                if (!is_ai_predict && !icard.IsSecret())
                    player.AddHistory(GameAction.PlayCard, card);

                //Update ongoing effects
                game_data.last_played = card.uid;
                UpdateOngoing();

                //Trigger abilities
                if (card.CardData.IsDynamicManaCost())
                {
                    GoToSelectorCost(card);
                }
                else
                {
                    TriggerSecrets(AbilityTrigger.OnPlayOther, card); //After playing card
                    TriggerCardAbilityType(AbilityTrigger.OnPlay, card);
                    TriggerOtherCardsAbilityType(AbilityTrigger.OnPlayOther, card);
                }

                //Re-check ongoing effects after all abilities are triggered
                UpdateOngoing();

                RefreshData();

                onCardPlayed?.Invoke(card, slot);
                resolve_queue.ResolveAll(0.3f);
            }
        }

        /// <summary>把任意牌堆（牌库/墓地/手牌/自定义堆）里的一张卡直接置入战场：
        /// 不要求在手牌、不扣费，走与 PlayCard 相同的入场触发（入场/战吼、OnPlayOther）。
        /// 返回是否成功（非法目标/格子被占/非战场卡返回 false）。供「卡牌置入战场(210002)」等节点使用。</summary>
        public virtual bool PlaceCardOnBoard(Card card, Slot slot)
        {
            if (!game_data.CanPlaceCardOnBoard(card, slot))
                return false;

            Player player = game_data.GetPlayer(card.player_id);

            //从当前所在牌堆摘除（RemoveCardFromAllGroups 覆盖 手牌/牌库/墓地/装备/奥秘/暂存/战场）
            player.RemoveCardFromAllGroups(card);

            //Add to board
            player.cards_board.Add(card);
            card.slot = slot;
            card.AddStatus(StatusType.SummonDisorder, 0, 1);   //与 PlayCard 一致：当回合行动限制

            //History
            if (!is_ai_predict)
                player.AddHistory(GameAction.PlayCard, card);
            game_data.last_summoned = card.uid;

            //Update ongoing effects
            UpdateOngoing();

            //Trigger abilities（与 PlayCard 的入场触发段保持一致）
            TriggerSecrets(AbilityTrigger.OnPlayOther, card);
            TriggerCardAbilityType(AbilityTrigger.OnPlay, card);
            TriggerOtherCardsAbilityType(AbilityTrigger.OnPlayOther, card);

            //Re-check ongoing effects after all abilities are triggered
            UpdateOngoing();
            RefreshData();

            onCardSummoned?.Invoke(card, slot);
            return true;
        }

        public virtual void MoveCard(Card card, Slot slot, bool skip_cost = false)
        {
            Player player = game_data.GetPlayer(card.player_id);
            if (game_data.CanMoveCard(card, slot, skip_cost))
            {
                card.slot = slot;
                if(!skip_cost)
                card.exhausted = true;
                //Moving doesn't really have any effect in demo so can be done indefinitely

                //card.RemoveStatus(StatusEffect.Stealth);
                
                player.AddHistory(GameAction.Move, card);

                //Also move the equipment
                Card equip = game_data.GetEquipCard(card.equipped_uid);
                if (equip != null)
                    equip.slot = slot;

                UpdateOngoing();
                RefreshData();

                onCardMoved?.Invoke(card, slot);
                resolve_queue.ResolveAll(0.2f);
            }
        }

        public virtual void CastAbility(Card card, AbilityData iability)
        {
            if (game_data.CanCastAbility(card, iability))
            {
                //图事件「起动时」：发动结算前广播（可「阻止本事件」= 本次发动取消、不扣灵力）；value=灵力费用
                if (!EmitActivateBefore(card, iability))
                    return;   //被阻止：不记历史、不横置、不扣费、不结算

                Player player = game_data.GetPlayer(card.player_id);
                if (!is_ai_predict && iability.target != AbilityTarget.SelectTarget)
                    player.AddHistory(GameAction.CastAbility, card, iability);
                card.RemoveStatus(StatusType.Stealth);
                TriggerCardAbility(iability, card);
                resolve_queue.ResolveAll();
            }
        }

        /// <summary>「起动时」广播（起动式能力发动结算前）。返回 false = 被「阻止本事件」取消本次发动。
        /// 施法卡作为额外宿主，保证其自身图无论所在区域都能响应。</summary>
        private bool EmitActivateBefore(Card caster, AbilityData ability)
        {
            if (caster == null || ability == null || game_data == null)
                return true;
            GraphEventContext ctx = new GraphEventContext();
            ctx.action = ACTIVATE_BEFORE;
            ctx.phase = GraphEventPhase.Before;      //Before 才能被「阻止本事件」/「修改事件值」
            ctx.card = caster;                       //事件主体=发动该能力的卡（英雄技能=英雄卡）
            ctx.player = game_data.GetPlayer(caster.player_id);
            ctx.value = ability.mana_cost;           //事件值=本次灵力费用（只读提示；实际扣费仍按能力定义）
            bool cancelled = EmitGraphEvent(ctx, caster);
            if (cancelled)
                GameLog.Log("[起动触发] " + (caster.CardData != null ? caster.CardData.id : "?")
                    + " 的起动被「阻止本事件」取消（本次不扣灵力、不结算）");
            return !cancelled;
        }

        /// <summary>「起动后」广播（起动式能力结算完成后：灵力已扣、exhausted 已生效、效果已结算）。
        /// 入口若配了「延迟/等待事件」，由 FireEventHost 转为入队延后执行。纯通知，不可阻止。</summary>
        private void EmitActivateAfter(Card caster, AbilityData ability)
        {
            if (caster == null || ability == null || game_data == null || game_data.state == GameState.GameEnded)
                return;
            GraphEventContext ctx = new GraphEventContext();
            ctx.action = ACTIVATE_AFTER;
            ctx.phase = GraphEventPhase.After;
            ctx.card = caster;
            ctx.player = game_data.GetPlayer(caster.player_id);
            ctx.value = ability.mana_cost;
            EmitGraphEvent(ctx, caster);
        }

        //----- 攻击（统一骨架：仆从/英雄/玩家 共用一条链，差异仅命中落点与回调组） -----
        //英雄=卡牌 最小路由：命中 CardType.Hero 的卡时走 DamagePlayer（不发生英雄反击），
        //旧入口 AttackTarget/AttackPlayer 及 Resolve* 链全部保留为转发，回调时点与参数不变。

        //统一攻击入口：按命中对象路由（Card=仆从/英雄卡，Player=玩家），供节点编辑器等统一调用
        public virtual void Attack(Card attacker, object target, bool skip_cost = false)
        {
            if (target is Player tplayer)
            {
                AttackPlayer(attacker, tplayer, skip_cost);
                return;
            }
            if (target is Card tcard)
            {
                AttackTarget(attacker, tcard, skip_cost);
                return;
            }
        }

        //攻击场上卡牌（旧入口保留：转发统一骨架）
        public virtual void AttackTarget(Card attacker, Card target, bool skip_cost = false)
        {
            StartAttack(attacker, target, null, skip_cost, false);
        }

        //攻击玩家（旧入口保留：转发统一骨架）
        public virtual void AttackPlayer(Card attacker, Player target, bool skip_cost = false)
        {
            StartAttack(attacker, null, target, skip_cost, true);
        }

        //统一攻击入口段：合法性/历史/战前能力与秘术（两组触发顺序与旧实现逐条一致）
        private void StartAttack(Card attacker, Card target_card, Player target_player, bool skip_cost, bool vs_player)
        {
            if (attacker == null || (vs_player && target_player == null) || (!vs_player && target_card == null))
                return;

            bool can_attack = vs_player
                ? game_data.CanAttackTarget(attacker, target_player, skip_cost)
                : game_data.CanAttackTarget(attacker, target_card, skip_cost);
            if (!can_attack)
                return;

            Player player = game_data.GetPlayer(attacker.player_id);
            if (!is_ai_predict)
            {
                if (vs_player)
                    player.AddHistory(GameAction.AttackPlayer, attacker, target_player);
                else
                    player.AddHistory(GameAction.Attack, attacker, target_card);
            }

            //旧 AttackPlayer 不写 last_target，保持一致
            if (!vs_player)
                game_data.last_target = target_card.uid;

            //Trigger before attack abilities（打卡：能力先/秘术后；打玩家：秘术先/能力后——与旧实现一致）
            if (vs_player)
            {
                TriggerSecrets(AbilityTrigger.OnBeforeAttack, attacker);
                TriggerCardAbilityType(AbilityTrigger.OnBeforeAttack, attacker, target_player);
            }
            else
            {
                TriggerCardAbilityType(AbilityTrigger.OnBeforeAttack, attacker, target_card);
                TriggerCardAbilityType(AbilityTrigger.OnBeforeDefend, target_card, attacker);
                TriggerSecrets(AbilityTrigger.OnBeforeAttack, attacker);
                TriggerSecrets(AbilityTrigger.OnBeforeDefend, target_card);
            }

            //Resolve attack
            if (vs_player)
                resolve_queue.AddAttack(attacker, target_player, ResolveAttackPlayer, skip_cost);
            else
                resolve_queue.AddAttack(attacker, target_card, ResolveAttack, skip_cost);
            resolve_queue.ResolveAll();
        }

        //攻击结算第一段（旧签名保留：转发统一链；RedirectAttack 仍引用本方法）
        protected virtual void ResolveAttack(Card attacker, Card target, bool skip_cost)
        {
            ResolveAttackStart(attacker, target, null, skip_cost, false);
        }

        protected virtual void ResolveAttackPlayer(Card attacker, Player target, bool skip_cost)
        {
            ResolveAttackStart(attacker, null, target, skip_cost, true);
        }

        //统一攻击结算第一段：隐去/秘术准备，入队命中段
        private void ResolveAttackStart(Card attacker, Card target_card, Player target_player, bool skip_cost, bool vs_player)
        {
            if (!game_data.IsOnBoard(attacker))
                return;
            if (!vs_player && (target_card == null || !game_data.IsOnBoard(target_card)))
                return;

            if (vs_player)
                onAttackPlayerStart?.Invoke(attacker, target_player);
            else
                onAttackStart?.Invoke(attacker, target_card);

            attacker.RemoveStatus(StatusType.Stealth);
            UpdateOngoing();

            if (vs_player)
                resolve_queue.AddAttack(attacker, target_player, ResolveAttackPlayerHit, skip_cost);
            else
                resolve_queue.AddAttack(attacker, target_card, ResolveAttackHit, skip_cost);
            resolve_queue.ResolveAll(0.3f);
        }

        //攻击结算命中段（旧签名保留：转发统一链）
        protected virtual void ResolveAttackHit(Card attacker, Card target, bool skip_cost)
        {
            ResolveAttackHitCore(attacker, target, null, skip_cost, false);
        }

        protected virtual void ResolveAttackPlayerHit(Card attacker, Player target, bool skip_cost)
        {
            ResolveAttackHitCore(attacker, null, target, skip_cost, true);
        }

        //统一命中段：差异仅命中落点（Player 直接 DamagePlayer；Card 为英雄时经最小路由调 DamagePlayer，
        //英雄不参与反击伤害；仆从保留 反击/先攻/护甲/免疫/践踏/吸血/致死 的原伤害链）
        private void ResolveAttackHitCore(Card attacker, Card target_card, Player target_player, bool skip_cost, bool vs_player)
        {
            if (vs_player)
            {
                DamagePlayer(attacker, target_player, attacker.GetAttack());
            }
            else
            {
                if (target_card == null)
                    return;

                //Count attack damage
                int datt1 = attacker.GetAttack();
                int datt2 = target_card.GetAttack();

                //Damage Cards（英雄卡在 DamageCard 入口路由为 DamagePlayer，落回玩家 hp）
                DamageCard(attacker, target_card, datt1);

                //Counter Damage（英雄不反击：英雄命中已在上方路由，走不到这里）
                if (!attacker.HasStatus(StatusType.FirstStrike))
                    DamageCard(target_card, attacker, datt2);
            }

            //Save attack and exhaust
            if (!skip_cost)
                ExhaustBattle(attacker);

            //Recalculate bonus
            UpdateOngoing();

            //Abilities（打玩家：无 OnAfterDefend；秘术仅在攻击者存活时触发——与旧实现一致）
            bool att_board = game_data.IsOnBoard(attacker);
            if (vs_player)
            {
                if (att_board)
                    TriggerCardAbilityType(AbilityTrigger.OnAfterAttack, attacker, target_player);
                TriggerSecrets(AbilityTrigger.OnAfterAttack, attacker);
            }
            else
            {
                bool def_board = game_data.IsOnBoard(target_card);
                if (att_board)
                    TriggerCardAbilityType(AbilityTrigger.OnAfterAttack, attacker, target_card);
                if (def_board)
                    TriggerCardAbilityType(AbilityTrigger.OnAfterDefend, target_card, attacker);
                if (att_board)
                    TriggerSecrets(AbilityTrigger.OnAfterAttack, attacker);
                if (def_board)
                    TriggerSecrets(AbilityTrigger.OnAfterDefend, target_card);
            }

            if (vs_player)
                onAttackPlayerEnd?.Invoke(attacker, target_player);
            else
                onAttackEnd?.Invoke(attacker, target_card);

            RefreshData();
            CheckForWinner();

            resolve_queue.ResolveAll(0.2f);
        }

        //Exhaust after battle
        public virtual void ExhaustBattle(Card attacker)
        {
            bool attacked_before = game_data.cards_attacked.Contains(attacker.uid);
            game_data.cards_attacked.Add(attacker.uid);
            bool attack_again = attacker.HasStatus(StatusType.Fury) && !attacked_before;
            attacker.exhausted = !attack_again;
        }

        //Redirect attack to a new target
        public virtual void RedirectAttack(Card attacker, Card new_target)
        {
            foreach (AttackQueueElement att in resolve_queue.GetAttackQueue())
            {
                if (att.attacker.uid == attacker.uid)
                {
                    att.target = new_target;
                    att.ptarget = null;
                    att.callback = ResolveAttack;
                    att.pcallback = null;
                }
            }
        }

        public virtual void RedirectAttack(Card attacker, Player new_target)
        {
            foreach (AttackQueueElement att in resolve_queue.GetAttackQueue())
            {
                if (att.attacker.uid == attacker.uid)
                {
                    att.ptarget = new_target;
                    att.target = null;
                    att.pcallback = ResolveAttackPlayer;
                    att.callback = null;
                }
            }
        }

        public virtual void ShuffleDeck(List<Card> cards)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                Card temp = cards[i];
                int randomIndex = random.Next(i, cards.Count);
                cards[i] = cards[randomIndex];
                cards[randomIndex] = temp;
            }
        }

        public virtual void DrawCard(Player player, int nb = 1)
        {
            for (int i = 0; i < nb; i++)
            {
                if (player.cards_deck.Count > 0 && player.cards_hand.Count < GameplayData.Get().cards_max)
                {
                    Card card = player.cards_deck[0];
                    player.cards_deck.RemoveAt(0);
                    player.cards_hand.Add(card);

                    //图事件「抽卡后」（全场监听；主体=抽到的卡）
                    {
                        GraphEventContext ectx = new GraphEventContext();
                        ectx.action = "OnAfterDraw";
                        ectx.phase = GraphEventPhase.After;
                        ectx.card = card;
                        ectx.player = player;
                        ectx.value = 1;
                        EmitGraphEvent(ectx);
                    }

                    TriggerPlayerCardsAbilityType(player, AbilityTrigger.OnDraw);
                    UpdateOngoingCards(); 
                }
            }

            onCardDrawn?.Invoke(nb);
        }
        //Put a card from deck into discard
        public virtual void DrawDiscardCard(Player player, int nb = 1)
        {
            for (int i = 0; i < nb; i++)
            {
                if (player.cards_deck.Count > 0)
                {
                    Card card = player.cards_deck[0];
                    player.cards_deck.RemoveAt(0);
                    player.cards_discard.Add(card);
                }
            }
        }

        //Summon copy of an exiting card
        public virtual Card SummonCopy(Player player, Card copy, Slot slot)
        {
            CardData icard = copy.CardData;
            return SummonCard(player, icard, copy.VariantData, slot);
        }

        //Summon copy of an exiting card into hand
        public virtual Card SummonCopyHand(Player player, Card copy)
        {
            CardData icard = copy.CardData;
            return SummonCardHand(player, icard, copy.VariantData);
        }

        //Create a new card and send it to the board
        public virtual Card SummonCard(Player player, CardData card, VariantData variant, Slot slot)
        {
            if (!slot.IsValid())
                return null;

            if (game_data.GetSlotCard(slot) != null)
                return null;

            Card acard = SummonCardHand(player, card, variant);
            PlayCard(acard, slot, true);

            onCardSummoned?.Invoke(acard, slot);

            return acard;
        }

        //Create a new card and send it to your hand
        public virtual Card SummonCardHand(Player player, CardData card, VariantData variant)
        {
            Card acard = Card.Create(card, variant, player);
            player.cards_hand.Add(acard);
            game_data.last_summoned = acard.uid;
            return acard;
        }

        //shuffle a new card to deck

        public virtual Card AddCardDeck(Player player, CardData card, VariantData variant)
        {
            Card acard = Card.Create(card, variant, player);
            player.cards_deck.Add(acard);
            game_data.last_Added_To_Deck = acard.uid;
            return acard;
        }

        //Transform card into another one
        public virtual Card TransformCard(Card card, CardData transform_to)
        {
            //图事件「变形时」：全场监听；可阻止本次变形
            if (card != null)
            {
                GraphEventContext tctx = new GraphEventContext();
                tctx.action = "OnBeforeTransform";
                tctx.phase = GraphEventPhase.Before;
                tctx.card = card;
                tctx.source_card = null;
                tctx.player = game_data.GetPlayer(card.player_id);
                tctx.value = 0;
                if (EmitGraphEvent(tctx))
                    return card;    //被阻止：保持原卡不变
            }

            card.SetCard(transform_to, card.VariantData);

            onCardTransformed?.Invoke(card);

            //图事件「变形后」（主体=变形后的卡）
            if (card != null)
            {
                GraphEventContext actx = new GraphEventContext();
                actx.action = "OnAfterTransform";
                actx.phase = GraphEventPhase.After;
                actx.card = card;
                actx.player = game_data.GetPlayer(card.player_id);
                actx.value = 0;
                EmitGraphEvent(actx);
            }

            return card;
        }

        public virtual void EquipCard(Card card, Card equipment)
        {
            if (card == null || equipment == null || card.player_id != equipment.player_id)
                return;
            if (card.CardData.IsEquipment() || !equipment.CardData.IsEquipment())
                return;

            //图事件「装备道具时」：全场监听；可阻止=装备不上（卡留在原位）
            {
                GraphEventContext ectx = new GraphEventContext();
                ectx.action = "OnBeforeEquip";
                ectx.phase = GraphEventPhase.Before;
                ectx.card = card;               //佩戴者（英雄/随从）
                ectx.source_card = equipment;   //被装上的装备
                ectx.player = game_data.GetPlayer(card.player_id);
                ectx.value = 0;
                if (EmitGraphEvent(ectx))
                    return;
            }

            UnequipAll(card); //Unequip previous cards, only 1 equip at a time

            Player player = game_data.GetPlayer(card.player_id);
            player.RemoveCardFromAllGroups(equipment);
            player.cards_equip.Add(equipment);
            card.equipped_uid = equipment.uid;
            equipment.slot = card.slot;

            //图事件「装备道具后」（主体=佩戴者，来源=装备）
            {
                GraphEventContext actx = new GraphEventContext();
                actx.action = "OnAfterEquip";
                actx.phase = GraphEventPhase.After;
                actx.card = card;
                actx.source_card = equipment;
                actx.player = game_data.GetPlayer(card.player_id);
                actx.value = 0;
                EmitGraphEvent(actx);
            }
        }

        public virtual void UnequipAll(Card card)
        {
            if (card != null && card.equipped_uid != null)
            {
                Player player = game_data.GetPlayer(card.player_id);
                Card equip = player.GetEquipCard(card.equipped_uid);
                if (equip != null)
                {
                    card.equipped_uid = null;
                    DiscardCard(equip);
                }
            }
        }

        //Change owner of a card
        public virtual void ChangeOwner(Card card, Player owner)
        {
            if (card.player_id != owner.player_id)
            {
                Player powner = game_data.GetPlayer(card.player_id);
                powner.RemoveCardFromAllGroups(card);
                powner.cards_all.Remove(card.uid);
                owner.cards_all[card.uid] = card;
                card.player_id = owner.player_id;
            }
        }

        //统一伤害入口：按命中对象路由（Card=仆从/英雄卡，Player=玩家）。
        //英雄=卡牌 最小路由：CardType.Hero 的卡在 DamageCard 入口转调 DamagePlayer，结算落回玩家 hp，
        //节点编辑器（NodeDoc/规则图）只需调用本入口即可同时命中 仆从/英雄/玩家。
        public virtual void DealDamage(Card source, object target, int value, bool spell_damage = false)
        {
            if (target is Player tplayer)
            {
                DamagePlayer(source, tplayer, value);
                return;
            }
            if (target is Card tcard)
            {
                DamageCard(source, tcard, value, spell_damage);
                return;
            }
        }

        //Damage a player
        public virtual void DamagePlayer(Card attacker, Player target, int value)
        {
            //图事件「伤害时」（对玩家伤害：攻击玩家/法术打玩家）；全场监听，可阻止=本次伤害取消、改值=改写伤害量
            if (value > 0)
            {
                GraphEventContext dctx = new GraphEventContext();
                dctx.action = "OnBeforeDamage";
                dctx.phase = GraphEventPhase.Before;
                dctx.card = null;
                dctx.source_card = attacker;
                dctx.player = target;
                dctx.value = value;
                if (EmitGraphEvent(dctx))
                    return;         //被阻止：本次伤害不结算
                value = Mathf.Max(dctx.value, 0);
            }

            //Damage player
            target.hp -= value;
            target.hp = Mathf.Clamp(target.hp, 0, target.hp_max);

            //Lifesteal（attacker 可为 null：无来源伤害经英雄路由落到这里）
            if (attacker != null && attacker.HasStatus(StatusType.LifeSteal))
            {
                Player aplayer = game_data.GetPlayer(attacker.player_id);
                aplayer.hp += value;
            }

            onPlayerDamaged?.Invoke(target, value);

            //图事件「伤害后」（对玩家伤害；主体=玩家 player、来源=attacker）
            if (value > 0)
            {
                GraphEventContext actx = new GraphEventContext();
                actx.action = "OnAfterDamage";
                actx.phase = GraphEventPhase.After;
                actx.card = null;
                actx.source_card = attacker;
                actx.player = target;
                actx.value = value;
                EmitGraphEvent(actx);
            }
        }

        //统一治疗入口：按命中对象路由（Card=仆从/英雄卡，Player=玩家）。
        //英雄=卡牌 最小路由：CardType.Hero 的卡在 HealCard 入口转调 HealPlayer，治疗落回玩家 hp。
        public virtual void Heal(object source, object target, int value)
        {
            if (target is Player tplayer)
            {
                HealPlayer(tplayer, value);
                return;
            }
            if (target is Card tcard)
            {
                HealCard(tcard, value);
                return;
            }
        }

        //Heal a card
        public virtual void HealCard(Card target, int value)
        {
            if (target == null)
                return;

            //英雄=卡牌 最小路由：英雄卡治疗落回玩家 hp（治疗量 clamp 由 HealPlayer 内部处理，行为不变）
            if (IsHeroCard(target))
            {
                Player hero_player = game_data.GetPlayer(target.player_id);
                if (hero_player != null)
                    HealPlayer(hero_player, value);
                return;
            }

            if (target.HasStatus(StatusType.Invincibility))
                return;

            //图事件「治疗时」：全场监听；可阻止本次治疗 / 修改治疗量
            if (value > 0)
            {
                GraphEventContext hctx = new GraphEventContext();
                hctx.action = "OnBeforeHeal";
                hctx.phase = GraphEventPhase.Before;
                hctx.card = target;
                hctx.source_card = null;
                hctx.player = game_data.GetPlayer(target.player_id);
                hctx.value = value;
                if (EmitGraphEvent(hctx))
                    return;
                value = Mathf.Max(hctx.value, 0);
            }

            target.damage -= value;
            target.damage = Mathf.Max(target.damage, 0);

            onCardHealed?.Invoke(target, value);

            //图事件「治疗后」（主体=被治疗的卡）
            if (value > 0)
            {
                GraphEventContext actx = new GraphEventContext();
                actx.action = "OnAfterHeal";
                actx.phase = GraphEventPhase.After;
                actx.card = target;
                actx.player = game_data.GetPlayer(target.player_id);
                actx.value = value;
                EmitGraphEvent(actx);
            }
        }

        public virtual void HealPlayer(Player target, int value)
        {
            if (target == null)
                return;

            //图事件「治疗时」（对玩家治疗）；可阻止/改治疗量
            if (value > 0)
            {
                GraphEventContext hctx = new GraphEventContext();
                hctx.action = "OnBeforeHeal";
                hctx.phase = GraphEventPhase.Before;
                hctx.card = null;
                hctx.source_card = null;
                hctx.player = target;
                hctx.value = value;
                if (EmitGraphEvent(hctx))
                    return;
                value = Mathf.Max(hctx.value, 0);
            }

            target.hp += value;
            target.hp = Mathf.Clamp(target.hp, 0, target.hp_max);

            onPlayerHealed?.Invoke(target, value);

            //图事件「治疗后」（对玩家治疗；主体=玩家）
            if (value > 0)
            {
                GraphEventContext actx = new GraphEventContext();
                actx.action = "OnAfterHeal";
                actx.phase = GraphEventPhase.After;
                actx.card = null;
                actx.player = target;
                actx.value = value;
                EmitGraphEvent(actx);
            }
        }

        //英雄=卡牌：CardType.Hero 的卡（存于 Player.hero，不占棋盘格）
        private bool IsHeroCard(Card card)
        {
            return card != null && card.CardData != null && card.CardData.type == CardType.Hero;
        }

        //Generic damage that doesnt come from another card
        public virtual void DamageCard(Card target, int value)
        {
            if (target == null)
                return;

            //英雄=卡牌 最小路由：英雄卡伤害落回玩家 hp（无来源，attacker 传 null）
            if (IsHeroCard(target))
            {
                Player hero_player = game_data.GetPlayer(target.player_id);
                if (hero_player != null)
                    DamagePlayer(null, hero_player, value);
                return;
            }

            if (target.HasStatus(StatusType.Invincibility))
                return; //Invincible

            if (target.HasStatus(StatusType.SpellImmunity))
                return; //Spell immunity

            target.damage += value;

            onCardDamaged?.Invoke(target, value);

            //图事件「伤害后」（无来源伤害：毒/燃烧等；主体=受伤卡）
            if (value > 0)
            {
                GraphEventContext actx = new GraphEventContext();
                actx.action = "OnAfterDamage";
                actx.phase = GraphEventPhase.After;
                actx.card = target;
                actx.player = game_data.GetPlayer(target.player_id);
                actx.value = value;
                EmitGraphEvent(actx);
            }

            if (target.GetHP() <= 0)
                DiscardCard(target, CardDiscardReason.Death);   //无来源致死（毒/燃烧等）按死亡处理
        }

        //Damage a card with attacker/caster
        public virtual void DamageCard(Card attacker, Card target, int value, bool spell_damage = false)
        {
            if (attacker == null || target == null)
                return;

            //英雄=卡牌 最小路由：命中 CardType.Hero 的卡时转调玩家版，伤害落回玩家 hp
            //（否则 damage 累加在英雄卡对象上不可见、且英雄不在 board 上永远不会被结算死亡——"打英雄有的行有的不行"的根因）
            if (IsHeroCard(target))
            {
                Player hero_player = game_data.GetPlayer(target.player_id);
                if (hero_player != null)
                    DamagePlayer(attacker, hero_player, value);
                return;
            }

            if (target.HasStatus(StatusType.Invincibility))
                return; //Invincible

            if (target.HasStatus(StatusType.SpellImmunity) && attacker.CardData.type != CardType.Character)
                return; //Spell immunity

            //Shell
            bool doublelife = target.HasStatus(StatusType.Shell);
            if (doublelife && value > 0)
            {
                target.RemoveStatus(StatusType.Shell);
                return;
            }

            //Immunity
            if (!spell_damage && target.HasStatus(StatusType.Immunity))
                value = 0;

            //Armor
            if (!spell_damage && target.HasStatus(StatusType.Armor))
                value = Mathf.Max(value - target.GetStatusValue(StatusType.Armor), 0);



            //图事件「伤害时」（全场监听；可阻止=取消本次伤害；改值=改写实际伤害量，随后按新值结算）
            if (value > 0)
            {
                GraphEventContext dctx = new GraphEventContext();
                dctx.action = "OnBeforeDamage";
                dctx.phase = GraphEventPhase.Before;
                dctx.card = target;
                dctx.source_card = attacker;
                dctx.player = game_data.GetPlayer(target.player_id);
                dctx.value = value;
                if (EmitGraphEvent(dctx))
                    return;         //被阻止：本次伤害不结算
                value = Mathf.Max(dctx.value, 0);
                //图上改写（208005 更改受伤卡牌 / 208006 更改伤害源）：按新目标、新来源结算（未改写则保持原值）
                if (dctx.card != null && dctx.card != target)
                    target = dctx.card;
                if (dctx.source_card != null && dctx.source_card != attacker)
                    attacker = dctx.source_card;
            }

            //Damage
            int damage_max = Mathf.Min(value, target.GetHP());
            int extra = value - target.GetHP();
            target.damage += value;

            //Trample
            Player tplayer = game_data.GetPlayer(target.player_id);
            if (!spell_damage && extra > 0 && attacker.player_id == game_data.current_player && attacker.HasStatus(StatusType.Trample))
                tplayer.hp -= extra;

            //Lifesteal
            Player player = game_data.GetPlayer(attacker.player_id);
            if (!spell_damage && attacker.HasStatus(StatusType.LifeSteal))
                player.hp += damage_max;

            //Remove sleep on damage
            target.RemoveStatus(StatusType.Sleep);

            //Callback
            onCardDamaged?.Invoke(target, value);

            //图事件「伤害后」（全场监听；主体=受伤卡、来源=伤害来源、数值=实际伤害）
            if (value > 0)
            {
                GraphEventContext actx = new GraphEventContext();
                actx.action = "OnAfterDamage";
                actx.phase = GraphEventPhase.After;
                actx.card = target;
                actx.source_card = attacker;
                actx.player = game_data.GetPlayer(target.player_id);
                actx.value = value;
                EmitGraphEvent(actx);
            }

            //Deathtouch
            if (value > 0 && attacker.HasStatus(StatusType.Deathtouch) && target.CardData.type == CardType.Character)
                KillCard(attacker, target);

            //Kill card if no hp
            if (target.CardData.type  != CardType.Character  && target.GetHP() <= 0)
                KillCard(attacker, target);
        }

        //A card that kills another card
        public virtual void KillCard(Card attacker, Card target)
        {
            if (target == null)
                return;

            if (!game_data.IsOnBoard(target) && !game_data.IsEquipped(target))
                return; //Already killed

            if (target.HasStatus(StatusType.Invincibility))
                return; //Cant be killed

            //attacker 为 null = 规则致死（EndTurn 结算 Doomed / Freezing 就是这么调的）：不计击杀数
            if (attacker != null)
            {
                Player pattacker = game_data.GetPlayer(attacker.player_id);
                if (pattacker != null && attacker.player_id != target.player_id)
                    pattacker.kill_count++;
            }

            DiscardCard(target, CardDiscardReason.Death);

            //OnKill 只在确有击杀者时触发：TriggerCardAbilityType 第一步就是 caster.GetAbilities()，
            //传 null 会直接空引用（原先那句 attacker==null 早退其实是在掩盖这里）
            if (attacker != null)
                TriggerCardAbilityType(AbilityTrigger.OnKill, attacker, target);
        }

        //Send card into discard（reason 区分 死亡/弃置，用于图事件 弃牌时/后、死亡时/后）
        public enum CardDiscardReason { Discard = 0, Death = 1 }

        public virtual void DiscardCard(Card card)
        {
            DiscardCard(card, CardDiscardReason.Discard);
        }

        public virtual void DiscardCard(Card card, CardDiscardReason reason)
        {
            if (card == null)
                return;

            if (game_data.IsInDiscard(card))
                return; //Already discarded

            CardData icard = card.CardData;
            Player player = game_data.GetPlayer(card.player_id);
            bool was_on_board = game_data.IsOnBoard(card) || game_data.IsEquipped(card);
            bool is_death = reason == CardDiscardReason.Death;

            //图事件「时」（死亡时/弃牌时）：全场监听；可阻止=本次不进墓地（卡保持原位）。卡自身作为额外宿主，任意牌堆都能响应。
            GraphEventContext pre = new GraphEventContext();
            pre.action = is_death ? "OnBeforeDeath" : "OnBeforeDiscard";
            pre.phase = GraphEventPhase.Before;
            pre.card = card;
            pre.source_card = null;
            pre.player = player;
            pre.value = 0;
            if (EmitGraphEvent(pre, card))
                return;

            //Unequip card
            UnequipAll(card);

            //Remove card from board and add to discard
            player.RemoveCardFromAllGroups(card);

            if(!card.HasStatus(StatusType.Disappear))
                player.cards_discard.Add(card);
            game_data.last_destroyed = card.uid;

            //Remove from bearer
            Card bearer = player.GetBearerCard(card);
            if (bearer != null)
                bearer.equipped_uid = null;

            if (was_on_board)
            {
                //Trigger on death abilities（引擎亡语语义不变）
                TriggerCardAbilityType(AbilityTrigger.OnDeath, card);
                TriggerOtherCardsAbilityType(AbilityTrigger.OnDeathOther, card);
                TriggerSecrets(AbilityTrigger.OnDeathOther, card);
                UpdateOngoingCards(); //Not UpdateOngoing() here to avoid recursive calls in UpdateOngoingKills
            }

            cards_to_clear.Add(card); //Will be Clear() in the next UpdateOngoing, so that simultaneous damage effects work
            onCardDiscarded?.Invoke(card);

            //图事件「后」（死亡后/弃牌后；主体=进墓地的卡，并作为额外宿主使其自身图能响应）
            GraphEventContext actx = new GraphEventContext();
            actx.action = is_death ? "OnAfterDeath" : "OnAfterDiscard";
            actx.phase = GraphEventPhase.After;
            actx.card = card;
            actx.source_card = null;
            actx.player = player;
            actx.value = 0;
            EmitGraphEvent(actx, card);
        }

        public int RollRandomValue(int dice)
        {
            return RollRandomValue(1, dice + 1);
        }

        public virtual int RollRandomValue(int min, int max)
        {
            game_data.rolled_value = random.Next(min, max);
            onRollValue?.Invoke(game_data.rolled_value);
            resolve_queue.SetDelay(1f);
            return game_data.rolled_value;
        }

        //--- Abilities --

        public virtual void TriggerCardAbilityType(AbilityTrigger type, Card caster, Card triggerer = null)
        {
            foreach (AbilityData iability in caster.GetAbilities())
            {
                if (iability && iability.trigger == type)
                {
                    TriggerCardAbility(iability, caster, triggerer);
                }
            }

            TriggerCardKeywords(type, caster, triggerer, null);

            Card equipped = game_data.GetEquipCard(caster.equipped_uid);
            if (equipped != null)
                TriggerCardAbilityType(type, equipped, triggerer);
        }

        public virtual void TriggerCardAbilityType(AbilityTrigger type, Card caster, Player triggerer)
        {
            foreach (AbilityData iability in caster.GetAbilities())
            {
                if (iability && iability.trigger == type)
                {
                    TriggerCardAbility(iability, caster, triggerer);
                }
            }

            TriggerCardKeywords(type, caster, null, triggerer);

            Card equipped = game_data.GetEquipCard(caster.equipped_uid);
            if (equipped != null)
                TriggerCardAbilityType(type, equipped, triggerer);
        }

        /// <summary>
        /// 自定义关键词（带规则图的 KeywordData）触发旁路：
        /// 遍历卡牌拥有的关键词，按触发时机执行其规则图。
        /// 与能力不同，关键词规则不走 resolve_queue，直接同步执行（NodeDocRunner 本身就是同步的）。
        /// </summary>
        public virtual void TriggerCardKeywords(AbilityTrigger type, Card caster, Card triggerer, Player triggerer_player)
        {
            if (caster == null || caster.HasStatus(StatusType.Silenced))
                return;

            string action = type.ToString();

            //目标解析：攻击/受伤类事件用 triggerer；打出类事件从 caster.slot 解析玩家选中的 PlayTarget（同 ResolveCardAbilityPlayTarget）
            Card target_card = (triggerer != null && triggerer != caster) ? triggerer : null;
            Player target_player = triggerer_player;
            if (target_card == null && target_player == null && type == AbilityTrigger.OnPlay)
            {
                Slot slot = caster.slot;
                if (slot.IsValid())
                {
                    Card slot_card = game_data.GetSlotCard(slot);
                    if (slot_card != null)
                        target_card = slot_card;
                    else if (slot.IsPlayerSlot())
                        target_player = game_data.GetPlayer(slot.p);
                }
            }

            foreach (string keyword_id in caster.keywords)
            {
                KeywordData kdata = KeywordData.Get(keyword_id);
                if (kdata == null || !kdata.HasRules)
                    continue;
                KeywordRule rule = kdata.GetRule(action);
                if (rule == null)
                    continue;

                Workshop.NodeDocRunner.Run(this, rule.graph, caster, target_card, target_player, action);
            }
        }

        public virtual void TriggerOtherCardsAbilityType(AbilityTrigger type, Card triggerer)
        {
            foreach (Player oplayer in game_data.players)
            {
                if (oplayer.hero != null)
                    TriggerCardAbilityType(type, oplayer.hero, triggerer);

                foreach (Card card in oplayer.cards_board)
                    TriggerCardAbilityType(type, card, triggerer);
            }
        }

        public virtual void TriggerPlayerCardsAbilityType(Player player, AbilityTrigger type)
        {
            if (player.hero != null)
                TriggerCardAbilityType(type, player.hero, player.hero);

            foreach (Card card in player.cards_board)
                TriggerCardAbilityType(type, card, card);
        }

        public virtual void TriggerCardAbility(AbilityData iability, Card caster)
        {
            TriggerCardAbility(iability, caster, caster);
        }

        public virtual void TriggerCardAbility(AbilityData iability, Card caster, Card triggerer)
        {
            Card trigger_card = triggerer != null ? triggerer : caster; //Triggerer is the caster if not set
            if (!caster.HasStatus(StatusType.Silenced) && iability.AreTriggerConditionsMet(game_data, caster, trigger_card))
            {
                resolve_queue.AddAbility(iability, caster, trigger_card, ResolveCardAbility);
            }
        }

        public virtual void TriggerCardAbility(AbilityData iability, Card caster, Player triggerer)
        {
            if (!caster.HasStatus(StatusType.Silenced) && iability.AreTriggerConditionsMet(game_data, caster, triggerer))
            {
                resolve_queue.AddAbility(iability, caster, caster, ResolveCardAbility);
            }
        }

        public virtual void TriggerAbilityDelayed(AbilityData iability, Card caster)
        {
            resolve_queue.AddAbility(iability, caster, caster, TriggerCardAbility);
        }

        public virtual void TriggerAbilityDelayed(AbilityData iability, Card caster, Card triggerer)
        {
            Card trigger_card = triggerer != null ? triggerer : caster; //Triggerer is the caster if not set
            resolve_queue.AddAbility(iability, caster, trigger_card, TriggerCardAbility);
        }

        //Resolve a card ability, may stop to ask for target
        protected virtual void ResolveCardAbility(AbilityData iability, Card caster, Card triggerer)
        {
            if (!caster.CanDoAbilities())
                return; //Silenced card cant cast

            //Debug.Log("Trigger Ability " + iability.id + " : " + caster.card_id);

            onAbilityStart?.Invoke(iability, caster);
            game_data.ability_triggerer = triggerer.uid;
            game_data.ability_played.Add(iability.id);

            bool is_selector = ResolveCardAbilitySelector(iability, caster);
            if (is_selector)
                return; //Wait for player to select

            ResolveCardAbilityPlayTarget(iability, caster);
            ResolveCardAbilityPlayers(iability, caster);
            ResolveCardAbilityCards(iability, caster);
            ResolveCardAbilitySlots(iability, caster);
            ResolveCardAbilityCardData(iability, caster);
            ResolveCardAbilityNoTarget(iability, caster);
            AfterAbilityResolved(iability, caster);
        }

        protected virtual bool ResolveCardAbilitySelector(AbilityData iability, Card caster)
        {
            if (iability.target == AbilityTarget.SelectTarget)
            {
                //Wait for target
                GoToSelectTarget(iability, caster);
                return true;
            }
            else if (iability.target == AbilityTarget.CardSelector)
            {
                GoToSelectorCard(iability, caster);
                return true;
            }
            else if (iability.target == AbilityTarget.ChoiceSelector)
            {
                GoToSelectorChoice(iability, caster);
                return true;
            }
            return false;
        }

        protected virtual void ResolveCardAbilityPlayTarget(AbilityData iability, Card caster)
        {
            GameLog.Log($"ResolveCardAbilityPlayTarget called: iability={iability?.id}, caster={caster?.CardData.id}, target={iability?.target}, multi={iability?.multi_target}");
            
            if (iability.target == AbilityTarget.PlayTarget)
            {
                Slot slot = caster.slot;
                GameLog.Log($"ResolveCardAbilityPlayTarget: slot={slot}, IsPlayerSlot={slot.IsPlayerSlot()}");
                
                Card slot_card = game_data.GetSlotCard(slot);
                if (slot.IsPlayerSlot())
                {
                    Player tplayer = game_data.GetPlayer(slot.p);
                    GameLog.Log($"ResolveCardAbilityPlayTarget: tplayer={tplayer?.player_id}");
                    if (iability.CanTarget(game_data, caster, tplayer))
                    {
                        GameLog.Log($"ResolveCardAbilityPlayTarget: calling ResolveEffectTarget for player {tplayer.player_id}");
                        ResolveEffectTarget(iability, caster, tplayer);
                    }
                    else
                    {
                        GameLog.Log($"ResolveCardAbilityPlayTarget: CanTarget returned false for player {tplayer?.player_id}");
                    }
                }
                else if (slot_card != null)
                {
                    if (iability.CanTarget(game_data, caster, slot_card))
                    {
                        game_data.last_target = slot_card.uid;
                        ResolveEffectTarget(iability, caster, slot_card);
                    }
                }
                else
                {
                    if (iability.CanTarget(game_data, caster, slot))
                        ResolveEffectTarget(iability, caster, slot);
                }
            }
        }

        protected virtual void ResolveCardAbilityPlayers(AbilityData iability, Card caster)
        {
            //Get Player Targets based on conditions
            List<Player> targets = iability.GetPlayerTargets(game_data, caster, player_array);

            //Resolve effects
            foreach (Player target in targets)
            {
                ResolveEffectTarget(iability, caster, target);
            }
        }

        protected virtual void ResolveCardAbilityCards(AbilityData iability, Card caster)
        {
            //Get Cards Targets based on conditions
            List<Card> targets = iability.GetCardTargets(game_data, caster, card_array);

            //Resolve effects
            foreach (Card target in targets)
            {
                ResolveEffectTarget(iability, caster, target);
            }
        }

        protected virtual void ResolveCardAbilitySlots(AbilityData iability, Card caster)
        {
            //Get Slot Targets based on conditions
            List<Slot> targets = iability.GetSlotTargets(game_data, caster, slot_array);

            //Resolve effects
            foreach (Slot target in targets)
            {
                ResolveEffectTarget(iability, caster, target);
            }
        }

        protected virtual void ResolveCardAbilityCardData(AbilityData iability, Card caster)
        {
            //Get Cards Targets based on conditions
            List<CardData> targets = iability.GetCardDataTargets(game_data, caster, card_data_array);

            //Resolve effects
            foreach (CardData target in targets)
            {
                ResolveEffectTarget(iability, caster, target);
            }
        }

        protected virtual void ResolveCardAbilityNoTarget(AbilityData iability, Card caster)
        {
            if (iability.target == AbilityTarget.None)
                iability.DoEffects(this, caster);
        }

        protected virtual void ResolveEffectTarget(AbilityData iability, Card caster, Player target)
        {
            GameLog.Log($"ResolveEffectTarget called: iability={iability?.id}, caster={caster?.CardData.id}, target_player={target?.player_id}");
            iability.DoEffects(this, caster, target);
            GameLog.Log($"ResolveEffectTarget: DoEffects completed");

            onAbilityTargetPlayer?.Invoke(iability, caster, target);
        }

        protected virtual void ResolveEffectTarget(AbilityData iability, Card caster, Card target)
        {
            iability.DoEffects(this, caster, target);

            onAbilityTargetCard?.Invoke(iability, caster, target);
        }

        protected virtual void ResolveEffectTarget(AbilityData iability, Card caster, Slot target)
        {
            iability.DoEffects(this, caster, target);

            onAbilityTargetSlot?.Invoke(iability, caster, target);
        }

        protected virtual void ResolveEffectTarget(AbilityData iability, Card caster, CardData target)
        {
            iability.DoEffects(this, caster, target);
        }

        protected virtual void AfterAbilityResolved(AbilityData iability, Card caster)
        {
            Player player = game_data.GetPlayer(caster.player_id);

            //Pay cost
            bool is_activate = iability.trigger == AbilityTrigger.Activate;
            if (is_activate || iability.trigger == AbilityTrigger.None)
            {
                player.mana -= iability.mana_cost;
                caster.exhausted = caster.exhausted || iability.exhaust;
            }

            //图事件「起动后」（仅起动式能力）：费用已扣、效果已结算之后广播
            if (is_activate)
                EmitActivateAfter(caster, iability);

            //Recalculate and clear
            UpdateOngoing();
            CheckForWinner();

            //Chain ability
            if (iability.target != AbilityTarget.ChoiceSelector && game_data.state != GameState.GameEnded)
            {
                foreach (AbilityData chain_ability in iability.chain_abilities)
                {
                    if (chain_ability != null)
                    {
                        TriggerCardAbility(chain_ability, caster);
                    }
                }
            }

            onAbilityEnd?.Invoke(iability, caster);
            resolve_queue.ResolveAll(0.5f);
            RefreshData();
        }

        //This function is called often to update status/stats affected by ongoing abilities
        //It basically first reset the bonus to 0 (CleanOngoing) and then recalculate it to make sure it it still present
        //Only cards in hand and on board are updated in this way
        public virtual void UpdateOngoing()
        {
            Profiler.BeginSample("Update Ongoing");
            UpdateOngoingCards(); //Update status and stats
            UpdateOngoingKills(); //Kill cards with 0 HP
            Profiler.EndSample();
        }

        protected virtual void UpdateOngoingCards()
        {
            for (int p = 0; p < game_data.players.Length; p++)
            {
                Player player = game_data.players[p];
                player.ClearOngoing();

                for (int c = 0; c < player.cards_board.Count; c++)
                    player.cards_board[c].ClearOngoing();

                for (int c = 0; c < player.cards_equip.Count; c++)
                    player.cards_equip[c].ClearOngoing();

                for (int c = 0; c < player.cards_hand.Count; c++)
                    player.cards_hand[c].ClearOngoing();
            }

            //Iterate multiple times to resolve dependencies between ongoing effects
            //For example: Card A gives flying if another flying card exists, Card B has flying
            //Need multiple passes so that Card B gets flying first, then Card A can detect it
            for (int iteration = 0; iteration < 5; iteration++)
            {
                bool changed = false;

                //Step 1: First pass - add self-targeting ongoing effects (like Flying)
                for (int p = 0; p < game_data.players.Length; p++)
                {
                    Player player = game_data.players[p];
                    
                    int heroCount = player.hero != null ? player.hero.ongoing_status.Count : 0;
                    UpdateOngoingSelfEffects(player, player.hero);
                    if (player.hero != null && player.hero.ongoing_status.Count != heroCount)
                        changed = true;

                    for (int c = 0; c < player.cards_board.Count; c++)
                    {
                        Card card = player.cards_board[c];
                        int count = card.ongoing_status.Count;
                        UpdateOngoingSelfEffects(player, card);
                        if (card.ongoing_status.Count != count)
                            changed = true;
                    }

                    for (int c = 0; c < player.cards_equip.Count; c++)
                    {
                        Card card = player.cards_equip[c];
                        int count = card.ongoing_status.Count;
                        UpdateOngoingSelfEffects(player, card);
                        if (card.ongoing_status.Count != count)
                            changed = true;
                    }
                }

                //If no changes, we can stop early
                if (!changed)
                    break;
            }

            //Step 2: Second pass - add aura effects (like Flying Aura)
            for (int p = 0; p < game_data.players.Length; p++)
            {
                Player player = game_data.players[p];
                UpdateOngoingAuraEffects(player, player.hero);

                for (int c = 0; c < player.cards_board.Count; c++)
                {
                    Card card = player.cards_board[c];
                    UpdateOngoingAuraEffects(player, card);
                }

                for (int c = 0; c < player.cards_equip.Count; c++)
                {
                    Card card = player.cards_equip[c];
                    UpdateOngoingAuraEffects(player, card);
                }
            }

            //Stats bonus
            for (int p = 0; p < game_data.players.Length; p++)
            {
                Player player = game_data.players[p];
                for (int c = 0; c < player.cards_board.Count; c++)
                {
                    Card card = player.cards_board[c];

                    //Taunt effect
                    if (card.HasStatus(StatusType.Protection) && !card.HasStatus(StatusType.Stealth))
                    {
                        player.AddOngoingStatus(StatusType.Protected, 0);

                        for (int tc = 0; tc < player.cards_board.Count; tc++)
                        {
                            Card tcard = player.cards_board[tc];
                            if (!tcard.HasStatus(StatusType.Protection) && !tcard.HasStatus(StatusType.Protected))
                            {
                                tcard.AddOngoingStatus(StatusType.Protected, 0);
                            }
                        }
                    }

                    //Haste
                    //if (card.HasStatus(StatusType.Haste) && card.) 
                    //{
                    //    player.has
                    //}
                    //
                    //Status bonus
                    foreach (CardStatus status in card.status)
                        AddOngoingStatusBonus(card, status);
                    foreach (CardStatus status in card.ongoing_status)
                        AddOngoingStatusBonus(card, status);
                }

                for (int c = 0; c < player.cards_hand.Count; c++)
                {
                    Card card = player.cards_hand[c];
                    //Status bonus
                    foreach (CardStatus status in card.status)
                        AddOngoingStatusBonus(card, status);
                    foreach (CardStatus status in card.ongoing_status)
                        AddOngoingStatusBonus(card, status);
                }
            }

            //Equipment bonus
            for (int p = 0; p < game_data.players.Length; p++)
            {
                Player player = game_data.players[p];
                for (int c = 0; c < player.cards_board.Count; c++)
                {
                    Card card = player.cards_board[c];
                    if (card.equipped_uid != null)
                    {
                        Card equip = game_data.GetCard(card.equipped_uid);
                        if (equip != null && equip.CardData.IsEquipment())
                        {
                            // Add equipment attack and hp to the bearer's ongoing stats
                            card.attack_ongoing += equip.attack;
                            card.hp_ongoing += equip.hp;
                        }
                    }
                }
            }

            //Update attack equal to HP effects (after status bonus)
            for (int p = 0; p < game_data.players.Length; p++)
            {
                Player player = game_data.players[p];
                for (int c = 0; c < player.cards_board.Count; c++)
                {
                    Card card = player.cards_board[c];
                    UpdateAtkEqualHpEffects(player, card);
                }
            }
        }

        protected virtual void UpdateAtkEqualHpEffects(Player player, Card card)
        {
            if (card == null || !card.CanDoAbilities())
                return;

            //Handle Vitality status
            if (card.HasStatus(StatusType.Vitality))
            {
                card.attack = card.GetHP();
                card.attack_ongoing = 0;
            }

            //Handle EffectSetAtkEqualHpReal abilities
            List<AbilityData> cabilities = card.GetAbilities();
            for (int a = 0; a < cabilities.Count; a++)
            {
                AbilityData ability = cabilities[a];
                if (ability != null && ability.trigger == AbilityTrigger.Ongoing && ability.AreTriggerConditionsMet(game_data, card))
                {
                    if (ability.target == AbilityTarget.Self && ability.AreTargetConditionsMet(game_data, card, card))
                    {
                        foreach (EffectData effect in ability.effects)
                        {
                            if (effect is EffectSetAtkEqualHpReal)
                                effect.DoOngoingEffect(this, ability, card, card);
                        }
                    }
                }
            }
        }

        protected virtual void UpdateOngoingKills()
        {
            //Kill stuff with 0 hp
            for (int p = 0; p < game_data.players.Length; p++)
            {
                Player player = game_data.players[p];
                for (int i = player.cards_board.Count - 1; i >= 0; i--)
                {
                    if (i < player.cards_board.Count)
                    {
                        Card card = player.cards_board[i];
                        if (card.GetHP() <= 0)
                            DiscardCard(card);
                    }
                }
                //修改 可以防御力为0

                //for (int i = player.cards_equip.Count - 1; i >= 0; i--)
                //{
                //    if (i < player.cards_equip.Count)
                //    {
                //        Card card = player.cards_equip[i];
                //        if (card.GetHP() <= 0)
                //            DiscardCard(card);
                //        Card bearer = player.GetBearerCard(card);
                //        if (bearer == null)
                //            DiscardCard(card);
                //    }
                //}
            }

            //Clear cards
            for (int c = 0; c < cards_to_clear.Count; c++)
                cards_to_clear[c].Clear();
            cards_to_clear.Clear();
        }

        //Step 1: Add self-targeting ongoing effects (like Flying status to self)
        protected virtual void UpdateOngoingSelfEffects(Player player, Card card)
        {
            if (card == null || !card.CanDoAbilities())
                return;

            List<AbilityData> cabilities = card.GetAbilities();
            for (int a = 0; a < cabilities.Count; a++)
            {
                AbilityData ability = cabilities[a];
                if (ability != null && ability.trigger == AbilityTrigger.Ongoing && ability.AreTriggerConditionsMet(game_data, card))
                {
                    //Only process self-targeting effects
                    if (ability.target == AbilityTarget.Self)
                    {
                        if (ability.AreTargetConditionsMet(game_data, card, card))
                        {
                            ability.DoOngoingEffects(this, card, card);
                        }
                    }
                }
            }
        }

        //Step 2: Add aura effects that target other cards (like Flying Aura)
        protected virtual void UpdateOngoingAuraEffects(Player player, Card card)
        {
            if (card == null || !card.CanDoAbilities())
                return;

            List<AbilityData> cabilities = card.GetAbilities();
            for (int a = 0; a < cabilities.Count; a++)
            {
                AbilityData ability = cabilities[a];
                if (ability != null && ability.trigger == AbilityTrigger.Ongoing && ability.AreTriggerConditionsMet(game_data, card))
                {
                    //Skip self-targeting effects (already processed in Step 1)
                    if (ability.target == AbilityTarget.Self)
                        continue;

                    if (ability.target == AbilityTarget.PlayerSelf)
                    {
                        if (ability.AreTargetConditionsMet(game_data, card, player))
                        {
                            ability.DoOngoingEffects(this, card, player);
                        }
                    }

                    if (ability.target == AbilityTarget.AllPlayers || ability.target == AbilityTarget.PlayerOpponent)
                    {
                        for (int tp = 0; tp < game_data.players.Length; tp++)
                        {
                            if (ability.target == AbilityTarget.AllPlayers || tp != player.player_id)
                            {
                                Player oplayer = game_data.players[tp];
                                if (ability.AreTargetConditionsMet(game_data, card, oplayer))
                                {
                                    ability.DoOngoingEffects(this, card, oplayer);
                                }
                            }
                        }
                    }

                    if (ability.target == AbilityTarget.EquippedCard)
                    {
                        if (card.CardData.IsEquipment())
                        {
                            Card target = player.GetBearerCard(card);
                            if (target != null && ability.AreTargetConditionsMet(game_data, card, target))
                            {
                                ability.DoOngoingEffects(this, card, target);
                            }
                        }
                        else if (card.equipped_uid != null)
                        {
                            Card target = game_data.GetCard(card.equipped_uid);
                            if (target != null && ability.AreTargetConditionsMet(game_data, card, target))
                            {
                                ability.DoOngoingEffects(this, card, target);
                            }
                        }
                    }

                    if (ability.target == AbilityTarget.AllCardsAllPiles || ability.target == AbilityTarget.AllCardsHand || ability.target == AbilityTarget.AllCardsBoard)
                    {
                        for (int tp = 0; tp < game_data.players.Length; tp++)
                        {
                            Player tplayer = game_data.players[tp];

                            if (ability.target == AbilityTarget.AllCardsAllPiles || ability.target == AbilityTarget.AllCardsHand)
                            {
                                for (int tc = 0; tc < tplayer.cards_hand.Count; tc++)
                                {
                                    Card tcard = tplayer.cards_hand[tc];
                                    if (ability.AreTargetConditionsMet(game_data, card, tcard))
                                    {
                                        ability.DoOngoingEffects(this, card, tcard);
                                    }
                                }
                            }

                            if (ability.target == AbilityTarget.AllCardsAllPiles || ability.target == AbilityTarget.AllCardsBoard)
                            {
                                for (int tc = 0; tc < tplayer.cards_board.Count; tc++)
                                {
                                    Card tcard = tplayer.cards_board[tc];
                                    if (ability.AreTargetConditionsMet(game_data, card, tcard))
                                    {
                                        ability.DoOngoingEffects(this, card, tcard);
                                    }
                                }
                            }

                            if (ability.target == AbilityTarget.AllCardsAllPiles)
                            {
                                for (int tc = 0; tc < tplayer.cards_equip.Count; tc++)
                                {
                                    Card tcard = tplayer.cards_equip[tc];
                                    if (ability.AreTargetConditionsMet(game_data, card, tcard))
                                    {
                                        ability.DoOngoingEffects(this, card, tcard);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected virtual void UpdateOngoingAbilities(Player player, Card card)
        {
            if (card == null || !card.CanDoAbilities())
                return;

            List<AbilityData> cabilities = card.GetAbilities();
            for (int a = 0; a < cabilities.Count; a++)
            {
                AbilityData ability = cabilities[a];
                if (ability != null && ability.trigger == AbilityTrigger.Ongoing && ability.AreTriggerConditionsMet(game_data, card))
                {
                    if (ability.target == AbilityTarget.Self)
                    {
                        if (ability.AreTargetConditionsMet(game_data, card, card))
                        {
                            ability.DoOngoingEffects(this, card, card);
                        }
                    }

                    if (ability.target == AbilityTarget.PlayerSelf)
                    {
                        if (ability.AreTargetConditionsMet(game_data, card, player))
                        {
                            ability.DoOngoingEffects(this, card, player);
                        }
                    }

                    if (ability.target == AbilityTarget.AllPlayers || ability.target == AbilityTarget.PlayerOpponent)
                    {
                        for (int tp = 0; tp < game_data.players.Length; tp++)
                        {
                            if (ability.target == AbilityTarget.AllPlayers || tp != player.player_id)
                            {
                                Player oplayer = game_data.players[tp];
                                if (ability.AreTargetConditionsMet(game_data, card, oplayer))
                                {
                                    ability.DoOngoingEffects(this, card, oplayer);
                                }
                            }
                        }
                    }

                    if (ability.target == AbilityTarget.EquippedCard)
                    {
                        if (card.CardData.IsEquipment())
                        {
                            //Get bearer of the equipment
                            Card target = player.GetBearerCard(card);
                            if (target != null && ability.AreTargetConditionsMet(game_data, card, target))
                            {
                                ability.DoOngoingEffects(this, card, target);
                            }
                        }
                        else if (card.equipped_uid != null)
                        {
                            //Get equipped card
                            Card target = game_data.GetCard(card.equipped_uid);
                            if (target != null && ability.AreTargetConditionsMet(game_data, card, target))
                            {
                                ability.DoOngoingEffects(this, card, target);
                            }
                        }
                    }

                    if (ability.target == AbilityTarget.AllCardsAllPiles || ability.target == AbilityTarget.AllCardsHand || ability.target == AbilityTarget.AllCardsBoard)
                    {
                        for (int tp = 0; tp < game_data.players.Length; tp++)
                        {
                            //Looping on all cards is very slow, since there are no ongoing effects that works out of board/hand we loop on those only
                            Player tplayer = game_data.players[tp];

                            //Hand Cards
                            if (ability.target == AbilityTarget.AllCardsAllPiles || ability.target == AbilityTarget.AllCardsHand)
                            {
                                for (int tc = 0; tc < tplayer.cards_hand.Count; tc++)
                                {
                                    Card tcard = tplayer.cards_hand[tc];
                                    if (ability.AreTargetConditionsMet(game_data, card, tcard))
                                    {
                                        ability.DoOngoingEffects(this, card, tcard);
                                    }
                                }
                            }

                            //Board Cards
                            if (ability.target == AbilityTarget.AllCardsAllPiles || ability.target == AbilityTarget.AllCardsBoard)
                            {
                                for (int tc = 0; tc < tplayer.cards_board.Count; tc++)
                                {
                                    Card tcard = tplayer.cards_board[tc];
                                    if (ability.AreTargetConditionsMet(game_data, card, tcard))
                                    {
                                        ability.DoOngoingEffects(this, card, tcard);
                                    }
                                }
                            }

                            //Equip Cards
                            if (ability.target == AbilityTarget.AllCardsAllPiles)
                            {
                                for (int tc = 0; tc < tplayer.cards_equip.Count; tc++)
                                {
                                    Card tcard = tplayer.cards_equip[tc];
                                    if (ability.AreTargetConditionsMet(game_data, card, tcard))
                                    {
                                        ability.DoOngoingEffects(this, card, tcard);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected virtual void AddOngoingStatusBonus(Card card, CardStatus status)
        {
            if (status.type == StatusType.AddAttack)
                card.attack_ongoing += status.value;
            if (status.type == StatusType.AddHP)
                card.hp_ongoing += status.value;
            if (status.type == StatusType.AddManaCost)
                card.mana_ongoing += status.value;
        }

        //---- Secrets ------------

        public virtual bool TriggerPlayerSecrets(Player player, AbilityTrigger secret_trigger)
        {
            for (int i = player.cards_secret.Count - 1; i >= 0; i--)
            {
                Card card = player.cards_secret[i];
                CardData icard = card.CardData;
                if (icard.type == CardType.Secret && !card.exhausted)
                {
                    if (card.AreAbilityConditionsMet(secret_trigger, game_data, card, card))
                    {
                        resolve_queue.AddSecret(secret_trigger, card, card, ResolveSecret);
                        resolve_queue.SetDelay(0.5f);
                        card.exhausted = true;

                        if (onSecretTrigger != null)
                            onSecretTrigger.Invoke(card, card);

                        return true; //Trigger only 1 secret per trigger
                    }
                }
            }
            return false;
        }

        public virtual bool TriggerSecrets(AbilityTrigger secret_trigger, Card trigger_card)
        {
            if (trigger_card != null && trigger_card.HasStatus(StatusType.SpellImmunity))
                return false; //Spell Immunity, triggerer is the one that trigger the trap, target is the one attacked, so usually the player who played the trap, so we dont check the target

            for (int p = 0; p < game_data.players.Length; p++)
            {
                if (p != game_data.current_player)
                {
                    Player other_player = game_data.players[p];
                    for (int i = other_player.cards_secret.Count - 1; i >= 0; i--)
                    {
                        Card card = other_player.cards_secret[i];
                        CardData icard = card.CardData;
                        if (icard.type == CardType.Secret && !card.exhausted)
                        {
                            Card trigger = trigger_card != null ? trigger_card : card;
                            if (card.AreAbilityConditionsMet(secret_trigger, game_data, card, trigger))
                            {
                                resolve_queue.AddSecret(secret_trigger, card, trigger, ResolveSecret);
                                resolve_queue.SetDelay(0.5f);
                                card.exhausted = true;

                                if (onSecretTrigger != null)
                                    onSecretTrigger.Invoke(card, trigger);

                                return true; //Trigger only 1 secret per trigger
                            }
                        }
                    }
                }
            }
            return false;
        }

        protected virtual void ResolveSecret(AbilityTrigger secret_trigger, Card secret_card, Card trigger)
        {
            CardData icard = secret_card.CardData;
            Player player = game_data.GetPlayer(secret_card.player_id);
            if (icard.type == CardType.Secret)
            {
                Player tplayer = game_data.GetPlayer(trigger.player_id);
                if (!is_ai_predict)
                    tplayer.AddHistory(GameAction.SecretTriggered, secret_card, trigger);

                TriggerCardAbilityType(secret_trigger, secret_card, trigger);
                DiscardCard(secret_card);

                if (onSecretResolve != null)
                    onSecretResolve.Invoke(secret_card, trigger);
            }
        }

        //---- Resolve Selector -----

        public virtual void SelectCard(Card target)
        {
            if (game_data.selector == SelectorType.None)
                return;

            Card caster = game_data.GetCard(game_data.selector_caster_uid);
            AbilityData ability = AbilityData.Get(game_data.selector_ability_id);

            if (caster == null || target == null || ability == null)
                return;

            if (game_data.selector == SelectorType.SelectTarget)
            {
                //多目标（顺序逐槽）：本槽校验 → 记录结果 → 推进下一槽（不立即结算）
                if (ability.HasTargetSlots())
                {
                    int node_slot = CurrentSelectSlotNode();
                    if (!CanSelectForSlot(ability, caster, target, node_slot))
                        return;
                    StoreCurrentSlotResult(target.uid);
                    GameLog.Log("[多目标] 目标" + node_slot + " 选中 " + target.CardData?.id);
                    return;
                }

                if (!ability.CanTarget(game_data, caster, target))
                    return; //Can't target that target

                Player player = game_data.GetPlayer(caster.player_id);
                if (!is_ai_predict)
                    player.AddHistory(GameAction.CastAbility, caster, ability, target);

                game_data.selector = SelectorType.None;
                game_data.last_target = target.uid;
                ResolveEffectTarget(ability, caster, target);
                AfterAbilityResolved(ability, caster);
                resolve_queue.ResolveAll();
            }

            if (game_data.selector == SelectorType.SelectorCard)
            {
                if (!ability.IsCardSelectionValid(game_data, caster, target, card_array))
                    return; //Supports conditions and filters

                game_data.selector = SelectorType.None;
                game_data.last_target = target.uid;
                ResolveEffectTarget(ability, caster, target);
                AfterAbilityResolved(ability, caster);
                resolve_queue.ResolveAll();
            }
        }

        public virtual void SelectPlayer(Player target)
        {
            if (game_data.selector == SelectorType.None)
                return;

            Card caster = game_data.GetCard(game_data.selector_caster_uid);
            AbilityData ability = AbilityData.Get(game_data.selector_ability_id);

            if (caster == null || target == null || ability == null)
                return;

            if (game_data.selector == SelectorType.SelectTarget)
            {
                //多目标：英雄格代表该玩家的英雄卡（与图条件用英雄卡代入的约定一致）
                if (ability.HasTargetSlots())
                {
                    Card hero = target != null ? target.hero : null;
                    int node_slot = CurrentSelectSlotNode();
                    if (hero == null || !CanSelectForSlot(ability, caster, hero, node_slot))
                        return;
                    StoreCurrentSlotResult(hero.uid);
                    GameLog.Log("[多目标] 目标" + node_slot + " 选中英雄 p" + target.player_id);
                    return;
                }

                if (!ability.CanTarget(game_data, caster, target))
                    return; //Can't target that target

                Player player = game_data.GetPlayer(caster.player_id);
                if (!is_ai_predict)
                    player.AddHistory(GameAction.CastAbility, caster, ability, target);

                game_data.selector = SelectorType.None;
                ResolveEffectTarget(ability, caster, target);
                AfterAbilityResolved(ability, caster);
                resolve_queue.ResolveAll();
            }
        }

        public virtual void SelectSlot(Slot target)
        {
            if (game_data.selector == SelectorType.None)
                return;

            Card caster = game_data.GetCard(game_data.selector_caster_uid);
            AbilityData ability = AbilityData.Get(game_data.selector_ability_id);

            if (caster == null || ability == null || !target.IsValid())
                return;

            if (game_data.selector == SelectorType.SelectTarget)
            {
                //多目标：格子候选 → 格内卡；英雄格 → 该玩家英雄卡（结算时按卡处理）
                if (ability.HasTargetSlots())
                {
                    Card slot_card = game_data.GetSlotCard(target);
                    if (slot_card == null && target.IsPlayerSlot())
                    {
                        Player sp = game_data.GetPlayer(target.p);
                        slot_card = sp != null ? sp.hero : null;
                    }
                    int node_slot = CurrentSelectSlotNode();
                    if (slot_card == null || !CanSelectForSlot(ability, caster, slot_card, node_slot))
                        return;
                    StoreCurrentSlotResult(slot_card.uid);
                    GameLog.Log("[多目标] 目标" + node_slot + " 选中 " + slot_card.CardData?.id + " @" + target);
                    return;
                }

                if (!ability.CanTarget(game_data, caster, target))
                    return; //Conditions not met

                Player player = game_data.GetPlayer(caster.player_id);
                if (!is_ai_predict)
                    player.AddHistory(GameAction.CastAbility, caster, ability, target);

                game_data.selector = SelectorType.None;
                ResolveEffectTarget(ability, caster, target);
                AfterAbilityResolved(ability, caster);
                resolve_queue.ResolveAll();
            }
        }

        public virtual void SelectChoice(int choice)
        {
            if (game_data.selector == SelectorType.None)
                return;

            Card caster = game_data.GetCard(game_data.selector_caster_uid);
            AbilityData ability = AbilityData.Get(game_data.selector_ability_id);

            if (caster == null || ability == null || choice < 0)
                return;

            if (game_data.selector == SelectorType.SelectorChoice && ability.target == AbilityTarget.ChoiceSelector)
            {
                if (choice >= 0 && choice < ability.chain_abilities.Length)
                {
                    AbilityData achoice = ability.chain_abilities[choice];
                    if (achoice != null && game_data.CanSelectAbility(caster, achoice))
                    {
                        game_data.selector = SelectorType.None;
                        AfterAbilityResolved(ability, caster);
                        ResolveCardAbility(achoice, caster, caster);
                        resolve_queue.ResolveAll();
                    }
                }
            }
        }

        public virtual void SelectCost(int select_cost)
        {
            if (game_data.selector == SelectorType.None)
                return;

            Player player = game_data.GetPlayer(game_data.selector_player_id);
            Card caster = game_data.GetCard(game_data.selector_caster_uid);

            if (player == null || caster == null || select_cost < 0)
                return;

            if (game_data.selector == SelectorType.SelectorCost)
            {
                if (select_cost >= 0 && select_cost < 10 && select_cost <= player.mana)
                {
                    game_data.selector = SelectorType.None;
                    game_data.selected_value = select_cost;
                    player.mana -= select_cost;
                    RefreshData();

                    TriggerSecrets(AbilityTrigger.OnPlayOther, caster);
                    TriggerCardAbilityType(AbilityTrigger.OnPlay, caster);
                    TriggerOtherCardsAbilityType(AbilityTrigger.OnPlayOther, caster);
                    resolve_queue.ResolveAll();
                }
            }
        }

        public virtual void CancelSelection()
        {
            if (game_data.selector != SelectorType.None)
            {
                //Return card to hand if was selecting cost
                if (game_data.selector == SelectorType.SelectorCost)
                    CancelPlayCard();

                //End selection（多目标：连同槽计划一起清掉）
                game_data.selector = SelectorType.None;
                game_data.selector_slot_index = 0;
                game_data.selector_slot_nodes = null;
                game_data.selector_selected_uids = null;
                multi_target_results = null;
                RefreshData();
            }
        }

        public void CancelPlayCard()
        {
            Card card = game_data.GetCard(game_data.selector_caster_uid);
            if (card != null)
            {
                Player player = game_data.GetPlayer(card.player_id);
                if (card.CardData.IsDynamicManaCost())
                    player.mana += game_data.selected_value;
                else
                    player.mana += card.CardData.cost;

                player.RemoveCardFromAllGroups(card);
                player.AddCard(player.cards_hand, card);
                card.Clear();
            }
        }

        public virtual void Mulligan(Player player, string[] cards)
        {
            if (game_data.phase == GamePhase.Mulligan && !player.ready)
            {
                int count = 0;
                List<Card> remove_list = new List<Card>();
                foreach (Card card in player.cards_hand)
                {
                    if (cards.Contains(card.uid))
                    {
                        remove_list.Add(card);
                        count++;
                    }
                }

                foreach (Card card in remove_list)
                {
                    player.RemoveCardFromAllGroups(card);
                    player.cards_deck.Add(card);
                }

                ShuffleDeck(player.cards_deck);

                player.ready = true;
                DrawCard(player, count);
                RefreshData();

                if (game_data.AreAllPlayersReady())
                {
                    StartTurn();
                }
            }
        }

        //-----Trigger Selector-----

        protected virtual void GoToSelectTarget(AbilityData iability, Card caster)
        {
            game_data.selector = SelectorType.SelectTarget;
            game_data.selector_player_id = caster.player_id;
            game_data.selector_ability_id = iability.id;
            game_data.selector_caster_uid = caster.uid;

            //顺序逐槽多目标：建立槽计划（图槽号，可空洞），游标归零，然后自动跳过"无合法候选"的槽
            if (iability.HasTargetSlots())
            {
                List<int> nodes = iability.GetSelectableSlotNodes();
                game_data.selector_slot_nodes = nodes.ToArray();
                game_data.selector_selected_uids = new string[nodes.Count];
                game_data.selector_slot_index = 0;
                RefreshData();
                AdvanceSelectSlot(iability, caster);
                return;
            }

            game_data.selector_slot_index = 0;
            game_data.selector_slot_nodes = null;
            game_data.selector_selected_uids = null;
            RefreshData();
        }

        //-----多目标（顺序逐槽选择）-----

        /// <summary>顺序逐槽多目标的选择结果：图槽号 → 选中的卡（仅结算期间有效，供 EffectRunGraph 读取）</summary>
        public Dictionary<int, Card> GetMultiTargetResults()
        {
            return multi_target_results;
        }

        /// <summary>当前正在选择的图槽号（非多目标选择态返回 0）</summary>
        public int CurrentSelectSlotNode()
        {
            return game_data.CurrentSelectSlotNode();
        }

        /// <summary>该 uid 是否已被前面的槽选走（多目标：同一张卡不可被两个槽选中）</summary>
        private bool IsAlreadySelected(string uid)
        {
            return game_data.IsSlotTargetSelected(uid);
        }

        /// <summary>目标能否被指定槽选中：全局条件 + 槽私有条件 +（开启"目标去重"时）未被前面的槽选过</summary>
        public bool CanSelectForSlot(AbilityData ability, Card caster, Card target, int node_slot)
        {
            if (ability == null || caster == null || target == null)
                return false;
            if (!ability.CanTarget(game_data, caster, target))
                return false;
            if (!ability.AreSlotConditionsMet(game_data, caster, target, node_slot))
                return false;
            if (ability.UseTargetDedupe() && IsAlreadySelected(target.uid))
                return false;
            return true;
        }

        /// <summary>当前多目标槽的合法候选卡（AI 决策 / UI 高亮用；非多目标返回空列表）</summary>
        public List<Card> GetCurrentSlotCandidates(AbilityData ability, Card caster)
        {
            List<Card> list = new List<Card>();
            int node_slot = CurrentSelectSlotNode();
            if (node_slot <= 0 || ability == null || caster == null)
                return list;
            foreach (Player p in game_data.players)
            {
                if (p == null || p.cards_board == null)
                    continue;
                foreach (Card c in p.cards_board)
                {
                    if (c != null && CanSelectForSlot(ability, caster, c, node_slot))
                        list.Add(c);
                }
            }
            return list;
        }

        /// <summary>某槽是否至少有一张合法候选（用于自动跳过）。候选范围=双方场上卡 + 英雄（SelectTarget 通道）。</summary>
        private bool SlotHasCandidate(AbilityData ability, Card caster, int node_slot)
        {
            foreach (Player p in game_data.players)
            {
                if (p == null)
                    continue;
                if (p.cards_board != null)
                {
                    foreach (Card c in p.cards_board)
                    {
                        if (c != null && CanSelectForSlot(ability, caster, c, node_slot))
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>记录当前槽的选择结果并推进：跳过的槽留 null（§决策：无合法目标=跳过该槽，不拦截结算）</summary>
        private void StoreCurrentSlotResult(string uid)
        {
            game_data.selector_selected_uids[game_data.selector_slot_index] = uid;
            game_data.selector_slot_index++;
            Card caster = game_data.GetCard(game_data.selector_caster_uid);
            AbilityData ability = AbilityData.Get(game_data.selector_ability_id);
            if (caster != null && ability != null)
                AdvanceSelectSlot(ability, caster);
        }

        /// <summary>推进到下一个"有合法候选"的槽；无候选的槽记为跳过并提示其 error 文案；全部处理完则统一结算。</summary>
        private void AdvanceSelectSlot(AbilityData ability, Card caster)
        {
            if (game_data.selector_slot_nodes == null || game_data.selector_selected_uids == null)
                return;
            while (game_data.selector_slot_index < game_data.selector_slot_nodes.Length)
            {
                int node_slot = game_data.selector_slot_nodes[game_data.selector_slot_index];
                if (SlotHasCandidate(ability, caster, node_slot))
                {
                    RefreshData();
                    return;     //停在该槽等玩家选
                }
                AbilityTargetSlot spec = ability.GetTargetSlot(node_slot);
                string err = spec != null ? spec.error : "";
                GameLog.Log("[多目标] 目标" + node_slot + " 无合法目标，已跳过" + (string.IsNullOrEmpty(err) ? "" : "（" + err + "）"));
                game_data.selector_selected_uids[game_data.selector_slot_index] = null;
                game_data.selector_slot_index++;
            }
            FinishMultiTarget(ability, caster);
        }

        /// <summary>玩家/ AI 主动跳过当前槽（UI「跳过此目标」按钮）</summary>
        public virtual void SkipCurrentSelectSlot()
        {
            if (game_data.selector != SelectorType.SelectTarget || game_data.selector_slot_nodes == null)
                return;
            Card caster = game_data.GetCard(game_data.selector_caster_uid);
            AbilityData ability = AbilityData.Get(game_data.selector_ability_id);
            if (caster == null || ability == null)
                return;
            if (game_data.selector_slot_index < 0 || game_data.selector_slot_index >= game_data.selector_selected_uids.Length)
                return;
            int node_slot = game_data.selector_slot_nodes[game_data.selector_slot_index];
            GameLog.Log("[多目标] 目标" + node_slot + " 被跳过");
            game_data.selector_selected_uids[game_data.selector_slot_index] = null;
            game_data.selector_slot_index++;
            AdvanceSelectSlot(ability, caster);
        }

        /// <summary>全部槽处理完：关掉选择态 → 槽号→卡 映射交给 EffectRunGraph → 效果只结算一次。</summary>
        private void FinishMultiTarget(AbilityData ability, Card caster)
        {
            game_data.selector = SelectorType.None;

            Dictionary<int, Card> results = new Dictionary<int, Card>();
            if (game_data.selector_slot_nodes != null && game_data.selector_selected_uids != null)
            {
                for (int i = 0; i < game_data.selector_slot_nodes.Length && i < game_data.selector_selected_uids.Length; i++)
                {
                    string uid = game_data.selector_selected_uids[i];
                    Card c = string.IsNullOrEmpty(uid) ? null : game_data.GetCard(uid);
                    results[game_data.selector_slot_nodes[i]] = c;   //null=该槽被跳过/留空
                }
            }
            game_data.selector_slot_index = 0;
            game_data.selector_slot_nodes = null;
            game_data.selector_selected_uids = null;

            Player player = game_data.GetPlayer(caster.player_id);
            if (!is_ai_predict && player != null)
                player.AddHistory(GameAction.CastAbility, caster, ability);   //多目标只记一条历史（不逐槽刷屏）

            multi_target_results = results;
            try
            {
                ability.DoEffects(this, caster);   //无目标重载：EffectRunGraph 读多目标映射，整张图只跑一次
                AfterAbilityResolved(ability, caster);
                resolve_queue.ResolveAll();
            }
            finally
            {
                multi_target_results = null;
            }
        }

        protected virtual void GoToSelectorCard(AbilityData iability, Card caster)
        {
            game_data.selector = SelectorType.SelectorCard;
            game_data.selector_player_id = caster.player_id;
            game_data.selector_ability_id = iability.id;
            game_data.selector_caster_uid = caster.uid;
            RefreshData();
        }

        protected virtual void GoToSelectorChoice(AbilityData iability, Card caster)
        {
            game_data.selector = SelectorType.SelectorChoice;
            game_data.selector_player_id = caster.player_id;
            game_data.selector_ability_id = iability.id;
            game_data.selector_caster_uid = caster.uid;
            RefreshData();
        }

        protected virtual void GoToSelectorCost(Card caster)
        {
            game_data.selector = SelectorType.SelectorCost;
            game_data.selector_player_id = caster.player_id;
            game_data.selector_ability_id = "";
            game_data.selector_caster_uid = caster.uid;
            game_data.selected_value = 0;
            RefreshData();
        }

        protected virtual void GoToMulligan()
        {
            game_data.phase = GamePhase.Mulligan;
            game_data.turn_timer = GameplayData.Get().turn_duration;
            foreach (Player player in game_data.players)
                player.ready = false;
            RefreshData();
        }

        //-------------

        public virtual void RefreshData()
        {
            onRefresh?.Invoke();
        }

        public virtual void ClearResolve()
        {
            resolve_queue.Clear();
        }

        public virtual bool IsResolving()
        {
            return resolve_queue.IsResolving();
        }

        public virtual bool IsGameStarted()
        {
            return game_data.HasStarted();
        }

        public virtual bool IsGameEnded()
        {
            return game_data.HasEnded();
        }

        public virtual Game GetGameData()
        {
            return game_data;
        }

        public System.Random GetRandom()
        {
            return random;
        }

        public Game GameData { get { return game_data; } }
        public ResolveQueue ResolveQueue { get { return resolve_queue; } }
    }
}
