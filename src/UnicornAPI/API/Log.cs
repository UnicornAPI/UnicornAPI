using BepInEx.Logging;

namespace Unicorn.API
{
    /// <summary>Логгер. Пишет в консоль BepInEx и BepInEx/LogOutput.log.</summary>
    public static class Log
    {
        internal static ManualLogSource Source;

        public static void Info(object msg) => Source?.LogInfo(msg);
        public static void Warn(object msg) => Source?.LogWarning(msg);
        public static void Error(object msg) => Source?.LogError(msg);
        public static void Debug(object msg) => Source?.LogDebug(msg);
    }
}
