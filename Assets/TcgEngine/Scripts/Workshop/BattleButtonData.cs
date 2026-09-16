using System;
using System.Collections.Generic;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 战斗界面自定义按钮定义（全局按钮，不属于某张卡）。
    /// 显示内容（title / background / desc / 自定义参数）由按钮编辑器配置，
    /// 触发动作由全局按钮图（BattleButtonConfig.graph）配置，
    /// 图内用「点击按钮时」事件节点的 button_id 字段区分不同按钮（一图多按钮分支）。
    /// 自定义参数与「增益 / 卡牌」完全同规格：名称:类型[:数组] + 初始值（复用 BuffCustomProp / BuffPropType）。
    /// </summary>
    [Serializable]
    public class BattleButtonData
    {
        public string id;          // 按钮唯一标识（规则图 button_id 字段匹配用）
        public string title;       // 按钮显示文本
        public string desc;        // 描述（可选）
        public string background;  // 按钮背景图文件名（Workshop/Art 下，与卡图同一目录；空=未设置）

        /// <summary>自定义参数声明（名称 / 类型 / 是否为数组 / 初始值）</summary>
        public List<BuffCustomProp> custom_prop_defs = new List<BuffCustomProp>();

        public string GetTitle()
        {
            return string.IsNullOrEmpty(title) ? id : title;
        }

        // ==================== 自定义参数（与 BuffData / CardCustomData 同名同语义） ====================

        /// <summary>自定义参数列表（幂等自愈）</summary>
        public List<BuffCustomProp> EnsureCustomPropDefs()
        {
            if (custom_prop_defs == null)
                custom_prop_defs = new List<BuffCustomProp>();
            return custom_prop_defs;
        }

        public BuffCustomProp FindCustomProp(string name)
        {
            if (custom_prop_defs == null || string.IsNullOrEmpty(name))
                return null;
            foreach (BuffCustomProp c in custom_prop_defs)
            {
                if (c != null && c.name == name)
                    return c;
            }
            return null;
        }

        /// <summary>取某自定义参数的初始值（找不到→0）</summary>
        public int CustomPropInit(string name)
        {
            BuffCustomProp c = FindCustomProp(name);
            return c != null ? c.InitInt() : 0;
        }

        /// <summary>新增（同名视为编辑：覆盖类型 / 是否数组 / 初始值）</summary>
        public BuffCustomProp AddCustomProp(string name, string type, bool is_array, string init_value)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            EnsureCustomPropDefs();
            BuffCustomProp exist = FindCustomProp(name);
            if (exist == null)
            {
                exist = new BuffCustomProp(name, type, is_array, init_value);
                custom_prop_defs.Add(exist);
            }
            else
            {
                exist.type = string.IsNullOrEmpty(type) ? BuffPropType.Int : type;
                exist.is_array = is_array;
                exist.init_value = init_value ?? "";
            }
            return exist;
        }

        public void RemoveCustomProp(string name)
        {
            if (custom_prop_defs != null)
                custom_prop_defs.RemoveAll(c => c == null || c.name == name);
        }
    }

    /// <summary>
    /// 全局按钮配置（buttons.json 根对象）：按钮定义列表 + 全局共享按钮图。
    /// </summary>
    [Serializable]
    public class BattleButtonConfig
    {
        public string timestamp;                                 // 保存时间（供排查用）
        public BattleButtonData[] buttons = new BattleButtonData[0];  // 按钮定义列表（"按钮池"）
        public GraphData graph;                                  // 兼容字段：= graphs[0]（旧工具/旧编译仍读它）
        public List<CardEffectData> graphs = new List<CardEffectData>();   // 多张按钮图（每张可各配「点击按钮时/后」入口）

        /// <summary>多张按钮图（幂等）：首次调用把旧单图 graph 迁移成 graphs[0]，并把 graph 双写指向第一张。</summary>
        public List<CardEffectData> EnsureGraphs()
        {
            if (graphs == null)
                graphs = new List<CardEffectData>();
            if (graphs.Count == 0)
            {
                if (graph == null)
                    graph = new GraphData();
                graphs.Add(new CardEffectData { name = "按钮图1", graph = graph });
            }
            if (graphs[0] != null && graphs[0].graph != null)
                graph = graphs[0].graph;
            return graphs;
        }
    }
}
