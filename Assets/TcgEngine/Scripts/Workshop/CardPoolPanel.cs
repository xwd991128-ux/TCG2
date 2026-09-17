using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡池管理系统面板（游戏内 Unity 组件版）
    /// 显示内置卡池（按卡包划分）+ 本地卡池（JSON 文件），
    /// 支持多选导入/导出、单个导出、删除本地卡池。
    /// 面板由 Editor 工具（CardPoolPanelBuilder）在场景中用 Unity UI 组件搭建，
    /// 所有 UI 引用在 Inspector 中手动绑定（与卡牌管理 CollectionPanel 一致），
    /// 运行时不动态创建界面。
    /// </summary>
    public class CardPoolPanel : UIPanel
    {
        [Header("列表")]
        public ScrollRect scroll_rect;          // 滚动区域
        public RectTransform scroll_content;    // 列表容器（Content）
        public GameObject line_template;        // 行模板（隐藏，运行时复制）

        [Header("标题")]
        public Text title_text;                 // 标题文字
        public Button close_btn;                // 关闭按钮

        [Header("工具栏")]
        public Button select_all_btn;           // 全选
        public Button select_none_btn;          // 全不选
        public Button import_btn;               // 导入
        public Button export_btn;               // 批量导出
        public Text status_text;                // 底部状态提示

        private readonly float item_h = 50f;

        private List<PoolEntry> pool_entries = new List<PoolEntry>();
        private static CardPoolPanel instance;

        private class PoolEntry
        {
            public CardPoolIO.PoolInfo info;
            public Toggle toggle;
            public GameObject line;
        }

        public static CardPoolPanel Get() { return instance; }

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            if (close_btn != null) close_btn.onClick.AddListener(() => Hide());
            if (select_all_btn != null) select_all_btn.onClick.AddListener(() => SetAllSelected(true));
            if (select_none_btn != null) select_none_btn.onClick.AddListener(() => SetAllSelected(false));
            if (import_btn != null) import_btn.onClick.AddListener(OnImport);
            if (export_btn != null) export_btn.onClick.AddListener(OnExportSelected);

            EnsureBackButton();   //★ 场景没绑 close_btn 时运行时补「返回」（Builder 版漏绑 → 页面没有出口）
        }

        /// <summary>保证本页有「返回」出口（自愈，不依赖 Inspector 绑定）：
        /// ① 场景已绑 close_btn → 只确保它激活；
        /// ② 未绑（Builder 漏绑/引用丢失）→ 运行时在**左上角**补一个「返回」按钮。
        /// 为什么必须自愈：本面板是 UIPanel（全屏 + CanvasGroup 吃射线、盖住下层），
        /// 一旦没有关闭入口，玩家就只能强退——所以不能"等场景里有人绑"。
        /// 样式对齐项目规范：TMP + UIFonts 字体管线 + UITheme 配色（CtrlStrong 即"返回键等主要操作"的既定底色）。</summary>
        private void EnsureBackButton()
        {
            if (close_btn != null)
            {
                if (!close_btn.gameObject.activeSelf)
                    close_btn.gameObject.SetActive(true);
                SelfHealButtonLabel(close_btn);        //★ 场景里往往"按钮在、字不在"（TMP 无字体 → 标签不渲染）
                return;
            }

            RectTransform parent = transform as RectTransform;
            if (parent == null)
                return;

            GameObject go = new GameObject("BackBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);        //左上角：截图里该区域为空，不会压标题/列表/底部按钮
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -14f);
            rt.sizeDelta = new Vector2(96f, 40f);

            Image img = go.GetComponent<Image>();
            img.color = UITheme.CtrlStrong;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = btn.colors;
            cb.normalColor = UITheme.BtnTintNormal;
            cb.highlightedColor = UITheme.BtnTintHighlight;
            cb.pressedColor = UITheme.BtnTintPressed;
            btn.colors = cb;

            CreateLabel(rt);                           //文字走同一套构建函数
            btn.onClick.AddListener(() => Hide());
            go.transform.SetAsLastSibling();           //保证在其它内容之上，能点到
            close_btn = btn;

            Debug.Log("[卡池管理] 场景未绑定 close_btn → 已在左上角补建「返回」按钮（点击关闭本页）");
        }

        /// <summary>场景里那颗返回按钮的"自愈 + 显眼化"（幂等）。
        /// 2026-09 实测：场景里的 BackBtn 客观存在（屏幕矩形在视口内、25% 白底、可点、文字也渲染出 2 个字形），
        /// 但**尺寸 96x40 贴在面板最左上角、深色背景下与其它按钮不连贯** → 玩家一眼扫过去就是"这页没有返回按钮"。
        /// 所以这里三件事一起做：① 缺字体/空文字则补齐（走 UIFonts 管线）；② 统一放大到 116x44 并往内挪；
        /// ③ 底框确保可见（CtrlStrong = UITheme 里"返回键等主要操作"的既定底色）。</summary>
        private void SelfHealButtonLabel(Button btn)
        {
            if (btn == null)
                return;

            int fixed_bits = 0;

            // ---- ① 尺寸/位置（往面板内侧挪一点，避免贴着最外角显得像"游离的小方块"）----
            RectTransform rt = btn.transform as RectTransform;
            if (rt != null)
            {
                if (Mathf.Abs(rt.sizeDelta.x - 116f) > 0.5f || Mathf.Abs(rt.sizeDelta.y - 44f) > 0.5f)
                    fixed_bits++;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(24f, -20f);
                rt.sizeDelta = new Vector2(116f, 44f);
            }

            // ---- ② 底框可见 ----
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                if (!img.enabled)
                {
                    img.enabled = true;
                    fixed_bits++;
                }
                if (img.color.a < 0.3f)
                {
                    img.color = UITheme.CtrlStrong;
                    fixed_bits++;
                }
                img.raycastTarget = true;
            }

            // ---- ③ 文字（字体/内容/颜色/字号）----
            TextMeshProUGUI[] texts = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts == null || texts.Length == 0)
            {
                TextMeshProUGUI made = CreateLabel(rt);
                if (made != null)
                    made.fontSize = 22f;
                Debug.Log("[卡池管理] 返回按钮自愈：原来没有文字对象 → 已补「返回」标签");
            }
            else
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    TextMeshProUGUI t = texts[i];
                    if (t == null)
                        continue;

                    if (t.font == null)                    //没有字体资源 → 文字不渲染
                    {
                        UIFonts.ApplyFont(t);
                        fixed_bits++;
                    }
                    if (string.IsNullOrEmpty(t.text))
                    {
                        t.text = "返回";
                        fixed_bits++;
                    }
                    if (t.color.a < 0.9f)
                    {
                        t.color = UITheme.TextTitle;
                        fixed_bits++;
                    }
                    if (t.fontSize < 21f)
                    {
                        t.fontSize = 22f;                  //原来 20，放大一点
                        fixed_bits++;
                    }
                    t.raycastTarget = false;
                }
            }

            if (fixed_bits > 0)
                Debug.Log("[卡池管理] 返回按钮自愈：修正 " + fixed_bits
                    + " 项（尺寸/位置按 116x44@(24,-20)、底框 CtrlStrong、文字字体/字号/颜色）→ 现在位于面板左上角，点击关闭本页");
        }

        /// <summary>Esc 作为第二出口（仅本页可见时生效）</summary>
        protected override void Update()
        {
            base.Update();

            if (visible && Input.GetKeyDown(KeyCode.Escape))
                Hide();
        }

        /// <summary>按钮文字（TMP + UIFonts 字体管线，居中铺满父级）</summary>
        private TextMeshProUGUI CreateLabel(RectTransform parent)
        {
            GameObject tgo = new GameObject("Text", typeof(RectTransform));
            RectTransform trt = tgo.GetComponent<RectTransform>();
            trt.SetParent(parent, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            TextMeshProUGUI txt = tgo.AddComponent<TextMeshProUGUI>();
            txt.text = "返回";
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = 20f;
            txt.color = UITheme.TextTitle;
            txt.raycastTarget = false;
            UIFonts.ApplyFont(txt);                    //★ 必须走项目字体管线（否则字体不一致/发糊/缺字）
            return txt;
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            //★ 每次打开都自愈一次（幂等）：Awake 只在对象首次激活时跑一次，
            //  而返回按钮的问题（漏绑/字看不见）往往在"之后某次打开"才暴露 → 必须挂在 Show 上才可靠。
            EnsureBackButton();
            RefreshList();
        }

        // ---------------- 列表 ----------------

        private void RefreshList()
        {
            if (scroll_content == null)
                return;

            //清除旧行（保留模板）
            for (int i = scroll_content.childCount - 1; i >= 0; i--)
            {
                Transform child = scroll_content.GetChild(i);
                if (child != null && child.gameObject != line_template)
                    Destroy(child.gameObject);
            }
            pool_entries.Clear();

            List<CardPoolIO.PoolInfo> pools = new List<CardPoolIO.PoolInfo>();
            pools.AddRange(CardPoolIO.GetBuiltinPools());
            pools.AddRange(CardPoolIO.GetLocalPools());

            foreach (CardPoolIO.PoolInfo info in pools)
                CreatePoolLine(info);

            SetStatus(pools.Count > 0 ? "共 " + pools.Count + " 个卡池" : "暂无卡池，点击「导入」添加本地卡池");
        }

        private void CreatePoolLine(CardPoolIO.PoolInfo info)
        {
            if (line_template == null)
                return;

            GameObject line = Instantiate(line_template, scroll_content);
            line.name = "PoolLine_" + info.name;
            line.SetActive(true);

            PoolEntry entry = new PoolEntry();
            entry.info = info;
            entry.line = line;
            entry.toggle = line.transform.Find("Toggle")?.GetComponent<Toggle>();

            Text name_text = line.transform.Find("NameText")?.GetComponent<Text>();
            Text count_text = line.transform.Find("CountText")?.GetComponent<Text>();
            if (name_text != null)
                name_text.text = info.name + (info.IsReadonly ? "  <color=#9FD5FF>（内置）</color>" : "  <color=#FFE08A>（本地）</color>");
            if (count_text != null)
                count_text.text = info.card_count + " 张";

            Button export_btn = line.transform.Find("ExportBtn")?.GetComponent<Button>();
            if (export_btn != null)
                export_btn.onClick.AddListener(() => OnExportOne(info));

            Button edit_btn = line.transform.Find("EditBtn")?.GetComponent<Button>();
            if (edit_btn != null)
            {
                if (info.IsReadonly)
                    edit_btn.gameObject.SetActive(false);
                else
                    edit_btn.onClick.AddListener(() => OnEdit(info));
            }

            Button del_btn = line.transform.Find("DeleteBtn")?.GetComponent<Button>();
            if (del_btn != null)
            {
                if (info.IsReadonly)
                    del_btn.gameObject.SetActive(false);
                else
                    del_btn.onClick.AddListener(() => OnDelete(info));
            }

            pool_entries.Add(entry);
        }

        private void SetAllSelected(bool selected)
        {
            foreach (PoolEntry entry in pool_entries)
            {
                if (entry.toggle != null)
                    entry.toggle.isOn = selected;
            }
        }

        private List<CardPoolIO.PoolInfo> GetSelectedPools()
        {
            List<CardPoolIO.PoolInfo> list = new List<CardPoolIO.PoolInfo>();
            foreach (PoolEntry entry in pool_entries)
            {
                if (entry.toggle != null && entry.toggle.isOn)
                    list.Add(entry.info);
            }
            return list;
        }

        // ---------------- 导入/导出/删除 ----------------

        private void OnImport()
        {
            string[] files = FileDialogTool.OpenFiles("选择要导入的卡池文件", "卡池文件 (*.tcgpool;*.json)|*.tcgpool;*.json", true);
            if (files == null || files.Length == 0)
            {
                SetStatus("未选择文件，导入已取消");
                return;
            }

            int copied = 0;
            int packages = 0;
            int total = 0;
            foreach (string file in files)
            {
                try
                {
                    int before = CardData.GetAll().Count;

                    //.tcgpool 卡池包：解包后资源落地本地 Art/Audio 目录，pool.json 落地本地卡池目录并注册
                    if (Path.GetExtension(file).Equals(PoolPackageIO.PoolExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        PoolPackageIO.ImportPoolPackage(file);
                        packages++;
                    }
                    else
                    {
                        //.json：复制到本地卡池目录，保证重启后自动加载（持久化）
                        string target = file;
                        string folder = CardPoolIO.SaveFolder;
                        if (!Path.GetDirectoryName(file).Equals(folder, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.CreateDirectory(folder);
                            target = Path.Combine(folder, Path.GetFileName(file));
                            File.Copy(file, target, true);
                            copied++;
                        }
                        CardPoolIO.ImportFromFile(target, true); //授予拥有数量，使构筑界面立即可用
                    }

                    total += CardData.GetAll().Count - before;
                }
                catch (Exception e)
                {
                    Debug.LogError("导入失败: " + file + " " + e.Message);
                    SetStatus("导入失败: " + Path.GetFileName(file) + " " + e.Message);
                }
            }
            SetStatus("已导入 " + files.Length + " 个文件，新增 " + total + " 张卡" +
                (packages > 0 ? "（含 " + packages + " 个卡池包，资源已落地）" : ""));
            RefreshList();
        }

        private void OnExportOne(CardPoolIO.PoolInfo info)
        {
            string folder = FileDialogTool.SelectFolder("选择导出保存目录");
            if (string.IsNullOrEmpty(folder))
                folder = CardPoolIO.SaveFolder; //不可用/取消时降级到游戏内目录
            if (ExportPool(info, folder))
                SetStatus("已导出卡池: " + info.name + " -> " + folder);
        }

        private void OnExportSelected()
        {
            List<CardPoolIO.PoolInfo> selected = GetSelectedPools();
            if (selected.Count == 0)
            {
                SetStatus("请先勾选要导出的卡池");
                return;
            }

            string folder = FileDialogTool.SelectFolder("选择导出保存目录");
            if (string.IsNullOrEmpty(folder))
                folder = CardPoolIO.SaveFolder;

            int count = 0;
            foreach (CardPoolIO.PoolInfo info in selected)
            {
                if (ExportPool(info, folder))
                    count++;
            }
            SetStatus("已导出 " + count + " 个卡池到: " + folder);
        }

        /// <summary>导出一个卡池到指定目录（内置重新序列化，本地解析后一并打包资源为 .tcgpool）</summary>
        private bool ExportPool(CardPoolIO.PoolInfo info, string folder)
        {
            try
            {
                if (info.IsReadonly)
                {
                    if (info.cards == null || info.cards.Count == 0)
                        return false;
                    PoolPackageIO.ExportToPackage(info.cards, info.name, folder);
                }
                else
                {
                    if (string.IsNullOrEmpty(info.file) || !File.Exists(info.file))
                        return false;
                    CardPoolData pool = JsonUtility.FromJson<CardPoolData>(File.ReadAllText(info.file));
                    if (pool == null || pool.cards == null || pool.cards.Count == 0)
                        return false;
                    PoolPackageIO.ExportPoolPackage(pool, info.name, folder);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("导出失败: " + info.name + " " + e.Message);
                return false;
            }
        }

        private void OnDelete(CardPoolIO.PoolInfo info)
        {
            if (CardPoolIO.DeletePoolFile(info.file))
            {
                SetStatus("已删除卡池: " + info.name);
                RefreshList();
            }
        }

        /// <summary>打开卡牌编辑器编辑该本地卡池</summary>
        private void OnEdit(CardPoolIO.PoolInfo info)
        {
            if (string.IsNullOrEmpty(info.file) || !File.Exists(info.file))
            {
                SetStatus("卡池文件不存在，无法编辑");
                return;
            }

            CardEditorPanel editor = CardEditorPanel.Get();
            if (editor == null)
                editor = FindObjectOfType<CardEditorPanel>(true); //含失活对象（默认为失活，Awake 未执行）
            if (editor == null)
            {
                SetStatus("未找到卡牌编辑器面板，请先运行「生成卡牌编辑器页面」工具");
                return;
            }

            editor.Open(info.file);
            editor.Show();
            Hide();
        }

        private void SetStatus(string msg)
        {
            if (status_text != null)
                status_text.text = msg;
        }
    }
}
