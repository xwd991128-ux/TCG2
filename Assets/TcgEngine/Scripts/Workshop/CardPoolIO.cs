using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 卡池导入/导出核心
    /// 导出：CardData → CardCustomData(DTO) → JSON 文件
    /// 导入：JSON 文件 → DTO → 运行时生成 CardData/AbilityData 实例 → 注入静态字典
    /// 自定义卡池存放目录：Application.persistentDataPath/Workshop/*.json（启动时由 DataLoader 自动加载）
    /// </summary>
    public static class CardPoolIO
    {
        /// <summary>自定义卡池存放目录</summary>
        public static string SaveFolder
        {
            get { return Path.Combine(Application.persistentDataPath, "Workshop"); }
        }

        /// <summary>自定义卡牌图片存放目录</summary>
        public static string ArtFolder
        {
            get { return Path.Combine(SaveFolder, "Art"); }
        }

        /// <summary>自定义卡牌音频存放目录</summary>
        public static string AudioFolder
        {
            get { return Path.Combine(SaveFolder, "Audio"); }
        }

        /// <summary>从 ArtFolder 加载卡牌图片（不存在返回 null）</summary>
        public static Sprite LoadArt(string art_path)
        {
            if (string.IsNullOrEmpty(art_path))
                return null;
            try
            {
                string path = Path.Combine(ArtFolder, art_path);
                if (!File.Exists(path))
                    return null;
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                    return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (System.Exception e)
            {
                Debug.LogError("加载卡牌图片失败: " + art_path + " " + e.Message);
                return null;
            }
        }

        //记录运行时导入/创建的自定义卡牌 id，用于"仅导出自定义卡"
        private static readonly HashSet<string> custom_ids = new HashSet<string>();

        //记录每个本地卡池文件注册的卡牌 id，删除卡池时按文件从内存卸载对应卡
        private static readonly Dictionary<string, List<string>> pool_file_cards = new Dictionary<string, List<string>>();

        /// <summary>运行时导入的自定义卡牌列表</summary>
        public static List<CardData> GetCustomCards()
        {
            List<CardData> list = new List<CardData>();
            foreach (CardData card in CardData.GetAll())
            {
                if (card != null && custom_ids.Contains(card.id))
                    list.Add(card);
            }
            return list;
        }

        /// <summary>本地已有的卡池 JSON 文件名列表</summary>
        public static List<string> GetPoolFiles()
        {
            List<string> files = new List<string>();
            if (!Directory.Exists(SaveFolder))
                return files;
            files.AddRange(Directory.GetFiles(SaveFolder, "*.json"));
            return files;
        }

        /// <summary>删除本地卡池文件（同时从内存卸载该文件注册的卡牌）</summary>
        public static bool DeletePoolFile(string path)
        {
            if (!File.Exists(path))
                return false;
            //先从内存卸载该文件注册的自定义卡，使卡牌构筑等界面即时减少
            UnloadPoolCards(path);
            File.Delete(path);
            return true;
        }

        /// <summary>按文件从内存卸载自定义卡牌</summary>
        private static void UnloadPoolCards(string fileKey)
        {
            if (pool_file_cards.TryGetValue(fileKey, out List<string> ids))
            {
                foreach (string id in ids)
                    RemoveCard(id);
                pool_file_cards.Remove(fileKey);
            }
        }

        /// <summary>从静态字典移除一张运行时自定义卡</summary>
        private static void RemoveCard(string id)
        {
            if (CardData.card_dict.TryGetValue(id, out CardData card))
            {
                CardData.card_list.Remove(card);
                CardData.card_dict.Remove(id);
            }
            custom_ids.Remove(id);
        }

        /// <summary>把卡牌列表导出为 JSON 文件到指定目录（玩家自选路径）</summary>
        public static void ExportToPath(List<CardData> cards, string poolName, string directory)
        {
            if (cards == null || cards.Count == 0)
                return;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, poolName + ".json");
            string json = ExportToJson(cards, poolName);
            File.WriteAllText(path, json);
            Debug.Log("已导出卡池到: " + path + "（共 " + cards.Count + " 张卡）");
        }

        // ---------------- 卡池列表模型 ----------------

        /// <summary>
        /// 一个卡池条目：内置卡池（按卡包）或本地卡池（JSON 文件）
        /// </summary>
        public class PoolInfo
        {
            public string name;              // 显示名称
            public string source;            // "builtin" 内置 / "local" 本地
            public string file;              // 本地文件完整路径（local 时有效）
            public PackData pack;            // 对应卡包（builtin 时有效）
            public int card_count;           // 卡牌数量
            public List<CardData> cards;     // 卡牌列表（builtin 时直接可用）

            public bool IsReadonly { get { return source == "builtin"; } }
        }

        /// <summary>内置卡池：按卡包划分（每个卡包一个池，池内卡属于该包）</summary>
        public static List<PoolInfo> GetBuiltinPools()
        {
            List<PoolInfo> list = new List<PoolInfo>();
            foreach (PackData pack in PackData.GetAll())
            {
                List<CardData> cards = CardData.GetAll(pack);
                if (cards.Count == 0)
                    continue;
                PoolInfo info = new PoolInfo();
                info.name = string.IsNullOrEmpty(pack.title) ? pack.id : pack.title;
                info.source = "builtin";
                info.pack = pack;
                info.card_count = cards.Count;
                info.cards = cards;
                list.Add(info);
            }
            return list;
        }

        /// <summary>本地卡池：Workshop 目录下的 JSON 文件</summary>
        public static List<PoolInfo> GetLocalPools()
        {
            List<PoolInfo> list = new List<PoolInfo>();
            foreach (string file in GetPoolFiles())
            {
                PoolInfo info = new PoolInfo();
                info.name = Path.GetFileNameWithoutExtension(file);
                info.source = "local";
                info.file = file;
                info.card_count = CountCardsInFile(file);
                info.cards = null;
                list.Add(info);
            }
            return list;
        }

        /// <summary>统计本地 JSON 卡池的卡牌数量（只解析不实例化）</summary>
        public static int CountCardsInFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return 0;
                string json = File.ReadAllText(path);
                CardPoolData pool = JsonUtility.FromJson<CardPoolData>(json);
                if (pool != null && pool.cards != null)
                    return pool.cards.Count;
            }
            catch (Exception) { }
            return 0;
        }

        // ---------------- 卡池筛选（供卡牌构筑界面） ----------------

        /// <summary>一个可选卡池：key 为 ""（全部）/ "pack:xxx"（内置卡包）/ "file:xxx"（本地文件）</summary>
        public class PoolOption
        {
            public string key;
            public string label;
        }

        /// <summary>构筑界面的卡池下拉选项：全部 + 内置各卡包 + 本地各卡池</summary>
        public static List<PoolOption> GetPoolOptions()
        {
            List<PoolOption> list = new List<PoolOption>();
            list.Add(new PoolOption { key = "", label = "全部卡池" });

            foreach (PackData pack in PackData.GetAll())
            {
                if (CardData.GetAll(pack).Count == 0)
                    continue;
                string label = string.IsNullOrEmpty(pack.title) ? pack.id : pack.title;
                list.Add(new PoolOption { key = "pack:" + pack.id, label = label });
            }

            foreach (string file in GetPoolFiles())
                list.Add(new PoolOption { key = "file:" + file, label = Path.GetFileNameWithoutExtension(file) });

            return list;
        }

        /// <summary>判断一张卡是否属于所选卡池（key 为空表示全部，返回 true）</summary>
        public static bool IsCardInPool(CardData card, string key)
        {
            if (card == null || string.IsNullOrEmpty(key))
                return true;

            if (key.StartsWith("pack:"))
            {
                PackData pack = PackData.Get(key.Substring(5));
                return pack != null && card.HasPack(pack);
            }

            if (key.StartsWith("file:"))
            {
                string file = key.Substring(5);
                return pool_file_cards.TryGetValue(file, out List<string> ids) && ids.Contains(card.id);
            }

            return true;
        }

        // ---------------- 导出 ----------------

        /// <summary>把卡牌列表导出为 CardPoolData</summary>
        public static CardPoolData BuildPool(List<CardData> cards, string poolName, string author = "")
        {
            CardPoolData pool = new CardPoolData();
            pool.name = poolName;
            pool.author = author;
            pool.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            foreach (CardData card in cards)
            {
                if (card != null)
                    pool.cards.Add(CardToData(card));
            }
            return pool;
        }

        /// <summary>把卡牌列表导出为 JSON 字符串</summary>
        public static string ExportToJson(List<CardData> cards, string poolName, string author = "")
        {
            CardPoolData pool = BuildPool(cards, poolName, author);
            return JsonUtility.ToJson(pool, true);
        }

        /// <summary>把卡牌列表导出为 JSON 文件（保存到 SaveFolder）</summary>
        public static void ExportToFile(List<CardData> cards, string poolName, string author = "")
        {
            string json = ExportToJson(cards, poolName, author);
            string path = Path.Combine(SaveFolder, poolName + ".json");
            Directory.CreateDirectory(SaveFolder);
            File.WriteAllText(path, json);
            Debug.Log("已导出卡池到: " + path + "（共 " + cards.Count + " 张卡）");
        }

        /// <summary>CardData → CardCustomData</summary>
        public static CardCustomData CardToData(CardData card)
        {
            CardCustomData data = new CardCustomData();
            data.id = card.id;
            data.title = card.title;
            data.type = card.type.ToString();
            data.team = card.team != null ? card.team.id : "";
            data.rarity = card.rarity != null ? card.rarity.id : "";
            data.trait = card.traits != null && card.traits.Length > 0 && card.traits[0] != null ? card.traits[0].id : "";
            data.mana = card.mana;
            data.attack = card.attack;
            data.hp = card.hp;
            data.text = card.text;
            data.desc = card.desc;
            data.deckbuilding = card.deckbuilding;
            data.cost = card.cost;

            if (card.abilities != null)
            {
                foreach (AbilityData ability in card.abilities)
                {
                    if (ability != null)
                        data.abilities.Add(AbilityToData(ability));
                }
            }
            return data;
        }

        /// <summary>AbilityData → AbilityCustomData</summary>
        public static AbilityCustomData AbilityToData(AbilityData ability)
        {
            AbilityCustomData data = new AbilityCustomData();
            data.id = ability.id;
            data.trigger = ability.trigger.ToString();
            data.target = ability.target.ToString();
            data.value = ability.value;
            data.duration = ability.duration;
            data.mana_cost = ability.mana_cost;
            data.exhaust = ability.exhaust;
            data.title = ability.title;
            data.desc = ability.desc;

            if (ability.status != null)
            {
                foreach (StatusData status in ability.status)
                {
                    if (status != null)
                        data.status_ids.Add(status.effect.ToString());
                }
            }

            if (ability.chain_abilities != null)
            {
                foreach (AbilityData chain in ability.chain_abilities)
                {
                    if (chain != null)
                        data.chain_ability_ids.Add(chain.id);
                }
            }

            data.effects = SerializeComponents(ability.effects);
            data.conditions_trigger = SerializeComponents(ability.conditions_trigger);
            data.conditions_target = SerializeComponents(ability.conditions_target);
            data.filters_target = SerializeComponents(ability.filters_target);
            return data;
        }

        // ---------------- 导入 ----------------

        /// <summary>启动时加载本地自定义卡池目录下所有 JSON</summary>
        public static void LoadCustomPools()
        {
            if (!Directory.Exists(SaveFolder))
                return;

            string[] files = Directory.GetFiles(SaveFolder, "*.json");
            foreach (string file in files)
            {
                try
                {
                    ImportFromFile(file);
                }
                catch (Exception e)
                {
                    Debug.LogError("加载自定义卡池失败: " + file + "\n" + e.Message);
                }
            }
        }

        /// <summary>从 JSON 文件导入卡池并注册到游戏</summary>
        /// <param name="grantOwnership">是否授予玩家拥有数量（玩家主动导入时 true；启动自动加载时 false，避免重复累加）</param>
        public static void ImportFromFile(string path, bool grantOwnership = false)
        {
            if (!File.Exists(path))
                return;

            string json = File.ReadAllText(path);
            CardPoolData pool = JsonUtility.FromJson<CardPoolData>(json);
            if (pool == null)
            {
                Debug.LogError("卡池 JSON 解析失败: " + path);
                return;
            }

            int count = ImportToGame(pool, path, grantOwnership);
            Debug.Log("已导入卡池「" + pool.name + "」，新增 " + count + " 张卡: " + path);
        }

        /// <summary>将 CardPoolData 注册到游戏（返回实际新增卡牌数）</summary>
        /// <param name="fileKey">卡池文件路径（用于删除时按文件卸载），为空则不做归属记录</param>
        public static int ImportToGame(CardPoolData pool, string fileKey = "", bool grantOwnership = false)
        {
            if (pool == null || pool.cards == null)
                return 0;

            int added = 0;
            foreach (CardCustomData cdata in pool.cards)
            {
                CardData card = BuildCardData(cdata);
                if (card != null && RegisterCard(card))
                {
                    added++;
                    if (!string.IsNullOrEmpty(fileKey))
                    {
                        if (!pool_file_cards.TryGetValue(fileKey, out List<string> list))
                        {
                            list = new List<string>();
                            pool_file_cards[fileKey] = list;
                        }
                        list.Add(card.id);
                    }
                    if (grantOwnership)
                        GrantOwnership(card);
                }
            }
            return added;
        }

        /// <summary>授予玩家拥有该自定义卡（默认变体 2 张），使其可正常构筑</summary>
        private static void GrantOwnership(CardData card)
        {
            VariantData variant = VariantData.GetDefault();
            Authenticator auth = Authenticator.Get();
            if (auth == null || variant == null)
                return;
            UserData udata = auth.UserData;
            if (udata != null)
                udata.AddCard(card.id, variant.id, 2);
        }

        /// <summary>CardCustomData → CardData（运行时实例）</summary>
        public static CardData BuildCardData(CardCustomData data)
        {
            if (data == null || string.IsNullOrEmpty(data.id))
                return null;

            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.id = data.id;
            card.title = data.title ?? "";
            card.type = ParseEnum(data.type, CardType.None);
            card.team = string.IsNullOrEmpty(data.team) ? GetFirstTeam() : TeamData.Get(data.team);
            card.rarity = string.IsNullOrEmpty(data.rarity) ? RarityData.GetFirst() : RarityData.Get(data.rarity);
            if (string.IsNullOrEmpty(data.trait))
                card.traits = new TraitData[0];
            else
            {
                TraitData trait = TraitData.Get(data.trait);
                card.traits = trait != null ? new TraitData[] { trait } : new TraitData[0];
            }
            card.mana = data.mana;
            card.attack = data.attack;
            card.hp = data.hp;
            card.text = data.text ?? "";
            card.desc = data.desc ?? "";
            card.deckbuilding = data.deckbuilding;
            card.cost = data.cost;
            card.art_board = LoadArt(data.art_path);
            card.art_full = LoadArt(data.art_full_path);

            List<AbilityData> abilities = new List<AbilityData>();
            foreach (AbilityCustomData adata in data.abilities)
            {
                AbilityData ability = BuildAbilityData(adata);
                if (ability != null)
                {
                    RegisterAbility(ability);
                    abilities.Add(ability);
                }
            }
            //规则图编译为能力（图 → AbilityData），使规则编辑器画的图在真实对战中生效
            abilities.AddRange(CompileGraphAbilities(data));
            card.abilities = abilities.ToArray();
            //数组字段置空数组而非 null，避免 Card.SetCard/SetTraits 等遍历时报空引用
            card.stats = new TraitStat[0];
            card.packs = new PackData[0];
            //异步补载 4 个音频槽（spawn/attack/death/damage）写回 CardData，使自定义音效真实可播
            CardAudioLoader.LoadCardAudio(data, card);
            return card;
        }

        /// <summary>更新运行时已注册的自定义卡数据（实例引用不变，仅更新字段）。
        /// 规则编辑器保存后调用，使卡牌构筑/编辑器卡面立即反映最新属性；未注册则构建并注册。</summary>
        public static void UpdateCardData(CardCustomData data)
        {
            if (data == null || string.IsNullOrEmpty(data.id))
                return;

            CardData card = CardData.Get(data.id);
            if (card == null)
            {
                card = BuildCardData(data);
                if (card != null)
                    RegisterCard(card);
                return;
            }

            //仅更新基础属性（abilities 由其他流程维护，此处不重建）
            card.title = data.title ?? "";
            card.type = ParseEnum(data.type, CardType.None);
            card.team = string.IsNullOrEmpty(data.team) ? GetFirstTeam() : TeamData.Get(data.team);
            card.rarity = string.IsNullOrEmpty(data.rarity) ? RarityData.GetFirst() : RarityData.Get(data.rarity);
            if (string.IsNullOrEmpty(data.trait))
                card.traits = new TraitData[0];
            else
            {
                TraitData trait = TraitData.Get(data.trait);
                card.traits = trait != null ? new TraitData[] { trait } : new TraitData[0];
            }
            card.mana = data.mana;
            card.attack = data.attack;
            card.hp = data.hp;
            card.text = data.text ?? "";
            card.desc = data.desc ?? "";
            card.deckbuilding = data.deckbuilding;
            card.cost = data.cost;
            card.art_board = LoadArt(data.art_path);
            card.art_full = LoadArt(data.art_full_path);

            //重建能力（data.abilities + 规则图编译），使规则编辑器保存的图/能力在真实对战中立即生效
            List<AbilityData> abilities = new List<AbilityData>();
            foreach (AbilityCustomData adata in data.abilities)
            {
                AbilityData ability = BuildAbilityData(adata);
                if (ability != null)
                {
                    RegisterAbility(ability);
                    abilities.Add(ability);
                }
            }
            abilities.AddRange(CompileGraphAbilities(data));
            card.abilities = abilities.ToArray();
            //异步补载音频，使规则编辑器保存的音效立即生效（编辑器保存后调用本方法）
            CardAudioLoader.LoadCardAudio(data, card);
        }

        // ---------------- 规则图 → 能力编译 ----------------

        /// <summary>把卡的规则图（GraphData）编译为 AbilityData 数组，复用现有对账结算体系。
        /// 目前支持：Event(OnPlay/StartOfTurn/EndOfTurn/OnDeath/OnAttack) → Action(Draw/Heal/Damage)。
        /// 其余节点（条件/值）与未支持动作暂时跳过并警告。</summary>
        private static List<AbilityData> CompileGraphAbilities(CardCustomData data)
        {
            List<AbilityData> result = new List<AbilityData>();
            if (data == null || data.graph == null)
                return result;

            GraphData graph = data.graph;
            foreach (GraphNode ev in graph.nodes)
            {
                if (ev == null || ev.type != GraphNodeType.Event)
                    continue;

                AbilityTrigger trigger = MapGraphTrigger(ev.action);
                if (trigger == AbilityTrigger.None)
                {
                    Debug.LogWarning("[规则图] 触发器未支持，跳过: " + ev.action);
                    continue;
                }

                List<GraphNode> acts = FindReachableActions(graph, ev.id);
                bool is_spell = string.Equals(data.type, "Spell", StringComparison.OrdinalIgnoreCase);

                //NodeDoc(zmcs) 动作：挂 EffectRunGraph 由解释器在真实对局执行（按事件单独挂载）
                bool has_node_doc = false;
                foreach (GraphNode act in acts)
                {
                    if (act != null && !string.IsNullOrEmpty(act.category))
                    {
                        has_node_doc = true;
                        break;
                    }
                }
                if (has_node_doc)
                {
                    AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
                    ab.id = "graph_" + data.id + "_" + ev.action + "_node" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    ab.trigger = trigger;
                    //v1 目标模式：法术且图中含"需选择目标"类动作(伤害/消灭/治疗目标卡) → PlayTarget 弹出选择
                    bool wants_target = false;
                    foreach (GraphNode act in acts)
                    {
                        if (act != null && !string.IsNullOrEmpty(act.category)
                            && (act.action == "202001" || act.action == "202016"
                                || act.action == "202013" || act.action == "202039" || act.action == "202047"))
                        {
                            wants_target = true;
                            break;
                        }
                    }
                    ab.target = (is_spell && wants_target) ? AbilityTarget.PlayTarget : AbilityTarget.None;
                    if (wants_target && !is_spell)
                        Debug.LogWarning("[规则图] " + (ev.title ?? ev.action)
                            + " 下游动作需要选择目标，但该卡类型不是法术 → 效果不会执行（请把卡类型改为法术，或改用无需目标的动作）");
                    EffectRunGraph run = ScriptableObject.CreateInstance<EffectRunGraph>();
                    run.graph = graph;
                    run.trigger_action = ev.action;
                    ab.effects = new EffectData[] { run };
                    ab.conditions_trigger = new ConditionData[0];
                    ab.conditions_target = new ConditionData[0];
                    ab.filters_target = new FilterData[0];
                    ab.status = new StatusData[0];
                    ab.chain_abilities = new AbilityData[0];
                    ab.title = (ev.title ?? ev.action) + "：规则图执行";
                    ab.desc = ab.title;
                    RegisterAbility(ab);
                    result.Add(ab);
                }

                //内置直通动作：按动作逐个编译为效果（真实对局用 TCG2 原生结算）
                foreach (GraphNode act in acts)
                {
                    if (act == null || !string.IsNullOrEmpty(act.category))
                        continue;   //NodeDoc 动作已由上面 EffectRunGraph 统一执行
                    EffectData effect = BuildGraphEffect(act);
                    if (effect == null)
                    {
                        Debug.LogWarning("[规则图] 动作未支持，跳过: " + act.action);
                        continue;
                    }

                    AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
                    ability.id = "graph_" + data.id + "_" + ev.action + "_" + act.action + "_" +
                                 Guid.NewGuid().ToString("N").Substring(0, 6);
                    ability.trigger = trigger;
                    GraphTargetInfo gt = GetGraphTarget(act, is_spell);
                    ability.target = gt.target;
                    ability.value = GraphRuntime.GetFieldInt(act, "value", 1);
                    ability.effects = new EffectData[] { effect };
                    //数组字段必须置空数组而非 null，否则 AbilityData 遍历（条件/状态/链）会 NRE
                    ability.conditions_trigger = new ConditionData[0];
                    ability.conditions_target = BuildOwnerCondition(gt);   //"全部友方/敌方随从"→归属过滤条件
                    ability.filters_target = new FilterData[0];
                    ability.status = new StatusData[0];
                    ability.chain_abilities = new AbilityData[0];
                    ability.title = (ev.title ?? ev.action) + "：" + (act.title ?? act.action);
                    ability.desc = ability.title;
                    RegisterAbility(ability);
                    result.Add(ability);
                }
            }
            return result;
        }

        /// <summary>事件节点 action → AbilityTrigger 映射（未支持返回 None）</summary>
        private static AbilityTrigger MapGraphTrigger(string action)
        {
            switch (action)
            {
                case "OnPlay": return AbilityTrigger.OnPlay;
                case "StartOfTurn": return AbilityTrigger.StartOfTurn;
                case "EndOfTurn": return AbilityTrigger.EndOfTurn;
                case "OnDeath": return AbilityTrigger.OnDeath;
                case "OnAttack": return AbilityTrigger.OnBeforeAttack;
                case "OnDraw": return AbilityTrigger.OnDraw;
                default: return AbilityTrigger.None;
            }
        }

        /// <summary>从事件节点出发，沿输出连线查找可达的动作节点（跳过条件/值节点）</summary>
        private static List<GraphNode> FindReachableActions(GraphData graph, string from_id)
        {
            List<GraphNode> result = new List<GraphNode>();
            if (graph == null)
                return result;

            Stack<GraphNode> stack = new Stack<GraphNode>();
            HashSet<string> visited = new HashSet<string>();
            GraphNode start = graph.GetNode(from_id);
            if (start != null)
                stack.Push(start);

            while (stack.Count > 0)
            {
                GraphNode node = stack.Pop();
                if (node == null || visited.Contains(node.id))
                    continue;
                visited.Add(node.id);

                if (node.type == GraphNodeType.Action)
                {
                    result.Add(node);
                    continue; //动作节点不再继续向下
                }

                //条件/值节点继续展开：沿动作线（Flow 输出）找后续动作；
                //两线制：取值线（数据输出）不驱动执行，跳过。条件真假分支的后续动作都会编译。
                foreach (GraphLink link in graph.GetOutgoing(node.id))
                {
                    GraphPin out_pin = graph.GetPin(node.id, link.from_pin);
                    if (out_pin != null && out_pin.type != NodeValueType.Flow && out_pin.type != NodeValueType.None)
                        continue;
                    GraphNode next = graph.GetNode(link.to_node);
                    if (next != null)
                        stack.Push(next);
                }
            }
            return result;
        }

        /// <summary>动作节点 → 效果组件实例（未支持返回 null）；部分效果需在实例上补配置字段</summary>
        private static EffectData BuildGraphEffect(GraphNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.action))
                return null;

            if (node.action == "Draw")
                return ScriptableObject.CreateInstance<EffectDraw>();
            if (node.action == "Heal")
                return ScriptableObject.CreateInstance<EffectHeal>();
            if (node.action == "Damage")
                return ScriptableObject.CreateInstance<EffectDamage>();
            if (node.action == "Destroy")
                return ScriptableObject.CreateInstance<EffectDestroy>();
            if (node.action == "GainMana")
            {
                EffectMana mana = ScriptableObject.CreateInstance<EffectMana>();
                string mode = GraphRuntime.GetFieldString(node, "mana_mode", "增加上限(空水晶)");
                mana.increase_max = mode != "恢复当前";   //空水晶=加法力上限；恢复当前=加当前法力
                mana.increase_value = mode == "恢复当前";
                return mana;
            }
            if (node.action == "AddAttack")
            {
                EffectAddStat stat = ScriptableObject.CreateInstance<EffectAddStat>();
                stat.type = EffectStatType.Attack;
                return stat;
            }
            if (node.action == "AddHP")
            {
                EffectAddStat stat = ScriptableObject.CreateInstance<EffectAddStat>();
                stat.type = EffectStatType.HP;
                return stat;
            }
            if (node.action == "ReturnHand")
            {
                EffectSendPile pile = ScriptableObject.CreateInstance<EffectSendPile>();
                pile.pile = PileType.Hand;
                return pile;
            }
            if (node.action == "ShuffleDeck")
            {
                EffectSendPile pile = ScriptableObject.CreateInstance<EffectSendPile>();
                pile.pile = PileType.Deck;
                return pile;
            }
            return null;
        }

        /// <summary>编译目标信息：目标枚举 + 是否需要"随从归属"过滤条件（敌/我）</summary>
        private class GraphTargetInfo
        {
            public AbilityTarget target = AbilityTarget.None;
            public bool owner_enemy;   //true=只选敌方随从（ConditionOwner.IsFalse）
            public bool owner_ally;    //true=只选友方随从（ConditionOwner.IsTrue）
        }

        /// <summary>动作节点目标下拉 → AbilityTarget。非法术卡选"出牌选目标"自动降级为自身，避免空目标。</summary>
        private static GraphTargetInfo GetGraphTarget(GraphNode node, bool is_spell)
        {
            GraphTargetInfo info = new GraphTargetInfo();
            if (node == null)
                return info;

            string mode = GraphRuntime.GetFieldString(node, "target", "");
            if (mode == "自身") { info.target = AbilityTarget.Self; }
            else if (mode == "全部敌方随从") { info.target = AbilityTarget.AllCardsBoard; info.owner_enemy = true; }
            else if (mode == "全部友方随从") { info.target = AbilityTarget.AllCardsBoard; info.owner_ally = true; }
            else if (mode == "全体随从") { info.target = AbilityTarget.AllCardsBoard; }
            else if (mode == "敌方英雄") { info.target = AbilityTarget.PlayerOpponent; }
            else if (mode == "己方英雄") { info.target = AbilityTarget.PlayerSelf; }
            else if (mode == "出牌选目标") { info.target = is_spell ? AbilityTarget.PlayTarget : AbilityTarget.None; }
            else
            {
                //旧图没有 target 字段 → 按动作语义取默认
                switch (node.action)
                {
                    case "Draw":
                    case "Heal":
                    case "GainMana":
                        info.target = AbilityTarget.PlayerSelf;
                        break;
                    case "Damage":
                    case "Destroy":
                    case "ReturnHand":
                    case "ShuffleDeck":
                    case "AddAttack":
                    case "AddHP":
                    default:
                        //伤害/消灭等需"目标"的动作：非法术卡上不隐式打自己（空转），由玩家显式选择
                        info.target = is_spell ? AbilityTarget.PlayTarget : AbilityTarget.None;
                        break;
                }
            }
            return info;
        }

        /// <summary>为"全部友方/敌方随从"目标附加归属条件（ConditionOwner：IsTrue 同阵营 / IsFalse 敌方）</summary>
        private static ConditionData[] BuildOwnerCondition(GraphTargetInfo info)
        {
            if (!info.owner_enemy && !info.owner_ally)
                return new ConditionData[0];
            ConditionOwner cond = ScriptableObject.CreateInstance<ConditionOwner>();
            cond.oper = info.owner_enemy ? ConditionOperatorBool.IsFalse : ConditionOperatorBool.IsTrue;
            return new ConditionData[] { cond };
        }

        /// <summary>AbilityCustomData → AbilityData（运行时实例）</summary>
        public static AbilityData BuildAbilityData(AbilityCustomData data)
        {
            if (data == null)
                return null;

            AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.id = string.IsNullOrEmpty(data.id) ? "custom_ability_" + Guid.NewGuid().ToString("N").Substring(0, 8) : data.id;
            ability.trigger = ParseEnum(data.trigger, AbilityTrigger.None);
            ability.target = ParseEnum(data.target, AbilityTarget.None);
            ability.value = data.value;
            ability.duration = data.duration;
            ability.mana_cost = data.mana_cost;
            ability.exhaust = data.exhaust;
            ability.title = data.title;
            ability.desc = data.desc;

            List<StatusData> status = new List<StatusData>();
            foreach (string sid in data.status_ids)
            {
                StatusData sdata = StatusData.Get(ParseEnum(sid, StatusType.None));
                if (sdata != null)
                    status.Add(sdata);
            }
            ability.status = status.ToArray();

            List<AbilityData> chains = new List<AbilityData>();
            foreach (string cid in data.chain_ability_ids)
            {
                AbilityData chain = AbilityData.Get(cid);
                if (chain != null)
                    chains.Add(chain);
            }
            ability.chain_abilities = chains.ToArray();

            ability.effects = DeserializeComponents<EffectData>(data.effects);
            ability.conditions_trigger = DeserializeComponents<ConditionData>(data.conditions_trigger);
            ability.conditions_target = DeserializeComponents<ConditionData>(data.conditions_target);
            ability.filters_target = DeserializeComponents<FilterData>(data.filters_target);
            return ability;
        }

        // ---------------- 组件（效果/条件/过滤器）序列化 ----------------

        /// <summary>把 ScriptableObject 数组序列化为 ComponentCustomData 列表</summary>
        public static List<ComponentCustomData> SerializeComponents<T>(T[] array) where T : ScriptableObject
        {
            List<ComponentCustomData> list = new List<ComponentCustomData>();
            if (array == null)
                return list;

            foreach (T comp in array)
            {
                if (comp == null)
                    continue;

                ComponentCustomData cd = new ComponentCustomData();
                cd.type = comp.GetType().Name;
                ReflectionUtil.SerializeFields(comp, cd.fields);
                list.Add(cd);
            }
            return list;
        }

        /// <summary>把 ComponentCustomData 列表还原为 ScriptableObject 数组</summary>
        public static T[] DeserializeComponents<T>(List<ComponentCustomData> list) where T : ScriptableObject
        {
            List<T> result = new List<T>();
            if (list == null)
                return result.ToArray();

            foreach (ComponentCustomData cd in list)
            {
                Type type = GetComponentType(cd.type);
                if (type == null || !typeof(T).IsAssignableFrom(type))
                    continue;

                ScriptableObject comp = ScriptableObject.CreateInstance(type);
                if (comp == null)
                    continue;

                ReflectionUtil.DeserializeFields(comp, cd.fields);
                result.Add((T)comp);
            }
            return result.ToArray();
        }

        /// <summary>按类名查找类型（支持全名与 TcgEngine 命名空间内短名）</summary>
        public static Type GetComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName);
                if (type != null)
                    return type;
                type = assembly.GetType("TcgEngine." + typeName);
                if (type != null)
                    return type;
            }
            return null;
        }

        // ---------------- 注册到静态字典 ----------------

        /// <summary>注册生成的 CardData 到静态字典（id 冲突时跳过）</summary>
        public static bool RegisterCard(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
                return false;

            if (CardData.Get(card.id) != null)
            {
                Debug.LogWarning("卡牌 id 已存在，跳过: " + card.id);
                return false;
            }

            CardData.card_list.Add(card);
            CardData.card_dict.Add(card.id, card);
            custom_ids.Add(card.id);
            return true;
        }

        /// <summary>注册生成的 AbilityData 到静态字典（id 冲突时跳过）</summary>
        public static bool RegisterAbility(AbilityData ability)
        {
            if (ability == null || string.IsNullOrEmpty(ability.id))
                return false;

            if (AbilityData.Get(ability.id) != null)
                return false;

            AbilityData.ability_list.Add(ability);
            AbilityData.ability_dict.Add(ability.id, ability);
            return true;
        }

        // ---------------- 工具 ----------------

        //取第一个阵营作为默认（避免导入卡 team 为 null 导致 UI 报错）
        private static TeamData GetFirstTeam()
        {
            List<TeamData> teams = TeamData.GetAll();
            if (teams != null && teams.Count > 0)
                return teams[0];
            return null;
        }

        private static T ParseEnum<T>(string value, T defaultValue) where T : struct
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            if (Enum.TryParse(value, true, out T result))
                return result;
            return defaultValue;
        }
    }

#if UNITY_EDITOR
    /// <summary>编辑器测试入口：一键导出/导入卡池 JSON</summary>
    public static class CardPoolEditor
    {
        [UnityEditor.MenuItem("TcgEngine/导出全部卡牌为JSON")]
        public static void ExportAllCards()
        {
            List<CardData> cards = CardData.GetAll();
            CardPoolIO.ExportToFile(cards, "export_all", UnityEditor.EditorUserBuildSettings.development ? "editor" : "player");
            UnityEditor.EditorUtility.DisplayDialog("卡池导出", "已导出 " + cards.Count + " 张卡到:\n" + CardPoolIO.SaveFolder, "确定");
        }

        [UnityEditor.MenuItem("TcgEngine/导入自定义卡池(JSON)")]
        public static void ImportPool()
        {
            string path = UnityEditor.EditorUtility.OpenFilePanel("选择卡池 JSON", CardPoolIO.SaveFolder, "json");
            if (string.IsNullOrEmpty(path))
                return;
            CardPoolIO.ImportFromFile(path);
            UnityEditor.EditorUtility.DisplayDialog("卡池导入", "当前卡牌总数: " + CardData.GetAll().Count, "确定");
        }
    }
#endif
}
