using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Client;

namespace TcgEngine.UI
{

    public class AdventurePanel : UIPanel
    {

        private List<LevelUI> level_uis = new List<LevelUI>();

        private static AdventurePanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            //★ 导航修复：本页原先同样没有任何退出路径（点进来只能选关开局），补「返回首页」。
            EnsureExitButton("返回首页", () =>
            {
                HomePanel home = HomePanel.Get();
                if (home != null)
                    home.ReturnHome();
                else
                    Hide();
            });
        }

        protected override void Start()
        {
            base.Start();
            level_uis.AddRange(GetComponentsInChildren<LevelUI>());
        }

        private void RefreshLevels()
        {
            foreach (LevelUI level in level_uis)
            {
                level.RefreshLevel();
            }
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            RefreshLevels();
        }

        public static AdventurePanel Get()
        {
            return instance;
        }
    }
}