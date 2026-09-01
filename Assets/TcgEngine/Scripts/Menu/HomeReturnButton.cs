using UnityEngine;
using UnityEngine.UI;

namespace TcgEngine.UI
{
    /// <summary>
    /// 挂在各全屏页面左上角「返回」按钮上：点击回到主菜单首页。
    /// 运行时自动绑定按钮点击事件（与 TabButton 绑定方式一致）。
    /// </summary>
    public class HomeReturnButton : MonoBehaviour
    {
        private void Start()
        {
            Button btn = GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(ReturnHome);
        }

        private void ReturnHome()
        {
            if (HomePanel.Get() != null)
                HomePanel.Get().ReturnHome();
        }
    }
}
