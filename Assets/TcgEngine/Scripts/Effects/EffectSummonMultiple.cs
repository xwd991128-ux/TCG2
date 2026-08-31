using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// 召唤多个单位的数量支持参数来源：
    ///   不填 amount 时，用本资源上填写的固定 count（兼容旧配置）；
    ///   填了 amount 时，每次执行的数量 = amount 动态取值（例如"每1个头召唤1个"）。
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/SummonMultiple", order = 10)]
    public class EffectSummonMultiple : EffectData
    {
        public CardData summon;

        [Header("固定数量（amount 为空时使用）")]
        public int count;

        [Header("数量来源：填了则优先用动态值，忽略上面 count")]
        public ValueSource amount;

        // 本次召唤个数 = amount 或固定 count
        private int GetCount(Game data, AbilityData ability, Card caster, Card target, Player target_player)
        {
            if (amount != null)
                return amount.GetValue(data, ability, caster, target, target_player);
            return count;
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            int nb = GetCount(logic.GameData, ability, caster, null, target);
            for (int i = 0; i < nb; i++)
            {
                logic.SummonCardHand(target, summon, caster.VariantData);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            int nb = GetCount(logic.GameData, ability, caster, target, null);
            for (int i = 0; i < nb; i++)
            {
                logic.SummonCardHand(player, summon, caster.VariantData);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Slot target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            int nb = GetCount(logic.GameData, ability, caster, null, null);
            for (int i = 0; i < nb; i++)
            {
                logic.SummonCard(player, summon, caster.VariantData, target);
            }
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, CardData target)
        {
            Player player = logic.GameData.GetPlayer(caster.player_id);
            int nb = GetCount(logic.GameData, ability, caster, null, null);
            for (int i = 0; i < nb; i++)
            {
                logic.SummonCardHand(player, target, caster.VariantData);
            }
        }
    }
}