using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// 给卡牌/玩家**加上一个关键词**（并按其绑定状态叠加数值）—— 2026-09「法术伤害」由特性迁移到关键词后，
    /// 「法术伤害+N」的加法器：关键词 id 与数值都配在这里，运行时给目标挂上该关键词绑定的状态值（如 StatusType.SpellDamage）。
    ///
    /// 设计说明：关键词本身是"有/无"（`KeywordData` 无数值字段），数值统一由**关键词绑定的状态值**承载，
    /// 这与工程既有约定一致（armor→StatusType.Armor、taunt→Protection 等都如此），所以本效果：
    ///   ① 若目标卡还没有该关键词 → 加进关键词列表（`Card.keywords`，`HasKeyword` 立即可查）；
    ///   ② 按 keyword.status_type 叠加状态值（价值 = value；duration=0 表示永久）。
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/AddKeyword", order = 12)]
    public class EffectAddKeyword : EffectData
    {
        public KeywordData keyword;
        public int value = 1;
        public int duration = 0;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Apply(target);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            Apply(target);
        }

        /// <summary>持续效果（光环/持续能力）与普通效果同路径：都只是"加关键词 + 叠状态值"</summary>
        public override void DoOngoingEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Apply(target);
        }

        private void Apply(Card target)
        {
            if (keyword == null || target == null)
                return;
            if (target.keywords == null)
                target.keywords = new List<string>();
            if (!target.keywords.Contains(keyword.id))
                target.keywords.Add(keyword.id);
            if (keyword.status_type != StatusType.None)
                target.AddStatus(keyword.status_type, value, duration);
            Debug.Log("[Keyword] 加上关键词「" + keyword.title + "」→ " + (target.CardData != null ? target.CardData.title : target.uid)
                + "（+" + value + (duration > 0 ? "，" + duration + " 回合" : "") + "）");
        }

        private void Apply(Player target)
        {
            if (keyword == null || target == null)
                return;
            if (keyword.status_type != StatusType.None)
                target.AddStatus(keyword.status_type, value, duration);
            Debug.Log("[Keyword] 加上关键词「" + keyword.title + "」→ 玩家 " + target.player_id
                + "（+" + value + (duration > 0 ? "，" + duration + " 回合" : "") + "）");
        }
    }
}
