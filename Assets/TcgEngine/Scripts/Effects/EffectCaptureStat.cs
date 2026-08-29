using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    /// <summary>
    /// 【通用采集效果】EffectCaptureStat
    /// 作用：读取目标单位的某项属性（攻击力/血量/法力），写入全局临时变量 selected_value。
    /// 
    /// 为什么要这个效果？
    ///   卡牌游戏里经常出现"属性联动"类效果，例如：
    ///     - 目标每有 1 点攻击力，抽 1 张牌
    ///     - 目标每有 1 点攻击力，对敌方英雄造成 1 点伤害
    ///     - 目标每有 1 点血量，召唤 1 个 1/1 小兵
    ///   这类效果的数量 = 目标的属性值（动态数值），无法用固定数值的 EffectDraw / EffectDamage 表达，
    ///   所以先用本效果把"属性值"采集到 selected_value，再由后面的效果按这个值执行。
    ///
    /// 推荐用法（配合现成效果拼装，免改其他代码）：
    ///   Ability 配置（例：法术"战术洞察"，打出时选择 1 个单位，每 1 攻抽 1 张牌）：
    ///     trigger = OnPlay
    ///     target  = SelectTarget  （玩家点选 1 个单位）
    ///     effects[0] = EffectCaptureStat   type=Attack, multiplier=1, oper=Set   → 目标攻击力写入 selected_value
    ///     effects[1] = EffectRepeat        type=SelectedValue                     → 循环 selected_value 次
    ///                    └─ 子 Ability：EffectDraw value=1（抽 1 张牌）            → 每 1 攻抽 1 张
    ///
    /// 注意：本效果必须放在"读取 selected_value 的效果"【之前】，保证顺序是先采集、后消费。
    /// </summary>

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/CaptureStat", order = 10)]
    public class EffectCaptureStat : EffectData
    {
        [Header("读目标的哪个属性")]
        public EffectStatType type = EffectStatType.Attack;   // Attack=攻击力 / HP=血量 / Mana=法力值

        [Header("系数：写入 selected_value = 属性值 × multiplier")]
        public int multiplier = 1;                            // 例：想"每 2 点攻击力抽 1 张"，填 1（代表 1 单位），更多用法见下方说明

        [Header("写入方式：Set 覆盖旧值 / Add 累加到旧值")]
        public EffectOperatorInt oper = EffectOperatorInt.Set; // 默认 Set 覆盖，避免继承上层残留值

        // 对"单张卡"目标：读它的攻击/血量/法力，写入 selected_value
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            int val = GetStat(target) * multiplier;                              // 取目标属性并乘系数
            logic.GameData.selected_value = AddOrSet(logic.GameData.selected_value, oper, val); // 写进临时变量
        }

        // 对"玩家"目标：读他的英雄攻击/血量/法力，写入 selected_value
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            int val = GetStat(target) * multiplier;
            logic.GameData.selected_value = AddOrSet(logic.GameData.selected_value, oper, val);
        }

        // 对"卡牌原型"目标（例如从牌组/卡池里选卡）：读卡牌基础属性，写入 selected_value
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, CardData target)
        {
            int val = GetStat(target) * multiplier;
            logic.GameData.selected_value = AddOrSet(logic.GameData.selected_value, oper, val);
        }

        // 读取"卡牌"目标的属性（已包含各种 buff/光环修正后的最终值）
        private int GetStat(Card target)
        {
            if (type == EffectStatType.Attack) return target.GetAttack();   // 最终攻击力（含攻击 buff）
            if (type == EffectStatType.HP) return target.GetHP();           // 最终血量（含伤害扣除）
            if (type == EffectStatType.Mana) return target.GetMana();       // 最终法力（含费用修正）
            return 0;
        }

        // 读取"玩家"目标的属性（玩家本身没有攻击力，用英雄的攻击力代替）
        private int GetStat(Player target)
        {
            if (type == EffectStatType.Attack) return target.hero != null ? target.hero.GetAttack() : 0;
            if (type == EffectStatType.HP) return target.hp;                // 玩家当前血量
            if (type == EffectStatType.Mana) return target.mana;            // 玩家当前剩余法力
            return 0;
        }

        // 读取"卡牌原型"目标的属性（用配置上的基础数值）
        private int GetStat(CardData target)
        {
            if (type == EffectStatType.Attack) return target.attack;
            if (type == EffectStatType.HP) return target.hp;
            if (type == EffectStatType.Mana) return target.mana;
            return 0;
        }
    }
}
