using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TcgEngine;

namespace TcgEngine.EditorTool
{
    public class ResourceBrowserWindow : EditorWindow
    {
        private string currentCategory = "";
        private List<Object> assets = new List<Object>();
        private List<Object> filteredAssets = new List<Object>();
        private Vector2 leftScrollPosition;
        private Vector2 rightScrollPosition;

        private string searchQuery = "";
        private string newCardId = "";
        private string newCardTitle = "";
        private Object selectedAsset = null;
        private Editor selectedEditor = null;
        private Object lastDrawnAsset = null;

        private CardType filterCardType = CardType.None;
        private int filterRarityIndex = 0;
        private int filterTeamIndex = 0;
        private int filterPackIndex = 0;
        private int filterDeckBuildingIndex = 0;
        private string[] deckBuildingOptions = new string[] { "All", "Deck Building", "Not Deck Building" };

        private AbilityTrigger filterAbilityTrigger = AbilityTrigger.None;
        private AbilityTarget filterAbilityTarget = AbilityTarget.None;

        private int sortByIndex = 0;
        private bool sortAscending = true;
        private static string[] sortOptions = new string[] { "Name", "Type", "Rarity", "Team", "Mana", "Attack", "HP" };

        private List<RarityData> rarities = new List<RarityData>();
        private List<TeamData> teams = new List<TeamData>();
        private List<PackData> packs = new List<PackData>();
        private string[] rarityNames;
        private string[] teamNames;
        private string[] packNames;

        private static Dictionary<string, string> categoryPaths = new Dictionary<string, string>()
        {
            { "Cards", "Cards" },
            { "Abilities", "Abilities" },
            { "Effects", "Effects" },
            { "Status", "Status" },
            { "Traits", "Traits" },
            { "Teams", "Teams" },
            { "Decks", "Decks" },
            { "Packs", "Packs" },
            { "Levels", "Levels" },
            { "Rarities", "Rarities" },
            { "Variants", "Variants" },
            { "Avatars", "Avatars" },
            { "Cardbacks", "Cardbacks" },
            { "Conditions", "Conditions" },
        };

        [MenuItem("Resource/Cards")]
        public static void OpenCards() => OpenCategory("Cards");

        [MenuItem("Resource/Abilities")]
        public static void OpenAbilities() => OpenCategory("Abilities");

        [MenuItem("Resource/Effects")]
        public static void OpenEffects() => OpenCategory("Effects");

        [MenuItem("Resource/Status")]
        public static void OpenStatus() => OpenCategory("Status");

        [MenuItem("Resource/Traits")]
        public static void OpenTraits() => OpenCategory("Traits");

        [MenuItem("Resource/Teams")]
        public static void OpenTeams() => OpenCategory("Teams");

        [MenuItem("Resource/Decks")]
        public static void OpenDecks() => OpenCategory("Decks");

        [MenuItem("Resource/Packs")]
        public static void OpenPacks() => OpenCategory("Packs");

        [MenuItem("Resource/Levels")]
        public static void OpenLevels() => OpenCategory("Levels");

        [MenuItem("Resource/Rarities")]
        public static void OpenRarities() => OpenCategory("Rarities");

        [MenuItem("Resource/Avatars")]
        public static void OpenAvatars() => OpenCategory("Avatars");

        [MenuItem("Resource/Cardbacks")]
        public static void OpenCardbacks() => OpenCategory("Cardbacks");

        [MenuItem("Resource/Conditions")]
        public static void OpenConditions() => OpenCategory("Conditions");

        private static void OpenCategory(string category)
        {
            ResourceBrowserWindow window = CreateWindow<ResourceBrowserWindow>();
            window.titleContent = new GUIContent(category + " Browser");
            window.minSize = new Vector2(1100, 500);
            window.currentCategory = category;
            window.LoadAssets();
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(currentCategory))
            {
                LoadAssets();
            }
        }

        private void LoadAssets()
        {
            assets.Clear();
            filteredAssets.Clear();
            selectedAsset = null;
            selectedEditor = null;

            LoadFilterData();

            if (categoryPaths.ContainsKey(currentCategory))
            {
                LoadAssetsFromPath(categoryPaths[currentCategory]);
            }

            ApplyFilter();
        }

        private void LoadFilterData()
        {
            rarities = Resources.LoadAll<RarityData>("Rarities").ToList();
            teams = Resources.LoadAll<TeamData>("Teams").ToList();
            packs = Resources.LoadAll<PackData>("Packs").ToList();

            Debug.Log($"Loaded {packs.Count} packs from Resources/Packs");

            rarityNames = new string[rarities.Count + 1];
            rarityNames[0] = "All Rarities";
            for (int i = 0; i < rarities.Count; i++)
            {
                rarityNames[i + 1] = rarities[i].title;
            }

            teamNames = new string[teams.Count + 1];
            teamNames[0] = "All Teams";
            for (int i = 0; i < teams.Count; i++)
            {
                teamNames[i + 1] = teams[i].title;
            }

            packNames = new string[packs.Count + 1];
            packNames[0] = "All Packs";
            for (int i = 0; i < packs.Count; i++)
            {
                Debug.Log($"Pack {i}: {packs[i].id} - {packs[i].title}");
                packNames[i + 1] = packs[i].title;
            }
        }

        private void LoadAssetsFromPath(string path)
        {
            string fullPath = "Assets/TcgEngine/Resources/" + path;
            if (Directory.Exists(fullPath))
            {
                string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { fullPath });
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (asset != null && !assets.Contains(asset))
                    {
                        assets.Add(asset);
                    }
                }
            }
        }

        private void ApplyFilter()
        {
            filteredAssets = assets.Where(asset =>
            {
                bool matches = true;

                CardData card = asset as CardData;
                if (card != null)
                {
                    if (filterCardType != CardType.None && card.type != filterCardType)
                        matches = false;

                    if (filterRarityIndex > 0 && card.rarity != rarities[filterRarityIndex - 1])
                        matches = false;

                    if (filterTeamIndex > 0 && card.team != teams[filterTeamIndex - 1])
                        matches = false;

                    if (filterPackIndex > 0 && !CardHasPack(card, packs[filterPackIndex - 1]))
                        matches = false;

                    if (filterDeckBuildingIndex == 1 && !card.deckbuilding)
                        matches = false;

                    if (filterDeckBuildingIndex == 2 && card.deckbuilding)
                        matches = false;
                }

                AbilityData ability = asset as AbilityData;
                if (ability != null)
                {
                    if (filterAbilityTrigger != AbilityTrigger.None && ability.trigger != filterAbilityTrigger)
                        matches = false;

                    if (filterAbilityTarget != AbilityTarget.None && ability.target != filterAbilityTarget)
                        matches = false;
                }

                if (!string.IsNullOrEmpty(searchQuery))
                {
                    if (!asset.name.ToLower().Contains(searchQuery.ToLower()))
                    {
                        if (card != null && card.title != null)
                        {
                            if (!card.title.ToLower().Contains(searchQuery.ToLower()))
                                matches = false;
                        }
                        else if (ability != null && ability.title != null)
                        {
                            if (!ability.title.ToLower().Contains(searchQuery.ToLower()))
                                matches = false;
                        }
                        else
                        {
                            matches = false;
                        }
                    }
                }

                return matches;
            }).ToList();

            ApplySort();

            if (selectedAsset != null && !filteredAssets.Contains(selectedAsset))
            {
                selectedAsset = null;
                selectedEditor = null;
            }
        }

        private void ApplySort()
        {
            if (currentCategory == "Cards")
            {
                switch (sortByIndex)
                {
                    case 0:
                        filteredAssets = sortAscending 
                            ? filteredAssets.OrderBy(a => a.name).ToList()
                            : filteredAssets.OrderByDescending(a => a.name).ToList();
                        break;
                    case 1:
                        filteredAssets = sortAscending
                            ? filteredAssets.OrderBy(a => (a as CardData)?.type ?? CardType.None).ToList()
                            : filteredAssets.OrderByDescending(a => (a as CardData)?.type ?? CardType.None).ToList();
                        break;
                    case 2:
                        filteredAssets = sortAscending
                            ? filteredAssets.OrderBy(a => (a as CardData)?.rarity?.id ?? "").ToList()
                            : filteredAssets.OrderByDescending(a => (a as CardData)?.rarity?.id ?? "").ToList();
                        break;
                    case 3:
                        filteredAssets = sortAscending
                            ? filteredAssets.OrderBy(a => (a as CardData)?.team?.id ?? "").ToList()
                            : filteredAssets.OrderByDescending(a => (a as CardData)?.team?.id ?? "").ToList();
                        break;
                    case 4:
                        filteredAssets = sortAscending
                            ? filteredAssets.OrderBy(a => (a as CardData)?.mana ?? 0).ToList()
                            : filteredAssets.OrderByDescending(a => (a as CardData)?.mana ?? 0).ToList();
                        break;
                    case 5:
                        filteredAssets = sortAscending
                            ? filteredAssets.OrderBy(a => (a as CardData)?.attack ?? 0).ToList()
                            : filteredAssets.OrderByDescending(a => (a as CardData)?.attack ?? 0).ToList();
                        break;
                    case 6:
                        filteredAssets = sortAscending
                            ? filteredAssets.OrderBy(a => (a as CardData)?.hp ?? 0).ToList()
                            : filteredAssets.OrderByDescending(a => (a as CardData)?.hp ?? 0).ToList();
                        break;
                }
            }
            else
            {
                filteredAssets = sortAscending 
                    ? filteredAssets.OrderBy(a => a.name).ToList()
                    : filteredAssets.OrderByDescending(a => a.name).ToList();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private string GetAssetDisplayName(Object asset)
        {
            CardData card = asset as CardData;
            if (card != null)
            {
                string stats = "";
                if (card.type == CardType.Character)
                    stats = $"({card.mana}/{card.attack}/{card.hp})";
                else if (card.type == CardType.Spell || card.type == CardType.Secret)
                    stats = $"({card.mana})";
                else if (card.type == CardType.Equipment)
                    stats = $"({card.mana}/{card.attack}/{card.hp})";
                else if (card.type == CardType.Artifact)
                    stats = $"({card.mana})";
                else if (card.type == CardType.Hero)
                    stats = $"({card.hp} HP)";
                return $"{card.id} | {card.title} | [{card.type}]{stats}";
            }

            AbilityData ability = asset as AbilityData;
            if (ability != null)
            {
                return $"{ability.id} | {ability.title} | [{ability.trigger}]";
            }

            EffectData effect = asset as EffectData;
            if (effect != null)
                return $"{asset.name} | [Effect]";

            StatusData status = asset as StatusData;
            if (status != null)
                return $"{asset.name} | {status.title} | [Status]";

            TraitData trait = asset as TraitData;
            if (trait != null)
                return $"{trait.id} | {trait.title} | [Trait]";

            TeamData team = asset as TeamData;
            if (team != null)
                return $"{team.id} | {team.title} | [Team]";

            DeckData deck = asset as DeckData;
            if (deck != null)
                return $"{deck.id} | {deck.title} | [Deck]";

            PackData pack = asset as PackData;
            if (pack != null)
                return $"{pack.id} | {pack.title} | [Pack]";

            LevelData level = asset as LevelData;
            if (level != null)
                return $"{level.id} | {level.title} | [Level {level.level}]";

            RarityData rarity = asset as RarityData;
            if (rarity != null)
                return $"{rarity.id} | {rarity.title} | [Rarity]";

            AvatarData avatar = asset as AvatarData;
            if (avatar != null)
                return $"{avatar.id} | [Avatar]";

            CardbackData cardback = asset as CardbackData;
            if (cardback != null)
                return $"{cardback.id} | [Cardback]";

            ConditionData condition = asset as ConditionData;
            if (condition != null)
                return $"{asset.name} | [Condition]";

            return asset.name;
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(300));

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"[{currentCategory}]", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
                LoadAssets();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            searchQuery = EditorGUILayout.TextField("Search", searchQuery);

            if (currentCategory == "Cards")
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Sort", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                sortByIndex = EditorGUILayout.Popup("Sort by", sortByIndex, sortOptions);
                if (GUILayout.Button(sortAscending ? "↑" : "↓", GUILayout.Width(50)))
                    sortAscending = !sortAscending;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

                filterCardType = (CardType)EditorGUILayout.EnumPopup("Type", filterCardType);

                if (rarityNames != null && rarityNames.Length > 0)
                    filterRarityIndex = EditorGUILayout.Popup("Rarity", filterRarityIndex, rarityNames);

                if (teamNames != null && teamNames.Length > 0)
                    filterTeamIndex = EditorGUILayout.Popup("Team", filterTeamIndex, teamNames);

                if (packNames != null && packNames.Length > 1)
                    filterPackIndex = EditorGUILayout.Popup("Pack", filterPackIndex, packNames);

                filterDeckBuildingIndex = EditorGUILayout.Popup("Deck Building", filterDeckBuildingIndex, deckBuildingOptions);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear Filters", GUILayout.Height(20)))
                {
                    filterCardType = CardType.None;
                    filterRarityIndex = 0;
                    filterTeamIndex = 0;
                    filterPackIndex = 0;
                    filterDeckBuildingIndex = 0;
                    searchQuery = "";
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("New Card Settings", EditorStyles.boldLabel);
                newCardId = EditorGUILayout.TextField("ID", newCardId);
                newCardTitle = EditorGUILayout.TextField("Title", newCardTitle);

                EditorGUI.BeginDisabledGroup(filterTeamIndex <= 0 || string.IsNullOrEmpty(newCardId));
                if (GUILayout.Button("New Card", GUILayout.Height(25)))
                {
                    CreateNewCard();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(10);
                if (GUILayout.Button("Export to CSV", GUILayout.Height(30)))
                {
                    EditorApplication.delayCall += () => ExportCardsToCSV();
                }
            }

            if (currentCategory == "Abilities")
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

                filterAbilityTrigger = (AbilityTrigger)EditorGUILayout.EnumPopup("Trigger", filterAbilityTrigger);
                filterAbilityTarget = (AbilityTarget)EditorGUILayout.EnumPopup("Target", filterAbilityTarget);

                if (GUILayout.Button("Clear Filters", GUILayout.Height(20)))
                {
                    filterAbilityTrigger = AbilityTrigger.None;
                    filterAbilityTarget = AbilityTarget.None;
                    searchQuery = "";
                }
            }

            if (EditorGUI.EndChangeCheck())
                ApplyFilter();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"Items: {filteredAssets.Count}", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);

            leftScrollPosition = EditorGUILayout.BeginScrollView(leftScrollPosition);

            for (int i = 0; i < filteredAssets.Count; i++)
            {
                Object asset = filteredAssets[i];
                bool isSelected = (asset == selectedAsset);

                Color originalColor = GUI.backgroundColor;
                if (isSelected)
                    GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                string displayName = GetAssetDisplayName(asset);
                GUIContent content = new GUIContent(displayName, AssetDatabase.GetAssetPath(asset));

                if (GUILayout.Toggle(isSelected, content, EditorStyles.radioButton, GUILayout.Width(270)))
                {
                    if (!isSelected)
                    {
                        selectedAsset = asset;
                        selectedEditor = null;
                        lastDrawnAsset = null;
                        Selection.activeObject = asset;
                    }
                }

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = originalColor;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (selectedAsset != null)
            {
                EditorGUILayout.Space(10);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(selectedAsset.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Type: {selectedAsset.GetType().Name}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);

                rightScrollPosition = EditorGUILayout.BeginScrollView(rightScrollPosition);

                CardData card = selectedAsset as CardData;
                bool needsNewEditor = selectedAsset != lastDrawnAsset;

                if (card != null)
                {
                    if (needsNewEditor)
                    {
                        if (selectedEditor != null)
                        {
                            DestroyImmediate(selectedEditor);
                        }
                        selectedEditor = Editor.CreateEditor(card);
                    }
                    DrawCardDataTwoColumns(card);
                }
                else
                {
                    if (needsNewEditor)
                    {
                        if (selectedEditor != null)
                        {
                            DestroyImmediate(selectedEditor);
                        }
                        selectedEditor = Editor.CreateEditor(selectedAsset);
                    }

                    if (selectedEditor != null)
                        selectedEditor.OnInspectorGUI();
                }

                lastDrawnAsset = selectedAsset;

                EditorGUILayout.Space(20);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Open in Inspector", GUILayout.Height(30)))
                {
                    Selection.activeObject = selectedAsset;
                    EditorGUIUtility.PingObject(selectedAsset);
                }
                if (GUILayout.Button("Delete", GUILayout.Height(30), GUILayout.Width(80)))
                {
                    if (EditorUtility.DisplayDialog("Delete Asset",
                        $"Are you sure you want to delete '{selectedAsset.name}'?",
                        "Delete", "Cancel"))
                    {
                        string assetPath = AssetDatabase.GetAssetPath(selectedAsset);
                        AssetDatabase.DeleteAsset(assetPath);
                        LoadAssets();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("Select an item from the list to view and edit details.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCardDataTwoColumns(CardData card)
        {
            SerializedObject so = null;
            if (selectedEditor != null)
            {
                so = selectedEditor.serializedObject;
                so.Update();

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                
                DrawSerializedProperty(so, "id");
                DrawSerializedProperty(so, "title");
                DrawSerializedProperty(so, "art_full");
                DrawSerializedProperty(so, "art_board");

                EditorGUILayout.Space(5);
                DrawSerializedProperty(so, "type");
                DrawSerializedProperty(so, "team");
                DrawSerializedProperty(so, "rarity");
                DrawSerializedProperty(so, "mana");
                DrawSerializedProperty(so, "attack");
                DrawSerializedProperty(so, "hp");

                EditorGUILayout.Space(5);
                DrawSerializedProperty(so, "traits");
                DrawSerializedProperty(so, "stats");

                EditorGUILayout.Space(5);
                DrawSerializedProperty(so, "text");
                DrawSerializedProperty(so, "desc");

                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                
                DrawSerializedProperty(so, "abilities");

                EditorGUILayout.Space(5);
                DrawSerializedProperty(so, "spawn_fx");
                DrawSerializedProperty(so, "death_fx");
                DrawSerializedProperty(so, "attack_fx");
                DrawSerializedProperty(so, "damage_fx");
                DrawSerializedProperty(so, "idle_fx");

                EditorGUILayout.Space(5);
                DrawSerializedProperty(so, "spawn_audio");
                DrawSerializedProperty(so, "death_audio");
                DrawSerializedProperty(so, "attack_audio");
                DrawSerializedProperty(so, "damage_audio");

                EditorGUILayout.Space(5);
                DrawSerializedProperty(so, "deckbuilding");
                DrawSerializedProperty(so, "cost");
                DrawSerializedProperty(so, "packs");

                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();

                so.ApplyModifiedProperties();
            }
        }

        private void DrawSerializedProperty(SerializedObject so, string propertyName)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        private void OnDestroy()
        {
            if (selectedEditor != null)
                DestroyImmediate(selectedEditor);
        }

        private void CreateNewCard()
        {
            if (filterTeamIndex <= 0 || string.IsNullOrEmpty(newCardId))
            {
                EditorUtility.DisplayDialog("Warning", "Please enter an ID and select a Team first.", "OK");
                return;
            }

            TeamData selectedTeam = teams[filterTeamIndex - 1];
            string teamId = selectedTeam.id;
            string cardId = newCardId;
            string cardTitle = string.IsNullOrEmpty(newCardTitle) ? newCardId : newCardTitle;

            string resourcesPath = "Assets/TcgEngine/Resources";
            string cardsPath = resourcesPath + "/Cards";
            string teamPath = cardsPath + "/" + teamId;

            if (!AssetDatabase.IsValidFolder(resourcesPath))
                AssetDatabase.CreateFolder("Assets/TcgEngine", "Resources");
            if (!AssetDatabase.IsValidFolder(cardsPath))
                AssetDatabase.CreateFolder(resourcesPath, "Cards");
            if (!AssetDatabase.IsValidFolder(teamPath))
                AssetDatabase.CreateFolder(cardsPath, teamId);

            CardData newCard = ScriptableObject.CreateInstance<CardData>();
            newCard.id = cardId;
            newCard.title = cardTitle;
            newCard.team = selectedTeam;
            
            if (filterCardType != CardType.None)
                newCard.type = filterCardType;
            else
                newCard.type = CardType.Character;
                
            if (filterRarityIndex > 0)
                newCard.rarity = rarities[filterRarityIndex - 1];

            if (filterPackIndex > 0)
            {
                newCard.packs = new PackData[] { packs[filterPackIndex - 1] };
            }

            string assetPath = $"{teamPath}/{cardId}.asset";
            AssetDatabase.CreateAsset(newCard, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LoadAssets();
            
            selectedAsset = newCard;
            selectedEditor = null;
            Selection.activeObject = newCard;

            newCardId = "";
            newCardTitle = "";
        }

        private void ExportCardsToCSV()
        {
            string path = EditorUtility.SaveFilePanel("Export Cards to CSV", "", "cards.csv", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("ID,Title,Type,Team,Rarity,Mana,Attack,HP,Text,Desc,Packs");

            foreach (Object asset in filteredAssets)
            {
                CardData card = asset as CardData;
                if (card != null)
                {
                    string id = EscapeCSV(card.id);
                    string title = EscapeCSV(card.title);
                    string type = card.type.ToString();
                    string team = card.team != null ? EscapeCSV(card.team.title) : "";
                    string rarity = card.rarity != null ? EscapeCSV(card.rarity.title) : "";
                    string mana = card.mana.ToString();
                    string attack = card.attack.ToString();
                    string hp = card.hp.ToString();
                    string text = EscapeCSV(card.text);
                    string desc = EscapeCSV(card.desc);
                    string packs = GetCardPacksCSV(card);

                    sb.AppendLine($"{id},{title},{type},{team},{rarity},{mana},{attack},{hp},{text},{desc},{packs}");
                }
            }

            System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            EditorUtility.DisplayDialog("Export Complete", $"Exported {filteredAssets.Count} cards to:\n{path}", "OK");
        }

        private string GetCardPacksCSV(CardData card)
        {
            if (card == null || card.packs == null || card.packs.Length == 0)
                return "";

            List<string> packNames = new List<string>();
            foreach (PackData pack in card.packs)
            {
                if (pack != null)
                    packNames.Add(EscapeCSV(pack.title));
            }
            return string.Join(";", packNames);
        }

        private string EscapeCSV(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private bool CardHasPack(CardData card, PackData pack)
        {
            if (card == null || card.packs == null || pack == null)
                return false;

            foreach (PackData cardPack in card.packs)
            {
                if (cardPack == pack)
                    return true;
            }
            return false;
        }
    }
}