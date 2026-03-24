using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TcgEngine.Client
{
    public class RewardManager : MonoBehaviour
    {
        private bool reward_gained = false;

        private static RewardManager instance;

        void Awake()
        {
            instance = this;
        }

        private void Start()
        {
            GameClient.Get().onGameEnd += OnGameEnd;
            Debug.Log("RewardManager: Subscribed to onGameEnd");
        }

        void OnGameEnd(int winner)
        {
            int player_id = GameClient.Get().GetPlayerID();
            Debug.Log("RewardManager: OnGameEnd called, winner=" + winner + ", player_id=" + player_id + ", game_mode=" + GameClient.game_settings.game_mode);
            
            if (GameClient.game_settings.game_mode == GameMode.GoldBattle)
            {
                Debug.Log("RewardManager: GoldBattle mode detected!");
                if (winner < 0)
                {
                    Debug.Log("RewardManager: Draw game, no coins exchanged");
                    return;
                }
                if (winner == player_id)
                {
                    Debug.Log("RewardManager: You are the winner!");
                    if (Authenticator.Get().IsTest())
                        GainGoldBattleRewardTest(true);
                    if (Authenticator.Get().IsApi())
                        GainGoldBattleRewardAPI(true);
                }
                else
                {
                    Debug.Log("RewardManager: You lost!");
                    if (Authenticator.Get().IsTest())
                        GainGoldBattleRewardTest(false);
                    if (Authenticator.Get().IsApi())
                        GainGoldBattleRewardAPI(false);
                }
                return;
            }
            
            if (GameClient.game_settings.game_type == GameType.Adventure && winner == player_id)
            {
                UserData udata = Authenticator.Get().UserData;
                LevelData level = LevelData.Get(GameClient.game_settings.level);
                if (level != null && !udata.HasReward(level.id) && !reward_gained)
                {
                    if (Authenticator.Get().IsTest())
                        GainRewardTest(level);
                    if (Authenticator.Get().IsApi())
                        GainRewardAPI(level);
                }
            }
        }

        private async void GainGoldBattleRewardTest(bool is_winner)
        {
            UserData udata = Authenticator.Get().UserData;
            if (is_winner)
            {
                udata.coins += 100;
                Debug.Log("Gold Battle: You won 100 coins! New total: " + udata.coins);
            }
            else
            {
                udata.coins -= 100;
                if (udata.coins < 0) udata.coins = 0;
                Debug.Log("Gold Battle: You lost 100 coins! New total: " + udata.coins);
            }
            await Authenticator.Get().SaveUserData();
        }

        private async void GainGoldBattleRewardAPI(bool is_winner)
        {
            bool success = await GainGoldBattleRewardAPI(is_winner ? 100 : -100);
            reward_gained = success;
        }

        public async Task<bool> GainGoldBattleRewardAPI(int coins)
        {
            GoldBattleRewardRequest req = new GoldBattleRewardRequest();
            req.coins = coins;

            string url = ApiClient.ServerURL + "/users/goldbattle/reward/" + ApiClient.Get().UserID;
            string json = ApiTool.ToJson(req);
            WebResponse res = await ApiClient.Get().SendPostRequest(url, json);
            Debug.Log("Gold Battle Reward API: coins=" + coins + ", success=" + res.success);
            return res.success;
        }

        private async void GainRewardTest(LevelData level)
        {
            VariantData variant = VariantData.GetDefault();
            UserData udata = Authenticator.Get().UserData;
            udata.coins += level.reward_coins;
            udata.xp += level.reward_xp;
            udata.AddReward(level.id);

            foreach (CardData card in level.reward_cards)
            {
                udata.AddCard(card.id, variant.id, 1);
            }

            foreach (PackData pack in level.reward_packs)
            {
                udata.AddPack(pack.id, 1);
            }

            reward_gained = true;
            await Authenticator.Get().SaveUserData();
        }

        private async void GainRewardAPI(LevelData level)
        {
            bool success = await GainRewardAPI(level.id);
            reward_gained = success;
        }

        public async Task<bool> GainRewardAPI(string reward_id)
        {
            RewardGainRequest req = new RewardGainRequest();
            req.reward = reward_id;

            string url = ApiClient.ServerURL + "/users/rewards/gain/" + ApiClient.Get().UserID;
            string json = ApiTool.ToJson(req);
            WebResponse res = await ApiClient.Get().SendPostRequest(url, json);
            Debug.Log("Gain Reward: " + reward_id + " " + res.success);
            return res.success;
        }

        public bool IsRewardGained()
        {
            return reward_gained;
        }

        public static RewardManager Get()
        {
            return instance;
        }
    }
}
