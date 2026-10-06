using System;
using System.Collections.Generic;
using System.Text;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;
using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 战斗内调试控制台（**服务端侧**指令执行器）。
    ///
    /// 为什么必须在服务端执行：客户端 `GameData` 只是服务端下发的只读投影
    /// （客户端发动作 → GameServer 执行 → `RefreshAll()` 覆盖客户端快照），
    /// 在客户端直接改状态下一次快照就会被抹掉。本类由 `GameServer.ReceiveDevCommand` 调用
    /// （人机/模拟=本机 ServerManagerLocal；联机对局由 GameServer 直接拒绝）。
    ///
    /// 输出约定：返回**多行文本**，每行以 `OK `/`ERR `/`WARN `/`INFO ` 开头，
    /// 客户端控制台按前缀着色（见 DevConsoleUI）。
    /// </summary>
    public static class DevCommandExecutor
    {
        public const int MaxGivePerCall = 60;      //单次发卡上限（防手滑）
        public const int MaxListCards = 80;        //cards 指令最多列多少张

        // ---------------- 入口 ----------------

        public static string Execute(GameLogic logic, Game game, Player player, string line)
        {
            if (logic == null || game == null || player == null)
                return Err("对局未就绪（还没进入战斗或已结束）");

            List<string> a = Tokenize(line);
            if (a.Count == 0)
                return Err("空指令（输入 help 看命令表）");

            string cmd = a[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "help": case "?": return CmdHelp(a);
                    case "state": return CmdState(game, player);
                    case "cards": return CmdCards(a);
                    case "card": return CmdCard(a);
                    case "give": return CmdGive(logic, game, player, a);
                    case "giveall": return CmdGiveAll(logic, game, player, a);
                    case "mana": return CmdMana(game, player, a);
                    case "manamax": return CmdManaMax(game, player, a);
                    case "manatotal": return CmdManaTotal(game, player, a);
                    case "hp": return CmdHp(game, player, a);
                    case "dmg": return CmdDmg(logic, game, player, a);
                    case "heal": return CmdHeal(logic, game, player, a);
                    case "atk": return CmdStatus(game, player, a, StatusType.AddAttack, "攻击力");
                    case "hpcard": return CmdStatus(game, player, a, StatusType.AddHP, "生命值");
                    case "armor": return CmdStatus(game, player, a, StatusType.Armor, "护甲");
                    case "draw": return CmdDraw(game, player, a);
                    case "discard": return CmdDiscard(logic, game, player, a);
                    case "shuffle": return CmdShuffle(logic, game, player, a);
                    case "spawn": return CmdSpawn(logic, game, player, a);
                    case "kill": return CmdKill(logic, game, player, a);
                    case "ability": return CmdAbility(logic, game, player, a);
                    case "endturn": case "pass": return CmdEndTurn(logic);
                    case "endgame": return CmdEndGame(logic, game, player, a);
                    case "trigger": return CmdTrigger(logic, game, player, a);
                    case "preview": return CmdPreview(logic, a);
                }
            }
            catch (Exception e)
            {
                return Err("执行异常：" + e.GetType().Name + " " + e.Message);
            }
            return Err("未知指令「" + cmd + "」（输入 help 看命令表）");
        }

        // ---------------- 只读 ----------------

        private static readonly string[][] HelpTable = new string[][]
        {
            new string[] { "help",     "help [指令]",                        "看命令表 / 单条说明" },
            new string[] { "state",    "state",                             "看双方血量·灵力·手牌/牌库/战场一览" },
            new string[] { "cards",    "cards [关键词] [--all]",             "列出卡牌 id 与标题（给 give 用）" },
            new string[] { "card",     "card <卡>",                          "★诊断单张卡：类型/编译出的能力(触发器)/图里的入口动作" },
            new string[] { "give",     "give <卡> [数量=1] [hand|deck|board] [p0|p1]", "发牌到指定区域" },
            new string[] { "giveall",  "giveall [hand|deck|board] [p0|p1]",   "把卡池全部可组卡发过去" },
            new string[] { "mana",     "mana <n> [p0|p1]",                   "设置当前灵力" },
            new string[] { "manamax",  "manamax <n> [p0|p1]",                "设置灵力上限" },
            new string[] { "manatotal","manatotal <n> [p0|p1]",              "设置灵力上限（总）" },
            new string[] { "hp",       "hp <n> [p0|p1]",                     "设置英雄生命值（直接改，不走伤害流程）" },
            new string[] { "dmg",      "dmg <n> [p0|p1]",                    "对英雄造成伤害（走伤害流程）" },
            new string[] { "heal",     "heal <n> [p0|p1]",                   "治疗英雄" },
            new string[] { "atk",      "atk <槽位> <±n> [p0|p1]",            "给战场随从加/减攻击（永久）" },
            new string[] { "hpcard",   "hpcard <槽位> <±n> [p0|p1]",         "给战场随从加/减生命（永久）" },
            new string[] { "armor",    "armor <槽位> <n> [p0|p1]",           "给战场随从加护甲" },
            new string[] { "draw",     "draw [n=1] [p0|p1]",                 "抽牌" },
            new string[] { "discard",  "discard [n=1] [p0|p1]",              "弃掉手牌（从最后一张开始）" },
            new string[] { "shuffle",  "shuffle [p0|p1]",                    "洗牌库" },
            new string[] { "spawn",    "spawn <卡> [x=自动] [p0|p1]",         "直接在战场生成一个随从" },
            new string[] { "kill",     "kill <槽位> [p0|p1]",                "杀死战场随从（走死亡流程）" },
            new string[] { "ability",  "ability <槽位|hero> [序号=0] [p0|p1]","手动发动该卡的第 n 个能力（测规则图）" },
            new string[] { "trigger",  "trigger <事件名> [值] [p0|p1]",       "直接广播一个图事件（如 OnAfterPlay / 使用卡牌后）" },
            new string[] { "preview",  "preview <事件名>",                    "干跑：列出会响应这个事件的卡/图（不执行）" },
            new string[] { "endturn",  "endturn",                            "结束当前回合" },
            new string[] { "endgame",  "endgame <0|1>",                      "强制结束对局（0/1=赢家玩家号）" },
            new string[] { "own",      "own give <卡> [数量=2]",              "（本地）把卡发进**收藏**，可在构筑界面用" },
            new string[] { "log",      "log on|off",                         "（本地）控制台输出是否同时写 Unity Console" },
            new string[] { "clear",    "clear",                              "（本地）清空控制台日志" },
        };

        private static int HelpIndex(string key)
        {
            for (int i = 0; i < HelpTable.Length; i++)
                if (HelpTable[i][0] == key)
                    return i;
            return -1;
        }

        private static string CmdHelp(List<string> a)
        {
            if (a.Count > 1)
            {
                string key = a[1].ToLowerInvariant();
                int idx = HelpIndex(key);
                if (idx < 0)
                    return Err("没有这条指令：" + key);
                return Info(HelpTable[idx][1] + "\n用法：" + HelpTable[idx][2] + "\n（本地指令 own/log/clear 由客户端直接执行）");
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Info("战斗控制台 · 指令表（人机/模拟对局可用；联机对局被服务端拒绝）"));
            for (int i = 0; i < HelpTable.Length; i++)
                sb.AppendLine("INFO " + HelpTable[i][1].PadRight(40) + HelpTable[i][2]);
            sb.AppendLine(Info("目标写法：p0/p1 或 ai/me（默认=自己）；卡可写 id、id 前缀或中文标题"));
            return sb.ToString().TrimEnd();
        }

        private static string CmdState(Game game, Player me)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Info("对局状态" + (game.state == GameState.GameEnded ? "（已结束）" : "")));
            for (int i = 0; i < game.players.Length; i++)
            {
                Player p = game.players[i];
                if (p == null)
                    continue;
                sb.AppendLine("INFO p" + p.player_id + (p == me ? "(我)" : "") + (p.is_ai ? "[AI]" : "")
                    + "  HP " + p.hp + "/" + p.hp_max
                    + "  灵力 " + p.mana + "/" + p.mana_max + "(总" + p.mana_max_total + ")"
                    + "  手牌 " + p.cards_hand.Count
                    + "  牌库 " + p.cards_deck.Count
                    + "  战场 " + p.cards_board.Count
                    + "  墓地 " + p.cards_discard.Count);
                for (int j = 0; j < p.cards_board.Count; j++)
                {
                    Card c = p.cards_board[j];
                    if (c == null)
                        continue;
                    sb.AppendLine("INFO    [x" + c.slot.x + "] " + CardName(c.CardData)
                        + " 攻" + c.attack + " 血" + (c.hp - c.damage) + "/" + c.hp
                        + (c.exhausted ? " (已横置)" : ""));
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static string CmdCards(List<string> a)
        {
            bool all = false;
            string filter = null;
            for (int i = 1; i < a.Count; i++)
            {
                if (a[i] == "--all") all = true;
                else if (filter == null) filter = a[i];
            }

            List<CardData> list = CardData.GetAll();
            StringBuilder sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CardData c = list[i];
                if (c == null || string.IsNullOrEmpty(c.id))
                    continue;
                if (!all && !c.deckbuilding)
                    continue;
                if (filter != null && !Match(c, filter))
                    continue;
                if (shown >= MaxListCards)
                {
                    sb.AppendLine("WARN 结果过多，只列前 " + MaxListCards + " 条（加关键词缩小范围）");
                    break;
                }
                sb.AppendLine("INFO " + c.id.PadRight(24) + CardName(c) + "  费" + c.mana
                    + (c.deckbuilding ? "" : "  [不可组卡]"));
                shown++;
            }
            if (shown == 0)
                return Warn("没有匹配的卡（试试 cards 不带参数；加 --all 连不可组卡的一起列）");
            return sb.ToString().TrimEnd() + "\n" + Info("共 " + shown + " 条");
        }

        /// <summary>诊断单张卡：类型 / 编译出的能力（触发器）/ 图里的入口动作。
        /// 用途：卡"不生效"时一眼分清是**编译侧**（入口动作没映射成触发器 → abilities 为空）
        /// 还是**运行侧**（触发器对了但条件/接线不对）。</summary>
        private static string CmdCard(List<string> a)
        {
            if (a.Count < 2)
                return Err("用法：" + HelpTable[HelpIndex("card")][2]);
            CardData c = ResolveCard(a[1]);
            if (c == null)
                return Err("找不到卡「" + a[1] + "」（用 cards 关键词 找 id）");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Info("卡定义 " + CardName(c) + "  类型=" + TypeName(c.type)
                + " 费=" + c.mana + " 攻=" + c.attack + " 血=" + c.hp + " 可组卡=" + c.deckbuilding));

            AbilityData[] abs = c.abilities;
            if (abs == null || abs.Length == 0)
                sb.AppendLine(Warn("★没有编译出任何能力（abilities 为空）→ 打出/触发时不会有任何动作（多半是入口动作没被 MapGraphTrigger 映射）"));
            else
            {
                sb.AppendLine(Info("编译出的能力 " + abs.Length + " 个："));
                for (int i = 0; i < abs.Length; i++)
                {
                    AbilityData ab = abs[i];
                    sb.AppendLine("INFO   [" + i + "] trigger=" + (ab != null ? ab.trigger.ToString() : "空")
                        + "  title=" + (ab != null ? ab.title : "")
                        + (ab != null && !string.IsNullOrEmpty(ab.passive_area) ? ("  passive_area=" + ab.passive_area) : "")
                        + (ab != null && !string.IsNullOrEmpty(ab.aura_group) ? "  (光环能力)" : ""));
                }
            }

            // 原始图入口（未编译数据）：核对"入口节点到底写了什么 action"
            CardCustomData raw = CardPoolIO.GetCustomData(c.id);
            if (raw == null)
                sb.AppendLine(Warn("不在自定义卡池里（内置卡/未导入）→ 没有原始图数据可查"));
            else
            {
                List<CardEffectData> gs = raw.EnsureEffects();
                int entries = 0;
                for (int g = 0; g < gs.Count; g++)
                {
                    GraphData gr = gs[g] != null ? gs[g].graph : null;
                    if (gr == null || gr.nodes == null)
                        continue;
                    for (int n = 0; n < gr.nodes.Count; n++)
                    {
                        GraphNode node = gr.nodes[n];
                        if (node == null || node.type != GraphNodeType.Event)
                            continue;
                        entries++;
                        sb.AppendLine("INFO   图" + g + " 入口节点：action=" + node.action + "（" + node.title + "）"
                            + "  生效区域=" + GraphRuntime.GetFieldString(node, "zones", "-")
                            + "  标签=" + GraphRuntime.GetFieldString(node, "tags", "-"));
                    }
                }
                if (entries == 0)
                    sb.AppendLine(Warn("图里没有任何事件入口节点 → 无从触发"));
            }
            return sb.ToString().TrimEnd();
        }

        private static string TypeName(CardType t)
        {
            switch (t)
            {
                case CardType.Character: return "随从";
                case CardType.Spell: return "法术";
                case CardType.Secret: return "奥秘(陷阱)";
                case CardType.Skill: return "技能";
                case CardType.Hero: return "英雄";
                case CardType.Artifact: return "神器";
                case CardType.Equipment: return "装备";
                default: return t.ToString();
            }
        }

        // ---------------- 发卡 ----------------

        private static string CmdGive(GameLogic logic, Game game, Player me, List<string> a)
        {
            if (a.Count < 2)
                return Err("用法：" + HelpTable[HelpIndex("give")][2]);
            CardData card = ResolveCard(a[1]);
            if (card == null)
                return Err("找不到卡「" + a[1] + "」（用 cards 关键词 找 id）");

            int count = 1;
            string zone = "hand";
            Player target = me;
            for (int i = 2; i < a.Count; i++)
            {
                if (int.TryParse(a[i], out int n)) count = n;
                else if (IsZone(a[i])) zone = a[i].ToLowerInvariant();
                else if (TryPlayer(game, me, a[i], out Player tp)) target = tp;
            }
            count = Mathf.Clamp(count, 1, MaxGivePerCall);

            int ok = 0;
            for (int i = 0; i < count; i++)
            {
                if (GiveOne(logic, target, card, zone) == null)
                    break;
                ok++;
            }
            if (ok == 0)
                return Err("发放失败（目标区域满了？战场最多 " + Slot.x_max + " 个）");
            return Ok("给 p" + target.player_id + " 的 " + ZoneLabel(zone) + " 发放 " + CardName(card)
                + " ×" + ok + (ok < count ? "（请求 " + count + "，区域不足已截断）" : ""));
        }

        private static string CmdGiveAll(GameLogic logic, Game game, Player me, List<string> a)
        {
            string zone = "hand";
            Player target = me;
            for (int i = 1; i < a.Count; i++)
            {
                if (IsZone(a[i])) zone = a[i].ToLowerInvariant();
                else if (TryPlayer(game, me, a[i], out Player tp)) target = tp;
            }

            List<CardData> list = CardData.GetAll();
            int ok = 0;
            for (int i = 0; i < list.Count && ok < MaxGivePerCall; i++)
            {
                CardData c = list[i];
                if (c == null || string.IsNullOrEmpty(c.id) || !c.deckbuilding)
                    continue;
                if (GiveOne(logic, target, c, zone) != null)
                    ok++;
            }
            return ok > 0
                ? Ok("给 p" + target.player_id + " 的 " + ZoneLabel(zone) + " 发放全部可组卡，共 " + ok + " 张")
                : Warn("没有可发放的卡（卡池为空？）");
        }

        /// <summary>发一张卡到指定区域；区域满则返回 null</summary>
        private static Card GiveOne(GameLogic logic, Player target, CardData card, string zone)
        {
            VariantData variant = VariantData.GetDefault();
            if (zone == "deck")
                return logic.AddCardDeck(target, card, variant);
            if (zone == "board")
            {
                List<Slot> empty = target.GetEmptySlots();
                if (empty.Count == 0)
                    return null;
                return logic.SummonCard(target, card, variant, empty[0]);
            }
            return logic.SummonCardHand(target, card, variant);
        }

        // ---------------- 资源 ----------------

        private static string CmdMana(Game game, Player me, List<string> a)
        {
            int n;
            Player p = ParseValueAndPlayer(game, a, me, out n);
            if (p == null)
                return Err("用法：" + HelpTable[HelpIndex("mana")][2]);
            p.mana = Mathf.Max(0, n);
            if (p.mana > p.mana_max)
                p.mana_max = p.mana;      //灵力比上限大时同步抬高上限（否则 UI 观感是 0/负数）
            return Ok("p" + p.player_id + " 灵力 = " + p.mana + "（上限 " + p.mana_max + "）");
        }

        private static string CmdManaMax(Game game, Player me, List<string> a)
        {
            int n;
            Player p = ParseValueAndPlayer(game, a, me, out n);
            if (p == null)
                return Err("用法：" + HelpTable[HelpIndex("manamax")][2]);
            p.mana_max = Mathf.Max(0, n);
            if (p.mana_max_total < p.mana_max)
                p.mana_max_total = p.mana_max;
            if (p.mana > p.mana_max)
                p.mana = p.mana_max;
            return Ok("p" + p.player_id + " 灵力上限 = " + p.mana_max);
        }

        private static string CmdManaTotal(Game game, Player me, List<string> a)
        {
            int n;
            Player p = ParseValueAndPlayer(game, a, me, out n);
            if (p == null)
                return Err("用法：" + HelpTable[HelpIndex("manatotal")][2]);
            p.mana_max_total = Mathf.Max(0, n);
            return Ok("p" + p.player_id + " 灵力上限(总) = " + p.mana_max_total);
        }

        private static string CmdHp(Game game, Player me, List<string> a)
        {
            int n;
            Player p = ParseValueAndPlayer(game, a, me, out n);
            if (p == null)
                return Err("用法：" + HelpTable[HelpIndex("hp")][2]);
            p.hp = Mathf.Clamp(n, 0, p.hp_max);
            return Ok("p" + p.player_id + " 生命 = " + p.hp + "/" + p.hp_max
                + (p.hp <= 0 ? "（直接改血不触发死亡判定；要触发请用 dmg）" : ""));
        }

        private static string CmdDmg(GameLogic logic, Game game, Player me, List<string> a)
        {
            int n;
            Player p = ParseValueAndPlayer(game, a, me, out n);
            if (p == null)
                return Err("用法：" + HelpTable[HelpIndex("dmg")][2]);
            int amount = Mathf.Max(1, n);
            int before = p.hp;
            Card source = (me != null && me != p) ? me.hero : null;   //伤害来源=发起者英雄（可为 null）
            logic.DamagePlayer(source, p, amount);
            return Ok("p" + p.player_id + " 受到 " + amount + " 点伤害：HP " + before + " → " + p.hp);
        }

        private static string CmdHeal(GameLogic logic, Game game, Player me, List<string> a)
        {
            int n;
            Player p = ParseValueAndPlayer(game, a, me, out n);
            if (p == null)
                return Err("用法：" + HelpTable[HelpIndex("heal")][2]);
            int before = p.hp;
            logic.HealPlayer(p, Mathf.Max(0, n));
            return Ok("p" + p.player_id + " 治疗 " + n + "：HP " + before + " → " + p.hp);
        }

        private static string CmdStatus(Game game, Player me, List<string> a, StatusType type, string label)
        {
            if (a.Count < 3)
            {
                string key = type == StatusType.AddAttack ? "atk" : (type == StatusType.AddHP ? "hpcard" : "armor");
                return Err("用法：" + HelpTable[HelpIndex(key)][2]);
            }
            if (!int.TryParse(a[1], out int x))
                return Err("槽位要写数字（1~" + Slot.x_max + "）\n" + CmdState(game, me));
            if (!int.TryParse(a[2], out int n))
                return Err("数值要写数字（可带负号）");
            Player p = me;
            for (int i = 3; i < a.Count; i++)
                if (TryPlayer(game, me, a[i], out Player tp)) p = tp;

            Card card = FindBoardCard(p, x);
            if (card == null)
                return Err("p" + p.player_id + " 战场 x" + x + " 没有随从");
            card.AddStatus(type, n, 0);       //duration 0 = 永久（见 Card.AddStatus）
            return Ok("p" + p.player_id + " x" + x + " " + CardName(card.CardData) + " " + label + " "
                + (n >= 0 ? "+" : "") + n
                + "（当前 攻" + card.attack + " 血" + (card.hp - card.damage) + "/" + card.hp + "）");
        }

        // ---------------- 牌堆/战场 ----------------

        private static string CmdDraw(Game game, Player me, List<string> a)
        {
            Player p = me;
            int n = 1;
            for (int i = 1; i < a.Count; i++)
            {
                if (int.TryParse(a[i], out int v)) n = v;
                else if (TryPlayer(game, me, a[i], out Player tp)) p = tp;
            }
            int take = Mathf.Clamp(n, 1, 10);
            int before = p.cards_hand.Count;
            int moved = 0;
            for (int i = 0; i < take && p.cards_deck.Count > 0; i++)
            {
                Card c = p.cards_deck[0];
                p.cards_deck.RemoveAt(0);
                if (c != null)
                {
                    p.cards_hand.Add(c);
                    moved++;
                }
            }
            return Ok("p" + p.player_id + " 抽 " + moved + " 张：手牌 " + before + " → " + p.cards_hand.Count
                + "（牌库剩 " + p.cards_deck.Count + "）");
        }

        private static string CmdDiscard(GameLogic logic, Game game, Player me, List<string> a)
        {
            Player p = me;
            int n = 1;
            for (int i = 1; i < a.Count; i++)
            {
                if (int.TryParse(a[i], out int v)) n = v;
                else if (TryPlayer(game, me, a[i], out Player tp)) p = tp;
            }
            int take = Mathf.Clamp(n, 1, 10);
            int done = 0;
            for (int i = 0; i < take && p.cards_hand.Count > 0; i++)
            {
                logic.DiscardCard(p.cards_hand[p.cards_hand.Count - 1]);
                done++;
            }
            return Ok("p" + p.player_id + " 弃掉 " + done + " 张（手牌剩 " + p.cards_hand.Count + "）");
        }

        private static string CmdShuffle(GameLogic logic, Game game, Player me, List<string> a)
        {
            Player p = me;
            for (int i = 1; i < a.Count; i++)
                if (TryPlayer(game, me, a[i], out Player tp)) p = tp;
            logic.ShuffleDeck(p.cards_deck);
            return Ok("p" + p.player_id + " 牌库已洗牌（" + p.cards_deck.Count + " 张）");
        }

        private static string CmdSpawn(GameLogic logic, Game game, Player me, List<string> a)
        {
            if (a.Count < 2)
                return Err("用法：" + HelpTable[HelpIndex("spawn")][2]);
            CardData card = ResolveCard(a[1]);
            if (card == null)
                return Err("找不到卡「" + a[1] + "」（用 cards 关键词 找 id）");

            Player target = me;
            int x = 0;
            for (int i = 2; i < a.Count; i++)
            {
                if (int.TryParse(a[i], out int v)) x = v;
                else if (TryPlayer(game, me, a[i], out Player tp)) target = tp;
            }

            Slot slot;
            if (x >= 1 && x <= Slot.x_max)
            {
                slot = new Slot(x, 1, target.player_id);
                if (target.GetSlotCard(slot) != null)
                    return Err("p" + target.player_id + " x" + x + " 已经有随从了");
            }
            else
            {
                List<Slot> empty = target.GetEmptySlots();
                if (empty.Count == 0)
                    return Err("p" + target.player_id + " 战场已满");
                slot = empty[0];
            }

            Card created = logic.SummonCard(target, card, VariantData.GetDefault(), slot);
            return created != null
                ? Ok("在 p" + target.player_id + " x" + slot.x + " 生成 " + CardName(card))
                : Err("生成失败");
        }

        private static string CmdKill(GameLogic logic, Game game, Player me, List<string> a)
        {
            if (a.Count < 2 || !int.TryParse(a[1], out int x))
                return Err("用法：" + HelpTable[HelpIndex("kill")][2]);
            Player p = me;
            for (int i = 2; i < a.Count; i++)
                if (TryPlayer(game, me, a[i], out Player tp)) p = tp;

            Card card = FindBoardCard(p, x);
            if (card == null)
                return Err("p" + p.player_id + " 战场 x" + x + " 没有随从");
            string name = CardName(card.CardData);
            card.damage = card.hp;                                    //先打到 0 血
            logic.DiscardCard(card, GameLogic.CardDiscardReason.Death); //再走死亡流程（触发死亡事件）
            return Ok("已杀死 p" + p.player_id + " x" + x + " " + name + "（走死亡流程）");
        }

        private static string CmdAbility(GameLogic logic, Game game, Player me, List<string> a)
        {
            Player p = me;
            int index = 0;
            int x = 0;
            bool hero = a.Count < 2;
            for (int i = 1; i < a.Count; i++)
            {
                string t = a[i].ToLowerInvariant();
                if (t == "hero" || t == "英雄") { hero = true; x = 0; }
                else if (int.TryParse(a[i], out int v))
                {
                    if (x == 0 && !hero) x = v;
                    else index = v;
                }
                else if (TryPlayer(game, me, a[i], out Player tp)) p = tp;
            }

            Card card = hero ? p.hero : FindBoardCard(p, x);
            if (card == null)
                return Err("找不到目标（用 ability <槽位> 或 ability hero）");
            AbilityData[] abs = card.CardData != null ? card.CardData.abilities : null;
            if (abs == null || abs.Length == 0)
                return Err(CardName(card.CardData) + " 没有能力（abilities 为空）");
            if (index < 0 || index >= abs.Length)
                return Err("序号越界（该卡有 " + abs.Length + " 个能力，序号 0~" + (abs.Length - 1) + "）");
            AbilityData ability = abs[index];
            logic.CastAbility(card, ability);
            return Ok("已发动 " + CardName(card.CardData) + " 的第 " + index + " 个能力：" + (ability != null ? ability.id : "?"));
        }

        private static string CmdEndTurn(GameLogic logic)
        {
            logic.EndTurn();
            return Ok("已结束当前回合");
        }

        private static string CmdEndGame(GameLogic logic, Game game, Player me, List<string> a)
        {
            if (a.Count < 2 || !int.TryParse(a[1], out int winner))
                return Err("用法：" + HelpTable[HelpIndex("endgame")][2]);
            logic.EndGame(winner);
            return Ok("已强制结束对局，赢家 = 玩家 " + winner);
        }

        // ---------------- 规则图事件（测卡用） ----------------

        private static string CmdTrigger(GameLogic logic, Game game, Player me, List<string> a)
        {
            if (a.Count < 2)
                return Err("用法：" + HelpTable[HelpIndex("trigger")][2]);
            string action = NormalizeEvent(a[1]);
            if (string.IsNullOrEmpty(action))
                return Err("事件名不能为空");

            Player p = me;
            int value = 0;
            for (int i = 2; i < a.Count; i++)
            {
                if (int.TryParse(a[i], out int v)) value = v;
                else if (TryPlayer(game, me, a[i], out Player tp)) p = tp;
            }
            Card subject = p != null ? p.hero : me.hero;

            GraphEventContext ctx = new GraphEventContext();
            ctx.action = action;
            ctx.phase = GraphEventPhase.After;
            ctx.card = subject;
            ctx.player = p;
            ctx.value = value;
            bool cancelled = logic.EmitGraphEvent(ctx, subject);
            return Ok("已广播图事件 " + action + "（主体=" + (subject != null ? CardName(subject.CardData) : "无")
                + "，值=" + value + (cancelled ? "，被「阻止本事件」取消" : "") + "）\n"
                + Info("想先看谁会响应：preview " + action));
        }

        private static string CmdPreview(GameLogic logic, List<string> a)
        {
            if (a.Count < 2)
                return Err("用法：" + HelpTable[HelpIndex("preview")][2]);
            string action = NormalizeEvent(a[1]);
            string report = logic.PreviewEventTriggers(action);
            return string.IsNullOrEmpty(report)
                ? Warn("没有任何卡/图会响应 " + action)
                : Info(report.TrimEnd());
        }

        /// <summary>事件名容错：支持英文 action（OnAfterPlay）与中文标题（使用卡牌后）</summary>
        private static string NormalizeEvent(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;
            switch (s)
            {
                case "使用卡牌后": return "OnAfterPlay";
                case "使用卡牌时": return "OnBeforePlay";
                case "起动后": return "OnAfterActivate";
                case "起动时": return "OnBeforeActivate";
                case "伤害时": return "OnBeforeDamage";
                case "伤害后": return "OnAfterDamage";
                case "死亡时": return "OnBeforeDeath";
                case "死亡后": return "OnAfterDeath";
                case "回合开始": return "OnTurnStart";
                case "回合结束": return "OnTurnEnd";
            }
            return s;
        }

        // ---------------- 解析工具 ----------------

        private static List<string> Tokenize(string line)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(line))
                return list;
            StringBuilder cur = new StringBuilder();
            bool quote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    quote = !quote;
                    continue;
                }
                if (!quote && char.IsWhiteSpace(c))
                {
                    if (cur.Length > 0) { list.Add(cur.ToString()); cur.Length = 0; }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0)
                list.Add(cur.ToString());
            return list;
        }

        private static bool IsZone(string s)
        {
            string t = s.ToLowerInvariant();
            return t == "hand" || t == "deck" || t == "board";
        }

        private static string ZoneLabel(string zone)
        {
            switch (zone)
            {
                case "deck": return "牌库";
                case "board": return "战场";
                default: return "手牌";
            }
        }

        /// <summary>p0/p1/ai/me/玩家号 → Player；识别失败返回 false</summary>
        private static bool TryPlayer(Game game, Player me, string token, out Player result)
        {
            result = null;
            if (string.IsNullOrEmpty(token) || game == null || game.players == null)
                return false;
            string t = token.ToLowerInvariant();
            if (t == "me" || t == "我")
            {
                result = me;
                return me != null;
            }
            if (t == "ai" || t == "对手" || t == "opp")
            {
                for (int i = 0; i < game.players.Length; i++)
                {
                    Player p = game.players[i];
                    if (p != null && p != me)
                    {
                        result = p;
                        return true;
                    }
                }
                return false;
            }
            if (t.Length >= 1 && (t[0] == 'p' || char.IsDigit(t[0])))
            {
                string num = t[0] == 'p' ? t.Substring(1) : t;
                if (int.TryParse(num, out int id))
                {
                    Player p = game.GetPlayer(id);
                    if (p != null)
                    {
                        result = p;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>解析"数值 + 可选目标"（如 mana 20 p1）。没找到数值时返回 null（调用方报用法错误）</summary>
        private static Player ParseValueAndPlayer(Game game, List<string> a, Player me, out int value)
        {
            value = 0;
            bool has_value = false;
            Player target = me;
            for (int i = 1; i < a.Count; i++)
            {
                if (!has_value && int.TryParse(a[i], out int v))
                {
                    value = v;
                    has_value = true;
                    continue;
                }
                if (TryPlayer(game, me, a[i], out Player tp))
                    target = tp;
            }
            return has_value ? target : null;
        }

        private static Card FindBoardCard(Player p, int x)
        {
            if (p == null)
                return null;
            for (int i = 0; i < p.cards_board.Count; i++)
            {
                Card c = p.cards_board[i];
                if (c != null && c.slot.x == x)
                    return c;
            }
            return null;
        }

        /// <summary>卡牌解析：id 精确 → id 前缀（唯一）→ 标题精确/包含</summary>
        private static CardData ResolveCard(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            CardData exact = CardData.Get(token);
            if (exact != null)
                return exact;

            List<CardData> all = CardData.GetAll();
            List<CardData> prefix = new List<CardData>();
            List<CardData> title = new List<CardData>();
            for (int i = 0; i < all.Count; i++)
            {
                CardData c = all[i];
                if (c == null || string.IsNullOrEmpty(c.id))
                    continue;
                if (c.id.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    prefix.Add(c);
                else if (!string.IsNullOrEmpty(c.title) && (c.title == token || c.title.Contains(token)))
                    title.Add(c);
            }
            if (prefix.Count == 1)
                return prefix[0];
            if (prefix.Count > 1)
                return null;      //歧义：让用户写完整 id
            if (title.Count >= 1)
                return title[0];  //标题优先取第一张（含糊时可用完整 id）
            return null;
        }

        private static bool Match(CardData c, string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                return true;
            if (c.id != null && c.id.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(c.title) && c.title.Contains(keyword))
                return true;
            return false;
        }

        private static string CardName(CardData c)
        {
            if (c == null)
                return "(空)";
            return string.IsNullOrEmpty(c.title) ? c.id : (c.title + "(" + c.id + ")");
        }

        // ---------------- 输出前缀（客户端按前缀着色） ----------------

        public const string PrefixOk = "OK ";
        public const string PrefixErr = "ERR ";
        public const string PrefixWarn = "WARN ";
        public const string PrefixInfo = "INFO ";

        private static string Ok(string msg) { return PrefixOk + msg; }
        private static string Err(string msg) { return PrefixErr + msg; }
        private static string Warn(string msg) { return PrefixWarn + msg; }
        private static string Info(string msg) { return PrefixInfo + msg; }
    }
}
