using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace TcgEngine.UI
{
    /// <summary>
    /// A toggle button that let you change tab panel
    /// </summary>

    public class TabButton : MonoBehaviour
    {
        public string group;
        public bool active;
        public GameObject highlight;
        public UIPanel ui_panel;

        public UnityAction onClick;
        public static UnityAction<TabButton> onClickAny;

        private static List<TabButton> tab_list = new List<TabButton>();

        private void Awake()
        {
            tab_list.Add(this);
        }

        private void OnDestroy()
        {
            tab_list.Remove(this);
        }

        void Start()
        {
            Button button = GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(OnClick);

            if (active && ui_panel != null)
                ui_panel.Show();
        }

        void Update()
        {
            if (highlight != null)
                highlight.SetActive(active);
        }

        private void OnClick()
        {
            Activate();
            onClick?.Invoke();
            onClickAny?.Invoke(this);
        }

        public void Activate()
        {
            //★ 没绑定 ui_panel 时**不能**先隐藏整组：那会把当前页面也藏掉却没有任何页面顶上 → 直接黑屏
            //  （2026-09 审计：Menu.unity 里有 4 个 TabButton 的 ui_panel = {fileID: 0}）。
            //  正解是"保持现状 + 明确报错"，玩家至少不会掉进黑屏。
            if (ui_panel == null)
            {
                Debug.LogError("[导航] TabButton「" + gameObject.name + "」未绑定 ui_panel：已**保持当前页面不变**"
                    + "（原实现会隐藏整组导致黑屏）。请运行对应生成工具修复该按钮的绑定。");
                return;
            }

            //切换分组期间置位：这期间各页的 Hide() 不应触发 UIPanel 的"黑屏兜底→回首页"，否则会打架
            switching_depth++;
            try
            {
                SetAll(group, false);
                active = true;
                if (ui_panel != null)
                    ui_panel.Show();
            }
            finally
            {
                switching_depth--;
            }
        }

        public void Deactivate()
        {
            switching_depth++;
            try
            {
                active = false;
                if (ui_panel != null)
                    ui_panel.Hide();
            }
            finally
            {
                switching_depth--;
            }
        }

        public bool IsActive()
        {
            return active;
        }

        /// <summary>是否正在"切分组/切页"（UIPanel 的黑屏兜底会看这个标志，避免和切页流程互相打架）</summary>
        private static int switching_depth;
        public static bool IsSwitchingGroups { get { return switching_depth > 0; } }

        public static void SetAll(string group, bool act)
        {
            switching_depth++;
            try
            {
                foreach (TabButton btn in tab_list)
                {
                    if (btn.group == group)
                    {
                        btn.active = act;
                        if (btn.ui_panel != null)
                            btn.ui_panel.SetVisible(act);
                    }
                }
            }
            finally
            {
                switching_depth--;
            }
        }

        public static List<TabButton> GetAll(string group)
        {
            List<TabButton> glist = new List<TabButton>();
            foreach (TabButton btn in tab_list)
            {
                if (btn.group == group)
                    glist.Add(btn);
            }
            return glist;
        }

        public static List<TabButton> GetAll()
        {
            return tab_list;
        }
    }
}
