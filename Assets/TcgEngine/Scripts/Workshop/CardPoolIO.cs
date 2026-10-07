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

        /// <summary>
        /// 从 ArtFolder 加载卡牌图片（不存在返回 null）。
        ///
        /// 带缓存：key = 文件路径 + 最后写入时间。RefreshArt()/UpdateCardData() 会反复调用本方法，
        /// 之前每次都新建一张**全尺寸 RGBA32 纹理**且从不释放——刷新几次就能吃掉几百 MB 显存，
        /// 纹理创建/上传失败时 SpriteRenderer 会渲染成**品红**（表现为「战场上卡图全紫」）。
        /// 重新裁切/换图后文件写入时间变化，key 不同即自动重载，旧的回收掉。
        /// </summary>
        public static Sprite LoadArt(string art_path)
        {
            if (string.IsNullOrEmpty(art_path))
                return null;
            try
            {
                string path = Path.Combine(ArtFolder, art_path);
                if (!File.Exists(path))
                    return null;

                string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
                if (art_cache.TryGetValue(key, out Sprite cached) && cached != null)
                    return cached;

                //同一路径的旧版本作废并回收
                if (art_cache_path.TryGetValue(path, out string old_key) && old_key != key)
                {
                    if (art_cache.TryGetValue(old_key, out Sprite old_sp))
                        DestroyArtSprite(old_sp);
                    art_cache.Remove(old_key);
                }
                art_cache_path[path] = key;

                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    DestroyUnityObject(tex);   //解码失败也要回收，否则同样是泄漏
                    return null;
                }
                tex.wrapMode = TextureWrapMode.Clamp;   //边缘 clamp：避免采样到对侧像素
                tex.filterMode = FilterMode.Bilinear;

                //FullRect：整图网格。默认的 Tight 会按 alpha 收网格，透明边多的卡面图容易收出怪形状
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                art_cache[key] = sprite;
                return sprite;
            }
            catch (System.Exception e)
            {
                Debug.LogError("加载卡牌图片失败: " + art_path + " " + e.Message);
                return null;
            }
        }

        //卡图缓存：路径 → (key → Sprite)
        private static readonly Dictionary<string, Sprite> art_cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, string> art_cache_path = new Dictionary<string, string>();

        private static void DestroyArtSprite(Sprite sp)
        {
            if (sp == null)
                return;
            Texture2D tex = sp.texture;
            DestroyUnityObject(sp);
            DestroyUnityObject(tex);
        }

        /// <summary>运行时用 Destroy、编辑器下用 DestroyImmediate（避免编辑模式报错）</summary>
        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }

        //记录运行时导入/创建的自定义卡牌 id，用于"仅导出自定义卡"
        /// <summary>
        /// 每次 Play 复位卡池的运行时状态（理由同 CardData.ResetStatics：
        /// Editor 关闭域重载时静态字段跨 Play 存活，会让卡池注册表反复累积）。
        /// 复位后由 DataLoader 重新导入本地卡池，内容不变。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            custom_ids.Clear();
            pool_file_cards.Clear();
            custom_data.Clear();
        }

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
            //★ 基础卡池不可删除（**硬门禁**：不依赖界面上按钮是否隐藏 —— 隐藏按钮 ≠ 不允许）
            if (IsBasePoolFile(Path.GetFileName(path)))
            {
                Debug.LogError("[卡池] 拒绝删除基础卡池：" + Path.GetFileName(path)
                    + "（它承载内置卡迁移数据，误删等于丢掉整份迁移产物）");
                return false;
            }
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

            /// <summary>基础卡池：承载内置卡迁移数据的池（既不可删除也不可导出）</summary>
            public bool IsBasePool { get { return CardPoolIO.IsBasePoolFile(file); } }
        }

        /// <summary>基础卡池的文件名前缀（v1/v2… 都算，避免写死版本号）</summary>
        public const string BasePoolPrefix = "base_pool";

        /// <summary>「允许覆盖内置卡的池清单」文件名（项目既有的迁移验证开关，内容每行一个池文件名）</summary>
        public const string OverrideFlagFile = "override_builtin.txt";

        /// <summary>
        /// 是否为基础卡池 —— 判定按**文件名**（显示名会被改名，文件名才是稳定标识）：
        ///   ① 文件名以 "base_pool" 开头（v1/v2… 都覆盖）；
        ///   ② 或该文件登记在 override_builtin.txt 清单里（项目自己的"基础池"登记处）。
        /// 为什么要有这个判定：基础卡池**不可删除**（误删=丢掉整份迁移数据）、**不可导出**
        /// （它是"替代内置卡"的池，不是给普通 mod 分发的产物）。
        /// </summary>
        public static bool IsBasePoolFile(string file_name)
        {
            if (string.IsNullOrEmpty(file_name))
                return false;

            string stem = Path.GetFileNameWithoutExtension(file_name);
            if (!string.IsNullOrEmpty(stem) && stem.StartsWith(BasePoolPrefix, StringComparison.OrdinalIgnoreCase))
                return true;

            string flag = Path.Combine(SaveFolder, OverrideFlagFile);
            if (!File.Exists(flag))
                return false;
            try
            {
                string txt = File.ReadAllText(flag).Trim();
                if (string.IsNullOrEmpty(txt))
                    return false;   //清单为空 = 允许所有池覆盖，此时不据此判定（否则会把所有池都当基础池）
                foreach (string line in txt.Split('\n'))
                {
                    if (line.Trim().Equals(file_name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[卡池] 读取 " + OverrideFlagFile + " 失败：" + e.Message);
            }
            return false;
        }

        public static bool IsBasePool(PoolInfo info)
        {
            return info != null && IsBasePoolFile(info.file);
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

        /// <summary>卡牌所属的**卡池标识**（供 103025「获取卡牌定义所属卡池」用）：
        /// 返回池文件的显示名（去目录与扩展名）；不在任何池里返回空串。
        /// 为什么不能只读 CardData.packs：卡包(PackData)是内置资产，池卡导入时 packs 被置成空数组，
        /// 直接读 packs 会让该节点对**所有池卡**恒返回空（实测全量用例 103025 ❌）。</summary>
        public static string PoolIdOf(string card_id)
        {
            if (string.IsNullOrEmpty(card_id))
                return "";
            foreach (KeyValuePair<string, List<string>> kv in pool_file_cards)
            {
                if (kv.Value == null || !kv.Value.Contains(card_id))
                    continue;
                string key = kv.Key ?? "";
                if (key.StartsWith("file:"))
                    key = key.Substring(5);
                string name = Path.GetFileNameWithoutExtension(key);
                return string.IsNullOrEmpty(name) ? key : name;
            }
            return "";
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
            data.traits = new List<string>();   //种族多选：全部写入列表，trait 保留首项兼容旧工具
            if (card.traits != null)
            {
                foreach (TraitData t in card.traits)
                {
                    if (t != null && !string.IsNullOrEmpty(t.id))
                        data.traits.Add(t.id);
                }
            }
            data.trait = data.traits.Count > 0 ? data.traits[0] : "";
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
                    if (ability == null)
                        continue;
                    //★图派生能力**不写进能力级 abilities**：它们由图（卡级 data.effects）代表，
                    //  两边都写 → 导入时同一效果被编译两次（实测：技能卡伤害翻倍、能力数 1→2）。
                    //  判断依据：能力的效果里含 EffectRunGraph（图能力的唯一标识）。
                    bool graph_derived = false;
                    if (ability.effects != null)
                    {
                        foreach (EffectData ef in ability.effects)
                        {
                            if (ef is EffectRunGraph)
                            {
                                graph_derived = true;
                                break;
                            }
                        }
                    }
                    if (graph_derived)
                        continue;
                    data.abilities.Add(AbilityToData(ability));
                }
            }
            data.keywords.Clear();
            if (card.keywords != null)
            {
                foreach (KeywordData keyword in card.keywords)
                {
                    if (keyword != null && !data.keywords.Contains(keyword.id))
                        data.keywords.Add(keyword.id);
                }
            }
            data.skills = card.skills != null ? new List<string>(card.skills) : new List<string>();   //英雄技能卡引用（导出保留）

            //★把「图能力」的图回写到卡级效果列表（data.effects）——**这是导出方向的关键缺口**：
            //  导入侧 CompileOneGraphAbilities 只认 CardEffectData.graph（能力级的 AbilityCustomData.effects
            //  是另一套旧表示，图能力不走它）。以前这里一个字都不写 ⇒
            //  **导出卡池 → 再导入 = 所有图效果消失**：EffectRunGraph.graph 变成 null，
            //  图里动作一个都不跑，而且**没有任何日志**（实测：SkillTypeProbe 打出技能卡零伤害，
            //  诊断行「导入实例 graph=★null」；探针 ② 的根因就是它）。
            //  去重按"图实例"判定：被动入口的 生效/失效 两条线共用同一张图，只需回写一条（导入时会重新派生两条线）。
            if (card.abilities != null)
            {
                foreach (AbilityData ability in card.abilities)
                {
                    if (ability == null || ability.effects == null)
                        continue;
                    foreach (EffectData ef in ability.effects)
                    {
                        EffectRunGraph rg = ef as EffectRunGraph;
                        if (rg == null || rg.graph == null)
                            continue;
                        bool dup = false;
                        for (int i = 0; i < data.effects.Count; i++)
                        {
                            CardEffectData e2 = data.effects[i];
                            if (e2 != null && object.ReferenceEquals(e2.graph, rg.graph))
                            {
                                dup = true;
                                break;
                            }
                        }
                        if (dup)
                            continue;
                        data.effects.Add(new CardEffectData
                        {
                            name = string.IsNullOrEmpty(ability.title) ? ability.id : ability.title,
                            graph = rg.graph,
                        });
                    }
                }
            }
            return data;
        }

        /// <summary>种族写回 CardData：优先 traits 列表（多选 + 自定义），为空时回退旧单字段 trait；
        /// 未注册的自定义种族 id 直接跳过（TCG2 的 TraitData 为资产池，自定义种族需先建资产）。</summary>
        private static void ApplyTraits(CardData card, CardCustomData data)
        {
            if (card == null)
                return;
            TraitData.Load();
            List<TraitData> list = new List<TraitData>();
            if (data != null && data.traits != null)
            {
                foreach (string id in data.traits)
                {
                    if (string.IsNullOrEmpty(id))
                        continue;
                    TraitData t = TraitData.Get(id);
                    if (t != null && !list.Contains(t))
                        list.Add(t);
                }
            }
            if (list.Count == 0 && data != null && !string.IsNullOrEmpty(data.trait))
            {
                TraitData t = TraitData.Get(data.trait);   //旧数据：单种族字段
                if (t != null)
                    list.Add(t);
            }
            card.traits = list.ToArray();
        }

        /// <summary>关键词 id 列表 → KeywordData 数组（未注册/缺失的 id 跳过并警告）</summary>
        public static KeywordData[] ResolveKeywords(List<string> keyword_ids)
        {
            if (keyword_ids == null || keyword_ids.Count == 0)
                return new KeywordData[0];
            KeywordData.Load();
            List<KeywordData> result = new List<KeywordData>();
            foreach (string id in keyword_ids)
            {
                if (string.IsNullOrEmpty(id))
                    continue;
                KeywordData keyword = KeywordData.Get(id);
                if (keyword != null)
                    result.Add(keyword);
                else
                    Debug.LogWarning("[CardPoolIO] 关键词未找到，已跳过: " + id);
            }
            return result.ToArray();
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

            //★图能力不写进"能力级 effects"：图能力的图由**卡级** data.effects 承载（见 CardToData 末尾的回写）。
            //  两边都写 → 导入时"卡级图 + 能力级图"各编译一遍 → **同一个效果跑两次**
            //  （实测：技能卡 2 点伤害变成 6 点、能力数 1 → 2）。
            List<EffectData> legacy_effects = new List<EffectData>();
            if (ability.effects != null)
            {
                foreach (EffectData ef in ability.effects)
                {
                    if (ef != null && !(ef is EffectRunGraph))
                        legacy_effects.Add(ef);
                }
            }
            data.effects = SerializeComponents(legacy_effects.ToArray());
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
                    //★授予拥有数量：否则工坊里做好的卡在构筑界面是"未拥有"——灰显且点不动
                    //（用户实报"我做好的卡牌怎么无法加入构筑？"）。GrantOwnership 已改为幂等补齐，
                    //每次启动都调也不会累加，所以这里给 true。
                    ImportFromFile(file, true);
                }
                catch (Exception e)
                {
                    Debug.LogError("加载自定义卡池失败: " + file + "\n" + e);   //带堆栈，便于定位抛异常的 Add
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

            //★ 只处理「卡池」JSON：Workshop 目录下还并存 buffs.json / buttons.json / bgm_library.json 等其它数据文件。
            //  它们没有 "cards" 字段，JsonUtility 会得到「name 默认值 MyCardPool + 空卡列表」→ 旧实现于是每个都报
            //  「已导入卡池「MyCardPool」，新增 0 张卡」（实测 3 条误报）。这里用一次廉价字符串检查直接跳过。
            if (json.IndexOf("\"cards\"", StringComparison.Ordinal) < 0)
            {
                if (grantOwnership)
                    Debug.LogWarning("[卡池] 这不是卡池文件（缺少 cards 字段），已跳过: " + path);
                return;
            }

            CardPoolData pool = JsonUtility.FromJson<CardPoolData>(json);
            if (pool == null)
            {
                Debug.LogError("卡池 JSON 解析失败: " + path);
                return;
            }

            int count = ImportToGame(pool, path, grantOwnership);
            Debug.Log("已导入卡池「" + pool.name + "」，新增 " + count + " 张卡: " + path);
        }

        // ---------------- 自定义参数登记表（供高级筛选 p:参数名 使用） ----------------

        /// <summary>card_id → 卡池 JSON 原始数据（含自定义参数声明 custom_prop_defs）。运行时 CardData 是
        /// ScriptableObject，装不下这些声明，所以这里保留一份登记表。</summary>
        private static readonly Dictionary<string, CardCustomData> custom_data = new Dictionary<string, CardCustomData>();

        /// <summary>取某张自定义卡牌的原始数据（内置卡牌/未导入的卡返回 null）</summary>
        public static CardCustomData GetCustomData(string card_id)
        {
            if (string.IsNullOrEmpty(card_id))
                return null;
            CardCustomData data;
            return custom_data.TryGetValue(card_id, out data) ? data : null;
        }

        /// <summary>所有自定义参数名（去重，用于高级筛选的字段速查）</summary>
        public static List<string> GetAllCustomPropNames()
        {
            List<string> names = new List<string>();
            foreach (KeyValuePair<string, CardCustomData> kv in custom_data)
            {
                CardCustomData d = kv.Value;
                if (d == null)
                    continue;
                List<BuffCustomProp> defs = d.EnsureCustomPropDefs();
                for (int i = 0; i < defs.Count; i++)
                {
                    BuffCustomProp cp = defs[i];
                    if (cp == null || string.IsNullOrEmpty(cp.name))
                        continue;
                    if (!names.Contains(cp.name))
                        names.Add(cp.name);
                }
            }
            names.Sort();
            return names;
        }

        /// <summary>将 CardPoolData 注册到游戏（返回实际新增卡牌数）</summary>
        /// <param name="fileKey">卡池文件路径（用于删除时按文件卸载），为空则不做归属记录</param>
        public static int ImportToGame(CardPoolData pool, string fileKey = "", bool grantOwnership = false)
        {
            if (pool == null || pool.cards == null)
                return 0;

            int added = 0;
            int overrode = 0;
            int art_ok = 0;
            bool allow_override = OverrideBuiltinFor(Path.GetFileName(fileKey));   //★迁移验证开关（按池文件名限定）
            foreach (CardCustomData cdata in pool.cards)
            {
                CardData card = BuildCardData(cdata);
                bool did_override;
                if (card != null && RegisterCard(card, allow_override, out did_override))
                {
                    if (did_override)
                        overrode++;
                    if (card.art_full != null)
                        art_ok++;          //证据：覆盖后仍有**手牌/收藏用图**的张数（=继承内置美术成功）

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
            if (overrode > 0)
                Debug.Log("[卡池] 已用池卡**覆盖同名内置卡** " + overrode + " 张（其中带卡图 " + art_ok + " 张；"
                    + "验证开关 override_builtin.txt 生效中，删除该文件并重开游戏即恢复内置卡优先）");
            return added;
        }

        /// <summary>自定义卡默认授予的拥有张数（=可正常构筑）</summary>
        public const int CustomOwnCount = 2;

        /// <summary>授予玩家拥有该自定义卡（默认变体 2 张），使其可正常构筑。
        ///
        /// ★**幂等**（"补齐到 2 张"，不是每次 +2）：启动自动加载每次都会走到这里，
        /// 用累加会让拥有数随每次启动无限增长；补齐则稳定，且玩家自己买/开包得到的更多张数不会被削减。
        ///
        /// ★为什么要修：以前只有"玩家主动导入卡池"才授予拥有，启动自动加载（LoadCustomPools）不授予，
        /// 于是工坊里做好的卡在构筑界面是"未拥有"状态 —— 表现就是**卡片灰显、点它没有任何反应**
        /// （CollectionPanel.OnClickCard 里 `owner && deck_limit` 不成立就静默 return），
        /// 用户实报："我做好的卡牌怎么无法加入构筑？"</summary>
        private static void GrantOwnership(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
                return;
            VariantData variant = VariantData.GetDefault();
            Authenticator auth = Authenticator.Get();
            if (auth == null || variant == null)
                return;
            UserData udata = auth.UserData;
            if (udata == null)
                return;      //玩家数据还没跑完（异步）：交给 EnsureCustomCardOwned 在构筑界面按需补
            int have = udata.GetCardQuantity(card.id, variant.id, variant.is_default);
            if (have >= CustomOwnCount)
                return;
            udata.AddCard(card.id, variant.id, CustomOwnCount - have);
            Debug.Log("[卡池] 授予拥有 " + card.id + " ×" + CustomOwnCount
                + "（补齐 " + have + "→" + CustomOwnCount + "，可在构筑界面使用）");
        }

        /// <summary>确保玩家拥有该自定义卡（补齐到 <see cref="CustomOwnCount"/> 张）。
        /// 返回是否为**自定义卡池**的卡（内置卡返回 false）。幂等，可反复调。
        /// 构筑界面刷新/加卡前调用：卡池自动加载发生在启动早期，那时玩家数据可能尚未读完，
        /// 单靠加载路径授予会漏，这里兜住（"作者自己的卡"永远可构筑）。</summary>
        public static bool EnsureCustomCardOwned(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
                return false;
            if (GetCustomData(card.id) == null)
                return false;                    //内置卡：不参与（保持原有收集/经济语义）
            GrantOwnership(card);
            return true;
        }

        /// <summary>CardCustomData → CardData（运行时实例）</summary>
        public static CardData BuildCardData(CardCustomData data)
        {
            if (data == null || string.IsNullOrEmpty(data.id))
                return null;

            custom_data[data.id] = data;    //登记原始数据（自定义参数筛选/字段速查要用）
            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.id = data.id;
            card.title = data.title ?? "";
            card.type = ParseEnum(data.type, CardType.None);
            card.team = string.IsNullOrEmpty(data.team) ? GetFirstTeam() : TeamData.Get(data.team);
            card.rarity = string.IsNullOrEmpty(data.rarity) ? RarityData.GetFirst() : RarityData.Get(data.rarity);
            ApplyTraits(card, data);   //种族支持多选（traits 列表），并兼容旧单字段 trait
            card.keywords = ResolveKeywords(data.keywords);
            //★英雄技能卡引用（type=Hero 时）：id 列表原样带入运行期定义（开战时挂载到英雄卡实例）
            card.skills = data.skills != null ? new List<string>(data.skills) : new List<string>();
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
            //★判定类入口（攻击限制 / 被攻击限制）的图：不生成能力，单独挂到 CardData.attack_graph，
            //  由 Game.CanAttackTargetByRules 直接提问（UI 高亮 / AI / 结算共用那一处判定）。
            card.attack_graph = FindAttackGraph(data);
            RebuildGlobalAttackGraphs();   //★"作用范围=全部卡牌"的规则：配一次，对所有卡（含 AI）生效
            //数组字段置空数组而非 null，避免 Card.SetCard/SetTraits 等遍历时报空引用
            card.stats = new TraitStat[0];
            card.packs = new PackData[0];
            //异步补载 4 个音频槽（spawn/attack/death/damage）写回 CardData，使自定义音效真实可播
            CardAudioLoader.LoadCardAudio(data, card);
            InheritBuiltinVisuals(card);
            return card;
        }

        /// <summary>找卡牌里带【攻击限制 / 被攻击限制】口连线的图（找不到 = null = 不做任何限制，零开销）。
        /// 判据 = "攻击时 / 被攻击时入口的 limit 口有没有接线"：纯效果入口（限制口没接）不会让这张卡进入攻击判定。
        /// 两个限制口放**同一张图**最省事（各入口自己带自己的限制口）。</summary>
        private static GraphData FindAttackGraph(CardCustomData data)
        {
            if (data == null)
                return null;
            foreach (CardEffectData eff in data.EnsureEffects())
            {
                GraphData g = eff != null ? eff.graph : null;
                if (g == null || g.nodes == null)
                    continue;
                foreach (GraphNode n in g.nodes)
                {
                    if (n == null || n.type != GraphNodeType.Event)
                        continue;
                    if (n.action != "OnAttack" && n.action != "OnBeforeDefend")
                        continue;
                    GraphPin pin = g.GetPinByName(n.id, "limit");
                    if (pin != null && g.GetIncomingLink(n.id, pin.id) != null)
                        return g;   //★有"限制"口连线 = 这张卡有攻击规则
                }
            }
            return null;
        }

        /// <summary>★全局攻击规则（入口「作用范围 = 全部卡牌」）：
        /// 不针对某一张卡，而是**任何一次攻击 / 任何一次被攻击**都要满足它 —— 包括 AI 的卡。
        /// 用途：如"英雄不能被任何卡攻击"、"所有卡都不能无视嘲讽" 这类整局规则（配一次即可，不用每张卡配）。
        /// 由卡池导入时重建（见 RebuildGlobalAttackGraphs）。</summary>
        public static readonly List<GraphData> GlobalAttackGraphs = new List<GraphData>();   //攻击者侧（"OnAttack" 入口）
        public static readonly List<GraphData> GlobalDefendGraphs = new List<GraphData>();   //被攻击侧（"OnBeforeDefend" 入口）

        /// <summary>一条"全局事件入口"：图 + 入口 action（Run 时用它只跑这条入口）</summary>
        public class GlobalEntry
        {
            public GraphData graph;
            public string action;
        }

        /// <summary>★全局入口（作用范围=「全部卡牌」的事件类入口），按**触发器**归组：
        /// 语义（用户口径）：除 主动效果/起动式/被动 外，入口**不限制发动主体**——任何卡满足触发条件都算，
        /// 效果对所有满足条件的目标生效（想只对某张卡生效，在条件里判"主体==本卡"）。
        /// 旧数据没有 scope 字段 → 不算全局（保持"卡自身"的旧行为，否则内置卡迁移图会互相触发）。</summary>
        public static readonly Dictionary<AbilityTrigger, List<GlobalEntry>> GlobalEntriesByTrigger =
            new Dictionary<AbilityTrigger, List<GlobalEntry>>();

        /// <summary>取某触发器的全局入口（无则 null）</summary>
        public static List<GlobalEntry> GetGlobalEntries(AbilityTrigger trigger)
        {
            List<GlobalEntry> list;
            return GlobalEntriesByTrigger.TryGetValue(trigger, out list) ? list : null;
        }

        /// <summary>入口 action → 触发器（给引擎侧按事件查全局入口用）</summary>
        public static AbilityTrigger MapTrigger(string action)
        {
            if (string.IsNullOrEmpty(action))
                return AbilityTrigger.None;
            if (action == "EventEffect")
                return AbilityTrigger.None;   //按 event_name 映射，见 ResolveEventTrigger
            return MapGraphTrigger(action);
        }

        /// <summary>入口是否"作用范围=全部卡牌"。**缺字段=仅本卡**（旧数据/内置迁移图），
        /// 只有编辑器里显式选了"全部卡牌"（新拖入的默认值）才算全局。</summary>
        public static bool IsGlobalScopeEntry(GraphNode n)
        {
            return n != null && GraphRuntime.GetFieldString(n, "scope", "仅本卡") == "全部卡牌";
        }

        /// <summary>登记一条"全局事件入口"（按触发器归组；同图同入口去重）</summary>
        private static void RegisterGlobalEntryGraph(GraphNode n, GraphData g)
        {
            if (n == null || g == null || string.IsNullOrEmpty(n.action))
                return;
            AbilityTrigger trigger = ResolveEventTrigger(n);
            if (trigger == AbilityTrigger.None)
            {
                //★不要静默：显式选了"全部卡牌"却映射不到触发时机（监听事件/标签写错）→ 用户会以为规则生效了
                Debug.LogWarning("[全局入口] 触发时机未识别，已跳过：" + n.action
                    + (n.action == "EventEffect" ? ("（监听事件=" + GraphRuntime.GetFieldString(n, "event_name", "") + "）") : "")
                    + "。请检查入口的「监听事件」/标签字段。");
                return;
            }
            List<GlobalEntry> list;
            if (!GlobalEntriesByTrigger.TryGetValue(trigger, out list))
            {
                list = new List<GlobalEntry>();
                GlobalEntriesByTrigger[trigger] = list;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].graph == g && list[i].action == n.action)
                    return;
            }
            list.Add(new GlobalEntry { graph = g, action = n.action });
        }

        /// <summary>重建全局攻击规则列表（扫全部已导入卡池数据）。无全局规则时两个列表都为空 → 判定零开销。</summary>
        public static void RebuildGlobalAttackGraphs()
        {
            int prev_a = GlobalAttackGraphs.Count;
            int prev_d = GlobalDefendGraphs.Count;
            GlobalAttackGraphs.Clear();
            GlobalDefendGraphs.Clear();
            GlobalEntriesByTrigger.Clear();
            foreach (KeyValuePair<string, CardCustomData> pair in custom_data)
            {
                CardCustomData data = pair.Value;
                if (data == null)
                    continue;
                foreach (CardEffectData eff in data.EnsureEffects())
                {
                    GraphData g = eff != null ? eff.graph : null;
                    if (g == null || g.nodes == null)
                        continue;
                    foreach (GraphNode n in g.nodes)
                    {
                        if (n == null || n.type != GraphNodeType.Event)
                            continue;

                        //★作用范围=全部卡牌 的事件入口 → 登记为"全局入口图"（任何卡满足条件都算；
                        //  主动/起动式/被动/光环 这些是卡自身语义，不参与全局登记）。
                        if (IsGlobalScopeEntry(n)
                            && n.action != "ActivateEffect" && n.action != "ActivateAbility"
                            && n.action != "PassiveEffect" && n.action != "AuraEffect")
                            RegisterGlobalEntryGraph(n, g);

                        if (n.action != "OnAttack" && n.action != "OnBeforeDefend")
                            continue;
                        //限制口：**缺作用范围=整局**（历史行为/用户口径），但显式写了「仅本卡」→ 只对这张卡生效
                        //（卡自身那条路径由 CardData.attack_graph 负责）
                        if (GraphRuntime.GetFieldString(n, "scope", "全部卡牌") == "仅本卡")
                            continue;
                        GraphPin pin = g.GetPinByName(n.id, "limit");
                        if (pin == null || g.GetIncomingLink(n.id, pin.id) == null)
                            continue;                       //限制口没接线 = 不限制
                        List<GraphData> list = n.action == "OnAttack" ? GlobalAttackGraphs : GlobalDefendGraphs;
                        if (!list.Contains(g))
                            list.Add(g);
                    }
                }
            }
            if (GlobalAttackGraphs.Count != prev_a || GlobalDefendGraphs.Count != prev_d)
                Debug.Log("[攻击规则] 全局规则（作用范围=全部卡牌）已载入：攻击者侧 " + GlobalAttackGraphs.Count
                    + " 张图，被攻击侧 " + GlobalDefendGraphs.Count + " 张图");
        }

        /// <summary>★池卡覆盖同名**内置资产**时，继承它的美术/特效/音效引用。
        /// 为什么需要：池格式（CardCustomData）只带**文件路径**（art_path/art_full_path/*_audio_id），
        /// 而内置卡的美术是**资产引用**（art_board/art_full/spawn_fx…）→ 转换器导出的池里这些字段是空的，
        /// 覆盖后进对局就是"卡片全黑、战场没图"（实测用户截图就是这个）。
        /// 规则：只在池**没有**提供时回退到被覆盖的内置资产；池自带资源（自制卡/Phase 5 资源随池）优先。
        /// 找不到同名内置卡（纯自制卡）则什么都不做。</summary>
        private static void InheritBuiltinVisuals(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
                return;
            CardData builtin = CardData.Get(card.id);   //调用点在 RegisterCard 之前 → 拿到的是被覆盖的内置资产
            if (builtin == null || builtin == card)
                return;
            if (card.art_board == null) card.art_board = builtin.art_board;
            if (card.art_full == null) card.art_full = builtin.art_full;
            if (card.spawn_fx == null) card.spawn_fx = builtin.spawn_fx;
            if (card.death_fx == null) card.death_fx = builtin.death_fx;
            if (card.attack_fx == null) card.attack_fx = builtin.attack_fx;
            if (card.damage_fx == null) card.damage_fx = builtin.damage_fx;
            if (card.idle_fx == null) card.idle_fx = builtin.idle_fx;
            if (card.spawn_audio == null) card.spawn_audio = builtin.spawn_audio;
            if (card.death_audio == null) card.death_audio = builtin.death_audio;
            if (card.attack_audio == null) card.attack_audio = builtin.attack_audio;
            if (card.damage_audio == null) card.damage_audio = builtin.damage_audio;
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
            ApplyTraits(card, data);   //种族支持多选（traits 列表），并兼容旧单字段 trait
            card.keywords = ResolveKeywords(data.keywords);
            card.skills = data.skills != null ? new List<string>(data.skills) : new List<string>();   //英雄技能卡引用
            card.mana = data.mana;
            card.attack = data.attack;
            card.hp = data.hp;
            card.text = data.text ?? "";
            card.desc = data.desc ?? "";
            card.deckbuilding = data.deckbuilding;
            card.cost = data.cost;
            //★只在池提供了图片时才覆盖：否则会把内置资产自己带的图清成 null（「模拟测试」/覆盖后的卡整场黑图）
            Sprite up_board = LoadArt(data.art_path);
            if (up_board != null) card.art_board = up_board;
            Sprite up_full = LoadArt(data.art_full_path);
            if (up_full != null) card.art_full = up_full;

            //判定类入口（攻击限制/被攻击限制）的图也随池更新（否则改完规则要重启才生效）
            card.attack_graph = FindAttackGraph(data);
            RebuildGlobalAttackGraphs();   //全局规则（作用范围=全部卡牌）同样随池更新

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
            if (data == null)
                return result;

            //多效果图：一张卡可有多张效果图（战吼/亡语/光环/事件…），逐张编译为独立能力
            foreach (CardEffectData eff in data.EnsureEffects())
            {
                if (eff == null || eff.graph == null)
                    continue;
                CompileOneGraphAbilities(data, eff.graph, eff, result);
            }
            return result;
        }

        /// <summary>编译单张效果图为能力并追加进 result（effdto = 该效果图的数据层，携带数据型过滤器）</summary>
        private static void CompileOneGraphAbilities(CardCustomData data, GraphData graph, CardEffectData effdto, List<AbilityData> result)
        {
            //★先体检"有节点但一个入口(Event)都没有"的图：下面 foreach 只认 type==Event，
            //  这种图一个都进不去 ⇒ 编译出 0 个能力 ⇒ **卡打出去毫无反应**，
            //  而且导入时不报错、运行时也不会尝试跑图（图压根没挂到任何能力上）→ 完全静默的"配了没用"坑。
            //  实测出处：sample_pool.json 的 custom_JJw8BRifSr8「幽幽柚子」＝
            //  Action(Draw) + IntegerConst(value=5) + 112010 + 101003，无 Event 节点，编译后 abilities=0
            //  （PoolSmokeProbe 报"图「幽幽柚子」有节点但没有入口(Event)节点"）。
            //  注意：0 节点的空占位图（DTO 里每卡的默认 NewGraph）是正常的，不在此列。
            bool has_event_node = false;
            if (graph.nodes != null)
            {
                foreach (GraphNode n0 in graph.nodes)
                    if (n0 != null && n0.type == GraphNodeType.Event) { has_event_node = true; break; }
                if (!has_event_node && graph.nodes.Count > 0)
                    Debug.LogWarning("[规则图] 卡「" + (data.title ?? "?") + "」(" + data.id + ") 的图「" + graph.name
                        + "」有 " + graph.nodes.Count + " 个节点，但**没有任何入口(Event)节点** → 该图编译不出任何能力，"
                        + "打出去不会有任何效果。请从节点库拖入一个入口节点（主动效果入口/亡语入口/光环入口…）"
                        + "并把它的动作流接到后续节点。");
            }

            //控制节点(212001 分支 / 212002 重复)下游若接了内置直通动作：内置动作按无条件触发编译（不走分支/循环），提醒改用 NodeDoc 动作
            foreach (GraphNode bn in graph.nodes)
            {
                if (bn == null || bn.type != GraphNodeType.Action
                    || (bn.action != "212001" && bn.action != "212002"))
                    continue;
                string ctrl = bn.action == "212001" ? "分支动作" : "重复动作";
                foreach (GraphNode ba in FindReachableActions(graph, bn.id))
                {
                    if (ba != null && string.IsNullOrEmpty(ba.category))
                    {
                        Debug.LogWarning("[规则图] " + ctrl + "下游包含内置直通动作（" + ba.action + " " + ba.title + "）：内置动作不走" + ctrl + "，" + (bn.action == "212001" ? "分支内" : "循环体内") + "请使用 NodeDoc 动作（如 造成伤害或法伤）");
                        break;
                    }
                }
            }
            foreach (GraphNode ev in graph.nodes)
            {
                if (ev == null || ev.type != GraphNodeType.Event)
                    continue;

                //★作用范围=全部卡牌 的入口：走"全局入口"通道（任何卡触发都跑），
                //  不再编译成本卡自己的能力 —— 否则同一张卡触发时会跑两遍。
                if (IsGlobalScopeEntry(ev)
                    && ev.action != "ActivateEffect" && ev.action != "ActivateAbility"
                    && ev.action != "PassiveEffect" && ev.action != "AuraEffect")
                    continue;

                //★被动效果入口（PassiveEffect）的「生效/失效」两条预留线：编译成独立能力（状态切换驱动）。
                //  两条线都没接线 = 不生成任何能力 → 对既有卡（含只用亡语动作口的卡）零影响。
                if (ev.action == "PassiveEffect")
                    CompilePassiveStateLines(data, graph, effdto, ev, result);

                AbilityTrigger trigger = ResolveEventTrigger(ev);
                if (trigger == AbilityTrigger.None)
                {
                    //被动入口非亡语标签：按约定跳过（其余触发时机等用户后续提供的节点格式）
                    if (ev.action == "PassiveEffect")
                        continue;
                    Debug.LogWarning("[规则图] 触发器未支持，跳过: " + ev.action +
                        (ev.action == "EventEffect" ? "（监听事件: " + GraphRuntime.GetFieldString(ev, "event_name", "") + "）" : ""));
                    continue;
                }
                //光环入口的下游 NodeDoc 动作线：由 GameLogic.SyncAuraEffects 在**目标被这个光环施加的那一刻执行一次**
                //（不是每帧执行 → 伤害/抽牌这类一次性动作也安全）。若想让某个数值"持续跟随后变化"，
                //用它施加的「增益定义」来承载（增益系统的属性修改是持续生效的）。

                //光环效果入口：增益由入口节点自身定义（增益定义/生效区域/作用区域）；
                //· 具名增益（buffs.json 的 BuffData.id）→ aura_group/aura_buff，由 SyncAuraEffects 差分施加/移除；
                //· 旧式 StatusType 枚举 → EffectAddStatus + 每帧 Ongoing 重算（原行为不变）。
                if (ev.action == "AuraEffect")
                {
                    AbilityData aura = BuildAuraAbility(data, graph, ev);
                    if (aura != null)
                    {
                        RegisterAbility(aura);
                        result.Add(aura);
                    }
                    continue;
                }

                List<GraphNode> acts = FindReachableActions(graph, ev.id,
                    ev.action == "PassiveEffect" ? new string[] { "enable", "disable" } : null);
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
                //★C 批：入口**没有动作节点、只带连锁数据**时也要编译出能力 —— 典型是"选择菜单"入口
                //  （如 bear：target=ChoiceSelector + chain_abilities=[选项A, 选项B]，入口自身没有效果；
                //   引擎 SelectChoice 读的正是这份 chain_abilities）。否则整条能力会被丢掉、卡变白板。
                bool has_chain_only = !has_node_doc && effdto != null
                    && effdto.chain_ability_ids != null && effdto.chain_ability_ids.Count > 0;
                if (has_node_doc || has_chain_only)
                {
                    AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
                    ab.id = StableAbilityId(data.id, ev.action, ev.id);   //★稳定 id（同入口重导入/保存不换 id）
                    ab.trigger = trigger;

                    //图中含"需选择目标"类动作(伤害/消灭/治疗目标卡) → 需要目标解析
                    //★同时记录"这些动作的目标口是否都已由连线给出"：图自己给了目标时不该再弹"选择目标"
                    //  （用户实报：伤害.targets ← 入口.卡牌 已经连好了，触发陷阱还要手动选目标）。
                    bool wants_target = false;
                    bool all_targets_wired = true;
                    foreach (GraphNode act in acts)
                    {
                        if (act != null && !string.IsNullOrEmpty(act.category)
                            && (act.action == "202001" || act.action == "202016"
                                || act.action == "202013" || act.action == "202039" || act.action == "202047"
                                || act.action == "202041"))
                        {
                            wants_target = true;
                            if (!IsTargetInputWired(graph, act))
                                all_targets_wired = false;
                        }
                    }

                    //入场目标配置（读"打出时"事件节点上的字段，语义同"入场技能目标设置"面板）：
                    //   target_side  归属：任意/敌方/友方
                    //   target_scope 范围：任意/仅角色/仅英雄（仅英雄=直接选玩家，不再弹目标）
                    //   target_mode  ★迁移期新增：**显式目标模式**（内置卡迁移用）。有该字段时直接采用、
                    //               不走下面的 wants_target 启发式；旧图没有该字段 → 行为完全不变。
                    string tside = GraphRuntime.GetFieldString(ev, "target_side", "任意");
                    string tscope = GraphRuntime.GetFieldString(ev, "target_scope", "任意");
                    string tmode = GraphRuntime.GetFieldString(ev, "target_mode", "");
                    bool hero_only = tscope == "仅英雄";
                    List<ConditionData> tconds = new List<ConditionData>();
                    AbilityTarget atarget = AbilityTarget.None;
                    if (!string.IsNullOrEmpty(tmode))
                    {
                        atarget = ParseTargetMode(tmode);
                    }
                    else if (wants_target)
                    {
                        if (hero_only)
                        {
                            //打脸/打自己英雄：直接按归属指向玩家，无需弹出选择
                            atarget = tside == "友方" ? AbilityTarget.PlayerSelf : AbilityTarget.PlayerOpponent;
                        }
                        else if (!is_spell && all_targets_wired)
                        {
                            //★非法术 + 目标口已全部连线 → **不再弹"选择目标"**：
                            //  图自己就给了目标（典型：陷阱的 伤害.targets ← 入口.卡牌，触发时=发起攻击的卡），
                            //  再让玩家手选一遍既多余、又容易被当成"接线没生效"（用户实报：
                            //  "这里是直接对攻击者造成伤害，为什么还要我手动选择目标"）。
                            //  法术不走这条：法术的"打出时瞄准(PlayTarget)"是固有流程，不能去掉。
                            atarget = AbilityTarget.None;
                        }
                        else
                        {
                            //目标通道：法术=打出时拖选(PlayTarget)；随从/装备等=入场后弹出选择(SelectTarget)
                            atarget = is_spell ? AbilityTarget.PlayTarget : AbilityTarget.SelectTarget;
                            if (tside == "敌方")
                                tconds.Add(MakeOwnerCondition(false));
                            else if (tside == "友方")
                                tconds.Add(MakeOwnerCondition(true));
                            if (tscope == "仅角色")
                            {
                                ConditionTargetRole role = ScriptableObject.CreateInstance<ConditionTargetRole>();
                                role.player_only = false;   //只能选场上角色，不能选玩家
                                tconds.Add(role);
                            }
                        }
                    }
                    ab.target = atarget;

                    EffectRunGraph run = ScriptableObject.CreateInstance<EffectRunGraph>();
                    run.graph = graph;
                    run.trigger_action = ev.action;
                    ab.effects = new EffectData[] { run };
                    WorkshopLog.Info("[事件编译] 卡=" + data.id + " 入口=" + ev.action + " 触发器=" + trigger
                        + " 能力id=" + ab.id + " 动作线=" + (FindReachableActions(graph, ev.id).Count > 0 ? "有" : "无"));
                    //★迁移期新增（内置卡迁移 D 批）：**数据型条件**直通（CardEffectData.conditions_trigger/target）。
                    //  图里表达不了的条件（ConditionCount / SlotRange / 类型含阵营·种族 / SelectedValue…）不再让
                    //  整条能力作废，而是原样交回引擎判定（发动前 AreTriggerConditionsMet、解析目标集合
                    //  AreTargetConditionsMet）——与旧能力逐行一致；CardSelector 的候选列表/选择校验也才筛得对。
                    //  空/缺省 = 条件只由图守卫表达（旧图行为完全不变）。
                    ConditionData[] dtrigger = effdto != null
                        ? DeserializeComponents<ConditionData>(effdto.conditions_trigger) : null;
                    ab.conditions_trigger = (dtrigger != null && dtrigger.Length > 0)
                        ? dtrigger : new ConditionData[0];
                    ab.conditions_target = tconds.ToArray();
                    ConditionData[] dtarget = effdto != null
                        ? DeserializeComponents<ConditionData>(effdto.conditions_target) : null;
                    if (dtarget != null && dtarget.Length > 0)
                        ab.conditions_target = AppendConditions(ab.conditions_target, dtarget);
                    //★迁移期新增（内置卡迁移 D 批）：数据型目标过滤器（CardEffectData.filters_target）原样还原。
                    //  过滤器作用于**整个目标集合**（AbilityData.GetCardTargets/GetPlayerTargets/GetSlotTargets 里
                    //  逐条 FilterTargets），图只做"目标相对"的动作 → 两者不冲突、也不重复施加。
                    //  空/缺省 = 不过滤（旧图行为不变）。
                    ab.filters_target = effdto != null
                        ? (DeserializeComponents<FilterData>(effdto.filters_target) ?? new FilterData[0])
                        : new FilterData[0];
                    //★迁移期新增（内置卡迁移 B 批）：数据型状态（能力自带 status）→ 原样还原成 ab.status，
                    //  由引擎在 DoEffects 里按目标施加（旧行为逐行一致；图里不做"添加状态"节点）。
                    ab.status = ResolveStatusList(effdto != null ? effdto.status_ids : null);
                    //能力自带 status 时，旧引擎用 `ability.value` 施加状态（DoEffects）→ 从入口字段还原（转换器写入）
                    if (ab.status != null && ab.status.Length > 0)
                        ab.value = GraphRuntime.GetFieldInt(ev, "status_ability_value", ab.value);
                    //★迁移期新增（内置卡迁移 C 批）：连锁能力随数据带走（chain_ability_ids）→ 还原成 ab.chain_abilities。
                    //  引擎在 AfterAbilityResolved 里逐个 TriggerCardAbility(chain, caster)，与旧能力逐行一致；
                    //  被引用的连锁能力资产=池内 id 引用（同 EffectAddAbility，Phase 5 打包随池打）。
                    ab.chain_abilities = ResolveAbilityList(effdto != null ? effdto.chain_ability_ids : null);
                    //★标题只作**内部标识**；描述**默认留空** —— 卡面/悬浮提示会把"有描述的能力"渲染成一行
                    //  （CardData.GetAbilitiesDesc → "<b>标题:</b> 描述"），留样板文字会让卡面多出一行
                    //  "主动效果入口：规则图执行"（实测的"显示异常"）。要让卡面显示，请在入口节点填「能力描述」。
                    ab.title = (ev.title ?? ev.action) + "：规则图执行";
                    ab.desc = "";
                    ApplyEntryOverrides(ab, ev, is_spell, graph);
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
                    ability.id = StableAbilityId(data.id, ev.action + "_" + act.action, act.id);   //★稳定 id
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
        }

        /// <summary>★迁移期新增：入口 target_mode 文本 → AbilityTarget（内置卡迁移用，旧图无此字段）。
        /// 取值与转换器（AbilityToGraphConverter）写出的文本一一对应；未知返回 None。</summary>
        private static AbilityTarget ParseTargetMode(string mode)
        {
            switch (mode)
            {
                case "无": return AbilityTarget.None;
                case "自身": return AbilityTarget.Self;
                case "施法者玩家": return AbilityTarget.PlayerSelf;
                case "对手玩家": return AbilityTarget.PlayerOpponent;
                case "全体玩家": return AbilityTarget.AllPlayers;
                case "所有角色": return AbilityTarget.AllCardsBoard;
                case "双方手牌": return AbilityTarget.AllCardsHand;
                case "所有牌堆": return AbilityTarget.AllCardsAllPiles;
                case "卡牌定义": return AbilityTarget.AllCardData;
                case "选择目标": return AbilityTarget.SelectTarget;
                case "打出目标": return AbilityTarget.PlayTarget;
                case "所有槽位": return AbilityTarget.AllSlots;       //★D 批：逐槽结算（槽位条件+过滤器由引擎筛，图内用呼叫上下文槽位落位）
                case "触发者": return AbilityTarget.AbilityTriggerer;
                case "卡牌选择": return AbilityTarget.CardSelector;   //★D 批：选择器（候选=条件+过滤器筛过的全区域卡）
                case "选择器": return AbilityTarget.ChoiceSelector;   //★C 批：选择菜单（chain_abilities=菜单选项，引擎 SelectChoice 消费）
                default: return AbilityTarget.None;
            }
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
                //★被攻击时/被攻击后：编辑器新补的入口（GameLogic 早就在触发 OnBeforeDefend/OnAfterDefend，
                //  只是 MapGraphTrigger 一直没这两个映射 → 写了入口也编译不出能力）
                case "OnBeforeDefend": return AbilityTrigger.OnBeforeDefend;
                case "OnAfterDefend": return AbilityTrigger.OnAfterDefend;
                case "OnDraw": return AbilityTrigger.OnDraw;
                //★迁移期新增（内置卡迁移 D 批）：引擎原生触发时机（非图事件广播）——
                //  action 名与 AbilityTrigger 枚举名一致；转换器（AbilityToGraphConverter）写出的入口同名。
                //  OnPlayOther=别的卡被打出时 / OnAfterAttack=攻击结算后 / OnKill=攻击中击杀 / OnDeathOther=别的卡死亡
                case "OnPlayOther": return AbilityTrigger.OnPlayOther;
                case "OnAfterAttack": return AbilityTrigger.OnAfterAttack;
                case "OnKill": return AbilityTrigger.OnKill;
                case "OnDeathOther": return AbilityTrigger.OnDeathOther;
                case "ActivateEffect": return AbilityTrigger.OnPlay;    //主动效果入口（zmcs）= 打出时触发（炉石战吼/法术）
                case "ActivateAbility": return AbilityTrigger.Activate; //起动式效果入口（zmcs）= 点击发动（英雄技能/卡牌主动技）
                case "AuraEffect": return AbilityTrigger.Ongoing;       //光环效果入口（zmcs）
                //图事件入口（EventContext 广播）：action 名与 AbilityTrigger 枚举名一致（事件预设 BuildGraphEventPresets）
                case "OnBeforePlay": return AbilityTrigger.OnBeforePlay;
                case "OnAfterPlay": return AbilityTrigger.OnAfterPlay;   //使用卡牌后（打出结算完成后广播）
                case "OnBeforeDamage": return AbilityTrigger.OnBeforeDamage;
                case "OnAfterDamage": return AbilityTrigger.OnAfterDamage;
                case "OnAfterDraw": return AbilityTrigger.OnAfterDraw;
                case "OnBeforeHeal": return AbilityTrigger.OnBeforeHeal;
                case "OnAfterHeal": return AbilityTrigger.OnAfterHeal;
                case "OnBeforeTransform": return AbilityTrigger.OnBeforeTransform;
                case "OnAfterTransform": return AbilityTrigger.OnAfterTransform;
                case "OnBeforeEquip": return AbilityTrigger.OnBeforeEquip;
                case "OnAfterEquip": return AbilityTrigger.OnAfterEquip;
                case "OnBeforeDeath": return AbilityTrigger.OnBeforeDeath;
                case "OnAfterDeath": return AbilityTrigger.OnAfterDeath;
                case "OnBeforeDiscard": return AbilityTrigger.OnBeforeDiscard;
                case "OnAfterDiscard": return AbilityTrigger.OnAfterDiscard;
                case "OnBeforeGameStart": return AbilityTrigger.OnBeforeGameStart;
                case "OnAfterGameStart": return AbilityTrigger.OnAfterGameStart;
                case "OnBeforeGameEnd": return AbilityTrigger.OnBeforeGameEnd;
                case "OnAfterGameEnd": return AbilityTrigger.OnAfterGameEnd;
                case "OnBeforeTurnStart": return AbilityTrigger.OnBeforeTurnStart;
                case "OnAfterTurnStart": return AbilityTrigger.OnAfterTurnStart;
                case "OnBeforeTurnEnd": return AbilityTrigger.OnBeforeTurnEnd;
                case "OnAfterTurnEnd": return AbilityTrigger.OnAfterTurnEnd;
                //起动式（Activate）能力的「时/后」：GameLogic.CastAbility 与 AfterAbilityResolved 广播
                case "OnBeforeActivate": return AbilityTrigger.OnBeforeActivate;
                case "OnAfterActivate": return AbilityTrigger.OnAfterActivate;
                //禁锢（202014）的「时/后」：NodeDocRunner 202014 分支广播
                case "OnBeforeFreeze": return AbilityTrigger.OnBeforeFreeze;
                case "OnAfterFreeze": return AbilityTrigger.OnAfterFreeze;
                //增益属性变动（206003 设置增益属性）的「时/后」：NodeDocRunner 206003 分支广播
                case "OnBeforeBuffPropChange": return AbilityTrigger.OnBeforeBuffPropChange;
                case "OnAfterBuffPropChange": return AbilityTrigger.OnAfterBuffPropChange;
                //卡牌属性变动（202037 设置卡牌属性 / SetCardHpClearDamage 设置生命值清伤害）的「时/后」：
                //由 NodeDocRunner 这两处写入分支广播；口径A=只有显式设置属性算变动（受伤/治疗/增益重算不算）
                case "OnBeforeCardPropChange": return AbilityTrigger.OnBeforeCardPropChange;
                case "OnAfterCardPropChange": return AbilityTrigger.OnAfterCardPropChange;
                //护甲变动（202018 增加护甲 / 202019 增加固定数量护甲 / 202020 失去护甲）的「时/后」：
                //主体=持有护甲的英雄卡；202018/202019 是"加值"、202020 是"设为剩余值"，都在 NodeDocRunner 广播
                case "OnBeforeArmorChange": return AbilityTrigger.OnBeforeArmorChange;
                case "OnAfterArmorChange": return AbilityTrigger.OnAfterArmorChange;
                //封印(沉默)变动（202040 封印=施加 Silenced；清状态=移除 Silenced）的「时/后」：主体=该卡
                case "OnBeforeSilenceChange": return AbilityTrigger.OnBeforeSilenceChange;
                case "OnAfterSilenceChange": return AbilityTrigger.OnAfterSilenceChange;
                //全局图事件：添加增益时/后（BuffRuntime.AddBuff 广播）
                //（爆牌时/后已下线：用户决定"爆牌不做"）
                case "OnBeforeAddBuff": return AbilityTrigger.OnBeforeAddBuff;
                case "OnAfterAddBuff": return AbilityTrigger.OnAfterAddBuff;
                default: return AbilityTrigger.None;
            }
        }

        /// <summary>入口节点触发器解析：常规入口按 action 映射；
        /// 被动效果入口按 标签列表（v1 仅「亡语」→OnDeath，其余时机待后续节点格式）；
        /// 事件效果入口按 监听事件 字段（zmcs 事件名 → TCG2 触发器）。</summary>
        private static AbilityTrigger ResolveEventTrigger(GraphNode ev)
        {
            switch (ev.action)
            {
                case "PassiveEffect":
                    return GraphRuntime.GetFieldString(ev, "tag_list", "亡语") == "亡语" ? AbilityTrigger.OnDeath : AbilityTrigger.None;
                case "EventEffect":
                    return MapEventName(GraphRuntime.GetFieldString(ev, "event_name", "回合结束"));
                default:
                    return MapGraphTrigger(ev.action);
            }
        }

        /// <summary>该动作节点的「目标」输入口是否已有连线（=图自己提供目标，不需要玩家再选）。
        /// 兼容不同节点的命名：targets / target / cards（伤害、消灭、治疗、指定目标等）。</summary>
        private static bool IsTargetInputWired(GraphData graph, GraphNode act)
        {
            if (graph == null || act == null)
                return false;
            string[] names = { "targets", "target", "cards" };
            for (int i = 0; i < names.Length; i++)
            {
                GraphPin pin = graph.GetPinByName(act.id, names[i]);
                if (pin != null && graph.GetIncomingLink(act.id, pin.id) != null)
                    return true;
            }
            return false;
        }

        /// <summary>zmcs 事件入口的监听事件名 → TCG2 触发器（未支持返回 None）。
        /// 注：受伤/治疗类事件 TCG2 无对应触发器，暂不提供选项。</summary>
        private static AbilityTrigger MapEventName(string name)
        {
            switch (name)
            {
                case "回合结束": return AbilityTrigger.EndOfTurn;
                case "回合开始": return AbilityTrigger.StartOfTurn;
                case "打出牌": return AbilityTrigger.OnPlayOther;   //zmcs UseEvent=任意卡打出（含其他卡）
                case "使用卡牌后":
                case "使用后": return AbilityTrigger.OnAfterPlay;   //打出并结算完成后
                case "攻击时": return AbilityTrigger.OnBeforeAttack;
                case "死亡时": return AbilityTrigger.OnDeath;
                case "抽到时": return AbilityTrigger.OnDraw;
                default: return AbilityTrigger.None;
            }
        }

        /// <summary>光环效果入口 → Ongoing 光环能力，两条通道：
        /// · 增益定义 = **具名增益**（buffs.json 的 BuffData.id）→ 写 aura_group/aura_buff，
        ///   由 GameLogic.SyncAuraEffects 差分施加（目标进入范围施加一次、离开/来源离场精确移除），
        ///   入口「动作」出口的动作线随"施加那一刻"执行一次；
        /// · 增益定义 = 旧式 StatusType 枚举 → EffectAddStatus + 每帧 Ongoing 重算（原行为不变）。
        /// 生效区域 → AbilityTarget，作用区域 → 目标归属条件（ConditionOwner）。返回 null 表示未配置/未识别。</summary>
        private static AbilityData BuildAuraAbility(CardCustomData data, GraphData graph, GraphNode ev)
        {
            string buff = GraphRuntime.GetFieldString(ev, "buff", "AddAttack");

            //★ 具名增益通道（增益池里能查到 → 走"持续施加这份增益"）
            BuffData bdef = ResolveAuraBuffDefine(buff);
            if (bdef != null)
                return BuildAuraBuffAbility(data, graph, ev, bdef);
            //★顺序兜底：增益池若此刻尚未加载（历史 DataLoader 顺序：卡池在前），值长得像增益 id（buff_…）时
            //  仍按"具名增益"编译（真定义运行时由 BuffPoolIO.Get 取），否则这条光环会被当成未知 StatusType 丢掉。
            if (!string.IsNullOrEmpty(buff) && buff.StartsWith("buff_", System.StringComparison.Ordinal))
            {
                BuffData stub = new BuffData();
                stub.id = buff;
                WorkshopLog.Info("[规则图] 光环的增益定义此刻未加载 → 按具名增益 id 编译：" + buff + "（运行期取真实定义）");
                return BuildAuraBuffAbility(data, graph, ev, stub);
            }

            StatusData sdata = StatusData.Get(ParseEnum(buff, StatusType.None));
            if (sdata == null)
            {
                Debug.LogWarning("[规则图] 光环效果入口的增益定义未识别（既不是增益池里的增益，也不是状态枚举）: " + buff);
                return null;
            }

            EffectAddStatus effect = ScriptableObject.CreateInstance<EffectAddStatus>();
            effect.status = sdata;
            effect.value = 1;
            effect.duration = 0;

            AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
            ab.id = StableAbilityId(data.id, "aura", ev.id);   //★稳定 id
            ab.trigger = AbilityTrigger.Ongoing;
            ApplyAuraTargetArea(ab, ev);

            ab.effects = new EffectData[] { effect };
            ab.value = 1;
            ab.conditions_trigger = new ConditionData[0];
            ab.filters_target = new FilterData[0];
            ab.status = new StatusData[0];
            ab.chain_abilities = new AbilityData[0];
            ab.title = (ev.title ?? "光环效果入口") + "：" + buff;
            ab.desc = "";   //★描述留空：卡面不给"有描述的能力"多渲染一行样板文字（显示异常）
            return ab;
        }

        /// <summary>「增益定义」按**增益池**解析：先按 id 精确查（与编辑器下拉一致），
        /// 再遍历池按 id/标题匹配（兼容把标题写进字段的情况）。不是增益池里的 → 返回 null（交给旧 StatusType 通道）。</summary>
        private static BuffData ResolveAuraBuffDefine(string buff)
        {
            if (string.IsNullOrEmpty(buff))
                return null;
            BuffData bd = BuffPoolIO.Get(buff);
            if (bd != null)
                return bd;
            List<BuffData> all = BuffPoolIO.GetAll();
            if (all != null)
            {
                foreach (BuffData b in all)
                {
                    if (b == null)
                        continue;
                    if (b.id == buff)
                        return b;
                    if (!string.IsNullOrEmpty(b.title) && b.title == buff)
                        return b;
                }
            }
            return null;
        }

        /// <summary>光环动作线能否"每次同步重算"：只允许**幂等**动作（设置属性 / 设置增益属性 / 控制流）。
        /// 伤害 / 抽牌 / 加增益 / 召唤 这类每帧执行会重复发生 → 不重算（只在目标进入范围时执行一次）。</summary>
        private static bool AllAuraActionsAreRepeatable(GraphData graph, string from_id)
        {
            List<GraphNode> acts = FindReachableActionsFromPin(graph, from_id, "out");
            for (int i = 0; i < acts.Count; i++)
            {
                GraphNode a = acts[i];
                if (a == null)
                    continue;
                switch (a.action)
                {
                    case "202037":   //设置卡牌属性
                    case "202007":   //设置属性
                    case "206003":   //设置增益属性（值不变则不写入、不广播，可安全重算）
                    case "212001":   //分支动作
                    case "212002":   //重复动作
                    case "212005":   //重复动作直到
                    case "212006":   //停止重复动作
                    case "212007":   //跳过重复动作
                        break;
                    default:
                        WorkshopLog.Info("[规则图] 光环动作线含「" + a.action + "」→ 只在目标进入范围时执行一次（不按状态重算）");
                        return false;
                }
            }
            return acts.Count > 0;
        }

        /// <summary>具名增益型光环：Ongoing 能力（aura_group / aura_buff）+ 动作线。
        /// 动作线全为幂等动作（设置属性/设置增益属性）→ 每次收敛点按当前状态重算（值随状态跟随）；
        /// 含一次性动作 → 只在目标进入范围时执行一次。</summary>
        private static AbilityData BuildAuraBuffAbility(CardCustomData data, GraphData graph, GraphNode ev, BuffData bdef)
        {
            AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
            ab.id = StableAbilityId(data.id, "aurabuff", ev.id);   //★稳定 id
            ab.trigger = AbilityTrigger.Ongoing;
            ApplyAuraTargetArea(ab, ev);
            ab.aura_group = "ag_" + data.id + "_" + ev.id;   //★账本分组键（同一张卡的多个光环入口可区分）
            ab.aura_buff = bdef.id;                          //★要持续施加的具名增益（SyncAuraEffects 用）
            ab.value = 1;
            ab.conditions_trigger = new ConditionData[0];
            ab.filters_target = new FilterData[0];
            ab.status = new StatusData[0];
            ab.chain_abilities = new AbilityData[0];

            //动作线（入口「动作」出口）
            bool has_line = graph != null && FindReachableActionsFromPin(graph, ev.id, "out").Count > 0;
            if (has_line)
            {
                EffectRunGraph run = ScriptableObject.CreateInstance<EffectRunGraph>();
                run.graph = graph;
                run.trigger_action = ev.action;   //入口匹配仍按 AuraEffect
                ab.effects = new EffectData[] { run };
            }
            else
            {
                ab.effects = new EffectData[0];
            }

            //动作线可否重算（幂等动作线 → 每次收敛点重跑，值随状态跟随；否则只在进入范围时跑一次）
            ab.aura_repeat = has_line && AllAuraActionsAreRepeatable(graph, ev.id);
            WorkshopLog.Info("[光环编译] 卡=" + data.id + " 节点=" + ev.id + " 增益=" + ab.aura_buff
                + " 分组=" + ab.aura_group
                + " 生效区域=" + GraphRuntime.GetFieldString(ev, "live_area", "?")
                + " 作用区域=" + GraphRuntime.GetFieldString(ev, "target_area", "?")
                + " 动作线=" + (has_line ? "有" : "无") + " 可重算=" + ab.aura_repeat);

            ab.title = (ev.title ?? "光环效果入口") + "：" + (string.IsNullOrEmpty(bdef.title) ? bdef.id : bdef.title);
            ab.desc = "";   //★同上：描述留空，避免卡面多出样板行
            return ab;
        }

        /// <summary>光环入口的 生效区域/作用区域 → 能力字段。
        /// 生效区域 = 载体（本卡）需要在的区域；作用区域 = 被施加增益的卡所在区域。
        /// 两者都是 ZoneNames 口径的区域名（"任意"= 不限）。</summary>
        private static void ApplyAuraTargetArea(AbilityData ab, GraphNode ev)
        {
            if (ab == null || ev == null)
                return;
            ab.aura_zone = GraphRuntime.GetFieldString(ev, "live_area", "任意");
            ab.aura_target_zone = GraphRuntime.GetFieldString(ev, "target_area", "任意");
            //引擎 Ongoing 通道的口径（旧式 StatusType 光环仍走它）：战场/手牌/其它→所有牌堆
            ab.target = ab.aura_target_zone == "手牌" ? AbilityTarget.AllCardsHand
                      : ab.aura_target_zone == "战场" ? AbilityTarget.AllCardsBoard
                      : AbilityTarget.AllCardsAllPiles;
            ab.conditions_target = new ConditionData[0];
        }

        /// <summary>入口的一个目标槽（读自图节点字段 target_type{i}/target_side{i}/target_error{i}）。</summary>
        private class EntrySlotSpec
        {
            public int slot;        //图槽号（1 基，可空洞）
            public string type;     //无/角色/英雄
            public string side;     //任意/敌方/友方
            public string error;    //无合法目标被跳过时的提示文案
            public bool legacy;     //旧图：未编号的 target_type / targetCondition（单目标）
        }

        /// <summary>读入口的目标槽列表（按槽号升序）。无编号字段时把旧的单一 target_type 当作槽1（向后兼容）。</summary>
        private static List<EntrySlotSpec> ReadEntryTargetSlots(GraphNode ev)
        {
            List<EntrySlotSpec> list = new List<EntrySlotSpec>();
            if (ev == null || ev.fields == null)
                return list;
            //槽1：优先编号字段 target_type1（新图/迁移后）；否则读旧的未编号 target_type（旧图兼容，条件口=未编号 targetCondition）
            bool num1 = HasFieldName(ev, "target_type1");
            bool legacy1 = !num1 && HasFieldName(ev, "target_type");
            if (num1 || legacy1)
            {
                list.Add(new EntrySlotSpec
                {
                    slot = 1,
                    type = GraphRuntime.GetFieldString(ev, num1 ? "target_type1" : "target_type", "无"),
                    side = GraphRuntime.GetFieldString(ev, num1 ? "target_side1" : "target_side", "任意"),
                    error = GraphRuntime.GetFieldString(ev, num1 ? "target_error1" : "target_error", ""),
                    legacy = legacy1,
                });
            }
            //槽 ≥2：编号字段（由编辑器「＋新增目标」生成；槽号只增不复用，可有空洞）
            foreach (FieldCustomData f in ev.fields)
            {
                if (f == null || string.IsNullOrEmpty(f.name) || !f.name.StartsWith("target_type"))
                    continue;
                string num = f.name.Substring("target_type".Length);
                if (string.IsNullOrEmpty(num) || !int.TryParse(num, out int n) || n < 2)
                    continue;   //未编号=槽1（上面已处理）
                list.Add(new EntrySlotSpec
                {
                    slot = n,
                    type = f.value,
                    side = GraphRuntime.GetFieldString(ev, "target_side" + n, "任意"),
                    error = GraphRuntime.GetFieldString(ev, "target_error" + n, ""),
                });
            }
            list.Sort((a, b) => a.slot.CompareTo(b.slot));
            return list;
        }

        /// <summary>节点是否含某字段（用于判定槽1走编号字段还是旧的未编号字段）</summary>
        private static bool HasFieldName(GraphNode ev, string name)
        {
            if (ev == null || ev.fields == null)
                return false;
            foreach (FieldCustomData f in ev.fields)
            {
                if (f != null && f.name == name)
                    return true;
            }
            return false;
        }

        /// <summary>槽的归属/角色条件（不含「目标N条件」图条件链），供多目标逐槽校验。</summary>
        private static List<ConditionData> BuildSlotBaseConditions(EntrySlotSpec s)
        {
            List<ConditionData> conds = new List<ConditionData>();
            if (s.side == "敌方")
                conds.Add(MakeOwnerCondition(false));
            else if (s.side == "友方")
                conds.Add(MakeOwnerCondition(true));
            if (s.type == "角色")
            {
                ConditionTargetRole role = ScriptableObject.CreateInstance<ConditionTargetRole>();
                role.player_only = false;
                role.allow_player = true;   //zmcs「角色」含英雄：玩家英雄也可选
                conds.Add(role);
            }
            else if (s.type == "英雄")
            {
                ConditionTargetRole role = ScriptableObject.CreateInstance<ConditionTargetRole>();
                role.player_only = true;    //只接受英雄（玩家目标）
                role.allow_player = true;
                conds.Add(role);
            }
            return conds;
        }

        /// <summary>槽的「目标N条件」图条件链（无连线返回 null；旧图走未编号的 targetCondition）。</summary>
        private static ConditionData BuildSlotGraphCondition(GraphNode ev, GraphData graph, EntrySlotSpec s)
        {
            if (graph == null)
                return null;
            string pin_name = s.legacy ? "targetCondition" : "targetCondition" + s.slot;
            GraphPin pin = graph.GetPinByName(ev.id, pin_name);
            GraphLink link = pin != null ? graph.GetIncomingLink(ev.id, pin.id) : null;
            if (link == null)
                return null;
            ConditionGraphTarget cond = ScriptableObject.CreateInstance<ConditionGraphTarget>();
            cond.graph = graph;
            cond.entry_node_id = ev.id;
            cond.slot_index = s.legacy ? 0 : s.slot;
            return cond;
        }

        /// <summary>入口节点级参数覆盖（在能力编译完成后调用）：
        /// 主动效果入口=打出时触发（炉石战吼/法术）；
        /// 目标槽 ≥2 → 顺序逐槽多目标（法术也走 SelectTarget：PlayTarget 只有一个落点），槽条件为槽私有；
        /// 目标槽 ≤1 → 沿用旧的单目标编译（角色=PlayTarget/SelectTarget，英雄=直接指向玩家）。</summary>
        private static void ApplyEntryOverrides(AbilityData ab, GraphNode ev, bool is_spell, GraphData graph)
        {
            if (ab == null || ev == null)
                return;
            //★「每回合一次」：**与入口类型无关**——迁移期把旧 ConditionOnce 编译成入口字段 once_per_turn
            //  （转换器 AbilityToGraphConverter 写出）。必须放在下面"只处理目标槽入口"的早退之前，
            //  否则非起动式入口（打出时/亡语…）会静默丢掉这个发动条件。
            //★迁移期：入口「能力名」对**所有入口**生效（转换器写 ability_title）——
            //  原来只对起动式入口读，导致打出时/亡语/事件类入口编译出的能力名变成"节点标题：规则图执行"（难看）
            string ab_title_any = GraphRuntime.GetFieldString(ev, "ability_title", "");
            if (!string.IsNullOrEmpty(ab_title_any))
                ab.title = ab_title_any;
            //★「能力描述」同样对**所有入口**生效（原来只有起动式入口读它）：
            //  图编译出的能力默认 desc 为空（卡面不会多渲染样板行）；想让它显示在卡面上就填这个字段。
            string ab_desc_any = GraphRuntime.GetFieldString(ev, "ability_desc", "");
            if (!string.IsNullOrEmpty(ab_desc_any))
                ab.desc = ab_desc_any;

            if (GraphRuntime.GetFieldString(ev, "once_per_turn", "false") == "true")
                ab.conditions_trigger = AppendCondition(ab.conditions_trigger,
                    ScriptableObject.CreateInstance<ConditionOnce>());   //每回合一次（ability_played 每回合清空）

            //带目标槽的图入口：主动效果入口（打出触发）/ 起动式效果入口（点击发动）共用同一套目标槽编译
            if (ev.action != "ActivateEffect" && ev.action != "ActivateAbility")
                return;

            //起动式效果入口：能力自身的参数（灵力费用/横置/能力名与描述）——与目标槽无关，先读
            if (ev.action == "ActivateAbility")
            {
                ab.mana_cost = GraphRuntime.GetFieldInt(ev, "mana_cost", 0);
                ab.exhaust = GraphRuntime.GetFieldString(ev, "exhaust", "true") != "false";
                string at = GraphRuntime.GetFieldString(ev, "ability_title", "");
                if (!string.IsNullOrEmpty(at))
                    ab.title = at;
                string ad = GraphRuntime.GetFieldString(ev, "ability_desc", "");
                if (!string.IsNullOrEmpty(ad))
                    ab.desc = ad;
            }

            List<EntrySlotSpec> slots = ReadEntryTargetSlots(ev);
            List<EntrySlotSpec> active = new List<EntrySlotSpec>();
            foreach (EntrySlotSpec s in slots)
            {
                if (!string.IsNullOrEmpty(s.type) && s.type != "无")
                    active.Add(s);
            }

            //兜底：编号槽存在但类型为"无"（迁移/历史操作遗留的空槽 target_type1），而旧的未编号
            //target_type 字段配置了有效类型 → 以旧字段为准。否则空槽会把类型顶掉，能力被编译成
            //target=None（无目标），表现为"打出后图不执行、双方血量无变化"。
            if (active.Count == 0)
            {
                string legacy_type = GraphRuntime.GetFieldString(ev, "target_type", "");
                if (!string.IsNullOrEmpty(legacy_type) && legacy_type != "无")
                {
                    Debug.LogWarning("[规则图] 入口编号目标槽类型为空，回退旧 target_type 字段: " + legacy_type
                        + "（建议在节点上重新设置 目标类型）");
                    EntrySlotSpec legacy = new EntrySlotSpec
                    {
                        slot = 1,
                        type = legacy_type,
                        side = GraphRuntime.GetFieldString(ev, "target_side", "任意"),
                        error = GraphRuntime.GetFieldString(ev, "target_error", ""),
                        legacy = true,
                    };
                    slots = new List<EntrySlotSpec> { legacy };
                    active.Add(legacy);
                }
            }

            //★迁移期（内置卡迁移 D 批）：**没有配置目标槽**的入口若带显式 `target_mode`（迁移写出），
            //  则目标已由 target_mode 权威给出（上面 ab.target = ParseTargetMode(tmode)），
            //  不要再走下面"无槽 = 无目标"的旧兜底 —— 否则迁移过来的 Activate 卡（dark_stallion/phoenix/
            //  hero_fire/forest/water）会被覆盖成 ab.target=None，发动后效果打空（实测：编译冒烟逮到）。
            //  注：手工在编辑器里配了目标槽（active.Count>0）时依旧以槽配置为准，旧图不受影响。
            if (active.Count == 0 && !string.IsNullOrEmpty(GraphRuntime.GetFieldString(ev, "target_mode", "")))
                return;

            // ---- 多目标：顺序逐槽选择（§冲突1：法术也走 SelectTarget） ----
            if (active.Count >= 2)
            {
                ab.multi_target = true;
                ab.target = AbilityTarget.SelectTarget;
                ab.conditions_target = new ConditionData[0];   //归属/角色条件下沉到各槽私有条件，避免全局二次过滤
                //目标去重（默认关闭=允许多个槽选同一张卡；勾选后同一张卡只能被一个槽选中）
                ab.unique_targets = GraphRuntime.GetFieldString(ev, "unique_targets", "false") == "true";
                List<AbilityTargetSlot> tslots = new List<AbilityTargetSlot>();
                foreach (EntrySlotSpec s in active)
                {
                    AbilityTargetSlot ts = new AbilityTargetSlot();
                    ts.node_slot = s.slot;
                    ts.label = s.type;
                    ts.side = s.side;
                    ts.error = s.error;
                    List<ConditionData> conds = BuildSlotBaseConditions(s);
                    ConditionData chain = BuildSlotGraphCondition(ev, graph, s);
                    if (chain != null)
                        conds.Add(chain);
                    ts.conditions = conds.ToArray();
                    tslots.Add(ts);
                }
                ab.target_slots = tslots.ToArray();
                return;
            }

            // ---- 单目标 / 无目标：沿用旧行为（槽1 的类型/归属） ----
            EntrySlotSpec first = active.Count == 1 ? active[0] : (slots.Count > 0 ? slots[0] : null);
            string tt = first != null ? first.type : GraphRuntime.GetFieldString(ev, "target_type", "无");
            string side = first != null ? first.side : GraphRuntime.GetFieldString(ev, "target_side", "任意");
            if (tt == "角色")
            {
                //法术=打出时拖选(PlayTarget)；随从/装备等=入场后弹出选择(SelectTarget)，且只能选场上角色
                ab.target = is_spell ? AbilityTarget.PlayTarget : AbilityTarget.SelectTarget;
                if (side == "敌方")
                    ab.conditions_target = AppendCondition(ab.conditions_target, MakeOwnerCondition(false));
                else if (side == "友方")
                    ab.conditions_target = AppendCondition(ab.conditions_target, MakeOwnerCondition(true));
                ConditionTargetRole role = ScriptableObject.CreateInstance<ConditionTargetRole>();
                role.player_only = false;
                role.allow_player = true;   //zmcs「角色」含英雄：玩家英雄也可选
                ab.conditions_target = AppendCondition(ab.conditions_target, role);
            }
            else if (tt == "英雄")
            {
                ab.target = side == "友方" ? AbilityTarget.PlayerSelf : AbilityTarget.PlayerOpponent;
            }
            else
            {
                ab.target = AbilityTarget.None; //无目标：走无目标分支直接执行（NodeDoc 动作自行决定目标）
                //新拖的入口默认零目标槽；下游若引用了 目标卡牌N，往往说明用户忘了点「＋ 新增目标」，提示避免"无反应"
                if (graph != null && GraphHasNodeDocAction(graph, ev.id))
                    Debug.LogWarning("[规则图] " + (ev.title ?? "图入口") + " 没有目标槽（点入口上的「＋ 新增目标」并设置目标类型），当前按无目标执行: "
                        + ev.action);
            }
            //目标条件（zmcs：目标1条件 ← 条件节点链，如 卡牌类型判断=仆从）
            if (first != null)
            {
                ConditionData chain = BuildSlotGraphCondition(ev, graph, first);
                if (chain != null)
                    ab.conditions_target = AppendCondition(ab.conditions_target, chain);
            }

            //编译诊断：打印入口目标槽字段实况与最终编译结果（排查"打出无反应/无目标"时对照此日志）
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (EntrySlotSpec s in slots)
                sb.Append(s.slot).Append(':').Append(string.IsNullOrEmpty(s.type) ? "(空)" : s.type).Append(' ');
            WorkshopLog.Info("[规则图] 入口编译 → target=" + ab.target + " multi=" + ab.multi_target
                + " is_spell=" + is_spell + " 槽[" + sb.ToString().TrimEnd() + "]");
        }

        /// <summary>追加条件到数组（保持非 null 约定）</summary>
        private static ConditionData[] AppendCondition(ConditionData[] array, ConditionData extra)
        {
            List<ConditionData> list = new List<ConditionData>();
            if (array != null)
                list.AddRange(array);
            list.Add(extra);
            return list.ToArray();
        }

        /// <summary>追加一组条件到数组（数据型条件直通用；保持非 null 约定）</summary>
        private static ConditionData[] AppendConditions(ConditionData[] array, ConditionData[] extra)
        {
            List<ConditionData> list = new List<ConditionData>();
            if (array != null)
                list.AddRange(array);
            if (extra != null)
                list.AddRange(extra);
            return list.ToArray();
        }

        /// <summary>能力 id 列表 → AbilityData 数组（连锁/被引用能力；未注册的跳过并警告）</summary>
        private static AbilityData[] ResolveAbilityList(List<string> ids)
        {
            if (ids == null || ids.Count == 0)
                return new AbilityData[0];
            List<AbilityData> list = new List<AbilityData>();
            foreach (string id in ids)
            {
                if (string.IsNullOrEmpty(id))
                    continue;
                AbilityData a = AbilityData.Get(id);
                if (a != null)
                    list.Add(a);
                else
                    Debug.LogWarning("[规则图] 被引用的连锁能力未注册，已跳过: " + id);
            }
            return list.ToArray();
        }

        /// <summary>StatusType 枚举名列表 → StatusData 数组（数据型状态直通用，与 AbilityCustomData.status_ids 同口径）</summary>
        private static StatusData[] ResolveStatusList(List<string> status_ids)
        {
            if (status_ids == null || status_ids.Count == 0)
                return new StatusData[0];
            List<StatusData> list = new List<StatusData>();
            foreach (string sid in status_ids)
            {
                StatusData sdata = StatusData.Get(ParseEnum(sid, StatusType.None));
                if (sdata != null)
                    list.Add(sdata);
                else if (!string.IsNullOrEmpty(sid))
                    Debug.LogWarning("[规则图] 状态未识别，已跳过: " + sid);
            }
            return list.ToArray();
        }

        /// <summary>某事件节点下游是否连有 NodeDoc(zmcs) 动作（沿 Flow 可达，预留出口不计）</summary>
        private static bool GraphHasNodeDocAction(GraphData graph, string event_id)
        {
            GraphNode ev = graph.GetNode(event_id);
            string[] skip = (ev != null && ev.action == "PassiveEffect") ? new string[] { "enable", "disable" } : null;
            foreach (GraphNode act in FindReachableActions(graph, event_id, skip))
            {
                if (act != null && !string.IsNullOrEmpty(act.category))
                    return true;
            }
            return false;
        }

        // ============ 被动效果入口（PassiveEffect）的「生效/失效」线编译 ============
        // 背景：被动入口有三个 Flow 出口 —— 动作(out，亡语等主效果) / 生效(enable) / 失效(disable)。
        // 动作口的触发时机由入口「标签列表」映射成 AbilityTrigger（亡语=OnDeath）；后两者是**状态切换**语义：
        // 卡进入/离开入口「生效区域」、以及变形（SetCard 换定义）时，应执行对应的动作线。
        // 做法：两条线各编译成一个独立能力（trigger = OnPassiveEnable/OnPassiveDisable，共享 passive_group 分组键、
        // 带 passive_area 生效区域），运行时由 GameLogic.SyncPassiveEffects 做「区域状态差分」后触发
        // （进场=生效、离场=失效、变形=旧形态失效+新形态生效）。
        // ★没有接线就不生成能力 → 对既有卡零影响。

        /// <summary>编译被动入口的 生效/失效 两条线（各自只在有可达动作时才生成能力）</summary>
        private static void CompilePassiveStateLines(CardCustomData data, GraphData graph, CardEffectData effdto,
            GraphNode ev, List<AbilityData> result)
        {
            if (data == null || graph == null || ev == null || result == null)
                return;
            //分组键：同一入口的 生效/失效 两条线共用（Card.passive_groups 记录"这张卡当前已生效的分组"）
            string group = "pg_" + data.id + "_" + ev.id;
            //生效区域：与入口字段同名同口径（战场/手牌/牌库/墓地/装备区/全部区域）
            string area = GraphRuntime.GetFieldString(ev, "live_area", "战场");
            CompileOnePassiveStateLine(data, graph, effdto, ev, "enable", AbilityTrigger.OnPassiveEnable, group, area, result);
            CompileOnePassiveStateLine(data, graph, effdto, ev, "disable", AbilityTrigger.OnPassiveDisable, group, area, result);
        }

        /// <summary>编译被动入口的一条状态切换线（pin=enable/disable）</summary>
        private static void CompileOnePassiveStateLine(CardCustomData data, GraphData graph, CardEffectData effdto,
            GraphNode ev, string pin, AbilityTrigger trigger, string group, string area, List<AbilityData> result)
        {
            List<GraphNode> acts = FindReachableActionsFromPin(graph, ev.id, pin);
            if (acts.Count == 0)
                return;   //该线没接任何动作 → 不生成能力（保持"预留出口"的零影响）

            AbilityData ab = ScriptableObject.CreateInstance<AbilityData>();
            ab.id = StableAbilityId(data.id, "passive_" + pin, ev.id);   //★稳定 id
            ab.trigger = trigger;
            //状态切换触发：**不走目标选择**（否则进出场/变形时会弹目标选择框）。
            //线内动作若引用了「目标卡牌」口且没连线，按解释器既有回退口径落到施法卡自身（见 NodeDocRunner.ResolveInputCard）。
            ab.target = AbilityTarget.None;
            ConditionData[] dtrigger = effdto != null
                ? DeserializeComponents<ConditionData>(effdto.conditions_trigger) : null;
            ab.conditions_trigger = (dtrigger != null && dtrigger.Length > 0) ? dtrigger : new ConditionData[0];
            ab.conditions_target = new ConditionData[0];
            ab.filters_target = new FilterData[0];
            ab.passive_group = group;
            ab.passive_area = string.IsNullOrEmpty(area) ? "战场" : area;

            EffectRunGraph run = ScriptableObject.CreateInstance<EffectRunGraph>();
            run.graph = graph;
            run.trigger_action = ev.action;     //入口匹配仍按 PassiveEffect（同一条入口节点）
            run.entry_pin = pin;                //★只走这条出口（解释器据此跳过另一条线与动作口）
            ab.effects = new EffectData[] { run };

            ab.title = (ev.title ?? ev.action) + "：" + (pin == "enable" ? "生效" : "失效") + "：规则图执行";
            ab.desc = "";   //★描述留空（同上）
            ApplyEntryOverrides(ab, ev, false, graph);   //入口级覆盖（能力名/能力描述/每回合一次；目标槽部分对非主动入口直接返回）
            RegisterAbility(ab);
            result.Add(ab);
        }

        /// <summary>只沿入口指定出口(pin)找可达动作（被动入口的 生效/失效 线专用）。
        /// 与 FindReachableActions 的区别：起点只展开 pin 这一个 Flow 出口，其余出口（含动作口 out）一律不走。</summary>
        private static List<GraphNode> FindReachableActionsFromPin(GraphData graph, string from_id, string pin)
        {
            List<GraphNode> result = new List<GraphNode>();
            if (graph == null)
                return result;

            Stack<GraphNode> stack = new Stack<GraphNode>();
            HashSet<string> visited = new HashSet<string>();
            GraphNode start = graph.GetNode(from_id);
            if (start != null)
            {
                visited.Add(start.id);
                foreach (GraphLink link in graph.GetOutgoing(start.id))
                {
                    GraphPin out_pin = graph.GetPin(start.id, link.from_pin);
                    if (out_pin == null || out_pin.name != pin)
                        continue;
                    if (out_pin.type != NodeValueType.Flow && out_pin.type != NodeValueType.None)
                        continue;
                    GraphNode next = graph.GetNode(link.to_node);
                    if (next != null)
                        stack.Push(next);
                }
            }

            while (stack.Count > 0)
            {
                GraphNode node = stack.Pop();
                if (node == null || visited.Contains(node.id))
                    continue;
                visited.Add(node.id);
                if (node.type == GraphNodeType.Action)
                {
                    result.Add(node);
                    continue;   //动作节点不再向下
                }
                foreach (GraphLink link in graph.GetOutgoing(node.id))
                {
                    GraphPin out_pin = graph.GetPin(node.id, link.from_pin);
                    if (out_pin != null && out_pin.type != NodeValueType.Flow && out_pin.type != NodeValueType.None)
                        continue;   //取值线不驱动执行
                    GraphNode next = graph.GetNode(link.to_node);
                    if (next != null)
                        stack.Push(next);
                }
            }
            return result;
        }

        /// <summary>从事件节点出发，沿输出连线查找可达的动作节点（跳过条件/值节点）。
        /// skip_pin_names：跳过起点上这些短名的 Flow 输出口（如被动入口预留的 生效/失效动作）。</summary>
        private static List<GraphNode> FindReachableActions(GraphData graph, string from_id, params string[] skip_pin_names)
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
                    if (out_pin != null && skip_pin_names != null && Array.IndexOf(skip_pin_names, out_pin.name) >= 0)
                        continue;   //预留出口（如被动入口 生效/失效动作）：v1 不执行
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
                //三套灵力（按模式关键字分流，未知/空值维持旧默认"增加上限"）：
                //  恢复当前            → 加当前灵力 mana
                //  增加上限(空水晶)     → 加灵力上限 mana_max
                //  增加最大灵力值(硬顶) → 加最大灵力值 mana_max_total
                mana.increase_value = mode.Contains("当前");
                mana.increase_max = mode.Contains("上限");
                mana.increase_max_total = mode.Contains("最大");
                if (!mana.increase_value && !mana.increase_max && !mana.increase_max_total)
                    mana.increase_max = true;   //未知/空 → 旧默认行为
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
            else if (mode == "出牌选目标") { info.target = is_spell ? AbilityTarget.PlayTarget : AbilityTarget.SelectTarget; }
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
                        //伤害/消灭等需"目标"的动作：法术打出拖选目标，随从等入场后弹出选择
                        info.target = is_spell ? AbilityTarget.PlayTarget : AbilityTarget.SelectTarget;
                        break;
                }
            }
            return info;
        }

        /// <summary>创建"目标归属"条件：ally=true 只选友方，false 只选敌方</summary>
        private static ConditionData MakeOwnerCondition(bool ally)
        {
            ConditionOwner cond = ScriptableObject.CreateInstance<ConditionOwner>();
            cond.oper = ally ? ConditionOperatorBool.IsTrue : ConditionOperatorBool.IsFalse;
            return cond;
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

        /// <summary>★迁移期新增：**允许池卡覆盖同名内置卡**（运行时替换注册表条目、**不改动资产**，可逆）。
        /// 开关 = persistentDataPath/Workshop/override_builtin.txt；**文件内容 = 允许覆盖的池文件名清单（每行一个）**，
        /// 内容为空 = 允许所有池。为什么按文件限定：Workshop 下常并存多个池（Dlc1 Pack/Elite Pack/示例卡池…），
        /// 否则"最后导入的池"会把前面池的覆盖又盖回去，验证时会看到不是迁移版的卡。
        /// 用途：内置卡迁移验证 —— 卡组/「模拟测试」走 UserDeckData(tid) → CardData.Get(id)，
        /// 覆盖后整个对局里这些卡就是**迁移版（规则图）**的版本。删文件即恢复内置卡优先。</summary>
        private static bool OverrideBuiltinFor(string file_name)
        {
            string flag = Path.Combine(SaveFolder, "override_builtin.txt");
            if (!File.Exists(flag) || string.IsNullOrEmpty(file_name))
                return false;
            try
            {
                string txt = File.ReadAllText(flag).Trim();
                if (string.IsNullOrEmpty(txt))
                    return true;                      //空内容 = 允许所有池
                foreach (string line in txt.Split('\n'))
                    if (line.Trim().Equals(file_name, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>注册生成的 CardData 到静态字典（id 冲突时跳过）</summary>
        public static bool RegisterCard(CardData card)
        {
            bool overrode;
            return RegisterCard(card, false, out overrode);
        }

        /// <summary>注册 CardData；allow_override=true 时同名已存在则**替换**注册表条目（见 OverrideBuiltin）。</summary>
        public static bool RegisterCard(CardData card, bool allow_override, out bool overrode)
        {
            overrode = false;
            if (card == null || string.IsNullOrEmpty(card.id))
                return false;

            CardData existing = CardData.Get(card.id);
            if (existing != null)
            {
                if (existing == card)
                    return true;   //同一实例重复注册：幂等成功（刷新卡牌列表等场景会重复调用）
                if (!allow_override)
                {
                    Debug.LogWarning("卡牌 id 已存在，跳过注册: " + card.id);
                    return false;
                }
                //★覆盖：运行时替换注册表条目（内置资产对象本身不变，重开游戏即恢复）
                overrode = true;
                if (CardData.card_dict.ContainsKey(card.id))
                    CardData.card_dict[card.id] = card;
                else
                    CardData.card_dict.Add(card.id, card);
                CardData.card_list.RemoveAll(c => c != null && c.id == card.id);
                CardData.card_list.Add(card);
                return true;
            }

            //字典与列表可能不同步（历史卸载/重载只清了其中一边），统一用索引器写入并去重列表，
            //杜绝「An item with the same key has already been added」异常
            if (CardData.card_dict.ContainsKey(card.id))
                CardData.card_dict[card.id] = card;
            else
                CardData.card_dict.Add(card.id, card);

            CardData.card_list.RemoveAll(c => c != null && c.id == card.id);   //同 id 旧实例移除，保证一 id 一条
            CardData.card_list.Add(card);
            custom_ids.Add(card.id);
            return true;
        }

        /// <summary>★稳定能力 id：同卡同入口重复导入/保存时 id 不变 → 注册表**替换**而非累积。
        /// 旧实现带随机 Guid：每次保存/导入都生成新 id → 同一张图的能力在注册表里越积越多
        /// → 事件广播时同一条图被执行 N 遍（增益翻倍/伤害翻倍），且已发到手牌的卡实例引用旧 id 失效。</summary>
        private static string StableAbilityId(string card_id, string kind, string key)
        {
            string raw = (card_id ?? "") + "|" + (kind ?? "") + "|" + (key ?? "");
            int h = 0;
            foreach (char ch in raw)
                h = h * 31 + ch;
            return "graph_" + card_id + "_" + (kind ?? "ev") + "_" + h.ToString("x8");
        }

        /// <summary>注册生成的 AbilityData 到静态字典（★id 冲突时**替换**：重导入/重保存后以最新编译为准）</summary>
        public static bool RegisterAbility(AbilityData ability)
        {
            if (ability == null || string.IsNullOrEmpty(ability.id))
                return false;

            //★不能用 AbilityData.Get(id) != null 判断"是否已注册"：AbilityData 是 ScriptableObject，
            //  重开卡池/刷新列表时旧实例被 Destroy，字典里留下已销毁引用 —— Unity 的 == 重载
            //  把已销毁对象判成 null → Get 取到了引用却"看似 null" → 误走 Add 分支 →
            //  「An item with the same key has already been added」（实测：点卡池「编辑」即崩）。
            //  用 ContainsKey 纯字典判断，销毁与否都走"替换"路径。
            if (AbilityData.ability_dict.ContainsKey(ability.id))
            {
                AbilityData.ability_dict[ability.id] = ability;                 //替换字典
                AbilityData.ability_list.RemoveAll(a => a == null || a.id == ability.id);  //顺带清掉已销毁的旧条目（先判 null 再取 id，防 MissingReference）
                AbilityData.ability_list.Add(ability);                          //列表同步去重
                return true;
            }

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
