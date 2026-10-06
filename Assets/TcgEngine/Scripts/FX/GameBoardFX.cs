using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Client;
using TcgEngine.UI;

namespace TcgEngine.FX
{
    /// <summary>
    /// FX that are not related to any card/player, and appear in the middle of the board
    /// Usually when big abilities are played
    /// </summary>

    public class GameBoardFX : MonoBehaviour
    {
        void Start()
        {
            GameClient client = GameClient.Get();
            client.onNewTurn += OnNewTurn;
            client.onCardPlayed += OnPlayCard;
            client.onAbilityStart += OnAbility;
            client.onSecretTrigger += OnSecret;
            client.onValueRolled += OnRoll;
        }

        private void OnDestroy()
        {
            GameClient client = GameClient.Get();
            client.onNewTurn -= OnNewTurn;
            client.onCardPlayed -= OnPlayCard;
            client.onAbilityStart -= OnAbility;
            client.onSecretTrigger -= OnSecret;
            client.onValueRolled -= OnRoll;
        }

        void OnNewTurn(int player_id)
        {
            AudioTool.Get().PlaySFX("turn", AssetData.Get().new_turn_audio);
            FXTool.DoFX(AssetData.Get().new_turn_fx, Vector3.zero);
        }

        void OnPlayCard(Card card, Slot slot)
        {
            int player_id = GameClient.Get().GetPlayerID();
            if (card != null)
            {
                CardData icard = CardData.Get(card.card_id);
                //★icard 可能解析不到（卡池里已没有这个 id / 客户端与主机卡池不一致）→ 以前这里直接 icard.type 就是
                //  空引用，而本方法**每次出牌都会走**。另外技能卡按设计「行为与法术一致」（CardData.cs:20 枚举注释），
                //  施法特效/音效应与法术同款——此前只有 Spell 走这段，技能卡出牌没有特效音效。
                if (icard != null && (icard.type == CardType.Spell || icard.type == CardType.Skill))
                {
                    GameObject prefab = player_id == card.player_id ? AssetData.Get().play_card_fx : AssetData.Get().play_card_other_fx;
                    GameObject obj = FXTool.DoFX(prefab, Vector3.zero);
                    CardUI ui = obj.GetComponentInChildren<CardUI>();
                    ui.SetCard(icard, card.VariantData);

                    AudioClip spawn_audio = icard.spawn_audio != null ? icard.spawn_audio : AssetData.Get().card_spawn_audio;
                    AudioTool.Get().PlaySFX("card_spell", spawn_audio);
                }

                if (icard.type == CardType.Secret)
                {
                    GameObject sprefab = player_id == card.player_id ? AssetData.Get().play_secret_fx : AssetData.Get().play_secret_other_fx;
                    FXTool.DoFX(sprefab, Vector3.zero);

                    AudioClip spawn_audio = icard.spawn_audio != null ? icard.spawn_audio : AssetData.Get().card_spawn_audio;
                    AudioTool.Get().PlaySFX("card_spell", spawn_audio);
                }
            }
        }

        private void OnAbility(AbilityData iability, Card caster)
        {
            if (iability != null)
            {
                FXTool.DoFX(iability.board_fx, Vector3.zero);
            }
        }

        private void OnSecret(Card secret, Card triggerer)
        {
            CardData icard = CardData.Get(secret.card_id);
            if (icard?.attack_audio != null)
                AudioTool.Get().PlaySFX("card_secret", icard.attack_audio);
        }

        private void OnRoll(int value)
        {
            GameObject fx = FXTool.DoFX(AssetData.Get().dice_roll_fx, Vector3.zero);
            DiceRollFX dice = fx?.GetComponent<DiceRollFX>();
            if (dice != null)
            {
                dice.value = value;
            }
        }

    }
}