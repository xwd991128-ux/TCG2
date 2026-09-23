using UnityEngine;

namespace TcgEngine
{
    /// <summary>约束的作用范围（新增一种范围 = 加一个枚举值 + 校验器里一个分支，不动数据结构）</summary>
    public enum DeckScope
    {
        WholeDeck = 0,    //整副卡组（主卡 + 所有额外区）
        MainDeck = 10,    //仅主卡（不含额外区）
        ExtraZone = 20,   //指定额外区；zone_id 留空 = 遍历所有额外区
        SingleCard = 30,  //逐张卡各自判定（如"每张卡的费用都必须是偶数"）
    }

    /// <summary>约束作用的属性（新增一种 = 加一个枚举值 + 校验器里一个取值分支）</summary>
    public enum DeckAttr
    {
        CardCount = 0,      //卡牌张数
        DistinctCount = 10, //不同卡的种类数
        ManaCost = 20,      //费用
        Attack = 30,
        Hp = 40,
        Rarity = 50,        //稀有度（attr_id = 稀有度 id）
        CardType = 60,      //卡牌类型（attr_id = CardType 名）
        Trait = 70,         //种族（attr_id = 种族 id）
        Team = 80,          //阵营（attr_id = 阵营 id）
        Keyword = 90,       //关键词（attr_id = 关键词 id）
        Modifier = 100,     //是否带指定修饰（attr_id = DeckModifierData 的 id）
    }

    /// <summary>操作符</summary>
    public enum DeckOp
    {
        Max = 0,      //最多 value
        Min = 10,     //最少 value
        Equal = 20,   //恰好 value
        Even = 30,    //全部为偶数（attr=ManaCost 即"费用全偶数"）
        Odd = 40,     //全部为奇数
        Require = 50, //必须存在（attr_id 指向额外区 id / 修饰 id）
    }

    /// <summary>
    /// 一条构筑约束：作用于「谁(scope)」的「哪个属性(attr)」，按「怎么判(op)」与「数值(value)」比较。
    /// 判定所需的一切都在字段上 —— 新增一种规则通常只需在格式/修饰里多配一条，不用改代码。
    /// </summary>
    [System.Serializable]
    public class DeckConstraint
    {
        [Header("作用范围")]
        public DeckScope scope = DeckScope.MainDeck;
        public string zone_id;          //scope=ExtraZone 时指定哪个额外区（留空=所有额外区）

        [Header("属性与判定")]
        public DeckAttr attr = DeckAttr.CardCount;
        public DeckOp op = DeckOp.Max;
        public int value;               //op=Even/Odd/Require 时忽略
        public string attr_id;          //attr 的限定标识（稀有度/类型/种族/阵营/关键词/修饰 的 id，按 attr 解释）

        [Header("提示")]
        public string message;          //中文提示；留空时由 DeckValidator 按模板生成
    }
}
