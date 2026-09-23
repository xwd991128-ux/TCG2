using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TcgEngine.Client;
using System.Linq;

namespace TcgEngine.UI
{
    /// <summary>
    /// Scripts to display all stats of a card, 
    /// is used by other script that display cards like BoardCard, and HandCard, CollectionCard..
    /// </summary>

    public class CardUI : MonoBehaviour, IPointerClickHandler
    {
        public Image card_image;
        public Image frame_image;
        public Image card_description_image;
        public Image card_traits_image;
        public Image team_icon;
        public Image rarity_icon;
        public Image attack_icon;
        public Image hp_icon;
        public Image cost_icon;
        public Image Character_trait_icon; //角色图标
        public Text attack;
        public Text hp;
        public Text cost;

        public Text card_title;
        public Text card_text;
        public Text trait_text;

        public TraitUI[] stats;

        public UnityAction<CardUI> onClick;
        public UnityAction<CardUI> onClickRight;

        private CardData card;
        private VariantData variant;

        //★ 脏标记（性能）：BoardCard.Update / HandCard.Update 每帧都会调 SetCard(card)，
        //  而 SetCard 内部会写一堆 TMP 文本与图标（每次写 .text 都会让 TMP 组件标脏并重建网格）。
        //  用「运行时会变的显示输入」做一个不分配的签名，签名没变就整套跳过。
        private Card last_card;      //上次刷新的卡实例
        private int last_sig;        //上次的显示签名
        private bool sig_valid;      //是否已有有效签名（首帧必须刷新一次）

        public static int stat_calls;      //诊断：SetCard(Card) 被调用次数（累计）
        public static int stat_rebuilds;   //诊断：其中真正重建卡面的次数（stat_calls - stat_rebuilds = 省下的次数）

        public void SetCard(Card card)
        {
            if (card == null)
                return;

            stat_calls++;

            //★ 脏标记快速路径：同一张卡、显示签名未变、且自身已激活 → 直接返回。
            //  （自身未激活时不走快速路径，保证下面 SetCard(CardData,VariantData) 里的 SetActive(true) 仍会执行）
            int sig = CalcSignature(card);
            if (sig_valid && card == last_card && sig == last_sig && gameObject.activeSelf)
                return;

            sig_valid = true;
            last_card = card;
            last_sig = sig;
            stat_rebuilds++;

            SetCard(card.CardData, card.VariantData);

            if (cost != null)
                cost.text = card.GetMana().ToString();
            if (cost != null && card.CardData.IsDynamicManaCost())
                cost.text = "X";
            if (attack != null)
                attack.text = card.GetAttack().ToString();
            if (hp != null)
                hp.text = card.GetHP().ToString();

            foreach (TraitUI stat in stats)
                stat.SetCard(card);
        }

        /// <summary>卡面显示签名（**零分配**，只做整数混合）：覆盖 SetCard(Card) 里所有「运行时会变」的输入 ——
        /// 卡/变体实例、当前法力、攻击、生命，以及每个 TraitUI 关注的「是否拥有 + 数值」。
        /// 其余输入（卡名/描述/卡图/图标/种族文本）都来自 CardData，对同一个卡实例是静态的，故不进签名。</summary>
        private int CalcSignature(Card card)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (card.CardData != null ? card.CardData.GetInstanceID() : 0);
                h = h * 31 + (card.VariantData != null ? card.VariantData.GetInstanceID() : 0);
                h = h * 31 + card.GetMana();
                h = h * 31 + card.GetAttack();
                h = h * 31 + card.GetHP();
                if (stats != null)
                {
                    for (int i = 0; i < stats.Length; i++)
                    {
                        TraitUI stat = stats[i];
                        if (stat == null || stat.trait == null)
                            continue;
                        h = h * 31 + stat.trait.GetInstanceID();
                        h = h * 31 + (card.HasTrait(stat.trait) ? 1 : 0);
                        h = h * 31 + card.GetTraitValue(stat.trait);
                    }
                }
                return h;
            }
        }

        public void SetCard(CardData card, VariantData variant)
        {
            if (card == null)
                return;

            this.card = card;
            this.variant = variant;

            if(card_image != null)
                card_image.sprite = card.GetFullArt(variant);
            if (frame_image != null)
                frame_image.sprite = variant.frame;
            if (card_title != null)
                card_title.text = card.GetTitle().ToUpper();
            if (card_text != null)
                card_text.text = card.GetText();

            if (attack_icon != null)
                attack_icon.enabled = card.IsCharacter()|| card.IsEquipment();
            if (attack != null)
                attack.enabled = card.IsCharacter()|| card.IsEquipment();
            if (hp_icon != null)
                hp_icon.enabled = card.IsBoardCard() || card.IsEquipment();
            if (hp != null)
                hp.enabled = card.IsBoardCard() || card.IsEquipment();
            if (cost_icon != null)
                cost_icon.enabled = card.type != CardType.Hero;
            if (cost != null)
                cost.enabled = card.type != CardType.Hero;

            if (cost != null)
                cost.text = card.mana.ToString();
            if (cost != null && card.IsDynamicManaCost())
                cost.text = "X";
            if (attack != null)
                attack.text = card.IsEquipment()? (card.attack>=0? "+" + card.attack.ToString(): card.attack.ToString()) : card.attack.ToString();
            if (hp != null)
                hp.text = card.IsEquipment() ? (card.hp >= 0 ? "+" + card.hp.ToString() : card.hp.ToString()) : card.hp.ToString();

            if (team_icon != null)
            {
                team_icon.sprite = card.team.icon;
                team_icon.enabled = team_icon.sprite != null;
            }

            if (rarity_icon != null)
            {
                rarity_icon.sprite = card.rarity.icon;
                rarity_icon.enabled = rarity_icon.sprite != null && card.type != CardType.Hero;
            }
            

            //if (Character_trait_icon != null)
            //{
            //    Character_trait_icon.sprite = card.character_trait != null ? card.character_trait.icon : null;
            //    Character_trait_icon.enabled = Character_trait_icon.sprite != null && card.type != CardType.Hero;
            //}

            if (trait_text != null)
            {
                if (card.traits != null && card.traits.Length > 0 && card.type != CardType.Hero)
                {
                    trait_text.text = string.Join(",", card.traits.Where(t => t != null).Select(t => t.title));
                    trait_text.enabled = true;
                }
                else
                {
                    trait_text.text = "";
                    trait_text.enabled = false;
                }
            }

            foreach (TraitUI stat in stats)
                stat.SetCard(card);

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }

        public void SetHP(int hp_value)
        {
            if (hp != null)
                hp.text = hp_value.ToString();
        }

        public void SetMaterial(Material mat)
        {
            if (card_image != null)
                card_image.material = mat;
            if (frame_image != null)
                frame_image.material = mat;
            if (team_icon != null)
                team_icon.material = mat;
            if (rarity_icon != null)
                rarity_icon.material = mat;
            if (Character_trait_icon != null)
                Character_trait_icon.material = mat;
            if (attack_icon != null)
                attack_icon.material = mat;
            if (hp_icon != null)
                hp_icon.material = mat;
            if (cost_icon != null)
                cost_icon.material = mat;
        }

        public void SetOpacity(float opacity)
        {
            if (card_image != null)
                card_image.color = new Color(card_image.color.r, card_image.color.g, card_image.color.b, opacity);
            if (frame_image != null)
                frame_image.color = new Color(frame_image.color.r, frame_image.color.g, frame_image.color.b, opacity);
            if (team_icon != null)
                team_icon.color = new Color(team_icon.color.r, team_icon.color.g, team_icon.color.b, opacity);
            if (rarity_icon != null)
                rarity_icon.color = new Color(rarity_icon.color.r, rarity_icon.color.g, rarity_icon.color.b, opacity);
            if (Character_trait_icon != null)
                Character_trait_icon.color = new Color(Character_trait_icon.color.r, Character_trait_icon.color.g, Character_trait_icon.color.b, opacity);
            if (attack_icon != null)
                attack_icon.color = new Color(attack_icon.color.r, attack_icon.color.g, attack_icon.color.b, opacity);
            if (hp_icon != null)
                hp_icon.color = new Color(hp_icon.color.r, hp_icon.color.g, hp_icon.color.b, opacity);
            if (cost_icon != null)
                cost_icon.color = new Color(cost_icon.color.r, cost_icon.color.g, cost_icon.color.b, opacity);
            if (attack != null)
                attack.color = new Color(attack.color.r, attack.color.g, attack.color.b, opacity);
            if (hp != null)
                hp.color = new Color(hp.color.r, hp.color.g, hp.color.b, opacity);
            if (cost != null)
                cost.color = new Color(cost.color.r, cost.color.g, cost.color.b, opacity);
            if (card_title != null)
                card_title.color = new Color(card_title.color.r, card_title.color.g, card_title.color.b, opacity);
            if (card_text != null)
                card_text.color = new Color(card_text.color.r, card_text.color.g, card_text.color.b, opacity);
        }

        public void Hide()
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (onClick != null)
                    onClick.Invoke(this);
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (onClickRight != null)
                    onClickRight.Invoke(this);
            }
        }

        public CardData GetCard()
        {
            return card;
        }

        public VariantData GetVariant()
        {
            return variant;
        }
    }
}
