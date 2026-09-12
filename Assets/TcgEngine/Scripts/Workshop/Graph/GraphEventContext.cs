using System.Collections.Generic;
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
        public int slot = -1;          // 事件主体卡所在牌堆中的位置（slot/第几张；无则 -1）
        public string tags;            // 入口「标签列表」（战吼/亡语等自定义标签，供判断）
        public bool cancelled;         // 已被「阻止本事件」置位（仅 Before 事件有效）
        public Dictionary<string, object> vars;   // 自定义效果属性（事件自定义变量：入口「自定义效果属性」声明，获取/设置变量按名读写）

        // ---- 事件日志/事件家族（108xxx/208009）用元数据；不序列化，仅运行期存在 ----
        public int turn;               // 事件发生的回合数（EmitGraphEvent 落日志时填 game_data.turn_count）
        public int repeat = 1;         // 事件重复次数（默认 1；208009 可改，供 108010 读）
        public GraphEventContext parent;   // 父事件（广播嵌套：外层事件）
        public List<GraphEventContext> children = new List<GraphEventContext>();   // 直接子事件
        public Card card_before;       // 事件主体卡在广播前/后的快照（108008/108009 用；Card.CloneNew 深克隆）
        public Card card_after;
    }
}
