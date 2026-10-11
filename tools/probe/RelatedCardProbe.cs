using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>【相关卡牌：数据闭环验证】卡牌属性新字段 related_card_ids 必须能
    ///   ① CardData → CardCustomData（导出）；② JsonUtility 往返；③ CardCustomData → 运行期 CardData（导入/保存）。
    /// 触发：tools/related_flag.txt → 进 Play → tools/related_result.tsv → 自动删标记。跑完把被测卡恢复原样。</summary>
    public class RelatedCardProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/related_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/related_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("RelatedCardProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<RelatedCardProbe>();
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
            if (frames < 150)
                return;
            done = true;
            try { Run(); }
            catch (Exception e) { sb.AppendLine("EXCEPTION\t" + e.Message + "\t" + e.StackTrace); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[相关卡牌] 验证完成 → " + OutPath);
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        private void Run()
        {
            CardData bear = CardData.Get("bear");
            Check("① 前置：拿到被测卡 bear", bear != null, bear != null ? "id=" + bear.id : "CardData.Get(\"bear\")=null");
            if (bear == null)
                return;

            List<string> backup = bear.related_card_ids != null
                ? new List<string>(bear.related_card_ids) : new List<string>();

            List<string> want = new List<string> { "wolf_stalker", "tree_angry" };
            bear.related_card_ids = new List<string>(want);

            CardCustomData dto = CardPoolIO.CardToData(bear);
            bool ok1 = dto != null && dto.related_card_ids != null
                && dto.related_card_ids.Count == want.Count
                && dto.related_card_ids.Contains(want[0]) && dto.related_card_ids.Contains(want[1]);
            Check("② 导出：CardData → CardCustomData 带上相关卡牌", ok1,
                dto == null ? "CardToData=null" : "条数=" + (dto.related_card_ids != null ? dto.related_card_ids.Count : -1));

            if (dto == null)
            {
                bear.related_card_ids = backup;
                return;
            }

            CardPoolData pool = new CardPoolData();
            pool.name = "probe_related";
            pool.cards = new List<CardCustomData> { dto };
            string json = JsonUtility.ToJson(pool, true);
            bool json_has = json.Contains("related_card_ids");
            CardPoolData back = JsonUtility.FromJson<CardPoolData>(json);
            List<string> got = (back != null && back.cards != null && back.cards.Count > 0)
                ? back.cards[0].related_card_ids : null;
            bool ok2 = json_has && got != null && got.Count == want.Count
                && got.Contains(want[0]) && got.Contains(want[1]);
            Check("③ JSON 往返：卡池文件里保留相关卡牌", ok2,
                "json含字段=" + json_has + " 回读条数=" + (got != null ? got.Count : -1));

            bear.related_card_ids = new List<string>();
            CardPoolIO.UpdateCardData(back.cards[0]);
            CardData after = CardData.Get("bear");
            List<string> after_ids = after != null ? after.related_card_ids : null;
            bool ok3 = after_ids != null && after_ids.Count == want.Count
                && after_ids.Contains(want[0]) && after_ids.Contains(want[1]);
            Check("④ 导入/保存：运行期 CardData 拿到相关卡牌", ok3,
                "条数=" + (after_ids != null ? after_ids.Count : -1));

            CardPoolIO.UpdateCardData(dto);
            bear.related_card_ids = backup;
            sb.AppendLine("INFO\t已恢复 bear 的相关卡牌为原值（" + backup.Count + " 条）");
        }
    }
}
