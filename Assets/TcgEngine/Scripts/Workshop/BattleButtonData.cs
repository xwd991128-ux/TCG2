using System;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 战斗界面自定义按钮定义（全局按钮，不属于某张卡）。
    /// 显示内容（title/desc）由按钮编辑器配置，触发动作由全局按钮图（BattleButtonConfig.graph）配置，
    /// 图内用「点击按钮时」事件节点的 button_id 字段区分不同按钮（一图多按钮分支）。
    /// </summary>
    [Serializable]
    public class BattleButtonData
    {
        public string id;      // 按钮唯一标识（规则图 button_id 字段匹配用）
        public string title;   // 按钮显示文本
        public string desc;    // 描述（可选）
    }

    /// <summary>
    /// 全局按钮配置（buttons.json 根对象）：按钮定义列表 + 全局共享按钮图。
    /// </summary>
    [Serializable]
    public class BattleButtonConfig
    {
        public string timestamp;                                 // 保存时间（供排查用）
        public BattleButtonData[] buttons = new BattleButtonData[0];  // 按钮定义列表
        public GraphData graph;                                  // 一图多按钮：全局共享按钮图
    }
}
