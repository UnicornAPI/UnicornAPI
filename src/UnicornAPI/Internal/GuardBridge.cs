using System;
using System.Collections.Generic;
using System.Reflection;
using Unicorn.API;
using Unicorn.Commands;
using Unicorn.Events;

namespace Unicorn.Internal
{
    /// <summary>
    /// Античит — отдельный плагин P90Guard (BepInEx/plugins/P90Guard.dll). UnicornAPI с ним технически не связан:
    /// если P90Guard установлен, через reflection подписываемся на его события и пересылаем срабатывания в
    /// PlayerEvents.CheatSuspected (→ WebBridge → сайт), а его команды «pg ...» делаем командами UnicornAPI —
    /// чтобы они работали из консоли сайта. Нет P90Guard — ничего не происходит.
    /// </summary>
    internal static class GuardBridge
    {
        private static bool _connected;
        private static Type _api;
        private static MethodInfo _run;
        private static int _attempts;

        internal static void TryConnect()
        {
            if (_connected) return;
            try
            {
                _api = Type.GetType("P90Guard.Api, P90Guard");
                if (_api == null)
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        if (asm.GetName().Name == "P90Guard") { _api = asm.GetType("P90Guard.Api"); break; }
                if (_api == null)
                {
                    if (++_attempts < 12) Timing.CallDelayed(10f, TryConnect);   // ждём, пока BepInEx загрузит P90Guard
                    else Log.Warn("[GuardBridge] античит P90Guard не найден — срабатывания на сайт не идут");
                    return;
                }
                const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
                _api.GetEvent("Flagged", S)?.AddEventHandler(null, new Action<IReadOnlyDictionary<string, object>>(OnFlagged));
                _api.GetEvent("Punished", S)?.AddEventHandler(null, new Action<IReadOnlyDictionary<string, object>>(OnPunished));
                _run = _api.GetMethod("RunCommand", S, null, new[] { typeof(string) }, null);
                _connected = true;
                RegisterCommands();
                Log.Info($"[GuardBridge] античит P90Guard v{Version()} подключён: срабатывания идут на сайт, команды pg/cheatban доступны в консоли UnicornAPI");
            }
            catch (Exception e) { Log.Warn($"[GuardBridge] {e.Message}"); }
        }

        private static string Version()
        {
            try { return _api?.GetProperty("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string ?? "?"; }
            catch { return "?"; }
        }

        private static T Get<T>(IReadOnlyDictionary<string, object> d, string k, T def = default)
        {
            try { return d != null && d.TryGetValue(k, out var v) && v is T t ? t : def; } catch { return def; }
        }

        private static void OnFlagged(IReadOnlyDictionary<string, object> d)
        {
            try
            {
                ulong id = Get<ulong>(d, "steam_id");
                if (id == 0) return;                      // отправитель неизвестен — не приписываем другому игроку
                var p = Player.Get(id);
                if (p == null) return;
                PlayerEvents.CheatSuspected.Invoke(new CheatSuspectedEventArgs(p, Get<string>(d, "check"), Get<string>(d, "details"), Get(d, "count", 1), Get<bool>(d, "blocked")));
            }
            catch (Exception e) { Log.Warn($"[GuardBridge] {e.Message}"); }
        }

        private static void OnPunished(IReadOnlyDictionary<string, object> d)
        {
            try
            {
                ulong id = Get<ulong>(d, "steam_id");
                if (id == 0) return;
                var p = Player.Get(id);
                if (p == null) return;
                string what = Get<string>(d, "action") == "ban" ? "БАН" : "КИК";
                PlayerEvents.CheatSuspected.Invoke(new CheatSuspectedEventArgs(p, "punishment", $"{what}: {Get<string>(d, "reason")}", 1, true));
            }
            catch { }
        }

        /// <summary>Выполнить команду P90Guard (без «pg»), вернуть её вывод.</summary>
        private static bool Run(string args, out string r)
        {
            if (_run == null) { r = "эта версия P90Guard не поддерживает команды из UnicornAPI (нужна 1.1.1+)"; return false; }
            try { r = _run.Invoke(null, new object[] { args }) as string ?? ""; return true; }
            catch (Exception e) { r = "ошибка P90Guard: " + (e.InnerException?.Message ?? e.Message); return false; }
        }

        private static void RegisterCommands()
        {
            CommandManager.Register(new SimpleCommand("pg", "pg <команда> — команды античита P90Guard (pg help)", (string[] a, out string r) =>
                Run(string.Join(" ", a), out r), "p90guard"));
            CommandManager.Register(new SimpleCommand("cheatban", "cheatban <игрок|SteamID> <минуты|perm> <причина> — бан за читы (P90Guard) + отчёт в сеть читеров", (string[] a, out string r) =>
                Run("cheatban " + string.Join(" ", a), out r)));
            CommandManager.Register(new SimpleCommand("cheatunban", "cheatunban <SteamID> — снять бан за читы (P90Guard) и отозвать отчёт в сети", (string[] a, out string r) =>
                Run("unban " + string.Join(" ", a), out r)));
            CommandManager.Register(new SimpleCommand("cheatcheck", "cheatcheck <игрок|SteamID> — проверить по сети читеров (ответ сети — в логе)", (string[] a, out string r) =>
                Run("check " + string.Join(" ", a), out r)));
        }

        internal static string Status()
        {
            if (!_connected) return "античит P90Guard не найден (отдельный плагин BepInEx: BepInEx/plugins/P90Guard.dll). Встроенного античита в UnicornAPI больше нет.";
            Run("status", out var s);
            return $"античит P90Guard v{Version()} подключён. Настройки: BepInEx/config/P90Guard.json, баны: BepInEx/config/P90Guard.bans.json, логи: BepInEx/P90Guard/logs\n{s}";
        }
    }
}
