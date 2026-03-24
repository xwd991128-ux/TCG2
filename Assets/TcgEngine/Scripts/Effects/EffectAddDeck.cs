using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    //Effect to Summon an entirely new card (not in anyones deck)
    //And places it on the board (if target slot) or hand (if target player)
    //Unlike EffectCreate, this effect targets where the card goes, and the carddata is selected on the effect

    [CreateAssetMenu(fileName = "effect", menuName = "TcgEngine/Effect/AddDeck", order = 10)]
    public class EffectAddDeck : EffectData
    {
        public CardData card;

        public override void DoEffect(GameLogic logic, AbilityData ability, Card caster, Player target)
        {
            logic.AddCardDeck(target, card, caster.VariantData); //Add a card to deck

        }


    }
}