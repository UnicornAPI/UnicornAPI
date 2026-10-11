using System;
using System.Collections.Generic;
using Unicorn.API;
using UnityEngine;
using GameDoor = global::Door;

namespace Unicorn.Internal
{
    /// <summary>
    /// Баг игры №8 (общий): SyncVar-хуки на сервере Mirror вызывает только у хоста. Часть игровой логики
    /// висит на хуках — на dedicated она не выполняется на сервере. Эмулируем хост: следим за значениями
    /// и вызываем хук сами, когда значение меняется:
    ///  - CharacterClassManager.CurClassHook — модель/хитбоксы игрока и настройка SCP-173 на сервере;
    ///  - Scp173Player.OnViewersCountChanged — «смотрят ли на 173» (SyncLooking);
    ///  - Door.OnDoorStateChanged — анимация (коллайдеры) дверей на сервере: выстрелы/предметы/гранаты;
    ///  - Elevator.OnCurrentFloorChanged, ElevatorManager.OnFloorChanged — двери лифтов и их синхронизация.
    /// </summary>
    internal static class HookEmulator
    {
        private const float Interval = 0.1f, Rescan = 3f;
        private static float _next, _nextScan;

        private static readonly Dictionary<IntPtr, int> Classes = new();
        private static readonly Dictionary<IntPtr, int> Viewers = new();
        private static readonly Dictionary<IntPtr, bool> Doors = new();
        private static readonly Dictionary<IntPtr, bool> Elevators = new();
        private static readonly Dictionary<IntPtr, bool> ElevatorMgrs = new();

        private static CharacterClassManager[] _ccms = Array.Empty<CharacterClassManager>();
        private static Scp173Player[] _scp173 = Array.Empty<Scp173Player>();
        private static GameDoor[] _doors = Array.Empty<GameDoor>();
        private static Elevator[] _elev = Array.Empty<Elevator>();
        private static ElevatorManager[] _elevMgr = Array.Empty<ElevatorManager>();

        private static readonly HashSet<string> Reported = new();

        public static void Tick()
        {
            if (!Application.isBatchMode) return;
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + Interval;
            try
            {
                if (now >= _nextScan) { _nextScan = now + Rescan; Scan(); }
                foreach (var c in _ccms) Watch(c, Classes, x => x.curClass, (x, o, n) => x.CurClassHook(o, n), "CurClassHook", -1);
                foreach (var s in _scp173) Watch(s, Viewers, x => x.knp, (x, o, n) => x.cvs(o, n), "OnViewersCountChanged");
                foreach (var d in _doors) Watch(d, Doors, x => x.isOpen, (x, o, n) => x.bqp(o, n), "OnDoorStateChanged");
                foreach (var e in _elev) Watch(e, Elevators, x => x.keo, (x, o, n) => x.btg(o, n), "OnCurrentFloorChanged");
                foreach (var m in _elevMgr) Watch(m, ElevatorMgrs, x => x.jvz, (x, o, n) => x.rd(o, n), "OnFloorChanged");
            }
            catch (Exception e) { Log.Error($"[HookEmulator] {e}"); }
        }

        private static void Scan()
        {
            _ccms = UnityEngine.Object.FindObjectsOfType<CharacterClassManager>();
            _scp173 = UnityEngine.Object.FindObjectsOfType<Scp173Player>();
            var doors = UnityEngine.Object.FindObjectsOfType<GameDoor>();
            // двери на сервере должны анимироваться даже без камер, иначе коллайдеры не двигаются
            foreach (var d in doors)
                if (!Doors.ContainsKey(d.Pointer) && d.parts != null)
                    foreach (var an in d.parts) if (an != null) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _doors = doors;
            _elev = UnityEngine.Object.FindObjectsOfType<Elevator>();
            _elevMgr = UnityEngine.Object.FindObjectsOfType<ElevatorManager>();
        }

        /// <summary>Вызвать хук, если значение изменилось. firstDefault: если при первом взгляде значение != этого — тоже вызвать хук.</summary>
        private static void Watch<TC, TV>(TC comp, Dictionary<IntPtr, TV> state, Func<TC, TV> get, Action<TC, TV, TV> hook, string name, object firstDefault = null)
            where TC : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            if (comp == null) return;
            IntPtr key = comp.Pointer;
            TV cur;
            try { cur = get(comp); } catch { state.Remove(key); return; }
            if (!state.TryGetValue(key, out var old))
            {
                state[key] = cur;
                if (firstDefault is TV def && !EqualityComparer<TV>.Default.Equals(cur, def)) Call(comp, def, cur, hook, name);
                return;
            }
            if (EqualityComparer<TV>.Default.Equals(old, cur)) return;
            state[key] = cur;
            Call(comp, old, cur, hook, name);
        }

        private static void Call<TC, TV>(TC comp, TV old, TV cur, Action<TC, TV, TV> hook, string name)
        {
            try { hook(comp, old, cur); }
            catch (Exception e)
            {
                if (Reported.Add(name)) Log.Warn($"[HookEmulator] {name}: {e.InnerException?.Message ?? e.Message} (дальше не пишу)");
            }
        }

        /// <summary>Сбросить состояние (новая карта/раунд).</summary>
        public static void Reset()
        {
            Classes.Clear(); Viewers.Clear(); Doors.Clear(); Elevators.Clear(); ElevatorMgrs.Clear();
            _nextScan = 0;
        }
    }
}
