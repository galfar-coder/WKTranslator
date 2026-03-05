namespace WKTranslator;

public class TranslationFolder
{
    public string FolderPath { get; }
    public TranslationConfig Config { get; }

    public TranslationFolder(string path, TranslationConfig config)
    {
        FolderPath = path;
        Config = config;
    }
}