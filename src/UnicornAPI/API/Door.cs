using System.Collections.Generic;
using System.Linq;
using Unicorn.Internal;
using UnityEngine;
using GameDoor = global::Door;

namespace Unicorn.API
{
    /// <summary>Дверь на карте.</summary>
    public sealed class Door
    {
        public GameDoor Base { get; }
        private Door(GameDoor d) { Base = d; }

        private static readonly Dictionary<System.IntPtr, Door> Cache = new();

        public static Door Get(GameDoor d)
        {
            if (d == null) return null;
            if (!Cache.TryGetValue(d.Pointer, out var w) || w.Base == null) Cache[d.Pointer] = w = new Door(d);
            return w;
        }

        /// <summary>Все двери на карте.</summary>
        public static IReadOnlyList<Door> List => Object.FindObjectsOfType<GameDoor>().Select(Get).ToList();

        /// <summary>Поиск по имени объекта (без учёта регистра, можно часть).</summary>
        public static IEnumerable<Door> Find(string name) =>
            List.Where(d => d.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Ближайшая дверь к точке.</summary>
        public static Door Nearest(Vector3 pos, float maxDistance = float.MaxValue) =>
            List.Select(d => (d, dist: Vector3.Distance(d.Position, pos))).Where(x => x.dist <= maxDistance)
                .OrderBy(x => x.dist).Select(x => x.d).FirstOrDefault();

        /// <summary>Дверь ещё существует на карте.</summary>
        public bool IsValid { get { try { return Base != null && Base.gameObject != null; } catch { return false; } } }

        public string Name => Base.gameObject.name;
        public Vector3 Position => Base.transform.position;

        /// <summary>Уровень доступа (пусто — открывается всеми; "Checkpoint" и т.п.).</summary>
        public string AccessLevel { get => Base.AccessLevel; set => Base.AccessLevel = value; }

        /// <summary>Задержка между открытием/закрытием, сек.</summary>
        public float Cooldown { get => Base.cooldown; set => Base.cooldown = value; }

        public bool IsAirlock => Base.isAirlockDoor;

        /// <summary>
        /// Зона двери: по корню сцены (Map_light_zone и т.п.), если не распознан — по ближайшим лампам.
        /// </summary>
        public ZoneType Zone
        {
            get
            {
                var z = Map.ZoneByRoot(Base.gameObject);
                return z != ZoneType.Unknown ? z : Map.GetZone(Position);
            }
        }

        /// <summary>Зона определена по корню сцены (надёжно), а не по лампам.</summary>
        public bool ZoneFromRoot => Map.ZoneByRoot(Base.gameObject) != ZoneType.Unknown;

        /// <summary>Имя корневого объекта сцены (для диагностики).</summary>
        public string RootName { get { try { return Base.transform.root.name; } catch { return "?"; } } }

        public bool IsOpen
        {
            get => Base.isOpen;
            set { if (Base.isOpen == value) return; Base.SetOpen(value); try { Base.RpcPlaySound(value); } catch { } }
        }

        /// <summary>Заблокирована: игроки не могут открыть/закрыть (как во время событий игры).</summary>
        public bool IsLocked { get => Base.IsLockedRaw(); set => Base.SetLocked(value); }

        public bool IsDestroyed => Base.IsDestroyedRaw();

        /// <summary>
        /// Можно ли сейчас переключить дверь (как CanChangeState в игре): не сломана, не заблокирована,
        /// прошла задержка, не в движении (застрявший на сервере флаг движения игнорируется после задержки).
        /// </summary>
        public bool CanChange
        {
            get
            {
                var b = Base;
                if (b.destroyed || b.IsLockedRaw() || b.ked) return false;
                if (b.kec > 0f) return false;
                var st = b.status;
                if (st != null && st.inMove) { if (UnityEngine.Application.isBatchMode) st.inMove = false; else return false; }
                return true;
            }
        }

        /// <summary>
        /// Переключить дверь «как игра» при успешном доступе: задержка, звук, подсветка кнопки, сигнал «доступ разрешён».
        /// false — дверь сейчас нельзя переключить.
        /// </summary>
        public bool InteractAllowed()
        {
            if (!CanChange) return false;
            var b = Base;
            b.kec = b.cooldown;
            bool open = !b.isOpen;
            b.SetOpen(open);
            try { b.RpcPlaySound(open); } catch { }
            try { b.RpcButtonBlink(); } catch { }
            try { b.RpcAccepted(); } catch { }
            return true;
        }

        /// <summary>Отказ «как игра»: красная подсветка и звук.</summary>
        public void InteractDenied()
        {
            try { Base.RpcButtonBlinkRed(); } catch { }
            try { Base.RpcDenied(); } catch { }
        }

        public void Open() => IsOpen = true;
        public void Close() => IsOpen = false;
        public void Toggle() => IsOpen = !IsOpen;
        public void Lock() => IsLocked = true;
        public void Unlock() => IsLocked = false;

        /// <summary>Сломать дверь (как SCP).</summary>
        public void Destroy() { if (!IsDestroyed) Base.ServerDestroy(); }

        /// <summary>Почему игрок не может открыть дверь (все проверки игры из ServerRequestChangeState/CanChangeState).</summary>
        public string Diagnose(Player p = null)
        {
            var b = Base;
            var sb = new System.Text.StringBuilder($"{Name}: open={b.isOpen} locked={b.keb} destroyed={b.destroyed} busy(ked)={b.ked} " +
                $"inMove={(b.status != null ? b.status.inMove.ToString() : "нет status")} cooldownLeft={b.kec:0.00}/{b.cooldown:0.00} access='{b.AccessLevel}'");
            sb.Append($"\n  позиция двери {Fmt(Position)}");
            if (p != null) sb.Append($"\n  игрок {p.Nickname} на сервере в {Fmt(p.Position)}, дистанция {Vector3.Distance(p.Position, Position):0.0} (игра требует <= 15)");
            return sb.ToString();
        }

        private static string Fmt(Vector3 v) => $"{v.x:0.0} {v.y:0.0} {v.z:0.0}";

        public override string ToString() => $"{Name} ({(IsOpen ? "open" : "closed")}{(IsLocked ? ", locked" : "")})";
    }
}
