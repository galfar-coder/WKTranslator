using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions; // Added for Regex
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement; // Needed for scene names
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WKTranslator;

public static class TextScanner
{
    // Key = Category (Scene Name), Value = Set of strings
    private static Dictionary<string, HashSet<string>> _categorizedStrings = new Dictionary<string, HashSet<string>>();
    private static bool _isActive;
    private static GameObject _scannerGo;
    
    private static readonly Regex GarbageFilter = new Regex(
        @"^<[^>]+>[\d\.,\s]+<\/[^>]+>$|" +          // Matches <tag>123</tag>
        @"^Time Since Last Hit:|" +                 // Matches specific debug text
        @"^<color=[^>]+>null</color>|" +            // Matches null object errors
        @"^[\d\W]+$|" +                             // Matches pure numbers/symbols (123, ---)
        @"<sprite="                                 // Ignore strings that are JUST input icons
        , RegexOptions.IgnoreCase);

    public static void RunScanner()
    {
        if (_isActive) return;
        _isActive = true;
        _categorizedStrings.Clear();
        
        _scannerGo = new GameObject("WK_TextScanner");
        _scannerGo.AddComponent<ScannerBehaviour>();
        Object.DontDestroyOnLoad(_scannerGo);
    }
    
    public static void StopScanner()
    {
        if (!_isActive) return;
        _isActive = false;
        if (_scannerGo != null)
            Object.Destroy(_scannerGo);
        
        SaveToFile();
    }

    private static void AddFoundString(string text)
    {
        if (!IsValidText(text)) return;

        string category = SceneManager.GetActiveScene().name;
        if (string.IsNullOrEmpty(category)) category = "Unknown";

        if (!_categorizedStrings.ContainsKey(category))
            _categorizedStrings[category] = new HashSet<string>();

        if (_categorizedStrings[category].Contains(text)) return;
        
        _categorizedStrings[category].Add(text);
        LogManager.Debug($"Found text: {text}");
    }

    private static bool IsValidText(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
    
        // Ignore internal Unity strings
        if (s.Contains("UnityEngine") || s.Contains("System.")) return false;

        // Filter out the garbage defined in Regex
        if (GarbageFilter.IsMatch(s)) return false;

        // Filter out single letters (unless it's 'I' or 'A')
        if (s.Length == 1 && s != "I" && s != "A" && s != "a") return false;

        return true;
    }

    private static void SaveToFile()
    {
        // Logic to move recurring strings to "_Common"
        var frequencyMap = new Dictionary<string, int>();
        foreach (var cat in _categorizedStrings.Values)
        {
            foreach (var str in cat)
            {
                if (!frequencyMap.ContainsKey(str)) frequencyMap[str] = 0;
                frequencyMap[str]++;
            }
        }

        var finalMap = new Dictionary<string, HashSet<string>>();
        var commonSet = new HashSet<string>();

        foreach (var category in _categorizedStrings)
        {
            var catName = category.Key;
            finalMap[catName] = new HashSet<string>();

            foreach (var str in category.Value)
            {
                // If it appears in more than 1 scene, move to Common
                if (frequencyMap[str] > 1)
                {
                    commonSet.Add(str);
                }
                else
                {
                    finalMap[catName].Add(str);
                }
            }
        }

        // Add common set if not empty
        if (commonSet.Count > 0)
        {
            // Sort Common strings alphabetically
            finalMap["_Common"] = commonSet;
        }

        // Build the JSON
        var outRoot = new JObject();

        // Sort categories alphabetically
        foreach (var catName in finalMap.Keys.OrderBy(k => k))
        {
            var strings = finalMap[catName];
            if (strings.Count == 0) continue;

            var catObj = new JObject();
            
            // Sort strings alphabetically within category
            foreach (var s in strings.OrderBy(x => x))
            {
                // Key = Original, Value = Original (ready for editing)
                catObj[s] = s;
            }

            outRoot[catName] = catObj;
        }

        var pluginDir = Path.Combine(Paths.PluginPath, "WKTranslator");
        Directory.CreateDirectory(pluginDir);
        var outPath = Path.Combine(pluginDir, "scan_output_categorized.json");
        
        File.WriteAllText(outPath, outRoot.ToString(Formatting.Indented));
        Debug.Log($"[WKTranslator] Scan saved to {outPath}");
    }

    private class ScannerBehaviour : MonoBehaviour
    {
        private float _timer;
        private const float ScanInterval = 3.0f; // Scan every 3 seconds

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer >= ScanInterval)
            {
                _timer = 0;
                ScanUI();
            }
        }

        private void ScanUI()
        {
            // Scan Legacy UI Text
            foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
            {
                // Only scan active/visible text or things in the current scene
                if (t.gameObject.scene.isLoaded || t.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    AddFoundString(t.text);
                }
            }

            // Scan TextMeshPro
            foreach (var tmp in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (tmp.gameObject.scene.isLoaded || tmp.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    AddFoundString(tmp.text);
                }
            }
        }
    }
}