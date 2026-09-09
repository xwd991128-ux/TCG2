using TcgEngine.Gameplay;

namespace TcgEngine.Workshop
{
    /// <summary>图事件广播的阶段：Before=「X 时」（动作前，可阻止/改值）；After=「X 后」（动作后，只读）</summary>
    public enum GraphEventPhase
    {
        Before,
        After,
    }

    /// <summary>
    /// 一次图事件广播的上下文（全场监听）。由 GameLogic 的桩点在动作前/后构造并通过 EmitGraphEvent 广播，
    /// 广播期间 NodeDocRunner 把它作为当前事件上下文（cur_event），供事件入口端口（self/subject/source/value/player/enemy）取值，
    /// 以及「阻止本事件」「修改事件值」动作读写。
    /// 不序列化、不进存档，仅运行期存在于服务器侧 GameLogic（客户端无需感知，表现随广播事件同步）。
    /// </summary>
    public class GraphEventContext
    {
        public string action;          // 入口 action（=AbilityTrigger 枚举名：OnBeforePlay/OnBeforeDamage/OnAfterDamage/OnAfterDraw），用于匹配图入口事件节点
        public GraphEventPhase phase;  // 时 / 后
        public Card card;              // 事件主体卡（被使用的牌 / 受伤的卡 / 抽到的卡），可为空
        public Card source_card;       // 来源卡（伤害来源等），可为空
        public Player player;          // 主体玩家（抽牌玩家等），可为空
        public int value;              // 事件数值（伤害量 / 抽卡批次等；Before 事件下可被「修改事件值」改写并生效）
        public bool cancelled;         // 已被「阻止本事件」置位（仅 Before 事件有效）
    }
}
