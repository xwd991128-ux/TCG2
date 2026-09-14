using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.VFX
{
    /// <summary>
    /// 战斗内特效运行时（**仅客户端表现层**，不参与对局逻辑）：
    /// 按配置在"目标卡 / 施法者 / 场景固定点"上生成序列帧特效（SpriteRenderer），按生命周期自动销毁。
    ///
    /// 隔离与安全：
    /// ① 不在对战场景（`GameBoard.Get() == null`，例如服务器/无头端）时**直接忽略**，不产生任何对象；
    /// ② 特效对象是临时对象：父物体为卡/槽（跟随移动），随场景销毁，不留存任何数据；
    /// ③ 同时存在实例数有上限（超过则跳过并提示一次），防止卡顿；
    /// ④ 未配置特效的节点不会调用到这里，零开销。
    /// </summary>
    public static class VFXRuntime
    {
        /// <summary>同场景同时存在的特效率上限（超过则跳过新特效并警告一次）</summary>
        public const int MaxActive = 24;

        private static readonly List<SpriteAnimationPlayer> active = new List<SpriteAnimationPlayer>();
        private static VFXRunner runner;
        private static bool cap_warned;

        /// <summary>是否处于对战表现场景（项目既有判定方式：棋盘对象存在）。非主线程一律返回 false（禁止跨线程访问 Unity 对象）</summary>
        public static bool IsClientView
        {
            get
            {
                if (!MainThreadUtil.IsMainThread)
                    return false;
                try
                {
                    return TcgEngine.Client.GameBoard.Get() != null;
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }

        public static int ActiveCount
        {
            get
            {
                Prune();
                return active.Count;
            }
        }

        /// <summary>触发一次特效（节点执行/事件触发时调用）。不安全/未配置/非对战场景/非主线程时静默返回。</summary>
        public static void Trigger(VFXConfig cfg, Card caster, Card target_card, Player target_player, string label = null)
        {
            if (cfg == null || !cfg.IsConfigured)
            {
                Diag("unconfigured", "[VFX] 不出特效：该节点未配置特效或未选素材（节点=" + (label ?? "?") + "）");
                return;   //纯数据判断，后台线程也安全
            }

            //★ 必须在访问任何 Unity API（GameBoard/transform…）之前判定主线程：
            //  NodeDoc 图会被 AI 推演线程执行（AILogic 后台线程），在那种线程里碰 transform 会抛
            //  UnityException 并被外层当成"推演线程异常"终止 AI 计算。AI 预测阶段无需出特效。
            if (!MainThreadUtil.IsMainThread)
            {
                Diag("thread", "[VFX] 不出特效：当前不在主线程（AI 推演阶段）节点=" + (label ?? "?"));
                return;
            }

            if (!IsClientView)
            {
                Diag("view", "[VFX] 不出特效：当前不在对战表现场景（GameBoard 不存在）节点=" + (label ?? "?"));
                return;   //服务器/非战斗场景：不产生表现层对象
            }

            //起点锚点：弹道开启时 bind = 起点，否则就是特效本身的落点
            ResolveBindContext(cfg.bind, caster, target_card, target_player, out Card bind_card, out Player bind_player);
            Transform parent = ResolveParent(cfg.bind, bind_card, bind_player);
            if (parent == null)
            {
                Diag("parent", "[VFX] 不出特效：找不到绑定对象（bind=" + cfg.bind + " 目标卡="
                    + (bind_card != null ? bind_card.uid : "null") + "）节点=" + (label ?? "?"));
                return;
            }

            //终点锚点（弹道飞行用）：bind_to 决定的卡/玩家
            Transform end_parent = null;
            if (cfg.travel)
            {
                ResolveBindContext(cfg.bind_to, caster, target_card, target_player, out Card end_card, out Player end_player);
                end_parent = ResolveParent(cfg.bind_to, end_card, end_player);
                if (end_parent == null)
                    Diag("travel_end", "[VFX] 弹道终点解析不到，按原地播放处理（bind_to=" + cfg.bind_to + "）节点=" + (label ?? "?"));
            }

            Prune();
            if (active.Count >= MaxActive)
            {
                if (!cap_warned)
                {
                    cap_warned = true;
                    Debug.LogWarning("[VFX] 同时特效数量已达上限 " + MaxActive + "，后续特效被跳过（可减少循环特效或缩短时长）");
                }
                return;
            }
            cap_warned = false;
            EnsureRunner().StartCoroutine(SpawnRoutine(cfg, parent, end_parent, label));
        }

        /// <summary>把「绑定方式 + 目标上下文」解析成实际对象：
        /// TargetCard=被选目标（为空回退施法卡）/ Caster=施法卡 / WorldFixed=不绑对象。
        /// 英雄卡（英雄=卡牌）换算成其所属玩家（走玩家地块）。</summary>
        private static void ResolveBindContext(VFXBind bind, Card caster, Card target_card, Player target_player,
            out Card card, out Player player)
        {
            card = null;
            player = null;
            if (bind == VFXBind.WorldFixed)
                return;
            card = bind == VFXBind.Caster ? caster : (target_card != null ? target_card : caster);
            player = target_player;
            if (card != null && card.CardData != null && card.CardData.type == CardType.Hero)
                player = GetPlayerOf(card);   //英雄=卡牌：英雄走玩家地块
        }

        /// <summary>清理残留在场景里的特效与特效音（切换场景/结束战斗时可调）</summary>
        public static void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] != null)
                    Object.Destroy(active[i].gameObject);
            }
            active.Clear();
            VFXAudio.StopAll();   //音效源池不随特效销毁，这里一并静音（否则切场景会残留尾音）
        }

        // ---------------- 内部 ----------------

        private static IEnumerator SpawnRoutine(VFXConfig cfg, Transform parent, Transform end_parent, string label)
        {
            if (cfg.delay > 0.01f)
                yield return new WaitForSeconds(cfg.delay);

            if (parent == null)
                yield break;
            if (active.Count >= MaxActive)
                yield break;

            List<Sprite> frames = VFXFrameLibrary.ResolveFrames(cfg);
            if (frames.Count == 0)
            {
                Debug.LogWarning("[VFX] 特效帧解析为空（来源=" + cfg.frame_source + "，帧数=" +
                    (cfg.frame_names != null ? cfg.frame_names.Count : 0) + "）→ 跳过播放：" + (label ?? ""));
                yield break;
            }

            GameObject go = new GameObject("VFX_" + (string.IsNullOrEmpty(label) ? "fx" : label));
            go.transform.SetParent(parent, false);

            //锚点：把特效**中心**偏移到绑定对象的 中心/下缘/上缘/左缘/右缘。
            //★ 符号曾经写反（Bottom 用了 +size.y*0.5 → 实际跑到顶部，Top/Left/Right 同理），此处已修正。
            //弹道模式下两端用的是锚点原点，不再叠加锚点偏移（否则起点终点会各偏一截）。
            Vector2 pivot_shift = Vector2.zero;
            if (!cfg.travel && frames.Count > 0 && frames[0] != null)
            {
                Vector2 size = frames[0].bounds.size;
                switch (cfg.pivot)
                {
                    case VFXPivot.Bottom: pivot_shift = new Vector2(0f, -size.y * 0.5f); break;   //放到绑定对象下缘
                    case VFXPivot.Top: pivot_shift = new Vector2(0f, size.y * 0.5f); break;        //放到绑定对象上缘
                    case VFXPivot.Left: pivot_shift = new Vector2(-size.x * 0.5f, 0f); break;      //放到绑定对象左缘
                    case VFXPivot.Right: pivot_shift = new Vector2(size.x * 0.5f, 0f); break;      //放到绑定对象右缘
                }
            }

            //局部偏移：跟随绑定对象（卡移动时特效跟随），上下左右即父物体的局部 X/Y
            go.transform.localPosition = new Vector3(cfg.offset.x + pivot_shift.x, cfg.offset.y + pivot_shift.y, 0f);
            go.transform.localRotation = Quaternion.identity;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();

            //排序：优先"与场上卡同层、压在卡之上"。
            //原因：父级可能是**玩家地块**（BoardSlotPlayer），它自带的 SpriteRenderer 是"可攻击高亮背景"，
            //序通常很低（甚至为负）；直接 parent.order + 3 会让特效被棋盘背景/卡面挡住 → 看起来"没出特效"。
            int s_layer = 0;
            int s_order = 200;
            SpriteRenderer ref_sr = null;
            TcgEngine.Client.BoardCard bcard_parent = parent.GetComponentInParent<TcgEngine.Client.BoardCard>();
            if (bcard_parent != null)
                ref_sr = bcard_parent.card_sprite;
            if (ref_sr == null)
            {
                List<TcgEngine.Client.BoardCard> all_cards = TcgEngine.Client.BoardCard.GetAll();
                for (int i = 0; i < all_cards.Count; i++)
                {
                    if (all_cards[i] != null && all_cards[i].card_sprite != null)
                    {
                        ref_sr = all_cards[i].card_sprite;
                        break;
                    }
                }
            }
            if (ref_sr != null)
            {
                s_layer = ref_sr.sortingLayerID;
                s_order = ref_sr.sortingOrder + 30;      //同层且明显高于卡
            }
            else
            {
                SpriteRenderer parent_sr = parent.GetComponentInChildren<SpriteRenderer>();
                if (parent_sr != null)
                {
                    s_layer = parent_sr.sortingLayerID;
                    s_order = Mathf.Max(parent_sr.sortingOrder + 3, 100);   //保底高序，避免被背景盖住
                }
            }
            sr.sortingLayerID = s_layer;
            sr.sortingOrder = s_order;

            //缩放补偿：父级（玩家地块常带缩放，用来把 1 单位贴图铺满整块区域）会把特效一起缩放 → 大小失控。
            //这里按父级 lossyScale 反向抵消，使特效的**世界尺寸只由素材（单帧像素 / PPU × 缩放倍率）决定**。
            Vector3 pscale = parent.lossyScale;
            Vector2 compens = new Vector2(
                Mathf.Abs(pscale.x) > 0.0001f ? 1f / pscale.x : 1f,
                Mathf.Abs(pscale.y) > 0.0001f ? 1f / pscale.y : 1f);

            SpriteAnimationPlayer player = go.AddComponent<SpriteAnimationPlayer>();
            player.pingpong = false;
            player.base_scale = compens;
            player.Setup(cfg, frames, sr, null);
            active.Add(player);
            player.Play();

            //音效：与动画同时开始（解码异步，首次可能晚几十毫秒；成功后进缓存即点即响）
            if (cfg.HasAudio)
                VFXAudio.Play(cfg.audio_path, cfg.audio_volume, cfg.audio_loop);

            //弹道飞行：从起点锚点飞向终点锚点（两端是活 Transform，卡/地块移动时轨迹跟随更新）
            float anim_life = cfg.loop
                ? (cfg.loop_duration > 0.01f ? cfg.loop_duration : 10f)
                : cfg.Duration;
            float travel_dur = 0f;
            if (cfg.travel && end_parent != null)
            {
                travel_dur = cfg.travel_duration > 0.01f ? cfg.travel_duration : anim_life;
                VFXTravel tv = go.AddComponent<VFXTravel>();
                tv.Setup(parent, end_parent, parent.position, end_parent.position, travel_dur,
                    cfg.travel_curve != null ? cfg.travel_curve : VFXConfig.TravelPreset("linear"));
            }

            //存活时长：循环=loop_duration；弹道时至少要飞完全程
            float life = cfg.travel ? Mathf.Max(anim_life, travel_dur) : anim_life;

            Diag("spawn", "[VFX] 播放特效：" + frames.Count + " 帧 / " + cfg.frame_rate + "fps · 时长 " + life.ToString("0.00") + "s"
                + " · 挂点=" + parent.name + " · 绑定=" + cfg.bind + " · 节点=" + (label ?? "?")
                + " · 排序=" + s_order + "(层" + s_layer + ")"
                + " · 父级缩放=" + pscale.x.ToString("0.##") + "," + pscale.y.ToString("0.##")
                + " · 单帧世界尺寸≈" + (frames[0] != null
                    ? (frames[0].bounds.size.x.ToString("0.00") + "单位（缩放曲线再乘 0~1）")
                    : "?")
                + (cfg.travel ? " · 弹道→" + (end_parent != null ? end_parent.name : "无") + " 飞行" + travel_dur.ToString("0.00") + "s" : "")
                + (cfg.HasAudio ? " · 音效=" + System.IO.Path.GetFileName(cfg.audio_path) : "")
                + " · 世界坐标=" + go.transform.position.ToString("0.0"));

            yield return new WaitForSeconds(life);

            if (player != null)
            {
                player.Stop();
                Object.Destroy(go);
            }
            active.Remove(player);
        }

        private static Transform ResolveParent(VFXBind bind, Card card, Player player)
        {
            //场景固定：挂到棋盘（局部偏移即棋盘坐标系下的上下左右）
            if (bind == VFXBind.WorldFixed)
            {
                TcgEngine.Client.GameBoard board = TcgEngine.Client.GameBoard.Get();
                return board != null ? board.transform : null;
            }

            //目标卡/施法者：优先战场卡对象
            if (card != null)
            {
                TcgEngine.Client.BoardCard bc = TcgEngine.Client.BoardCard.Get(card.uid);
                if (bc != null)
                    return bc.transform;
            }
            //英雄/玩家目标：走玩家地块。
            //补充：绑定卡不在战场上时（典型情况：「目标卡牌」口接成了手牌/弃牌里的法术卡本身，或目标为空而回退到施法卡），
            //退回该卡拥有者的玩家地块 —— 否则会挂到棋盘根节点，特效"在场上但看不见"。
            if (player == null && card != null)
                player = GetPlayerOf(card);
            if (player != null)
            {
                TcgEngine.Client.BoardSlotPlayer sp = TcgEngine.Client.BoardSlotPlayer.Get(player.player_id);
                if (sp != null)
                    return sp.transform;
            }
            //兜底：棋盘
            TcgEngine.Client.GameBoard gb = TcgEngine.Client.GameBoard.Get();
            return gb != null ? gb.transform : null;
        }

        // ---------------- 诊断（排查"特效不显示"用；同一条最多 3 次，排查完可整段删除） ----------------

        private static readonly Dictionary<string, int> diag_counts = new Dictionary<string, int>();

        private static void Diag(string key, string msg, bool warn = false)
        {
            diag_counts.TryGetValue(key, out int n);
            if (n >= 3)
                return;
            diag_counts[key] = n + 1;
            if (warn)
                Debug.LogWarning(msg);
            else
                Debug.Log(msg);
        }

        private static Player GetPlayerOf(Card card)
        {
            try
            {
                return TcgEngine.Client.GameClient.Get().GetGameData().GetPlayer(card.player_id);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static void Prune()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] == null)
                    active.RemoveAt(i);
            }
        }

        private static VFXRunner EnsureRunner()
        {
            if (runner != null)
                return runner;
            GameObject go = new GameObject("VFXRuntime");
            runner = go.AddComponent<VFXRunner>();
            return runner;
        }
    }

    /// <summary>承载协程的隐形宿主（随战斗场景创建/销毁，不做 DontDestroyOnLoad）</summary>
    public class VFXRunner : MonoBehaviour
    {
    }
}
