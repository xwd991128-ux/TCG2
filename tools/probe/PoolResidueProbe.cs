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
