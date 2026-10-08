using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【卡池导入/导出：删池残留 + 纯 JSON 往返丢字段】
    /// ① 导入一个临时卡池（含 美术/音效/自定义参数声明）；
    /// ② 纯 JSON 导出必须保留 art_path / art_full_path / 4 个音效 id / custom_prop_defs
    ///    （旧实现一字未写 ⇒ 分享 JSON 给别人 = 卡图全黑、音效全无、高级筛选 p:参数 消失；
    ///      .tcgpool 包有"按运行时资源反编码回填"的兜底，所以这个洞只在纯 JSON 出口暴露）；
    /// ③ 删池后：文件 / 卡定义 / **custom_data 原始数据登记表** 都要清
    ///    （漏清 ⇒ 已删池的卡仍会被授权，且其"作用范围=全部卡牌"规则继续生效）；
    /// ④ 删池后全局规则表必须被重建（不能残留被删池的全局入口）。
    /// 触发：建 tools/poolresidue_flag.txt → 进 Play → 写 tools/pool_residue_result.tsv → 自动删标记。
    /// 临时卡池文件写在 Workshop 目录，跑完在 finally 里删掉（不留垃圾）。
    /// </summary>
    public class PoolResidueProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/pool_residue_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/poolresidue_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("PoolResidueProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PoolResidueProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();

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
            Debug.Log("[卡池残留] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        /// <summary>全局规则表总条数（攻击侧 + 被攻击侧 + 按触发器归组的全局入口）</summary>
        private static int GlobalCount()
        {
            int n = CardPoolIO.GlobalAttackGraphs.Count + CardPoolIO.GlobalDefendGraphs.Count;
            foreach (KeyValuePair<AbilityTrigger, List<CardPoolIO.GlobalEntry>> kv in CardPoolIO.GlobalEntriesByTrigger)
                if (kv.Value != null)
                    n += kv.Value.Count;
            return n;
        }

        // ---------------- 告警捕获：断言"真的打出告警"，而不是靠肉眼看控制台 ----------------

        private static readonly List<string> captured = new List<string>();

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (!string.IsNullOrEmpty(msg))
                captured.Add(msg);
        }

        private static void CaptureStart()
        {
            captured.Clear();
            Application.logMessageReceived += OnLog;
        }

        private static void CaptureStop()
        {
            Application.logMessageReceived -= OnLog;
        }

        private static bool Warned(string key)
        {
            return captured.Exists(l => l != null && l.Contains(key));
        }

        // ---------------- 图夹具小工具 ----------------

        private static GraphNode Node(GraphData g, string id, GraphNodeType type, string action, string title)
        {
            var n = new GraphNode();
            n.id = id; n.type = type; n.action = action; n.title = title;
            n.pins = new List<GraphPin>();
            n.fields = new List<FieldCustomData>();
            n.category = type == GraphNodeType.Event ? "事件" : "其他";
            g.nodes.Add(n);
            return n;
        }

        private static void Pin(GraphNode n, string name, NodeValueType type, bool output)
        {
            n.pins.Add(new GraphPin
            {
                id = n.id + "_" + name, name = name, display_name = name,
                is_output = output, type = type
            });
        }

        private static void Link(GraphData g, GraphNode from, string fromPin, GraphNode to, string toPin)
        {
            g.links.Add(new GraphLink
            {
                from_node = from.id, from_pin = from.id + "_" + fromPin,
                to_node = to.id, to_pin = to.id + "_" + toPin
            });
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");
            const string POOL_NAME = "probe_poolresidue_tmp";
            const string CARD_ID = "probe_poolresidue_card";
            string path = Path.Combine(CardPoolIO.SaveFolder, POOL_NAME + ".json");

            try
            {
                Directory.CreateDirectory(CardPoolIO.SaveFolder);

                //---- 夹具：一张带 美术/音效/自定义参数声明 的卡 ----
                CardPoolData pool = new CardPoolData();
                pool.name = POOL_NAME;
                pool.author = "probe";
                pool.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                CardCustomData cd = new CardCustomData();
                cd.id = CARD_ID;
                cd.title = "探针残留卡";
                cd.type = CardType.Character.ToString();
                cd.mana = 1; cd.attack = 1; cd.hp = 3;
                cd.deckbuilding = true;
                cd.art_path = "probe_art.png";
                cd.art_full_path = "probe_art_full.png";
                cd.spawn_audio_id = "probe_spawn.wav";
                cd.attack_audio_id = "probe_attack.wav";
                cd.death_audio_id = "probe_death.wav";
                cd.damage_audio_id = "probe_damage.wav";
                cd.custom_prop_defs = new List<BuffCustomProp>
                {
                    new BuffCustomProp("探针参数", BuffPropType.Int, false, "7")
                };
                pool.cards.Add(cd);
                File.WriteAllText(path, JsonUtility.ToJson(pool, true), new UTF8Encoding(false));

                //---- ① 导入 ----
                CardPoolIO.ImportFromFile(path, false);      //false = 不授予拥有权（别污染玩家存档）
                CardData card = CardData.Get(CARD_ID);
                CardCustomData reg = CardPoolIO.GetCustomData(CARD_ID);
                Check("① 临时卡池导入成功", card != null && reg != null,
                    "卡定义=" + (card != null) + " 原始数据登记=" + (reg != null) + " 文件=" + Path.GetFileName(path));

                //---- ② 纯 JSON 导出：美术/音效/自定义参数 必须一起带走 ----
                string json = card != null ? CardPoolIO.ExportToJson(new List<CardData> { card }, POOL_NAME) : "";
                CardPoolData back = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<CardPoolData>(json);
                CardCustomData b = back != null && back.cards != null && back.cards.Count > 0 ? back.cards[0] : null;
                bool art_ok = b != null && b.art_path == "probe_art.png" && b.art_full_path == "probe_art_full.png";
                bool audio_ok = b != null && b.spawn_audio_id == "probe_spawn.wav"
                    && b.attack_audio_id == "probe_attack.wav"
                    && b.death_audio_id == "probe_death.wav"
                    && b.damage_audio_id == "probe_damage.wav";
                bool props_ok = b != null && b.custom_prop_defs != null && b.custom_prop_defs.Count == 1
                    && b.custom_prop_defs[0] != null && b.custom_prop_defs[0].name == "探针参数";
                Check("② 纯 JSON 导出保留 美术/音效/自定义参数", art_ok && audio_ok && props_ok,
                    "美术=" + art_ok + "（art_path=" + (b != null ? "\"" + b.art_path + "\"" : "null") + "）"
                    + " 音效=" + audio_ok + " 自定义参数=" + props_ok
                    + "（" + (b != null && b.custom_prop_defs != null ? b.custom_prop_defs.Count.ToString() : "0") + " 项）");

                //---- ③ 编辑器保存（UpdateCardData）必须刷新登记表（否则导出/自定义参数读到旧 DTO）----
                CardCustomData edited = new CardCustomData();      //★故意用**新实例**：编辑器保存时也是反序列化出来的新 DTO
                edited.id = CARD_ID;
                edited.title = "探针残留卡(已改)";
                edited.type = CardType.Character.ToString();
                edited.mana = 1; edited.attack = 2; edited.hp = 4;
                edited.deckbuilding = true;
                edited.art_path = "probe_art_v2.png";
                edited.custom_prop_defs = new List<BuffCustomProp>
                {
                    new BuffCustomProp("探针参数", BuffPropType.Int, false, "9")
                };
                CardPoolIO.UpdateCardData(edited);                 //规则编辑器保存走的正是这条路
                CardCustomData reg2 = CardPoolIO.GetCustomData(CARD_ID);
                string json2 = CardPoolIO.ExportToJson(new List<CardData> { CardData.Get(CARD_ID) }, POOL_NAME);
                CardPoolData back2 = string.IsNullOrEmpty(json2) ? null : JsonUtility.FromJson<CardPoolData>(json2);
                CardCustomData b2 = back2 != null && back2.cards != null && back2.cards.Count > 0 ? back2.cards[0] : null;
                bool reg_ok = reg2 != null && reg2.art_path == "probe_art_v2.png"
                    && reg2.custom_prop_defs != null && reg2.custom_prop_defs.Count == 1
                    && reg2.custom_prop_defs[0].init_value == "9";
                bool exp_ok = b2 != null && b2.art_path == "probe_art_v2.png" && b2.attack == 2;
                Check("⑤ 编辑器保存后登记表与导出同步", reg_ok && exp_ok,
                    "登记表 art_path=" + (reg2 != null ? reg2.art_path : "null") + "（期望 probe_art_v2.png）"
                    + " 导出 art_path=" + (b2 != null ? b2.art_path : "null") + " attack=" + (b2 != null ? b2.attack.ToString() : "?") + "（期望 2）");

                //---- ⑥ 改池后重新导入必须生效（同一文件的卡允许覆盖，否则"半新半旧"）----
                CardPoolData pool2 = new CardPoolData();
                pool2.name = POOL_NAME;
                pool2.author = "probe";
                pool2.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                CardCustomData cd2 = new CardCustomData();
                cd2.id = CARD_ID;
                cd2.title = "探针残留卡 v2";
                cd2.type = CardType.Character.ToString();
                cd2.mana = 2; cd2.attack = 3; cd2.hp = 6;
                cd2.deckbuilding = true;
                cd2.art_path = "probe_art_v3.png";
                pool2.cards.Add(cd2);
                File.WriteAllText(path, JsonUtility.ToJson(pool2, true), new UTF8Encoding(false));
                CardPoolIO.ImportFromFile(path, false);
                CardData again = CardData.Get(CARD_ID);
                Check("⑥ 改池重新导入生效（不再半新半旧）",
                    again != null && again.hp == 6 && again.attack == 3 && again.title == "探针残留卡 v2",
                    "重导入后 标题=" + (again != null ? again.title : "null")
                    + " hp=" + (again != null ? again.hp.ToString() : "?")
                    + " attack=" + (again != null ? again.attack.ToString() : "?")
                    + "（期望 v2/6/3；旧行为会停在编辑后的 4/2，却只提示\"新增 0 张卡\"）");

                //---- ④ 删池：文件 / 卡定义 / 原始数据登记表 全清 ----
                int globals_before = GlobalCount();
                bool deleted = CardPoolIO.DeletePoolFile(path);
                bool file_gone = !File.Exists(path);
                bool card_gone = CardData.Get(CARD_ID) == null;
                bool data_gone = CardPoolIO.GetCustomData(CARD_ID) == null;
                Check("③ 删池清理干净（文件/卡定义/原始数据）", deleted && file_gone && card_gone && data_gone,
                    "删除返回=" + deleted + " 文件已删=" + file_gone + " 卡已卸载=" + card_gone
                    + " 原始数据已清=" + data_gone + "（旧实现漏清最后一项：已删池的卡仍会授权、全局规则仍生效）");

                //---- ④ 删池后全局规则表已重建 ----
                int globals_after = GlobalCount();
                Check("④ 删池后全局规则表已重建", globals_after == globals_before,
                    "全局规则条数 删前=" + globals_before + " → 删后=" + globals_after + "（必须相等，不能残留）");

                //---- ⑦ 池级规则图：必须明确告警（旧行为：编辑器能画、对局零效果、零告警）----
                string pg_path = Path.Combine(CardPoolIO.SaveFolder, POOL_NAME + "_pg.json");
                CaptureStart();
                try
                {
                    CardPoolData pg = new CardPoolData();
                    pg.name = POOL_NAME + "_pg";
                    pg.author = "probe";
                    GraphData pgraph = new GraphData
                    {
                        name = "探针池级图",
                        nodes = new List<GraphNode>(),
                        links = new List<GraphLink>()
                    };
                    GraphNode pev = Node(pgraph, "ev", GraphNodeType.Event, "OnBeforeDamage", "伤害时");
                    Pin(pev, "out", NodeValueType.Flow, true);
                    pg.graph = pgraph;
                    CardCustomData pgc = new CardCustomData();
                    pgc.id = CARD_ID + "_pg";
                    pgc.title = "池级图探针卡";
                    pgc.type = CardType.Character.ToString();
                    pgc.hp = 1; pgc.mana = 0; pgc.deckbuilding = true;
                    pg.cards.Add(pgc);
                    File.WriteAllText(pg_path, JsonUtility.ToJson(pg, true), new UTF8Encoding(false));

                    int g_before = GlobalCount();
                    CardPoolIO.ImportFromFile(pg_path, false);
                    bool warned_pg = Warned("池级规则图不会被导入");
                    bool card_pg = CardData.Get(CARD_ID + "_pg") != null;
                    int g_after = GlobalCount();
                    CardPoolIO.DeletePoolFile(pg_path);
                    Check("⑦ 池级规则图：明确告警且不被当成全局规则", warned_pg && card_pg && g_after == g_before,
                        "告警=" + warned_pg + " 同池的卡仍正常导入=" + card_pg
                        + " 全局规则条数 " + g_before + "→" + g_after + "（池级图确实没生效，但已如实告知）");
                }
                finally { CaptureStop(); }

                //---- ⑧ 缺 id 的卡：不再静默丢弃 ----
                string bad_path = Path.Combine(CardPoolIO.SaveFolder, POOL_NAME + "_bad.json");
                CaptureStart();
                try
                {
                    CardPoolData bad = new CardPoolData();
                    bad.name = POOL_NAME + "_bad";
                    bad.author = "probe";
                    bad.cards.Add(new CardCustomData { id = "", title = "没有 id 的卡", type = CardType.Character.ToString() });
                    CardCustomData okc = new CardCustomData();
                    okc.id = CARD_ID + "_ok";
                    okc.title = "正常卡";
                    okc.type = CardType.Character.ToString();
                    okc.hp = 1; okc.mana = 0; okc.deckbuilding = true;
                    bad.cards.Add(okc);
                    File.WriteAllText(bad_path, JsonUtility.ToJson(bad, true), new UTF8Encoding(false));

                    CardPoolIO.ImportFromFile(bad_path, false);
                    bool warned_bad = Warned("张卡被跳过");
                    bool ok_imported = CardData.Get(CARD_ID + "_ok") != null;
                    CardPoolIO.DeletePoolFile(bad_path);
                    Check("⑧ 缺 id 的卡不再静默丢弃", warned_bad && ok_imported,
                        "告警=" + warned_bad + " 同池正常卡仍导入=" + ok_imported + "（旧行为：少一张卡、一句日志都没有）");
                }
                finally { CaptureStop(); }

                //---- ⑨ 条件类型还原失败：不再静默（条件凭空消失是最难查的一类）----
                CaptureStart();
                try
                {
                    GraphData g9 = new GraphData
                    {
                        name = "探针条件图",
                        nodes = new List<GraphNode>(),
                        links = new List<GraphLink>()
                    };
                    GraphNode ev9 = Node(g9, "ev", GraphNodeType.Event, "OnBeforeDamage", "伤害时");
                    Pin(ev9, "out", NodeValueType.Flow, true);
                    GraphNode act9 = Node(g9, "a", GraphNodeType.Action, "202001", "造成伤害");
                    Pin(act9, "in", NodeValueType.Flow, false);
                    Pin(act9, "value", NodeValueType.Int32, false);
                    Pin(act9, "out", NodeValueType.Flow, true);
                    Link(g9, ev9, "out", act9, "in");

                    CardCustomData c9 = new CardCustomData();
                    c9.id = CARD_ID + "_cond";
                    c9.title = "条件还原探针卡";
                    c9.type = CardType.Character.ToString();
                    c9.hp = 1; c9.mana = 0; c9.deckbuilding = true;
                    c9.effects = new List<CardEffectData>
                    {
                        new CardEffectData
                        {
                            name = "效果1",
                            graph = g9,
                            conditions_trigger = new List<ComponentCustomData>
                            {
                                new ComponentCustomData { type = "ProbeNotExistCondition" }   //故意写一个不存在的类名
                            }
                        }
                    };
                    CardData built9 = CardPoolIO.BuildCardData(c9);
                    Check("⑨ 条件类型还原失败不再静默", Warned("条件/过滤器类型无法还原") && built9 != null,
                        "告警=" + Warned("条件/过滤器类型无法还原") + " 卡仍能构建=" + (built9 != null)
                        + "（只是丢掉那条条件 —— 但要让你看得见）");
                }
                finally { CaptureStop(); }
            }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION 卡池\t" + e.Message + "\t" + e.StackTrace);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }   //不留垃圾在 Workshop
            }
        }
    }
}
