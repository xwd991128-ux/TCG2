using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;
using TcgEngine.Audio;   //BgmManager / BgmKeys（场景BGM配置）

namespace TcgEngine.UI
{
    /// <summary>
    /// Main script for the main menu scene
    /// </summary>

    public class MainMenu : MonoBehaviour
    {
        public AudioClip music;
        public AudioClip ambience;

        [Header("Player UI")]
        public Text username_txt;
        public Text credits_txt;
        public AvatarUI avatar;
        public GameObject loader;

        [Header("UI")]
        public Text version_text;
        public DeckSelector deck_selector;
        public DeckDisplay deck_preview;

        private bool starting = false;
        private float starting_time = 0f;    //「进入对局」锁的起始时间（用于超时自愈复位）

        private static MainMenu instance;

        void Awake()
        {
            instance = this;

            //Set default settings
            Application.targetFrameRate = 120;
            GameClient.game_settings = GameSettings.Default;

            //回到大厅 = 局域网会话结束（清掉断线兜底守卫；不在局域网会话里时是空操作）
            GameClientLAN.EndSession();
        }

        private void Start()
        {
            BlackPanel.Get().Show(true);
            //主菜单 BGM：优先用「场景BGM配置」里 main_menu 配的曲；未配置则回退到本组件 Inspector 上挂的 music（迁移兼容）
            BgmManager.PlayFor(BgmKeys.MainMenu, false, music, 0.4f);
            AudioTool.Get().PlaySFX("ambience", ambience, 0.5f, true, true);
            MusicLibraryLauncher.EnsureEntry();   //主菜单「音乐库」入口（运行时注入，不依赖重跑生成工具）
            LanPanel.EnsureMenuEntry();           //主菜单「局域网对战」入口（运行时注入；也可在场景里手放按钮绑 OnClickLan）

            //防御：重构后这些引用可能未被绑定（指向已隐藏/被删除的 TopBar 元素），判空避免 NRE
            if (username_txt != null) username_txt.text = "";
            if (credits_txt != null) credits_txt.text = "";
            if (version_text != null) version_text.text = "Version " + Application.version;
            if (deck_selector != null) deck_selector.onChange += OnChangeDeck;

            if (Authenticator.Get().IsConnected())
                AfterLogin();
            else
                RefreshLogin();
        }

        void Update()
        {
            //★ 导航修复：「进入对局」锁的超时自愈（兜底）。
            //  正常流程由场景加载接管；但若某次 StartGame 中途失败/被打断，锁会一直挂着 →
            //  玩家之后点任何入口都"没反应"。这里超过 25 秒就自动解锁。
            if (starting)
            {
                if (starting_time <= 0f)
                    starting_time = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - starting_time > 25f)
                    ResetStarting();
            }

            UserData udata = Authenticator.Get().UserData;
            if (udata != null && credits_txt != null)
            {
                credits_txt.text = GameUI.FormatNumber(udata.coins);
            }

            GameClientMatchmaker matchmaker = GameClientMatchmaker.Get();
            bool matchmaking = matchmaker != null && matchmaker.IsMatchmaking();
            if (loader != null && loader.activeSelf != matchmaking)
                loader.SetActive(matchmaking);
            MatchmakingPanel mm_panel = MatchmakingPanel.Get();
            if (mm_panel != null && mm_panel.IsVisible() != matchmaking)
                mm_panel.SetVisible(matchmaking);
        }

        private void OnDestroy()
        {
            if (deck_selector != null)
                deck_selector.onChange -= OnChangeDeck;

            GameClientMatchmaker matchmaker = GameClientMatchmaker.Get();
            if (matchmaker != null)
            {
                matchmaker.onMatchmaking -= OnMatchmakingDone;
                matchmaker.onMatchList -= OnReceiveObserver;
            }
        }

        private async void RefreshLogin()
        {
            bool success = await Authenticator.Get().RefreshLogin();
            if (success)
                AfterLogin();
            else
                SceneNav.GoTo("LoginMenu");
        }

        private void AfterLogin()
        {
            BlackPanel.Get().Hide();

            //Events
            GameClientMatchmaker matchmaker = GameClientMatchmaker.Get();
            matchmaker.onMatchmaking += OnMatchmakingDone;
            matchmaker.onMatchList += OnReceiveObserver;

            //Deck
            GameClient.player_settings.deck.tid = PlayerPrefs.GetString("tcg_deck_" + Authenticator.Get().Username, "");

            //UserData
            RefreshUserData();

            //Friend list
            //FriendPanel.Get().Show();

            //强制隐藏可能残留的全屏页面（组件被禁用无法自动隐藏时会一直拦截主界面点击）
            ForceHideModalPanels();
        }

        /// <summary>
        /// 强制隐藏 CardEditorPanel / CardPoolPanel / GraphEditorPanel / BlackPanel 等全屏页面。
        /// 这些页面若脚本组件被禁用（enabled=false），UIPanel 的自动隐藏（AfterHide）不会执行，
        /// 会一直 activeSelf=true + blocksRaycasts=true 全屏拦截主界面点击。
        /// </summary>
        private void ForceHideModalPanels()
        {
            UIPanel[] panels = FindObjectsOfType<UIPanel>(true);
            foreach (UIPanel p in panels)
            {
                if (p == null) continue;
                if (p.name == "HomePanel") continue;                       //主界面保留
                if (p.name == "BlackPanel" || p.name == "CardEditorPanel" ||
                    p.name == "CardPoolPanel" || p.name == "GraphEditorPanel" || p.name == "KeywordPanel")
                {
                    if (p.name == "GraphEditorPanel" && !p.enabled)
                        p.enabled = true; //修复：组件被禁用会导致永不自动隐藏且 Show() 失效
                    if (p.gameObject.activeSelf)
                        p.gameObject.SetActive(false);
                }
            }
        }

        public async void RefreshUserData()
        {
            UserData user = await Authenticator.Get().LoadUserData();
            if (user != null)
            {
                if (username_txt != null) username_txt.text = user.username;
                if (credits_txt != null) credits_txt.text = GameUI.FormatNumber(user.coins);

                if (avatar != null)
                {
                    AvatarData avatar_data = AvatarData.Get(user.avatar);
                    this.avatar.SetAvatar(avatar_data);
                }

                //Decks
                RefreshDeckList();
            }
        }

        public void RefreshDeckList()
        {
            deck_selector.SetupUserDeckList();
            deck_selector.SelectDeck(GameClient.player_settings.deck.tid);
            RefreshDeck(deck_selector.GetDeckID());
        }

        private void RefreshDeck(string tid)
        {
            if (deck_preview != null)
            {
                deck_preview.SetDeck(tid);
            }
        }

        private void OnChangeDeck(string tid)
        {
            GameClient.player_settings.deck = deck_selector.GetDeck();
            PlayerPrefs.SetString("tcg_deck_" + Authenticator.Get().Username, tid);
            RefreshDeck(tid);
        }

        private void OnMatchmakingDone(MatchmakingResult result)
        {
            if (result == null)
                return;

            if (result.success)
            {
                Debug.Log("Matchmaking found: " + result.success + " " + result.server_url + "/" + result.game_uid);
                StartGame(GameType.Multiplayer, result.game_uid, result.server_url);
            }
            else
            {
                MatchmakingPanel.Get().SetCount(result.players);
            }
        }

        private void OnReceiveObserver(MatchList list)
        {
            MatchListItem target = null;
            foreach (MatchListItem item in list.items)
            {
                if (item.username == GameClient.observe_user)
                    target = item;
            }

            if (target != null)
            {
                StartGame(GameType.Observer, target.game_uid, target.game_url);
            }
        }

        public void StartGame(GameType type, GameMode mode)
        {
            string uid = GameTool.GenerateRandomID();
            GameClient.game_settings.game_type = type;
            GameClient.game_settings.game_mode = mode;
            StartGame(uid); 
        }

        public void StartGame(GameType type, string game_uid, string server_url = "")
        {
            GameClient.game_settings.game_type = type;
            StartGame(game_uid, server_url);
        }

        public void StartGame(string game_uid, string server_url = "")
        {
            if (!starting)
            {
                starting = true;
                GameClient.game_settings.server_url = server_url; //Empty server_url will use the default one in NetworkData
                GameClient.game_settings.game_uid = game_uid;
                GameClientMatchmaker.Get().Disconnect();
                FadeToScene(GameClient.game_settings.GetScene());
            }
        }

        public void StartObserve(string user)
        {
            GameClient.observe_user = user;
            GameClientMatchmaker.Get().StopMatchmaking();
            GameClientMatchmaker.Get().RefreshMatchList(user);
        }

        public void StartChallenge(string user)
        {
            string self = Authenticator.Get().Username;
            if (self == user)
                return; //Cant challenge self

            string key;
            if (self.CompareTo(user) > 0)
                key = self + "-" + user;
            else
                key = user + "-" + self;

            StartMathmaking(GameMode.Casual, key);
        }

        public void StartMathmaking(GameMode mode, string group)
        {
            UserDeckData deck = deck_selector.GetDeck();
            if (deck != null)
            {
                GameClient.game_settings.game_type = GameType.Multiplayer;
                GameClient.game_settings.game_mode = mode;
                GameClient.player_settings.deck = deck;
                GameClient.game_settings.scene = GameplayData.Get().GetRandomArena();
                GameClientMatchmaker.Get().StartMatchmaking(group, GameClient.game_settings.nb_players);
            }
        }

        public void OnClickSolo()
        {
            if (!Authenticator.Get().IsConnected())
            {
                FadeToScene("LoginMenu");
                return;
            }

            SoloPanel.Get().Show();
        }

        /// <summary>局域网对战入口（开房/加入，房主直连，不走匹配服务器）。
        /// 场景按钮可直接绑本方法；未登录时由 GameClientLAN 用本地身份兜底（AuthenticatorLocal）。</summary>
        public void OnClickLan()
        {
            LanPanel.Open(transform);
        }

        public void OnClickPvP()
        {
            if (!Authenticator.Get().IsConnected())
            {
                FadeToScene("LoginMenu");
                return;
            }

            UserDeckData deck = deck_selector.GetDeck();
            if (deck == null || !deck.IsValid())
                return;

            //构筑规则：不合规就别进匹配（否则会卡在服务端校验上）
            List<DeckError> derrors = DeckValidator.Validate(deck, GameClient.game_settings);
            if (derrors.Count > 0)
            {
                Debug.LogWarning("[构筑规则] 卡组不合规：" + DeckValidator.JoinMessages(derrors, "；"));
                return;   //后续阶段：这里改成弹"错误列表"弹框并支持「返回调整」
            }

            StartMathmaking(GameMode.Ranked, "");
        }

        public void OnClickAdventure()
        {
            AdventurePanel.Get().Show();
        }

        public void OnClickCardPool()
        {
            if (CardPoolPanel.Get() != null)
                CardPoolPanel.Get().Show();
        }

        /// <summary>打开「音乐库」（主菜单音乐配置入口）。场景里也可直接把按钮 OnClick 绑到本方法。</summary>
        public void OnClickMusicLibrary()
        {
            MusicLibraryLauncher.Open(transform);
        }

        /// <summary>打开关键词管理面板（场景按钮 OnClick 里选此方法；面板未生成时提示）</summary>
        public void OnClickKeywordPanel()
        {
            TcgEngine.UI.KeywordPanel panel = TcgEngine.UI.KeywordPanel.Get();
            if (panel == null)
                panel = FindObjectOfType<TcgEngine.UI.KeywordPanel>(true);
            if (panel == null)
            {
                Debug.LogWarning("未找到 KeywordPanel：请先在编辑器菜单运行「生成关键词管理页面到主菜单场景」");
                return;
            }
            panel.Show();
        }

        public void OnClickPlayCode()
        {
            JoinCodePanel.Get().ShowGoldBattle(false);
        }

        public void OnClickGoldBattle()
        {
            JoinCodePanel.Get().ShowGoldBattle(true);
        }
        
        public void OnClickCancelMatch()
        {
            GameClientMatchmaker.Get().StopMatchmaking();
        }

        public void OnClickSettings()
        {
            SettingsPanel.Get().Show();
        }

        public void FadeToScene(string scene)
        {
            StartCoroutine(FadeToRun(scene));
        }

        private IEnumerator FadeToRun(string scene)
        {
            //★ 导航修复：目标场景不存在（未加入 Build Settings / 名字拼错）时**不能**进黑屏 ——
            //  BlackPanel 是全屏且自身没有任何出口，GoTo 失败就永久黑屏，玩家只能强退游戏。
            if (!SceneNav.DoSceneExist(scene))
            {
                Debug.LogError("[导航] 目标场景不存在，已取消跳转：" + scene
                    + "（检查 Build Settings 的场景列表 / 场景名拼写）");
                ResetStarting();
                BlackPanel.Get().Hide(true);
                yield break;
            }

            BlackPanel.Get().Show();
            AudioTool.Get().FadeOutMusic("music");
            yield return new WaitForSeconds(1f);
            SceneNav.GoTo(scene);
        }

        /// <summary>复位「进入对局」锁（失败/超时时调用）。
        /// 原实现 `starting` 一旦置 true 就**再不复位**，于是之后点任何"开始/进入"入口都会被
        /// `StartGame` 开头那句 `if (!starting)` 静默挡掉 —— 表现就是"点了没反应"。</summary>
        private void ResetStarting()
        {
            if (!starting)
                return;
            starting = false;
            starting_time = 0f;
            Debug.LogWarning("[导航] 已复位『进入对局』锁（原实现置位后永不复位 → 之后所有跳转静默失效）");
        }

        public void OnClickLogout()
        {
            TcgNetwork.Get().Disconnect();
            Authenticator.Get().Logout();
            FadeToScene("LoginMenu");
        }

        public void OnClickQuit()
        {
            Application.Quit();
        }

        public static MainMenu Get()
        {
            return instance;
        }
    }
}
