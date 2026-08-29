using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// 动态抽牌效果：对目标单位，每有 X 点指定属性（攻击力/血量/法力）抽 N 张牌
    /// 抽牌数 = 属性值 × multiplier，例如目标是一个 5 攻随从，type=Attack，multiplier=1 时抽 5 张
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/DrawStat", order = 11)]
    public class EffectDrawStat : EffectData
    {
        [Header("读取哪个属性")]
        public EffectStatType type = EffectStatType.Attack;

        [Header("系数：抽牌数 = 属性值 × multiplier")]
        public int multiplier = 1;

        [Header("默认从施法者抽牌；勾选后改为从目标的拥有者抽牌")]
        public bool draw_for_target_owner = false;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            int stat = GetCardStat(target);
            int nb = Mathf.Max(0, stat * multiplier);
            Player drawer = GetDrawer(logic, caster, target.player_id);
            logic.DrawCard(drawer, nb);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            int stat = GetPlayerStat(target);
            int nb = Mathf.Max(0, stat * multiplier);
            Player drawer = GetDrawer(logic, caster, target.player_id);
            logic.DrawCard(drawer, nb);
        }

        private int GetCardStat(Card card)
        {
            if (card == null)
                return 0;
            if (type == EffectStatType.Attack)
                return card.GetAttack();
            if (type == EffectStatType.HP)
                return card.GetHP();
            if (type == EffectStatType.Mana)
                return card.GetMana();
            return 0;
        }

        private int GetPlayerStat(Player player)
        {
            if (player == null)
                return 0;
            if (type == EffectStatType.Attack)
                return player.hero != null ? player.hero.GetAttack() : 0;
            if (type == EffectStatType.HP)
                return player.hp;
            if (type == EffectStatType.Mana)
                return player.mana;
            return 0;
        }

        private Player GetDrawer(GameLogic logic, Card caster, int target_player_id)
        {
            if (draw_for_target_owner)
                return logic.GameData.GetPlayer(target_player_id);
            return logic.GameData.GetPlayer(caster.player_id);
        }
    }
}