using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine
{
    /// <summary>
    /// 规则图( NodeDoc )执行入口效果：当能力被触发/目标解析后，把真实对局上下文
    /// （logic + caster + 选中目标）交给 NodeDocRunner 解释执行对应 NodeDoc 动作。
    /// 由 CardPoolIO 编译链路在检测到事件节点下游含 NodeDoc 动作时挂载。
    /// </summary>
    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/RunGraph", order = 10)]
    public class EffectRunGraph : EffectData
    {
        [Header("规则图")]
        public GraphData graph;            //要解释执行的图
        public string trigger_action;      //入口事件 action（OnPlay/StartOfTurn…），空=任意事件

        /// <summary>只走入口的哪个 Flow 出口（空 = 默认：动作口 out / 分支口等）。
        /// 被动效果入口有三条线（动作 out / 生效 enable / 失效 disable），它们共用一个入口节点，
        /// 靠本字段区分：能力触发时只执行对应那条线（见 NodeDocRunner.WalkFlowOutputs 的 only_pin）。</summary>
        public string entry_pin;

        /// <summary>被动效果入口的「生效条件」：为假 → 该入口的**动作线**不执行（亡语/战吼等主效果不跑）。
        /// 只挡动作线（entry_pin 为空的那条）：生效线/失效线必须照跑，
        /// 否则"条件由真变假"时该补执行的失效动作会被自己挡掉。</summary>
        private bool PassiveActionBlocked(GameLogic logic, Card caster)
        {
            if (trigger_action != "PassiveEffect" || !string.IsNullOrEmpty(entry_pin))
                return false;
            return !NodeDocRunner.IsEntryConditionMet(logic, graph, "PassiveEffect", caster, caster, null);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster)
        {
            if (PassiveActionBlocked(logic, caster))
                return;
            //顺序逐槽多目标：选择结果在图槽号 → 卡的映射里（入口「目标卡牌N」输出口按槽取值）；
            //槽1同时作为 target_card 传入，兼容旧的单一「目标/目标卡牌」引用。
            Dictionary<int, Card> slots = logic != null ? logic.GetMultiTargetResults() : null;
            Card slot1 = null;
            if (slots != null)
                slots.TryGetValue(1, out slot1);

            //★奥秘（陷阱）必须补上"触发者"：陷阱的能力编译成 AbilityTarget.None（目标由图里连线自己解析，
            //  不该弹选择器），此时走的是本重载 —— 没有选择结果 ⇒ slot1=null ⇒ 图里
            //  「入口.卡牌 / 入口.目标卡牌」全为空 ⇒ 条件恒假 ⇒ **陷阱不造成任何伤害**。
            //  （用户实报："这里直接对攻击者造成伤害，为什么还要我手动选目标" → 去掉选择器后
            //    又变成"不造成伤害"，根因就是这一处。）
            //  触发链 TriggerSecrets → TriggerCardAbilityType(OnBeforeAttack, 奥秘卡, 攻击者)，
            //  引擎在 ResolveCardAbility 里把触发者存进 game_data.ability_triggerer（GameLogic.cs:3426），
            //  这里取回来当"事件主体/目标卡牌"= 发起攻击的那张卡。
            //  只对奥秘生效：其它卡的同名重载语义保持原样（避免影响内置卡的既有行为）。
            if (slot1 == null && caster != null && caster.CardData != null && caster.CardData.type == CardType.Secret)
            {
                Game data = logic != null ? logic.GetGameData() : null;
                if (data != null && !string.IsNullOrEmpty(data.ability_triggerer))
                    slot1 = data.GetCard(data.ability_triggerer);
            }

            NodeDocRunner.Run(logic, graph, caster, slot1, null, trigger_action, ability: ability, target_slots: slots, entry_pin: entry_pin);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Card target)
        {
            if (PassiveActionBlocked(logic, caster))
                return;
            Debug.Log("[RunGraph] 图执行触发 action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null") + " 目标卡=" + (target != null ? target.CardData?.id : "无"));
            NodeDocRunner.Run(logic, graph, caster, target, null, trigger_action, ability: ability, entry_pin: entry_pin);
        }

        /// <summary>★卡牌定义目标（AbilityTarget.AllCardData）：引擎在 ResolveCardAbilityCardData 里
        /// **逐定义**调 `DoEffects(logic, caster, CardData)`。此前 EffectRunGraph 没有这个重载 →
        /// 落到基类空实现 → 这类图能力**静默不执行**（图里动作一个都不跑，也不报错）。
        /// 现在把"本次定义"带给解释器（ctx_target_define），定义口未接线的动作即可取到它
        /// （如旧 EffectCreate：从目标定义创建衍生卡）。</summary>
        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, CardData target)
        {
            if (PassiveActionBlocked(logic, caster))
                return;
            Debug.Log("[RunGraph] 图执行触发(卡牌定义目标) action=" + trigger_action + " caster="
                + (caster != null ? caster.CardData?.id : "null") + " 定义=" + (target != null ? target.id : "无"));
            NodeDocRunner.Run(logic, graph, caster, null, null, trigger_action, ability: ability, target_define: target, entry_pin: entry_pin);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            if (PassiveActionBlocked(logic, caster))
                return;
            Debug.Log("[RunGraph] 图执行触发(玩家目标) action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null")
                + " 目标玩家=p" + (target != null ? target.player_id.ToString() : "null") + " 目标英雄卡=" + (target != null && target.hero != null ? target.hero.CardData?.id : "无"));
            //英雄=卡牌：选中英雄（含「英雄」类型直接指向、逐槽选英雄）时，把英雄卡同时作为 target_card 传入，
            //使「目标卡牌N/目标」引用拿到英雄卡（伤害经 DamageCard/DealDamage 英雄路由落到玩家 hp，
            //并保留 OnBefore/AfterDamage 图事件与吸血）；「玩家」口仍返回玩家本体（抽牌/属性类动作不受影响）。
            //此前这里传 null：单目标选英雄时下游 目标卡牌1 解析为空 → "打随从生效、打英雄无效"。
            Card hero = target != null ? target.hero : null;
            NodeDocRunner.Run(logic, graph, caster, hero, target, trigger_action, ability: ability, entry_pin: entry_pin);
        }

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Slot target)
        {
            if (PassiveActionBlocked(logic, caster))
                return;
            //选目标时以"格子"结算（SelectSlot/PlayTarget 落点）：格内有卡→卡牌目标；空己方格→玩家目标(打脸)；空敌方格→无目标
            if (target == null)
                return;
            Card slot_card = logic.GameData.GetSlotCard(target);
            if (slot_card != null)
            {
                Debug.Log("[RunGraph] 图执行触发(格子目标) action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null")
                    + " 格内卡=" + (slot_card.CardData != null ? slot_card.CardData.id : "null"));
                //★D 批：把槽位一并带入 —— AllSlots/落点结算时引擎已选好槽，召唤类动作要落到这个槽
                NodeDocRunner.Run(logic, graph, caster, slot_card, null, trigger_action, ability: ability, target_slot: target, entry_pin: entry_pin);
            }
            else
            {
                Player tplayer = logic.GameData.GetPlayer(target.p);
                Card hero = tplayer != null ? tplayer.hero : null;   //同上：打脸也把英雄卡作为目标卡传入
                Debug.Log("[RunGraph] 图执行触发(空格/打脸) action=" + trigger_action + " caster=" + (caster != null ? caster.CardData?.id : "null")
                    + " 目标玩家=p" + (tplayer != null ? tplayer.player_id.ToString() : "null"));
                NodeDocRunner.Run(logic, graph, caster, hero, tplayer, trigger_action, ability: ability, target_slot: target, entry_pin: entry_pin);
            }
        }
    }
}
