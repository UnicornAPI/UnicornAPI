using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unicorn.API;

namespace Unicorn.Internal
{
    /// <summary>
    /// Проверка версии игры по SHA-256 GameAssembly.dll. Имена методов игры обфусцированы и меняются с каждым
    /// обновлением — патч по старому имени может встать не на тот метод. Поэтому при несовпадении — громкое
    /// предупреждение, а в режиме "safe" UnicornAPI вообще не ставит патчи и не грузит плагины.
    /// </summary>
    internal static class GameVersionCheck
    {
        /// <summary>Сборка игры, под которую собран UnicornAPI.</summary>
        internal const string KnownHash = "ba69d16edce7b6fb2362823b8d7285b9905105c648289f0f25cbccdb9973164a";

        internal static string CurrentHash { get; private set; }
        internal static bool Matches { get; private set; } = true;

        /// <summary>true — можно ставить патчи.</summary>
        internal static bool Run()
        {
            var mode = (CoreConfig.Current.GameHashCheck ?? "warn").Trim().ToLowerInvariant();
            if (mode == "off") return true;
            try
            {
                var path = Path.Combine(BepInEx.Paths.GameRootPath, "GameAssembly.dll");
                using (var fs = File.OpenRead(path))
                using (var sha = SHA256.Create())
                    CurrentHash = string.Concat(sha.ComputeHash(fs).Select(b => b.ToString("x2")));
            }
            catch (Exception e) { Log.Warn($"[GameVersion] не удалось посчитать хэш игры: {e.Message}"); return true; }

            var allowed = CoreConfig.Current.AllowedGameHashes ?? new System.Collections.Generic.List<string>();
            Matches = CurrentHash == KnownHash || allowed.Any(h => string.Equals(h?.Trim(), CurrentHash, StringComparison.OrdinalIgnoreCase));
            if (Matches) { Log.Info($"[GameVersion] версия игры совпадает ({CurrentHash[..12]}…)"); return true; }

            var prev = System.Console.ForegroundColor;
            System.Console.ForegroundColor = System.ConsoleColor.Red;
            System.Console.WriteLine("==============================================================");
            System.Console.WriteLine(" UnicornAPI: ИГРА ОБНОВИЛАСЬ — GameAssembly.dll не та, под которую собран UnicornAPI");
            System.Console.WriteLine($"   ожидался {KnownHash[..16]}…, сейчас {CurrentHash[..16]}…");
            System.Console.WriteLine(mode == "safe"
                ? "   Режим safe: патчи и плагины НЕ загружены. Нужна новая сборка UnicornAPI."
                : "   Режим warn: патчи всё равно ставятся — возможны ошибки. Нужна новая сборка UnicornAPI.");
            System.Console.WriteLine("   Если это точно та же игра — добавь хэш в unicorn.json → AllowedGameHashes.");
            System.Console.WriteLine("==============================================================");
            System.Console.ForegroundColor = prev;
            Log.Error($"[GameVersion] хэш игры {CurrentHash} не совпадает с ожидаемым {KnownHash} (режим {mode})");
            return mode != "safe";
        }
    }
}
