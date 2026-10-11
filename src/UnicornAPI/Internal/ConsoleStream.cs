using System;
using BepInEx.Logging;
using Unicorn.Events;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>Поток строк лога → ServerEvents.ConsoleLine (для WebBridge/логов на сайте).</summary>
    internal static class ConsoleStream
    {
        [ThreadStatic] private static bool _inside;   // защита от рекурсии (обработчик сам пишет в лог)

        internal static void Game(string text, LogType type) =>
            Emit("game", type switch
            {
                LogType.Error or LogType.Exception or LogType.Assert => "error",
                LogType.Warning => "warning",
                _ => "info",
            }, text);

        internal static void Emit(string source, string level, string text)
        {
            if (_inside || string.IsNullOrEmpty(text)) return;
            _inside = true;
            try { ServerEvents.ConsoleLine.Invoke(new ConsoleLineEventArgs(source, level, text)); }
            catch { }
            finally { _inside = false; }
        }

        private static Listener _listener;

        internal static void Install()
        {
            if (_listener != null) return;
            _listener = new Listener();
            BepInEx.Logging.Logger.Listeners.Add(_listener);
        }

        /// <summary>Логи BepInEx (UnicornAPI, плагины, сам BepInEx). Сообщения Unity пропускаем — их даёт хук игры.</summary>
        private sealed class Listener : ILogListener
        {
            public LogLevel LogLevelFilter => LogLevel.Info | LogLevel.Warning | LogLevel.Error | LogLevel.Fatal | LogLevel.Message;

            public void LogEvent(object sender, LogEventArgs e)
            {
                var src = e.Source?.SourceName ?? "?";
                if (src == "Unity" || src == "Unity Log") return;
                var lvl = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 ? "error"
                        : (e.Level & LogLevel.Warning) != 0 ? "warning" : "info";
                Emit(src, lvl, e.Data?.ToString());
            }

            public void Dispose() { }
        }
    }
}
