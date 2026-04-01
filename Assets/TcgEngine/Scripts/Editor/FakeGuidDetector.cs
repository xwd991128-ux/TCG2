using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class FakeGuidDetector : EditorWindow
{
    private List<string> fakeGuidFiles = new List<string>();
    private List<string> referencedByFiles = new List<string>();
    private Vector2 scrollPosition;
    private bool scanned = false;

    [MenuItem("Tools/Fake GUID Detector")]
    public static void ShowWindow()
    {
        GetWindow<FakeGuidDetector>("Fake GUID Detector");
    }

    void OnGUI()
    {
        GUILayout.Label("Fake GUID Detector", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This tool detects .meta files with fake GUIDs (sequential patterns like a1b2c3d4... or 01234567...)\n\n" +
            "Steps:\n" +
            "1. Click 'Scan for Fake GUIDs'\n" +
            "2. Review the list of problematic files\n" +
            "3. Click 'Delete Fake GUID .meta Files'\n" +
            "4. Unity will regenerate proper GUIDs\n" +
            "5. Fix any 'Missing' references in Inspector",
            MessageType.Info);

        GUILayout.Space(10);

        if (GUILayout.Button("Scan for Fake GUIDs", GUILayout.Height(40)))
        {
            ScanForFakeGuids();
        }

        GUILayout.Space(10);

        if (scanned)
        {
            GUILayout.Label($"Found {fakeGuidFiles.Count} files with fake GUIDs:", EditorStyles.boldLabel);
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));
            foreach (var file in fakeGuidFiles)
            {
                EditorGUILayout.SelectableLabel(file, EditorStyles.helpBox, GUILayout.Height(20));
            }
            GUILayout.EndScrollView();

            GUILayout.Space(10);

            if (referencedByFiles.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Warning: {referencedByFiles.Count} files reference these fake GUIDs!\n" +
                    "After deleting .meta files, you must manually fix these references in Unity Inspector.",
                    MessageType.Warning);
                
                if (GUILayout.Button("Show Referencing Files"))
                {
                    string paths = string.Join("\n", referencedByFiles);
                    EditorUtility.DisplayDialog("Referencing Files", paths, "OK");
                }
            }

            GUILayout.Space(10);

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Delete Fake GUID .meta Files", GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog("Confirm Delete",
                    $"Are you sure you want to delete {fakeGuidFiles.Count} .meta files?\n\n" +
                    "This will cause Unity to regenerate GUIDs.\n" +
                    "You must manually fix any 'Missing' references afterwards.",
                    "Yes, Delete", "Cancel"))
                {
                    DeleteFakeMetaFiles();
                }
            }
            GUI.backgroundColor = Color.white;
        }
    }

    void ScanForFakeGuids()
    {
        fakeGuidFiles.Clear();
        referencedByFiles.Clear();
        
        string assetsPath = Application.dataPath;
        string[] metaFiles = Directory.GetFiles(assetsPath, "*.meta", SearchOption.AllDirectories);

        Regex fakeGuidPattern = new Regex(@"guid:\s*([a-f0-9]+)", RegexOptions.IgnoreCase);
        
        foreach (var metaFile in metaFiles)
        {
            string content = File.ReadAllText(metaFile);
            Match match = fakeGuidPattern.Match(content);
            
            if (match.Success)
            {
                string guid = match.Groups[1].Value;
                
                if (IsFakeGuid(guid))
                {
                    string relativePath = "Assets" + metaFile.Substring(assetsPath.Length);
                    fakeGuidFiles.Add(relativePath);
                    
                    FindReferencesToGuid(guid);
                }
            }
        }
        
        scanned = true;
        Debug.Log($"[FakeGuidDetector] Found {fakeGuidFiles.Count} files with fake GUIDs");
    }

    bool IsFakeGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return false;
        
        if (guid.Length != 32)
            return true;

        if (IsSequentialPattern(guid))
            return true;

        if (IsSimplePattern(guid))
            return true;

        return false;
    }

    bool IsSequentialPattern(string guid)
    {
        string lower = guid.ToLower();
        
        if (lower.StartsWith("01234567") || lower.StartsWith("12345678") ||
            lower.StartsWith("23456789") || lower.StartsWith("34567890") ||
            lower.StartsWith("45678901") || lower.StartsWith("56789012") ||
            lower.StartsWith("67890123") || lower.StartsWith("78901234") ||
            lower.StartsWith("89012345") || lower.StartsWith("90123456"))
            return true;

        if (lower.StartsWith("a1b2c3d4") || lower.StartsWith("b2c3d4e5") ||
            lower.StartsWith("c3d4e5f6") || lower.StartsWith("d4e5f6a7") ||
            lower.StartsWith("e5f6a7b8") || lower.StartsWith("f6a7b8c9") ||
            lower.StartsWith("a7b8c9d0") || lower.StartsWith("b8c9d0e1") ||
            lower.StartsWith("c9d0e1f2") || lower.StartsWith("d0e1f2a3") ||
            lower.StartsWith("e1f2a3b4") || lower.StartsWith("f2a3b4c5") ||
            lower.StartsWith("a3b4c5d6") || lower.StartsWith("b4c5d6e7") ||
            lower.StartsWith("c5d6e7f8") || lower.StartsWith("d6e7f8a9") ||
            lower.StartsWith("e6f7a8b9") || lower.StartsWith("f7a8b9c0"))
            return true;

        return false;
    }

    bool IsSimplePattern(string guid)
    {
        string lower = guid.ToLower();
        
        if (lower == "a1b2c3d4e5f6789012345678abcdefaa" ||
            lower == "a1b2c3d4e5f6789012345678abcdef99")
            return true;

        for (int i = 0; i < 16; i++)
        {
            char c = (char)('a' + i);
            string pattern = new string(c, 32);
            if (lower == pattern)
                return true;
        }

        for (int i = 0; i < 10; i++)
        {
            char c = (char)('0' + i);
            string pattern = new string(c, 32);
            if (lower == pattern)
                return true;
        }

        return false;
    }

    void FindReferencesToGuid(string guid)
    {
        string assetsPath = Application.dataPath;
        string[] assetFiles = Directory.GetFiles(assetsPath, "*.asset", SearchOption.AllDirectories);
        string[] prefabFiles = Directory.GetFiles(assetsPath, "*.prefab", SearchOption.AllDirectories);
        
        var allFiles = new List<string>(assetFiles);
        allFiles.AddRange(prefabFiles);

        foreach (var file in allFiles)
        {
            if (file.EndsWith(".meta")) continue;
            
            string content = File.ReadAllText(file);
            if (content.Contains(guid))
            {
                string relativePath = "Assets" + file.Substring(assetsPath.Length);
                if (!referencedByFiles.Contains(relativePath))
                    referencedByFiles.Add(relativePath);
            }
        }
    }

    void DeleteFakeMetaFiles()
    {
        int deleted = 0;
        foreach (var metaFile in fakeGuidFiles)
        {
            string fullPath = Path.Combine(Application.dataPath, "..", metaFile);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                deleted++;
            }
        }
        
        AssetDatabase.Refresh();
        Debug.Log($"[FakeGuidDetector] Deleted {deleted} .meta files. Unity will regenerate proper GUIDs.");
        
        EditorUtility.DisplayDialog("Complete",
            $"Deleted {deleted} .meta files.\n\n" +
            "Unity is now regenerating proper GUIDs.\n\n" +
            "Please check the following files for 'Missing' references:\n" +
            string.Join("\n", referencedByFiles.GetRange(0, Mathf.Min(5, referencedByFiles.Count))) +
            (referencedByFiles.Count > 5 ? $"\n... and {referencedByFiles.Count - 5} more" : ""),
            "OK");
        
        fakeGuidFiles.Clear();
        scanned = false;
    }
}
