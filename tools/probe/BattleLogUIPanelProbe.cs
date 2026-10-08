using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;
using TcgEngine.Gameplay;
using TcgEngine.UI;

namespace TcgEngine.Probe
{
    /// <summary>【对战记录面板 P1 收尾验证：悬停看卡 + 筛选】
    /// ① 面板/筛选条/浮层构建正常；② 记录行能收指针事件（否则"悬停看卡"根本不可能）；
    /// ③ 筛选谓词口径（全部/只看我方/只看关键）；④ **端到端**：注入假对局 → 面板 Refresh →
    /// 三种筛选下的行数与内容都正确（不是只测谓词）；⑤ 悬停某行 → 右侧浮层显示该卡名/图；
    /// ⑥ 浮层绝不吃点击（所有 Graphic.raycastTarget=false，否则会挡住战场操作）；
    /// ⑦ 关面板时浮层必须收起（它挂在面板外，不关会留在屏幕上）。
    /// 触发：建 tools/battlelog_ui_flag.txt → 进 Play → 写 tools/battlelog_ui_result.tsv → 自动删标记。
    /// </summary>
    public class BattleLogUIPanelProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/battlelog_ui_result.tsv"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/battlelog_ui_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("BattleLogUIPanelProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<BattleLogUIPanelProbe>();
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
            Debug.Log("[对战记录UI] 验证完成 → " + OutPath);
        }

        // ---------------- 反射小工具（面板的字段/方法是 private，探针不改产品代码可见性） ----------------

        private static object F(object o, string n)
        {
            FieldInfo f = o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f != null ? f.GetValue(o) : null;
        }

        private static void SF(object o, string n, object v)
        {
            FieldInfo f = o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(o, v);
        }

        private static object M(object o, string n, params object[] a)
        {
            MethodInfo m = o.GetType().GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return m != null ? m.Invoke(o, a) : null;
        }

        private void Check(string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
        }

        // ---------------- 假对局 ----------------

        private static BattleLogEntry E(int turn, int actor, BattleLogKind kind, string card_id, int value = 0, int target_id = -1)
        {
            return new BattleLogEntry
            {
                seq = 0, turn = turn, actor = actor, kind = (byte)kind,
                card_id = card_id, value = value, target_id = target_id
            };
        }

        private void Run()
        {
            sb.AppendLine("结果\t断言\t说明");

            //---- 找两张标题不同的角色卡（用来分辨"我方卡/对方卡"）----
            CardData cardA = null, cardB = null, art_card = null;
            foreach (CardData c in CardData.GetAll())
            {
                if (c == null || c.type != CardType.Character || string.IsNullOrEmpty(c.title))
                    continue;
                if (art_card == null && c.GetFullArt(null) != null)
                    art_card = c;
                if (cardA == null) { cardA = c; continue; }
                if (cardB == null && c.title != cardA.title) { cardB = c; }
            }
            if (cardA == null || cardB == null)
            {
                sb.AppendLine("SKIP\t缺少两张角色卡\t");
                return;
            }
            if (art_card == null)
                art_card = cardA;

            //---- 假对局：我方(0)/对方(1) 各含"关键"与"噪音" ----
            Game game = new Game("probe_battlelog", 2);
            game.state = GameState.Play;
            game.players[0].username = "本方";
            game.players[1].username = "敌方";
            game.battle_log = new List<BattleLogEntry>
            {
                E(1, -1, BattleLogKind.TurnStart, null),                    //  1 噪音（回合分隔）
                E(1,  0, BattleLogKind.PlayCard, cardA.id),                  //  2 我方关键
                E(1,  0, BattleLogKind.Draw, cardA.id),                      //  3 我方噪音
                E(1,  0, BattleLogKind.Damage, cardA.id, 3, 1),              //  4 我方关键
                E(1,  1, BattleLogKind.PlayCard, cardB.id),                  //  5 对方关键
                E(2,  1, BattleLogKind.Draw, null),                          //  6 对方噪音
                E(2,  1, BattleLogKind.Damage, cardB.id, 2, 0),              //  7 对方关键
                E(2,  1, BattleLogKind.Death, cardB.id),                     //  8 对方关键
                E(2,  0, BattleLogKind.Heal, null, 5),                       //  9 我方关键
                E(2,  1, BattleLogKind.Move, cardB.id),                      // 10 对方噪音
            };
            int N_ALL = 10, N_MINE = 4, N_KEY = 6;

            //---- 注入 GameClient（面板 Refresh 走 GameClient.Get().GetGameData()）----
            GameClient gc = GameClient.Get();
            GameObject gc_go = null;
            object old_game = null;
            bool created = false;
            if (gc == null)
            {
                gc_go = new GameObject("ProbeGameClient");
                gc_go.SetActive(false);          //不激活：不让它的 Start/Update 跑（避免网络依赖）
                gc = gc_go.AddComponent<GameClient>();
                typeof(GameClient).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.SetValue(null, gc);
                created = true;
            }
            else
            {
                old_game = F(gc, "game_data");
            }
            SF(gc, "game_data", game);

            BattleLogPanel panel = null;
            try
            {
                //---- 建面板（会跑 Awake：构建入口按钮/面板/浮层）----
                GameObject pgo = new GameObject("BattleLogPanel");
                panel = pgo.AddComponent<BattleLogPanel>();

                //内建画布尺寸固定 1920x1080（CanvasScaler），这里不依赖真实分辨率即可断言
                GameObject root = (GameObject)F(panel, "root");
                GameObject art_root = (GameObject)F(panel, "art_root");
                GameObject filter_bar = (GameObject)F(panel, "filter_bar");
                RectTransform scroll_rt = (RectTransform)F(panel, "scroll_rt");
                Check("① 面板构建", root != null && art_root != null && filter_bar != null && scroll_rt != null,
                    "root=" + (root != null) + " 浮层=" + (art_root != null) + " 筛选条=" + (filter_bar != null));
                Check("① 浮层默认隐藏", art_root != null && !art_root.activeSelf,
                    "activeSelf=" + (art_root != null ? art_root.activeSelf.ToString() : "null"));

                panel.Toggle();   //打开（Refresh/悬停断言需要面板打开）
                GameObject root2 = (GameObject)F(panel, "root");
                M(panel, "Refresh");

                //---- 行是否能收指针事件（悬停看卡的前提）----
                List<GameObject> rows = (List<GameObject>)F(panel, "rows");
                bool row_hit = rows != null && rows.Count > 0;
                foreach (GameObject r in rows)
                {
                    Image img = r.GetComponent<Image>();
                    if (img == null || !img.raycastTarget) { row_hit = false; break; }
                }
                Check("② 记录行能收指针事件", row_hit,
                    "行数=" + (rows != null ? rows.Count : 0) + "（每行需有 raycastTarget=true 的 Image）");

                //---- ③ 筛选谓词口径 ----
                BattleLogEntry mine_key = E(1, 0, BattleLogKind.PlayCard, cardA.id);
                BattleLogEntry opp_key = E(1, 1, BattleLogKind.PlayCard, cardB.id);
                BattleLogEntry mine_noise = E(1, 0, BattleLogKind.Draw, null);
                BattleLogEntry turn_row = E(1, -1, BattleLogKind.TurnStart, null);
                bool pf = BattleLogPanel.PassFilter(null, 0, 0) == false
                    && BattleLogPanel.PassFilter(mine_key, 0, 0) && BattleLogPanel.PassFilter(opp_key, 0, 0)
                    && BattleLogPanel.PassFilter(mine_key, 1, 0) && !BattleLogPanel.PassFilter(opp_key, 1, 0)
                    && BattleLogPanel.PassFilter(mine_key, 2, 0) && BattleLogPanel.PassFilter(opp_key, 2, 0)
                    && !BattleLogPanel.PassFilter(mine_noise, 2, 0) && !BattleLogPanel.PassFilter(turn_row, 2, 0)
                    && BattleLogPanel.PassFilter(mine_noise, 1, 0);
                Check("③ 筛选谓词（null/全部/只看我方/只看关键）", pf,
                    "全部=全收 只看我方=仅本方 只看关键=滤掉抽牌与回合分隔");

                //---- ④ 端到端：真面板 Refresh 后按筛选出不同行集 ----
                int c_all, opp_all, draw_all, turn_all, death_all, q_all;
                StatRows(rows, out c_all, out opp_all, out draw_all, out turn_all, out death_all, out q_all);
                DumpTexts(rows, "全部");

                SF(panel, "filter", 1);
                M(panel, "Refresh");
                int c_mine, opp_mine, draw_mine, turn_mine, death_mine, q_mine;
                StatRows(rows, out c_mine, out opp_mine, out draw_mine, out turn_mine, out death_mine, out q_mine);
                DumpTexts(rows, "只看我方");

                SF(panel, "filter", 2);
                M(panel, "Refresh");
                int c_key, opp_key_rows, draw_key, turn_key, death_key, q_key;
                StatRows(rows, out c_key, out opp_key_rows, out draw_key, out turn_key, out death_key, out q_key);
                DumpTexts(rows, "只看关键");

                Check("④ 全部：行数与内容", c_all == N_ALL && opp_all == 3 && draw_all == 2 && turn_all == 1 && death_all == 1,
                    "行=" + c_all + "/" + N_ALL + " 对方行动行=" + opp_all + "/3 抽牌=" + draw_all + "/2 回合分隔=" + turn_all + "/1 消灭=" + death_all + "/1");
                Check("④ 只看我方：对方行动行全消失", c_mine == N_MINE && opp_mine == 0,
                    "行=" + c_mine + "/" + N_MINE + " 对方行动行=" + opp_mine + "（期望 0）");
                Check("④ 只看关键：滤掉抽牌与回合分隔、留下消灭", c_key == N_KEY && draw_key == 0 && turn_key == 0 && death_key == 1,
                    "行=" + c_key + "/" + N_KEY + " 抽牌=" + draw_key + "（期望0） 回合分隔=" + turn_key + "（期望0） 消灭=" + death_key + "/1");
                Check("④ 治疗文案不再出现问号", q_all == 0 && q_mine == 0 && q_key == 0,
                    "含「?」的行：全部=" + q_all + " 我方=" + q_mine + " 关键=" + q_key + "（期望均为 0）");

                //---- ⑤ 悬停看卡：悬停"带卡"的那一行 → 浮层显示该卡 ----
                SF(panel, "filter", 0);
                M(panel, "Refresh");
                BattleLogRow hover = null;
                foreach (GameObject r in rows)
                {
                    if (!r.activeSelf) continue;
                    BattleLogRow rr = r.GetComponent<BattleLogRow>();
                    if (rr != null && !string.IsNullOrEmpty(rr.card_id)) { hover = rr; break; }
                }
                SF(panel, "hover_row", hover);
                M(panel, "UpdateHoverArt");
                GameObject art2 = (GameObject)F(panel, "art_root");
                Image art_img = (Image)F(panel, "art_image");
                TMP_Text art_name = (TMP_Text)F(panel, "art_name");
                CardData hcd = hover != null ? CardData.Get(hover.card_id) : null;
                bool art_ok = hover != null && art2 != null && art2.activeSelf && hcd != null
                    && art_name != null && art_name.text == hcd.title
                    && art_img != null && art_img.enabled == (hcd.GetFullArt(null) != null);
                Check("⑤ 悬停行 → 浮层显示该卡", art_ok,
                    "悬停卡=" + (hcd != null ? hcd.title : "无") + " 浮层名=" + (art_name != null ? art_name.text : "null")
                    + " 图=" + (art_img != null ? (art_img.sprite != null ? "有" : "无") : "null")
                    + " enabled=" + (art_img != null ? art_img.enabled.ToString() : "null"));

                //---- ⑥ 浮层绝不吃点击 ----
                bool no_raycast = true;
                int gcount = 0;
                if (art2 != null)
                {
                    foreach (Graphic g in art2.GetComponentsInChildren<Graphic>(true))
                    {
                        gcount++;
                        if (g.raycastTarget) no_raycast = false;
                    }
                }
                Check("⑥ 浮层不吃点击（raycastTarget 全关）", no_raycast && gcount > 0, "图形数=" + gcount);

                //---- ⑦ 关面板 → 浮层收起 ----
                panel.Toggle();
                bool root_off = root2 != null && !root2.activeSelf;
                GameObject art3 = (GameObject)F(panel, "art_root");
                Check("⑦ 关面板浮层收起", root_off && art3 != null && !art3.activeSelf,
                    "面板=" + (root_off ? "关" : "开") + " 浮层=" + (art3 != null ? (art3.activeSelf ? "还开着" : "已收起") : "null"));

                //---- ⑧ 切到别的页：筛选条隐藏（不做"看得见但没用"的控件）+ 滚动区让位 ----
                GameObject root3 = (GameObject)F(panel, "root");
                root3.SetActive(true);
                SF(panel, "tab", 3);
                M(panel, "Refresh");
                GameObject fb3 = (GameObject)F(panel, "filter_bar");
                RectTransform srt3 = (RectTransform)F(panel, "scroll_rt");
                bool tab_ok = fb3 != null && !fb3.activeSelf && srt3 != null
                    && Mathf.RoundToInt(srt3.sizeDelta.y) == -108;
                Check("⑧ 非记录页隐藏筛选条并让回空间", tab_ok,
                    "筛选条=" + (fb3 != null ? (fb3.activeSelf ? "可见" : "隐藏") : "null")
                    + " 滚动区高=" + (srt3 != null ? srt3.sizeDelta.y.ToString() : "null") + "（期望 -108）");

                //---- ⑨ 行首类型色标：伤害红 / 治疗绿（色块区分类型，不依赖字体字形）----
                SF(panel, "tab", 0);
                SF(panel, "filter", 0);
                M(panel, "Refresh");
                Color dmg_dot = Color.clear, heal_dot = Color.clear;
                int dot_h = 0;
                foreach (GameObject r in rows)
                {
                    if (r == null || !r.activeSelf) continue;
                    BattleLogRow rr = r.GetComponent<BattleLogRow>();
                    if (rr == null || rr.label == null || rr.dot == null) continue;
                    if (rr.label.text.Contains("受到")) { dmg_dot = rr.dot.color; dot_h = Mathf.RoundToInt(rr.dot.rectTransform.sizeDelta.y); }
                    if (rr.label.text.Contains("恢复")) heal_dot = rr.dot.color;
                }
                Check("⑨ 行首色标随类型变化", Near(dmg_dot, new Color(1f, 0.45f, 0.4f)) && Near(heal_dot, new Color(0.5f, 1f, 0.6f)) && dot_h > 0,
                    "伤害色=" + dmg_dot + " 治疗色=" + heal_dot + " 色标高=" + dot_h);

                //---- ⑩ 「已使用卡牌」页：卡图缩略图 + 行高 56 + 正文让位（margin.x=62）----
                SF(panel, "tab", 1);
                M(panel, "Refresh");
                int tile_total = 0, tile_ok = 0;
                foreach (GameObject r in rows)
                {
                    if (r == null || !r.activeSelf) continue;
                    BattleLogRow rr = r.GetComponent<BattleLogRow>();
                    if (rr == null || rr.thumb == null || rr.label == null || string.IsNullOrEmpty(rr.card_id)) continue;
                    tile_total++;
                    LayoutElement le = r.GetComponent<LayoutElement>();
                    if (rr.thumb.enabled && rr.thumb.sprite != null
                        && Mathf.RoundToInt(rr.thumb.rectTransform.sizeDelta.y) == 48
                        && Mathf.RoundToInt(rr.label.margin.x) == 62
                        && le != null && Mathf.RoundToInt(le.preferredHeight) == 56)
                        tile_ok++;
                }
                Check("⑩ 已使用卡牌：卡图缩略图 + 正文让位", tile_total > 0 && tile_ok == tile_total,
                    "带卡行=" + tile_total + " 合格=" + tile_ok + "（缩略图 42x48 / 行高 56 / margin.x=62）");

                //---- ⑪ 记录页行不带缩略图、也不白留一块（margin.x=14）----
                SF(panel, "tab", 0);
                M(panel, "Refresh");
                int plain = 0, plain_ok = 0;
                foreach (GameObject r in rows)
                {
                    if (r == null || !r.activeSelf) continue;
                    BattleLogRow rr = r.GetComponent<BattleLogRow>();
                    if (rr == null || rr.thumb == null || rr.label == null) continue;
                    plain++;
                    if (!rr.thumb.enabled && Mathf.RoundToInt(rr.label.margin.x) == 14) plain_ok++;
                }
                Check("⑪ 记录页行无缩略图且不留白", plain > 0 && plain_ok == plain, "行=" + plain + " 合格=" + plain_ok);

                //---- ⑫ 淡入：新内容 alpha=0 → 淡入到 1；同一份数据重建**不会**重新变 0（否则整页每帧闪）----
                SF(panel, "filter", 0);
                SF(panel, "tab", 0);
                M(panel, "Refresh");
                AdvanceFades(rows);
                int act = 0, full = 0;
                CountAlpha(rows, 1f, out act, out full);
                M(panel, "Refresh");                       //同一份数据重建
                int act2 = 0, full2 = 0;
                CountAlpha(rows, 1f, out act2, out full2);
                SF(panel, "filter", 1);                    //换筛选 → 行内容变 → 应重新淡入
                M(panel, "Refresh");
                int act3 = 0, fade3 = 0;
                CountFading(rows, out act3, out fade3);
                Check("⑫ 新条目淡入 / 重建不闪", act > 0 && full == act && act2 > 0 && full2 == act2 && fade3 > 0,
                    "淡入后 alpha=1 行=" + full + "/" + act + " 同数据重建仍=1 行=" + full2 + "/" + act2
                    + " 换筛选后正在淡入的行=" + fade3);

                //---- ⑬ ★行文字必须撑满整行：宽度为 0 + 自动换行 = 每个字一行（用户实报的"竖线"）----
                SF(panel, "tab", 0);
                SF(panel, "filter", 0);
                M(panel, "Refresh");
                RectTransform ct = (RectTransform)F(panel, "content");
                if (ct != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(ct);   //不重建布局的话 rect 尺寸还是旧值
                float row_w = 0f, lbl_w = 0f, best_pref = 0f;
                int checked_rows = 0;
                foreach (GameObject r in rows)
                {
                    if (r == null || !r.activeSelf) continue;
                    BattleLogRow rr = r.GetComponent<BattleLogRow>();
                    if (rr == null || rr.label == null || string.IsNullOrEmpty(rr.label.text)) continue;
                    checked_rows++;
                    row_w = Mathf.Max(row_w, r.GetComponent<RectTransform>().rect.width);
                    lbl_w = Mathf.Max(lbl_w, rr.label.rectTransform.rect.width);
                    best_pref = Mathf.Max(best_pref, rr.label.preferredWidth);   //若被折成竖线，这里只有 ~20（单字宽）
                }
                Check("⑬ 行文字撑满整行（不折成竖线）", checked_rows > 0 && row_w > 100f && lbl_w > row_w - 12f && best_pref > 100f,
                    "行宽=" + row_w.ToString("0") + " 文字宽=" + lbl_w.ToString("0")
                    + " 单行首选宽=" + best_pref.ToString("0") + "（竖线时只有 ~20） 行数=" + checked_rows);
            }
            catch (Exception e)
            {
                sb.AppendLine("EXCEPTION 面板\t" + e.Message + "\t" + e.StackTrace);
            }
            finally
            {
                if (panel != null)
                {
                    GameObject r = (GameObject)F(panel, "root");
                    if (r != null) r.SetActive(false);
                }
                SF(gc, "game_data", old_game);        //还原：别把假对局留给同进程里的其它代码
                if (created)
                {
                    typeof(GameClient).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)
                        ?.SetValue(null, null);
                    if (gc_go != null) Destroy(gc_go);
                }
            }
        }

        /// <summary>统计当前可见行：总数 / "对方"开头的行（对方行动）/ 含"抽了"（抽牌噪音）/
        /// 含"── 第"（回合分隔）/ 含"被消灭"（破坏）/ 含「?」（文案兜底失败的痕迹）</summary>
        private static void StatRows(List<GameObject> rows, out int total, out int opp, out int draw,
            out int turn, out int death, out int question)
        {
            total = 0; opp = 0; draw = 0; turn = 0; death = 0; question = 0;
            if (rows == null) return;
            foreach (GameObject r in rows)
            {
                if (r == null || !r.activeSelf) continue;
                TMP_Text t = r.GetComponentInChildren<TMP_Text>(true);
                if (t == null) continue;
                total++;
                if (t.text.StartsWith("对方")) opp++;
                if (t.text.Contains("抽了")) draw++;
                if (t.text.Contains("── 第")) turn++;
                if (t.text.Contains("被消灭")) death++;
                if (t.text.Contains("「?」")) question++;
            }
        }

        private static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
        }

        /// <summary>推进所有行的淡入（Update 里的 unscaledDeltaTime 每帧很小，多推几次才够 FadeTime）</summary>
        private static void AdvanceFades(List<GameObject> rows)
        {
            if (rows == null) return;
            foreach (GameObject r in rows)
            {
                if (r == null || !r.activeSelf) continue;
                BattleLogRow rr = r.GetComponent<BattleLogRow>();
                if (rr == null) continue;
                for (int i = 0; i < 40; i++)
                    M(rr, "Update");
            }
        }

        private static void CountAlpha(List<GameObject> rows, float target, out int active, out int reached)
        {
            active = 0; reached = 0;
            if (rows == null) return;
            foreach (GameObject r in rows)
            {
                if (r == null || !r.activeSelf) continue;
                BattleLogRow rr = r.GetComponent<BattleLogRow>();
                if (rr == null || rr.group == null) continue;
                active++;
                if (rr.group.alpha >= target) reached++;
            }
        }

        private static void CountFading(List<GameObject> rows, out int active, out int fading)
        {
            active = 0; fading = 0;
            if (rows == null) return;
            foreach (GameObject r in rows)
            {
                if (r == null || !r.activeSelf) continue;
                BattleLogRow rr = r.GetComponent<BattleLogRow>();
                if (rr == null || rr.group == null || string.IsNullOrEmpty(rr.card_id)) continue;
                active++;
                if (rr.group.alpha < 1f) fading++;
            }
        }

        /// <summary>把当前可见行的文案写进结果文件（便于核对"筛选后的行内容"到底长什么样）</summary>
        private void DumpTexts(List<GameObject> rows, string mode)
        {
            if (rows == null) return;
            int i = 0;
            foreach (GameObject r in rows)
            {
                if (r == null || !r.activeSelf) continue;
                TMP_Text t = r.GetComponentInChildren<TMP_Text>(true);
                sb.AppendLine("ROW\t" + mode + " #" + (++i) + "\t" + (t != null ? t.text.Replace("\n", " ") : ""));
            }
        }
    }
}
