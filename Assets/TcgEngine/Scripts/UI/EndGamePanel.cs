using TcgEngine.Client;
using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// Endgame panel is shown when a game end
    /// Showing winner and rewards obtained
    /// </summary>

    public class EndGamePanel : UIPanel
    {
        public Text winner_text;
        public Image winner_glow;

        public Text player_name;
        public Text other_name;
        public Image player_avatar;
        public Image other_avatar;

        public Text coins_text;
        public Text xp_text;

        private bool reward_loaded = false;
        private float timer = 0f;

        private int target_coins = 0;
        private int target_xp = 0;
        private float coins = 0;
        private float xp = 0;

        private static EndGamePanel _instance;

        protected override void Awake()
        {
            base.Awake();
            _instance = this;
        }

        protected override void Start()
        {
            base.Start();

            coins_text.text = "";
            xp_text.text = "";

        }

        //★ 结算奖励加载的重试控制（原来 Update 每秒调一次 async void RefreshRewards：
        //  ① 请求在途时定时器会再发一个 → 同一个 URL 叠发多份；② 失败后无限重试）
        private bool reward_requesting;                 //请求进行中
        private bool reward_give_up;                    //连续失败达到上限 → 停止重试
        private int reward_retry;
        private const int REWARD_MAX_RETRY = 8;

        protected override void Update()
        {
            base.Update();

            if (!reward_loaded && !reward_requesting && !reward_give_up && IsVisible())
            {
                timer += Time.deltaTime;
                if (timer > 1f)
                {
                    timer = 0f;
                    RefreshRewards();
                }
            }

            if (reward_loaded)
            {
                coins = Mathf.MoveTowards(coins, target_coins, 2000f * Time.deltaTime);
                xp = Mathf.MoveTowards(xp, target_xp, 500f * Time.deltaTime);

                coins_text.text = "+ " + Mathf.RoundToInt(coins) + " coins";
                xp_text.text = "+ " + Mathf.RoundToInt(xp) + " xp";

                if (Mathf.RoundToInt(coins) == 0)
                    coins_text.text = "";
                if (Mathf.RoundToInt(xp) == 0)
                    xp_text.text = "";
            }
        }

        private void RefreshPanel(int winner)
        {
            Game data = GameClient.Get().GetGameData();
            Player pwinner = data.GetPlayer(winner);
            Player player = GameClient.Get().GetPlayer();
            Player oplayer = GameClient.Get().GetOpponentPlayer();

            player_name.text = player.username;
            other_name.text = oplayer.username;

            AvatarData avat1 = AvatarData.Get(player.avatar);
            AvatarData avat2 = AvatarData.Get(oplayer.avatar);
            if(avat1 != null)
                player_avatar.sprite = avat1.avatar;
            if (avat2 != null)
                other_avatar.sprite = avat2.avatar;

            if (pwinner != null && pwinner == player)
                winner_text.text = "Victory";
            else if (pwinner != null)
                winner_text.text = "Defeat";
            else
                winner_text.text = "Tie";

            if (pwinner == player)
                winner_glow.rectTransform.anchoredPosition = player_avatar.rectTransform.anchoredPosition;
            if (pwinner == oplayer)
                winner_glow.rectTransform.anchoredPosition = other_avatar.rectTransform.anchoredPosition;
            winner_glow.gameObject.SetActive(pwinner != null);
        }

        private async void RefreshRewards()
        {
            if (reward_requesting)
                return;                     //★ 上一次请求还没回来 → 不再发（避免同 URL 叠发）
            reward_requesting = true;
            try
            {
                //Online rewards
                if (GameClient.game_settings.IsOnline())
                {
                    string url = ApiClient.ServerURL + "/matches/" + GameClient.game_settings.game_uid;
                    WebResponse res = await ApiClient.Get().SendGetRequest(url);
                    if (res.success)
                    {
                        reward_loaded = true;
                        MatchResponse match = ApiTool.JsonToObject<MatchResponse>(res.data);
                        string username = ApiClient.Get().Username.ToLower();
                        foreach (MatchDataResponse data in match.udata)
                        {
                            if (data.username.ToLower() == username)
                            {
                                target_coins = data.reward.coins;
                                target_xp = data.reward.xp;
                            }
                        }
                    }
                }

                //Adventure Rewards
                if (GameClient.game_settings.game_type == GameType.Adventure)
                {
                    LevelData lvl = LevelData.Get(GameClient.game_settings.level);
                    if (lvl != null && RewardManager.Get().IsRewardGained())
                    {
                        target_coins = lvl.reward_coins;
                        target_xp = lvl.reward_xp;
                        reward_loaded = true;
                    }
                }
            }
            finally
            {
                reward_requesting = false;
                //★ 重试上限：失败（或本模式没有奖励可拿）时不再无限每帧重试
                if (!reward_loaded && !reward_give_up && ++reward_retry >= REWARD_MAX_RETRY)
                {
                    reward_give_up = true;
                    //只有"本该有奖励"的联网模式才提示；单机（Solo）本来就拿不到奖励，静默停止即可
                    if (GameClient.game_settings.IsOnline())
                        Debug.LogWarning("[结算] 奖励加载连续 " + REWARD_MAX_RETRY + " 次未成功，已停止重试"
                            + "（mode=" + GameClient.game_settings.game_type + "）；重新打开结算面板会重置");
                }
            }
        }

        public void ShowEnd(int winner)
        {
            reward_loaded = false;
            reward_retry = 0;          //重新打开结算面板 → 重置重试计数/放弃标记
            reward_give_up = false;
            RefreshPanel(winner);
            RefreshRewards();
            Show();
        }

        public void OnClickQuit()
        {
            GameUI.Get().OnClickQuit();
        }

        public static EndGamePanel Get()
        {
            return _instance;
        }
    }
}
