using System;
using HarmonyLib;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Баг игры (dedicated): граната после взрыва удаляется только у клиентов — в RpcDoExplosionEffects стоит
    /// Destroy(gameObject, destroyTime), а это локальное удаление. У хоста оно заодно удаляло и серверный объект,
    /// на dedicated сервер Rpc не выполняет — взорвавшиеся гранаты (и мины, и флешки) живут на сервере вечно,
    /// и каждый, кто заходит позже, видит их по всему комплексу (поднять нельзя — это не предмет, а снаряд).
    /// Делаем то, что задумано: через destroyTime после взрыва удаляем гранату на сервере (у всех).
    /// Плюс при рестарте раунда удаляем все оставшиеся гранаты.
    /// </summary>
    [HarmonyPatch(typeof(global::Grenade), "bub")]   // [Server] Explode()
    internal static class GrenadeCleanupFix
    {
        internal static int Removed;

        private static void Postfix(global::Grenade __instance)
        {
            if (!Application.isBatchMode || __instance == null) return;
            try
            {
                var go = __instance.gameObject;
                float t = Mathf.Clamp(__instance.destroyTime, 0.5f, 30f) + 0.5f;   // как у клиентов + запас на эффекты
                Timing.CallDelayed(t, () =>
                {
                    try { if (go != null) { Mirror.NetworkServer.hbk(go); Removed++; } }   // NetworkServer.Destroy
                    catch (Exception e) { Log.Debug($"[GrenadeCleanup] {e.Message}"); }
                });
            }
            catch (Exception e) { Log.Debug($"[GrenadeCleanup] {e.Message}"); }
        }

        /// <summary>Удалить все гранаты на карте (конец раунда).</summary>
        internal static int RemoveAll()
        {
            int n = 0;
            try
            {
                foreach (var g in UnityEngine.Object.FindObjectsOfType<global::Grenade>())
                {
                    try { if (g != null) { Mirror.NetworkServer.hbk(g.gameObject); n++; } } catch { }
                }
            }
            catch (Exception e) { Log.Warn($"[GrenadeCleanup] {e.Message}"); }
            Removed += n;
            return n;
        }
    }
}
