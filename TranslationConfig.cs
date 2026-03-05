using System.Collections.Generic;
using Newtonsoft.Json;

namespace WKTranslator;

public class TranslationConfig
{
    [JsonProperty("languageKey")]
    public string LanguageKey { get; set; }
    
    [JsonProperty("languageName")]
    public string LanguageName { get; set; }
    
    [JsonProperty("authors")]
    public List<string> Authors { get; set; }
    
    [JsonIgnore]
    public string ConfigFileName { get; set; }
    
    [JsonIgnore]
    [JsonProperty("fontFile")]
    public string FontFileName { get; set; }
}