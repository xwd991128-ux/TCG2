using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 战斗界面自定义按钮（全局按钮）的加载/保存核心。
    /// 存储：Application.persistentDataPath/Workshop/buttons.json（与 buffs.json 同目录）。
    /// 启动时由 DataLoader 调用 LoadAll 注入静态缓存；战斗界面（GameUI）与服务器（GameLogic）读取。
    /// 所有按钮共享一张规则图（一图多按钮），图内事件节点用 button_id 字段区分按钮。
    /// </summary>
    public static class BattleButtonIO
    {
        private static BattleButtonConfig config = null;   // 当前按钮配置（含按钮列表 + 共享图）
        private static readonly Dictionary<string, BattleButtonData> button_dict = new Dictionary<string, BattleButtonData>();

        public static string SaveFolder
        {
            get { return Path.Combine(Application.persistentDataPath, "Workshop"); }
        }

        public static string ButtonFile
        {
            get { return Path.Combine(SaveFolder, "buttons.json"); }
        }

        public static BattleButtonConfig GetConfig()
        {
            if (config == null)
            {
                config = new BattleButtonConfig();
                config.buttons = new BattleButtonData[0];
            }
            return config;
        }

        public static List<BattleButtonData> GetAll()
        {
            List<BattleButtonData> list = new List<BattleButtonData>();
            BattleButtonConfig cfg = GetConfig();
            if (cfg.buttons != null)
                list.AddRange(cfg.buttons);
            return list;
        }

        public static BattleButtonData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            button_dict.TryGetValue(id, out BattleButtonData b);
            return b;
        }

        /// <summary>启动时/进入战斗前加载按钮配置（DataLoader 在 BuffPoolIO.LoadAll 后调用）</summary>
        public static void LoadAll()
        {
            config = null;
            button_dict.Clear();
            if (!File.Exists(ButtonFile))
            {
                //无文件：创建默认空配置（不写盘，首次保存时落盘）
                config = new BattleButtonConfig();
                config.buttons = new BattleButtonData[0];
                return;
            }
            try
            {
                string json = File.ReadAllText(ButtonFile);
                BattleButtonConfig cfg = JsonUtility.FromJson<BattleButtonConfig>(json);
                if (cfg == null)
                    cfg = new BattleButtonConfig();
                if (cfg.buttons == null)
                    cfg.buttons = new BattleButtonData[0];
                config = cfg;
                foreach (BattleButtonData b in cfg.buttons)
                {
                    if (b == null || string.IsNullOrEmpty(b.id))
                        continue;
                    button_dict[b.id] = b;
                }
                if (cfg.graph != null && string.IsNullOrEmpty(cfg.graph.name))
                    cfg.graph.name = "battle_buttons";
                Debug.Log("[BattleButtonIO] 加载按钮配置 " + GetAll().Count + " 个：" + ButtonFile);
            }
            catch (Exception e)
            {
                Debug.LogError("[BattleButtonIO] 加载按钮配置失败: " + ButtonFile + " " + e.Message);
            }
        }

        /// <summary>保存按钮配置到 buttons.json（按钮编辑器保存按钮调用）</summary>
        public static void SaveAll()
        {
            try
            {
                if (!Directory.Exists(SaveFolder))
                    Directory.CreateDirectory(SaveFolder);
                BattleButtonConfig cfg = GetConfig();
                cfg.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(ButtonFile, JsonUtility.ToJson(cfg, true));
                Debug.Log("[BattleButtonIO] 已保存按钮配置 " + GetAll().Count + " 个 → " + ButtonFile);
            }
            catch (Exception e)
            {
                Debug.LogError("[BattleButtonIO] 保存按钮配置失败: " + e.Message);
            }
        }

        /// <summary>新增按钮（生成唯一 id）</summary>
        public static BattleButtonData New()
        {
            BattleButtonData b = new BattleButtonData();
            b.id = "btn_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            b.title = "新按钮";
            b.desc = "";
            Add(b);
            return b;
        }

        /// <summary>添加/覆盖按钮定义</summary>
        public static void Add(BattleButtonData b)
        {
            if (b == null || string.IsNullOrEmpty(b.id))
                return;
            BattleButtonConfig cfg = GetConfig();
            List<BattleButtonData> list = new List<BattleButtonData>(cfg.buttons ?? new BattleButtonData[0]);
            //覆盖同名按钮（先移除旧项）
            list.RemoveAll(x => x != null && x.id == b.id);
            list.Add(b);
            cfg.buttons = list.ToArray();
            button_dict[b.id] = b;
        }

        /// <summary>删除按钮定义（同时从缓存移除）</summary>
        public static void Remove(BattleButtonData b)
        {
            if (b == null)
                return;
            BattleButtonConfig cfg = GetConfig();
            List<BattleButtonData> list = new List<BattleButtonData>(cfg.buttons ?? new BattleButtonData[0]);
            list.RemoveAll(x => x == b || (x != null && x.id == b.id));
            cfg.buttons = list.ToArray();
            button_dict.Remove(b.id);
        }

        /// <summary>深拷贝按钮定义（复制 = 新 id + 原显示内容，图共享不复制——一图多按钮）</summary>
        public static BattleButtonData Duplicate(BattleButtonData src)
        {
            BattleButtonData b = new BattleButtonData();
            b.id = "btn_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            b.title = src.title + " 副本";
            b.desc = src.desc;
            Add(b);
            return b;
        }

        /// <summary>从按钮列表构建按钮 id 下拉（规则图 button_id 字段用）</summary>
        public static List<string> GetOptionIds()
        {
            List<string> ids = new List<string>();
            foreach (BattleButtonData b in GetAll())
            {
                if (!string.IsNullOrEmpty(b.id))
                    ids.Add(b.id);
            }
            return ids;
        }
    }
}
