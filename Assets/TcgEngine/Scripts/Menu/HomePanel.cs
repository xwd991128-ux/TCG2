using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 主菜单首页：顶部玩家信息（头像/用户名/金币）+ 中央 2×3 六大模块入口
    /// （对战/卡牌/卡包/排行榜/卡池/设置）。模块由 TabButton(group=home) 互斥切换，
    /// 各全屏页面通过 HomeReturnButton 调用 ReturnHome() 返回首页。
    /// </summary>
    public class HomePanel : UIPanel
    {
        [Header("顶部信息")]
        public Text username_txt;
        public Text credits_txt;
        public AvatarUI avatar;

        [Header("退出")]
        public Button logout_btn;
        public Button quit_btn;

        private TabButton home_tab;
        private static HomePanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;
            home_tab = GetComponent<TabButton>();

            if (logout_btn != null)
                logout_btn.onClick.AddListener(OnClickLogout);
            if (quit_btn != null)
                quit_btn.onClick.AddListener(OnClickQuit);
        }

        protected override void Start()
        {
            base.Start();
            RefreshUser();
        }

        protected override void Update()
        {
            base.Update(); // 必须调用基类，否则首页 alpha 永远为 0（透明但可点击）
            UserData udata = Authenticator.Get() != null ? Authenticator.Get().UserData : null;
            if (udata != null)
            {
                if (username_txt != null)
                    username_txt.text = udata.username;
                if (credits_txt != null)
                    credits_txt.text = GameUI.FormatNumber(udata.coins);
            }
        }

        private void RefreshUser()
        {
            UserData udata = Authenticator.Get() != null ? Authenticator.Get().UserData : null;
            if (udata != null && avatar != null)
            {
                AvatarData ava = AvatarData.Get(udata.avatar);
                avatar.SetAvatar(ava);
            }
        }

        private void OnClickLogout()
        {
            if (MainMenu.Get() != null)
                MainMenu.Get().OnClickLogout();
        }

        private void OnClickQuit()
        {
            if (MainMenu.Get() != null)
                MainMenu.Get().OnClickQuit();
        }

        /// <summary>各页面「返回」按钮调用：互斥隐藏其它 home 组页面并显示首页</summary>
        public void ReturnHome()
        {
            if (home_tab != null)
                home_tab.Activate();
            else
                Show();
        }

        public static HomePanel Get()
        {
            return instance;
        }
    }
}
