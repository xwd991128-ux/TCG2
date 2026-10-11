using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;

namespace TcgEngine.Probe
{
    /// <summary>【Game / Player 查询层的边界】
    /// 背景：`Card.Create` 在卡牌定义为空时返回 null；写侧有几处不判空就 `card.uid` + 把 null 灌进区域列表。
    /// 更麻烦的是**读侧口径分裂**：`Player.GetHandCard/GetBoardCard/…` 直接 `card.uid`（不判元素），
    /// 而同类 `Game.GetHandCard/GetBoardCard/…` 判了 ⇒ 一个 null 元素让"该玩家"全线崩、"Game 版"却正常，
    /// 极难定位。断言：
    /// ① 区域列表里出现 null 元素时，Player 的 5 个 Get*Card 不许 NRE（与 Game 版同口径）
    /// ② 对照：Game 的 Get*Card 同样安全（它本来就判了）
    /// ③ SummonCardHand(卡牌定义=null)：不崩 + 返回 null + 留告警 + **列表里不出现 null**
    /// ④ AddCardDeck(卡牌定义=null)：同上
    /// ⑤ EffectCreate(卡牌定义=null)：不崩 + 留告警
    /// ⑥ 单人局的随机查询：不崩（旧实现 GetPlayer(1)==null → 下游 NRE）
    /// ⑦ 对照：正常查询/正常随机仍然正确
    /// 触发：建 tools/query_boundary_flag.txt → 进 Play → 写 tools/query_boundary_result.tsv → 自动删标记。
    /// </summary>
    public class QueryBoundaryProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/query_boundary_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/query_boundary_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("QueryBoundaryProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<QueryBoundaryProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();
        private readonly List<string> warns = new List<string>();

        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                warns.Add((msg ?? "") + "|" + type);
        }

        private bool HasWarn(string kw)
        {
            for (int i = 0; i < warns.Count; i++)
                if (warns[i].Contains(kw)) return true;
            return false;
        }

        private string WarnSample()
        {
            if (warns.Count == 0) return "（无告警）";
            string s = warns[0];
            return "（" + warns.Count + " 条，首条：" + (s.Length > 70 ? s.Substring(0, 70) + "…" : s) + "）";
        }

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
            Debug.Log("[查询边界] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            CardData heroData = null;
            List<CardData> chars = new List<CardData>();
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null) continue;
                if (heroData == null && c.type == CardType.Hero) heroData = c;
                if (c.type == CardType.Character) chars.Add(c);
            }
            if (heroData == null || chars.Count < 1)
            {
                sb.AppendLine("SKIP\t缺少英雄/角色卡定义\t");
                return;
            }

            Game g = new Game("probe_query_boundary", 2);
            g.state = GameState.Play;
            g.current_player = 0;
            GameLogic logic = new GameLogic(g);
            Player p0 = g.GetPlayer(0), p1 = g.GetPlayer(1);
            p0.hero = Card.Create(heroData, null, p0, "qb_h0");
            p1.hero = Card.Create(heroData, null, p1, "qb_h1");

            //---- ① 区域列表里有 null 元素：Player 的 5 个 Get*Card 不许 NRE ----
            {
                p0.cards_hand.Add(null);
                p0.cards_board.Add(null);
                p0.cards_equip.Add(null);
                p0.cards_deck.Add(null);
                p0.cards_discard.Add(null);
                string[] broken = new string[0];
                List<string> bad = new List<string>();
                try
                {
                    if (p0.GetHandCard("nope") != null) bad.Add("GetHandCard 返回值异常");
                    if (p0.GetBoardCard("nope") != null) bad.Add("GetBoardCard 返回值异常");
                    if (p0.GetEquipCard("nope") != null) bad.Add("GetEquipCard 返回值异常");
                    if (p0.GetDeckCard("nope") != null) bad.Add("GetDeckCard 返回值异常");
                    if (p0.GetDiscardCard("nope") != null) bad.Add("GetDiscardCard 返回值异常");
                }
                catch (Exception e) { bad.Add(e.GetType().Name + ": " + e.Message); }
                Check("① 区域列表含 null 时 Player.Get*Card 不崩", bad.Count == 0,
                    bad.Count == 0 ? "5 个查询全部安全返回 null" : string.Join(" | ", bad.ToArray()));

                //---- ② 对照：Game 版同样安全 ----
                bool game_ok = true;
                string game_note = "（Game 版安全）";
                try
                {
                    g.GetHandCard("nope");
                    g.GetBoardCard("nope");
                    g.GetEquipCard("nope");
                    g.GetDeckCard("nope");
                    g.GetDiscardCard("nope");
                }
                catch (Exception e) { game_ok = false; game_note = e.GetType().Name; }
                Check("② 对照：Game 版 Get*Card 本来就安全", game_ok, game_note);

                p0.cards_hand.Remove(null);
                p0.cards_board.Remove(null);
                p0.cards_equip.Remove(null);
                p0.cards_deck.Remove(null);
                p0.cards_discard.Remove(null);
            }

            //---- ③④⑤ 创建路径：卡牌定义为 null 时不许崩、不许把 null 灌进列表 ----
            {
                warns.Clear();
                bool threw = false;
                string ex = "";
                Card created = null;
                try { created = logic.SummonCardHand(p0, null, VariantData.GetDefault()); }
                catch (Exception e) { threw = true; ex = e.GetType().Name; }
                bool null_in_hand = p0.cards_hand.Contains(null);
                Check("③ SummonCardHand(定义=null)：不崩 + 返回 null + 告警 + 不灌 null",
                    !threw && created == null && HasWarn("[召唤]") && !null_in_hand,
                    "抛异常=" + threw + (threw ? "(" + ex + ")" : "") + " 返回=" + (created == null ? "null" : "非null")
                    + " 有告警=" + HasWarn("[召唤]") + " 手牌里有 null=" + null_in_hand + WarnSample());
            }
            {
                warns.Clear();
                bool threw = false;
                string ex = "";
                Card added = null;
                try { added = logic.AddCardDeck(p0, null, VariantData.GetDefault()); }
                catch (Exception e) { threw = true; ex = e.GetType().Name; }
                bool null_in_deck = p0.cards_deck.Contains(null);
                Check("④ AddCardDeck(定义=null)：不崩 + 返回 null + 告警 + 不灌 null",
                    !threw && added == null && HasWarn("[牌库]") && !null_in_deck,
                    "抛异常=" + threw + (threw ? "(" + ex + ")" : "") + " 返回=" + (added == null ? "null" : "非null")
                    + " 有告警=" + HasWarn("[牌库]") + " 牌库里有 null=" + null_in_deck + WarnSample());
            }
            {
                Card caster = Card.Create(chars[0], null, p0, "qb_caster");
                p0.cards_board.Add(caster);
                EffectCreate eff = ScriptableObject.CreateInstance<EffectCreate>();
                eff.create_pile = PileType.Hand;
                AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
                warns.Clear();
                bool threw = false;
                string ex = "";
                try { eff.DoEffect(logic, ab, caster, (CardData)null); }   //卡牌定义为 null
                catch (Exception e) { threw = true; ex = e.GetType().Name; }
                Check("⑤ EffectCreate(定义=null)：不崩 + 留告警", !threw && HasWarn("[创建]"),
                    "抛异常=" + threw + (threw ? "(" + ex + ")" : "") + " 有[创建]告警=" + HasWarn("[创建]") + WarnSample());
            }

            //---- ⑥ 单人局的随机查询不许崩 ----
            {
                Game g1 = new Game("probe_query_solo", 1);
                g1.state = GameState.Play;
                bool threw = false;
                string ex = "";
                Card random_card = null;
                try
                {
                    Player rp = g1.GetRandomPlayer(new System.Random(1));
                    random_card = g1.GetRandomBoardCard(new System.Random(1));
                    Slot s = g1.GetRandomSlot(new System.Random(1));
                }
                catch (Exception e) { threw = true; ex = e.GetType().Name + ": " + e.Message; }
                Check("⑥ 单人局随机查询不崩", !threw,
                    "抛异常=" + threw + (threw ? "（" + ex + "）" : "") + " 随机卡=" + (random_card == null ? "null（空战场）" : "非null"));
            }

            //---- ⑦ 对照：正常查询仍然正确 ----
            {
                Card c = Card.Create(chars[0], null, p0, "qb_normal");
                p0.cards_hand.Add(c);
                bool ok = p0.GetHandCard("qb_normal") == c
                    && g.GetHandCard("qb_normal") == c
                    && g.IsInHand(c)
                    && g.GetCard("qb_normal") == c
                    && p0.GetHandCard("不存在的uid") == null;
                Check("⑦ 对照：正常查询语义不变", ok,
                    "Player.GetHandCard 命中=" + (p0.GetHandCard("qb_normal") == c)
                    + " Game.GetHandCard 命中=" + (g.GetHandCard("qb_normal") == c)
                    + " IsInHand=" + g.IsInHand(c) + " 不存在 uid 返回 null=" + (p0.GetHandCard("不存在的uid") == null));
            }
        }
    }
}
