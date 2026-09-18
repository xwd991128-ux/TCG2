using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.UI
{
    public class BlackPanel : UIPanel
    {

        private static BlackPanel instance;
        private float shown_time;                  //本次显示的起始时间（用于"黑屏兜底"）

        protected override void Awake()
        {
            base.Awake();
            instance = this;
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            shown_time = Time.realtimeSinceStartup;
        }

        protected override void Update()
        {
            base.Update();

            //★ 黑屏兜底（导航）：本页是全屏黑幕、**自身没有任何出口**，正常只在"切场景"的 1 秒里出现。
            //  一旦场景加载失败/流程被打断（例如目标场景不存在、StartGame 中途失败），
            //  玩家就会永久黑屏且什么都点不了（只能强退）。这里超时自动恢复。
            if (visible && shown_time > 0f && Time.realtimeSinceStartup - shown_time > 30f)
            {
                Debug.LogError("[导航] BlackPanel 已显示超过 30 秒仍未切换场景 → 自动隐藏，避免永久黑屏"
                    + "（正常切场景只需约 1 秒；请检查目标场景是否存在/是否加入 Build Settings）");
                shown_time = 0f;
                Hide(true);
            }
        }

        public static BlackPanel Get()
        {
            return instance;
        }
    }
}
