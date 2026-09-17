using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;
using TcgEngine;

namespace TcgEngine.UI
{
    /// <summary>
    /// Main player UI inside the GameUI, inside the game scene
    /// there is one for each player
    /// </summary>

    public class PlayerUI : MonoBehaviour
    {
        public bool is_opponent;
        public Text pname;
        public AvatarUI avatar;

        /// <summary>旧「空心/实心圆」灵力条：本面板已改为文本三值显示（见 mana_txt）。
        /// 该引用保留只为不破坏现有场景/预制绑定，不再写入数值；未拖绑时为 null，逻辑不受影响。</summary>
        public IconBar mana_bar;

        /// <summary>三套灵力文本「当前灵力 / 灵力上限 / 最大灵力值」（TMP，优先）。
        /// 可手动拖绑任意 TMP 文本；留空时会在运行时自动创建（位置复制 mana_bar，找不到则放面板底部居中）。</summary>
        public TMPro.TMP_Text mana_txt;

        public Text hp_txt;
        public Text hp_max_txt;
        public PlayedCardsPanel played_cards_panel_prefab;

        public Animator[] secrets;

        public GameObject dead_fx;
        public AudioClip dead_audio;
        public Sprite avatar_dead;

        private bool killed = false;
        private float timer = 0f;
        private PlayedCardsPanel played_cards_panel_instance;

        private int prev_hp = 0;
        private float delayed_damage_timer = 0f;

        //★ 脏检查（Update 每帧写这些文本：字符串拼接 + TMP 赋值）
        private string last_avatar_id;
        private int last_mana = int.MinValue;
        private int last_mana_max = int.MinValue;
        private int last_mana_total = int.MinValue;
        private int last_hp_shown = int.MinValue;
        private int last_hp_max = int.MinValue;

        private static List<PlayerUI> ui_list = new List<PlayerUI>();

        private void Awake()
        {
            ui_list.Add(this);
        }

        private void OnDestroy()
        {
            ui_list.Remove(this);
        }

        void Start()
        {
            pname.text = "";
            hp_txt.text = "";
            hp_max_txt.text = "";
            EnsureManaText();   //三套灵力：文本显示（未拖绑则运行时创建，并停用旧圆点条）

            for (int i = 0; i < secrets.Length; i++)
                secrets[i].gameObject.SetActive(false);

            avatar.onClick += OnClickAvatar;
            GameClient.Get().onSecretTrigger += OnSecretTrigger;
        }

        void Update()
        {
            if (!GameClient.Get().IsReady())
                return;

            Player player = GetPlayer();

            if (player != null)
            {
                pname.text = player.username;
                //三套灵力：当前灵力 / 灵力上限 / 最大灵力值（实时刷新）
                //★ 脏检查：值没变就不拼字符串、不写 TMP（原来每帧 3 次字符串拼接 + 赋值）
                int mana_total = player.GetManaMaxTotal();
                if (mana_txt != null)
                {
                    if (player.mana != last_mana || player.mana_max != last_mana_max || mana_total != last_mana_total)
                    {
                        last_mana = player.mana;
                        last_mana_max = player.mana_max;
                        last_mana_total = mana_total;
                        mana_txt.text = last_mana + " / " + last_mana_max + " / " + last_mana_total;
                    }
                }
                else if (mana_bar != null)
                {
                    //兜底：TMP 文本创建失败时退回旧圆点显示，避免灵力完全不显示
                    mana_bar.value = player.mana;
                    mana_bar.max_value = player.mana_max;
                }

                if (prev_hp != last_hp_shown)
                {
                    last_hp_shown = prev_hp;
                    hp_txt.text = last_hp_shown.ToString();
                }
                if (player.hp_max != last_hp_max)
                {
                    last_hp_max = player.hp_max;
                    hp_max_txt.text = "/" + last_hp_max.ToString();
                }

                AvatarData adata = AvatarData.Get(player.avatar);
                if (avatar != null && adata != null && !killed && adata.id != last_avatar_id)
                {
                    last_avatar_id = adata.id;
                    avatar.SetAvatar(adata);
                }

                delayed_damage_timer -= Time.deltaTime;
                if (!IsDamagedDelayed())
                    prev_hp = player.hp;
            }
            

            timer += Time.deltaTime;
            if (timer > 0.4f)
            {
                timer = 0f;
                SlowUpdate();
            }
        }

        void SlowUpdate()
        {
            Player player = GetPlayer();
            if (player == null)
                return;

            for (int i = 0; i < secrets.Length; i++)
            {
                bool active = i < player.cards_secret.Count;
                bool was_active = secrets[i].gameObject.activeSelf;
                if (active != was_active)
                    secrets[i].gameObject.SetActive(active);
                if (active && !was_active)
                    secrets[i].SetTrigger("appear");
                if (active && !was_active && !is_opponent)
                    secrets[i].GetComponent<SecretIconUI>().SetCard(player.cards_secret[i]);
                if (!active && was_active)
                    secrets[i].Rebind();
            }
        }

        /// <summary>
        /// 灵力文本兜底创建：优先使用场景里拖绑的 mana_txt；没有则在运行时创建 TMP 文本，
        /// 位置优先复制旧圆点条 mana_bar（视觉上原地替换），并把旧圆点条整条停用（对象保留，便于回退）。
        /// 只在绑定/创建成功后才停用圆点条：万一 TMP 创建失败，旧显示仍然可用。
        /// </summary>
        private void EnsureManaText()
        {
            if (mana_txt != null)
            {
                if (mana_bar != null)
                    mana_bar.gameObject.SetActive(false);
                return;
            }

            GameObject go = new GameObject("ManaText", typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(transform, false);

            RectTransform src = mana_bar != null ? mana_bar.GetComponent<RectTransform>() : null;
            if (src != null)
            {
                rt.anchorMin = src.anchorMin;
                rt.anchorMax = src.anchorMax;
                rt.pivot = src.pivot;
                rt.anchoredPosition = src.anchoredPosition;
                rt.sizeDelta = new Vector2(Mathf.Max(src.sizeDelta.x, 180f), Mathf.Max(src.sizeDelta.y, 32f));
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 12f);
                rt.sizeDelta = new Vector2(200f, 32f);
            }

            TMPro.TextMeshProUGUI txt = go.AddComponent<TMPro.TextMeshProUGUI>();
            UIFonts.ApplyFont(txt);                 //全项目统一字体管线（含中文字形）
            txt.fontSize = 24;
            txt.alignment = TMPro.TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.enableWordWrapping = false;
            txt.overflowMode = TMPro.TextOverflowModes.Overflow;
            txt.raycastTarget = false;
            mana_txt = txt;

            if (mana_bar != null)
                mana_bar.gameObject.SetActive(false);   //创建成功：停用旧圆点灵力显示
        }

        public void Kill()
        {
            killed = true;
            avatar.SetImage(avatar_dead);
            AudioTool.Get().PlaySFX("fx", dead_audio);
            FXTool.DoFX(dead_fx, avatar.transform.position);
        }

        public void DelayDamage(int damage, float duration = 1f)
        {
            if (damage != 0)
            {
                delayed_damage_timer = duration;
            }
        }

        public bool IsDamagedDelayed()
        {
            return delayed_damage_timer > 0f;
        }

        private void OnClickAvatar(AvatarData avatar)
        {
            Game gdata = GameClient.Get().GetGameData();
            int player_id = GameClient.Get().GetPlayerID();
            if (gdata.selector == SelectorType.SelectTarget && player_id == gdata.selector_player_id)
            {
                GameClient.Get().SelectPlayer(GetPlayer());
            }
            else
            {
                Player player = GetPlayer();
                if (player != null)
                {
                    if (played_cards_panel_instance != null && played_cards_panel_instance.IsVisible())
                    {
                        played_cards_panel_instance.Hide();
                    }
                    else
                    {
                        if (played_cards_panel_prefab != null)
                        {
                            if (played_cards_panel_instance == null)
                            {
                                played_cards_panel_instance = Instantiate(played_cards_panel_prefab, transform);
                                played_cards_panel_instance.transform.SetParent(transform, false);
                            }
                            played_cards_panel_instance.Show(player);
                        }
                    }
                }
            }
        }

        private void OnSecretTrigger(Card secret, Card triggerer)
        {
            Player player = GetPlayer();
            int index = player.cards_secret.Count - 1;
            if (player.player_id == secret.player_id && index >= 0 && index < secrets.Length)
            {
                secrets[index].SetTrigger("reveal");
            }
        }

        public Player GetPlayer()
        {
            int player_id = is_opponent ? GameClient.Get().GetOpponentPlayerID() : GameClient.Get().GetPlayerID();
            Game data = GameClient.Get() != null ? GameClient.Get().GetGameData() : null;
            //★ 同 GameClient.GetPlayer：进对战首帧 data 为 null，直接 data.GetPlayer(...) 会在 UI 刷新里抛 NRE
            //  （异常会把该帧 Update 后续逻辑全部掐断）→ 未就绪时返回 null，调用方按"暂无玩家"处理。
            if (data == null || data.players == null)
                return null;
            return data.GetPlayer(player_id);
        }

        public static PlayerUI Get(bool opponent)
        {
            foreach (PlayerUI ui in ui_list)
            {
                if (ui.is_opponent == opponent)
                    return ui;
            }
            return null;
        }

    }
}