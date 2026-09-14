using System.IO;
using UnityEditor;
using UnityEngine;
using TcgEngine.Audio;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 场景 BGM 配置工具：一键生成 `Assets/TcgEngine/Resources/SceneBgmConfig.asset`（已存在则选中它）。
    /// 生成后在 Inspector 里为各界面（主菜单/编辑/构筑/对战/结算…）选择 BGM：bgm_id 填音乐库里的条目 id 或显示名，
    /// 留空 = 该界面不主动切歌（走默认兜底或保持当前音乐）。
    /// </summary>
    public static class SceneBgmConfigBuilder
    {
        private const string Folder = "Assets/TcgEngine/Resources";
        private const string AssetPath = Folder + "/SceneBgmConfig.asset";

        [MenuItem("TcgEngine/创建场景BGM配置", false, 11)]
        public static void CreateOrSelect()
        {
            SceneBgmConfig existing = AssetDatabase.LoadAssetAtPath<SceneBgmConfig>(AssetPath);
            if (existing == null)
            {
                if (!Directory.Exists(Folder))
                    Directory.CreateDirectory(Folder);

                SceneBgmConfig cfg = ScriptableObject.CreateInstance<SceneBgmConfig>();
                cfg.EnsureDefaultEntries();
                AssetDatabase.CreateAsset(cfg, AssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                SceneBgmConfig.ClearCache();
                existing = AssetDatabase.LoadAssetAtPath<SceneBgmConfig>(AssetPath);
                Debug.Log("[BGM] 已创建场景BGM配置：" + AssetPath +
                    "。请在 Inspector 里为每个界面填写 bgm_id（音乐库条目的 id 或显示名），留空则用默认兜底曲。");
            }
            else
            {
                SceneBgmConfig.ClearCache();
                Debug.Log("[BGM] 场景BGM配置已存在：" + AssetPath);
            }

            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
            }
        }

        /// <summary>把当前音乐库里的条目打印到 Console（方便复制 id/名字填进配置表）</summary>
        [MenuItem("TcgEngine/打印音乐库条目", false, 12)]
        public static void PrintLibrary()
        {
            BgmLibrary.Reload();
            var list = BgmLibrary.GetAll();
            if (list.Count == 0)
            {
                Debug.Log("[BGM] 音乐库为空：可在主菜单「音乐库」导入，或把 AudioClip 放进任意 Resources/BGM/ 目录。");
                return;
            }
            Debug.Log("[BGM] 音乐库共 " + list.Count + " 条（id | 名称 | 来源）：");
            for (int i = 0; i < list.Count; i++)
            {
                BgmEntry e = list[i];
                Debug.Log("   " + e.id + " | " + e.title + " | " + BgmLibrary.SourceLabel(e)
                    + (e.id == BgmLibrary.Data.default_bgm ? "  ← 当前默认兜底" : ""));
            }
        }
    }
}
