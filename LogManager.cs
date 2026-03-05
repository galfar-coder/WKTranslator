using BepInEx.Logging;

namespace WKTranslator;

public static class LogManager
{
    private static ManualLogSource _logSource;
    
    public static void Initialize(ManualLogSource logger)
    {
        _logSource = logger;
    }
    
    public static void Info(object message) => _logSource.LogInfo(message);
    public static void Warn(object message) => _logSource.LogWarning(message);
    public static void Error(object message) => _logSource.LogError(message);
    public static void Debug(object message) => _logSource.LogDebug(message);
}