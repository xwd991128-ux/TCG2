namespace TcgEngine.Workshop
{
    /// <summary>
    /// 【区域（牌堆）名的唯一口径】——编辑器下拉、事件入口「生效区域」、增益属性里的牌堆选项，
    /// 以及运行期 Pile 值通道（编码 `"玩家id|区域名"`）**都从这里取名**。
    ///
    /// 为什么要有这张表：历史上"编辑器显示名"与"运行期内部名"各写一份（装备区 vs 装备、奥秘区 vs 奥秘），
    /// 只要有一处漏改，就会出现"编辑器勾了装备区、运行期当未知区域 → 静默返回空集合"（实测踩过）。
    /// 收敛到一张表后，新增/改名只需改这里。
    ///
    /// 三层名字：
    ///   1) 显示名 Display*：编辑器下拉给用户看的（玩家可见的 7 个区域 + 内部暂存区）
    ///   2) 内部名 Internal：运行期比较、Pile 编码、与 Player.cards_* 列表引用的规范名（Normalize 的输出）
    ///   3) 别名：兼容旧图 / zmcs 原文档写法（卡组、deck、场上、弃牌…）
    ///
    /// 注：「英雄」不是列表区域（= Player.hero 单卡），PileList 对它返回 null，读写走 ZoneCards / 英雄节点。
    /// </summary>
    public static class ZoneNames
    {
        // ---------------- 内部名（运行期唯一口径） ----------------

        public const string Board = "战场";
        public const string Hand = "手牌";
        public const string Deck = "牌库";
        public const string Discard = "墓地";
        public const string Equip = "装备";
        public const string Secret = "奥秘";
        public const string Hero = "英雄";
        public const string Temp = "暂存区";

        /// <summary>玩家可见的 7 个区域（编辑器下拉顺序；与事件入口「生效区域」同一套名字）</summary>
        public static readonly string[] Display =
        {
            "战场", "手牌", "牌库", "墓地", "装备区", "奥秘区", "英雄"
        };

        /// <summary>+ 内部暂存区（无 UI、玩家不可见：衍生卡未归区时的落地处，仅 Pile/移动类节点读写）</summary>
        public static readonly string[] DisplayWithTemp =
        {
            "战场", "手牌", "牌库", "墓地", "装备区", "奥秘区", "英雄", "暂存区"
        };

        /// <summary>+「任意」（事件入口「生效区域」多选用：选任意 = 全部牌堆）</summary>
        public static readonly string[] DisplayWithAny =
        {
            "任意", "战场", "手牌", "牌库", "墓地", "装备区", "奥秘区", "英雄"
        };

        /// <summary>把任意写法（显示名 / 内部名 / 英文别名）归一为**内部名**。
        /// 未知名字**原样返回**（不强行映射）：这样"勾了不存在的区域"会落在"恒不在任何牌堆"的判假分支，
        /// 而不是静默读到别的区域。TCG2 无 延迟区/技能区/道具栏/备牌区/任务区。</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return Board;
            switch (s)
            {
                case "牌库":
                case "卡组":
                case "deck":
                    return Deck;
                case "手牌":
                case "hand":
                    return Hand;
                case "墓地":
                case "弃牌":
                case "坟墓":
                case "discard":
                    return Discard;
                case "战场":
                case "场上":
                case "出战区":
                case "board":
                    return Board;
                case "装备":
                case "装备区":
                case "equip":
                    return Equip;
                case "奥秘":
                case "奥秘区":
                case "secret":
                    return Secret;
                case "英雄":
                case "英雄区":
                case "hero":
                    return Hero;
                case "暂存区":
                case "临时区":
                case "temp":
                    return Temp;
                default:
                    return s;
            }
        }

        /// <summary>内部名 → 编辑器显示名（日志/UI 展示用：装备→装备区、奥秘→奥秘区）。</summary>
        public static string ToDisplay(string internal_name)
        {
            switch (Normalize(internal_name))
            {
                case Equip: return "装备区";
                case Secret: return "奥秘区";
                default: return Normalize(internal_name);
            }
        }

        /// <summary>是否**列表区域**（有 Player.cards_* 列表引用）：
        /// 手牌/牌库/战场/墓地/装备/奥秘/暂存区 = 是；英雄（单卡）/ 未知 = 否。与 PileList 的口径一致。</summary>
        public static bool IsListZone(string internal_name)
        {
            switch (Normalize(internal_name))
            {
                case Hand:
                case Deck:
                case Board:
                case Discard:
                case Equip:
                case Secret:
                case Temp:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>是否**数据型区域**（可安全 insert/remove 的纯数据列表：手牌/牌库/墓地/奥秘/暂存区）。
        /// 战场/装备区带占位与佩戴关系、英雄是单卡 → 不属数据型（请走召唤/装备/英雄节点）。</summary>
        public static bool IsDataZone(string internal_name)
        {
            switch (Normalize(internal_name))
            {
                case Hand:
                case Deck:
                case Discard:
                case Secret:
                case Temp:
                    return true;
                default:
                    return false;
            }
        }
    }
}
