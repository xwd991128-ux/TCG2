using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Gameplay;

/// <summary>【临时探针】行为基线记录器（Phase 0-B）。
///
/// 目标：给"内置卡牌迁移到规则图"提供**可复现的端到端判据**——同一颗随机种子跑出来的对局，
///       迁移前后必须产生**完全相同的记录**。
///
/// 用法（配合 AutoBattleRunner）：
///   ① 拷贝本文件到 Assets/TcgEngine/Scripts/__TempProbe/ 并编译
///   ② 写两个标记文件（都在 persistentDataPath/Workshop/）：
///      - autobattle.json ：{"battles":1,"maxSecondsPerBattle":180,"stuckSeconds":40}  → 让 AI 自动打一局
///      - baseline.json   ：{"seed":12345}                                              → 记录器配置
///   ③ 进 Play：记录器先把 RNG 固定到 seed（整局可复现），再记录事件轨迹 + 逐回合状态快照
///   ④ 产出 <项目>/tools/baseline_state.txt（跑完自动删除 baseline.json）
///   ⑤ 复现性自检：同 seed 跑两次，两个文件必须逐字节相同（否则判据本身不可用）
///
/// 设计约束：
///   - **不记录 uid**（uid 由随机/Guid 生成，不可复现）→ 卡按 card_id 规范化排序后 dump
///   - 只在标记文件存在时创建，正常游玩零开销
///   - 只读游戏状态 + 只加事件监听，不改变任何玩法行为
public class BaselineRecorder : MonoBehaviour
{
    [Serializable]
    public class Cfg
    {
        public int seed = 12345;            //固定随机种子（整局可复现）
        public bool traceEvents = true;     //记录事件轨迹
        public bool dumpEveryTurn = true;   //每回合边界 dump 一次全量状态
        public bool dumpOnEvent = true;     //每次"能力结算/打出/召唤/伤害/治疗/攻击"后 dump 一次
        public int maxDumps = 4000;         //保险上限
    }

    public static string FlagPath { get { return Path.Combine(Application.persistentDataPath, "Workshop", "baseline.json"); } }
    private static string OutPath { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/baseline_state.txt")); } }

    private Cfg cfg = new Cfg();
    private GameLogic logic;
    private Game data;
    private readonly StringBuilder sb = new StringBuilder();
    private int dumps;
    private int lastTurn = -999;
    private int lastCurrent = -999;
    private bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        try
        {
            if (!File.Exists(FlagPath))
                return;
            Cfg c = JsonUtility.FromJson<Cfg>(File.ReadAllText(FlagPath)) ?? new Cfg();
            UnityEngine.Random.InitState(c.seed);      //★ 必须在任何随机调用之前
            GameObject go = new GameObject("__BaselineRecorder");
            DontDestroyOnLoad(go);
            BaselineRecorder rec = go.AddComponent<BaselineRecorder>();
            rec.cfg = c;
            Debug.Log("[Baseline] 启动：seed=" + c.seed + " → " + OutPath);
        }
        catch (Exception e)
        {
            Debug.LogError("[Baseline] 启动失败: " + e.Message);
        }
    }

    private void Awake()
    {
        sb.AppendLine("# BaselineRecorder  seed=" + cfg.seed
            + "  started=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("# 说明：不含 uid（不可复现）；卡按 card_id 排序；同 seed 两次运行必须逐字节相同");
        sb.AppendLine("# format: T<seq> <event> ... | S<seq> turn=.. cur=.. P0[..] P1[..] <zone>:<id>,hp,atk,mana,dmg,exh,sig;...");
    }

    private void OnDestroy()
    {
        Unhook();
    }

    // ---------------- 夹具获取（与 NodeBatchProbe 同法：反射找一个带 GetGameData 的 Logic） ----------------
    private static object Dive(object o, int depth, ref int budget)
    {
        if (o == null || depth > 2 || budget-- <= 0) return null;
        Type t = o.GetType();
        if (t.GetMethod("GetGameData") != null && t.Name.IndexOf("Logic", StringComparison.OrdinalIgnoreCase) >= 0) return o;
        if (t.IsPrimitive || o is string) return null;
        if (t.Namespace != null && (t.Namespace.StartsWith("System") || t.Namespace.StartsWith("Unity"))) return null;
        foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            object v;
            try { v = f.GetValue(o); } catch { continue; }
            if (v == null || v is string) continue;
            object r = Dive(v, depth + 1, ref budget);
            if (r != null) return r;
        }
        return null;
    }

    private void Update()
    {
        if (!hooked)
        {
            if (Time.frameCount % 10 != 0) return;
            object lo = FindLogic();
            logic = lo as GameLogic;
            if (logic == null) return;
            try { data = logic.GetGameData(); } catch { data = null; }
            if (data == null) return;
            Hook();
            hooked = true;
            Debug.Log("[Baseline] 已挂载对局（玩家数=" + (data.players != null ? data.players.Length : 0) + "）");
            return;
        }

        if (data == null) return;

        bool turnChanged = data.turn_count != lastTurn || data.current_player != lastCurrent;
        if (cfg.dumpEveryTurn && turnChanged)
        {
            lastTurn = data.turn_count; lastCurrent = data.current_player;
            Dump("turn");
        }

        if (data.HasEnded())
        {
            Dump("game_end");
            Finish("ended");
        }
    }

    private static object FindLogic()
    {
        int budget = 8000;
        foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (mb == null) continue;
            object hit = Dive(mb, 0, ref budget);
            if (hit != null) return hit;
            if (budget <= 0) break;
        }
        return null;
    }

    // ---------------- 事件监听 ----------------
    private void Hook()
    {
        try
        {
            logic.onCardPlayed += (Card c, Slot s) => T("card_played", c, "slot=" + s);
            logic.onCardSummoned += (Card c, Slot s) => T("card_summoned", c, "slot=" + s);
            logic.onCardMoved += (Card c, Slot s) => T("card_moved", c, "slot=" + s);
            logic.onCardTransformed += (Card c) => T("card_transformed", c, "");
            logic.onCardDiscarded += (Card c) => T("card_discarded", c, "");
            logic.onCardDrawn += (int pid) => T2("card_drawn", "player=" + pid);
            logic.onRollValue += (int v) => T2("roll_value", "value=" + v);
            logic.onAbilityStart += (AbilityData ab, Card c) => T2("ability_start", "ab=" + (ab != null ? ab.id : "?") + " caster=" + Cid(c));
            logic.onAbilityEnd += (AbilityData ab, Card c) => T2("ability_end", "ab=" + (ab != null ? ab.id : "?") + " caster=" + Cid(c));
            logic.onAbilityTargetCard += (AbilityData ab, Card c, Card tgt) => T2("ability_target_card", "ab=" + (ab != null ? ab.id : "?") + " caster=" + Cid(c) + " target=" + Cid(tgt));
            logic.onAbilityTargetPlayer += (AbilityData ab, Card c, Player tgt) => T2("ability_target_player", "ab=" + (ab != null ? ab.id : "?") + " caster=" + Cid(c) + " target=" + (tgt != null ? "P" + tgt.player_id : "?"));
            logic.onAbilityTargetSlot += (AbilityData ab, Card c, Slot tgt) => T2("ability_target_slot", "ab=" + (ab != null ? ab.id : "?") + " caster=" + Cid(c) + " slot=" + tgt);
            logic.onAttackStart += (Card a, Card d) => T2("attack_start", "attacker=" + Cid(a) + " defender=" + Cid(d));
            logic.onAttackEnd += (Card a, Card d) => T2("attack_end", "attacker=" + Cid(a) + " defender=" + Cid(d));
            logic.onCardDamaged += (Card c, int v) => T2("card_damaged", "card=" + Cid(c) + " value=" + v);
            logic.onCardHealed += (Card c, int v) => T2("card_healed", "card=" + Cid(c) + " value=" + v);
            logic.onPlayerDamaged += (Player p, int v) => T2("player_damaged", "player=P" + (p != null ? p.player_id : -1) + " value=" + v);
        }
        catch (Exception e)
        {
            Debug.LogError("[Baseline] 事件挂载失败: " + e.Message);
        }
    }

    private void Unhook()
    {
        if (logic == null) return;
        try
        {
            logic.onCardPlayed = null; logic.onCardSummoned = null; logic.onCardMoved = null;
            logic.onCardTransformed = null; logic.onCardDiscarded = null; logic.onCardDrawn = null;
            logic.onRollValue = null; logic.onAbilityStart = null; logic.onAbilityEnd = null;
            logic.onAbilityTargetCard = null; logic.onAbilityTargetPlayer = null; logic.onAbilityTargetSlot = null;
            logic.onAttackStart = null; logic.onAttackEnd = null; logic.onCardDamaged = null;
            logic.onCardHealed = null; logic.onPlayerDamaged = null;
        }
        catch { }
    }

    // ---------------- 记录 ----------------
    private static string Cid(Card c)
    {
        if (c == null) return "-";
        return (c.card_id ?? "?") + "@P" + c.player_id;
    }

    private void T(string ev, Card c, string extra)
    {
        if (!cfg.traceEvents) return;
        sb.Append("T").Append(seq++).Append(' ').Append(ev).Append(" card=").Append(Cid(c));
        if (!string.IsNullOrEmpty(extra)) sb.Append(' ').Append(extra);
        sb.AppendLine();
        if (cfg.dumpOnEvent) Dump(ev);
    }

    private void T2(string ev, string detail)
    {
        if (!cfg.traceEvents) return;
        sb.Append("T").Append(seq++).Append(' ').Append(ev).Append(' ').Append(detail).AppendLine();
        if (cfg.dumpOnEvent) Dump(ev);
    }

    private int seq = 0;

    /// <summary>状态快照：按 card_id 规范化排序（不含 uid，保证可复现）</summary>
    private void Dump(string reason)
    {
        if (data == null || data.players == null) return;
        if (++dumps > cfg.maxDumps) { Finish("max_dumps"); return; }

        sb.Append("S").Append(dumps).Append(" turn=").Append(data.turn_count)
          .Append(" cur=").Append(data.current_player)
          .Append(" rolled=").Append(data.rolled_value)
          .Append(" selected=").Append(data.selected_value)
          .Append(" reason=").Append(reason).AppendLine();

        foreach (Player p in data.players)
        {
            if (p == null) continue;
            sb.Append("  P").Append(p.player_id)
              .Append(" hp=").Append(p.hp).Append('/').Append(p.hp_max)
              .Append(" mana=").Append(p.mana).Append('/').Append(p.mana_max).Append('/').Append(p.mana_max_total)
              .Append(" kills=").Append(p.kill_count).Append(" skip=").Append(p.skip_turns)
              .Append(" n=").Append(p.cards_deck.Count).Append('/').Append(p.cards_hand.Count).Append('/')
              .Append(p.cards_board.Count).Append('/').Append(p.cards_equip.Count).Append('/')
              .Append(p.cards_discard.Count).Append('/').Append(p.cards_secret.Count).Append('/')
              .Append(p.cards_temp.Count).AppendLine();

            Zone(p.cards_board, "board");
            Zone(p.cards_hand, "hand");
            Zone(p.cards_equip, "equip");
            Zone(p.cards_discard, "discard");
            Zone(p.cards_secret, "secret");
            Zone(p.cards_temp, "temp");
            Zone(p.cards_deck, "deck");
        }
        sb.AppendLine();
    }

    private void Zone(List<Card> cards, string zone)
    {
        if (cards == null || cards.Count == 0) return;
        List<Card> sorted = new List<Card>(cards);
        sorted.Sort((a, b) =>
        {
            string x = a != null ? a.card_id : "";
            string y = b != null ? b.card_id : "";
            int c = string.CompareOrdinal(x, y);
            if (c != 0) return c;
            return (a != null ? a.hp : 0).CompareTo(b != null ? b.hp : 0);
        });
        sb.Append("    ").Append(zone).Append(':');
        for (int i = 0; i < sorted.Count; i++)
        {
            Card c = sorted[i];
            if (c == null) continue;
            if (i > 0) sb.Append(';');
            string eq = "";
            try
            {
                if (!string.IsNullOrEmpty(c.equipped_uid) && data != null)
                {
                    Card bearer = data.GetCard(c.equipped_uid);
                    eq = bearer != null ? ("|eq=" + bearer.card_id) : "|eq=?";
                }
            }
            catch { }
            sb.Append(c.card_id).Append(",hp=").Append(c.hp).Append(",atk=").Append(c.attack)
              .Append(",mana=").Append(c.mana).Append(",dmg=").Append(c.damage)
              .Append(",exh=").Append(c.exhausted ? 1 : 0)
              .Append(",on=").Append(c.attack_ongoing).Append('/').Append(c.hp_ongoing).Append('/').Append(c.mana_ongoing)
              .Append(",sig=").Append(SafeSig(c)).Append(eq);
        }
        sb.AppendLine();
    }

    private static int SafeSig(Card c)
    {
        try { return c.StatusSignature(); } catch { return -1; }
    }

    private void Finish(string reason)
    {
        try
        {
            sb.AppendLine("# END reason=" + reason + " dumps=" + dumps + " seq=" + seq
                + " at=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Baseline] 已写出 " + OutPath + "（reason=" + reason + " dumps=" + dumps + "）");
        }
        catch (Exception e)
        {
            Debug.LogError("[Baseline] 写文件失败: " + e.Message);
        }
        try { if (File.Exists(FlagPath)) File.Delete(FlagPath); } catch { }
        enabled = false;
    }
}
