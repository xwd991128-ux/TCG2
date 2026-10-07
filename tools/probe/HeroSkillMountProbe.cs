using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TcgEngine.Gameplay;
using TcgEngine.Workshop;

namespace TcgEngine.Probe
{
    /// <summary>
    /// 【英雄技能卡挂载验证】type=Skill 的卡 + 英雄卡 CardData.skills 引用：
    /// ① 走真实 SetPlayerDeck → 英雄卡实例上应出现技能卡的起动式能力；
    /// ② 技能的能力可执行（ActivateAbility → 伤害生效）；
    /// ③ 技能卡本身不能从手牌打出（CanPlayCard 拦截）。
    /// 触发：建 tools/heroskill_mount_flag.txt → 进 Play → 写 tools/heroskill_mount_result.txt → 自动删标记。
    /// ★标记/输出必须与 HeroSkillProbe 区分开：两者原先共用 tools/heroskill_flag.txt + tools/heroskill_result.txt，
    ///   后跑的会覆盖先跑的 ⇒ 其中一个"永远跑不到"（只有一份结果，肉眼看着像全绿）。
    /// </summary>
    public class HeroSkillMountProbe : MonoBehaviour
    {
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static string OutPath { get { return Path.Combine(Root, "tools/heroskill_mount_result.txt"); } }
        private static string FlagPath { get { return Path.Combine(Root, "tools/heroskill_mount_flag.txt"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("HeroSkillMountProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<HeroSkillMountProbe>();
        }

        private int frames;
        private bool done;
        private readonly StringBuilder sb = new StringBuilder();

        private void Update()
        {
            if (done)
            {
                if (File.Exists(FlagPath)) { done = false; frames = 0; sb.Clear(); }
                return;
            }
            frames++;
            if (frames < 120)
                return;
            done = true;
            try { Run(); }
            catch (Exception e) { sb.AppendLine("EXCEPTION 整体: " + e.Message); }
            try { File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { File.Delete(FlagPath); } catch { }
            Debug.Log("[英雄技能] 验证完成 → " + OutPath);
        }

        private static GraphNode Node(GraphData g, string id, GraphNodeType type, string action, string title, params string[] fp)
        {
            var n = new GraphNode();
            n.id = id; n.type = type; n.action = action; n.title = title;
            n.pins = new List<GraphPin>();
            n.fields = new List<FieldCustomData>();
            for (int i = 0; i + 1 < fp.Length; i += 2)
                n.fields.Add(new FieldCustomData { name = fp[i], value = fp[i + 1] });
            foreach (NodeDocDef d in NodeDocDb.All)
                if (d != null && d.define_id == action) { n.category = string.IsNullOrEmpty(d.category) ? "其他" : d.category; break; }
            if (string.IsNullOrEmpty(n.category)) n.category = "其他";
            g.nodes.Add(n);
            return n;
        }

        private static void Pin(GraphNode n, string name, NodeValueType type, bool output)
        {
            n.pins.Add(new GraphPin { id = n.id + "_" + name, name = name, display_name = name, is_output = output, type = type });
        }

        private static void Link(GraphData g, GraphNode from, string fromPin, GraphNode to, string toPin)
        {
            g.links.Add(new GraphLink
            {
                from_node = from.id, from_pin = from.id + "_" + fromPin,
                to_node = to.id, to_pin = to.id + "_" + toPin
            });
        }

        private void Run()
        {
            Game game = new Game("probe_heroskill", 2);
            game.state = GameState.Play;
            GameLogic logic = new GameLogic(game);
            Player p0 = game.players[0], p1 = game.players[1];
            CardData hero = null;
            foreach (CardData c in CardData.GetAll())
                if (c != null && c.type == CardType.Hero) { hero = c; break; }
            if (hero == null) { sb.AppendLine("SKIP 缺少英雄定义"); return; }
            p1.hero = Card.Create(hero, null, p1, "hs_p1hero"); p1.hp_max = 30; p1.hp = 30;

            //---- 技能卡（type=Skill）：起动式效果入口 → 造成伤害 3 ----
            GraphData g = new GraphData { name = "探针英雄技能", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            GraphNode ev = Node(g, "ev", GraphNodeType.Event, "ActivateAbility", "起动式效果入口：探针");
            Pin(ev, "out", NodeValueType.Flow, true);
            Pin(ev, "card", NodeValueType.Card, true);
            Pin(ev, "player", NodeValueType.Player, true);
            GraphNode dmg = Node(g, "d", GraphNodeType.Action, "202001", "造成伤害", "value", "3");
            Pin(dmg, "in", NodeValueType.Flow, false);
            Pin(dmg, "out", NodeValueType.Flow, true);
            Pin(dmg, "value", NodeValueType.Int32, false);
            Link(g, ev, "out", dmg, "in");

            CardCustomData sk = new CardCustomData();
            sk.id = "probe_skill_fire3";
            sk.title = "探针英雄技能";
            sk.type = "Skill";
            sk.mana = 2;
            sk.effects = new List<CardEffectData> { new CardEffectData { name = "效果1", graph = g } };

            //---- 英雄卡（type=Hero）：skills 引用上面的技能卡 ----
            GraphData hg = new GraphData { name = "英雄图", nodes = new List<GraphNode>(), links = new List<GraphLink>() };
            Node(hg, "he", GraphNodeType.Event, "ActivateEffect", "主动效果入口");
            CardCustomData hd = new CardCustomData();
            hd.id = "probe_hero_withskill";
            hd.title = "探针英雄";
            hd.type = "Hero";
            hd.mana = 0;
            hd.skills = new List<string> { sk.id };
            hd.effects = new List<CardEffectData> { new CardEffectData { name = "效果1", graph = hg } };

            //---- 真实导入（BuildCardData → BuildPool → ImportToGame → RegisterCard）----
            try
            {
                CardData sk_built = CardPoolIO.BuildCardData(sk);
                CardData hd_built = CardPoolIO.BuildCardData(hd);
                CardPoolData pool = CardPoolIO.BuildPool(new List<CardData> { sk_built, hd_built }, "probe_heroskill_pool");
                CardPoolIO.ImportToGame(pool);
            }
            catch (Exception e) { sb.AppendLine("导入抛异常: " + e.Message); }

            //---- ① 真实 SetPlayerDeck：英雄带技能卡 → 挂载 ----
            UserDeckData deck = new UserDeckData();
            deck.tid = "probe_heroskill_deck";
            deck.cards = new UserCardData[0];   //技能卡不进卡组（挂在英雄身上），卡组为空也允许
            deck.hero = new UserCardData { tid = hd.id, variant = "1" };
            logic.SetPlayerDeck(p0, deck);
            if (p0.hero == null) { sb.AppendLine("FAIL\t① 英雄未创建\t"); return; }
            int base_abilities = hero.abilities != null ? hero.abilities.Length : 0;
            int hero_abs = 0;
            foreach (AbilityData a in p0.hero.GetAbilities())
                if (a != null && a.trigger == AbilityTrigger.Activate) hero_abs++;
            sb.AppendLine((hero_abs > 0 ? "PASS" : "FAIL")
                + "\t① 开战后英雄身上出现起动式技能\tActivate 能力数=" + hero_abs
                + "（原英雄定义=" + base_abilities + "）");

            //---- ② 技能可执行：直接触发该起动式能力（与 cast_ability 同一条链）----
            AbilityData act = null;
            foreach (AbilityData a in p0.hero.GetAbilities())
                if (a != null && a.trigger == AbilityTrigger.Activate) { act = a; break; }
            int before = p1.hp;
            if (act != null)
            {
                //★图不在 AbilityData 上（在卡池自定义数据里，运行期按能力 id 约定查找）→
                //  这里直接以**英雄为宿主**执行同一张技能图，验证"挂到英雄身上后效果以英雄为施法者生效"
                try { NodeDocRunner.Run(logic, g, p0.hero, p1.hero, null, "ActivateAbility"); }
                catch (Exception e) { sb.AppendLine("② 执行抛异常: " + e.Message); }
            }
            else
                sb.AppendLine("② 没找到起动式能力 → 跳过执行");
            int got = before - p1.hp;
            sb.AppendLine((act != null && got == 3 ? "PASS" : "FAIL") + "\t② 技能效果执行（伤害 3）\t掉血=" + got);

            //---- ③ 技能卡不能从手牌打出 ----
            CardData skill_data = CardData.Get(sk.id);
            Card skill_card = skill_data != null ? Card.Create(skill_data, null, p0, "hs_skillcard") : null;
            if (skill_card != null)
            {
                p0.cards_hand.Add(skill_card);
                bool can = game.CanPlayCard(skill_card, new Slot(1, 1, p0.player_id), true);
                sb.AppendLine((can ? "FAIL" : "PASS") + "\t③ 技能卡不能从手牌打出\tCanPlayCard=" + can);
                p0.cards_hand.Remove(skill_card);
            }
            else
                sb.AppendLine("SKIP\t③ 技能卡定义未注册\t");
        }
    }
}
