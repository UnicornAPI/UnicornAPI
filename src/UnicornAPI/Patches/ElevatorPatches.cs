using System;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Internal;

namespace Unicorn.Patches
{
    /// <summary>Лифт типа Elevator: UserCode_CmdCallElevator(conn). Блокировки из Map.ElevatorBlockers.</summary>
    [HarmonyPatch(typeof(global::Elevator), "btk")]
    internal static class ElevatorCallPatch
    {
        private static bool Prefix(global::Elevator __instance, gu a)
        {
            try
            {
                if (Map.ElevatorBlockers.Count == 0) return true;
                var p = AdminAudit.BySender(a);
                return !Map.IsElevatorBlocked(__instance, p);
            }
            catch (Exception e) { Log.Error($"[ElevatorCallPatch] {e}"); return true; }
        }
    }

    /// <summary>Лифт типа ElevatorManager: invoker CmdCallElevator (тело встроено).</summary>
    [HarmonyPatch(typeof(ElevatorManager), "ro")]
    internal static class ElevatorManagerCallPatch
    {
        private static bool Prefix(NetworkBehaviour a, gu c)
        {
            try
            {
                if (Map.ElevatorBlockers.Count == 0 || a == null) return true;
                var m = a.TryCast<ElevatorManager>();
                if (m == null) return true;
                return !Map.IsElevatorBlocked(m, AdminAudit.BySender(c));
            }
            catch (Exception e) { Log.Error($"[ElevatorManagerCallPatch] {e}"); return true; }
        }
    }
}
