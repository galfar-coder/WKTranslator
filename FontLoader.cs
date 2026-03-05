using System.IO;
using TMPro;
using UnityEngine;

namespace WKTranslator;

public static class FontLoader
{
    public static TMP_FontAsset CustomFont;
    
    public static void LoadCustomFont(string folderPath)
    {
        // Look for .ttf or .otf files in the translation folder
        string[] fontFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
        string fontPath = null;
        
        foreach (var file in fontFiles)
        {
            if (file.EndsWith(".ttf") || file.EndsWith(".otf"))
            {
                fontPath = file;
                break;
            }
        }

        if (string.IsNullOrEmpty(fontPath)) return;

        // Load bytes and create Unity Font
        byte[] fontData = File.ReadAllBytes(fontPath);
        Font unityFont = new Font();
        
        // TODO: Create a dynamic font from OS/File data.
        // AND/OR by Using an AssetBundle containing the TMP_Font.
        unityFont = new Font(fontPath);
        
        // Create TMPro Asset
        CustomFont = TMP_FontAsset.CreateFontAsset(unityFont);
        CustomFont.name = "WK_CustomFont";
        
        // TODO: Add support for characters usually missing (Cyrillic, etc)
        // In theory they should work, but dunno
        LogManager.Info($"Loaded custom font: {Path.GetFileName(fontPath)}");
    }
}