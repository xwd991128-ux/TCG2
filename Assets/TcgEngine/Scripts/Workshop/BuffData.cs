using System;
using System.Collections.Generic;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 增益属性项：key 为属性名（"攻击加成"/"生命加成"/"持续回合"/自定义名），value 为数值。
    /// 攻击/生命加成在施加时自动映射原生 StatusType.AddAttack/AddHP 参与真实战斗，
    /// 其余自定义属性存入 Buff 实例供规则图读取（106004）/写入（206003）。
    /// </summary>
    [Serializable]
    public class BuffProp
    {
        public string key;
        public int value;

        public BuffProp() { }
        public BuffProp(string key, int value) { this.key = key; this.value = value; }
    }

    /// <summary>
    /// 增益定义（BuffDefine 的纯数据 DTO，JSON 存储于 Workshop/buffs.json）
    /// </summary>
    [Serializable]
    public class BuffData
    {
        public string id;
        public string title;
        public string category = "增益";      // 增益/减益/光环/印记
        public string desc;                    // 说明，支持 <value> 占位符
        public int duration;                   // 默认持续回合（0=永久）
        public List<BuffProp> props = new List<BuffProp>();
        public GraphData graph;                // 增益效果规则图（节点编辑器数据，可为空；入口为「增益触发」事件节点）

        public string GetTitle() { return string.IsNullOrEmpty(title) ? id : title; }
        public string GetDesc()
        {
            if (string.IsNullOrEmpty(desc))
                return "";
            string d = desc;
            foreach (BuffProp p in props)
                d = d.Replace("<" + p.key + ">", p.value.ToString());
            return d;
        }
    }

    /// <summary>
    /// 增益池 JSON 容器（对应 Workshop/buffs.json）
    /// </summary>
    [Serializable]
    public class SerializableBuffPool
    {
        public string version = "1.0";
        public string timestamp = "";
        public List<BuffData> buffs = new List<BuffData>();
    }

    /// <summary>
    /// 对局中卡牌身上的增益实例（随 Card 一起序列化）。
    /// props 保存实例属性（含从定义复制的全部属性，攻击/生命加成同时映射为原生 AddAttack/AddHP 状态）。
    /// </summary>
    [Serializable]
    public class CardBuff
    {
        public string buff_id;                    // 引用 BuffData.id
        public List<BuffProp> props = new List<BuffProp>();
        public int duration = 0;                  // 剩余回合（0=永久）
        public bool permanent = true;

        [NonSerialized]
        private BuffData data = null;

        public CardBuff() { }

        public CardBuff(string buff_id, List<BuffProp> props, int duration)
        {
            this.buff_id = buff_id;
            this.permanent = (duration <= 0);
            this.duration = duration;
            if (props != null)
                this.props.AddRange(props);
        }

        public BuffData BuffData
        {
            get
            {
                if (data == null || data.id != buff_id)
                    data = BuffPoolIO.Get(buff_id);
                return data;
            }
        }

        public int GetProp(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;
            foreach (BuffProp p in props)
            {
                if (p.key == key)
                    return p.value;
            }
            return 0;
        }

        public void SetProp(string key, int value)
        {
            foreach (BuffProp p in props)
            {
                if (p.key == key)
                {
                    p.value = value;
                    return;
                }
            }
            props.Add(new BuffProp(key, value));
        }
    }
}
