using Unicorn.API;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Фикс бага игры: CustomNetworkManager подписывается на SteamServersConnected_t через Callback.Create (клиентский),
    /// а не CreateGameServer. GameServer логинится в Steam, но колбэк никогда не доходит — Mirror-сервер не стартует.
    /// Мы ждём BLoggedOn() и сами вызываем обработчик игры.
    /// </summary>
    internal static class DedicatedFix
    {
        private static bool _done;
        private static float _loggedOnAt = -1f;
        private const float Grace = 3f; // даём шанс настоящему колбэку (если разраб починит)

        internal static void Tick()
        {
            if (_done || !Application.isBatchMode) return;

            bool loggedOn;
            try { loggedOn = Game.SteamGS_LoggedOn(); } catch { return; } // GameServer ещё не инициализирован
            if (!loggedOn) return;

            if (_loggedOnAt < 0f) { _loggedOnAt = Time.realtimeSinceStartup; return; }
            if (Time.realtimeSinceStartup - _loggedOnAt < Grace) return;

            _done = true;
            var cnm = Object.FindObjectOfType<CustomNetworkManager>();
            if (cnm == null) { Log.Error("[DedicatedFix] CustomNetworkManager не найден"); return; }

            Log.Info($"[DedicatedFix] Steam GameServer залогинен (SteamID {Game.SteamGS_SteamId()}), запускаю сервер вместо потерянного колбэка игры");
            try { cnm.CNM_OnSteamServersConnected(); }
            catch (System.Exception e) { Log.Error($"[DedicatedFix] {e}"); return; }

            PrintConnectId();
            LobbyAdvertiser.OnServerStarted();
        }

        internal static void PrintConnectId()
        {
            var id = SteamNative.UserSteamId();
            var prev = System.Console.ForegroundColor;
            System.Console.ForegroundColor = System.ConsoleColor.Yellow;
            System.Console.WriteLine("==============================================");
            System.Console.WriteLine(id != 0 ? $"  ID ДЛЯ ПОДКЛЮЧЕНИЯ: {id}" : "  ID для подключения не определён (Steam не залогинен?)");
            System.Console.WriteLine("  (SteamID64 аккаунта Steam на этом ПК)");
            System.Console.WriteLine("==============================================");
            System.Console.ForegroundColor = prev;
            Log.Info($"[UnicornAPI] ID для подключения: {id}");
        }
    }
}
