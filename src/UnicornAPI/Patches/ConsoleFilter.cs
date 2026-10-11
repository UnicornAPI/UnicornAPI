using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// Фильтр мусора в консоли сервера. Строки из UnicornAPI/console_filter.txt (по одной подстроке на строку, # — комментарий):
    /// сообщение игры, содержащее любую из них, в консоль не выводится. Файл создаётся сам; перечитать — команда `confilter`.
    /// </summary>
    [HarmonyPatch(typeof(ServerConsole), "bho")] // обработчик Application.logMessageReceived(string text, string stack, LogType)
    internal static class ConsoleFilter
    {
        private static readonly string[] Defaults =
        {
            "# Сообщения игры, содержащие эти строки, не выводятся в консоль сервера.",
            "# Одна подстрока на строку. После правки: команда confilter (или перезапуск).",
            "BoxColliders does not support negative scale",
            "Replaced texture in material",
        };

        private static string[] _patterns = Array.Empty<string>();
        internal static long Hidden;

        internal static string Path => System.IO.Path.Combine(Paths.Root, "console_filter.txt");

        internal static int Load()
        {
            try
            {
                if (!File.Exists(Path)) File.WriteAllLines(Path, Defaults);
                _patterns = File.ReadAllLines(Path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();
            }
            catch (Exception e) { Log.Warn($"[ConsoleFilter] {e.Message}"); }
            return _patterns.Length;
        }

        private static bool Prefix(string a, LogType c)
        {
            if (a == null) return true;
            foreach (var p in _patterns)
                if (a.IndexOf(p, StringComparison.Ordinal) >= 0) { Hidden++; return false; }
            Internal.ConsoleStream.Game(a, c);
            return true;
        }
    }
}
