using System;
using System.Collections.Generic;
using System.Linq;
using GameWeapon = global::Weapon;

namespace Unicorn.API
{
    /// <summary>
    /// Оружие игрока. Урон, магазин и перезарядку считает СЕРВЕР (CmdShoot → попадание → урон из weapon.damage),
    /// поэтому Damage/MagazineSize меняют игру по-настоящему. Скорострельность и отдачу рисует клиент —
    /// их изменение на сервере влияет только на серверную проверку темпа стрельбы.
    /// </summary>
    public sealed class PlayerWeapon
    {
        public Player Owner { get; }
        internal GameWeapon Base { get; }

        internal PlayerWeapon(Player owner, GameWeapon w) { Owner = owner; Base = w; }

        public int ItemId => Base.itemID;
        public string Name => Items.GetName(Base.itemID);

        /// <summary>Урон за попадание (сервер).</summary>
        public int Damage { get => Base.damage; set => Base.damage = value; }
        /// <summary>Патронов в магазине (полный).</summary>
        public int MagazineSize { get => Base.ammoPerMag; set => Base.ammoPerMag = value; }
        /// <summary>Поле maxAmmo оружия (в игре — лимит запаса для этого оружия).</summary>
        public int MaxAmmo { get => Base.maxAmmo; set => Base.maxAmmo = value; }
        public float FireRate { get => Base.fireRate; set => Base.fireRate = value; }
        public float Cooldown { get => Base.cooldown; set => Base.cooldown = value; }
        public float ReloadTime { get => Base.reloadingTime; set => Base.reloadingTime = value; }
        public bool FullAuto => Base.useFullauto;
        public string HitInfo => Base.hitInfo;
        /// <summary>Тип патронов (имя AmmoType), null — без патронов.</summary>
        public string AmmoType => Base.ammoType != null ? Base.ammoType.ammoName : null;

        /// <summary>Патронов в магазине сейчас (для оружия в руках — синхронизируемое значение).</summary>
        public int AmmoInMag
        {
            get
            {
                var wm = Owner.WeaponManager;
                return wm != null && IsInHands ? wm.syncCurAmmo : Base.curAmmo;
            }
            set
            {
                Base.curAmmo = value;
                var wm = Owner.WeaponManager;
                if (wm != null && IsInHands) wm.nfo = value;   // NetworksyncCurAmmo — уйдёт клиенту
            }
        }

        public bool IsInHands => Owner.Inventory.CurrentItem == Base.itemID;

        public Dictionary<string, object> Stats() => new()
        {
            ["item_id"] = ItemId, ["name"] = Name, ["damage"] = Damage, ["magazine"] = MagazineSize, ["max_ammo"] = MaxAmmo,
            ["fire_rate"] = FireRate, ["cooldown"] = Cooldown, ["reload_time"] = ReloadTime, ["full_auto"] = FullAuto,
            ["ammo_type"] = AmmoType, ["ammo_in_mag"] = AmmoInMag, ["hit_info"] = HitInfo,
        };

        public override string ToString() =>
            $"{Name} (#{ItemId}): урон {Damage}, магазин {AmmoInMag}/{MagazineSize}, патроны {AmmoType ?? "-"}, " +
            $"темп {FireRate:0.###}, перезарядка {ReloadTime:0.#} с{(FullAuto ? ", авто" : "")}";
    }

    /// <summary>Патроны игрока (AmmoManager). Изменения — только на сервере, клиент получает их сам (TargetSyncAmmo).</summary>
    public sealed class PlayerAmmo
    {
        private readonly Player _p;
        internal PlayerAmmo(Player p) { _p = p; }

        private AmmoManager Am => _p.AmmoManager;

        /// <summary>Сколько патронов этого типа (имя AmmoType, без учёта регистра).</summary>
        public int Get(string type)
        {
            var t = Weapons.FindAmmoType(type);
            if (t == null || Am == null) return 0;
            return Am.bjm(t);
        }

        public bool Set(string type, int amount) { var t = Weapons.FindAmmoType(type); if (t == null || Am == null) return false; Am.bjo(t, Math.Max(0, amount)); return true; }
        public bool Add(string type, int amount) { var t = Weapons.FindAmmoType(type); if (t == null || Am == null) return false; Am.bjn(t, amount); return true; }
        /// <summary>Забрать патроны. false — не хватило (игра ничего не списывает).</summary>
        public bool Take(string type, int amount) { var t = Weapons.FindAmmoType(type); if (t == null || Am == null) return false; return Am.bjp(t, amount); }

        /// <summary>Все патроны игрока: тип → количество.</summary>
        public Dictionary<string, int> All()
        {
            var r = new Dictionary<string, int>();
            if (Am == null) return r;
            foreach (var name in Weapons.AmmoTypes) r[name] = Get(name);
            return r;
        }
    }

    /// <summary>Оружие и патроны в целом: список, поиск, глобальные настройки.</summary>
    public static class Weapons
    {
        private sealed class Ovr { public Action<PlayerWeapon> Apply; public System.Reflection.Assembly Owner; }
        private readonly struct Defaults
        {
            public readonly int Damage, Mag, MaxAmmo; public readonly float FireRate, Cooldown, Reload;
            public Defaults(GameWeapon w) { Damage = w.damage; Mag = w.ammoPerMag; MaxAmmo = w.maxAmmo; FireRate = w.fireRate; Cooldown = w.cooldown; Reload = w.reloadingTime; }
            public void Restore(GameWeapon w) { w.damage = Damage; w.ammoPerMag = Mag; w.maxAmmo = MaxAmmo; w.fireRate = FireRate; w.cooldown = Cooldown; w.reloadingTime = Reload; }
        }

        private static readonly Dictionary<int, Ovr> Overrides = new();
        private static readonly Dictionary<int, Defaults> Original = new();

        // Кэш: список оружия и типы патронов одинаковы у всех игроков — берём один раз из первого WeaponManager.
        private static List<(int ItemId, string Name, int Damage, int Magazine, string AmmoType)> _all;
        private static readonly Dictionary<string, global::AmmoType> _ammo = new(StringComparer.OrdinalIgnoreCase);

        private static bool EnsureCache()
        {
            if (_all != null) return true;
            var wm = UnityEngine.Object.FindObjectOfType<WeaponManager>();
            if (wm?.weapons == null || wm.weapons.Length == 0) return false;
            var list = new List<(int, string, int, int, string)>();
            foreach (var w in wm.weapons)
            {
                if (w == null) continue;
                if (!Original.ContainsKey(w.itemID)) Original[w.itemID] = new Defaults(w);
                string at = w.ammoType != null ? w.ammoType.ammoName : null;
                if (at != null && !_ammo.ContainsKey(at)) _ammo[at] = w.ammoType;
                list.Add((w.itemID, Items.GetName(w.itemID), Original[w.itemID].Damage, Original[w.itemID].Mag, at));
            }
            _all = list;
            return true;
        }

        /// <summary>Все виды оружия (исходные характеристики игры). Пусто, пока на сервере нет ни одного игрока.</summary>
        public static IReadOnlyList<(int ItemId, string Name, int Damage, int Magazine, string AmmoType)> All =>
            EnsureCache() ? _all : (IReadOnlyList<(int, string, int, int, string)>)Array.Empty<(int, string, int, int, string)>();

        /// <summary>Имена всех типов патронов.</summary>
        public static IReadOnlyList<string> AmmoTypes => EnsureCache() ? _ammo.Keys.ToList() : (IReadOnlyList<string>)Array.Empty<string>();

        internal static global::AmmoType FindAmmoType(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !EnsureCache()) return null;
            return _ammo.TryGetValue(name.Trim(), out var t) ? t : null;
        }

        /// <summary>ID предмета оружия по имени (точно или часть) или числу. -1 — не найдено.</summary>
        public static int FindItemId(string nameOrId)
        {
            if (int.TryParse(nameOrId, out var id)) return id;
            var all = All;
            var exact = all.FirstOrDefault(x => string.Equals(x.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
            if (exact.Name != null) return exact.ItemId;
            var part = all.Where(x => x.Name != null && x.Name.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            return part.Count == 1 ? part[0].ItemId : -1;
        }

        /// <summary>
        /// Глобальная настройка оружия: применяется ко всем игрокам сейчас и к заходящим/появляющимся позже.
        /// Пример: Weapons.Configure(Weapons.FindItemId("P90"), w =&gt; w.Damage = 30);
        /// Каждый вызов для того же оружия заменяет прошлую настройку (значения сначала возвращаются к исходным).
        /// </summary>
        public static void Configure(int itemId, Action<PlayerWeapon> apply)
        {
            EnsureCache();
            Overrides[itemId] = new Ovr { Apply = apply, Owner = apply?.Method?.DeclaringType?.Assembly };
            foreach (var p in Player.List) ApplyTo(p, itemId);
        }

        /// <summary>Снять глобальную настройку и вернуть исходные характеристики игры у всех игроков.</summary>
        public static void ClearConfiguration(int itemId)
        {
            if (!Overrides.Remove(itemId)) return;
            foreach (var p in Player.List) RestoreFor(p, itemId);
        }

        /// <summary>Снять настройки, заданные сборкой плагина (горячая перезагрузка).</summary>
        internal static int RemoveOwnedBy(System.Reflection.Assembly asm)
        {
            var ids = Overrides.Where(kv => kv.Value.Owner == asm).Select(kv => kv.Key).ToList();
            foreach (var id in ids) ClearConfiguration(id);
            return ids.Count;
        }

        internal static void ApplyAll(Player p)
        {
            foreach (var id in Overrides.Keys.ToList()) ApplyTo(p, id);
        }

        private static void RestoreFor(Player p, int itemId)
        {
            if (!p.IsValid || !Original.TryGetValue(itemId, out var d)) return;
            foreach (var w in p.AllWeapons) if (w.ItemId == itemId) d.Restore(w.Base);
        }

        private static void ApplyTo(Player p, int itemId)
        {
            if (!Overrides.TryGetValue(itemId, out var o) || !p.IsValid) return;
            foreach (var w in p.AllWeapons)
                if (w.ItemId == itemId)
                {
                    try
                    {
                        if (!Original.ContainsKey(itemId)) Original[itemId] = new Defaults(w.Base);   // запомнить исходные до первого изменения
                        Original[itemId].Restore(w.Base);                                         // от исходных — чтобы настройки не накапливались
                        o.Apply(w);
                    }
                    catch (Exception e) { Log.Warn($"[Weapons] {e.Message}"); }
                }
        }
    }
}
