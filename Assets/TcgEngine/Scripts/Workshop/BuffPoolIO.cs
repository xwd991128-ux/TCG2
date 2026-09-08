using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 增益定义（BuffData）池的加载/保存核心。
    /// 存储：Application.persistentDataPath/Workshop/buffs.json（与自定义卡池同目录）。
    /// 启动时由 DataLoader 调用 LoadAll 注入静态缓存，供规则图 206001/106003 等节点引用。
    /// </summary>
    public static class BuffPoolIO
    {
        /// <summary>运行时加载的全部增益定义</summary>
        public static readonly List<BuffData> buffs = new List<BuffData>();

        private static readonly Dictionary<string, BuffData> buff_dict = new Dictionary<string, BuffData>();

        public static string SaveFolder
        {
            get { return Path.Combine(Application.persistentDataPath, "Workshop"); }
        }

        public static string BuffFile
        {
            get { return Path.Combine(SaveFolder, "buffs.json"); }
        }

        public static List<BuffData> GetAll() { return buffs; }

        public static BuffData Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            buff_dict.TryGetValue(id, out BuffData b);
            return b;
        }

        /// <summary>启动时加载增益池（DataLoader 在 CardPoolIO.LoadCustomPools 后调用）</summary>
        public static void LoadAll()
        {
            buffs.Clear();
            buff_dict.Clear();
            if (!File.Exists(BuffFile))
                return;
            try
            {
                string json = File.ReadAllText(BuffFile);
                SerializableBuffPool pool = JsonUtility.FromJson<SerializableBuffPool>(json);
                if (pool == null || pool.buffs == null)
                    return;
                foreach (BuffData b in pool.buffs)
                {
                    if (b == null || string.IsNullOrEmpty(b.id))
                        continue;
                    if (b.props == null)
                        b.props = new List<BuffProp>();
                    buffs.Add(b);
                    buff_dict[b.id] = b;
                }
                Debug.Log("[BuffPoolIO] 加载增益池 " + buffs.Count + " 个：" + BuffFile);
            }
            catch (Exception e)
            {
                Debug.LogError("[BuffPoolIO] 加载增益池失败: " + BuffFile + " " + e.Message);
            }
        }

        /// <summary>保存增益池到 buffs.json（编辑器保存按钮调用）</summary>
        public static void SaveAll()
        {
            try
            {
                if (!Directory.Exists(SaveFolder))
                    Directory.CreateDirectory(SaveFolder);
                SerializableBuffPool pool = new SerializableBuffPool();
                pool.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                pool.buffs.AddRange(buffs);
                File.WriteAllText(BuffFile, JsonUtility.ToJson(pool, true));
                Debug.Log("[BuffPoolIO] 已保存增益池 " + buffs.Count + " 个 → " + BuffFile);
            }
            catch (Exception e)
            {
                Debug.LogError("[BuffPoolIO] 保存增益池失败: " + e.Message);
            }
        }

        /// <summary>创建新增益（生成唯一 id）</summary>
        public static BuffData New()
        {
            BuffData b = new BuffData();
            b.id = "buff_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            b.title = "新增益";
            b.props.Add(new BuffProp("攻击加成", 0));
            b.props.Add(new BuffProp("生命加成", 0));
            buffs.Add(b);
            buff_dict[b.id] = b;
            return b;
        }

        /// <summary>删除增益定义（同时从缓存移除）</summary>
        public static void Remove(BuffData b)
        {
            if (b == null)
                return;
            buffs.Remove(b);
            buff_dict.Remove(b.id);
        }

        /// <summary>深拷贝增益定义（复制 = 新 id + 原属性 + 原效果图深拷贝，图互不共享）</summary>
        public static BuffData Duplicate(BuffData src)
        {
            BuffData b = new BuffData();
            b.id = "buff_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            b.title = src.title + " 副本";
            b.category = src.category;
            b.desc = src.desc;
            b.duration = src.duration;
            b.props = new List<BuffProp>();
            if (src.props != null)
            {
                foreach (BuffProp p in src.props)
                    b.props.Add(new BuffProp(p.key, p.value));
            }
            //效果图深拷贝（JsonUtility 往返），避免复制增益与源增益共享同一个图
            if (src.graph != null)
            {
                try
                {
                    b.graph = JsonUtility.FromJson<GraphData>(JsonUtility.ToJson(src.graph));
                }
                catch (Exception e)
                {
                    Debug.LogError("[BuffPoolIO] 复制增益效果图失败: " + e.Message);
                    b.graph = null;
                }
            }
            buffs.Add(b);
            buff_dict[b.id] = b;
            return b;
        }

        /// <summary>从 buffs 列表构建增益名下拉（规则图 206001 的 buff_id 字段用）</summary>
        public static List<string> GetOptionTitles()
        {
            List<string> titles = new List<string>();
            foreach (BuffData b in buffs)
                titles.Add(b.GetTitle());
            return titles;
        }
    }
}
