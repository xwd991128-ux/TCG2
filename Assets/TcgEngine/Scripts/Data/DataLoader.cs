using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Client;
using TcgEngine.Workshop;

namespace TcgEngine
{

    /// <summary>
    /// This script initiates loading all the game data
    /// </summary>

    public class DataLoader : MonoBehaviour
    {
        public GameplayData data;
        public AssetData assets;

        private HashSet<string> card_ids = new HashSet<string>();
        private HashSet<string> ability_ids = new HashSet<string>();
        private HashSet<string> deck_ids = new HashSet<string>();

        private static DataLoader instance;

        void Awake()
        {
            instance = this;
            LoadData();
        }

        public void LoadData()
        {
            //To make loading faster, add a path inside each Load() function, relative to Resources folder
            //For example CardData.Load("Cards");  to only load data inside the Resources/Cards folder
            CardData.Load();
            TeamData.Load();
            RarityData.Load();
            TraitData.Load();
            KeywordData.Load();
            VariantData.Load();
            PackData.Load();
            LevelData.Load();
            DeckData.Load();
            AbilityData.Load();
            StatusData.Load();
            AvatarData.Load();
            CardbackData.Load();
            RewardData.Load();

            CheckCardData();
            CheckAbilityData();
            CheckDeckData();
            CheckVariantData();

            //加载本地自定义卡池（创意工坊下载/玩家导入的 JSON 卡包）
            CardPoolIO.LoadCustomPools();

            //加载本地自定义增益池（规则编辑器「增益」界面设计，Workshop/buffs.json）
            BuffPoolIO.LoadAll();

            //加载战斗界面自定义按钮配置（按钮编辑器设计，Workshop/buttons.json）
            BattleButtonIO.LoadAll();
        }

        //Make sure the data is valid
        private void CheckCardData()
        {
            List<string> null_trait_cards = new List<string>();   //traits 里有空槽的卡（收集后统一报 + 就地剔除）
            card_ids.Clear();
            foreach (CardData card in CardData.GetAll())
            {
                if (string.IsNullOrEmpty(card.id))
                    Debug.LogError(card.name + " id is empty");
                if (card_ids.Contains(card.id))
                    Debug.LogError("Dupplicate Card ID: " + card.id);

                if (card.team == null)
                    Debug.LogError(card.id + " team is null");
                if (card.rarity == null)
                    Debug.LogError(card.id + " rarity is null");

                //★ 种族数组里的空槽（该槽引用的 TraitData 已被删除/未填）：
                //  旧实现在这里逐个 LogError → 本工程有 17 张卡命中，每次进场景刷 17 条；
                //  而空槽会一路传到 CardUI / 规则图（各处虽已判空，但留着仍是隐患）。
                //  改为：收集 → 统一报一条 → **就地剔除空槽**（只改本局内存，不动资产文件）。
                if (card.traits != null)
                {
                    int null_count = 0;
                    foreach (TraitData trait in card.traits)
                    {
                        if (trait == null)
                            null_count++;
                    }
                    if (null_count > 0)
                    {
                        null_trait_cards.Add(card.id + "(" + null_count + ")");
                        List<TraitData> kept = new List<TraitData>(card.traits.Length - null_count);
                        foreach (TraitData trait in card.traits)
                        {
                            if (trait != null)
                                kept.Add(trait);
                        }
                        card.traits = kept.ToArray();
                    }
                }

                if (card.stats != null)
                {
                    foreach (TraitStat stat in card.stats)
                    {
                        if (stat.trait == null)
                            Debug.LogError(card.id + " has null stat trait");
                    }
                }

                foreach (AbilityData ability in card.abilities)
                {
                    if(ability == null)
                        Debug.LogError(card.id + " has null ability");
                }

                card_ids.Add(card.id);
            }

            //空槽统一报一条（原来是逐张 LogError）；根治办法是在编辑器里把那些卡的 Traits 空槽清掉并保存资产
            if (null_trait_cards.Count > 0)
                Debug.LogError("[数据检查] " + null_trait_cards.Count + " 张卡的 traits 有空槽（引用了已删除/未填的 TraitData），"
                    + "本局已在内存中剔除（未改动资产文件）：" + string.Join("、", null_trait_cards.ToArray()));
        }

        //Make sure the data is valid
        private void CheckAbilityData()
        {
            ability_ids.Clear();
            foreach (AbilityData ability in AbilityData.GetAll())
            {
                if (string.IsNullOrEmpty(ability.id))
                    Debug.LogError(ability.name + " id is empty");
                if (ability_ids.Contains(ability.id))
                    Debug.LogError("Dupplicate Ability ID: " + ability.id);

                foreach (AbilityData chain in ability.chain_abilities)
                {
                    if (chain == null)
                        Debug.LogError(ability.id + " has null chain ability");
                }

                ability_ids.Add(ability.id);
            }
        }

        //Make sure the data is valid
        private void CheckDeckData()
        {
            GameplayData gdata = GameplayData.Get();
            CheckDeckArray(gdata.ai_decks);
            CheckDeckArray(gdata.free_decks);
            CheckDeckArray(gdata.starter_decks);

            if(gdata.test_deck == null || gdata.test_deck_ai == null)
                Debug.Log("Deck is null in Resources/GameplayData");

            deck_ids.Clear();
            foreach (DeckData deck in DeckData.GetAll())
            {
                if (string.IsNullOrEmpty(deck.id))
                    Debug.LogError(deck.name + " id is empty");
                if (deck_ids.Contains(deck.id))
                    Debug.LogError("Dupplicate Deck ID: " + deck.id);

                foreach (CardData card in deck.cards)
                {
                    if (card == null)
                        Debug.LogError(deck.id + " has null card");
                }

                deck_ids.Add(deck.id);
            }
        }

        private void CheckDeckArray(DeckData[] decks)
        {
            foreach (DeckData deck in decks)
            {
                if (deck == null)
                    Debug.Log("Deck is null in Resources/GameplayData");
            }
        }

        private void CheckVariantData()
        {
            VariantData dvariant = VariantData.GetDefault();
            if(dvariant == null)
                Debug.LogError("No default variant data found, make sure you have a default VariantData");
        }

        public static DataLoader Get()
        {
            return instance;
        }
    }
}