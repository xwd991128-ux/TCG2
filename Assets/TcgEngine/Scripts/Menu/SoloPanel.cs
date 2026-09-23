using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using TcgEngine.Client;

namespace TcgEngine.UI
{

    public class SoloPanel : UIPanel
    {
        public Text username;
        public DeckSelector selector_player;
        public DeckSelector selector_ai;

        public DeckDisplay display_player;
        public DeckDisplay display_ai;

        private static SoloPanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            //★ 导航修复：本页是主菜单的一个全屏模块页，原先**没有任何退出路径**（只有「开始」），
            //  玩家点进来就只能强退。补一个「返回首页」（与 CollectionPanel/PackPanel 的 HomeReturnBtn 同语义）。
            EnsureExitButton("返回首页", () =>
            {
                HomePanel home = HomePanel.Get();
                if (home != null)
                    home.ReturnHome();
                else
                    Hide();
            });
        }

        protected override void Start()
        {
            base.Start();

            selector_player.onChange += OnChangeDeck;
            selector_ai.onChange += OnChangeDeck;
        }

        private void RefreshDecks()
        {
            if(username != null)
                username.text = Authenticator.Get().Username;

            string selected_id = MainMenu.Get().deck_selector.GetDeckID();
            selector_player.SetupUserDeckList();
            selector_player.SelectDeck(selected_id);
            selector_ai.SetupAIDeckList();
            selector_ai.SelectDeck(0);

            RefreshDeckDisplay();
        }

        private void RefreshDeckDisplay()
        {
            display_player.SetDeck(selector_player.GetDeckID());
            display_ai.SetDeck(selector_ai.GetDeckID());
        }

        private void OnChangeDeck(string id)
        {
            RefreshDeckDisplay();
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            RefreshDecks();
        }

        public void OnClickPlay()
        {
            UserDeckData deck = selector_player.GetDeck();
            UserDeckData aideck = selector_ai.GetDeck();

            //★ 这里**不再**用 UserDeckData.IsValid() 兜底：它把张数写死成 GameplayData.deck_size(30)，
            //  会把"主卡 20 张的乱斗"这类合法卡组直接挡掉 —— 玩家连构筑规则弹框都进不去，也就永远选不到乱斗。
            //  也**不做空值静默返回**（那会让"没选卡组"时点「开始」毫无反应，玩家只会以为功能不存在）：
            //  一律打开弹框，把"没选卡组/张数不足"作为**中文错误**列出来，「开始」自然置灰。
            //  合法性统一由弹框按**当前构筑环境**判定（含对手卡组）。
            DeckFormatPopupUI popup = DeckFormatPopupUI.Create(transform);
            popup.Open(deck, aideck, () => StartSoloMatch(deck, aideck));
        }

        /// <summary>真正开始单人局：卡组的构筑规则已在弹框里确认过，选择也已写入对局设置</summary>
        private void StartSoloMatch(UserDeckData deck, UserDeckData aideck)
        {
            GameClient.player_settings.deck = deck;
            GameClient.ai_settings.deck = aideck;
            GameClient.ai_settings.ai_level = GameplayData.Get().ai_level;
            GameClient.game_settings.scene = GameplayData.Get().GetRandomArena();

            MainMenu.Get().StartGame(GameType.Solo, GameMode.Casual);
        }

        public static SoloPanel Get()
        {
            return instance;
        }
    }
}