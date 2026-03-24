using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;

namespace TcgEngine.UI
{
    public class GoldBattlePanel : UIPanel
    {
        public InputField code_field;
        public Text cost_text;
        public Text coins_text;
        public int gold_cost = 100;

        private string game_code = "";

        private static GoldBattlePanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;
        }

        protected override void Update()
        {
            base.Update();
            
            UserData udata = Authenticator.Get().UserData;
            if (udata != null && coins_text != null)
            {
                coins_text.text = udata.coins.ToString();
            }
        }

        public void OnClickRandomize()
        {
            game_code = GameTool.GenerateRandomID(4, 6).ToUpper();
            code_field.text = game_code;
        }

        public void OnClickJoinGoldBattle()
        {
            if (code_field.text.Length < 3)
                return;

            UserData udata = Authenticator.Get().UserData;
            if (udata == null || udata.coins < gold_cost)
            {
                Debug.Log("Not enough coins for gold battle!");
                return;
            }

            game_code = code_field.text.ToUpper();
            MainMenu.Get().StartMathmaking(GameMode.GoldBattle, "gold_" + game_code);
            Hide();
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            code_field.text = "";
            
            if (cost_text != null)
                cost_text.text = gold_cost.ToString();
        }

        public string GetCode()
        {
            return game_code;
        }

        public int GetGoldCost()
        {
            return gold_cost;
        }

        public static GoldBattlePanel Get()
        {
            return instance;
        }
    }
}
