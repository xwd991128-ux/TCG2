using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class FakeGuidFixer : EditorWindow
{
    private List<MetaFileInfo> fakeMetaFiles = new List<MetaFileInfo>();
    private Dictionary<string, string> oldToNewGuid = new Dictionary<string, string>();
    private Vector2 scrollPosition;
    private bool scanned = false;
    private int fixedCount = 0;

    private class MetaFileInfo
    {
        public string metaPath;
        public string assetPath;
        public string guid;
        public string reason;
    }

    [MenuItem("Tools/Fake GUID Fixer")]
    public static void ShowWindow()
    {
        GetWindow<FakeGuidFixer>("Fake GUID Fixer");
    }

    void OnGUI()
    {
        GUILayout.Label("Fake GUID Fixer", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This tool will:\n" +
            "1. Find all .meta files with fake GUIDs\n" +
            "2. Delete them and let Unity regenerate proper GUIDs\n" +
            "3. Update all references in .asset files to use new GUIDs\n\n" +
            "IMPORTANT: Make sure you have a backup before running this!",
            MessageType.Warning);

        GUILayout.Space(10);

        if (GUILayout.Button("Step 1: Scan for Fake GUIDs", GUILayout.Height(40)))
        {
            ScanForFakeGuids();
        }

        GUILayout.Space(10);

        if (scanned)
        {
            GUILayout.Label($"Found {fakeMetaFiles.Count} files with fake GUIDs:", EditorStyles.boldLabel);
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            foreach (var info in fakeMetaFiles)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{info.reason}: {info.guid}", EditorStyles.helpBox, GUILayout.Height(20));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(10);

            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("Step 2: Delete Fake .meta Files & Regenerate", GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog("Confirm",
                    $"This will delete {fakeMetaFiles.Count} .meta files.\n" +
                    "Unity will regenerate them with proper GUIDs.\n\n" +
                    "Continue?",
                    "Yes, Continue", "Cancel"))
                {
                    DeleteAndRegenerateMetaFiles();
                }
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(10);

            if (GUILayout.Button("Step 3: Update References in Asset Files", GUILayout.Height(40)))
            {
                UpdateAssetReferences();
            }

            GUILayout.Space(10);

            if (fixedCount > 0)
            {
                EditorGUILayout.HelpBox($"Fixed {fixedCount} references!", MessageType.Info);
            }
        }
    }

    void ScanForFakeGuids()
    {
        fakeMetaFiles.Clear();
        oldToNewGuid.Clear();
        
        string assetsPath = Application.dataPath;
        string[] metaFiles = Directory.GetFiles(assetsPath, "*.meta", SearchOption.AllDirectories);

        Regex guidPattern = new Regex(@"guid:\s*([a-fA-F0-9]+)");
        
        foreach (var metaFile in metaFiles)
        {
            string content = File.ReadAllText(metaFile);
            Match match = guidPattern.Match(content);
            
            if (match.Success)
            {
                string guid = match.Groups[1].Value;
                string reason = IsFakeGuid(guid);
                
                if (reason != null)
                {
                    string assetFile = metaFile.Replace(".meta", "");
                    fakeMetaFiles.Add(new MetaFileInfo
                    {
                        metaPath = metaFile,
                        assetPath = assetFile,
                        guid = guid,
                        reason = reason
                    });
                }
            }
        }
        
        scanned = true;
        Debug.Log($"[FakeGuidFixer] Found {fakeMetaFiles.Count} files with fake GUIDs");
        
        foreach (var info in fakeMetaFiles)
        {
            Debug.Log($"[FakeGuidFixer] {info.reason}: {Path.GetFileName(info.assetPath)} -> {info.guid}");
        }
    }

    string IsFakeGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return "Empty GUID";

        if (guid.Length != 32)
            return $"Wrong length ({guid.Length})";

        string lower = guid.ToLower();

        if (IsSequentialNumberPattern(lower))
            return "Sequential numbers";

        if (IsSequentialAlphaPattern(lower))
            return "Sequential alpha";

        if (IsRepeatingPattern(lower))
            return "Repeating pattern";

        return null;
    }

    bool IsSequentialNumberPattern(string guid)
    {
        for (int start = 0; start <= 9; start++)
        {
            string pattern = "";
            for (int i = 0; i < 32; i++)
            {
                pattern += ((start + i) % 10).ToString();
            }
            if (guid == pattern)
                return true;
        }
        
        if (guid.Contains("01234567") || guid.Contains("12345678") ||
            guid.Contains("23456789") || guid.Contains("34567890") ||
            guid.Contains("45678901") || guid.Contains("56789012") ||
            guid.Contains("67890123") || guid.Contains("78901234") ||
            guid.Contains("89012345") || guid.Contains("90123456"))
            return true;

        return false;
    }

    bool IsSequentialAlphaPattern(string guid)
    {
        string[] patterns = {
            "a1b2c3d4e5f6789012345678abcdefaa",
            "a1b2c3d4e5f6789012345678abcdef",
            "b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
            "c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7",
            "d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8",
            "e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9",
            "f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0",
            "a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1",
            "b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2",
            "c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3",
            "d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4",
            "e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5",
            "f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6",
            "a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7",
            "b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8",
            "c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9",
            "d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0",
            "e6f7a8b9c0d1234567890abcdef",
            "f7a8b9c0d1234567890abcdef12",
            "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5",
            "1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c"
        };

        foreach (var pattern in patterns)
        {
            if (guid.StartsWith(pattern.Substring(0, Mathf.Min(8, pattern.Length))))
                return true;
        }

        if (Regex.IsMatch(guid, @"^[a-f]1[a-f]2[a-f]3[a-f]4[a-f]5[a-f]6[a-f]7[a-f]8[a-f]9[a-f]0"))
            return true;

        if (Regex.IsMatch(guid, @"^[0-9][a-f][0-9][a-f][0-9][a-f][0-9][a-f]"))
            return true;

        return false;
    }

    bool IsRepeatingPattern(string guid)
    {
        for (int i = 0; i < 16; i++)
        {
            char c = (char)('a' + i);
            if (guid == new string(c, 32))
                return true;
        }

        for (int i = 0; i < 10; i++)
        {
            char c = (char)('0' + i);
            if (guid == new string(c, 32))
                return true;
        }

        return false;
    }

    void DeleteAndRegenerateMetaFiles()
    {
        oldToNewGuid.Clear();
        
        Dictionary<string, string> oldGuidToAssetPath = new Dictionary<string, string>();
        foreach (var info in fakeMetaFiles)
        {
            oldGuidToAssetPath[info.guid] = info.assetPath;
        }
        
        foreach (var info in fakeMetaFiles)
        {
            if (File.Exists(info.metaPath))
            {
                File.Delete(info.metaPath);
                Debug.Log($"[FakeGuidFixer] Deleted: {info.metaPath}");
            }
        }
        
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        
        System.Threading.Thread.Sleep(1000);
        
        foreach (var kvp in oldGuidToAssetPath)
        {
            string newMetaFile = kvp.Value + ".meta";
            if (File.Exists(newMetaFile))
            {
                string newGuid = GetGuidFromMeta(newMetaFile);
                if (!string.IsNullOrEmpty(newGuid) && newGuid.Length == 32)
                {
                    oldToNewGuid[kvp.Key] = newGuid;
                    Debug.Log($"[FakeGuidFixer] GUID mapping: {kvp.Key} -> {newGuid}");
                }
            }
        }
        
        Debug.Log($"[FakeGuidFixer] Regenerated {oldToNewGuid.Count} GUID mappings");
        EditorUtility.DisplayDialog("Step 2 Complete", 
            $"Deleted {fakeMetaFiles.Count} fake .meta files\n" +
            $"Created {oldToNewGuid.Count} new GUID mappings\n\n" +
            "Now click 'Step 3' to update references.", "OK");
    }

    void UpdateAssetReferences()
    {
        fixedCount = 0;
        string assetsPath = Application.dataPath;
        string[] allFiles = Directory.GetFiles(assetsPath, "*.asset", SearchOption.AllDirectories);
        List<string> alsoCheck = new List<string>();
        alsoCheck.AddRange(Directory.GetFiles(assetsPath, "*.prefab", SearchOption.AllDirectories));
        alsoCheck.AddRange(Directory.GetFiles(assetsPath, "*.unity", SearchOption.AllDirectories));
        
        var allAssetFiles = new List<string>(allFiles);
        allAssetFiles.AddRange(alsoCheck);
        
        int totalReplacements = 0;
        
        foreach (var assetFile in allAssetFiles)
        {
            if (!File.Exists(assetFile)) continue;
            
            string content = File.ReadAllText(assetFile);
            bool modified = false;
            
            foreach (var kvp in oldToNewGuid)
            {
                if (content.Contains(kvp.Key))
                {
                    content = content.Replace(kvp.Key, kvp.Value);
                    modified = true;
                    totalReplacements++;
                }
            }
            
            if (modified)
            {
                File.WriteAllText(assetFile, content);
                fixedCount++;
                Debug.Log($"[FakeGuidFixer] Updated: {assetFile}");
            }
        }
        
        AssetDatabase.Refresh();
        Debug.Log($"[FakeGuidFixer] Fixed {fixedCount} files with {totalReplacements} replacements");
        
        EditorUtility.DisplayDialog("Step 3 Complete", 
            $"Updated {fixedCount} files\n" +
            $"Total {totalReplacements} GUID references replaced\n\n" +
            "Please check Unity Console for any remaining errors.", "OK");
    }

    string GetGuidFromMeta(string metaFile)
    {
        if (!File.Exists(metaFile)) return "";
        
        string content = File.ReadAllText(metaFile);
        Regex guidPattern = new Regex(@"guid:\s*([a-fA-F0-9]+)");
        Match match = guidPattern.Match(content);
        
        return match.Success ? match.Groups[1].Value : "";
    }
}
