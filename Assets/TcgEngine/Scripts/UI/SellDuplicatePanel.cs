using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

namespace TcgEngine.UI
{

    public class SellDuplicatePanel : UIPanel
    {
        public Text error;

        private static SellDuplicatePanel instance;

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            //★ 导航修复：原来只有「卖掉」才会 Hide()（卖成功才关）→ 不想卖就只能卡在这页。
            EnsureExitButton("取消");
        }

        public async void OnClickSell()
        {
            SellDuplicateRequest req = new SellDuplicateRequest();
            req.keep = 2;

            string url = ApiClient.ServerURL + "/users/cards/sell/duplicate";
            string jdata = ApiTool.ToJson(req);
            error.text = "";

            WebResponse res = await ApiClient.Get().SendPostRequest(url, jdata);
            if (res.success)
            {
                CollectionPanel.Get().ReloadUser();
                Hide();
            }
            else
            {
                error.text = res.error;
            }
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            error.text = "";
        }

        public static SellDuplicatePanel Get()
        {
            return instance;
        }
    }
}