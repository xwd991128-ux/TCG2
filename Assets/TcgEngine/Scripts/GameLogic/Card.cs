using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Workshop;

namespace TcgEngine
{
    //Represent the current state of a card during the game (data only)

    [System.Serializable]
    public class Card
    {
        public string card_id;
        public string uid;
        public int player_id;
        public string variant_id;

        public Slot slot;
        public bool exhausted;
        public int damage = 0;

        public int mana = 0;
        public int attack = 0;
        public int hp = 0;

        public int mana_ongoing = 0;
        public int attack_ongoing = 0;
        public int hp_ongoing = 0;

        public string equipped_uid = null;

        public List<CardTrait> traits = new List<CardTrait>();
        public List<CardTrait> ongoing_traits = new List<CardTrait>();

        public List<string> keywords = new List<string>();  //拥有的关键词（KeywordData.id）

        //---- 增益「属性修改」的运行时可变项（由 BuffRuntime.ReapplyNative 统一重算，每张卡独立）----
        //不复用 ongoing_traits/ongoing_status：那些由其它系统按回合清理，混用会互相清掉。
        public List<string> buff_added_traits = new List<string>();      //增益附加的种族（TraitData.id）
        public List<string> buff_removed_traits = new List<string>();    //增益移除的种族（抑制 HasTrait）
        public List<string> buff_added_keywords = new List<string>();    //增益附加的关键词（KeywordData.id）
        public List<string> buff_removed_keywords = new List<string>();  //增益移除的关键词（抑制 HasKeyword）

        /// <summary>卡自身**持久移除**的关键词（KeywordData.id）= 抑制表，语义"这张卡已经失去了它"。
        /// 与上面两张 buff_* 表的**关键区别**：它不挂在任何增益上，也**不被 BuffRuntime.ReapplyNative 清空重建** ——
        /// 所以"失去圣盾/潜行"能稳定生效。若只 RemoveStatus，下一次任何增益增减触发重算，
        /// 引擎会按 Card.keywords 把状态补回来（表现=删了又冒出来）。
        /// 判定口径：keywords 列表**仍保留**该 id（来源不变），但 HasKeyword/GetAllKeywords/关键词规则图/状态重建都跳过它。
        /// 生命周期：进入本表后本局持续，直到 RestoreKeyword，或被 Clear()（复位/离场重进）整体清空回到卡面初始。</summary>
        public List<string> removed_keywords = new List<string>();

        /// <summary>卡牌**自定义属性**的**当前值**（key=属性名，value=当前值）。
        /// 与「增益」那套完全同构：**声明**来自卡池（CardCustomData.custom_prop_defs，见 CardPoolIO.GetCustomData），
        /// 这里只存"这张卡当前的取值"；没被规则图改过时读到的就是声明里的初始值（见 GetCustomProp）。
        /// 与关键词/状态的 key-value 表分开存：它们是存在性，这里是可自由命名的数值。
        /// 生命周期：Clear()（复位/离场重进）清空 → 回到声明初始值。</summary>
        public List<BuffProp> custom_props = new List<BuffProp>();

        public List<CardStatus> status = new List<CardStatus>();
        public List<CardStatus> ongoing_status = new List<CardStatus>();

        public List<CardBuff> buffs = new List<CardBuff>();  //增益实例（规则图 206001 施加，BuffData 定义）

        public List<string> abilities = new List<string>();
        public List<string> abilities_ongoing = new List<string>();

        /// <summary>这张卡**当前已生效**的「被动效果」分组键（AbilityData.passive_group：被动入口的 生效/失效 线共用）。
        /// 语义：分组键在表里 = 该被动的「生效动作」已执行过、还没执行「失效动作」。
        /// 与 buff_* / removed_keywords 一样属于**运行期状态**，但有一条关键约定：**Clear() 不清它** ——
        /// 离场/复位/送区域后，由 GameLogic.SyncPassiveEffects 扫到"已生效但已不在生效区域"才补发失效动作
        /// （若在此清掉，下一帧就再也看不出它曾经生效过 → 失效动作会被吞掉）。
        /// AI 预测树的克隆必须同步本表，否则预测会重复触发生效/失效线（见 Clone）。</summary>
        public List<string> passive_groups = new List<string>();

        [System.NonSerialized] private int hash = 0;
        [System.NonSerialized] private CardData data = null;
        [System.NonSerialized] private VariantData vdata = null;
        [System.NonSerialized] private List<AbilityData> abilities_data = null;

        public Card(string card_id, string uid, int player_id) { this.card_id = card_id; this.uid = uid; this.player_id = player_id; }

        public virtual void Refresh() { exhausted = false; }
        public virtual void ClearOngoing() { ongoing_status.Clear(); ongoing_traits.Clear(); ClearOngoingAbility(); attack_ongoing = 0; hp_ongoing = 0; mana_ongoing = 0; }

        public virtual void Clear()
        {
            ClearOngoing(); Refresh(); damage = 0; status.Clear(); buffs.Clear();
            buff_added_traits.Clear(); buff_removed_traits.Clear();
            buff_added_keywords.Clear(); buff_removed_keywords.Clear();
            removed_keywords.Clear();   //复位 = 回到卡面初始：持久移除的关键词在此恢复（离场重进后圣盾会回来）
            custom_props.Clear();       //自定义属性的"当前值"也清空 → 回到卡池声明里的初始值
            SetCard(CardData, VariantData); //Reset to initial stats
            equipped_uid = null;
        }

        public virtual int GetAttack() { return Mathf.Max(attack + attack_ongoing, 0); }
        public virtual int GetHP() { return Mathf.Max(hp + hp_ongoing - damage, 0); }
        public virtual int GetHP(int offset) { return Mathf.Max(hp + hp_ongoing - damage + offset, 0); }
        public virtual int GetHPMax() { return Mathf.Max(hp + hp_ongoing, 0); }
        public virtual int GetMana() { return Mathf.Max(mana + mana_ongoing, 0); }

        public virtual void SetCard(CardData icard, VariantData cvariant)
        {
            if (icard == null)
            {
                //卡牌定义缺失（未注册/资源被删）：以前会在这里 NRE，而且是在**服务端开局流程**里抛 →
                //异步任务静默中止 → 客户端永远停在「Connecting to server…」。这里改成报错并跳过赋值。
                Debug.LogError("[Card] SetCard 收到空 CardData（卡牌定义缺失/未注册）→ 已跳过本次赋值");
                return;
            }
            data = icard;
            card_id = icard.id;
            variant_id = cvariant != null ? cvariant.id : variant_id;
            attack = icard.attack;
            hp = icard.hp;
            mana = icard.mana;
            SetTraits(icard);
            SetKeywords(icard);
            SetAbilities(icard);
        }

        /// <summary>种族/属性：**逐项防空**。卡牌资源（CardData 资产）里的 traits/stats 数组可能有空槽
        /// （Missing 引用 / 编辑器里删了引用但数组没缩），旧写法 `trait.id` 直接 NRE，
        /// 而它发生在服务端 SetPlayerDeck → 整局开不起来（客户端卡在 "Connecting to server…"）。
        /// 现在跳过空槽并点名"哪张卡的第几项"，便于回头修资源。</summary>
        public void SetTraits(CardData icard)
        {
            traits.Clear();
            if (icard == null)
                return;
            if (icard.traits != null)
            {
                for (int i = 0; i < icard.traits.Length; i++)
                {
                    TraitData trait = icard.traits[i];
                    if (trait == null)
                    {
                        Debug.LogWarning("[Card] " + icard.id + " 的 traits[" + i + "] 是空引用（资源里该槽 Missing/未填）→ 已跳过，建议清掉该空槽");
                        continue;
                    }
                    SetTrait(trait.id, 0);
                }
            }
            if (icard.stats != null)
            {
                for (int i = 0; i < icard.stats.Length; i++)
                {
                    TraitStat stat = icard.stats[i];
                    if (stat.trait == null)     //TraitStat 是 struct：本身不可能为 null，只需判它内部的 trait 引用
                    {
                        Debug.LogWarning("[Card] " + icard.id + " 的 stats[" + i + "] 的 trait 为空引用 → 已跳过");
                        continue;
                    }
                    SetTrait(stat.trait.id, stat.value);
                }
            }
        }

        public void SetKeywords(CardData icard)
        {
            keywords.Clear();
            if (icard.keywords == null)
                return;
            foreach (KeywordData keyword in icard.keywords)
            {
                if (keyword == null || keywords.Contains(keyword.id))
                    continue;
                keywords.Add(keyword.id);
                //★ 被持久移除的关键词（removed_keywords）：id 照常记录（来源不变），但**不挂状态** ——
                //  否则任何一次 SetCard/重建都会把"已经失去的圣盾"加回来。
                if (removed_keywords.Contains(keyword.id))
                    continue;
                //原生机制关键词（风怒/冲锋/圣盾…）：直接转成永久状态，复用 GameLogic 原有机制判定
                if (keyword.status_type != StatusType.None)
                    AddStatus(keyword.status_type, 0, 0);
            }
        }

        public bool HasKeyword(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            if (removed_keywords.Contains(id))
                return false;                       //被卡自身**持久移除**（本局持续，直到 RestoreKeyword/复位）
            if (buff_removed_keywords.Contains(id))
                return false;                       //被增益属性修改移除（抑制，直到增益消失）
            return keywords.Contains(id) || buff_added_keywords.Contains(id);
        }

        /// <summary>该关键词是否已被卡自身持久移除（removed_keywords）。给规则图/UI 判断"是否永久失去"用。</summary>
        public bool IsKeywordRemoved(string id)
        {
            return !string.IsNullOrEmpty(id) && removed_keywords.Contains(id);
        }

        /// <summary>
        /// 【失去关键词（持久）】记进 removed_keywords，并**立即移除**该关键词绑定的原生状态
        /// （"当前有没有"马上变，如圣盾/潜行当场消失）。
        /// 与"增益移除关键词"的区别：本表生命周期属于**这张卡自己**，不随增益消失或重算而恢复 ——
        /// 因此不会被下一次任何增益增减（BuffRuntime.ReapplyNative）重新加回来。
        /// 恢复用 RestoreKeyword；Clear()（复位/离场重进）会清空本表，回到卡面初始。
        /// 返回 true = 本次新记入。
        /// </summary>
        public bool RemoveKeywordPersist(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            bool added = !removed_keywords.Contains(id);
            if (added)
                removed_keywords.Add(id);

            KeywordData kw = KeywordData.Get(id);
            if (kw != null && kw.status_type != StatusType.None)
                RemoveStatus(kw.status_type);       //立即生效（不只等下次重算）
            return added;
        }

        /// <summary>【恢复关键词（撤销持久移除）】从 removed_keywords 移出；若该卡当前确实应有此关键词
        /// （定义或增益给的、且没被增益另行抑制），补挂它的原生状态。返回 true = 确实移出了记录。</summary>
        public bool RestoreKeyword(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            if (!removed_keywords.Remove(id))
                return false;

            if (!keywords.Contains(id) && !buff_added_keywords.Contains(id))
                return true;                        //这张卡本来就没有它 → 只清记录
            if (buff_removed_keywords.Contains(id))
                return true;                        //仍被增益抑制 → 不补状态

            KeywordData kw = KeywordData.Get(id);
            if (kw != null && kw.status_type != StatusType.None && !HasStatus(kw.status_type))
                AddStatus(kw.status_type, 0, 0);
            return true;
        }

        /// <summary>
        /// 【状态存在性】统一写入入口：把某状态设为"有 / 没有"。对应参考节点里
        /// 「具有隐匿 / 具有护盾 / 被封印 / 被禁锢」这类可设置的**存在性属性**。
        ///   · on = true  → 挂状态（value = 状态数值，如护甲值/冻结值；0 = 只表示"有"且永久）；
        ///                  若该状态由"卡自身关键词"承载且此前被持久移除过 → 先 RestoreKeyword（否则会被抑制挡住）。
        ///   · on = false → 优先按**关键词来源**走 RemoveKeywordPersist（持久移除，之后任何重算都不会复活）；
        ///                  找不到关键词来源（增益/效果临时挂的状态）才退回 RemoveStatus。
        /// value != 0 时按"整值替换"（先移除再加），避免护甲/冻结这类数值状态叠加。
        /// 返回 true = 确实执行了改动。
        /// </summary>
        public bool SetStatusPresence(StatusType type, bool on, int value = 0)
        {
            if (type == StatusType.None)
                return false;

            string kw_id = KeywordData.KeywordIdForStatus(type);   //该状态由哪个关键词承载（可能没有）

            if (on)
            {
                if (!string.IsNullOrEmpty(kw_id) && removed_keywords.Contains(kw_id))
                    RestoreKeyword(kw_id);                          //曾被持久移除 → 先恢复关键词（内部会补挂状态）
                if (value != 0)
                {
                    RemoveStatus(type);                             //整值替换（护甲值 / 冻结值）
                    AddStatus(type, value, 0);
                }
                else if (!HasStatus(type))
                {
                    AddStatus(type, 0, 0);
                }
                return true;
            }

            if (!string.IsNullOrEmpty(kw_id))
                return RemoveKeywordPersist(kw_id);                 //持久移除（之后重算不会复活）
            RemoveStatus(type);                                     //无关键词来源：只删状态（增益/效果临时挂的）
            return true;
        }

        /// <summary>全部关键词（基础 + 增益附加，去掉"卡自身持久移除"与"增益移除"的）</summary>
        public List<string> GetAllKeywords()
        {
            List<string> all = new List<string>();
            foreach (string k in keywords)
            {
                if (!removed_keywords.Contains(k) && !buff_removed_keywords.Contains(k) && !all.Contains(k))
                    all.Add(k);
            }
            foreach (string k in buff_added_keywords)
            {
                if (!removed_keywords.Contains(k) && !all.Contains(k))
                    all.Add(k);
            }
            return all;
        }

        //---------------- 自定义属性（当前值） ----------------
        //声明来自卡池（CardCustomData.custom_prop_defs，用 CardPoolIO.GetCustomData(id) 取），
        //运行时只存"这张卡当前的取值"。没被改过时读到的是声明里的初始值 —— 与「增益」那套完全同构。

        /// <summary>读自定义属性**当前值**：本卡改过用本卡的；否则回退卡池声明里的**初始值**；都没有 = 0。</summary>
        public int GetCustomProp(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            if (custom_props != null)
            {
                foreach (BuffProp p in custom_props)
                {
                    if (p != null && p.key == name)
                        return p.value;
                }
            }
            return GetCustomPropInit(name);
        }

        /// <summary>卡池声明里的初始值（找不到 → 0）。声明存在 CardPoolIO 的登记表里
        /// —— 运行时 CardData 是 ScriptableObject 资产，装不下这些声明（项目既有约定）。</summary>
        public int GetCustomPropInit(string name)
        {
            if (string.IsNullOrEmpty(name) || CardData == null)
                return 0;
            CardCustomData raw = CardPoolIO.GetCustomData(CardData.id);
            return raw != null ? raw.CustomPropInit(name) : 0;
        }

        /// <summary>该名字是否是卡池里**声明过**的自定义属性（用于读取时区分"自定义属性"与"关键词"）。</summary>
        public bool IsDeclaredCustomProp(string name)
        {
            if (string.IsNullOrEmpty(name) || CardData == null)
                return false;
            CardCustomData raw = CardPoolIO.GetCustomData(CardData.id);
            return raw != null && raw.FindCustomProp(name) != null;
        }

        /// <summary>本卡是否存过该自定义属性的**当前值** —— 没在卡池声明过也能被规则图写上（202037 设自定义属性），
        /// 所以读取判定要"声明过 **或** 存过"两条件都看，否则"设了却读不回来"。</summary>
        public bool HasStoredCustomPropValue(string name)
        {
            if (string.IsNullOrEmpty(name) || custom_props == null)
                return false;
            foreach (BuffProp p in custom_props)
            {
                if (p != null && p.key == name)
                    return true;
            }
            return false;
        }

        /// <summary>写自定义属性**当前值**（同名覆盖、没有则新增）。返回 true = 值真的变了。</summary>
        public bool SetCustomProp(string name, int value)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (custom_props == null)
                custom_props = new List<BuffProp>();
            foreach (BuffProp p in custom_props)
            {
                if (p != null && p.key == name)
                {
                    if (p.value == value)
                        return false;
                    p.value = value;
                    return true;
                }
            }
            custom_props.Add(new BuffProp { key = name, value = value });
            return true;
        }

        public void SetAbilities(CardData icard)
        {
            abilities.Clear();
            abilities_ongoing.Clear();
            if (abilities_data != null)
                abilities_data.Clear();
            if (icard == null || icard.abilities == null)
                return;
            foreach (AbilityData ability in icard.abilities)
            {
                if (ability == null)
                    continue;      //能力数组里的空槽同样跳过（与 traits/keywords 同规）
                AddAbility(ability);
            }
        }
        
        //------ Custom Traits/Stats ---------

        public void SetTrait(string id, int value)
        {
            CardTrait trait = GetTrait(id);
            if (trait != null)
            {
                trait.value = value;
            }
            else
            {
                trait = new CardTrait(id, value);
                traits.Add(trait);
            }
        }

        public void AddTrait(string id, int value)
        {
            CardTrait trait = GetTrait(id);
            if (trait != null)
                trait.value += value;
            else
                SetTrait(id, value);
        }

        public void AddOngoingTrait(string id, int value)
        {
            CardTrait trait = GetOngoingTrait(id);
            if (trait != null)
            {
                trait.value += value;
            }
            else
            {
                trait = new CardTrait(id, value);
                ongoing_traits.Add(trait);
            }
        }

        public void RemoveTrait(string id)
        {
            for (int i = traits.Count - 1; i >= 0; i--)
            {
                if (traits[i].id == id)
                    traits.RemoveAt(i);
            }
        }

        public CardTrait GetTrait(string id)
        {
            foreach (CardTrait trait in traits)
            {
                if (trait.id == id)
                    return trait;
            }
            return null;
        }

        public CardTrait GetOngoingTrait(string id)
        {
            foreach (CardTrait trait in ongoing_traits)
            {
                if (trait.id == id)
                    return trait;
            }
            return null;
        }

        public int GetTraitValue(TraitData trait)
        {
            if (trait != null)
                return GetTraitValue(trait.id);
            return 0;
        }

        public virtual int GetTraitValue(string id)
        {
            int val = 0;
            CardTrait stat1 = GetTrait(id);
            CardTrait stat2 = GetOngoingTrait(id);
            if (stat1 != null)
                val += stat1.value;
            if (stat2 != null)
                val += stat2.value;
            return val;
        }

        public bool HasTrait(TraitData trait)
        {
            if (trait != null)
                return HasTrait(trait.id);
            return false;
        }

        public bool HasTrait(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            if (buff_removed_traits.Contains(id))
                return false;                       //被增益属性修改移除（抑制）
            if (buff_added_traits.Contains(id))
                return true;
            return GetTrait(id) != null || GetOngoingTrait(id) != null;
        }

        public List<CardTrait> GetAllTraits()
        {
            List<CardTrait> all_traits = new List<CardTrait>();
            foreach (CardTrait t in traits)
            {
                if (t != null && !buff_removed_traits.Contains(t.id))
                    all_traits.Add(t);
            }
            foreach (CardTrait t in ongoing_traits)
            {
                if (t != null && !buff_removed_traits.Contains(t.id))
                    all_traits.Add(t);
            }
            foreach (string id in buff_added_traits)
            {
                CardTrait exist = GetTrait(id);
                if (exist == null && GetOngoingTrait(id) == null)
                    all_traits.Add(new CardTrait(id, 0));
            }
            return all_traits;
        }
        
        //Alternate names since traits/stats are stored in same var
        public void SetStat(string id, int value) => SetTrait(id, value);
        public void AddStat(string id, int value) => AddTrait(id, value);
        public void AddOngoingStat(string id, int value) => AddOngoingTrait(id, value);
        public void RemoveStat(string id) => RemoveTrait(id);
        public int GetStatValue(TraitData trait) => GetTraitValue(trait);
        public int GetStatValue(string id) => GetTraitValue(id);
        public bool HasStat(TraitData trait) => HasTrait(trait);
        public bool HasStat(string id) => HasTrait(id);
        public List<CardTrait> GetAllStats() => GetAllTraits();

        //------  Status Effects ---------

        public void AddStatus(StatusData status, int value, int duration)
        {
            if (status != null)
                AddStatus(status.effect, value, duration);
        }

        public void AddOngoingStatus(StatusData status, int value)
        {
            if (status != null)
                AddOngoingStatus(status.effect, value);
        }

        public void AddStatus(StatusType type, int value, int duration)
        {
            if (type != StatusType.None)
            {
                CardStatus status = GetStatus(type);
                if (status == null)
                {
                    status = new CardStatus(type, value, duration);
                    this.status.Add(status);
                }
                else
                {
                    status.value += value;
                    status.duration = Mathf.Max(status.duration, duration);
                    status.permanent = status.permanent || duration == 0;
                }
            }
        }

        public void AddOngoingStatus(StatusType type, int value)
        {
            if (type != StatusType.None)
            {
                CardStatus status = GetOngoingStatus(type);
                if (status == null)
                {
                    status = new CardStatus(type, value, 0);
                    ongoing_status.Add(status);
                }
                else
                {
                    status.value += value;
                }
            }
        }

        public void RemoveStatus(StatusType type)
        {
            for (int i = status.Count - 1; i >= 0; i--)
            {
                if (status[i].type == type)
                    status.RemoveAt(i);
            }
        }

        public List<CardStatus> GetAllStatus()
        {
            List<CardStatus> all_status = new List<CardStatus>();
            all_status.AddRange(status);
            all_status.AddRange(ongoing_status);
            return all_status;
        }

        /// <summary>状态集合签名（**零分配**）：把「永久状态 + 持续状态」的类型与数值混成一个 int，
        /// 供每帧刷新的 UI/FX 做"状态没变就不重建"判断（BoardCardFX.Update 每帧对每张卡调用）。
        /// 用加法混合 → 与列表顺序无关，避免同样一组状态因顺序差异被判成"变了"。</summary>
        public int StatusSignature()
        {
            unchecked
            {
                int h = 17;
                if (status != null)
                {
                    h += status.Count * 31;
                    for (int i = 0; i < status.Count; i++)
                        h += (int)status[i].type * 37 + status[i].value * 53;
                }
                if (ongoing_status != null)
                {
                    h += ongoing_status.Count * 41;
                    for (int i = 0; i < ongoing_status.Count; i++)
                        h += (int)ongoing_status[i].type * 43 + ongoing_status[i].value * 59;
                }
                return h;
            }
        }

        public bool HasStatus(StatusType type)
        {
            return GetStatus(type) != null || GetOngoingStatus(type) != null;
        }

        public CardStatus GetStatus(StatusType type)
        {
            foreach (CardStatus status in status)
            {
                if (status.type == type)
                    return status;
            }
            return null;
        }

        public CardStatus GetOngoingStatus(StatusType type)
        {
            foreach (CardStatus status in ongoing_status)
            {
                if (status.type == type)
                    return status;
            }
            return null;
        }

        public virtual int GetStatusValue(StatusType type)
        {
            CardStatus status1 = GetStatus(type);
            CardStatus status2 = GetOngoingStatus(type);
            int v1 = status1 != null ? status1.value : 0;
            int v2 = status2 != null ? status2.value : 0;
            return v1 + v2;
        }

        public virtual void ReduceStatusDurations()
        {
            for (int i = status.Count - 1; i >= 0; i--)
            {
                if (!status[i].permanent)
                {
                    status[i].duration -= 1;
                    if (status[i].duration <= 0)
                        status.RemoveAt(i);
                }
            }
        }

        //----- Abilities ------------

        public void AddAbility(AbilityData ability)
        {
            abilities.Add(ability.id);
			if (abilities_data != null)
				abilities_data.Add(ability);
        }

        public void RemoveAbility(AbilityData ability)
        {
            abilities.Remove(ability.id);
            if (abilities_data != null)
                abilities_data.Remove(ability);
        }

        public void AddOngoingAbility(AbilityData ability)
        {
            if (!abilities_ongoing.Contains(ability.id) && !abilities.Contains(ability.id))
            {
                abilities_ongoing.Add(ability.id);
                if (abilities_data != null)
                    abilities_data.Add(ability);
            }
        }

        public void ClearOngoingAbility()
        {
            if (abilities_data != null)
            {
                for (int i = abilities_data.Count - 1; i >= 0; i--)
                {
                    AbilityData ability = abilities_data[i];
                    if (abilities_ongoing.Contains(ability.id))
                        abilities_data.RemoveAt(i);
                }
            }

            abilities_ongoing.Clear();
        }

        public AbilityData GetAbility(AbilityTrigger trigger)
        {
            foreach (AbilityData iability in GetAbilities())
            {
                if (iability.trigger == trigger)
                    return iability;
            }
            return null;
        }

        public bool HasAbility(AbilityData ability)
        {
            foreach (AbilityData iability in GetAbilities())
            {
                if (iability.id == ability.id)
                    return true;
            }
            return false;
        }

        public bool HasAbility(AbilityTrigger trigger)
        {
            AbilityData iability = GetAbility(trigger);
            if (iability != null)
                return true;
            return false;
        }

        public bool HasAbility(AbilityTrigger trigger, AbilityTarget target)
        {
            foreach (AbilityData iability in GetAbilities())
            {
                if (iability.trigger == trigger && iability.target == target)
                    return true;
            }
            return false;
        }

        public bool HasActiveAbility(Game data, AbilityTrigger trigger)
        {
            AbilityData iability = GetAbility(trigger);
            if (iability != null && CanDoAbilities() && iability.AreTriggerConditionsMet(data, this))
                return true;
            return false;
        }

        public bool AreAbilityConditionsMet(AbilityTrigger ability_trigger, Game data, Card caster, Card triggerer)
        {
            foreach (AbilityData ability in GetAbilities())
            {
                if (ability && ability.trigger == ability_trigger && ability.AreTriggerConditionsMet(data, caster, triggerer))
                    return true;
            }
            return false;
        }

        public List<AbilityData> GetAbilities()
        {
            //Load abilities data, important to do this here since this array will be null after being sent through networking (cant serialize it)
            if (abilities_data == null)
            {
                abilities_data = new List<AbilityData>(abilities.Count + abilities_ongoing.Count);
                for (int i = 0; i < abilities.Count; i++)
                    abilities_data.Add(AbilityData.Get(abilities[i]));
                for (int i = 0; i < abilities_ongoing.Count; i++)
                    abilities_data.Add(AbilityData.Get(abilities_ongoing[i]));
            }

            //Return
            return abilities_data;
        }

        //---- Action Check ---------

        public virtual bool CanAttack(bool skip_cost = false)
        {
            if (HasStatus(StatusType.SummonDisorder) && (!HasStatus(StatusType.Haste)))
            //if (HasStatus(StatusType.Disorder))
                return false;
            if (HasStatus(StatusType.Paralysed))
                return false;
            if (!skip_cost && exhausted)
                return false; //no more action
            return true;
        }

        public virtual bool CanMove(bool skip_cost = false)
        {
            if (HasStatus(StatusType.SummonDisorder) && (!HasStatus(StatusType.Haste)))
            //if (HasStatus(StatusType.Disorder))
                return false;
            if (!skip_cost && exhausted)
               return false; //no more action
            return false; 
        }

        public virtual bool CanDoActivatedAbilities()
        {
            if (HasStatus(StatusType.SummonDisorder) && (!HasStatus(StatusType.Haste)))
            //if (HasStatus(StatusType.Disorder))
                return false;
            if (HasStatus(StatusType.Silenced))
                return false;

            return true;
        }

        public virtual bool CanDoAbilities()
        {
            if (HasStatus(StatusType.Silenced))
                return false;
            return true;
        }

        public virtual bool CanDoAnyAction()
        {
            return CanAttack() || CanMove() || CanDoActivatedAbilities();
        }

        //----------------

        public CardData CardData 
        { 
            get { 
                if(data == null || data.id != card_id)
                    data = CardData.Get(card_id); //Optimization, store for future use
                return data;
            } 
        }

        public VariantData VariantData
        {
            get
            {
                if (vdata == null || vdata.id != variant_id)
                    vdata = VariantData.Get(variant_id); //Optimization, store for future use
                return vdata;
            }
        }

        public CardData Data => CardData; //Alternate name

        public int Hash
        {
            get {
                if (hash == 0)
                    hash = Mathf.Abs(uid.GetHashCode()); //Optimization, store for future use
                return hash;
            }
        }

        public static Card Create(CardData icard, VariantData ivariant, Player player)
        {
            return Create(icard, ivariant, player, GameTool.GenerateRandomID(11, 15));
        }

        public static Card Create(CardData icard, VariantData ivariant, Player player, string uid)
        {
            if (icard == null)
            {
                //卡牌定义缺失：以前在 icard.id 处 NRE（同样发生在服务端开局流程 → 整局开不起来）
                Debug.LogError("[Card] Create 收到空 CardData（该卡 id 未注册/资源丢失）→ 返回 null，调用方需判空");
                return null;
            }
            Card card = new Card(icard.id, uid, player.player_id);
            card.SetCard(icard, ivariant);
            player.cards_all[card.uid] = card;
            return card;
        }

        public static Card CloneNew(Card source)
        {
            Card card = new Card(source.card_id, source.uid, source.player_id);
            Clone(source, card);
            return card;
        }

        //Clone all card variables into another var, used mostly by the AI when building a prediction tree
        public static void Clone(Card source, Card dest)
        {
            dest.card_id = source.card_id;
            dest.uid = source.uid;
            dest.player_id = source.player_id;

            dest.variant_id = source.variant_id;
            dest.slot = source.slot;
            dest.exhausted = source.exhausted;
            dest.damage = source.damage;

            dest.attack = source.attack;
            dest.hp = source.hp;
            dest.mana = source.mana;

            dest.mana_ongoing = source.mana_ongoing;
            dest.attack_ongoing = source.attack_ongoing;
            dest.hp_ongoing = source.hp_ongoing;

            dest.equipped_uid = source.equipped_uid;

            CardTrait.CloneList(source.traits, dest.traits);
            CardTrait.CloneList(source.ongoing_traits, dest.ongoing_traits);
            //关键词与增益属性修改的运行时可变量：AI 预测树必须一致，否则预测会算错
            dest.keywords = new List<string>(source.keywords);
            dest.buff_added_traits = new List<string>(source.buff_added_traits);
            dest.buff_removed_traits = new List<string>(source.buff_removed_traits);
            dest.buff_added_keywords = new List<string>(source.buff_added_keywords);
            dest.buff_removed_keywords = new List<string>(source.buff_removed_keywords);
            dest.removed_keywords = new List<string>(source.removed_keywords);   //持久移除表也要一致，否则 AI 预测会算错
            //被动效果「已生效分组」同样必须一致：否则预测副本会重复执行 生效/失效 线（两侧状态错位）
            dest.passive_groups = source.passive_groups != null ? new List<string>(source.passive_groups) : new List<string>();
            dest.custom_props = new List<BuffProp>();                            //自定义属性当前值同样要一致
            if (source.custom_props != null)
            {
                foreach (BuffProp cp in source.custom_props)
                {
                    if (cp != null)
                        dest.custom_props.Add(new BuffProp { key = cp.key, value = cp.value });
                }
            }
            CardStatus.CloneList(source.status, dest.status);
            CardStatus.CloneList(source.ongoing_status, dest.ongoing_status);
            GameTool.CloneList(source.abilities, dest.abilities); 
            GameTool.CloneList(source.abilities_ongoing, dest.abilities_ongoing); 
            GameTool.CloneListRefNull(source.abilities_data, ref dest.abilities_data); //No need to deep copy since AbilityData doesn't change dynamically, its just a reference
        }

        //Clone a var that could be null
        public static void CloneNull(Card source, ref Card dest)
        {
            //Source is null
            if (source == null)
            {
                dest = null;
                return;
            }

            //Dest is null
            if (dest == null)
            {
                dest = CloneNew(source);
                return;
            }

            //Both arent null, just clone
            Clone(source, dest);
        }

        //Clone dictionary completely
        public static void CloneDict(Dictionary<string, Card> source, Dictionary<string, Card> dest)
        {
            foreach (KeyValuePair<string, Card> pair in source)
            {
                bool valid = dest.TryGetValue(pair.Key, out Card val);
                if (valid)
                    Clone(pair.Value, val);
                else
                    dest[pair.Key] = CloneNew(pair.Value);
            }
        }

        //Clone list by keeping references from ref_dict
        public static void CloneListRef(Dictionary<string, Card> ref_dict, List<Card> source, List<Card> dest)
        {
            for (int i = 0; i < source.Count; i++)
            {
                Card scard = source[i];
                bool valid = ref_dict.TryGetValue(scard.uid, out Card rcard);
                if (valid)
                {
                    if (i < dest.Count)
                        dest[i] = rcard;
                    else
                        dest.Add(rcard);
                }
            }

            if(dest.Count > source.Count)
                dest.RemoveRange(source.Count, dest.Count - source.Count);
        }
    }

    [System.Serializable]
    public class CardStatus
    {
        public StatusType type;
        public int value;
        public int duration = 1;
        public bool permanent = true;

        [System.NonSerialized]
        private StatusData data = null;

        public CardStatus() { }

        public CardStatus(StatusType type, int value, int duration)
        {
            this.type = type;
            this.value = value;
            this.duration = duration;
            this.permanent = (duration == 0);
        }

        public StatusData StatusData { 
            get
            {
                if (data == null || data.effect != type)
                    data = StatusData.Get(type);
                return data;
            }
        }

        public StatusData Data => StatusData; //Alternate name

        public static CardStatus CloneNew(CardStatus copy)
        {
            CardStatus status = new CardStatus(copy.type, copy.value, copy.duration);
            status.permanent = copy.permanent;
            return status;
        }

        public static void Clone(CardStatus source, CardStatus dest)
        {
            dest.type = source.type;
            dest.value = source.value;
            dest.duration = source.duration;
            dest.permanent = source.permanent;
        }

        public static void CloneList(List<CardStatus> source, List<CardStatus> dest)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (i < dest.Count)
                    Clone(source[i], dest[i]);
                else
                    dest.Add(CloneNew(source[i]));
            }

            if (dest.Count > source.Count)
                dest.RemoveRange(source.Count, dest.Count - source.Count);
        }
    }

    [System.Serializable]
    public class CardTrait
    {
        public string id;
        public int value;

        [System.NonSerialized]
        private TraitData data = null;

        public CardTrait(string id, int value)
        {
            this.id = id;
            this.value = value;
        }

        public CardTrait(TraitData trait, int value)
        {
            this.id = trait.id;
            this.value = value;
        }

        public TraitData TraitData
        {
            get
            {
                if (data == null || data.id != id)
                    data = TraitData.Get(id);
                return data;
            }
        }

        public TraitData Data => TraitData; //Alternate name


        public static CardTrait CloneNew(CardTrait copy)
        {
            CardTrait status = new CardTrait(copy.id, copy.value);
            return status;
        }

        public static void Clone(CardTrait source, CardTrait dest)
        {
            dest.id = source.id;
            dest.value = source.value;
        }

        public static void CloneList(List<CardTrait> source, List<CardTrait> dest)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (i < dest.Count)
                    Clone(source[i], dest[i]);
                else
                    dest.Add(CloneNew(source[i]));
            }

            if (dest.Count > source.Count)
                dest.RemoveRange(source.Count, dest.Count - source.Count);
        }
    }
}
