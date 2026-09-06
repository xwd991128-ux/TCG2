using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TcgEngine;
using TcgEngine.Workshop;
using TcgEngine.UI;

namespace TcgEngine.EditorTools
{
    /// <summary>
    /// 关键词管理窗口：集中查看/新建/编辑 Resources/Keywords 下的关键词，
    /// 规则图通过「编辑规则图」按钮跳转到运行时规则编辑器（Menu 场景运行中可用）。
    /// </summary>
    public class KeywordManagerWindow : EditorWindow
    {
        static KeywordManagerWindow()
        {
            //规则编辑器（运行时程序集）保存关键词图时写回资产
            GraphEditorPanel.keyword_asset_saver = kw =>
            {
                if (kw == null) return;
                EditorUtility.SetDirty(kw);
                AssetDatabase.SaveAssets();
            };
        }

        private Vector2 list_scroll;
        private Vector2 prop_scroll;
        private int selected = -1;
        private string new_id = "";

        [MenuItem("TcgEngine/关键词管理")]
        public static void OpenWindow()
        {
            KeywordData.Load();
            GetWindow<KeywordManagerWindow>("关键词管理");
        }

        private void OnGUI()
        {
            KeywordData.Load();
            List<KeywordData> keywords = KeywordData.GetAll();

            EditorGUILayout.BeginHorizontal();

            // ---- 左侧：关键词列表 ----
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            EditorGUILayout.LabelField($"关键词（{keywords.Count}）", EditorStyles.boldLabel);
            list_scroll = EditorGUILayout.BeginScrollView(list_scroll);
            for (int i = 0; i < keywords.Count; i++)
            {
                var kw = keywords[i];
                if (kw == null) continue;
                string tag = kw.HasMechanic ? "" : "（纯展示）";
                if (GUILayout.Toggle(i == selected, $"{kw.title}  [{kw.id}]{tag}", "Button"))
                    selected = i;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("新建 id：");
            new_id = EditorGUILayout.TextField(new_id);
            if (GUILayout.Button("新建关键词") && !string.IsNullOrWhiteSpace(new_id))
                CreateKeyword(new_id.Trim());
            EditorGUILayout.EndVertical();

            // ---- 右侧：属性编辑 ----
            EditorGUILayout.BeginVertical();
            prop_scroll = EditorGUILayout.BeginScrollView(prop_scroll);
            if (selected >= 0 && selected < keywords.Count && keywords[selected] != null)
                DrawKeyword(keywords[selected]);
            else
                EditorGUILayout.HelpBox("左侧选择一个关键词，或新建一个。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawKeyword(KeywordData kw)
        {
            Undo.RecordObject(kw, "Edit Keyword");

            kw.id = EditorGUILayout.TextField("id", kw.id);
            kw.title = EditorGUILayout.TextField("标题", kw.title);
            EditorGUILayout.LabelField("说明（悬浮预览/tooltip）");
            kw.desc = EditorGUILayout.TextArea(kw.desc ?? "", GUILayout.MinHeight(48));
            kw.status_type = (StatusType)EditorGUILayout.EnumPopup("原生机制状态", kw.status_type);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField($"自定义规则图（{kw.rules.Count} 条）", EditorStyles.boldLabel);
            for (int i = kw.rules.Count - 1; i >= 0; i--)
            {
                var rule = kw.rules[i];
                if (rule == null) continue;
                EditorGUILayout.BeginHorizontal();
                rule.trigger_action = EditorGUILayout.TextField("触发时机", rule.trigger_action);
                if (GUILayout.Button("编辑规则图", GUILayout.Width(90)))
                    OpenGraphEditor(kw, rule);
                if (GUILayout.Button("删除", GUILayout.Width(40)))
                {
                    kw.rules.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ 添加规则（规则图）"))
                kw.rules.Add(new KeywordRule { trigger_action = "OnPlay", graph = GraphBuilder.BuildSimpleGraph("OnPlay", "202001", 1) });

            EditorUtility.SetDirty(kw);
            if (GUI.changed)
                AssetDatabase.SaveAssets();
        }

        private void CreateKeyword(string id)
        {
            const string folder = "Assets/TcgEngine/Resources/Keywords";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/TcgEngine/Resources", "Keywords");
            string path = $"{folder}/{id}.asset";
            if (AssetDatabase.LoadAssetAtPath<KeywordData>(path) != null)
            {
                Debug.LogWarning("[关键词管理] 已存在同名关键词: " + id);
                return;
            }
            KeywordData keyword = ScriptableObject.CreateInstance<KeywordData>();
            keyword.id = id;
            keyword.title = id;
            AssetDatabase.CreateAsset(keyword, path);
            AssetDatabase.Refresh();
            KeywordData.Load();
            selected = KeywordData.GetAll().FindIndex(k => k != null && k.id == id);
        }

        /// <summary>跳转运行时规则编辑器编辑该规则的图（Menu 场景运行中可用）</summary>
        private void OpenGraphEditor(KeywordData kw, KeywordRule rule)
        {
            GraphEditorPanel editor = FindObjectOfType<GraphEditorPanel>(true);
            if (editor == null)
            {
                Debug.LogWarning("[关键词管理] 场景中未找到规则编辑器面板：请先运行 Menu 场景再点此按钮");
                EditorUtility.DisplayDialog("无法打开", "场景中没有 GraphEditorPanel（规则编辑器）。\n请先运行 Menu 场景，再回到本窗口点击「编辑规则图」。", "好的");
                return;
            }
            editor.OpenForKeyword(kw, rule);
            editor.Show();
        }
    }
}
