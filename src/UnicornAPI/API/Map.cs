using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unicorn.API
{
    /// <summary>Зоны комплекса (как SceneLight.Zone в игре).</summary>
    public enum ZoneType { Unknown = -1, Light = 0, Heavy = 1, Surface = 2, Entrance = 3 }

    /// <summary>Лифт карты: две точки и зоны, которые он соединяет.</summary>
    public sealed class MapElevator
    {
        public UnityEngine.Object Base { get; }
        public Vector3 PointA { get; }
        public Vector3 PointB { get; }
        public ZoneType ZoneA { get; }
        public ZoneType ZoneB { get; }
        public string Name { get; }
        internal MapElevator(UnityEngine.Object b, string name, Vector3 a, Vector3 bb)
        {
            Base = b; Name = name; PointA = a; PointB = bb; ZoneA = Map.GetZoneWide(a); ZoneB = Map.GetZoneWide(bb);
        }
        public bool Touches(ZoneType z) => ZoneA == z || ZoneB == z;
        public override string ToString() => $"{Name}: {ZoneA} ↔ {ZoneB}";
    }

    /// <summary>
    /// Карта: зоны (по ближайшей лампе SceneLight — у каждой лампы комнаты игра хранит зону), свет по зонам, лифты.
    /// Кэш ламп обновляется каждый раунд (карта генерируется заново).
    /// </summary>
    public static class Map
    {
        private static List<(Vector3 Pos, ZoneType Zone)> _lights;
        private static float _builtAt = -1f;

        /// <summary>Лампы дальше этого — считаем, что зона неизвестна (карманное измерение, вне карты).</summary>
        public static float MaxLightDistance = 30f;

        internal static void Invalidate() { _lights = null; }

        private static List<(Vector3, ZoneType)> Lights
        {
            get
            {
                // пересобираем, если кэша нет или он старше минуты и пуст (карта могла ещё не сгенерироваться)
                if (_lights != null && (_lights.Count > 0 || Time.realtimeSinceStartup - _builtAt < 5f)) return _lights;
                var list = new List<(Vector3, ZoneType)>();
                try
                {
                    foreach (var sl in UnityEngine.Object.FindObjectsOfType<SceneLight>())
                        if (sl != null) list.Add((sl.transform.position, (ZoneType)(int)sl.zone));
                }
                catch (Exception e) { Log.Warn($"[Map] лампы: {e.Message}"); }
                _lights = list; _builtAt = Time.realtimeSinceStartup;
                return list;
            }
        }

        public static int LightCount => Lights.Count;

        /// <summary>
        /// Зона точки: большинство среди 3 ближайших ламп (не дальше MaxLightDistance); при равенстве — ближайшая.
        /// Большинство сглаживает ошибку у стен на стыке зон (лампа соседней зоны за стеной).
        /// </summary>
        public static ZoneType GetZone(Vector3 pos)
        {
            float max2 = MaxLightDistance * MaxLightDistance;
            // три ближайших без сортировки всего списка
            float d0 = float.MaxValue, d1 = float.MaxValue, d2 = float.MaxValue;
            ZoneType z0 = ZoneType.Unknown, z1 = ZoneType.Unknown, z2 = ZoneType.Unknown;
            foreach (var (p, z) in Lights)
            {
                float d = (p - pos).sqrMagnitude;
                if (d > max2) continue;
                if (d < d0) { d2 = d1; z2 = z1; d1 = d0; z1 = z0; d0 = d; z0 = z; }
                else if (d < d1) { d2 = d1; z2 = z1; d1 = d; z1 = z; }
                else if (d < d2) { d2 = d; z2 = z; }
            }
            if (z0 == ZoneType.Unknown) return ZoneType.Unknown;
            if (z1 != ZoneType.Unknown && z1 == z2 && z1 != z0) return z1;   // две из трёх против ближайшей
            return z0;
        }

        /// <summary>Как GetZone, но если ламп рядом нет — ищет в радиусе 150 м (шахты лифтов и т.п.).</summary>
        public static ZoneType GetZoneWide(Vector3 pos)
        {
            var z = GetZone(pos);
            if (z != ZoneType.Unknown) return z;
            float keep = MaxLightDistance;
            try { MaxLightDistance = 150f; return GetZone(pos); } finally { MaxLightDistance = keep; }
        }

        /// <summary>Какие зоны есть в радиусе от точки (для дверей на стыке зон — КПП).</summary>
        public static HashSet<ZoneType> ZonesNear(Vector3 pos, float radius)
        {
            var r2 = radius * radius; var set = new HashSet<ZoneType>();
            foreach (var (p, z) in Lights) if ((p - pos).sqrMagnitude <= r2) set.Add(z);
            return set;
        }

        public static ZoneType Zone(this Player p) => p == null || !p.IsValid ? ZoneType.Unknown : GetZone(p.Position);

        /// <summary>
        /// Зона объекта карты по корню сцены (у игры каждая зона — свой корень: Map_light_zone, Map_heavy_zone, ...).
        /// Это точнее ламп для дверей и комнат. Unknown — корень не распознан.
        /// </summary>
        public static ZoneType ZoneByRoot(GameObject go)
        {
            try
            {
                var root = go != null ? go.transform.root.name : null;
                if (string.IsNullOrEmpty(root)) return ZoneType.Unknown;
                root = root.ToLowerInvariant();
                if (root.Contains("light_zone")) return ZoneType.Light;
                if (root.Contains("heavy_zone")) return ZoneType.Heavy;
                if (root.Contains("entrance")) return ZoneType.Entrance;
                if (root.Contains("surface")) return ZoneType.Surface;
            }
            catch { }
            return ZoneType.Unknown;
        }

        /// <summary>Идёт ли в этом раунде игровое событие «Блэкаут» (игра сама гасит весь свет).</summary>
        public static bool IsBlackoutRound
        {
            get
            {
                try { var re = UnityEngine.Object.FindObjectOfType<RoundEvents>(); return re != null && (int)re.curEvent == 1; }
                catch { return false; }
            }
        }

        // ---------------- Свет ----------------

        private const string LightsRpcName = "System.Void MapLightningController::RpcTurnOffLights(System.Int32)";
        private const int LightsRpcHash = unchecked((int)0x8db7d06e);   // из обёртки RpcTurnOffLights (bdu)

        /// <summary>
        /// Переключить свет зоны (в игре это переключатель: вкл↔выкл). targets = null — всем;
        /// иначе только этим игрокам (чтобы потом вернуть ровно тем, кому выключали).
        /// </summary>
        public static bool ToggleZoneLights(ZoneType zone, IEnumerable<Player> targets = null) => ToggleZoneLights(zone, targets, out _);

        /// <summary>То же, reached — кому отправка точно ушла (им потом и возвращать).</summary>
        public static bool ToggleZoneLights(ZoneType zone, IEnumerable<Player> targets, out List<Player> reached)
        {
            reached = new List<Player>();
            if (zone != ZoneType.Light && zone != ZoneType.Heavy && zone != ZoneType.Entrance) return false;
            var mlc = UnityEngine.Object.FindObjectOfType<MapLightningController>();
            if (mlc == null) { Log.Warn("[Map] MapLightningController не найден"); return false; }
            int z = zone == ZoneType.Light ? 0 : zone == ZoneType.Heavy ? 1 : 2;
            if (targets == null) { mlc.bdu(z); return true; }
            foreach (var p in targets)
            {
                var conn = p?.Connection;
                if (conn == null) continue;
                Mirror.hn w = null;
                try
                {
                    w = Mirror.hm.hfu();                     // NetworkWriterPool.Get
                    Mirror.hl.hdo(w, z);                     // WriteInt
                    mlc.gih(conn, LightsRpcName, LightsRpcHash, w, 0);   // SendTargetRPCInternal
                    reached.Add(p);
                }
                catch (Exception e) { Log.Warn($"[Map] свет для {p?.Nickname}: {e.Message}"); }
                finally { if (w != null) try { Mirror.hm.hfv(w); } catch { } }   // NetworkWriterPool.Return
            }
            return reached.Count > 0;
        }

        // ---------------- Лифты ----------------

        private static float _elevWarnAt = -999f;
        private static void ElevWarn(Exception e)
        {
            if (Time.realtimeSinceStartup - _elevWarnAt < 30f) return;
            _elevWarnAt = Time.realtimeSinceStartup;
            Log.Warn($"[Map] лифты: {e.Message}");
        }

        public static IReadOnlyList<MapElevator> Elevators
        {
            get
            {
                var res = new List<MapElevator>();
                // каждый лифт в своём try: один сломанный не должен скрыть остальные (иначе LCZ->HCZ не заблокируется)
                try
                {
                    foreach (var e in UnityEngine.Object.FindObjectsOfType<global::Elevator>())
                    {
                        try
                        {
                            var a = e._elevatorA.ElevatorTransform; var b = e._elevatorB.ElevatorTransform;
                            if (a != null && b != null) res.Add(new MapElevator(e, e.gameObject.name, a.position, b.position));
                        }
                        catch (Exception ex) { ElevWarn(ex); }
                    }
                }
                catch (Exception ex) { ElevWarn(ex); }
                try
                {
                    foreach (var m in UnityEngine.Object.FindObjectsOfType<ElevatorManager>())
                    {
                        try
                        {
                            var a = m._elevatorColliderA; var b = m._elevatorColliderB;
                            if (a != null && b != null) res.Add(new MapElevator(m, m.gameObject.name, a.transform.position, b.transform.position));
                        }
                        catch (Exception ex) { ElevWarn(ex); }
                    }
                }
                catch (Exception ex) { ElevWarn(ex); }
                return res;
            }
        }

        /// <summary>
        /// Запреты лифтов: функция (лифт, игрок-или-null) → true = нельзя вызвать. Проверяются все зарегистрированные.
        /// </summary>
        public static readonly List<Func<MapElevator, Player, bool>> ElevatorBlockers = new();

        internal static bool IsElevatorBlocked(UnityEngine.Object baseObj, Player p)
        {
            if (ElevatorBlockers.Count == 0) return false;
            var el = Elevators.FirstOrDefault(e => e.Base.Pointer == baseObj.Pointer);
            if (el == null) return false;
            foreach (var f in ElevatorBlockers.ToList())
            {
                try { if (f(el, p)) return true; } catch (Exception e) { Log.Warn($"[Map] блок лифта: {e.Message}"); }
            }
            return false;
        }
    }
}
