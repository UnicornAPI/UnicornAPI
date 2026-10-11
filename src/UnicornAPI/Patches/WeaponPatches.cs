using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Events;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.Patches
{
    internal static class WeaponCheck
    {
        /// <summary>Оружие, которым игра выстрелит: weapons[curWeapon] (так делает сама игра в dcw).</summary>
        internal static global::Weapon Current(WeaponManager wm)
        {
            var arr = wm.weapons;
            int i = wm.curWeapon;
            return arr != null && i >= 0 && i < arr.Length ? arr[i] : null;
        }
    }

    /// <summary>
    /// Выстрел: серверная обработка CmdShoot (кулдаун, расход патрона, луч, урон). Отмена = выстрела нет.
    /// Событие только для выстрелов, которые игра реально выполнит: есть оружие, патрон в магазине, не идёт перезарядка.
    /// </summary>
    [HarmonyPatch(typeof(WeaponManager), Game.WM_ServerShoot)]
    internal static class ShootingPatch
    {
        private static bool Prefix(WeaponManager __instance, Vector3 a, Vector3 b)
        {
            if (!PlayerEvents.Shooting.HasHandlers) return true;
            try
            {
                var gw = WeaponCheck.Current(__instance);
                if (gw == null || __instance.reloading || __instance.syncCurAmmo <= 0) return true;   // игра сама не выстрелит
                var p = Player.Get(__instance.gameObject) ?? AntiCheat.Owner(__instance);
                if (p == null) return true;
                var ev = new ShootingEventArgs(p, new PlayerWeapon(p, gw), gw.itemID, a, b);
                PlayerEvents.Shooting.Invoke(ev);
                return ev.IsAllowed;
            }
            catch (Exception e) { Log.Error($"[ShootingPatch] {e}"); return true; }
        }
    }

    /// <summary>Перезарядка: invoker CmdStartReload. Событие только если перезарядка реально начнётся. Отмена = перезарядки нет.</summary>
    [HarmonyPatch(typeof(WeaponManager), Game.WM_InvokeStartReload)]
    internal static class ReloadingPatch
    {
        private static bool Prefix(NetworkBehaviour a)
        {
            if (!PlayerEvents.Reloading.HasHandlers) return true;
            try
            {
                var wm = a?.TryCast<WeaponManager>();
                if (wm == null) return true;
                var gw = WeaponCheck.Current(wm);
                if (gw == null || wm.reloading || gw.ammoType == null) return true;
                if (wm.syncCurAmmo >= gw.ammoPerMag) return true;                           // магазин полон
                var am = wm.am;
                if (am != null && am.bjm(gw.ammoType) <= 0) return true;                    // запаса нет
                var p = AntiCheat.Owner(a);
                if (p == null) return true;
                var ev = new ReloadingEventArgs(p, new PlayerWeapon(p, gw));
                PlayerEvents.Reloading.Invoke(ev);
                return ev.IsAllowed;
            }
            catch (Exception e) { Log.Error($"[ReloadingPatch] {e}"); return true; }
        }
    }
}

namespace Unicorn.Internal
{
    /// <summary>
    /// Опрос раз в 0.1 с: предмет в руках (событие ChangedItem) и применение глобальных настроек оружия
    /// к новым игрокам / после смены класса.
    /// </summary>
    internal static class WeaponWatcher
    {
        private static readonly Dictionary<Player, int> Held = new();
        private static readonly Dictionary<Player, int> RoleSeen = new();
        private static float _next, _nextPrune;
        private static readonly HashSet<string> LoggedErrors = new();

        internal static void Tick()
        {
            float t = Time.realtimeSinceStartup;
            if (t < _next) return;
            _next = t + 0.1f;
            foreach (var p in Player.List)
            {
                try
                {
                    if (!p.IsValid) continue;

                    int role = p.RoleId;
                    if (!RoleSeen.TryGetValue(p, out var r0) || r0 != role) { RoleSeen[p] = role; Weapons.ApplyAll(p); }

                    int item = p.Inventory.CurrentItem;
                    if (!Held.TryGetValue(p, out var old)) { Held[p] = item; continue; }
                    if (old == item) continue;
                    Held[p] = item;
                    if (PlayerEvents.ChangedItem.HasHandlers)
                        PlayerEvents.ChangedItem.Invoke(new ChangedItemEventArgs(p, old, item, p.CurrentWeapon));
                }
                catch (Exception e)
                {
                    if (LoggedErrors.Add(e.GetType().Name + e.Message)) Log.Warn($"[WeaponWatcher] {e.Message}");
                }
            }
            if (t >= _nextPrune)
            {
                _nextPrune = t + 10f;
                foreach (var p in new List<Player>(Held.Keys)) if (!p.IsValid) { Held.Remove(p); RoleSeen.Remove(p); }
                foreach (var p in new List<Player>(RoleSeen.Keys)) if (!p.IsValid) RoleSeen.Remove(p);
            }
        }
    }
}
