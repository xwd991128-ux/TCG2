using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// Visual representation of a card in your collection in the Deckbuilder
    /// </summary>

    public class CollectionCard : MonoBehaviour
    {
        public CardUI card_ui;
        public Image quantity_bar;
        public Text quantity;

        [Header("Mat")]
        public Material color_mat;
        public Material grayscale_mat;

        public UnityAction<CardUI> onClick;
        public UnityAction<CardUI> onClickRight;

        private void Start()
        {
            card_ui.onClick += onClick;
            card_ui.onClickRight += onClickRight;
        }

        public void SetCard(CardData card, VariantData variant, int quantity)
        {
            card_ui.SetCard(card, variant);
            SetQuantity(quantity);
        }

        public void SetQuantity(int quantity)
        {
            if (this.quantity_bar != null)
                this.quantity_bar.enabled = quantity > 0;
            if (this.quantity != null)
                this.quantity.text = quantity.ToString();
            if (this.quantity != null)
                this.quantity.enabled = quantity > 0;
        }

        /// <summary>
        /// 切换卡面材质：grayscale=true 用灰度材质（"未拥有"），false 用彩色材质（真彩色）。
        /// 注意：color_mat / grayscale_mat 允许为空（为空即用默认材质，同样是真彩色）；
        ///       并且必须做空判断 —— 卡牌编辑器的卡面预制体并不保证有 quantity_bar。
        /// </summary>
        public void SetGrayscale(bool grayscale)
        {
            Material mat = grayscale ? grayscale_mat : color_mat;
            if (quantity_bar != null)
                quantity_bar.material = mat;
            if (card_ui != null)
                card_ui.SetMaterial(mat);
        }

        public CardData GetCard()
        {
            return card_ui.GetCard();
        }

        public VariantData GetVariant()
        {
            return card_ui.GetVariant();
        }
    }
}