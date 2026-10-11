using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Unicorn.API
{
    /// <summary>Ручка таймера: Cancel() — отменить.</summary>
    public sealed class TimerHandle
    {
        internal float At;
        internal float Interval;     // 0 — одноразовый
        internal Action Action;
        public bool IsCancelled { get; private set; }
        public bool IsRepeating => Interval > 0;
        public void Cancel() => IsCancelled = true;
    }

    /// <summary>Таймеры на главном потоке игры.</summary>
    public static class Timing
    {
        private static readonly List<TimerHandle> Jobs = new();
        private static readonly List<TimerHandle> Due = new();

        /// <summary>Выполнить action через delay секунд (на главном потоке).</summary>
        public static void CallDelayed(float delay, Action action) => Schedule(delay, action);

        /// <summary>Как CallDelayed, но возвращает ручку для отмены.</summary>
        public static TimerHandle Schedule(float delay, Action action) => Add(delay, 0f, action);

        /// <summary>
        /// Повторять action каждые interval секунд. Первый запуск через firstDelay (по умолчанию = interval).
        /// Остановить: handle.Cancel().
        /// </summary>
        public static TimerHandle CallRepeating(float interval, Action action, float firstDelay = -1f)
        {
            if (interval <= 0f) throw new ArgumentException("interval должен быть > 0");
            return Add(firstDelay < 0 ? interval : firstDelay, interval, action);
        }

        /// <summary>Выполнить на следующем кадре.</summary>
        public static void NextFrame(Action action) => Schedule(0f, action);

        private static TimerHandle Add(float delay, float interval, Action action)
        {
            var h = new TimerHandle { At = Time.realtimeSinceStartup + delay, Interval = interval, Action = action };
            lock (Jobs) Jobs.Add(h);
            return h;
        }

        /// <summary>Отменить все таймеры, созданные кодом из сборки (горячая перезагрузка плагина).</summary>
        internal static int CancelOwnedBy(Assembly asm)
        {
            int n = 0;
            lock (Jobs)
                foreach (var j in Jobs)
                    if (!j.IsCancelled && Events.EventUtil.Owned(j.Action, asm)) { j.Cancel(); n++; }
            return n;
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            Due.Clear();
            lock (Jobs)
            {
                for (int i = Jobs.Count - 1; i >= 0; i--)
                {
                    var j = Jobs[i];
                    if (j.IsCancelled) { Jobs.RemoveAt(i); continue; }
                    if (j.At > now) continue;
                    Due.Add(j);
                    if (j.IsRepeating) j.At = now + j.Interval;
                    else Jobs.RemoveAt(i);
                }
            }
            for (int i = Due.Count - 1; i >= 0; i--)
            {
                var j = Due[i];
                if (j.IsCancelled) continue;
                try { j.Action(); }
                catch (Exception e) { Log.Error($"[Timing] {j.Action.Method.DeclaringType?.FullName}.{j.Action.Method.Name}: {e}"); }
            }
        }
    }
}
