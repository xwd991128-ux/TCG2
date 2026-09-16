using System;
using System.Collections.Generic;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 种族（TraitData）资产落盘桥：TraitData 是 Resources 资产（ScriptableObject），
    /// 运行时程序集不能引用 UnityEditor，因此"创建/保存/删除资产"由 Editor 程序集注册实现
    /// （见 Editor/WorkshopTraitAssetBridge.cs，[InitializeOnLoad] 常驻注册）。
    /// 未注册（真机构建）时：CreateAsset 返回 false、Save/Delete 为空操作 —— 种族仅在内存生效。
    /// </summary>
    public static class TraitAssetIO
    {
        /// <summary>把种族对象落盘为资产（Editor 注册），成功返回 true</summary>
        public static Func<TraitData, string, bool> create;

        /// <summary>种族修改后写盘（Editor 注册）</summary>
        public static Action<TraitData> save;

        /// <summary>删除种族资产（Editor 注册）</summary>
        public static Action<TraitData> delete;

        /// <summary>新种族资产的存放目录（Editor 端创建目录）</summary>
        public const string ASSET_FOLDER = "Assets/TcgEngine/Resources/Traits/";

        public static bool CreateAsset(TraitData trait, string path = null)
        {
            if (trait == null || create == null)
                return false;
            return create.Invoke(trait, string.IsNullOrEmpty(path) ? ASSET_FOLDER + trait.id + ".asset" : path);
        }

        public static void SaveAsset(TraitData trait)
        {
            save?.Invoke(trait);
        }

        public static void DeleteAsset(TraitData trait)
        {
            delete?.Invoke(trait);
        }

        /// <summary>新建空种族：生成唯一 id + 占位标题，加入 TraitData.trait_list 并尝试落盘。</summary>
        public static TraitData New()
        {
            string id;
            int seed = 1;
            do
            {
                id = "trait_" + seed;
                seed++;
            }
            while (TraitData.Get(id) != null && seed < 10000);   //避免与既有种族重名（多为手工命名，几乎不冲突）

            TraitData t = ScriptableObject.CreateInstance<TraitData>();
            t.id = id;
            t.title = "新种族";
            TraitData.trait_list.Add(t);
            if (!CreateAsset(t))
                Debug.LogWarning("[TraitAssetIO] 新种族未落盘（编辑器桥未注册，仅内存生效）：" + id);
            return t;
        }

        /// <summary>删除种族：从静态列表移除，并请求 Editor 端删除资产（未注册时仅内存移除）。</summary>
        public static void Remove(TraitData t)
        {
            if (t == null)
                return;
            DeleteAsset(t);
            TraitData.trait_list.Remove(t);
        }

        /// <summary>种族改名（空标题回落为 id），并写盘</summary>
        public static void Rename(TraitData t, string title)
        {
            if (t == null)
                return;
            t.title = string.IsNullOrEmpty(title) ? t.id : title;
            SaveAsset(t);
        }

        /// <summary>供弹框列出全部种族（Resources 未加载时补一次 LoadAll）</summary>
        public static List<TraitData> All()
        {
            if (TraitData.trait_list.Count == 0)
                TraitData.trait_list.AddRange(Resources.LoadAll<TraitData>(""));
            return TraitData.trait_list;
        }
    }
}
