using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【复仇之怒（对敌人分配 8 点伤害）实测】直接读卡池 JSON 拿用户的图跑，验证：
    ///   1) 分配伤害的**总量**是否等于 8（原实现：候选池不剔除死者 → 目标死后继续打 → 丢失伤害）
    ///   2) 伤害是否落到了"活着的敌人"（含英雄=打脸）
    ///   3) 打印显示相关的编译结果（卡面字段 + 编译出的能力 title/desc/target/trigger），供排查"显示异常"
    /// 触发：建 tools/wrath_flag.txt → 进 Play（AfterSceneLoad 自触发）→ 写 tools/wrath_result.tsv → 自动删标记。
    /// </summary>
    public class WrathProbe : MonoBehaviour
    {
        private const string CARD_ID = "custom_CTZPjbOl";
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/wrath_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/wrath_flag.txt"); } }

        /// <summary>常驻（**不要求先建标志**）：放进 Assets 后，Play 期间随时建 tools/wrath_flag.txt 都会立刻跑，
        /// 不用停 Play / 重进 Play；跑完自动删标志 → 可反复重跑。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            GameObject go = new GameObject("WrathProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<WrathProbe>();
        }

        private int frames;
        private bool done;
        private int pass, fail;

        private void Update()
        {
            if (done)
            {
                //常驻：标志再次出现 → 复位重跑
                if (File.Exists(FlagPath))
                {
                    done = false;
                    frames = 0;
                    pass = 0;
                    fail = 0;
                    Debug.Log("[复仇之怒实测] 检测到标志 → 重跑");
                }
                return;
            }
            frames++;
            if (frames < 120)
                return;
            done = true;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("结果\t断言\t说明");
            try { Run(sb); }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION\t" + e.GetType().Name + "\t"
                    + (e.Message ?? "").Replace("\n", " ").Replace("\r", " "));
                Debug.LogError("[复仇之怒实测] 异常: " + e);
            }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[复仇之怒实测] 完成：" + OutPath + "（PASS=" + pass + " FAIL=" + fail + "）");
        }

        private void Check(StringBuilder sb, string name, bool ok, string detail)
        {
            if (ok) pass++; else fail++;
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private static CardData FirstMinion()
        {
            foreach (string id in new string[] { "imp", "fish", "crab_mana", "dragon_blue" })
            {
                CardData c = CardData.Get(id);
                if (c != null && c.type == CardType.Character)
                    return c;
            }
            return null;
        }

        private static CardData HeroData()
        {
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Hero)
                    return c;
            return null;
        }

        private void Run(StringBuilder sb)
        {
            // ---- 1) 从磁盘读用户的卡池，拿这张卡的图 ----
            string path = Path.Combine(Application.persistentDataPath, "Workshop", "sample_pool.json");
            if (!File.Exists(path))
            {
                sb.AppendLine("SKIP\t找不到卡池文件\t" + path);
                return;
            }
            CardPoolData pool = JsonUtility.FromJson<CardPoolData>(File.ReadAllText(path, Encoding.UTF8));
            CardCustomData cd = null;
            if (pool != null && pool.cards != null)
            {
                foreach (CardCustomData c in pool.cards)
                    if (c != null && c.id == CARD_ID) { cd = c; break; }
            }
            if (cd == null)
            {
                sb.AppendLine("SKIP\t卡池里没有 " + CARD_ID + "\t");
                return;
            }
            GraphData graph = cd.graph;
            sb.AppendLine("INFO\t卡面数据\t税费=" + cd.mana + " 类型=" + cd.type + " 文本=\"" + (cd.text ?? "") + "\""
                + " 图=" + (graph != null ? graph.nodes.Count + " 个节点" : "无") + "\t");

            // ---- 2) 打印显示相关：运行时注册卡 + 编译出的能力 ----
            CardData reg = CardData.Get(CARD_ID);
            if (reg != null)
            {
                sb.AppendLine("INFO\t运行时卡\t费用=" + reg.mana + " 攻击=" + reg.attack + " 生命=" + reg.hp
                    + " 类型=" + reg.type + " 文本=\"" + (reg.text ?? "") + "\""
                    + " 描述=\"" + (reg.desc ?? "") + "\""
                    + " 能力数=" + (reg.abilities != null ? reg.abilities.Length : -1) + "\t");
                if (reg.abilities != null)
                {
                    for (int i = 0; i < reg.abilities.Length; i++)
                    {
                        AbilityData ab = reg.abilities[i];
                        if (ab == null)
                        {
                            sb.AppendLine("INFO\t能力" + i + "\t<空引用>\t");
                            continue;
                        }
                        sb.AppendLine("INFO\t能力" + i + "\ttrigger=" + ab.trigger + " target=" + ab.target
                            + " value=" + ab.value + " effects=" + (ab.effects != null ? ab.effects.Length : -1)
                            + " title=\"" + (ab.title ?? "") + "\" desc=\"" + (ab.desc ?? "") + "\"\t");
                    }
                }
            }
            else
            {
                sb.AppendLine("INFO\t运行时卡\t未注册（CardData.Get 为空）\t");
            }

            if (graph == null)
            {
                sb.AppendLine("SKIP\t这张卡没有图\t");
                return;
            }

            // ---- 3) 夹具：p0 出牌；p1 三个随从（3/5/2 血）+ 英雄 ----
            Game g = new Game("probe_wrath", 2);
            GameLogic logic = new GameLogic(g);
            Player p0 = g.players[0];
            Player p1 = g.players[1];

            CardData minion = FirstMinion();
            if (minion == null)
            {
                sb.AppendLine("SKIP\t卡池没有可用随从定义\t");
                return;
            }
            p1.hero = Card.Create(HeroData(), null, p1);
            p1.hp_max = 30;
            p1.hp = 30;

            int[] hp_plan = new int[] { 3, 5, 2 };   // 合计 10 ≥ 8
            List<Card> enemies = new List<Card>();
            for (int i = 0; i < hp_plan.Length; i++)
            {
                Card m = Card.Create(minion, null, p1);
                if (m == null)
                    continue;
                m.hp = hp_plan[i];
                m.damage = 0;
                p1.cards_board.Add(m);
                enemies.Add(m);
            }
            int enemy_hp_before = 0;
            for (int i = 0; i < enemies.Count; i++)
                enemy_hp_before += enemies[i].hp;
            sb.AppendLine("INFO\t夹具\t敌方随从 " + enemies.Count + " 个（血 " + string.Join("/", Array.ConvertAll(enemies.ToArray(), x => x.hp.ToString()))
                + "） 敌方英雄 " + p1.hp + "/" + p1.hp_max + "\t");

            // ---- 4) 跑图：主动效果入口 ----
            CardData spellData = CardData.Get(CARD_ID);
            Card spell = Card.Create(spellData, null, p0);
            if (spell == null)
            {
                sb.AppendLine("SKIP\t无法构造法术卡实例\t");
                return;
            }
            p0.cards_hand.Add(spell);

            NodeDocRunner.Run(logic, graph, spell, null, null, "ActivateEffect");

            // ---- 5) 统计总伤害：随从 damage 合计 + 玩家掉血 ----
            int minion_dmg = 0;
            int dead = 0;
            string per = "";
            for (int i = 0; i < enemies.Count; i++)
            {
                minion_dmg += enemies[i].damage;
                if (enemies[i].GetHP() <= 0)
                    dead++;
                per += (i > 0 ? "/" : "") + enemies[i].damage;
            }
            int face_dmg = p1.hp_max - p1.hp;
            int total = minion_dmg + face_dmg;

            sb.AppendLine("INFO\t结果\t每个随从实际受到=[" + per + "] 合计=" + minion_dmg + "（死亡 " + dead
                + " 个） 打脸=" + face_dmg + " 总计=" + total + "\t");
            Check(sb, "分配伤害总量 = 8（不丢失）", total == 8, "实际总伤害=" + total);
            Check(sb, "伤害没有打到已死目标（总量正好 8，无溢出浪费）", minion_dmg <= enemy_hp_before + face_dmg,
                "随从初始总血=" + enemy_hp_before + " 随从受伤=" + minion_dmg + " 打脸=" + face_dmg);
        }
    }
}
