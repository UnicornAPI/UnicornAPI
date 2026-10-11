using System;
using System.Linq;
using HarmonyLib;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Блокировка раунда.
    ///  1) При старте сервера и (если ResetRoundLockEachRound) в каждом новом лобби — привести к RoundLockDefault.
    ///  2) Игра сама включает блокировку в ForceRoundStart, если на старте раунда игроков 2 или меньше
    ///     (чтобы раунд одного-двух игроков не закончился сразу). Такую «авто-блокировку» снимаем, как только
    ///     игроков становится AutoUnlockAtPlayers и больше. Блокировку, которую включил админ, не трогаем.
    /// </summary>
    internal static class RoundLockGuard
    {
        private static IntPtr _lastRm;
        private static float _nextCheck;
        private static bool _pending;
        private static string _why;
        private static bool _gameAutoLock;      // текущая блокировка поставлена игрой, а не админом
        private static bool _selfChange;        // меняем сами — не считать «ручным»
        private static bool? _lastSeen;

        internal static void Tick()
        {
            float t = Time.realtimeSinceStartup;
            if (t < _nextCheck) return;
            _nextCheck = t + 1f;

            var rm = RoundManager.singleton;
            if (rm == null) return;
            if (rm.Pointer != _lastRm)
            {
                bool first = _lastRm == IntPtr.Zero;
                _lastRm = rm.Pointer;
                if (first) Schedule("старт сервера");
                else if (CoreConfig.Current.ResetRoundLockEachRound) Schedule("новый раунд");
            }
            if (!rm.IsServer()) return;                  // сервер ещё не поднялся — ждём сколько нужно

            if (_pending) { _pending = false; Apply(rm, CoreConfig.Current.RoundLockDefault, _why); }

            bool locked = rm.roundLock;
            if (_lastSeen != locked)
            {
                if (_lastSeen != null && !locked) _gameAutoLock = false;
                _lastSeen = locked;
            }

            // авто-блокировка игры: снять, когда игроков стало достаточно
            int need = CoreConfig.Current.AutoUnlockAtPlayers;
            if (_gameAutoLock && locked && need > 0 && Round.IsStarted)
            {
                int players = Player.List.Count(p => p.IsValid && p.SteamId != 0);
                if (players >= need)
                {
                    _gameAutoLock = false;
                    Apply(rm, false, $"игроков стало {players}");
                }
            }
        }

        internal static void OnWaitingForPlayers()
        {
            _gameAutoLock = false;
            if (CoreConfig.Current.ResetRoundLockEachRound) Schedule("новый раунд");
        }

        private static void Schedule(string why) { _why = why; _pending = true; }

        private static void Apply(RoundManager rm, bool want, string why)
        {
            if (rm.roundLock == want) return;
            _selfChange = true;
            try { Round.IsLocked = want; }
            finally { _selfChange = false; }
            _lastSeen = want;
            Log.Info($"[RoundLock] {why}: блокировка раунда {(want ? "включена" : "снята")}");
        }

        /// <summary>Блокировку поменяли вручную (команда, API плагина, админ-панель) — авто-снятие больше не трогает.</summary>
        internal static void NoteManual()
        {
            if (_selfChange) return;
            _gameAutoLock = false;
        }

        internal static void NoteGameAutoLock(int players)
        {
            _gameAutoLock = true;
            int need = CoreConfig.Current.AutoUnlockAtPlayers;
            Log.Info($"[RoundLock] игра включила блокировку раунда: на старте игроков {players} (≤2)." +
                     (need > 0 ? $" Сниму автоматически, когда игроков станет {need}+." : ""));
        }
    }

    /// <summary>ForceRoundStart: игра ставит roundLock = true, если игроков ≤ 2. Запоминаем, что это сделала игра.</summary>
    [HarmonyPatch(typeof(RoundManager), "cpe")]
    internal static class GameAutoLockPatch
    {
        private static void Prefix(RoundManager __instance, out bool __state) => __state = __instance.roundLock;

        private static void Postfix(RoundManager __instance, bool __state)
        {
            try
            {
                if (!__state && __instance.roundLock)
                    RoundLockGuard.NoteGameAutoLock(Player.List.Count(p => p.IsValid && p.SteamId != 0));
            }
            catch (Exception e) { Log.Debug($"[RoundLock] {e.Message}"); }
        }
    }
}
