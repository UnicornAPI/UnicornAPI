using System;
using HarmonyLib;
using Unicorn.API;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// Баг игры №5: карта генерируется по ключу из SyncVar-хука ZoneGenerator._key. Хуки на сервере срабатывают
    /// только у хоста, поэтому на dedicated сервер ключ выбирает, а зоны у себя НЕ строит. У клиентов одна карта,
    /// у сервера другая → двери «в километре», предметы падают сквозь пол, урон/триггеры не там.
    /// После InitKey генерируем зону на сервере тем же ключом.
    /// </summary>
    [HarmonyPatch(typeof(ZoneGenerator), "Start")]
    internal static class MapGenFix
    {
        private static void Postfix(ZoneGenerator __instance)
        {
            if (!Application.isBatchMode) return;
            try
            {
                if (!__instance.IsServer()) return;
                int key = __instance._key;
                bool ok = __instance.GenerateZone(key);
                Log.Info($"[MapGenFix] {__instance.gameObject.name}: зона сгенерирована на сервере (ключ {key}, ok={ok})");
            }
            catch (Exception e) { Log.Error($"[MapGenFix] {e}"); }
        }
    }
}
