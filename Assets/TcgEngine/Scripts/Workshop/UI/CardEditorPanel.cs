using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TcgEngine;
using TcgEngine.Client;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡牌编辑器面板（P1：外壳 + 属性表单区 + 平铺节点展示）。
    /// 由 Editor 工具（CardEditorBuilder）在 Menu.unity 中生成并绑定 UI 引用，
    /// 本类运行时不动态创建界面，仅负责交互逻辑（符合项目工程约定）。
    /// P1 里程碑：能打开一个本地卡池 → 展示属性表单与平铺节点 → 保存写回 → 模拟执行验证。
    /// </summary>
    public class CardEditorPanel : UIPanel
    {
        [Header("标题/状态")]
        public Text title_text;              // 面板标题
        public Text status_text;             // 底部状态提示
        public Text file_text;               // 当前编辑的卡池文件路径

        [Header("卡牌列表区")]
        public ScrollRect card_scroll;       // 卡牌列表滚动区
        public RectTransform card_content;   // 卡牌列表容器
        public CardGrid card_grid;           // 卡牌网格（GridLayoutGroup）
        public Button btn_add_card;          // 新增卡按钮
        public Button btn_copy;              // 复制选中卡
        public Button btn_delete;            // 删除选中卡
        public Text editor_hint;             // 右侧编辑区占位提示
        private GameObject card_prefab;      // 卡面预制体（运行时从卡组构筑界面复制）
        private bool update_grid = false;    // 待刷新网格高度
        private float update_grid_timer = 0f;

        [Header("属性表单区")]
        public InputField input_name;        // 卡池名称
        public InputField input_desc;        // 卡池描述
        public InputField input_author;      // 作者

        [Header("平铺节点列表（P2 画布）")]
        public ScrollRect node_scroll;       // 节点滚动区
        public RectTransform node_content;   // 节点容器（画布）
        public GameObject node_template;     // 节点行模板（隐藏，运行时复制）

        [Header("工具栏")]
        public Button btn_save;              // 保存
        public Button btn_test;              // 模拟运行测试
        public Button btn_go;                // 进行：进入卡牌规则编辑器（GraphEditorPanel）
        public Button btn_close;             // 关闭（返回卡池管理）
        public Button btn_save2;             // 卡牌列表底部保存按钮

        private static CardEditorPanel instance;
        private string current_path;         // 当前编辑的本地卡池文件完整路径
        private CardPoolData current_pool;   // 当前编辑的卡池数据（含图）
        private CardCustomData current_card; // 当前选中的卡牌

        private readonly List<CardLine> card_lines = new List<CardLine>();   // 卡牌列表行

        private CanvasRect canvas;           // 画布容器（放节点，内含节点映射表）
        private readonly float node_h = 40f;

        private class CardLine
        {
            public CardCustomData card;
            public RectTransform rect;
            public CollectionCard ccard;
            public Image highlight;          // 选中高亮标记
        }

        public static CardEditorPanel Get() { return instance; }
        public string FilePath { get { return current_path; } }
        public CardPoolData CurrentPool { get { return current_pool; } }

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            if (btn_save != null) btn_save.onClick.AddListener(OnSave);
            if (btn_save2 != null) btn_save2.onClick.AddListener(OnSave);
            if (btn_test != null) btn_test.onClick.AddListener(OnTest);
            if (btn_go != null) btn_go.onClick.AddListener(OnGo);
            if (btn_close != null) btn_close.onClick.AddListener(OnClose);
            if (btn_add_card != null) btn_add_card.onClick.AddListener(OnAddCard);
            if (btn_copy != null) btn_copy.onClick.AddListener(OnCopyCard);
            if (btn_delete != null) btn_delete.onClick.AddListener(OnDeleteCard);
        }

        protected override void Update()
        {
            base.Update();
            //复刻卡组构筑器：延迟刷新滚动内容高度（待网格布局生效）
            update_grid_timer += Time.deltaTime;
            if (update_grid && update_grid_timer > 0.2f)
            {
                UpdateGridHeight();
                update_grid = false;
            }
        }

        /// <summary>打开指定本地卡池文件进行编辑</summary>
        public void Open(string filePath)
        {
            current_path = filePath;
            current_pool = LoadPool(filePath);

            if (current_pool == null)
            {
                current_pool = new CardPoolData();
                current_pool.name = Path.GetFileNameWithoutExtension(filePath);
            }

            RefreshForm();
            RefreshCardList();
            ConfigNodes();
            SetStatus("已打开: " + Path.GetFileName(filePath));
        }

        // ---------------- 加载 ----------------

        private CardPoolData LoadPool(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                return JsonUtility.FromJson<CardPoolData>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("读取卡池失败: " + path + " " + e.Message);
                return null;
            }
        }

        // ---------------- 表单 ----------------

        private void RefreshForm()
        {
            if (current_pool == null)
                return;
            SetInput(input_name, current_pool.name);
            SetInput(input_desc, current_pool.description);
            SetInput(input_author, current_pool.author);
            if (file_text != null)
                file_text.text = string.IsNullOrEmpty(current_path) ? "（未保存）" : current_path;
        }

        private void ReadForm()
        {
            if (current_pool == null)
                return;
            current_pool.name = GetInput(input_name, current_pool.name);
            current_pool.description = GetInput(input_desc, current_pool.description);
            current_pool.author = GetInput(input_author, current_pool.author);
        }

        // ---------------- 卡牌列表 ----------------

        /// <summary>渲染卡牌列表（从 current_pool.cards）</summary>
        private void RefreshCardList()
        {
            if (card_content == null)
                return;

            EnsureCardPrefab();

            //清除旧网格卡
            for (int i = card_content.childCount - 1; i >= 0; i--)
                Destroy(card_content.GetChild(i).gameObject);
            card_lines.Clear();

            if (current_pool == null)
                return;

            foreach (CardCustomData card in current_pool.cards)
                CreateCardThumb(card);

            //默认选中第一张（若有），否则清空选中
            if (card_lines.Count > 0)
                OnSelectCard(card_lines[0].card);
            else
                OnSelectCard(null);

            //触发网格高度刷新（仿 CollectionPanel 延迟计算）
            update_grid = true;
            update_grid_timer = 0f;
        }

        /// <summary>运行时从卡组构筑界面复制卡面预制体与网格参数，保证每排张数与卡牌界面一致</summary>
        private void EnsureCardPrefab()
        {
            CollectionPanel collection = FindObjectOfType<CollectionPanel>(true);
            if (collection == null || collection.grid_content == null)
                return;

            // 卡面预制体：仅当未绑定时复制
            if (card_prefab == null && collection.card_prefab != null)
                card_prefab = collection.card_prefab;

            // 网格参数完全复制（含 constraint），保证与卡牌界面每排张数一致
            GridLayoutGroup src = collection.grid_content.GetGrid();
            GridLayoutGroup dst = card_grid != null ? card_grid.GetGrid() : null;
            if (src != null && dst != null)
            {
                if (src.cellSize.x > 0 && src.cellSize.y > 0)
                    dst.cellSize = src.cellSize;
                dst.spacing = src.spacing;
                dst.constraint = src.constraint;
                dst.constraintCount = src.constraintCount;
            }
            if (dst != null)
            {
                dst.padding = new RectOffset(10, 10, 10, 10);
                dst.childAlignment = TextAnchor.UpperLeft;
                dst.startAxis = GridLayoutGroup.Axis.Horizontal;
            }
        }

        /// <summary>按行数更新滚动内容高度（仿 CollectionPanel.LateUpdate，列数计算更稳健）</summary>
        private void UpdateGridHeight()
        {
            if (card_content == null || card_grid == null)
                return;
            if (card_content.childCount == 0)
                return;

            GridLayoutGroup grid = card_grid.GetGrid();
            if (grid == null)
                return;

            //列数：固定列则直接用 constraintCount，否则按内容宽度估算
            int cols = grid.constraintCount;
            if (grid.constraint == GridLayoutGroup.Constraint.Flexible || cols < 1)
            {
                float cell_w = grid.cellSize.x + grid.spacing.x;
                float content_w = card_content.rect.width - grid.padding.horizontal;
                cols = Mathf.Max(1, Mathf.FloorToInt((content_w + grid.spacing.x) / cell_w));
            }
            if (cols < 1)
                cols = 1;

            int rows = Mathf.CeilToInt(card_content.childCount / (float)cols);
            float row_height = grid.cellSize.y + grid.spacing.y;
            float height = rows * row_height;
            Vector2 sd = card_content.sizeDelta;
            card_content.sizeDelta = new Vector2(sd.x, height + 100);
        }

        private void CreateCardThumb(CardCustomData cdata)
        {
            if (card_prefab == null || card_grid == null)
                return;

            //本地卡 CardData（已由 CardPoolIO 注入静态字典；新增未导出的卡则运行时构建注册）
            CardData card = CardData.Get(cdata.id);
            if (card == null)
            {
                card = CardPoolIO.BuildCardData(cdata);
                if (card != null)
                    CardPoolIO.RegisterCard(card);
            }
            if (card == null)
                return;

            GameObject inst = Instantiate(card_prefab, card_content);
            CollectionCard ccard = inst.GetComponent<CollectionCard>();
            if (ccard == null)
            {
                Destroy(inst);
                return;
            }

            VariantData variant = VariantData.GetDefault();
            ccard.SetCard(card, variant, 0);

            //高亮标记（放在选中时叠加一层，简化：用卡面的翻转角度/透明度区分）
            Image highlight = inst.transform.Find("Selected")?.GetComponent<Image>();

            CardLine entry = new CardLine();
            entry.card = cdata;
            entry.rect = inst.GetComponent<RectTransform>();
            entry.ccard = ccard;
            entry.highlight = highlight;
            if (highlight != null)
                highlight.enabled = false;

            //点击选中
            ccard.onClick += _ => OnSelectCardId(cdata.id);
            ccard.onClickRight += _ => CardZoomPanel.Get().ShowCard(card, variant);

            card_lines.Add(entry);
        }

        /// <summary>选中某张卡，高亮对应卡面并刷新右侧提示</summary>
        private void OnSelectCard(CardCustomData card)
        {
            current_card = card;
            foreach (CardLine entry in card_lines)
            {
                bool sel = entry.card == card;
                if (entry.ccard != null)
                    entry.ccard.SetGrayscale(!sel);
                if (entry.rect != null)
                    entry.rect.localScale = sel ? Vector3.one * 1.06f : Vector3.one;
            }

            if (editor_hint != null)
            {
                if (card == null)
                    editor_hint.text = "属性编辑区\n（暂无卡牌，点击「新增卡」添加）";
                else
                    editor_hint.text = "已选中: " + (string.IsNullOrEmpty(card.title) ? "（未命名）" : card.title)
                        + "\n右侧属性编辑将在后续版本提供";
            }
        }

        /// <summary>按 id 选中卡牌（来自 CollectionCard 点击）</summary>
        private void OnSelectCardId(string id)
        {
            if (current_pool == null)
                return;
            foreach (CardCustomData card in current_pool.cards)
            {
                if (card.id == id)
                {
                    OnSelectCard(card);
                    return;
                }
            }
        }

        /// <summary>新增一张默认卡牌（0费/中立/随从）并选中</summary>
        private void OnAddCard()
        {
            if (current_pool == null)
                return;

            CardCustomData card = new CardCustomData();
            card.id = "custom_" + GameTool.GenerateRandomID(8, 12);
            card.title = "新卡 " + (current_pool.cards.Count + 1);
            card.type = "Character";   // 随从
            card.team = "";
            card.rarity = "";
            card.mana = 0;             // 默认 0 费
            card.attack = 0;
            card.hp = 0;
            card.deckbuilding = true;
            card.abilities = new List<AbilityCustomData>();

            current_pool.cards.Add(card);
            CreateCardThumb(card);
            OnSelectCard(card);
            update_grid = true;
            update_grid_timer = 0f;
            SetStatus("已新增卡牌: " + card.title + "（0 费）");
        }

        /// <summary>复制当前选中卡，插入其后并选中</summary>
        private void OnCopyCard()
        {
            if (current_pool == null || current_card == null)
            {
                SetStatus("请先选择一张卡牌再复制");
                return;
            }

            CardCustomData copy = CloneCard(current_card);
            copy.id = "custom_" + GameTool.GenerateRandomID(8, 12);
            copy.title = current_card.title + " 复制";

            int index = current_pool.cards.IndexOf(current_card);
            if (index < 0)
                index = current_pool.cards.Count - 1;
            current_pool.cards.Insert(index + 1, copy);

            RefreshCardList();
            OnSelectCardId(copy.id);
            SetStatus("已复制卡牌: " + copy.title + "（记得保存）");
        }

        /// <summary>删除当前选中卡</summary>
        private void OnDeleteCard()
        {
            if (current_pool == null || current_card == null)
            {
                SetStatus("请先选择一张卡牌再删除");
                return;
            }

            string title = current_card.title;
            current_pool.cards.Remove(current_card);
            current_card = null;

            RefreshCardList();
            SetStatus("已删除卡牌: " + title + "（记得保存）");
        }

        /// <summary>深拷贝卡牌数据（JSON 往返，避免引用同一对象）</summary>
        private static CardCustomData CloneCard(CardCustomData src)
        {
            return JsonUtility.FromJson<CardCustomData>(JsonUtility.ToJson(src));
        }

        /// <summary>把卡牌列表写回（当前仅需保证列表字段已绑定在 current_pool 上，保存时直接序列化）</summary>
        private void ReadCardList()
        {
            //此行占位：current_pool.cards 保存时整体写回 JSON，无需额外读回逻辑
        }

        /// <summary>CardType 枚举名 → 中文显示</summary>
        private static string ToChinese(string type)
        {
            switch (type)
            {
                case "Hero": return "英雄";
                case "Character": return "随从";
                case "Spell": return "法术";
                case "Artifact": return "神器";
                case "Secret": return "奥秘";
                case "Equipment": return "装备";
                default: return string.IsNullOrEmpty(type) ? "随从" : type;
            }
        }

        // ---------------- 画布节点展示（P2 可拖拽） ----------------

        /// <summary>
        /// 画布容器：承载所有节点行，维护「运行时行 RectTransform → GraphNode」映射，
        /// 提供屏幕坐标到画布局部坐标的换算与节点移动逻辑。
        /// </summary>
        public class CanvasRect
        {
            public RectTransform root;                       // 画布容器 RectTransform
            public Dictionary<string, RectTransform> rows;   // 节点id → 行

            public CanvasRect(RectTransform root)
            {
                this.root = root;
                rows = new Dictionary<string, RectTransform>();
            }

            /// <summary>记录某个节点的行引用</summary>
            public void Register(string node_id, RectTransform row)
            {
                if (rows.ContainsKey(node_id))
                    rows[node_id] = row;
                else
                    rows.Add(node_id, row);
            }

            /// <summary>把某行按屏幕像素增量移动（delta 为屏幕增量，直接累加到画布局部坐标）</summary>
            public void MoveRow(RectTransform row, PointerEventData eventData)
            {
                row.anchoredPosition += eventData.delta;
            }
        }

        private void ConfigNodes()
        {
            if (node_content == null)
                return;

            //清除旧节点（保留模板）
            for (int i = node_content.childCount - 1; i >= 0; i--)
            {
                Transform child = node_content.GetChild(i);
                if (child != null && child.gameObject != node_template)
                    Destroy(child.gameObject);
            }

            //创建画布容器：大尺寸、无自动布局，节点自由定位
            if (canvas == null)
                canvas = new CanvasRect(node_content);

            GraphData graph = current_pool != null ? current_pool.graph : null;
            if (graph == null || graph.nodes.Count == 0)
            {
                SetStatus("该卡池没有规则图，可在编辑器中新建节点。");
                return;
            }

            foreach (GraphNode node in graph.nodes)
                CreateNodeLine(node, graph);
        }

        private void CreateNodeLine(GraphNode node, GraphData graph)
        {
            if (node_template == null)
                return;

            GameObject line = Instantiate(node_template, node_content);
            line.name = "Node_" + node.id;
            line.SetActive(true);

            RectTransform rect = line.GetComponent<RectTransform>();
            //节点在画布内自由定位：anchor+pivot 固定左下角，位置由 node.pos 决定（与模板一致）
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(node.pos.x, node.pos.y);

            Text type_text = line.transform.Find("TypeText")?.GetComponent<Text>();
            if (type_text != null)
                type_text.text = NodeTypeLabel(node.type);

            Text title_text = line.transform.Find("TitleText")?.GetComponent<Text>();
            if (title_text != null)
                title_text.text = node.title;

            Text desc_text = line.transform.Find("DescText")?.GetComponent<Text>();
            if (desc_text != null)
                desc_text.text = NodeSummary(node);

            //挂拖拽组件并登记到画布
            if (canvas != null)
                canvas.Register(node.id, rect);
            NodeDragger dragger = line.GetComponent<NodeDragger>();
            if (dragger == null)
                dragger = line.AddComponent<NodeDragger>();
            dragger.Setup(node.id, this);
        }

        /// <summary>拖拽中：把行按屏幕增量移动</summary>
        public void MoveNode(string node_id, RectTransform row, PointerEventData eventData)
        {
            if (canvas != null)
                canvas.MoveRow(row, eventData);
        }

        /// <summary>拖拽结束：把行位置写回对应 GraphNode.pos</summary>
        public void OnNodeMoved(string node_id, Vector2 pos)
        {
            if (current_pool == null || current_pool.graph == null)
                return;
            GraphNode node = current_pool.graph.GetNode(node_id);
            if (node == null)
                return;
            node.pos = new Vector2Data(pos.x, pos.y);
            SetStatus("节点已移动 (" + Mathf.RoundToInt(pos.x) + "," + Mathf.RoundToInt(pos.y) + ")，记得保存");
        }

        private static string NodeTypeLabel(GraphNodeType type)
        {
            switch (type)
            {
                case GraphNodeType.Event: return "触发";
                case GraphNodeType.Condition: return "条件";
                case GraphNodeType.Action: return "动作";
                case GraphNodeType.Value: return "数值";
                default: return "节点";
            }
        }

        private static string NodeSummary(GraphNode node)
        {
            string s = "<color=#9FD5FF>" + node.action + "</color>";
            foreach (FieldCustomData f in node.fields)
                s += "  " + f.name + "=" + f.value;
            //端口概要（▸输出 ◂输入）
            if (node.pins.Count > 0)
            {
                s += "  [";
                for (int i = 0; i < node.pins.Count; i++)
                {
                    GraphPin p = node.pins[i];
                    if (i > 0)
                        s += " ";
                    s += (p.is_output ? "▸" : "◂") + p.display_name;
                }
                s += "]";
            }
            return s;
        }

        // ---------------- 保存/测试 ----------------

        private void OnSave()
        {
            if (current_pool == null)
            {
                SetStatus("没有可保存的卡池");
                return;
            }

            ReadForm();

            //确保保存前图非空（无图则生成一张空图，保证结构完整）
            if (current_pool.graph == null)
                current_pool.graph = new GraphData();
            current_pool.graph.name = current_pool.name;

            string path = current_path;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(CardPoolIO.SaveFolder, current_pool.name + ".json");
                current_path = path;
            }

            try
            {
                Directory.CreateDirectory(CardPoolIO.SaveFolder);
                File.WriteAllText(path, JsonUtility.ToJson(current_pool, true));
                if (file_text != null)
                    file_text.text = path;
                SetStatus("已保存: " + Path.GetFileName(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("保存失败: " + e.Message);
                SetStatus("保存失败: " + e.Message);
            }
        }

        /// <summary>关闭编辑器并返回上一层页面（卡池管理页；兜底回首页）</summary>
        private void OnClose()
        {
            Hide();

            CardPoolPanel pool = CardPoolPanel.Get();
            if (pool == null)
                pool = FindObjectOfType<CardPoolPanel>(true);
            if (pool != null)
                pool.Show();
            else if (HomePanel.Get() != null)
                HomePanel.Get().ReturnHome();
        }

        /// <summary>「模拟测试」：跳转人机战斗。我方卡组由当前选中卡组成（一整套全是这张卡），
        /// AI 用随机初始卡池，开局双方法力直接为上限。</summary>
        private void OnTest()
        {
            if (current_card == null)
            {
                SetStatus("请先在卡牌列表中选择一张卡牌进行模拟测试");
                return;
            }

            //确保该卡运行时数据已注册且为最新（含规则图编译的能力）
            CardData card = CardData.Get(current_card.id);
            if (card == null)
            {
                card = CardPoolIO.BuildCardData(current_card);
                if (card != null)
                    CardPoolIO.RegisterCard(card);
            }
            else
            {
                CardPoolIO.UpdateCardData(current_card); //同步最新属性与规则图能力
            }
            if (card == null)
            {
                SetStatus("无法构建测试卡牌数据，请先保存卡池");
                return;
            }

            //我方卡组：一整套全是这张卡
            int deck_size = GameplayData.Get().deck_size;
            UserDeckData test_deck = new UserDeckData();
            test_deck.tid = "test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            test_deck.title = "测试 - " + (string.IsNullOrEmpty(current_card.title) ? card.title : current_card.title);
            test_deck.hero = GetDefaultHero();
            test_deck.cards = new UserCardData[]
            {
                new UserCardData { tid = card.id, variant = VariantData.GetDefault().id, quantity = deck_size }
            };

            //AI：随机初始卡池
            DeckData ai_data = GameplayData.Get().GetRandomAIDeck();
            if (ai_data == null)
            {
                SetStatus("没有可用的 AI 初始卡池");
                return;
            }
            UserDeckData ai_deck = new UserDeckData(ai_data);

            //设置对战参数并跳转人机战斗
            GameClient.player_settings.deck = test_deck;
            GameClient.ai_settings.deck = ai_deck;
            GameClient.ai_settings.ai_level = GameplayData.Get().ai_level;
            GameClient.game_settings.test_full_mana = true;

            MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual);
        }

        /// <summary>取一张默认英雄卡作为测试卡组的英雄</summary>
        private UserCardData GetDefaultHero()
        {
            foreach (CardData c in CardData.GetAll())
            {
                if (c.type == CardType.Hero)
                    return new UserCardData(c, VariantData.GetDefault());
            }
            return new UserCardData();
        }

        /// <summary>「进行」：进入全屏卡牌规则编辑器，编辑当前选中卡自己的规则图</summary>
        private void OnGo()
        {
            if (current_card == null)
            {
                SetStatus("请先在卡牌列表中选择一张卡牌");
                return;
            }

            GraphEditorPanel editor = GraphEditorPanel.Get();
            if (editor == null)
                editor = FindObjectOfType<GraphEditorPanel>(true); //含失活对象
            if (editor == null)
            {
                SetStatus("未找到规则编辑器面板，请先运行「生成规则编辑器页面」工具");
                return;
            }

            editor.Open(current_pool, current_card, current_path);
            editor.Show();
            Hide();
        }

        /// <summary>规则编辑器关闭后返回：重新渲染卡面（属性已在规则编辑器中修改并同步到运行时 CardData）</summary>
        public void NotifyGraphClosed()
        {
            string keep_id = current_card != null ? current_card.id : null;
            RefreshCardList();
            //重新选中之前编辑的卡
            if (!string.IsNullOrEmpty(keep_id))
                OnSelectCardId(keep_id);
            if (current_card != null)
                SetStatus("规则编辑完成: " + current_card.title + "（记得保存卡池）");
        }

        // ---------------- UI 辅助 ----------------

        private static void SetInput(InputField field, string val)
        {
            if (field != null)
                field.text = val ?? "";
        }

        private static string GetInput(InputField field, string def)
        {
            if (field == null)
                return def;
            return string.IsNullOrEmpty(field.text) ? def : field.text;
        }

        private void SetStatus(string msg)
        {
            if (status_text != null)
                status_text.text = msg;
        }
    }
}