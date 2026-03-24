using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;

namespace TcgEngine.UI
{

    public class JoinCodePanel : UIPanel
    {
        public InputField code_field;
        public int gold_cost = 100;

        private string game_code = "";
        private bool gold_battle_mode = false;

        private static JoinCodePanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;
        }

        protected override void Update()
        {
            base.Update();
        }


        public void OnClickRandomize()
        {
            game_code = GameTool.GenerateRandomID(4,6).ToUpper();
            code_field.text = game_code;
        }

        public void OnClickJoinCode()
        {
            Debug.Log("OnClickJoinCode called, code: " + code_field.text + ", gold_battle_mode: " + gold_battle_mode);
            
            if (code_field.text.Length < 3)
            {
                Debug.Log("Code too short");
                return;
            }

            game_code = code_field.text.ToUpper();
            
            if (gold_battle_mode)
            {
                UserData udata = Authenticator.Get().UserData;
                Debug.Log("UserData: " + (udata != null ? udata.username : "null") + ", coins: " + (udata != null ? udata.coins : 0));
                
                if (udata == null || udata.coins < gold_cost)
                {
                    Debug.Log("Not enough coins for gold battle! Need: " + gold_cost);
                    return;
                }
                Debug.Log("Starting GoldBattle matchmaking with code: gold_" + game_code);
                MainMenu.Get().StartMathmaking(GameMode.GoldBattle, "gold_" + game_code);
            }
            else
            {
                Debug.Log("Starting Casual matchmaking with code: code_" + game_code);
                MainMenu.Get().StartMathmaking(GameMode.Casual, "code_" + game_code);
            }
            Hide();
        }

        public void ShowGoldBattle(bool gold_battle)
        {
            gold_battle_mode = gold_battle;
            Debug.Log("ShowGoldBattle called with mode: " + gold_battle);
            Show();
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            code_field.text = "";
        }

        public string GetCode()
        {
            return game_code;
        }

        public static JoinCodePanel Get()
        {
            return instance;
        }

    }
}
