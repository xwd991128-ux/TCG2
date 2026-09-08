using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Client;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// Main UI script for all the game scene UI
    /// </summary>

    public class GameUI : MonoBehaviour
    {
        public Canvas game_canvas;
        public Canvas panel_canvas;
        public Canvas top_canvas;
        public UIPanel menu_panel;
        public Text quit_btn;

        [Header("Turn Area")]
        public Text turn_count;
        public Text turn_timer;
        public Button end_turn_button;
        public Animator timeout_animator;
        public AudioClip timeout_audio;

        [Header("Battle Buttons")]
        public RectTransform battle_btn_container;   // 自定义按钮容器（编辑器工具生成，屏幕右侧）
        public GameObject battle_btn_template;        // 按钮模板（隐藏，运行时实例化）

        private float selector_timer = 0f;
        private float end_turn_timer = 0f;
        private int prev_time_val = 0;

        private static GameUI instance;

        void Awake()
        {
            instance = this;

            if (game_canvas.worldCamera == null)
                game_canvas.worldCamera = Camera.main;
            if (panel_canvas.worldCamera == null)
                panel_canvas.worldCamera = Camera.main;
            if (top_canvas.worldCamera == null)
                top_canvas.worldCamera = Camera.main;
        }

        private void Start()
        {
            GameClient.Get().onGameStart += OnGameStart;
            GameClient.Get().onNewTurn += OnNewTurn;
            LoadPanel.Get().Show(true);
            BlackPanel.Get().Show(true);
            BlackPanel.Get().Hide();

            if (quit_btn != null)
                quit_btn.text = GameClient.game_settings.IsOnlinePlayer() ? "Resign" : "Quit";

            RefreshBattleButtons();
        }

        /// <summary>根据全局按钮配置（BattleButtonIO）动态生成战斗界面自定义按钮（模板实例化）。
        /// 每次进入战斗界面重新加载配置，确保按钮编辑器保存的修改立即生效。</summary>
        private void RefreshBattleButtons()
        {
            if (battle_btn_container == null || battle_btn_template == null)
                return;
            BattleButtonIO.LoadAll();
            //清空容器非模板子对象（模板本身保留）
            for (int i = battle_btn_container.childCount - 1; i >= 0; i--)
            {
                GameObject go = battle_btn_container.GetChild(i).gameObject;
                if (go != battle_btn_template)
                    Destroy(go);
            }
            List<BattleButtonData> list = BattleButtonIO.GetAll();
            for (int i = 0; i < list.Count; i++)
            {
                BattleButtonData b = list[i];
                if (b == null || string.IsNullOrEmpty(b.id))
                    continue;
                GameObject inst = Instantiate(battle_btn_template, battle_btn_container);
                inst.name = "BattleBtn_" + b.id;
                inst.SetActive(true);
                Text t = inst.GetComponentInChildren<Text>(true);
                if (t != null)
                    t.text = b.title;
                Button btn = inst.GetComponent<Button>();
                if (btn == null)
                    btn = inst.AddComponent<Button>();
                string bid = b.id;
                btn.onClick.AddListener(() => OnClickBattleButton(bid));
            }
        }

        /// <summary>点击自定义战斗按钮：走服务器（GameClient.SendBattleButton → 服务端校验并执行按钮图）</summary>
        private void OnClickBattleButton(string button_id)
        {
            if (GameClient.Get() != null && GameClient.Get().IsReady())
                GameClient.Get().SendBattleButton(button_id);
        }

        void Update()
        {
            Game data = GameClient.Get().GetGameData();
			bool is_connecting = data == null || data.state == GameState.Connecting;
            bool connection_lost = !is_connecting && !GameClient.Get().IsReady();
            ConnectionPanel.Get().SetVisible(connection_lost);

            //Menu
            if (Input.GetKeyDown(KeyCode.Escape))
                menu_panel.Toggle();

            if (!GameClient.Get().IsReady())
                return;

            bool yourturn = GameClient.Get().IsYourTurn();
            LoadPanel.Get().SetVisible(is_connecting && !data.HasStarted());
            end_turn_button.interactable = yourturn && end_turn_timer > 1f;
            end_turn_timer += Time.deltaTime;
            selector_timer += Time.deltaTime;

            //Timer
            turn_count.text = "Turn " + data.turn_count.ToString();
            turn_timer.enabled = data.turn_timer > 0f;
            turn_timer.text = Mathf.RoundToInt(data.turn_timer).ToString();
            turn_timer.enabled = data.turn_timer < 999f;

            //Simulate timer
            if (data.state == GameState.Play && data.turn_timer > 0f)
                data.turn_timer -= Time.deltaTime;

            //Timer warning
            if (data.state == GameState.Play)
            {
                int val = Mathf.RoundToInt(data.turn_timer);
                int tick_val = 10;
                if (val < prev_time_val && val <= tick_val)
                    PulseFX();
                prev_time_val = val;
            }

            //Show selector panels
            foreach (SelectorPanel panel in SelectorPanel.GetAll())
            {
                bool should_show = panel.ShouldShow();
                if (should_show != panel.IsVisible() && selector_timer > 1f)
                {
                    selector_timer = 0f;
                    panel.SetVisible(should_show);

                    if (should_show)
                    {
                        AbilityData ability = AbilityData.Get(data.selector_ability_id);
                        Card caster = data.GetCard(data.selector_caster_uid);
                        panel.Show(ability, caster);
                    }
                }
            }

            //Hide
            if (!yourturn && data.phase != GamePhase.Mulligan)
            {
                SelectorPanel.HideAll();
            }

        }

        private void PulseFX()
        {
            timeout_animator?.SetTrigger("pulse");
            AudioTool.Get().PlaySFX("time", timeout_audio, 1f);
        }

        private void OnGameStart()
        {
            
        }

        private void OnNewTurn(int player_id)
        {
            CardSelector.Get().Hide();
            SelectTargetUI.Get().Hide();
        }

        public void OnClickNextTurn()
        {
            if (!Tutorial.Get().CanDo(TutoEndTrigger.EndTurn))
                return;

            GameClient.Get().EndTurn();
            end_turn_timer = 0f; //Disable button immediately (dont wait for refresh)
        }

        public void OnClickRestart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void OnClickMenu()
        {
            menu_panel.Show();
        }

        public void OnClickBack()
        {
            menu_panel.Hide();
        }

        public void OnClickQuit()
        {
            bool online = GameClient.game_settings.IsOnlinePlayer();
            bool ended = GameClient.Get().HasEnded();
            if (online && !ended)
                GameClient.Get().Resign();
            else
                StartCoroutine(QuitRoutine("Menu"));
            menu_panel.Hide();
        }

        private IEnumerator QuitRoutine(string scene)
        {
            BlackPanel.Get().Show();
            AudioTool.Get().FadeOutMusic("music");
            AudioTool.Get().FadeOutSFX("ambience");
            AudioTool.Get().FadeOutSFX("ending_sfx");

            yield return new WaitForSeconds(1f);

            GameClient.Get().Disconnect();
            SceneNav.GoTo(scene);
        }

        public void OnClickSwapObserve()
        {
            int other = GameClient.Get().GetPlayerID() == 0 ? 1 : 0;
            GameClient.Get().SetObserverMode(other);
        }

        public static bool IsUIOpened()
        {
            return CardSelector.Get().IsVisible() || EndGamePanel.Get().IsVisible();
        }

        public static bool IsOverUI()
        {
            //return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
            eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
            return results.Count > 0;
        }

        public static bool IsOverUILayer(string sorting_layer)
        {
            return IsOverUILayer(SortingLayer.NameToID(sorting_layer));
        }

        public static bool IsOverUILayer(int sorting_layer)
        {
            //return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
            eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
            int count = 0;
            foreach (RaycastResult result in results)
            {
                if (result.sortingLayer == sorting_layer)
                    count++;
            }
            return count > 0;
        }

        public static bool IsOverRectTransform(Canvas canvas, RectTransform rect)
        {
            PointerEventData pevent = new PointerEventData(EventSystem.current);
            pevent.position = Input.mousePosition;

            List<RaycastResult> results = new List<RaycastResult>();
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            raycaster.Raycast(pevent, results);

            foreach (RaycastResult result in results)
            {
                if (result.gameObject.transform == rect || result.gameObject.transform.IsChildOf(rect))
                    return true;
            }
            return false;
        }

        public static Vector2 MouseToRectPos(Canvas canvas, RectTransform rect, Vector2 screen_pos)
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
            {
                Vector2 anchor_pos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen_pos, canvas.worldCamera, out anchor_pos);
                return anchor_pos;
            }
            else
            {
                Vector2 anchor_pos = screen_pos - new Vector2(rect.position.x, rect.position.y);
                anchor_pos = new Vector2(anchor_pos.x / rect.lossyScale.x, anchor_pos.y / rect.lossyScale.y);
                return anchor_pos;
            }
        }

        public static Vector3 MouseToWorld(Vector2 mouse_pos, float distance = 10f)
        {
            Camera cam = GameCamera.Get() != null ? GameCamera.GetCamera() : Camera.main;
            Vector3 wpos = cam.ScreenToWorldPoint(new Vector3(mouse_pos.x, mouse_pos.y, distance));
            return wpos;
        }

        public static string FormatNumber(int value)
        {
            return string.Format("{0:#,0}", value);
        }

        public static GameUI Get()
        {
            return instance;
        }
    }
}
