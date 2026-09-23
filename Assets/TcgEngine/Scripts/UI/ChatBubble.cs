using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// UI that appears when sending a chat message
    /// </summary>

    public class ChatBubble : MonoBehaviour
    {
        public Text msg_txt;
        public Image bubble;
        public CanvasGroup group;

        private float timer = 0f;

        private void Update()
        {
            timer -= Time.deltaTime;
            if (group != null)
                group.alpha = timer;   //未绑定时不再每帧空引用：那样会在 alpha 这行抛出，
                                       //导致下面的 Hide() 永远执行不到、气泡卡在屏幕上不走

            if (timer < 0f)
                Hide();
        }

        public void SetLine(string msg, float duration)
        {
            if (msg_txt != null)
                msg_txt.text = msg;
            timer = duration;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}