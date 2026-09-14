using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using TcgEngine.UI;   //MainMenu（开局入口）/ HomePanel（菜单注入）

namespace TcgEngine.Client
{
    /// <summary>
    /// 局域网对战（房主直连）流程封装：复用现有 Netcode + TcgTransport + ServerManagerLocal，不新增任何网络库。
    ///
    /// 模式映射（关键：完全复用 GameSettings 里已有的两种类型，不改 GameClient / ServerManagerLocal / TcgNetwork）：
    ///   房主：GameType.HostP2P     → GameClient.ConnectToServer() 走 TcgNetwork.StartHost(port)
    ///                              → 游戏场景里的 ServerManagerLocal 检测到 IsHost() 就地开一个本地 GameServer（Solo 已验证）
    ///   房客：GameType.Multiplayer → GameClient.ConnectToServer() 走 TcgNetwork.StartClient(房主IP, port)
    ///                              → 直连房主，不经过中央匹配服务器（绕过 GameClientMatchmaker 的固定 URL）
    ///
    /// 只做"局域网入口 + 匹配旁路"：天梯/在线匹配（StartMatchmaking）路径完全不动。
    /// 平台：PC 为主（WebGL 不支持 StartHost，面板会给出提示）。
    /// </summary>
    public static class GameClientLAN
    {
        /// <summary>局域网房间标识：房主/房客填同一个即可。
        /// （ServerManagerLocal 只按 user_id 与人数接纳玩家，不校验 game_uid，这里统一填只为日志可读）</summary>
        public const string RoomUID = "lan_room";

        /// <summary>第一版人数上限：2 人（房主为玩家 0，房客依次分配 1、2…）</summary>
        public const int MaxPlayers = 2;

        /// <summary>是否处于局域网会话（用于断线兜底判定与菜单状态清理）</summary>
        public static bool IsLanSession { get; private set; }

        /// <summary>本次局域网会话中，本机是否是房主</summary>
        public static bool IsLanHost { get; private set; }

        /// <summary>WebGL 无法开房（UnityTransport 不支持 StartHost），仅可做房客</summary>
        public static bool CanHost
        {
            get { return Application.platform != RuntimePlatform.WebGLPlayer; }
        }

        /// <summary>端口：沿用 NetworkData.port（房主与房客必须一致）</summary>
        public static ushort GetPort()
        {
            NetworkData nd = NetworkData.Get();
            return nd != null ? nd.port : (ushort)7777;
        }

        /// <summary>
        /// 局域网对局场景：固定取 arena_list[0]。
        /// 必须"双方算出同一个值"——GameSettings.GetScene() 在 scene 为空时会随机取地图，
        /// 房主与房客各自随机 → 会加载到不同场景，所以这里显式指定且不做随机。
        /// </summary>
        public static string GetLanScene()
        {
            GameplayData gd = GameplayData.Get();
            if (gd != null && gd.arena_list != null && gd.arena_list.Length > 0 && !string.IsNullOrEmpty(gd.arena_list[0]))
                return gd.arena_list[0];
            return "Game";
        }

        /// <summary>本机局域网 IPv4 列表（优先 192.168/10./172.16-31 网段；过滤回环与 169.254 自动私有地址）</summary>
        public static List<string> GetLocalIPv4()
        {
            List<string> found = new List<string>();
            try
            {
                NetworkInterface[] ifaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface ni in ifaces)
                {
                    if (ni == null || ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    IPInterfaceProperties props = ni.GetIPProperties();
                    if (props == null)
                        continue;

                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua == null || ua.Address == null)
                            continue;
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;   //只要 IPv4
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("127.") || ip.StartsWith("169.254."))
                            continue;   //回环 / 自动私有地址（连不通）
                        if (!found.Contains(ip))
                            found.Add(ip);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[局域网] 读取本机 IP 失败（不影响手填 IP）: " + e.Message);
            }

            //排序：常见家用/办公网段优先（192.168 > 10.0 > 172.16-31 > 其它）
            found.Sort((a, b) => IpScore(b).CompareTo(IpScore(a)));
            if (found.Count == 0)
                found.Add("127.0.0.1");   //兜底（同机双开测试可用）
            return found;
        }

        private static int IpScore(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return 0;
            if (ip.StartsWith("192.168."))
                return 3;
            if (ip.StartsWith("10."))
                return 2;
            if (ip.StartsWith("172."))
                return 1;
            return 0;
        }

        /// <summary>
        /// 确保本机有可用网络身份。
        /// 局域网按需求强制走本地鉴权（AuthenticatorLocal「本地点名」即可，不做真实登录）。
        /// 若 NetworkData.auth_type 仍是 Api（在线账号）模式，则不旁路登录，直接给出提示，避免拿到空身份被房主拒绝连接。
        /// </summary>
        public static bool EnsureLocalIdentity(out string error)
        {
            error = null;
            Authenticator auth = Authenticator.Get();
            if (auth == null)
            {
                error = "网络未初始化：当前场景缺少 TcgNetwork（请从主菜单进入）";
                return false;
            }

            NetworkData nd = NetworkData.Get();
            bool local_auth = nd == null || nd.auth_type == AuthenticatorType.LocalSave;
            if (!local_auth)
            {
                if (!auth.IsSignedIn())
                {
                    error = "当前是「在线账号」鉴权模式：请先登录，或把 NetworkData 的 auth_type 改成 LocalSave（局域网）";
                    return false;
                }
                return true;
            }

            if (auth.IsSignedIn() && !string.IsNullOrEmpty(auth.UserID))
                return true;

            //本地点名：优先沿用上次的名字（存档/卡组一致），否则生成一个
            string name = PlayerPrefs.GetString("tcg_user", "");
            if (string.IsNullOrEmpty(name))
                name = "LanPlayer" + UnityEngine.Random.Range(1000, 9999);
            auth.LoginTest(name);
            Debug.Log("[局域网] 使用本地身份：" + name);
            return true;
        }

        /// <summary>确保有可用卡组：主菜单当前选中的卡组 → player_settings → 测试卡组兜底</summary>
        public static bool EnsureDeck(out string error)
        {
            error = null;

            MainMenu mm = MainMenu.Get();
            if (mm != null && mm.deck_selector != null)
            {
                UserDeckData picked = mm.deck_selector.GetDeck();
                if (picked != null && picked.IsValid())
                {
                    GameClient.player_settings.deck = picked;
                    return true;
                }
            }

            if (GameClient.player_settings.HasDeck() && GameClient.player_settings.deck.IsValid())
                return true;

            GameplayData gd = GameplayData.Get();
            if (gd != null && gd.test_deck != null)
            {
                GameClient.player_settings.deck = new UserDeckData(gd.test_deck);
                Debug.LogWarning("[局域网] 未选择卡组 → 回退测试卡组：" + gd.test_deck.id);
                return true;
            }

            error = "没有可用卡组：请先回主菜单选择或构筑一套卡组";
            return false;
        }

        /// <summary>把本地身份/头像/卡背补进 player_settings（会随 PlayerSettings 发给房主，用于对局内显示名字与外观）</summary>
        private static void FillProfile()
        {
            Authenticator auth = Authenticator.Get();
            if (auth == null)
                return;
            if (string.IsNullOrEmpty(GameClient.player_settings.username))
                GameClient.player_settings.username = auth.Username;
            UserData udata = auth.UserData;
            if (udata != null)
            {
                if (string.IsNullOrEmpty(GameClient.player_settings.avatar))
                    GameClient.player_settings.avatar = udata.GetAvatar();
                if (string.IsNullOrEmpty(GameClient.player_settings.cardback))
                    GameClient.player_settings.cardback = udata.GetCardback();
            }
        }

        /// <summary>当前本机身份名（局域网下 = 本地鉴权里的 username，也是房主识别玩家的 key）</summary>
        public static string GetIdentityName()
        {
            Authenticator auth = Authenticator.Get();
            return auth != null ? auth.Username : "";
        }

        /// <summary>
        /// 【同机双开专用】换一个本地点名身份（只改内存，不写 PlayerPrefs、不动存档文件）：
        /// 同一台机器上两个实例共用 PlayerPrefs["tcg_user"]，默认会拿到同一个 user_id；
        /// 而 GameServer 是**按 user_id 认玩家**的（FindPlayerID）→ 两个实例会都被分配成同一位玩家（都是玩家0），
        /// 表现为"房客连上了但永远不开局、10 秒后被弹回大厅"。换身份后两个实例的 user_id 不同即可正常对战。
        /// </summary>
        public static bool SwitchTestIdentity(out string error)
        {
            error = null;
            Authenticator auth = Authenticator.Get();
            if (auth == null)
            {
                error = "网络未初始化：当前场景缺少 TcgNetwork";
                return false;
            }
            NetworkData nd = NetworkData.Get();
            if (nd != null && nd.auth_type != AuthenticatorType.LocalSave)
            {
                error = "当前是「在线账号」模式：同机双开请给两个实例各登录一个不同账号";
                return false;
            }

            string name = "LanPlayer" + UnityEngine.Random.Range(1000, 9999);
            auth.LoginTest(name);                          //只改内存身份（不 Churn PlayerPrefs / 存档）
            GameClient.player_settings.username = name;    //强制同步（FillProfile 只在为空时填，这里必须覆盖）
            Debug.Log("[局域网] 已切换本机身份：" + name);
            return true;
        }

        /// <summary>开房（房主=服务器+玩家0）。成功后进入对局场景等待房客加入。</summary>
        public static bool StartHost(out string error)
        {
            IsLanSession = false;
            IsLanHost = false;

            if (!CanHost)
            {
                error = "WebGL 不支持开房：局域网开房仅支持 PC 端";
                return false;
            }
            if (!EnsureLocalIdentity(out error) || !EnsureDeck(out error))
                return false;
            FillProfile();

            GameSettings settings = GameSettings.Default;   //干净起步，避免残留匹配/观察者状态
            settings.game_type = GameType.HostP2P;          //IsHost()=true → StartHost + 本地 ServerManagerLocal
            settings.game_mode = GameMode.Casual;
            settings.server_url = "127.0.0.1";              //房主不用它连接，仅日志可读
            settings.game_uid = RoomUID;
            settings.nb_players = MaxPlayers;
            settings.scene = GetLanScene();                 //双方必须一致
            GameClient.game_settings = settings;

            IsLanSession = true;
            IsLanHost = true;
            LanSessionGuard.Ensure();                       //断线兜底（跨场景常驻）
            Debug.Log("[局域网] 开房：port=" + GetPort() + " 地图=" + settings.scene);
            EnterGame();
            return true;
        }

        /// <summary>加入房主（房客=客户端）。host_ip 可以是 IPv4 或主机名（TcgTransport 内部会解析）。</summary>
        public static bool StartJoin(string host_ip, out string error)
        {
            error = null;
            IsLanSession = false;
            IsLanHost = false;

            string ip = (host_ip ?? "").Trim();
            if (string.IsNullOrEmpty(ip))
            {
                error = "请填写房主 IP";
                return false;
            }
            if (!EnsureLocalIdentity(out error) || !EnsureDeck(out error))
                return false;
            FillProfile();

            GameSettings settings = GameSettings.Default;
            settings.game_type = GameType.Multiplayer;      //IsHost()=false → StartClient(房主IP)
            settings.game_mode = GameMode.Casual;
            settings.server_url = ip;                       //GetUrl() 返回它 → 直连房主，绕过匹配服务器
            settings.game_uid = RoomUID;
            settings.nb_players = MaxPlayers;
            settings.scene = GetLanScene();                 //与房主一致
            GameClient.game_settings = settings;

            IsLanSession = true;
            IsLanHost = false;
            LanSessionGuard.Ensure();
            Debug.Log("[局域网] 加入房主：" + ip + ":" + GetPort() + " 地图=" + settings.scene);
            EnterGame();
            return true;
        }

        /// <summary>结束局域网会话（回到大厅时调用）：清标记 + 撤掉断线守卫</summary>
        public static void EndSession()
        {
            IsLanSession = false;
            IsLanHost = false;
            LanSessionGuard.Dispose();
        }

        /// <summary>进入对局场景：复用 MainMenu.StartGame（内含断开局内匹配 + 黑幕淡出 + 切场景）</summary>
        private static void EnterGame()
        {
            MainMenu mm = MainMenu.Get();
            if (mm != null)
                mm.StartGame(GameClient.game_settings.game_uid, GameClient.game_settings.server_url);
            else
                SceneNav.GoTo(GameClient.game_settings.GetScene());   //没有主菜单（直接跑游戏场景）时兜底
        }
    }

    /// <summary>
    /// 局域网对局期间的断线兜底（跨场景常驻）：
    /// 连接建立后开始监视，一旦与对端断开 → 提示并退回大厅，避免房客卡在加载界面或被 GameClient 的
    /// 自动重连逻辑无限重试。只在局域网会话里工作（GameClientLAN.IsLanSession），不影响在线匹配/ Solo。
    /// </summary>
    public class LanSessionGuard : MonoBehaviour
    {
        private const float GraceTime = 1.5f;   //给"正常退出对局（先 Disconnect 再切场景）"留缓冲

        private static LanSessionGuard instance;

        private bool armed;
        private float grace;
        private float diag_timer;
        private bool diag_done;

        public static void Ensure()
        {
            if (instance != null)
                return;
            GameObject go = new GameObject("LanSessionGuard");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LanSessionGuard>();
        }

        public static void Dispose()
        {
            if (instance != null)
            {
                Destroy(instance.gameObject);
                instance = null;
            }
        }

        private void Update()
        {
            if (!GameClientLAN.IsLanSession)
                return;

            TcgNetwork net = TcgNetwork.Get();
            if (net == null)
                return;

            if (!armed)
            {
                //在"真的连上了"之后才武装：避免把离开菜单时的主动断开误判成掉线
                if (net.IsConnected() && !net.IsConnecting())
                {
                    armed = true;
                    grace = 0f;
                }
                return;
            }

            if (net.IsConnected())
            {
                grace = 0f;
                DiagnoseDuplicateIdentity();
                return;
            }

            grace += Time.unscaledDeltaTime;
            if (grace < GraceTime)
                return;

            Debug.LogWarning("[局域网] 与" + (GameClientLAN.IsLanHost ? "房客" : "房主") + "的连接已断开，返回大厅");
            GameClientLAN.EndSession();
            SceneNav.GoTo("Menu");
        }

        /// <summary>
        /// 房客侧诊断：已连上房主，但长时间停在 Connecting 且自己被分配成「玩家0」——
        /// 房主（先连的人）一定是玩家0，所以这说明两个实例的 user_id 相同（同机双开时 PlayerPrefs 共用同一个名字）。
        /// 只报一次明确原因，避免用户以为是"网络连不通"。
        /// </summary>
        private void DiagnoseDuplicateIdentity()
        {
            if (diag_done || GameClientLAN.IsLanHost)
                return;

            GameClient gc = GameClient.Get();
            if (gc == null)
                return;
            Game gdata = gc.GetGameData();
            if (gdata == null || gdata.state != GameState.Connecting)
                return;

            diag_timer += Time.unscaledDeltaTime;
            if (diag_timer < 2f)
                return;

            diag_done = true;
            if (gc.GetPlayerID() == 0)
                Debug.LogError("[局域网] 已连上房主，但本机被分配为「玩家0」（房主的座位）→ 两个实例用的是同一个账号名 " +
                    "（同机双开时 PlayerPrefs 会共用同一个 tcg_user）。请在「局域网对战」面板点「换个身份」后重新加入，" +
                    "或用另一个账号登录后再加入。");
        }
    }
}
