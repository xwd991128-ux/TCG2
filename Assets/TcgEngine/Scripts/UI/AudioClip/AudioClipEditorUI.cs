using System;
using UnityEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// 音效DIY入口组件：挂在"音效位"按钮（或任意可点对象）上。
    /// 职责：持有当前加工结果 resultClip、打开编辑弹框、把确认后的结果通过 onAudioChanged 回写宿主（卡牌）。
    ///
    /// 生命周期：
    ///   首次点击 → Open()（没有素材，弹框提示先导入）
    ///   确定     → popup 回调 ApplyResult() → onAudioChanged(结果)
    ///   取消     → 丢弃本次改动，resultClip 不变
    ///   重新打开 → 以"上次保存结果"作为编辑素材（无结果则用原始素材）
    /// </summary>
    public class AudioClipEditorUI : MonoBehaviour
    {
        public AudioClipEditorPopupUI popup;    //可空，运行时自建
        public string slot_label = "音效";       //槽位名（界面标题用：打出/攻击/死亡/受伤）
        public int slot_index = -1;              //槽位序号（0打出 1攻击 2死亡 3受伤；-1=未指定）

        public AudioClip resultClip;             //当前加工结果（=已回写卡牌的音频；null 表示该槽为空）
        public AudioClip sourceClip;             //本次编辑使用的素材（上次结果或原始导入）
        public string source_file;               //素材来源文件名/路径（界面展示）

        /// <summary>结果变化回调（null = 清空该音效位）</summary>
        public event Action<AudioClip> onAudioChanged;

        public void Open()
        {
            EnsurePopup();
            AudioClip edit = resultClip != null ? resultClip : sourceClip;
            popup.Open(this, edit, source_file, SlotTitle());
        }

        public void Close()
        {
            if (popup != null)
                popup.OnClickCancel();
        }

        /// <summary>清空该音效位（回写 null）并收起弹框</summary>
        public void ClearClip()
        {
            resultClip = null;
            if (onAudioChanged != null)
                onAudioChanged(null);
        }

        /// <summary>打开前载入"当前已保存的音效"（首次为空时不调用或传 null）</summary>
        public void SetCurrentClip(AudioClip clip, string file)
        {
            resultClip = clip;
            if (clip != null)
            {
                sourceClip = clip;
                source_file = file;
            }
        }

        /// <summary>弹框"确定"回调：记录结果并回写</summary>
        public void ApplyResult(AudioClip result, AudioClip source, string src_file)
        {
            resultClip = result;
            if (source != null)
                sourceClip = source;
            source_file = src_file;
            if (onAudioChanged != null)
                onAudioChanged(result);
        }

        public void EnsurePopup()
        {
            if (popup == null)
                popup = AudioClipEditorPopupUI.Create(transform);
        }

        private string SlotTitle()
        {
            if (!string.IsNullOrEmpty(slot_label))
                return slot_label;
            switch (slot_index)
            {
                case 0: return "打出音效";
                case 1: return "攻击音效";
                case 2: return "死亡音效";
                case 3: return "受伤音效";
                default: return "音效";
            }
        }
    }
}
