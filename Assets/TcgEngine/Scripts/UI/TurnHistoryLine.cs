using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;
using UnityEngine.EventSystems;

namespace TcgEngine.UI
{
    /// <summary>
    /// One of the squares in the history bar
    /// </summary>

    public class TurnHistoryLine : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public HoverTargetUI hover;
        public Image card_img;

        private Card card;
        private float timer = 0f;
        private bool is_hover = false;

        private static List<TurnHistoryLine> line_list = new List<TurnHistoryLine>();

        void Awake()
        {
            line_list.Add(this);
        }

        void OnDestroy()
        {
            line_list.Remove(this);   //原来误写成 Add：静态列表只增不减，销毁过的对象会一直留在表里
        }

        void Start()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            timer += Time.deltaTime;
        }

        public void SetLine(ActionHistory history)
        {
            Game gdata = GameClient.Get().GetGameData();
            Card acard = gdata.GetCard(history.card_uid);
            CardData icard = CardData.Get(history.card_id);
            //acard 可能已被移出所有区域（衍生物被消灭等），GetCard 返回 null
            VariantData variant = acard != null ? acard.VariantData : null;
            card = acard;

            if (icard == null)
                return;

            //★文案统一走 BattleLog.FormatHistory（中文，与「对战记录」面板同一套格式化）
            //  旧实现这里全是英文（"X was played" / "casted ... on ..."），中文版里是明显瑕疵。
            string text = TcgEngine.Gameplay.BattleLog.FormatHistory(history, gdata, GameClient.Get().GetPlayerID());
            SetLine(icard, variant, text);
        }

        public void SetLine(CardData icard, VariantData variant, string text)
        {
            card_img.sprite = icard.GetFullArt(variant);
            hover.text = text;
            gameObject.SetActive(true);
            timer = 0f;
        }

        public void Hide()
        {
            card = null;
            if (timer > 0.05f)
                gameObject.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            timer = 0f;
            is_hover = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            timer = 0f;
            is_hover = false;
        }

        void OnDisable()
        {
            is_hover = false;
        }

        public static Card GetHoverCard()
        {
            foreach (TurnHistoryLine line in line_list)
            {
                if (line.card != null && line.is_hover)
                    return line.card;
            }
            return null;
        }
    }
}
