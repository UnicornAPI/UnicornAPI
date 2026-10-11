using System;
using Unicorn.API;
using Unicorn.Events;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Единая точка смены состояния раунда (лобби → started → ending → лобби).
    /// Источников два: патч сеттера RoundManager.info (мгновенно) и опрос раз в 0.25 с — на случай, если игра меняет
    /// состояние в обход сеттера (встроенный SyncVar-сеттер) или патч по какой-то причине не сработал.
    /// Каждое событие срабатывает ровно один раз на переход; каждый шаг изолирован — ошибка в одном
    /// (например, в RoundStartFix) не отменяет события RoundStarted/RoundEnded для плагинов.
    /// Здесь же — собственный таймер раунда (поле игры roundTime не подходит: там не время раунда).
    /// </summary>
    internal static class RoundStateTracker
    {
        internal const string Waiting = "Waiting for round start", Started = "started", Ending = "ending";

        private static string _last;
        private static IntPtr _rm;
        private static float _nextPoll;
        internal static float StartedAt = -1f;
        internal static float LastDuration;
        internal static long Transitions;

        internal static float Elapsed
        {
            get
            {
                if (_last == Started && StartedAt >= 0f) return Time.realtimeSinceStartup - StartedAt;
                if (_last == Ending) return LastDuration;
                return 0f;
            }
        }

        /// <summary>Вызов из патча сеттера info.</summary>
        internal static void FromSetter(RoundManager rm, string now) => Transition(rm, now, "setter");

        internal static void Tick()
        {
            float t = Time.realtimeSinceStartup;
            if (t < _nextPoll) return;
            _nextPoll = t + 0.25f;
            try
            {
                var rm = RoundManager.singleton;
                if (rm == null || !rm.IsServer()) return;
                var now = rm.info;
                if (rm.Pointer != _rm)
                {
                    bool first = _rm == IntPtr.Zero;
                    _rm = rm.Pointer;
                    if (first || _last == null)
                    {
                        _last = now;                                             // стартовое состояние, событий не шлём
                        if (now == Started) StartedAt = t;
                        return;
                    }
                }
                if (now != _last) Transition(rm, now, "poll");
            }
            catch (Exception e) { Log.Debug($"[Round] poll: {e.Message}"); }
        }

        private static void Transition(RoundManager rm, string now, string source)
        {
            if (string.IsNullOrEmpty(now) || now == _last) return;
            var prev = _last;
            _last = now;
            Transitions++;
            float t = Time.realtimeSinceStartup;

            switch (now)
            {
                case Started:
                    StartedAt = t;
                    API.Map.Invalidate();
                    API.Round.SessionData.Clear();
                    Log.Info($"[Round] раунд начался ({source})");
                    Safe("RoundStartFix", () => RoundStartFix.Run());
                    Safe("RoundStarted", ServerEvents.RoundStarted.Invoke);
                    break;

                case Ending:
                    LastDuration = StartedAt >= 0f ? t - StartedAt : 0f;
                    Log.Info($"[Round] раунд закончился через {TimeSpan.FromSeconds(LastDuration):mm\\:ss} ({source})");
                    Safe("RoundEndFix", () => RoundEndFix.OnEnding(rm));
                    Safe("RoundEnded", ServerEvents.RoundEnded.Invoke);
                    StartedAt = -1f;
                    break;

                case Waiting:
                    API.Map.Invalidate();
                    if (prev == Started) LastDuration = StartedAt >= 0f ? t - StartedAt : 0f;   // рестарт без «ending»
                    StartedAt = -1f;
                    Log.Info($"[Round] лобби ({source})");
                    Safe("RoundLockGuard", RoundLockGuard.OnWaitingForPlayers);
                    Safe("WaitingForPlayers", ServerEvents.WaitingForPlayers.Invoke);
                    break;

                default:
                    Log.Debug($"[Round] '{prev}' → '{now}' ({source})");
                    break;
            }
        }

        private static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Error($"[Round] {what}: {e}"); }
        }
    }
}
