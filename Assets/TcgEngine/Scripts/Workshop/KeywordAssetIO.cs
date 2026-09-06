using System;
using UnityEngine;

namespace TcgEngine.Workshop
{
    /// <summary>
    /// 关键词资产落盘桥：运行时面板（KeywordPanel）在 Editor 环境里需要把关键词写成资产，
    /// 但运行时程序集不能引用 UnityEditor，由 Editor 程序集启动时注册实现。
    /// 未注册（真机构建）时 CreateAsset 返回 false、SaveAsset 为空操作——关键词仅在内存生效。
    /// </summary>
    public static class KeywordAssetIO
    {
        /// <summary>把关键词对象落盘为资产（Editor 注册），成功返回 true</summary>
        public static Func<KeywordData, string, bool> create;

        /// <summary>关键词修改后写盘（Editor 注册）</summary>
        public static Action<KeywordData> save;

        public static bool CreateAsset(KeywordData keyword, string path)
        {
            return create != null && keyword != null && create.Invoke(keyword, path);
        }

        public static void SaveAsset(KeywordData keyword)
        {
            save?.Invoke(keyword);
        }
    }
}
