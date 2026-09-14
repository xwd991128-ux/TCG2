using UnityEngine;

namespace TcgEngine.VFX
{
    /// <summary>
    /// 弹道飞行（起点 → 终点）：
    /// 把特效从"起点锚点"沿缓动曲线移动到"终点锚点"；两端锚点是活的 Transform，
    /// 所以卡/地块移动时轨迹会跟着更新（不会飞到空气里）。
    ///
    /// 只负责**位移**：帧动画由 SpriteAnimationPlayer 驱动，缩放曲线同理，互不干扰。
    /// 用 unscaledDeltaTime 推进：与播放器一致，暂停（timeScale=0）时轨迹仍按真实时间前进。
    /// </summary>
    public class VFXTravel : MonoBehaviour
    {
        public Transform from_tf;
        public Transform to_tf;
        public Vector3 from_world;
        public Vector3 to_world;
        public float duration = 0.6f;
        public CurveData curve;

        private float elapsed;
        private bool done;

        public void Setup(Transform from, Transform to, Vector3 from_pos, Vector3 to_pos, float dur, CurveData c)
        {
            from_tf = from;
            to_tf = to;
            from_world = from_pos;
            to_world = to_pos;
            duration = dur > 0.001f ? dur : 0.6f;
            curve = c;
            elapsed = 0f;
            done = false;
            transform.position = from_pos;
        }

        /// <summary>飞行进度 0~1（供外部/诊断读取）</summary>
        public float Progress
        {
            get { return duration > 0.0001f ? Mathf.Clamp01(elapsed / duration) : 1f; }
        }

        private void Update()
        {
            if (done)
                return;

            Vector3 a = from_tf != null ? from_tf.position : from_world;
            Vector3 b = to_tf != null ? to_tf.position : to_world;
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            float e = curve != null ? Mathf.Clamp01(curve.Evaluate(p)) : p;
            transform.position = Vector3.Lerp(a, b, e);
            if (p >= 1f)
                done = true;      //到位后停止写入位置，让特效安稳停在终点（仍可跟随终点锚点）
        }
    }
}
