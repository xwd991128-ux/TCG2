using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 玩家自定义节点（DIY 节点）的加载/保存核心。
    /// 存储：Application.persistentDataPath/Workshop/custom_nodes.json（与 buffs.json / buttons.json 同目录）。
    /// 启动时由 DataLoader 调用 LoadAll 注入静态缓存（与 BattleButtonIO 同规）；
    /// 节点库（GraphEditorPanel.AllPresets → BuildCustomNodePresets）与运行时都从这里取。
    /// </summary>
    public static class CustomNodeIO
    {
        private static CustomNodeConfig config = null;
        private static readonly Dictionary<string, CustomNodeData> dict = new Dictionary<string, CustomNodeData>();

        public static string SaveFolder
        {
            get { return Path.Combine(Application.persistentDataPath, "Workshop"); }
        }

        public static string NodeFile
        {
            get { return Path.Combine(SaveFolder, "custom_nodes.json"); }
        }

        public static CustomNodeConfig GetConfig()
        {
            if (config == null)
            {
                config = new CustomNodeConfig();
                config.nodes = new CustomNodeData[0];
            }
            return config;
        }

        public static List<CustomNodeData> GetAll()
        {
            List<CustomNodeData> list = new List<CustomNodeData>();
            CustomNodeConfig cfg = GetConfig();
            if (cfg.nodes != null)
                list.AddRange(cfg.nodes);
            return list;
        }

        public static CustomNodeData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            CustomNodeData n;
            dict.TryGetValue(id, out n);
            return n;
        }

        /// <summary>启动时加载（DataLoader 在 BuffPoolIO/BattleButtonIO 之后调用）</summary>
        public static void LoadAll()
        {
            config = null;
            dict.Clear();
            if (!File.Exists(NodeFile))
            {
                config = new CustomNodeConfig();
                config.nodes = new CustomNodeData[0];   //无文件：空配置（不写盘，首次保存时落盘）
                return;
            }
            try
            {
                string json = File.ReadAllText(NodeFile);
                CustomNodeConfig cfg = JsonUtility.FromJson<CustomNodeConfig>(json);
                if (cfg == null)
                    cfg = new CustomNodeConfig();
                if (cfg.nodes == null)
                    cfg.nodes = new CustomNodeData[0];
                config = cfg;
                foreach (CustomNodeData n in cfg.nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.id))
                        continue;
                    if (n.inputs == null) n.inputs = new List<CustomNodePort>();
                    if (n.outputs == null) n.outputs = new List<CustomNodePort>();
                    if (n.graphs == null) n.graphs = new List<CardEffectData>();
                    dict[n.id] = n;
                }
                Debug.Log("[CustomNodeIO] 加载自定义节点 " + GetAll().Count + " 个：" + NodeFile);
            }
            catch (Exception e)
            {
                Debug.LogError("[CustomNodeIO] 加载自定义节点失败: " + NodeFile + " " + e.Message);
            }
        }

        /// <summary>保存到 custom_nodes.json（自定义节点编辑器保存按钮调用）</summary>
        public static void SaveAll()
        {
            try
            {
                if (!Directory.Exists(SaveFolder))
                    Directory.CreateDirectory(SaveFolder);
                CustomNodeConfig cfg = GetConfig();
                cfg.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(NodeFile, JsonUtility.ToJson(cfg, true));
                Debug.Log("[CustomNodeIO] 已保存自定义节点 " + GetAll().Count + " 个 → " + NodeFile);
            }
            catch (Exception e)
            {
                Debug.LogError("[CustomNodeIO] 保存自定义节点失败: " + e.Message);
            }
        }

        /// <summary>新增（生成唯一 id；默认动作节点）</summary>
        public static CustomNodeData New(CustomNodeKind kind = CustomNodeKind.Action)
        {
            CustomNodeData n = new CustomNodeData();
            n.id = "cn_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            n.kind = (int)kind;
            n.title = kind == CustomNodeKind.Action ? "新动作节点"
                : (kind == CustomNodeKind.Function ? "新函数节点" : "新事件节点");
            n.inputs.Add(new CustomNodePort("输入1", nameof(NodeValueType.Int32)));
            n.outputs.Add(new CustomNodePort("输出1", nameof(NodeValueType.Int32)));
            n.EnsureGraphs();
            Add(n);
            return n;
        }

        /// <summary>添加/覆盖</summary>
        public static void Add(CustomNodeData n)
        {
            if (n == null || string.IsNullOrEmpty(n.id))
                return;
            CustomNodeConfig cfg = GetConfig();
            List<CustomNodeData> list = new List<CustomNodeData>(cfg.nodes ?? new CustomNodeData[0]);
            list.RemoveAll(x => x != null && x.id == n.id);
            list.Add(n);
            cfg.nodes = list.ToArray();
            dict[n.id] = n;
        }

        public static void Remove(CustomNodeData n)
        {
            if (n == null)
                return;
            CustomNodeConfig cfg = GetConfig();
            List<CustomNodeData> list = new List<CustomNodeData>(cfg.nodes ?? new CustomNodeData[0]);
            list.RemoveAll(x => x == n || (x != null && x.id == n.id));
            cfg.nodes = list.ToArray();
            dict.Remove(n.id);
        }

        /// <summary>复制（新 id + 标题加"副本" + 图深拷贝）</summary>
        public static CustomNodeData Duplicate(CustomNodeData src)
        {
            if (src == null)
                return null;
            CustomNodeData n = JsonUtility.FromJson<CustomNodeData>(JsonUtility.ToJson(src));
            n.id = "cn_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            n.title = src.GetTitle() + " 副本";
            //图内的入口节点 action 里含旧 id → 改写成本节点的新 id（否则复制品的图会被认成原节点的）
            RewriteActionId(n, src.ActionId, n.ActionId);
            Add(n);
            return n;
        }

        /// <summary>把图内所有节点的 action 从 old 改成 @new（复制节点时用）</summary>
        private static void RewriteActionId(CustomNodeData n, string old_action, string new_action)
        {
            if (n == null || string.IsNullOrEmpty(old_action))
                return;
            n.EnsureGraphs();
            foreach (CardEffectData e in n.graphs)
            {
                if (e == null || e.graph == null || e.graph.nodes == null)
                    continue;
                foreach (GraphNode g in e.graph.nodes)
                {
                    if (g != null && g.action == old_action)
                        g.action = new_action;
                }
            }
        }
    }
}
