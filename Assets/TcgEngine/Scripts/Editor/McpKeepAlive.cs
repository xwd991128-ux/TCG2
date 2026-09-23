using System.Net;
using System.Net.NetworkInformation;
using UnityEditor;
using UnityEngine;
using Unity.MCP.Editor;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// MCP 连接守护：按固定间隔检查端口，一旦发现监听不在就自动把 MCP 服务器重新拉起来。
    ///
    /// 为什么需要它（这就是"MCP 频繁断开"的根因）：
    ///   插件的自动恢复挂在 EditorApplication.delayCall 上，域重载会丢弃该回调；
    ///   其 `if (this != null)` 守卫又会因为 EditorWindow 实例被重建而失效
    ///   （McpServerWindow.cs:401 / :1014-1030），WasRunning 标记也会被 StopServer() 擦掉。
    ///   结果：每次编译、进出 Play（都会触发域重载）之后监听就没了、且不会自愈，
    ///   只能人工去面板点一次「启动服务器」——IDE 侧看起来就是"连接频繁断开"。
    ///
    /// 做法：只调用插件公开的 McpServerWindow.StartServerStatic()，不修改第三方包
    ///       （包升级/重装都不会被覆盖），并由 EditorApplication.update 周期性检查 + 重连。
    ///
    /// 开关：菜单「TcgEngine/工具/MCP 连接守护」；EditorPrefs: TcgEngine.McpKeepAlive.Enabled（默认开）。
    /// 注意：守护开启时，在面板手动点「停止服务器」会在一个检查周期内被重新拉起；
    ///       要真正停掉服务，请先关掉本守护（菜单项）。
    /// 停用方式：关掉菜单开关，或者直接删除本文件。
    /// </summary>
    [InitializeOnLoad]
    public static class McpKeepAlive
    {
        private const int DefaultPort = 9123;
        private const string MenuPath = "TcgEngine/工具/MCP 连接守护";
        private const string EnabledKey = "TcgEngine.McpKeepAlive.Enabled";
        private const double CheckIntervalSeconds = 10.0;   //正常检查间隔（也是掉线后的重连间隔）
        private const double RetryIntervalSeconds = 30.0;   //启动没成功时的退避间隔，避免反复拉起刷屏

        private static double _nextCheckAt;
        private static bool _logPending = true;             //本次"掉线"还没提示过；恢复后重新武装

        static McpKeepAlive()
        {
            //★ 不用 delayCall（域重载会丢弃它，这正是插件失效的原因）：改成常驻的周期性检查
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextCheckAt)
                return;

            if (!EditorPrefs.GetBool(EnabledKey, true))
            {
                _nextCheckAt = now + CheckIntervalSeconds;   //关着也排下一次，免得重新打开后要等很久
                _logPending = true;
                return;
            }

            int port = EditorPrefs.GetInt("McpServer.Port", DefaultPort);
            if (IsListening(port))
            {
                //★ 域重载后 timeSinceStartup 是连续的、而 _nextCheckAt 归零 → 重载后立刻检查一次
                _nextCheckAt = now + CheckIntervalSeconds;
                _logPending = true;
                return;
            }

            if (_logPending)
            {
                Debug.Log("[McpKeepAlive] MCP 端口 " + port + " 未监听，自动重新启动服务器");
                _logPending = false;
            }
            McpServerWindow.StartServerStatic();
            _nextCheckAt = now + RetryIntervalSeconds;       //给它时间绑定；仍未成的话退避后再试
        }

        //只查监听表，不做真实连接：避免在服务端的「连接的客户端」列表里留下无意义记录
        private static bool IsListening(int port)
        {
            try
            {
                foreach (IPEndPoint ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                {
                    if (ep.Port == port)
                        return true;
                }
            }
            catch
            {
                //查不到监听表时按「未监听」处理，交给 StartServerStatic 自己重试
            }
            return false;
        }

        [MenuItem(MenuPath, false, 100)]
        private static void ToggleKeepAlive()
        {
            bool on = !EditorPrefs.GetBool(EnabledKey, true);
            EditorPrefs.SetBool(EnabledKey, on);
            _nextCheckAt = 0;                                 //立刻生效，不等下一个周期
            Debug.Log("[McpKeepAlive] MCP 连接守护" + (on ? "已开启" : "已关闭"));
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleKeepAliveValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(EnabledKey, true));
            return true;
        }
    }
}
