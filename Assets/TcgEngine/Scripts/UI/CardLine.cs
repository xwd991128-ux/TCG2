using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine;

namespace TcgEngine.UI
{
    public class CardLine : MonoBehaviour
    {
        public Text card_name;
        public Text count_txt;
        public Text mana_txt;
        public Image highlight;
        public Image card_icon;

        public void SetLine(CardData card, int count, bool highlight = false)
        {
            if (card_name != null)
                card_name.text = card != null ? card.title : "Unknown";
            
            if (mana_txt != null)
                mana_txt.text = card != null ? card.mana.ToString() : "0";
            
            if (count_txt != null)
            {
                if (count > 1)
                {
                    count_txt.text = "x" + count;
                    count_txt.gameObject.SetActive(true);
                }
                else
                {
                    count_txt.gameObject.SetActive(false);
                }
            }
            
            if (this.highlight != null)
                this.highlight.enabled = highlight;
            
            if (card_icon != null && card != null)
            {
                card_icon.sprite = card.art_full;
            }
            
            gameObject.SetActive(true);
        }

        public void SetAbility(AbilityData ability, int count, bool highlight = false)
        {
            if (card_name != null)
                card_name.text = !string.IsNullOrEmpty(ability.title) ? ability.title : ability.id;
            
            if (mana_txt != null)
                mana_txt.text = "";
            
            if (count_txt != null)
            {
                if (count > 1)
                {
                    count_txt.text = "x" + count;
                    count_txt.gameObject.SetActive(true);
                }
                else
                {
                    count_txt.gameObject.SetActive(false);
                }
            }
            
            if (this.highlight != null)
                this.highlight.enabled = highlight;
            
            if (card_icon != null)
            {
                card_icon.enabled = false;
            }
            
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
