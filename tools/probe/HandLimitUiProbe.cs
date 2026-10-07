using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using TMPro;
using TcgEngine.Client;
using TcgEngine.Gameplay;
using TcgEngine.UI;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【手牌上限 UI 验证】HandCardArea 的"手牌 n/上限"显示：
    /// ① 惰性创建（场景没配也不报错，自动建一个）；
    /// ② 文本 = "手牌 n/上限"（上限取 Player.GetHandMax()）；
    /// ③ 满了变红、不满是次要文字色；
    /// ④ 重复刷新不会建出第二个（幂等）；
    /// ⑤ 字体走项目统一管线（font != null，避免缺字方块）。
    /// 触发：建 tools/handmaxui_flag.txt → 进 Play → 写 tools/handmaxui_result.tsv → 自动删标记。
    /// </summary>
    public class HandLimitUiProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/handmaxui_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/handmaxui_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("HandLimitUiProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<HandLimitUiProbe>();
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
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e.Message); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[手牌上限UI] 验证完成 → " + OutPath);
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            CardData any = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Character) { any = c; break; }
            if (any == null) { sb.AppendLine("SKIP\t缺少角色定义\t"); return; }

            //---- 夹具：手牌区（root → CardArea / HandCardArea）----
            GameObject root = new GameObject("ProbeHandRoot", typeof(RectTransform));
            GameObject area = new GameObject("CardArea", typeof(RectTransform));
            area.transform.SetParent(root.transform, false);
            GameObject hca_obj = new GameObject("HandCardArea", typeof(RectTransform));
            hca_obj.transform.SetParent(root.transform, false);
            HandCardArea hca = hca_obj.AddComponent<HandCardArea>();
            hca.card_area = area.GetComponent<RectTransform>();

            Player p0 = new Player(0);
            p0.hand_max = 10;
            for (int i = 0; i < 3; i++)
                p0.cards_hand.Add(Card.Create(any, null, p0, "ui_" + i));

            MethodInfo mi = typeof(HandCardArea).GetMethod("UpdateHandLimitLabel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi == null) { sb.AppendLine("FAIL\t找不到 UpdateHandLimitLabel\t方法名被改过？"); return; }

            mi.Invoke(hca, new object[] { p0 });     // ① 首次刷新 = 惰性创建
            TMP_Text label = hca.hand_limit_label;
            bool created = label != null && label.text == "手牌 3/10";
            sb.AppendLine(((created && label.font != null) ? "PASS" : "FAIL")
                + "\t① 惰性创建 + 文本/字体正确\t文本=\"" + (label != null ? label.text : "null")
                + "\" 字体=" + (label != null && label.font != null ? label.font.name : "null"));

            //---- ③ 不满 = 次要文字色；满了变红 ----
            bool dim_ok = label.color == UITheme.TextDim;
            p0.cards_hand.Clear();
            for (int i = 0; i < 10; i++)
                p0.cards_hand.Add(Card.Create(any, null, p0, "ui_full_" + i));
            mi.Invoke(hca, new object[] { p0 });
            bool full_ok = label.text == "手牌 10/10" && label.color != UITheme.TextDim;
            // 改回未满 → 颜色应回到次要色
            p0.cards_hand.RemoveAt(0);
            mi.Invoke(hca, new object[] { p0 });
            bool back_ok = label.text == "手牌 9/10" && label.color == UITheme.TextDim;
            sb.AppendLine((dim_ok && full_ok && back_ok ? "PASS" : "FAIL")
                + "\t② 满/不满的颜色与文本切换\t不满=次要色(" + dim_ok + ") 满=" + label.text
                + " 变红(" + full_ok + ") 回落=" + back_ok);

            //---- ④ 幂等：重复刷新不会建第二个 ----
            mi.Invoke(hca, new object[] { p0 });
            mi.Invoke(hca, new object[] { p0 });
            int count = 0;
            foreach (Transform child in root.transform)
                if (child.name == "HandLimitLabel")
                    count++;
            sb.AppendLine((count == 1 ? "PASS" : "FAIL") + "\t③ 重复刷新不会建出第二个\tHandLimitLabel 个数=" + count);

            //---- ⑤ 上限随节点改变时文本跟着变（hand_max 是玩家属性）----
            p0.hand_max = 15;
            mi.Invoke(hca, new object[] { p0 });
            sb.AppendLine((label.text == "手牌 9/15" ? "PASS" : "FAIL")
                + "\t④ 上限被改后文本跟随\t文本=\"" + label.text + "\"");

            Destroy(root);
        }
    }
}
