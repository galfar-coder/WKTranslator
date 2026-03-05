using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WKTranslator;

public static class TranslationScanner
{
    public static List<TranslationFolder> Scan(string rootPluginPath)
    {
        var list = new List<TranslationFolder>();
        foreach (var dir in Directory.GetDirectories(rootPluginPath))
        {
            var jsons = Directory.GetFiles(dir, "*.json").Where(x => !string.IsNullOrEmpty(x));

            foreach (var json in jsons)
            {
                LogManager.Debug(json);

                try
                {
                    var config = JsonConvert.DeserializeObject<TranslationConfig>(File.ReadAllText(json));
                    config.ConfigFileName = Path.GetFileName(json);
                    list.Add(new TranslationFolder(dir, config));
                }
                catch (Exception e)
                {
                    LogManager.Error($"Failed to load {json}\n{e}");
                }
            }
        }
        return list;
    }
}