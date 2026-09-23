using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 一个额外区（例如炉石 ETC 的"乐队"）：主卡之外独立存储、独立统计的一组卡。
    ///
    /// 说明：本阶段额外区是「数据 + 校验 + UI 统计」形式存在；对局中的运行时区域
    /// （从额外区拉取）按计划放在后续阶段单独做，避免同时改动引擎的区域模型。
    /// </summary>
    [System.Serializable]
    public class DeckZone
    {
        public string id;               //唯一 id，如 "band"
        public string title;            //中文名，如 "乐队"
        public string desc;

        [Header("数量规则")]
        public int min_count;           //最少几张
        public int max_count = 3;       //最多几张
        public int per_card_max = 1;    //同一个卡 id 最多几张

        [Header("必填")]
        public bool required;           //来源卡带了这个区就必须凑满 min_count（如 ETC 卡强制生成必填乐队区）
    }
}
