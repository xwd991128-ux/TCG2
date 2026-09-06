using UnityEngine;

namespace TcgEngine
{
    /// <summary>
    /// 规则图入口"目标范围"条件：限制入场/打出能力可选择的单选目标只能是 角色(场上卡) 或 玩家英雄。
    /// 由 CardPoolIO 编译时根据「打出时」事件的 target_scope 字段动态创建（不需要 .asset）。
    /// </summary>
    [CreateAssetMenu(fileName = "condition", menuName = "TcgEngine/Condition/TargetRole", order = 10)]
    public class ConditionTargetRole : ConditionData
    {
        [Header("player_only=true：目标只能是玩家英雄；false：目标只能是场上角色")]
        public bool player_only;
        [Header("allow_player=true：玩家英雄也算合法目标（zmcs「角色」含英雄）")]
        public bool allow_player;

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Card target)
        {
            return !player_only;   //仅角色：接受场上角色（含英雄卡），拒绝"玩家目标"的判定
        }

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Player target)
        {
            if (allow_player)
                return true;           //角色含英雄：玩家英雄可选
            return player_only;        //仅英雄：接受玩家；仅角色：拒绝玩家
        }

        public override bool IsTargetConditionMet(Game data, AbilityData ability, Card caster, Slot target)
        {
            if (target == null)
                return false;
            if (allow_player && target.IsPlayerSlot())
                return true;           //英雄区格子：代表英雄，合法（结算时按玩家目标走）
            //选目标时以"格子"为候选：空格子非法，只有格内有卡才按角色规则判定
            return data.GetSlotCard(target) != null && !player_only;
        }
    }
}
