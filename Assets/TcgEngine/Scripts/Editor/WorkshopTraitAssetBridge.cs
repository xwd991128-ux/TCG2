using System.IO;
using UnityEditor;
using UnityEngine;
using TcgEngine.Workshop;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 工作台资源落盘桥的常驻注册（[InitializeOnLoad]）：
    ///   ① 种族（TraitData）：创建 / 保存 / 删除资产 —— 供变量选择弹框的「新增/编辑/删除」使用；
    ///   ② 关键词（KeywordAssetIO）：兜底注册，避免"必须打开过 KeywordManagerWindow 才可落盘"的隐性依赖
    ///      （原注册在 KeywordManagerWindow 的静态构造里，未打开过该窗口时 CreateAsset 恒失败）。
    /// 只在游戏编辑器（非播放器）生效；真机构建时这些桥保持未注册 → 运行时仅内存生效。
    /// </summary>
    [InitializeOnLoad]
    public static class WorkshopTraitAssetBridge
    {
        static WorkshopTraitAssetBridge()
        {
            TraitAssetIO.create = (trait, path) =>
            {
                if (trait == null)
                    return false;
                try
                {
                    string folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                        Directory.CreateDirectory(folder);
                    AssetDatabase.CreateAsset(trait, path);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[种族] 已创建资产 " + path);
                    return true;
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[种族] 资产创建失败: " + e.Message);
                    return false;
                }
            };

            TraitAssetIO.save = trait =>
            {
                if (trait == null)
                    return;
                EditorUtility.SetDirty(trait);
                AssetDatabase.SaveAssets();
            };

            TraitAssetIO.delete = trait =>
            {
                if (trait == null)
                    return;
                string path = AssetDatabase.GetAssetPath(trait);
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[种族] 已删除资产 " + path);
                }
            };

            //关键词资产桥兜底（不覆盖已有注册，避免改变既有行为）
            if (KeywordAssetIO.create == null)
            {
                KeywordAssetIO.create = (kw, path) =>
                {
                    if (kw == null)
                        return false;
                    try
                    {
                        string folder = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                            Directory.CreateDirectory(folder);
                        AssetDatabase.CreateAsset(kw, path);
                        AssetDatabase.SaveAssets();
                        return true;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("[关键词] 资产创建失败: " + e.Message);
                        return false;
                    }
                };
            }
            if (KeywordAssetIO.save == null)
            {
                KeywordAssetIO.save = kw =>
                {
                    if (kw == null)
                        return;
                    EditorUtility.SetDirty(kw);
                    AssetDatabase.SaveAssets();
                };
            }
            if (KeywordAssetIO.delete == null)
            {
                KeywordAssetIO.delete = kw =>
                {
                    if (kw == null)
                        return;
                    string path = AssetDatabase.GetAssetPath(kw);
                    if (!string.IsNullOrEmpty(path))
                    {
                        AssetDatabase.DeleteAsset(path);
                        AssetDatabase.SaveAssets();
                        Debug.Log("[关键词] 已删除资产 " + path);
                    }
                };
            }
        }
    }
}
