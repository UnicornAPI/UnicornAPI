using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Patches;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Баг игры (dedicated): мина после взрыва удаляется только у клиентов (RpcDeleteMine → Destroy у клиента).
    /// У хоста это удаляло и серверный объект; на dedicated мина остаётся на сервере навсегда. Все, кто зашёл позже,
    /// её видят, и каждый раз, когда кто-то в неё входит, КАЖДЫЙ клиент (команда без проверки владельца) шлёт
    /// CmdSpawnGrenade — мина бесконечно плодит гранаты (а гранаты на сервере тоже не удалялись — см. GrenadeCleanupFix).
    /// Делаем как задумано: после взрыва мина удаляется на сервере; повторные команды по уже взорвавшейся мине
    /// не выполняются. Проверку «точка взрыва далеко от мины» (чит) делает античит P90Guard.
    /// </summary>
    [HarmonyPatch(typeof(global::Landmine), "pf")]   // invoker CmdSpawnGrenade(Vector3 pos, Vector3 vel), rA=0
    internal static class LandmineCommandFix
    {
        // мина → её объект. Адрес в памяти после удаления мины может достаться новой мине (SpawnTool), поэтому
        // запись считается, только пока сохранённый объект жив; удалённая мина из списка убирается.
        internal static readonly Dictionary<IntPtr, GameObject> Exploded = new();

        private static bool AlreadyExploded(NetworkBehaviour a)
        {
            if (!Exploded.TryGetValue(a.Pointer, out var go)) return false;
            bool alive; try { alive = go != null; } catch { alive = false; }
            if (alive) return true;
            Exploded.Remove(a.Pointer);   // та мина уже удалена — это новая мина на том же адресе
            return false;
        }
        internal static int Blocked;

        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            if (!Application.isBatchMode || a == null) return true;
            try
            {
                if (AlreadyExploded(a)) { Blocked++; return false; }   // эта мина уже взорвалась
                // проверку «точка взрыва далеко от мины» (чит) делает античит P90Guard (landmine_far)
                Exploded[a.Pointer] = a.gameObject;
                var who = AdminAudit.BySender(c);
                Log.Info($"[Landmine] мина «{a.gameObject.name}» взорвалась (сработала у {who?.Nickname ?? "?"})");
                return true;
            }
            catch (Exception e) { Log.Debug($"[LandmineFix] {e.Message}"); return true; }
        }
    }

    [HarmonyPatch(typeof(global::Landmine), "pe")]   // [Server] UserCode_CmdSpawnGrenade: граната + RpcDeleteMine
    internal static class LandmineExplodeFix
    {
        internal static int Removed;

        private static void Postfix(global::Landmine __instance)
        {
            if (!Application.isBatchMode || __instance == null) return;
            try
            {
                var go = __instance.gameObject;
                // на следующем кадре, не внутри обработки команды
                Timing.CallDelayed(0.05f, () =>
                {
                    try { if (go != null) { NetworkServer.hbk(go); Removed++; } }   // NetworkServer.Destroy
                    catch (Exception e) { Log.Debug($"[LandmineFix] {e.Message}"); }
                    // из Exploded сразу НЕ убираем: если объект лишь выключается, а не удаляется, повторных взрывов не будет.
                    // Удалённая мина уберётся из списка при следующей проверке по этому адресу (AlreadyExploded).
                });
            }
            catch (Exception e) { Log.Debug($"[LandmineFix] {e.Message}"); }
        }
    }
}
