using System.Collections;
using System.Collections.Generic;
using TcgEngine.Client;
using UnityEngine;

namespace TcgEngine
{
    //Contains all gameplay state data that is sync across network

    [System.Serializable]
    public class Game
    {
        public string game_uid;
        public GameSettings settings;

        /// <summary>运行期反向引用（由 GameLogic 构造 / SetData 赋值）。[NonSerialized] 所以不进存档与网络包。
        /// 为什么要它：<c>Game.CanAttackTarget</c> 是 UI 高亮/AI/结算共用的判定入口，规则图的求值需要 GameLogic，
        /// 而 UI 那边只拿得到 Game。</summary>
        [System.NonSerialized] public Gameplay.GameLogic logic;

        //Game state
        public int first_player = 0;
        public int current_player = 0;
        public int turn_count = 0;

        //注：战斗页面按钮栏是**每个玩家各自**的（Player.battle_buttons），因为「增加/删除按钮」节点带玩家输入
        //    （给谁增减按钮），放在 Game 上无法区分玩家。
        public float turn_timer = 0f;

        public GameState state = GameState.Connecting;

        /// <summary>对战记录（影之诗「战斗记录」口径）：随对局同步 ⇒ 客户端 / 观战 / 断线重连天然一致。
        /// ★`Game.Clone` **不要拷它**：AI 每个预测节点都复制一份会爆内存/掉帧（埋点也只记真实对局）。
        /// 写入统一走 BattleLog.Record（唯一入口，内含 is_ai_predict 过滤）。</summary>
        public List<Gameplay.BattleLogEntry> battle_log = new List<Gameplay.BattleLogEntry>();
        public GamePhase phase = GamePhase.None;

        //Players
        public Player[] players;

        //Selector
        public SelectorType selector = SelectorType.None;
        public int selector_player_id = 0;
        public string selector_ability_id;
        public string selector_caster_uid;

        //多目标（顺序逐槽选择）：槽游标 + 计划（图槽号，可空洞）+ 各槽选择结果（uid；null=该槽被跳过/留空）
        public int selector_slot_index = 0;
        public int[] selector_slot_nodes;
        public string[] selector_selected_uids;

        //Other reference values
        public string last_played;
        public string last_target;
        public string last_destroyed;
        public string last_summoned;
        public string last_Added_To_Deck;
        public string ability_triggerer;
        public int rolled_value;
        public int selected_value;

        //Other reference arrays 
        public HashSet<string> ability_played = new HashSet<string>();
        public HashSet<string> cards_attacked = new HashSet<string>();

        public Game() { }
        
        public Game(string uid, int nb_players)
        {
            this.game_uid = uid;
            players = new Player[nb_players];
            for (int i = 0; i < nb_players; i++)
                players[i] = new Player(i);
            settings = GameSettings.Default;
        }

        public virtual bool AreAllPlayersReady()
        {
            int ready = 0;
            foreach (Player player in players)
            {
                if (player.IsReady())
                    ready++;
            }
            return ready >= settings.nb_players;
        }

        public virtual bool AreAllPlayersConnected()
        {
            int ready = 0;
            foreach (Player player in players)
            {
                if (player.IsConnected())
                    ready++;
            }
            return ready >= settings.nb_players;
        }

        //Check if its player's turn
        public virtual bool IsPlayerTurn(Player player)
        {
            return IsPlayerActionTurn(player) || IsPlayerSelectorTurn(player);
        }

        public virtual bool IsPlayerActionTurn(Player player)
        {
            return player != null && current_player == player.player_id 
                && state == GameState.Play && phase == GamePhase.Main && selector == SelectorType.None;
        }

        public virtual bool IsPlayerSelectorTurn(Player player)
        {
            return player != null && selector_player_id == player.player_id 
                && state == GameState.Play && phase == GamePhase.Main && selector != SelectorType.None;
        }

        public virtual bool IsPlayerMulliganTurn(Player player)
        {
            return phase == GamePhase.Mulligan && !player.ready;
        }
        


        ////CharactorTrait
        //public virtual bool HasCharactorTraitOnBoard(Player player,Card card)
        //{
        //    foreach (Card boardCard in player.cards_board )
        //    {
        //        if (boardCard != null && boardCard.CardData.character_trait == card.CardData.character_trait)
        //        {
        //            return true;
        //        }

        //    }
        //    return false;
        //}


        //Check if a card is allowed to be played on slot
        public virtual bool CanPlayCard(Card card, Slot slot, bool skip_cost = false)
        {
            if (card == null)
                return false;

            Player player = GetPlayer(card.player_id);
            //★这里**只能禁"合成的英雄技能定义"**（GameLogic.CreateSynthSkillDef：id 以 hero_skill_ 开头，
            //  它们不是手牌卡 —— 开战时挂到英雄身上变成技能按钮）。
            //  真实技能卡（工作台「卡牌类型」选 技能）应与法术**同款生命周期**：可从手牌打出 → 结算 → 进墓地
            //  （口径见 CardData.cs:20 枚举注释："行为与法术一致（从手牌使用→结算→进墓地）"）。
            //  此前这里一刀切禁掉所有 CardType.Skill → **技能卡永远打不出去**（实测 SkillTypeProbe ②③ FAIL：
            //  掉血0、既不在墓地也不在战场）。顺带一提：工作台类型下拉里 技能 是可选类型（GraphEditorPanel.cs:197），
            //  选了这个类型却打不出去，就是"配了没用"的静默坑。
            if (card.CardData != null && card.CardData.type == CardType.Skill
                && card.CardData.id != null && card.CardData.id.StartsWith(Gameplay.GameLogic.HERO_SKILL_DEF_PREFIX))
                return false; //合成技能定义：不进手牌，只在英雄技能位发动
            if (!skip_cost && !player.CanPayMana(card))
                return false; //Cant pay mana
            if (!player.HasCard(player.cards_hand, card))
                return false; // Card not in hand

            if (player.is_ai && card.CardData.IsDynamicManaCost() && player.mana == 0)
                return false; // AI cant play X-cost card at 0 cost

            if (card.CardData.IsBoardCard())
            {
                if (!slot.IsValid() || IsCardOnSlot(slot))
                    return false;   //Slot already occupied
                if (Slot.GetP(card.player_id) != slot.p)
                    return false; //Cant play on opponent side
                return true;
            }
            if (card.CardData.IsEquipment())
            {
                if (!slot.IsValid())
                    return false;

                Card target = GetSlotCard(slot);
                if (target == null || target.CardData.type != CardType.Character || target.player_id != card.player_id)
                    return false; //Target must be an allied character

                return true;
            }
            if (card.CardData.IsRequireTargetSpell())
            {
                return IsPlayTargetValid(card, slot); //Check play target on slot
            }
            if (card.CardData.type == CardType.Spell || card.CardData.type == CardType.Skill)
            {
                //★技能卡与法术同款：必须有"可触发的打出能力"才允许打出（否则打出去什么也不发生，属静默空放）
                return CanAnyPlayAbilityTrigger(card); //Check if spell will have abilities
            }
            return true;
        }

        //Check if a card from ANY pile (deck/discard/hand/custom pile) can be placed on a board slot.
        //Unlike CanPlayCard, this does NOT require the card to be in hand and does not check mana.
        public virtual bool CanPlaceCardOnBoard(Card card, Slot slot)
        {
            if (card == null || card.CardData == null || !card.CardData.IsBoardCard())
                return false; //Only characters/artifacts can occupy a board slot
            if (!slot.IsValid() || IsCardOnSlot(slot))
                return false; //Slot invalid or already occupied
            if (Slot.GetP(card.player_id) != slot.p)
                return false; //Cant place on opponent side
            return true;
        }

        //Check if a card is allowed to move to slot
        public virtual bool CanMoveCard(Card card, Slot slot, bool skip_cost = false)
        {
            if (card == null || !slot.IsValid())
                return false;

            if (!IsOnBoard(card))
                return false; //Only cards in play can move

            if (!card.CanMove(skip_cost))
                return false; //Card cant move

            if (Slot.GetP(card.player_id) != slot.p)
                return false; //Card played wrong side

            if (card.slot == slot)
                return false; //Cant move to same slot

            Card slot_card = GetSlotCard(slot);
            if (slot_card != null)
                return false; //Already a card there

            return true;
        }

        //Check if a card is allowed to attack a player
        public virtual bool CanAttackTarget(Card attacker, Player target, bool skip_cost = false)
        {
            return CanAttackTarget(attacker, target, skip_cost, out _);
        }

        /// <summary>同上，但给出"为什么不能攻击"（UI 提示 / 日志 / 控制台直接可读）。
        /// ★这里是"卡牌自定义攻击规则"的唯一判定入口：UI 高亮(BoardSlot/BoardSlotPlayer)、AI 选目标、
        ///   真实结算(GameLogic.StartAttack) 都走它 —— 加规则只需改这一处，三处自动一致。</summary>
        public virtual bool CanAttackTarget(Card attacker, Player target, bool skip_cost, out string reason)
        {
            reason = null;
            if (attacker == null || target == null)
            {
                reason = "目标不存在";
                return false;
            }

            if (!attacker.CanAttack(skip_cost))
            {
                reason = "该卡本回合无法攻击（未就绪 / 召唤失调 / 被禁锢）";
                return false; //Card cant attack
            }

            if (attacker.player_id == target.player_id)
            {
                reason = "不能攻击己方";
                return false; //Cant attack same player
            }

            if (!IsOnBoard(attacker) || !attacker.CardData.IsCharacter())
            {
                reason = "只能由战场上的角色发起攻击";
                return false; //Cards not on board
            }

            if (target.HasStatus(StatusType.Protected) && !attacker.HasStatus(StatusType.Flying))
            {
                reason = "被嘲讽保护（需要 飞行 / 无视嘲讽）";
                return false; //Protected by taunt
            }

            return CanAttackTargetByRules(attacker, null, target, out reason);
        }

        //Check if a card is allowed to attack another one
        public virtual bool CanAttackTarget(Card attacker, Card target, bool skip_cost = false)
        {
            return CanAttackTarget(attacker, target, skip_cost, out _);
        }

        /// <summary>同<see cref="CanAttackTarget(Card,Player,bool,string)"/>，目标为随从。</summary>
        public virtual bool CanAttackTarget(Card attacker, Card target, bool skip_cost, out string reason)
        {
            reason = null;
            if (attacker == null || target == null)
            {
                reason = "目标不存在";
                return false;
            }

            if (!attacker.CanAttack(skip_cost))
            {
                reason = "该卡本回合无法攻击（未就绪 / 召唤失调 / 被禁锢）";
                return false; //Card cant attack
            }

            if (attacker.player_id == target.player_id)
            {
                reason = "不能攻击己方";
                return false; //Cant attack same player
            }

            if (!IsOnBoard(attacker) || !IsOnBoard(target))
            {
                reason = "双方都必须在战场上";
                return false; //Cards not on board
            }

            if (!attacker.CardData.IsCharacter() || !target.CardData.IsBoardCard())
            {
                reason = "只能由角色攻击场上的卡牌";
                return false; //Only character can attack
            }

            if (target.HasStatus(StatusType.Stealth))
            {
                reason = "目标是潜行单位";
                return false; //Stealth cant be attacked
            }

            if (target.HasStatus(StatusType.Protected) && !attacker.HasStatus(StatusType.Flying))
            {
                reason = "被嘲讽保护（需要 飞行 / 无视嘲讽）";
                return false; //Protected by adjacent card
            }

            return CanAttackTargetByRules(attacker, target, null, out reason);
        }

        //=========================================================
        //  攻击目标规则（**精简版**：规则 = 一张图，图里放「攻击限制」/「被攻击限制」入口）
        //  唯一语义：入口的 cond 口为真 = 允许这次攻击；为假 = 拒绝（原因取入口字段 title）。
        //  条件一律用现有节点拼（类型/关键词/攻击力/生命/比较/逻辑运算），引擎不内置规则枚举。
        //  ★只读：本方法被 UI 高亮调用（每次选卡对场上每个槽），绝不允许修改任何状态。
        //=========================================================

        [System.ThreadStatic] private static int attack_rule_depth;   //递归守卫：规则图里若又去问"合法攻击目标"，直接放行
        [System.NonSerialized] private Gameplay.GameLogic view_logic;          //客户端副本的只读求值上下文（懒建）
        [System.NonSerialized] private bool attack_rule_error_logged;         //规则求值报错只报一次

        /// <summary>攻击目标规则判定：分别问"攻击者的&lt;攻击限制&gt;图"与"被攻击者的&lt;被攻击限制&gt;图"。
        /// 没配图 → 直接放行（零开销，等价原有行为）。
        /// ★只读：求值走 NodeDocRunner.IsEntryConditionMet（只算入口 cond 口，不执行任何动作节点），
        ///   所以 UI 每次选卡对全场每槽调用它也不会改到状态。</summary>
        public bool CanAttackTargetByRules(Card attacker, Card target_card, Player target_player, out string reason)
        {
            reason = null;
            //攻击玩家时"目标卡"为空 → 用该玩家的英雄卡代入（图里"目标是英雄/随从"永远有卡可判）
            Card defend_card = target_card != null ? target_card : (target_player != null ? target_player.hero : null);

            Workshop.GraphData a_graph = attacker != null && attacker.CardData != null ? attacker.CardData.attack_graph : null;
            Workshop.GraphData d_graph = defend_card != null && defend_card.CardData != null ? defend_card.CardData.attack_graph : null;
            int g_atk = Workshop.CardPoolIO.GlobalAttackGraphs.Count;     //★全局规则（作用范围=全部卡牌）
            int g_def = Workshop.CardPoolIO.GlobalDefendGraphs.Count;
            if (a_graph == null && d_graph == null && g_atk == 0 && g_def == 0)
                return true;    //没配规则：保持原有行为（零开销）

            //图求值需要一个 GameLogic 上下文。**客户端手里是快照副本**（GameClient.cs:485 反序列化而来，
            //[NonSerialized] 的 logic 必然是 null）→ 懒建一个"只读求值上下文"：它只读这份快照
            //（卡/状态/数值与界面显示完全一致），且不会被 Update 驱动 → 不会改任何状态。
            Gameplay.GameLogic ctx = logic;
            if (ctx == null)
            {
                //★只能在**主线程**建这个只读求值上下文：
                //  AI 推演跑在后台线程（GameLogic 里 `MainThreadUtil.IsMainThread` / CreateSynthSkillDef 都是为这条规则加的），
                //  而 AI 用的是 is_ai_predict 快照 —— 按 SetGameBackRef 的守卫它不会拿到 logic ⇒ **会走到这里**。
                //  在后台线程构造 GameLogic（含 ScriptableObject 缓存等）就是项目明令禁止的用法。
                //  拿不到上下文时**放行**（与"没配规则"同口径，fail-open）：AI 侧宁可少一层限制，
                //  也不能在后台线程碰 Unity API；主线程（UI 高亮）一跑起来就会把它建好并缓存，AI 之后自然复用。
                if (!MainThreadUtil.IsMainThread)
                    return true;
                if (view_logic == null || view_logic.GameData != this)
                    view_logic = new Gameplay.GameLogic(this);
                ctx = view_logic;
            }
            if (ctx == null || attack_rule_depth > 0)
                return true;    //拿不到上下文（极端情况）/ 递归 → 放行，绝不递归下探

            attack_rule_depth++;
            try
            {
                //① 攻击者侧：该卡「攻击时」入口上的【攻击限制】口（cond=触发条件，两者语义不同，读的是 limit 口）
                if (a_graph != null &&
                    !Workshop.NodeDocRunner.IsEntryPortMet(ctx, a_graph, "OnAttack", "limit",
                        attacker, defend_card, target_player))
                {
                    reason = Workshop.NodeDocRunner.GetEntryRejectText(a_graph, "OnAttack", "该卡不能攻击此目标", "limit");
                    return false;
                }

                //② 被攻击者侧：该卡「被攻击时」入口上的【被攻击限制】口
                Player atk_player = attacker != null ? GetPlayer(attacker.player_id) : null;
                if (d_graph != null)
                {
                    if (!Workshop.NodeDocRunner.IsEntryPortMet(ctx, d_graph, "OnBeforeDefend", "limit",
                            defend_card, attacker, atk_player))
                    {
                        reason = Workshop.NodeDocRunner.GetEntryRejectText(d_graph, "OnBeforeDefend", "该目标不能被攻击", "limit");
                        return false;
                    }
                }

                //③ 全局规则（入口「作用范围 = 全部卡牌」）：任何攻击/被攻击都要满足，含 AI 的卡 ——
                //   配一次就整局生效，不用每张卡配。典型用法：英雄不能被任何卡攻击（条件：目标是英雄 → 假）。
                for (int i = 0; i < Workshop.CardPoolIO.GlobalAttackGraphs.Count; i++)
                {
                    Workshop.GraphData gg = Workshop.CardPoolIO.GlobalAttackGraphs[i];
                    if (!Workshop.NodeDocRunner.IsEntryPortMet(ctx, gg, "OnAttack", "limit",
                            attacker, defend_card, target_player))
                    {
                        reason = Workshop.NodeDocRunner.GetEntryRejectText(gg, "OnAttack", "该攻击被全局规则禁止", "limit");
                        return false;
                    }
                }
                for (int i = 0; i < Workshop.CardPoolIO.GlobalDefendGraphs.Count; i++)
                {
                    Workshop.GraphData gg = Workshop.CardPoolIO.GlobalDefendGraphs[i];
                    if (!Workshop.NodeDocRunner.IsEntryPortMet(ctx, gg, "OnBeforeDefend", "limit",
                            defend_card, attacker, atk_player))
                    {
                        reason = Workshop.NodeDocRunner.GetEntryRejectText(gg, "OnBeforeDefend", "该目标被全局规则保护", "limit");
                        return false;
                    }
                }
            }
            catch (System.Exception e)
            {
                //★规则图求值出错绝不能影响可玩性：放行 + 只报一次（否则会打断 UI 高亮/AI 选目标）
                if (!attack_rule_error_logged)
                {
                    attack_rule_error_logged = true;
                    Debug.LogWarning("[攻击规则] 规则图求值失败（本次放行，不影响对局）：" + e.Message);
                }
                return true;
            }
            finally
            {
                attack_rule_depth--;
            }
            return true;
        }



        public virtual bool CanCastAbility(Card card, AbilityData ability)
        {
            if (ability == null || card == null || (!card.CanDoActivatedAbilities() && ability.exhaust))
                return false; //This card cant cast

            if (ability.trigger != AbilityTrigger.Activate)
                return false; //Not an activated ability

            Player player = GetPlayer(card.player_id);
            if (!player.CanPayAbility(card, ability))
                return false; //Cant pay for ability

            if (!ability.AreTriggerConditionsMet(this, card))
                return false; //Conditions not met

            return true;
        }

        //For choice selector
        public virtual bool CanSelectAbility(Card card, AbilityData ability)
        {
            if (ability == null || card == null || !card.CanDoAbilities())
                return false; //This card cant cast

            Player player = GetPlayer(card.player_id);
            if (!player.CanPayAbility(card, ability))
                return false; //Cant pay for ability

            if (!ability.AreTriggerConditionsMet(this, card))
                return false; //Conditions not met

            return true;
        }

        public virtual bool CanAnyPlayAbilityTrigger(Card card)
        {
            if (card == null)
                return false;
            if (card.CardData.IsDynamicManaCost())
                return true; //Cost not decided so condition could be false

            foreach (AbilityData ability in card.GetAbilities())
            {
                if (ability.trigger == AbilityTrigger.OnPlay && ability.AreTriggerConditionsMet(this, card))
                    return true;
            }
            return false;
        }

        //Check if Player play target is valid, play target is the target when a spell requires to drag directly onto another card
        public virtual bool IsPlayTargetValid(Card caster, Player target)
        {
            if (caster == null || target == null)
                return false;

            foreach (AbilityData ability in caster.GetAbilities())
            {
                if (ability && ability.trigger == AbilityTrigger.OnPlay && ability.target == AbilityTarget.PlayTarget)
                {
                    if (!ability.CanTarget(this, caster, target))
                        return false;
                }
            }
            return true;
        }

        //Check if Card play target is valid, play target is the target when a spell requires to drag directly onto another card
        public virtual bool IsPlayTargetValid(Card caster, Card target)
        {
            if (caster == null || target == null)
                return false;

            foreach (AbilityData ability in caster.GetAbilities())
            {
                if (ability && ability.trigger == AbilityTrigger.OnPlay && ability.target == AbilityTarget.PlayTarget)
                {
                    if (!ability.CanTarget(this, caster, target))
                        return false;
                }
            }
            return true;
        }

        //Check if Slot play target is valid, play target is the target when a spell requires to drag directly onto another card
        public virtual bool IsPlayTargetValid(Card caster, Slot target)
        {
            if (caster == null)
                return false;

            if (target.IsPlayerSlot())
                return IsPlayTargetValid(caster, GetPlayer(target.p)); //Slot 0,0, means we are targeting a player

            Card slot_card = GetSlotCard(target);
            if (slot_card != null)
                return IsPlayTargetValid(caster, slot_card); //Slot has card, check play target on that card

            foreach (AbilityData ability in caster.GetAbilities())
            {
                if (ability && ability.trigger == AbilityTrigger.OnPlay && ability.target == AbilityTarget.PlayTarget)
                {
                    if (!ability.CanTarget(this, caster, target))
                        return false;
                }
            }
            return true;
        }

        public Player GetPlayer(int id)
        {
            if (id >= 0 && id < players.Length)
                return players[id];
            return null;
        }

        public Player GetActivePlayer()
        {
            return GetPlayer(current_player);
        }

        public Player GetOpponentPlayer(int id)
        {
            int oid = id == 0 ? 1 : 0;
            return GetPlayer(oid);
        }

        public Card GetCard(string card_uid)
        {
            foreach (Player player in players)
            {
                Card acard = player.GetCard(card_uid);
                if (acard != null)
                    return acard;
            }
            return null;
        }

        public Card GetBoardCard(string card_uid)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_board)
                {
                    if (card != null && card.uid == card_uid)
                        return card;
                }
            }
            return null;
        }



        public Card GetEquipCard(string card_uid)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_equip)
                {
                    if (card != null && card.uid == card_uid)
                        return card;
                }
            }
            return null;
        }

        public Card GetHandCard(string card_uid)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_hand)
                {
                    if (card != null && card.uid == card_uid)
                        return card;
                }
            }
            return null;
        }

        public Card GetDeckCard(string card_uid)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_deck)
                {
                    if (card != null && card.uid == card_uid)
                        return card;
                }
            }
            return null;
        }

        public Card GetDiscardCard(string card_uid)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_discard)
                {
                    if (card != null && card.uid == card_uid)
                        return card;
                }
            }
            return null;
        }

        public Card GetSecretCard(string card_uid)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_secret)
                {
                    if (card != null && card.uid == card_uid)
                        return card;
                }
            }
            return null;
        }

        public Card GetTempCard(string card_uid)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_temp)
                {
                    if (card != null && card.uid == card_uid)
                        return card;
                }
            }
            return null;
        }

        public Card GetSlotCard(Slot slot)
        {
            foreach (Player player in players)
            {
                foreach (Card card in player.cards_board)
                {
                    if (card != null && card.slot == slot)
                        return card;
                }
            }
            return null;
        }
        
        public virtual Player GetRandomPlayer(System.Random rand)
        {
            Player player = GetPlayer(rand.NextDouble() < 0.5 ? 1 : 0);
            return player;
        }

        public virtual Card GetRandomBoardCard(System.Random rand)
        {
            Player player = GetRandomPlayer(rand);
            return player.GetRandomCard(player.cards_board, rand);
        }

        public virtual Slot GetRandomSlot(System.Random rand)
        {
            Player player = GetRandomPlayer(rand);
            return player.GetRandomSlot(rand);
        }

        public bool IsInHand(Card card)
        {
            return card != null && GetHandCard(card.uid) != null;
        }

        public bool IsOnBoard(Card card)
        {
            return card != null && GetBoardCard(card.uid) != null;
        }

        public bool IsEquipped(Card card)
        {
            return card != null && GetEquipCard(card.uid) != null;
        }

        public bool IsInDeck(Card card)
        {
            return card != null && GetDeckCard(card.uid) != null;
        }

        public bool IsInDiscard(Card card)
        {
            return card != null && GetDiscardCard(card.uid) != null;
        }

        public bool IsInSecret(Card card)
        {
            return card != null && GetSecretCard(card.uid) != null;
        }

        public bool IsInTemp(Card card)
        {
            return card != null && GetTempCard(card.uid) != null;
        }

        //---- 多目标（顺序逐槽选择）的只读查询：服务端结算 / 客户端高亮 / AI 共用 ----

        /// <summary>当前正在选择的图槽号（非多目标选择态返回 0）</summary>
        public int CurrentSelectSlotNode()
        {
            if (selector_slot_nodes == null)
                return 0;
            if (selector_slot_index < 0 || selector_slot_index >= selector_slot_nodes.Length)
                return 0;
            return selector_slot_nodes[selector_slot_index];
        }

        /// <summary>该 uid 是否已被前面的槽选走（同一张卡不可被两个槽选中）</summary>
        public bool IsSlotTargetSelected(string uid)
        {
            if (string.IsNullOrEmpty(uid) || selector_selected_uids == null)
                return false;
            foreach (string s in selector_selected_uids)
            {
                if (s == uid)
                    return true;
            }
            return false;
        }

        /// <summary>已处理完的槽数（UI 显示"已选 x/N"）</summary>
        public int SelectSlotDone()
        {
            return selector_slot_nodes != null ? Mathf.Clamp(selector_slot_index, 0, selector_slot_nodes.Length) : 0;
        }

        /// <summary>本次多目标的槽总数（非多目标返回 0）</summary>
        public int SelectSlotTotal()
        {
            return selector_slot_nodes != null ? selector_slot_nodes.Length : 0;
        }

        public bool IsCardOnSlot(Slot slot)
        {
            return GetSlotCard(slot) != null;
        }

        public bool HasStarted()
        {
            return state != GameState.Connecting;
        }

        public bool HasEnded()
        {
            return state == GameState.GameEnded;
        }

        //Same as clone, but also instantiates the variable (much slower)
        public static Game CloneNew(Game source)
        {
            Game game = new Game();
            Clone(source, game);
            return game;
        }

        //Clone all variables into another var, used mostly by the AI when building a prediction tree
        public static void Clone(Game source, Game dest)
        {
            dest.game_uid = source.game_uid;
            dest.settings = source.settings;

            dest.first_player = source.first_player;
            dest.current_player = source.current_player;
            dest.turn_count = source.turn_count;
            //局内按钮栏在 Player 上（每玩家一份），由 Player.Clone 负责拷贝
            dest.turn_timer = source.turn_timer;
            dest.state = source.state;
            dest.phase = source.phase;

            if (dest.players == null)
            {
                dest.players = new Player[source.players.Length];
                for(int i=0; i< source.players.Length; i++)
                    dest.players[i] = new Player(i);
            }

            for (int i = 0; i < source.players.Length; i++)
                Player.Clone(source.players[i], dest.players[i]);

            dest.selector = source.selector;
            dest.selector_player_id = source.selector_player_id;
            dest.selector_caster_uid = source.selector_caster_uid;
            dest.selector_ability_id = source.selector_ability_id;
            dest.selector_slot_index = source.selector_slot_index;
            dest.selector_slot_nodes = source.selector_slot_nodes != null ? (int[])source.selector_slot_nodes.Clone() : null;
            dest.selector_selected_uids = source.selector_selected_uids != null ? (string[])source.selector_selected_uids.Clone() : null;

            dest.last_destroyed = source.last_destroyed;
            dest.last_played = source.last_played;
            dest.last_target = source.last_target;
            dest.last_summoned = source.last_summoned;
            dest.ability_triggerer = source.ability_triggerer;
            dest.rolled_value = source.rolled_value;
            dest.selected_value = source.selected_value;

            CloneHash(source.ability_played, dest.ability_played);
            CloneHash(source.cards_attacked, dest.cards_attacked);
        }

        public static void CloneHash(HashSet<string> source, HashSet<string> dest)
        {
            dest.Clear();
            foreach (string str in source)
                dest.Add(str);
        }
    }

    [System.Serializable]
    public enum GameState
    {
        Connecting = 0, //Players are not connected
        Play = 20,      //Game is being played
        GameEnded = 99,
    }

    [System.Serializable]
    public enum GamePhase
    {
        None = 0,
        Mulligan = 5,
        StartTurn = 10, //Start of turn resolution
        Main = 20,      //Main play phase
        EndTurn = 30,   //End of turn resolutions
    }

    [System.Serializable]
    public enum SelectorType
    {
        None = 0,
        SelectTarget = 10,
        SelectorCard = 20,
        SelectorChoice = 30,
        SelectorCost = 40,
    }
}