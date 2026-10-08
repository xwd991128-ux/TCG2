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

        /// <summary>是否为 AI 推演实例（预测用）。供 BattleLog 等外部系统判断"这一条要不要记进对战记录"。</summary>
        public bool IsAIPredict { get { return is_ai_predict; } }

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
        //「使用卡牌后」= OnAfterPlay：打出卡牌结算后广播；发动英雄技能（技能卡）时同样广播（主体=技能卡）
        private const string PLAY_AFTER = "OnAfterPlay";

        /// <summary>该事件入口是否支持"延迟 / 等待事件后执行"（当前＝「起动后」入口）</summary>
        public static bool IsDelayableEntry(string action)
        {
            return action == ACTIVATE_AFTER;
        }

        /// <summary>对局开始挂载英雄技能时登记的能力 id → 技能卡定义（MountHeroSkills 写入，仅"技能卡挂载"这种配置才有）。</summary>
        private readonly Dictionary<string, CardData> hero_skill_defs = new Dictionary<string, CardData>();

        /// <summary>英雄卡**自带**起动式能力 → 合成技能卡定义（按能力 id 缓存，避免每次发动都新建 SO）。
        /// 本项目的内置英雄把技能写在**英雄卡自己的规则图**里（`起动式效果入口` 节点），
        /// 并不经过 `hero_data.skills` 的技能卡挂载，所以需要这一路兜底。
        /// 用 ConcurrentDictionary：主线程发动技能时可能写，AI 推演线程同时在读。</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CardData> hero_skill_synth_defs
            = new System.Collections.Concurrent.ConcurrentDictionary<string, CardData>();

        /// <summary>合成技能卡定义 id 前缀（便于从卡牌 id 一眼看出是"英雄技能"而不是真实卡池卡）。
        /// ★public：<c>Game.CanPlayCard</c> 靠它区分"合成英雄技能定义（不可从手牌打出）"与
        /// "真实技能卡（工作台类型=技能，与法术同款可从手牌打出）"。</summary>
        public const string HERO_SKILL_DEF_PREFIX = "hero_skill_";

        /// <summary>技能卡上下文卡的 uid 序号（保证同一次对局里 uid 不重复）</summary>
        private int skill_ctx_seq = 0;

        /// <summary>本次发动的能力对应的技能卡定义（"技能卡挂载"配置下的真实技能卡；没有则 null）</summary>
        private CardData GetHeroSkillDef(AbilityData ability)
        {
            if (ability == null || string.IsNullOrEmpty(ability.id))
                return null;
            CardData def;
            return hero_skill_defs.TryGetValue(ability.id, out def) ? def : null;
        }

        /// <summary>本次发动是不是「英雄技能」→ 返回它的**技能卡定义**（用于「使用卡牌后」的事件主体）。
        /// 两种来源都算：①英雄卡自带图里的起动式能力（内置英雄的写法）→ 按能力合成一张 type=Skill 的定义；
        /// ②`hero_data.skills` 挂载的技能卡能力 → 直接用技能卡定义。
        /// 为什么合成：技能本身没有卡实例，而「使用卡牌后」的现有写法是「卡牌类型判断=技能」「获取卡牌拥有者=施法玩家」，
        /// 合成一张 type=Skill 的定义后这些写法原样成立（等价于"把英雄技能当成一张技能牌"）。
        /// 非英雄卡（随从/装备主动技）返回 null——它们不是"使用一张牌"。
        /// ★线程约束：`ScriptableObject.CreateInstance` 只能主线程调用（AI 推演在**后台线程**执行图，
        ///   在那里创建会抛 UnityException 并把整次 AI 计算打掉）。所以：对局开始时已在主线程
        ///   `WarmupHeroSkillDefs` 预热，后台线程只读缓存；缓存未命中且不在主线程 → 返回 null（该次不广播）。</summary>
        private CardData ResolveSkillDefForUse(Card caster, AbilityData ability)
        {
            CardData mounted = GetHeroSkillDef(ability);
            if (mounted != null)
                return mounted;
            if (caster == null || caster.CardData == null || ability == null
                || string.IsNullOrEmpty(ability.id) || caster.CardData.type != CardType.Hero)
                return null;
            CardData def;
            if (hero_skill_synth_defs.TryGetValue(ability.id, out def) && def != null)
                return def;
            if (!MainThreadUtil.IsMainThread)
                return null;        //后台线程（AI 推演）：绝不在这里建 ScriptableObject
            return CreateSynthSkillDef(ability);
        }

        /// <summary>按能力合成一张 type=Skill 的"技能卡定义"并缓存（★只能在主线程调用）</summary>
        private CardData CreateSynthSkillDef(AbilityData ability)
        {
            CardData def;
            if (hero_skill_synth_defs.TryGetValue(ability.id, out def) && def != null)
                return def;
            def = ScriptableObject.CreateInstance<CardData>();
            def.id = HERO_SKILL_DEF_PREFIX + ability.id;
            def.title = string.IsNullOrEmpty(ability.title) ? ability.id : ability.title;
            def.type = CardType.Skill;      //★关键：让「卡牌类型判断=技能」成立
            hero_skill_synth_defs[ability.id] = def;
            return def;
        }

        /// <summary>对局开始（主线程）预热英雄技能的合成卡定义：AI 推演在后台线程执行图时只读缓存，
        /// 从而既能照常预测「英雄技能→使用卡牌后」的收益，又不会在后台线程碰 CreateInstance。</summary>
        private void WarmupHeroSkillDefs(Card hero)
        {
            if (hero == null || hero.CardData == null || hero.CardData.type != CardType.Hero)
                return;
            if (!MainThreadUtil.IsMainThread || hero.CardData.abilities == null)
                return;
            foreach (AbilityData a in hero.CardData.abilities)
            {
                if (a != null && a.trigger == AbilityTrigger.Activate && !string.IsNullOrEmpty(a.id))
                    CreateSynthSkillDef(a);
            }
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
            SetGameBackRef(game);
            resolve_queue = new ResolveQueue(game, false);
            //★对战记录：绑定当前权威对局（AI 推演用的是 GameLogic(bool) 构造函数 → 不会绑上来，天然隔离）
            Gameplay.BattleLog.Bind(game);
        }

        public virtual void SetData(Game game)
        {
            game_data = game;
            SetGameBackRef(game);
            resolve_queue.SetData(game);
        }

        /// <summary>给 Game 挂"运行期反向引用"：<c>Game.CanAttackTarget</c> 要问卡牌的规则图，
        /// 而 UI 高亮只拿得到 Game（拿不到 logic）→ 没有这个引用，规则图就只对 AI/结算生效、UI 高亮失效。
        /// ★AI 预测实例（is_ai_predict）**不抢**这个引用：它们与主逻辑共享同一个 Game，
        /// 若被预测实例覆盖，UI 判定就会读到预测态（目标高亮错乱）。</summary>
        private void SetGameBackRef(Game game)
        {
            if (game != null && !is_ai_predict)
                game.logic = this;
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

                //手牌上限（仿"灵力上限"）：开局从配置 GameplayData.cards_max 写进玩家身上，
                //之后由节点/效果局内改写（获取/设置/增加手牌上限）—— 不能再直接读全局配置，否则局内修改无效。
                player.hand_max = GameplayData.Get().cards_max;

                //对战记录：新对局才清空（重连/中途重建 Logic 不清，保留已有记录）
                if (game_data.turn_count <= 0)
                    Gameplay.BattleLog.Reset(game_data);
                player.current_turn = game_data.turn_count;

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
            game_data.turn_timer = (game_data.settings != null && game_data.settings.NoTurnTimer)
                ? GameSettings.NoTurnTimerValue                      //人机/模拟：不下发倒计时（999=GameUI 不显示）
                : GameplayData.Get().turn_duration;
            //★对战记录：回合分隔行（影之诗「战斗记录」按回合分组读的就是它）
            player.current_turn = game_data.turn_count;
            if (!is_ai_predict)
                BattleLog.Turn(player.player_id, true);
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
                //★全局入口（作用范围=全部卡牌）：广播类事件（伤害时/治疗时/死亡时/添加增益时/护甲变动…）
                //  同样**不限制发动主体** —— 除"宿主卡自身的图"外，全局入口图也要在本事件里跑一遍。
                //  用带事件上下文的执行（RunEventEntry）→ 图里能读到 事件主体/事件值/事件玩家。
                RunGlobalEntriesForEvent(ctx);

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

        /// <summary>广播事件的"全局入口"执行（作用范围=全部卡牌）：按事件名映射到触发器 → 逐个跑（**带事件上下文**）。
        /// 发动主体不受限（任何卡满足条件都算）；异常隔离，坏图不影响对局。</summary>
        private void RunGlobalEntriesForEvent(GraphEventContext ctx)
        {
            if (ctx == null)
                return;
            List<Workshop.CardPoolIO.GlobalEntry> list = Workshop.CardPoolIO.GetGlobalEntries(Workshop.CardPoolIO.MapTrigger(ctx.action));
            if (list == null || list.Count == 0)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                Workshop.CardPoolIO.GlobalEntry ge = list[i];
                if (ge == null || ge.graph == null)
                    continue;
                try
                {
                    Workshop.NodeDocRunner.RunEventEntry(this, ctx, ge.graph, ctx.card, ge.action);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[全局入口] " + ge.action + " 执行失败（已忽略，不影响对局）：" + e.Message);
                }
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
        private static int fireEvent_debug_left = 24;   //事件分发诊断日志节流

        private void FireEventHost(Card host, GraphEventContext ctx, bool is_extra)
        {
            if (host == null || ctx == null)
                return;
            if (host.HasStatus(StatusType.Silenced))
                return;     //被沉默的卡不响应图事件
            string zone = GetCardZone(host);   //宿主当前所在牌堆（空=不在任何已知牌堆）

            //★分发诊断（节流）：为什么"事件广播了但图没跑"——宿主有没有能力、触发器名对不对，一眼可见
            if (fireEvent_debug_left > 0)
            {
                fireEvent_debug_left--;
                int n_ab = 0;
                string trig_list = "";
                foreach (AbilityData ab in host.GetAbilities())
                {
                    n_ab++;
                    trig_list += (trig_list.Length > 0 ? "," : "") + (ab != null ? ab.trigger.ToString() : "（空能力）");
                }
                Debug.Log("[事件分发] action=" + ctx.action + " 宿主=" + (host.CardData != null ? host.CardData.id : "?")
                    + " is_extra=" + is_extra + " zone=" + (string.IsNullOrEmpty(zone) ? "无" : zone)
                    + " 能力数=" + n_ab + " 触发器=[" + trig_list + "]");
            }

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
                    if (fireEvent_debug_left > 0)
                    {
                        fireEvent_debug_left--;
                        Debug.Log("[事件分发·命中] action=" + ctx.action + " 宿主=" + (host.CardData != null ? host.CardData.id : "?")
                            + " rg=" + (rg != null ? "有" : "null") + " graph=" + (rg != null && rg.graph != null ? "有" : "null")
                            + " trigger_action=" + (rg != null ? rg.trigger_action : "?")
                            + " entry=" + (entry != null ? "有" : "无") + " → 执行 RunEvent");
                    }
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
                case "OnAfterPlay":
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

        /// <summary>英雄技能卡挂载：英雄定义（CardData.skills）指定的技能卡（type=Skill），
        /// 把上面的起动式能力（ActivateAbility）挂到英雄卡实例上 → 对局里成为英雄的技能按钮。
        /// 与火精灵「火焰」同一条执行链（cast_ability → RunGraph）。</summary>
        private void MountHeroSkills(Player player)
        {
            if (player == null || player.hero == null)
                return;
            CardData hero_data = player.hero.CardData;
            if (hero_data == null || hero_data.skills == null || hero_data.skills.Count == 0)
                return;
            if (player.hero.abilities == null)
                player.hero.abilities = new List<string>();
            if (player.hero.abilities_ongoing == null)
                player.hero.abilities_ongoing = new List<string>();
            int mounted = 0;
            foreach (string sid in hero_data.skills)
            {
                if (string.IsNullOrEmpty(sid))
                    continue;
                CardData sd = CardData.Get(sid);
                if (sd == null)
                {
                    Debug.LogWarning("[英雄技能] 技能卡未注册：" + sid + "（英雄=" + hero_data.id + "）→ 已跳过");
                    continue;
                }
                if (sd.type != CardType.Skill)
                    Debug.LogWarning("[英雄技能] 引用的不是技能卡：" + sid + "（type=" + sd.type + "）→ 仍会挂载其能力");
                if (sd.abilities == null)
                    continue;
                foreach (AbilityData a in sd.abilities)
                {
                    if (a == null || string.IsNullOrEmpty(a.id))
                        continue;
                    if (a.trigger == AbilityTrigger.Ongoing)
                        player.hero.abilities_ongoing.Add(a.id);
                    else
                        player.hero.abilities.Add(a.id);
                    hero_skill_defs[a.id] = sd;   //登记能力→技能卡定义：供「使用卡牌后」覆盖英雄技能时构造事件主体
                    mounted++;
                }
            }
            if (mounted > 0)
                Debug.Log("[英雄技能] 英雄 " + hero_data.id + " 已挂载 " + mounted + " 个技能能力");
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
                {
                    player.hero = Card.Create(hdata, hvariant, player);
                    MountHeroSkills(player);   //★英雄技能卡（CardData.skills）→ 挂成英雄的技能按钮
                    WarmupHeroSkillDefs(player.hero);   //主线程预热"英雄技能=一张技能牌"的合成定义（供 AI 推演线程只读）
                }
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

            //空卡组不再立即判负（2026-09-24 起改走疲劳：无牌可抽 → 每回合递增掉血，见 DrawFatigue）。
            //但仍然要报出来：它几乎一定是配置问题（卡组没配卡或全部卡牌无效），不报就会看不到。
            if (player.cards_deck.Count == 0)
                Debug.LogError("[Game] 玩家 p" + player.player_id + " 卡组为空（卡组没配卡或全部卡牌无效）"
                    + "→ 不会立即判负，但每次抽牌都会吃疲劳伤害");
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

                //图事件「使用卡牌后」（全场监听；此时已扣费、已入场、战吼等已结算；value=费用供读取）
                //★放在结算队列 ResolveAll 之后：保证"打出的牌的后效"已经生效完，再通知监听者。
                onCardPlayed?.Invoke(card, slot);
                resolve_queue.ResolveAll(0.3f);

                try
                {
                    GraphEventContext actx = new GraphEventContext();
                    actx.action = "OnAfterPlay";
                    actx.phase = GraphEventPhase.After;
                    actx.card = card;
                    actx.source_card = card;
                    actx.player = player;
                    actx.value = card.CardData != null ? card.CardData.mana : 0;
                    EmitGraphEvent(actx, card);
                    //★事件里加的增益/属性要在**本次打出内**可见：广播后再做一次收敛
                    //（否则 AddAttack 等状态要等下一次 UpdateOngoing 才反映到攻击/费用上，实测"打出去数值不变"）
                    UpdateOngoing();
                }
                catch (System.Exception e_ap)
                {
                    Debug.LogError("[使用卡牌后] 事件广播异常（已忽略，不影响本次打出）：" + e_ap);
                }
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
                //★ 潜行在"发动能力后"失去：走持久移除（理由见圣盾处），避免被重算复活
                card.SetStatusPresence(StatusType.Stealth, false);
                TriggerCardAbility(iability, card);
                resolve_queue.ResolveAll();
            }
        }

        /// <summary>构造「起动」类图事件上下文（主体=发动能力的卡：英雄技能=英雄卡；值=本次灵力费用）</summary>
        private GraphEventContext BuildCastCtx(string action, GraphEventPhase phase, Card caster, int mana_cost)
        {
            GraphEventContext ctx = new GraphEventContext();
            ctx.action = action;
            ctx.phase = phase;                       //Before 才能被「阻止本事件」/「修改事件值」
            ctx.card = caster;
            ctx.player = game_data.GetPlayer(caster.player_id);
            ctx.value = mana_cost;                   //只读提示；实际扣费仍按能力定义
            return ctx;
        }

        /// <summary>「起动时」广播（起动式能力发动结算前）。返回 false = 被「阻止本事件」取消本次发动。
        /// 施法卡作为额外宿主，保证其自身图无论所在区域都能响应。</summary>
        private bool EmitActivateBefore(Card caster, AbilityData ability)
        {
            if (caster == null || ability == null || game_data == null)
                return true;
            bool cancelled = EmitGraphEvent(BuildCastCtx(ACTIVATE_BEFORE, GraphEventPhase.Before, caster, ability.mana_cost), caster);
            if (cancelled)
                GameLog.Log("[起动触发] " + (caster.CardData != null ? caster.CardData.id : "?")
                    + " 的起动被「阻止本事件」取消（本次不扣灵力、不结算）");
            return !cancelled;
        }

        /// <summary>「起动后」广播（起动式能力结算完成后：灵力已扣、exhausted 已生效、效果已结算）。
        /// 英雄技能额外按「使用卡牌后」广播一次（主体=技能卡，见 BuildSkillContextCard）。
        /// 入口若配了「延迟/等待事件」，由 FireEventHost 转为入队延后执行。纯通知，不可阻止。</summary>
        private void EmitActivateAfter(Card caster, AbilityData ability)
        {
            if (caster == null || ability == null || game_data == null || game_data.state == GameState.GameEnded)
                return;
            EmitGraphEvent(BuildCastCtx(ACTIVATE_AFTER, GraphEventPhase.After, caster, ability.mana_cost), caster);
            CardData skill_def = ResolveSkillDefForUse(caster, ability);
            if (skill_def == null)
                return;
            //「使用卡牌后」覆盖英雄技能（英雄技能=一张"技能牌"）：主体=技能卡上下文卡 → 「卡牌类型判断=技能」
            //「获取卡牌拥有者=施法玩家」这类写法与「打出一张牌」完全同构，用户图无需改动。
            Card skill_ctx = BuildSkillContextCard(skill_def, caster);
            if (skill_ctx != null)
                EmitGraphEvent(BuildCastCtx(PLAY_AFTER, GraphEventPhase.After, skill_ctx, ability.mana_cost), skill_ctx);
        }

        /// <summary>为「使用卡牌后」构造**技能卡上下文卡**：技能卡没有卡实例（其能力直接挂在英雄上），
        /// 这里按技能卡定义临时建一张，仅作事件主体求值用——**不写入 player.cards_all**，
        /// 因此不会出现在任何牌堆/卡牌查询/UI 里（避免了"假卡"污染对局状态）。</summary>
        private Card BuildSkillContextCard(CardData skill_def, Card caster)
        {
            if (skill_def == null || caster == null)
                return null;
            skill_ctx_seq++;
            Card card = new Card(skill_def.id, "skillctx_" + skill_def.id + "_" + skill_ctx_seq, caster.player_id);
            card.SetCard(skill_def, null);
            return card;
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

        /// <summary>被拒攻击的告警去重表（按攻击者卡 id）：被拒攻击此前只写 GameLog（正式构建里关闭）→ 完全静默，
        /// 导致"AI 反复提出同一个攻击、每帧重试、对局卡死"这类问题无法定位。去重后保证每种情况至少留一条痕迹。</summary>
        private static readonly HashSet<string> attack_reject_warned = new HashSet<string>();

        //统一攻击入口段：合法性/历史/战前能力与秘术（两组触发顺序与旧实现逐条一致）
        private void StartAttack(Card attacker, Card target_card, Player target_player, bool skip_cost, bool vs_player)
        {
            if (attacker == null || (vs_player && target_player == null) || (!vs_player && target_card == null))
                return;

            //★带原因的版本：卡牌规则图（「攻击时/被攻击时」入口上的 攻击限制/被攻击限制 口）拒绝时，
            //  以前这里是**静默 return** —— 表现就是"点了没反应"，或误以为"AI 无视了攻击限制"
            //  （其实非法攻击根本没造成伤害，只是白打）。现在把原因打出来，一行定位。
            string reject = null;
            bool can_attack = vs_player
                ? game_data.CanAttackTarget(attacker, target_player, skip_cost, out reject)
                : game_data.CanAttackTarget(attacker, target_card, skip_cost, out reject);
            if (!can_attack)
            {
                if (!is_ai_predict && !string.IsNullOrEmpty(reject))
                    GameLog.Log("[攻击被拒] " + attacker.CardData.id + " → "
                        + (vs_player ? "玩家" + target_player.player_id : target_card.CardData.id) + "：" + reject);
                //★上面那行只走 GameLog，而 GameLog 在正式构建里是**关闭**的 → 被拒的攻击此前在控制台完全不可见，
                //  直接导致"AI 反复提出同一个攻击、每帧重试、对局卡死"这种问题无法一眼定位（用户实报"对面卡住"）。
                //  这里补一条 Warning（节流），确保任何一次被拒的攻击都留得下痕迹。
                if (!is_ai_predict && !string.IsNullOrEmpty(reject) && attack_reject_warned.Add(attacker.card_id))
                    Debug.LogWarning("[攻击被拒] " + attacker.CardData.id + " → "
                        + (vs_player ? "玩家" + target_player.player_id : target_card.CardData.id)
                        + "：" + reject + "（本次攻击不产生任何效果）");
                return;
            }

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

            //★ 潜行在"攻击后"失去：持久移除（理由见圣盾处）
            attacker.SetStatusPresence(StatusType.Stealth, false);
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
                if (player.cards_deck.Count > 0 && player.cards_hand.Count < player.GetHandMax())
                {
                    Card card = player.cards_deck[0];
                    player.cards_deck.RemoveAt(0);
                    player.cards_hand.Add(card);
                    if (!is_ai_predict)
                        BattleLog.Card(player.player_id, BattleLogKind.Draw, card, null, 1);   //对战记录：抽牌

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
                else if (player.cards_deck.Count == 0)
                {
                    //★★ 无牌可抽 → 疲劳（不再直接判负，也不会白抽一张什么都不发生）
                    //  注意：只在**牌库空**时触发；"手牌已满但牌库还有牌"不算无牌可抽（那种情况保持原行为：不抽、不疲劳）
                    DrawFatigue(player);
                }
            }

            onCardDrawn?.Invoke(nb);
        }

        /// <summary>
        /// 疲劳结算：疲劳层数 +1，并造成**等于新层数**的伤害（第一次 1 点、第二次 2 点…）。
        ///   · 伤害走 DamagePlayer(attacker=null)：能吃到既有的伤害事件管线（OnBefore/OnAfterDamage、减免等）；
        ///   · 打完**立刻 CheckForWinner()**：否则血量归零要等下一个回合边界才判负，会多打一整回合；
        ///   · 计数器用 Player 的「疲劳层数」特性（与规则图节点同键），规则图也能读/改它。
        /// </summary>
        protected virtual void DrawFatigue(Player player)
        {
            if (player == null || game_data.state == GameState.GameEnded)
                return;

            int level = player.GetTraitValue(Player.TraitFatigue) + 1;
            player.SetTrait(Player.TraitFatigue, level);

            GameLog.Log("[疲劳] p" + player.player_id + " 无牌可抽：疲劳 " + level + " 层 → 受到 " + level + " 点伤害");
            DamagePlayer(null, player, level);

            CheckForWinner();
        }
        //Put a card from deck into discard
        /// <summary>弃掉牌库顶 N 张（旧 EffectDiscard 的玩家目标分支；也是"回合结束的弃牌"这类行为的实现）。
        /// ★与「弃牌行为」统一：改走标准 `DiscardCard` —— 会广播「弃牌时/后」图事件、走正常离场处理
        /// （旧实现是 deck.RemoveAt(0) + cards_discard.Add()，**不发任何事件、不走离场**，属于绕过弃牌行为）。
        /// 「弃牌时」是可阻止事件：被阻止时 DiscardCard 会保持原状（卡仍在牌库），这里据此中断本次弃牌。</summary>
        public virtual void DrawDiscardCard(Player player, int nb = 1)
        {
            if (player == null)
                return;
            for (int i = 0; i < nb; i++)
            {
                if (player.cards_deck.Count > 0)
                {
                    Card card = player.cards_deck[0];
                    DiscardCard(card);
                    if (player.cards_deck.Contains(card))
                        break;   //被「弃牌时」阻止：本次不弃（避免对同一张卡反复尝试）
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

        // ---------------- 手牌上限闸门（统一入口） ----------------

        /// <summary>手牌是否还有空位（**统一的"手牌上限"判定**）：所有"意图把卡放进手牌"的入口都应先问这里。
        /// 上限值 = <c>Player.GetHandMax()</c>（开局来自 GameplayData.cards_max，可被"获取/设置/增加手牌上限"节点局内改写）。</summary>
        public virtual bool HandHasRoom(Player player)
        {
            return player != null && player.cards_hand.Count < player.GetHandMax();
        }

        /// <summary>把手牌之外的一张卡移入手牌（统一入口，含上限闸门）。
        /// 手牌已满 → 返回 false 且**完全不动**该卡（牌留在原处，由调用方决定告警文案）。
        /// ★为什么要有这个统一入口：以前每个"往手牌放卡"的地方各写各的（有的读配置、有的干脆不检查），
        ///   同一个上限出现"有的动作能超、有的不能"。
        /// ★**中转例外**：EffectPlay / SummonCard（召唤到战场）/ 202006（创建并装备）是"先移入手牌 → 立刻打出/装备"，
        ///   属**中转**而非入手 → 它们直接调 SummonCardHand / cards_hand.Add，**不走本闸门**
        ///   （否则"手牌满时连召唤到战场、装备都会静默失效"）。</summary>
        public virtual bool TryMoveCardToHand(Player player, Card card)
        {
            if (card == null || !HandHasRoom(player))
                return false;
            player.RemoveCardFromAllGroups(card);
            player.cards_hand.Add(card);
            return true;
        }

        //Create a new card and send it to your hand
        /// <summary>★底层 API：**不查手牌上限**（它同时服务于 SummonCard / 202006 等**中转**路径）。
        /// 意图是"真入手"的调用方必须先判 <c>HandHasRoom</c>（或走 <c>TryMoveCardToHand</c>）。</summary>
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

        // ==================== 被动效果：生效 / 失效（进场 / 离场 / 变形） ====================
        // 被动效果入口（PassiveEffect）有三条线：动作(out，亡语等主效果) / 生效(enable) / 失效(disable)。
        // 后两条由 CardPoolIO 编译成能力（trigger = OnPassiveEnable / OnPassiveDisable，共享 passive_group 分组键），
        // 本段负责**驱动**它们：
        //   · 该卡在其入口「生效区域」内 且 未生效 → 执行「生效动作」线
        //   · 该卡已生效 但已不在生效区域内（或已被封印）→ 执行「失效动作」线
        //   · 变形（SetCard 换定义）→ 旧形态整组失效 + 新形态重新生效（见 TransformCard）
        // 为什么用「区域状态差分」而不是逐个挂钩子：进场（打出/召唤/置入）、离场（死亡/弃牌/回手/洗回/进墓地/
        // 暂存）、变形、复位，全都在扫描时自动覆盖 —— 逐个挂入口一定会漏（本项目区域改动路径有 9 处以上）。
        // 执行口径：两条线**同步执行**（AbilityData.DoEffects → EffectRunGraph），**不走 resolve_queue** ——
        // 与引擎既有的「关键词规则图」同口径（见 TriggerCardKeywords 注释：关键词规则不走 resolve_queue，直接同步执行）。
        // 走队列有两个坑（探针实测踩到）：① AddAbility 只入队，必须再点火；而 ResolveAll() 无参在"当前正处于
        // 结算中"时是**空操作**（is_resolving 直接 return）→ 被动线会一直躺在队列里，直到别人偶然再点一次火；
        // ② 延迟执行会让"生效/失效"与触发它的那次区域变化错开，图里读到的可能已是旧状态。
        // ★没有接 生效/失效 线的卡（绝大多数）完全不受影响：它没有 OnPassiveEnable 能力 → 本段对其零操作。
        [System.NonSerialized] private bool passive_syncing = false;   //重入保护：生效/失效线自身又会触发图执行

        /// <summary>同步所有卡的被动效果「生效/失效」状态（差分扫描）。
        /// 调用点：UpdateOngoing 末尾、DiscardCard 末尾、TransformCard 内、以及图执行（NodeDocRunner.Run）末尾 ——
        /// 这几处覆盖了所有会改变卡区域的操作。</summary>
        public virtual void SyncPassiveEffects()
        {
            if (passive_syncing || game_data == null || game_data.players == null)
                return;   //嵌套同步直接跳过（差额由下一次扫描补上，状态不会丢）
            passive_syncing = true;
            try
            {
                for (int i = 0; i < game_data.players.Length; i++)
                {
                    Player p = game_data.players[i];
                    if (p == null)
                        continue;
                    //逐区域扫描（不建临时列表：本方法调用很频繁）
                    SyncPassiveCard(p, p.hero);
                    SyncPassiveCards(p, p.cards_board);
                    SyncPassiveCards(p, p.cards_equip);
                    SyncPassiveCards(p, p.cards_hand);
                    SyncPassiveCards(p, p.cards_deck);
                    SyncPassiveCards(p, p.cards_discard);
                    SyncPassiveCards(p, p.cards_secret);
                    SyncPassiveCards(p, p.cards_temp);
                }
                //★光环效果（具名增益）同步：与被动共用"任何区域变化都收敛到这里"的时机，
                //  所以不必再给光环单独挂钩子（多了迟早漏一处）。
                SyncAuraEffects();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[被动效果] 生效/失效同步异常（已忽略，不影响对局）：" + e);
            }
            finally
            {
                passive_syncing = false;
            }
        }

        // ==================== 光环效果：具名增益的「进入范围施加 / 离开范围移除」 ====================
        // 「光环效果入口」的增益定义填**具名增益**（BuffData.id）时由本段驱动；填 StatusType 的旧式光环
        // 仍走 EffectAddStatus + 每帧 Ongoing 重算，完全不经过这里（旧卡零影响）。
        //
        // 为什么必须"差分"而不是"每帧施加"：BuffRuntime 的施加入口会广播图事件「添加增益时/后」并播放增益特效，
        // 每帧调用 = 每帧广播 + 属性无限叠加。所以只做**状态发生变化**的那一次：
        //   · 目标进入光环范围（或光环刚上场）→ 施加一次（实例同时交给动作线的「目标增益」口）
        //   · 目标离开范围 / 载体离场·被封印·变形 / 该实例被驱散 → 精确移除该来源的实例
        // 逐实例记账（CardBuff.source_uid + source_group）保证：只删光环自己给的那份，
        // 手动施加的同名增益、另一张光环卡给的同名增益都不受影响。
        [System.NonSerialized] private bool aura_syncing = false;   //重入保护：动作线自身也可能改动区域

        /// <summary>正在执行的光环动作线"刚施加"的增益实例，供图的「目标增益」输出口读取
        /// （NodeDocRunner.ResolveInputBuff → 入口 target_buff 口）。</summary>
        [System.NonSerialized] public CardBuff aura_grant_buff = null;

        /// <summary>光环诊断日志（节流：最多 200 条，避免每帧刷屏；排查用，不影响逻辑）</summary>
        private static int aura_log_left = 200;

        /// <summary>已打过"被生效条件挡下"日志的光环节点（每个光环只报一次，避免逐目标刷屏）</summary>
        [System.NonSerialized] private HashSet<string> aura_cond_logged;

        /// <summary>增益实例的变量列表文本（诊断用）</summary>
        private static string PropsText(CardBuff b)
        {
            if (b == null || b.props == null || b.props.Count == 0)
                return "[]";
            System.Text.StringBuilder sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < b.props.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(b.props[i].key).Append('=').Append(b.props[i].value);
            }
            return sb.Append(']').ToString();
        }
        private static void AuraLog(string msg)
        {
            if (aura_log_left <= 0)
                return;
            aura_log_left--;
            Debug.Log("[光环] " + msg + (aura_log_left <= 0 ? "（后续光环日志已静音）" : ""));
        }

        /// <summary>同步所有光环的施加状态（差分扫描）。由 SyncPassiveEffects 末尾统一调用。</summary>
        public virtual void SyncAuraEffects()
        {
            if (aura_syncing || game_data == null || game_data.players == null)
                return;
            aura_syncing = true;
            try
            {
                //1) 算出"此刻应当存在"的光环增益：key = 目标uid|来源卡uid|分组键
                HashSet<string> should = new HashSet<string>();
                List<AuraGrant> grants = new List<AuraGrant>();
                CollectAuraGrants(should, grants);
                if (grants.Count > 0)
                    AuraLog("本次应有 " + grants.Count + " 个目标（目标|来源|分组 组合数）");

                //2) 移除"不该再存在"的：扫全部区域的卡，只动 aura 来源的实例（普通增益一律不碰）
                for (int i = 0; i < game_data.players.Length; i++)
                {
                    Player p = game_data.players[i];
                    if (p == null)
                        continue;
                    RemoveStaleAuraBuffsOnCard(p.hero, should);
                    RemoveStaleAuraBuffs(p.cards_board, should);
                    RemoveStaleAuraBuffs(p.cards_equip, should);
                    RemoveStaleAuraBuffs(p.cards_hand, should);
                    RemoveStaleAuraBuffs(p.cards_deck, should);
                    RemoveStaleAuraBuffs(p.cards_discard, should);
                    RemoveStaleAuraBuffs(p.cards_secret, should);
                    RemoveStaleAuraBuffs(p.cards_temp, should);
                }

                //3) 补上缺口：施加 + 跑该光环的动作线（动作线可以为空 = 纯数据光环）
                for (int i = 0; i < grants.Count; i++)
                {
                    AuraGrant g = grants[i];
                    CardBuff exist = BuffRuntime.GetAuraBuff(g.target, g.source.uid, g.ability.aura_group);
                    if (exist != null)
                    {
                        //★已在身上：动作线若全是**可重算的幂等动作**（设置属性 / 设置增益属性 等），就按当前状态再算一遍。
                        //  否则动作线算出来的值会永远停在"第一次施加的那一刻" —— 表现就是
                        //  "卡牌写每受到1点伤害费用降低1点，但费用始终不变"（实测踩到）。
                        //  非幂等动作线（伤害/抽牌/召唤…）仍然只在进入范围时执行一次，不会每帧重复发生。
                        if (g.ability.aura_repeat)
                            RunAuraActionLine(g, exist);
                        continue;
                    }
                    BuffData define = BuffPoolIO.Get(g.ability.aura_buff);
                    if (define == null)
                    {
                        AuraLog("光环增益定义找不到：aura_buff=" + g.ability.aura_buff + "（buffs.json 里没这条增益）");
                        continue;
                    }
                    AuraLog("光环施加：" + define.id + " → 目标卡 " + (g.target != null ? g.target.CardData?.id : "?")
                        + "（来源 " + (g.source != null ? g.source.CardData?.id : "?") + " 分组 " + g.ability.aura_group + "）");
                    CardBuff inst = BuffRuntime.AddAuraBuff(this, g.target, define,
                        define.duration, g.source.uid, g.ability.aura_group);
                    if (inst == null)
                        continue;   //被「添加增益时」阻止 → 下次扫描重试
                    RunAuraActionLine(g, inst);
                    //★诊断（一行定位"光环好像没生效"）：把目标卡**此刻的真实状态**打出来。
                    //   缺 关键词对应状态（如冲锋=Haste）⇒ 问题在"增益关键词→状态"没落地；
                    //   有 Haste ⇒ 光环已生效，"打不了"另有原因（攻击规则图/嘲讽/召唤失调残留）。
                    AuraLog("施加后目标状态：" + AuraStatusText(g.target));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[光环效果] 施加/移除同步异常（已忽略，不影响对局）：" + e);
            }
            finally
            {
                aura_syncing = false;
            }
        }

        /// <summary>目标卡当前状态文本（排查用：例 "SummonDisorder(1), Haste(0)"）</summary>
        private static string AuraStatusText(Card card)
        {
            if (card == null)
                return "?";
            if (card.status == null || card.status.Count == 0)
                return "(无状态)";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < card.status.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(card.status[i].type).Append('(').Append(card.status[i].value).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>一次"目标该被某光环施加"的记录</summary>
        private class AuraGrant
        {
            public Card source;         //光环载体（能力所在卡）
            public AbilityData ability; //光环能力（aura_group / aura_buff）
            public Card target;         //被施加的卡
        }

        private static string AuraKey(Card target, string source_uid, string group)
        {
            return (target != null ? target.uid : "?") + "|" + source_uid + "|" + group;
        }

        /// <summary>收集当前所有"应当生效"的光环目标（来源卡必须在场上/装备区/英雄位）</summary>
        private void CollectAuraGrants(HashSet<string> should, List<AuraGrant> grants)
        {
            for (int i = 0; i < game_data.players.Length; i++)
            {
                Player p = game_data.players[i];
                if (p == null)
                    continue;
                CollectAuraGrantsFrom(p.hero, should, grants);
                CollectAuraGrantsFromList(p.cards_board, should, grants);
                CollectAuraGrantsFromList(p.cards_equip, should, grants);
                //★手牌也能当光环来源：手牌里的卡给自己/己方手牌持续施加增益（如"英雄每损失1点生命，此牌费用-1"）
                //  是很常见的写法；不扫手牌的话这类光环**永远不生效**（实测踩到）。
                CollectAuraGrantsFromList(p.cards_hand, should, grants);
            }
        }

        private void CollectAuraGrantsFromList(List<Card> carriers, HashSet<string> should, List<AuraGrant> grants)
        {
            if (carriers == null)
                return;
            for (int i = 0; i < carriers.Count; i++)
                CollectAuraGrantsFrom(carriers[i], should, grants);
        }

        private void CollectAuraGrantsFrom(Card src, HashSet<string> should, List<AuraGrant> grants)
        {
            if (src == null || !src.CanDoAbilities())
                return;   //被封印的来源：整组光环停用（解封后下一轮自动补上）
            List<AbilityData> abs = src.GetAbilities();
            if (abs == null)
                return;
            for (int i = 0; i < abs.Count; i++)
            {
                AbilityData ab = abs[i];
                if (ab == null || ab.trigger != AbilityTrigger.Ongoing)
                    continue;
                if (string.IsNullOrEmpty(ab.aura_group) || string.IsNullOrEmpty(ab.aura_buff))
                    continue;   //不是"具名增益"型光环（旧式 StatusType 光环不走这里）
                if (!IsCardInZone(src, ab.aura_zone))
                    continue;   //载体不在「生效区域」→ 本光环不生效（离开该区域即整组失效）
                CollectAuraTargets(src, ab, should, grants);
            }
        }

        /// <summary>卡是否处于指定区域（ZoneNames 口径；空/「任意」= 不限区域）。
        /// 手牌/牌库/墓地/装备区/奥秘区/暂存区按玩家区域表判定；「英雄」= 该玩家英雄位。</summary>
        private bool IsCardInZone(Card card, string zone)
        {
            if (card == null)
                return false;
            if (string.IsNullOrEmpty(zone) || zone == "任意")
                return true;
            Player p = game_data != null ? game_data.GetPlayer(card.player_id) : null;
            if (p == null)
                return false;
            switch (zone)
            {
                case "英雄": return p.hero == card;
                case "战场":
                case "场上": return p.cards_board != null && p.cards_board.Contains(card);
                case "手牌":
                case "初始手牌": return p.cards_hand != null && p.cards_hand.Contains(card);
                case "牌库": return p.cards_deck != null && p.cards_deck.Contains(card);
                case "墓地": return p.cards_discard != null && p.cards_discard.Contains(card);
                case "装备区":
                case "装备": return p.cards_equip != null && p.cards_equip.Contains(card);
                case "奥秘区":
                case "奥秘": return p.cards_secret != null && p.cards_secret.Contains(card);
                case "暂存区":
                case "延迟": return p.cards_temp != null && p.cards_temp.Contains(card);
                default: return false;   //未知区域判否（不静默当成某个区域）
            }
        }

        /// <summary>按光环入口的「作用区域」（=被施加增益的卡所在区域）枚举目标</summary>
        private void CollectAuraTargets(Card src, AbilityData ab, HashSet<string> should, List<AuraGrant> grants)
        {
            for (int i = 0; i < game_data.players.Length; i++)
            {
                Player tp = game_data.players[i];
                if (tp == null)
                    continue;
                if (!string.IsNullOrEmpty(ab.aura_target_zone))
                {
                    AddAuraTargetsByName(src, ab, tp, ab.aura_target_zone, should, grants);
                    continue;
                }
                switch (ab.target)
                {
                    case AbilityTarget.AllCardsHand:
                        AddAuraTargets(src, ab, tp.cards_hand, should, grants);
                        break;
                    case AbilityTarget.AllCardsAllPiles:
                        AddAuraTargets(src, ab, tp.cards_board, should, grants);
                        AddAuraTargets(src, ab, tp.cards_equip, should, grants);
                        AddAuraTargets(src, ab, tp.cards_hand, should, grants);
                        AddAuraTargets(src, ab, tp.cards_deck, should, grants);
                        AddAuraTargets(src, ab, tp.cards_discard, should, grants);
                        AddAuraTargets(src, ab, tp.cards_secret, should, grants);
                        AddAuraTargets(src, ab, tp.cards_temp, should, grants);
                        break;
                    default:   //战场（入口默认）
                        AddAuraTargets(src, ab, tp.cards_board, should, grants);
                        break;
                }
            }
        }

        /// <summary>按区域名把该玩家对应区域的卡纳入光环候选（区域名 = ZoneNames 口径；空/「任意」= 全部区域）</summary>
        private void AddAuraTargetsByName(Card src, AbilityData ab, Player tp, string zone,
            HashSet<string> should, List<AuraGrant> grants)
        {
            if (tp == null)
                return;
            switch (zone)
            {
                case "英雄":
                    AddAuraTargetsOne(src, ab, tp.hero, should, grants);
                    break;
                case "战场":
                case "场上":
                    AddAuraTargets(src, ab, tp.cards_board, should, grants);
                    break;
                case "手牌":
                case "初始手牌":
                    AddAuraTargets(src, ab, tp.cards_hand, should, grants);
                    break;
                case "牌库":
                    AddAuraTargets(src, ab, tp.cards_deck, should, grants);
                    break;
                case "墓地":
                    AddAuraTargets(src, ab, tp.cards_discard, should, grants);
                    break;
                case "装备区":
                case "装备":
                    AddAuraTargets(src, ab, tp.cards_equip, should, grants);
                    break;
                case "奥秘区":
                case "奥秘":
                    AddAuraTargets(src, ab, tp.cards_secret, should, grants);
                    break;
                case "暂存区":
                case "延迟":
                    AddAuraTargets(src, ab, tp.cards_temp, should, grants);
                    break;
                default:   //任意 / 全部区域
                    AddAuraTargetsOne(src, ab, tp.hero, should, grants);
                    AddAuraTargets(src, ab, tp.cards_board, should, grants);
                    AddAuraTargets(src, ab, tp.cards_equip, should, grants);
                    AddAuraTargets(src, ab, tp.cards_hand, should, grants);
                    AddAuraTargets(src, ab, tp.cards_deck, should, grants);
                    AddAuraTargets(src, ab, tp.cards_discard, should, grants);
                    AddAuraTargets(src, ab, tp.cards_secret, should, grants);
                    AddAuraTargets(src, ab, tp.cards_temp, should, grants);
                    break;
            }
        }

        /// <summary>单张卡作为光环候选（英雄位用）</summary>
        private void AddAuraTargetsOne(Card src, AbilityData ab, Card one, HashSet<string> should, List<AuraGrant> grants)
        {
            if (one == null)
                return;
            if (!ab.AreTargetConditionsMet(game_data, src, one))
                return;
            if (!IsAuraConditionMet(src, ab, one))
                return;
            string key = AuraKey(one, src.uid, ab.aura_group);
            if (!should.Add(key))
                return;
            AuraGrant g = new AuraGrant();
            g.source = src;
            g.ability = ab;
            g.target = one;
            grants.Add(g);
        }

        private void AddAuraTargets(Card src, AbilityData ab, List<Card> zone, HashSet<string> should, List<AuraGrant> grants)
        {
            if (zone == null)
                return;
            for (int i = 0; i < zone.Count; i++)
            {
                Card t = zone[i];
                if (t == null)
                    continue;
                if (!ab.AreTargetConditionsMet(game_data, src, t))
                {
                    AuraLog("目标 " + (t.CardData != null ? t.CardData.id : "?") + " 被「作用区域」条件挡下"
                        + "（conditions_target=" + (ab.conditions_target != null ? ab.conditions_target.Length : 0) + " 条）");
                    continue;   //作用区域（己方/敌方/双方）等目标条件
                }
                if (!IsAuraConditionMet(src, ab, t))
                {
                    //★这条最容易造成"光环完全不生效却毫无提示"：生效条件（cond 口）求值为假时，该目标不施加。
                    //  逐目标打会刷屏 → 每个光环节点只报一次（列出被挡的第一个目标做样例）。
                    if (aura_cond_logged == null)
                        aura_cond_logged = new HashSet<string>();
                    string ck = (src.uid ?? "?") + "|" + ab.id;
                    if (aura_cond_logged.Add(ck))
                    {
                        AuraLog("目标 " + (t.CardData != null ? t.CardData.id : "?") + " 等被「生效条件」挡下"
                            + "（cond 连线的布尔源求值为假 → 这些目标不施加；不需要条件就把这条线去掉=放行）");
                    }
                    continue;   //入口「生效条件」口（cond，无连线=放行）
                }
                string key = AuraKey(t, src.uid, ab.aura_group);
                if (!should.Add(key))
                    continue;   //同一来源同一分组只算一次
                AuraGrant g = new AuraGrant();
                g.source = src;
                g.ability = ab;
                g.target = t;
                grants.Add(g);
            }
        }

        /// <summary>光环入口「生效条件」口求值（图 cond；没接图/没连线 = 放行）</summary>
        private bool IsAuraConditionMet(Card src, AbilityData ab, Card target)
        {
            EffectRunGraph run = GetAbilityRunGraph(ab);
            if (run == null || run.graph == null)
                return true;
            return NodeDocRunner.IsEntryConditionMet(this, run.graph, "AuraEffect", src, target, null);
        }

        /// <summary>取能力携带的规则图（光环的增益动作线 / 被动的生效·失效线都用它读入口「生效条件」）；无图返回 null</summary>
        private static EffectRunGraph GetAbilityRunGraph(AbilityData ab)
        {
            if (ab == null || ab.effects == null)
                return null;
            for (int i = 0; i < ab.effects.Length; i++)
            {
                EffectRunGraph run = ab.effects[i] as EffectRunGraph;
                if (run != null)
                    return run;
            }
            return null;
        }

        /// <summary>执行光环的动作线（一次性：目标进入范围时），期间把"刚施加的增益"暴露给「目标增益」口</summary>
        private void RunAuraActionLine(AuraGrant g, CardBuff inst)
        {
            if (g.ability.effects == null || g.ability.effects.Length == 0)
                return;
            aura_grant_buff = inst;
            try
            {
                for (int i = 0; i < g.ability.effects.Length; i++)
                {
                    if (g.ability.effects[i] != null)
                        g.ability.effects[i].DoEffect(this, g.ability, g.source, g.target);
                }
            }
            finally
            {
                aura_grant_buff = null;
            }
            //★诊断（排查用，不影响逻辑）：动作线跑完后，把"写进增益实例的变量"与"目标卡当前费用"一并打出来。
            //  一行即可判断断点：变量有没有写进去 / 写了有没有合进 mana_ongoing。
            AuraLog("动作线执行完毕：" + (g.target != null && g.target.CardData != null ? g.target.CardData.id : "?")
                + " 实例变量=" + PropsText(inst)
                + "｜目标费用=" + (g.target != null ? g.target.GetMana() : -1)
                + " mana_ongoing=" + (g.target != null ? g.target.mana_ongoing : 0));
        }

        /// <summary>清掉一组卡上"来源已不再成立"的光环增益</summary>
        private void RemoveStaleAuraBuffs(List<Card> cards, HashSet<string> should)
        {
            if (cards == null)
                return;
            for (int i = 0; i < cards.Count; i++)
                RemoveStaleAuraBuffsOnCard(cards[i], should);
        }

        /// <summary>清单张卡上"来源已不再成立"的光环增益（按实例精确删除，只动 aura 来源的实例）</summary>
        private void RemoveStaleAuraBuffsOnCard(Card card, HashSet<string> should)
        {
            if (card == null || card.buffs == null || card.buffs.Count == 0)
                return;
            for (int i = card.buffs.Count - 1; i >= 0; i--)
            {
                CardBuff b = card.buffs[i];
                if (b == null || !b.IsFromAura)
                    continue;   //不是光环给的 → 一律不碰
                if (!should.Contains(AuraKey(card, b.source_uid, b.source_group)))
                    BuffRuntime.RemoveBuffInstance(card, b);
            }
        }

        /// <summary>逐区域扫描的一层（集合）</summary>
        private void SyncPassiveCards(Player p, List<Card> cards)
        {
            if (cards == null)
                return;
            for (int i = 0; i < cards.Count; i++)
                SyncPassiveCard(p, cards[i]);
        }

        /// <summary>单张卡：把它「该生效的被动」与「已生效的被动」逐组对比，补差额（生效/失效各触发一次）</summary>
        private void SyncPassiveCard(Player p, Card card)
        {
            if (p == null || card == null)
                return;
            List<AbilityData> abs = card.GetAbilities();
            if (abs == null || abs.Count == 0)
                return;
            //只以「生效」线驱动扫描；同组的「失效」线由 FindPassiveAbility 按分组键找到
            for (int i = 0; i < abs.Count; i++)
            {
                AbilityData ab = abs[i];
                if (ab == null || ab.trigger != AbilityTrigger.OnPassiveEnable || string.IsNullOrEmpty(ab.passive_group))
                    continue;
                //该生效 = 卡在入口「生效区域」内 **且** 入口「生效条件」成立（无连线=放行）。
                //★刻意**不看封印**（Silenced）：封印是引擎在"执行能力"那一层统一拦截的（ResolveCardAbility 开头就 return）。
                //  若把封印也算作"不该生效"，封印瞬间会触发一次失效线——而失效线同样被封印拦掉 →
                //  表现就是"记账已销、动作从未执行"，解封后也不会补（与卡上实际状态对不上）。
                EffectRunGraph prun = GetAbilityRunGraph(ab);
                bool should = IsCardInPassiveArea(p, card, ab.passive_area)
                    && (prun == null || NodeDocRunner.IsEntryConditionMet(this, prun.graph, "PassiveEffect", card, card, null));
                bool is_on = card.passive_groups != null && card.passive_groups.Contains(ab.passive_group);
                if (should == is_on)
                    continue;
                if (should)
                    EnablePassive(card, ab.passive_group);
                else
                    DisablePassive(card, ab.passive_group);
            }
        }

        /// <summary>卡是否处于该被动入口的「生效区域」（口径与入口字段 live_area 一致：
        /// 战场/手牌/牌库/墓地/装备区/全部区域）</summary>
        private static bool IsCardInPassiveArea(Player p, Card card, string area)
        {
            if (p == null || card == null)
                return false;
            switch (area)
            {
                case "手牌": return p.cards_hand != null && p.cards_hand.Contains(card);
                case "牌库": return p.cards_deck != null && p.cards_deck.Contains(card);
                case "墓地": return p.cards_discard != null && p.cards_discard.Contains(card);
                case "装备区": return p.cards_equip != null && p.cards_equip.Contains(card);
                case "全部区域": return true;   //不论在哪个区域都生效
                default: return p.cards_board != null && p.cards_board.Contains(card);   //战场（入口默认）
            }
        }

        /// <summary>让某被动分组「生效」：**先记账再入队** —— 生效线内部若又触发一次扫描，不会重复执行。
        /// 触发条件不满足时同样先记账（否则每次扫描都会重试同一条件，行为不可预期）；封印中由上层判为"不该生效"。</summary>
        public virtual void EnablePassive(Card card, string group)
        {
            if (card == null || string.IsNullOrEmpty(group))
                return;
            if (card.passive_groups == null)
                card.passive_groups = new List<string>();
            if (card.passive_groups.Contains(group))
                return;
            card.passive_groups.Add(group);
            AbilityData ab = FindPassiveAbility(card, group, AbilityTrigger.OnPassiveEnable);
            if (ab != null && ab.AreTriggerConditionsMet(game_data, card, card))
                ab.DoEffects(this, card);   //★同步执行该被动线（口径见下方注释）
        }

        /// <summary>让某被动分组「失效」：**先销账**（无论有没有失效线，都不该再算"已生效"），再触发失效线。</summary>
        public virtual void DisablePassive(Card card, string group)
        {
            if (card == null || string.IsNullOrEmpty(group))
                return;
            if (card.passive_groups != null)
                card.passive_groups.Remove(group);
            AbilityData ab = FindPassiveAbility(card, group, AbilityTrigger.OnPassiveDisable);
            //★失效线是**回滚线**：必须执行，不能被触发条件挡住。
            //  与 NodeDocRunner 里"生效/失效线不受入口 cond 约束"是同一原理 ——
            //  图级 cond（入口口）与能力级 conditions_trigger 是**两个独立的拦截点**，缺一个就泄漏：
            //  条件由真转假时恰恰是"条件不满足"的时刻，若在这里把关，回滚永远不执行
            //  ⇒ 已施加的增益永久残留（EffectRunGraph 的注释早已写明"生效线/失效线必须照跑"）。
            //  记账（passive_groups 移除）在上面已经做完，这里只负责执行回滚。
            if (ab != null)
                ab.DoEffects(this, card);   //同步执行
        }

        /// <summary>让这张卡当前**已生效**的全部被动失效（变形：旧形态整组失效 → 换定义后再按新形态重新生效）</summary>
        public virtual void DisableAllPassiveGroups(Card card)
        {
            if (card == null || card.passive_groups == null || card.passive_groups.Count == 0)
                return;
            List<string> groups = new List<string>(card.passive_groups);   //复制后遍历（DisablePassive 会改本表）
            for (int i = 0; i < groups.Count; i++)
                DisablePassive(card, groups[i]);
        }

        /// <summary>按「分组键 + 触发器」找这张卡上的被动线能力（找不到返回 null = 该线没接线）</summary>
        private static AbilityData FindPassiveAbility(Card card, string group, AbilityTrigger trigger)
        {
            List<AbilityData> abs = card != null ? card.GetAbilities() : null;
            if (abs == null)
                return null;
            for (int i = 0; i < abs.Count; i++)
            {
                AbilityData ab = abs[i];
                if (ab != null && ab.trigger == trigger && ab.passive_group == group)
                    return ab;
            }
            return null;
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

            //★被动效果：变形 = 旧形态整组失效（此刻卡上还挂着旧定义的能力与字段）→ 换定义 → 新形态重新生效。
            //  被「变形时」阻止时上面已 return，所以那种情况"既没有失效也没有生效"，语义干净。
            DisableAllPassiveGroups(card);

            card.SetCard(transform_to, card.VariantData);

            //★换定义后**清空**旧形态的全部分组记账：变形语义 = 旧形态整体失效 → 新形态**全部重新生效**。
            //  不能只清"在新形态里找不到对应能力的分组"，两个原因（探针实测）：
            //   ① 若新旧形态恰好共用分组键，新形态的生效线会被"已记账"挡住 → 永不执行；
            //   ② 更关键：上面的失效线是同步执行的，而图执行收尾（NodeDocRunner.Run 的 finally）会再同步一次 ——
            //      那一刻卡还是旧形态、且仍在生效区域内 → 旧形态的被动会被**自动重新记账**（回马枪）。
            //  整体清空 + 紧接着 SyncPassiveEffects() 才能得到确定行为。
            if (card.passive_groups != null)
                card.passive_groups.Clear();

            SyncPassiveEffects();   //新定义按「生效区域」重新判定（变形后卡还在原区域 → 新形态的被动立即生效）

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
        private static int damage_mitigate_log_left = 40;   //减伤诊断日志的节流计数

        public virtual void DamagePlayer(Card attacker, Player target, int value, bool spell_damage = false)
        {
            int bl_hp_before = target != null ? target.hp : 0;   //对战记录：记录净差值用
            //★英雄侧减伤（护甲 / 免疫）：**直接以玩家为目标的伤害**（节点 target=玩家、法术打脸等）原先完全没有这一层，
            //  护甲只在"以英雄卡为目标"的分支里判过 → 两条路都必须判，否则表现就是"护甲卡对脸没用"。
            if (target != null && target.hero != null)
            {
                int raw = value;
                bool imm = !spell_damage && target.hero.HasStatus(StatusType.Immunity);
                int armor = (!spell_damage && target.hero.HasStatus(StatusType.Armor))
                    ? target.hero.GetStatusValue(StatusType.Armor) : 0;
                if (imm)
                    value = 0;
                if (armor != 0)
                    value = Mathf.Max(value - armor, 0);
                //诊断（节流）：被减伤吃掉时留痕 —— 否则"被打不掉血"完全无迹可寻
                if (value != raw && damage_mitigate_log_left > 0)
                {
                    damage_mitigate_log_left--;
                    Debug.Log("[伤害] 玩家" + target.player_id + " 被减伤：原 " + raw + " → " + value
                        + "（免疫=" + imm + " 护甲=" + armor + " 法术=" + spell_damage + "）"
                        + (damage_mitigate_log_left <= 0 ? "（后续减伤日志已静音）" : ""));
                }
            }

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

            //对战记录：记**净差值**（护甲/免疫/锁血/图事件改值之后仍准确；0 也记 —— "打了没掉血"一眼可见）
            if (!is_ai_predict)
                BattleLog.TargetPlayer(target.player_id, BattleLogKind.Damage, null, null, target,
                    Mathf.Max(bl_hp_before - target.hp, 0));

            //Lifesteal（attacker 可为 null：无来源伤害经英雄路由落到这里）
            if (attacker != null && attacker.HasStatus(StatusType.LifeSteal))
            {
                //★吸血是"治疗"，必须与 HealPlayer 同一口径（那里 clamp 到 hp_max）：旧写法 `hp += x` 无上限
                //  → 满血时吸血会把 hp 顶到 hp_max 之上（血条/“已满血”判断错乱）。
                Player aplayer = game_data.GetPlayer(attacker.player_id);
                if (aplayer != null)
                    aplayer.hp = Mathf.Clamp(aplayer.hp + value, 0, aplayer.hp_max);
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
            int bl_hp_before = target.hp;   //对战记录：治疗净差值用

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

            //对战记录：治疗净差值（满血时治疗=0 也记，能看出"治疗被浪费"）
            if (!is_ai_predict)
                BattleLog.TargetPlayer(target.player_id, BattleLogKind.Heal, null, null, target,
                    Mathf.Max(target.hp - bl_hp_before, 0));

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

            //（对战记录的伤害在下面"Damage"段用净差值记录）

            if (target.HasStatus(StatusType.Invincibility))
                return; //Invincible

            //英雄=卡牌 最小路由：命中 CardType.Hero 的卡时转调玩家版，伤害落回玩家 hp
            //（否则 damage 累加在英雄卡对象上不可见、且英雄不在 board 上永远不会被结算死亡——"打英雄有的行有的不行"的根因）
            //★减伤交给 DamagePlayer（它现在判护甲/免疫）。原先这里直接转发**且不传 spell 标记** →
            //  打到英雄的伤害绕过护甲/免疫/圣盾（实测：英雄带 5 护甲，4 点普通伤害照样打满血）。
            if (IsHeroCard(target))
            {
                Player hero_player = game_data.GetPlayer(target.player_id);
                if (hero_player != null)
                    DamagePlayer(attacker, hero_player, value, spell_damage);
                return;
            }

            if (target.HasStatus(StatusType.SpellImmunity) && attacker.CardData.type != CardType.Character)
                return; //Spell immunity

            //Shell
            bool doublelife = target.HasStatus(StatusType.Shell);
            if (doublelife && value > 0)
            {
                //★ 圣盾被打碎 = **失去**：走 SetStatusPresence(false) 记住"持久移除"。
                //  否则之后任何一次增益重算（BuffRuntime.ReapplyNative 按关键词重建状态）都会把圣盾加回来
                //  —— 表现就是"圣盾碎了下一次增益变化又出现"（实测坐实过）。
                target.SetStatusPresence(StatusType.Shell, false);
                return;
            }

            //★减伤前的伤害量：伤害被图上重定向到**另一张卡**时，必须按新目标重算减伤，
            //  不能沿用"已经从旧目标扣过护甲"的结果（否则新目标护甲被跳过或算两遍）。
            int value_raw = value;

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

                bool graph_set_value = Mathf.Max(dctx.value, 0) != value;   //图是否显式改写了伤害值
                value = Mathf.Max(dctx.value, 0);

                //图上改写（208005 更改受伤卡牌 / 208006 更改伤害源）：按新目标、新来源结算（未改写则保持原值）
                if (dctx.card != null && dctx.card != target)
                {
                    target = dctx.card;

                    //★重定向到"新目标"后，必须重走**新目标自己的**防御判定。旧写法只换 target 就继续
                    //  `target.damage += value` → 新目标的 圣盾/法术免疫/护甲 被**整体跳过**
                    //  （表现：把伤害甩给带圣盾的卡，结果它被打穿、圣盾还没消耗）。
                    if (IsHeroCard(target))
                    {
                        //改到英雄卡上：同样要落回玩家 hp（与上面"英雄=卡牌最小路由"同一口径）
                        Player hp_player = game_data.GetPlayer(target.player_id);
                        if (hp_player != null)
                            DamagePlayer(attacker, hp_player, value, spell_damage);
                        return;
                    }
                    if (target.HasStatus(StatusType.Invincibility))
                        return;     //新目标无敌
                    if (target.HasStatus(StatusType.SpellImmunity) && attacker.CardData.type != CardType.Character)
                        return;     //新目标法术免疫（与上方同一条件）
                    if (target.HasStatus(StatusType.Shell) && value > 0)
                    {
                        //圣盾被打碎=失去：SetStatusPresence(false) 与上方完全同一口径
                        target.SetStatusPresence(StatusType.Shell, false);
                        return;
                    }
                    if (!spell_damage && target.HasStatus(StatusType.Immunity))
                        value = 0;
                    if (!spell_damage && target.HasStatus(StatusType.Armor))
                    {
                        //图显式改过值 → 以图的值再扣新目标护甲；否则回到"减伤前"的值按新目标算
                        value = Mathf.Max((graph_set_value ? value : value_raw) - target.GetStatusValue(StatusType.Armor), 0);
                    }
                }
                if (dctx.source_card != null && dctx.source_card != attacker)
                    attacker = dctx.source_card;
            }

            //Damage
            int damage_max = Mathf.Min(value, target.GetHP());
            int extra = value - target.GetHP();
            target.damage += value;

            //对战记录：净差值 = min(本次伤害, 目标剩余HP)（overkill 不计；0 也记 → "白打"一眼可见）
            if (!is_ai_predict)
                BattleLog.Card(target.player_id, BattleLogKind.Damage, target, null, Mathf.Max(damage_max, 0));

            //Trample（溢出伤害打脸）
            //★必须走 DamagePlayer 统一入口：旧写法 `tplayer.hp -= extra` 会**绕过**英雄护甲/免疫、
            //  不广播「伤害时/后」图事件、不写对战记录、也不做数值收敛
            //  （实测：英雄带 5 护甲照样被溢出伤害打穿，且对战记录里凭空少一段伤害）。
            Player tplayer = game_data.GetPlayer(target.player_id);
            if (!spell_damage && extra > 0 && tplayer != null
                && attacker.player_id == game_data.current_player && attacker.HasStatus(StatusType.Trample))
                DamagePlayer(attacker, tplayer, extra);

            //Lifesteal（吸血）
            //★吸血=治疗，必须 clamp 到 hp_max（与 HealPlayer 同一口径）：旧写法 `hp += damage_max` 无上限
            //  → 满血时吸血会把 hp 顶到 hp_max 之上。
            Player player = game_data.GetPlayer(attacker.player_id);
            if (!spell_damage && player != null && attacker.HasStatus(StatusType.LifeSteal))
                player.hp = Mathf.Clamp(player.hp + damage_max, 0, player.hp_max);

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

            //对战记录：确认真的会死之后再记（上面两个 return 已挡掉"已死/免疫"）
            if (!is_ai_predict && target.CardData != null && target.CardData.type == CardType.Character)
                BattleLog.Card(target.player_id, BattleLogKind.Death, target);

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
                //★增益承载的"遗言"：卡身上带效果图的增益也要在死亡时跑一次
                //  （增益图原本没有死亡触发点，导致"给仆从挂遗言"的画法永不生效；另外死亡不走"移除增益"事件）。
                //  必须放在这里：此刻 cards_to_clear 还没 Clear，card.buffs 仍在。
                BuffRuntime.TriggerCarrierDeath(this, card);
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

            //★被动效果「失效」同步：卡已从所有区域移入墓地/消失 → 不在生效区域 → 执行它的「失效动作」线。
            //放在最后（亡语与「死亡后」图都发完之后），保证执行顺序是"亡语 → 死亡后 → 被动失效"。
            SyncPassiveEffects();
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

            //★全局入口（作用范围=全部卡牌）：**发动主体不受限** —— 任何卡触发这个时机都要跑它们。
            //  旧数据没有作用范围字段 → 不算全局（内置卡迁移图仍是"卡自身"，不会互相触发）。
            bool prev_guard = global_entries_guard;
            try
            {
                if (!prev_guard)
                {
                    global_entries_guard = true;
                    RunGlobalEntries(type, caster, triggerer, null);
                }

                Card equipped = game_data.GetEquipCard(caster.equipped_uid);
                if (equipped != null)
                    TriggerCardAbilityType(type, equipped, triggerer);   //装备递归里不再重复跑全局入口
            }
            finally
            {
                global_entries_guard = prev_guard;
            }
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

            bool prev_guard = global_entries_guard;
            try
            {
                if (!prev_guard)
                {
                    global_entries_guard = true;
                    RunGlobalEntries(type, caster, null, triggerer);
                }

                Card equipped = game_data.GetEquipCard(caster.equipped_uid);
                if (equipped != null)
                    TriggerCardAbilityType(type, equipped, triggerer);
            }
            finally
            {
                global_entries_guard = prev_guard;
            }
        }

        [System.NonSerialized] private bool global_entries_guard;   //本次时机是否已在跑全局入口（防逐卡循环/装备递归重复执行）

        /// <summary>执行"全局入口"（作用范围=「全部卡牌」的事件类入口）：
        /// 事件发生时，除"本卡自己的能力"外还要跑这些图 —— 任何卡满足条件都算，效果对所有满足条件的目标生效。
        /// caster = 本次事件主体（攻击时=攻击者；被攻击时=被攻击的卡…）；triggerer = 事件的对手（卡/玩家）。
        /// 只跑该入口自己的那条流（Run 的 trigger_action = 入口 action），不会连带跑同图其它入口。</summary>
        private void RunGlobalEntries(AbilityTrigger type, Card caster, Card triggerer, Player triggerer_player)
        {
            if (caster == null)
            {
                //没有"事件主体卡"（例：回合事件但该玩家没有英雄卡）→ 图里「卡牌/目标」口全空，动作可能无源/条件误判。
                //宁可跳过并告警，也不要跑出错误效果（这一类失败以前是完全静默的）。
                Debug.LogWarning("[全局入口] " + type + " 无事件主体卡，跳过全局入口执行");
                return;
            }
            List<Workshop.CardPoolIO.GlobalEntry> list = Workshop.CardPoolIO.GetGlobalEntries(type);
            if (list == null || list.Count == 0)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                Workshop.CardPoolIO.GlobalEntry ge = list[i];
                if (ge == null || ge.graph == null)
                    continue;
                try
                {
                    Workshop.NodeDocRunner.Run(this, ge.graph, caster, triggerer, triggerer_player, ge.action);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[全局入口] " + ge.action + " 执行失败（已忽略，不影响对局）：" + e.Message);
                }
            }
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
                //★ 被卡自身持久移除的关键词（失去圣盾/潜行）：规则图**同样不得触发** ——
                //  与 HasKeyword 保持同一口径，否则会出现"关键词没了但它的规则还在跑"。
                if (caster.IsKeywordRemoved(keyword_id))
                    continue;
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
            bool prev_guard = global_entries_guard;
            global_entries_guard = true;    //逐卡循环：全局入口在循环外只跑一次
            try
            {
                foreach (Player oplayer in game_data.players)
                {
                    if (oplayer.hero != null)
                        TriggerCardAbilityType(type, oplayer.hero, triggerer);

                    foreach (Card card in oplayer.cards_board)
                        TriggerCardAbilityType(type, card, triggerer);
                }
            }
            finally
            {
                global_entries_guard = prev_guard;
            }
            //★全局入口：一次事件只跑一次，主体=事件主体(triggerer)
            RunGlobalEntries(type, triggerer, null, null);
        }

        public virtual void TriggerPlayerCardsAbilityType(Player player, AbilityTrigger type)
        {
            if (player == null)
                return;
            bool prev_guard = global_entries_guard;
            global_entries_guard = true;    //逐卡循环：全局入口在循环外只跑一次
            try
            {
                if (player.hero != null)
                    TriggerCardAbilityType(type, player.hero, player.hero);

                foreach (Card card in player.cards_board)
                    TriggerCardAbilityType(type, card, card);
            }
            finally
            {
                global_entries_guard = prev_guard;
            }
            //★全局入口：一次回合事件只跑一次（主体=英雄，玩家一起传进去）
            RunGlobalEntries(type, player.hero, null, player);
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
            //★被动效果「生效/失效」同步：本方法是"进场/离场/装备/回合开始"的统一收敛点
            //（PlayCard/PlaceCardOnBoard/DiscardCard/StartTurn/AfterAbilityResolved/MoveCard 都会走到这里），
            //放在最后 → 卡的区域已经稳定，扫描到的就是最终状态。
            SyncPassiveEffects();
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
            game_data.turn_timer = (game_data.settings != null && game_data.settings.NoTurnTimer)
                ? GameSettings.NoTurnTimerValue                      //人机/模拟：不下发倒计时（999=GameUI 不显示）
                : GameplayData.Get().turn_duration;
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
