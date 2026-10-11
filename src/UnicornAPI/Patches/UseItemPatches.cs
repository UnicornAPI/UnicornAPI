using System;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Events;
using Unicorn.Internal;

namespace Unicorn.Patches
{
    /// <summary>Общий код: invoker Command отработал → событие UsedItem.</summary>
    internal static class UseItemCommon
    {
        internal static void Fire(NetworkBehaviour nb, UsableKind kind)
        {
            try
            {
                if (nb == null) return;
                var ps = nb.GetComponentInParent<PlayerStatistics>();
                var p = ps != null ? Player.Get(ps.gameObject) : null;
                if (p == null) return;
                PlayerEvents.UsedItem.Invoke(new UsedItemEventArgs(p, kind));
            }
            catch (Exception e) { Log.Error($"[UseItem:{kind}] {e}"); }
        }
    }

    [HarmonyPatch(typeof(Medkit), Game.Medkit_InvokeHealMe)]
    internal static class MedkitUsePatch
    {
        private static void Prefix(out string __state) => __state = ItemReason.Set("use");
        private static void Postfix(NetworkBehaviour a, string __state, bool __runOriginal) { ItemReason.Restore(__state); if (__runOriginal) UseItemCommon.Fire(a, UsableKind.Medkit); }
    }

    [HarmonyPatch(typeof(Scp420j), Game.Scp420j_InvokeDelete)]
    internal static class Scp420jUsePatch
    {
        private static void Prefix(out string __state) => __state = ItemReason.Set("use");
        private static void Postfix(NetworkBehaviour a, string __state, bool __runOriginal) { ItemReason.Restore(__state); if (__runOriginal) UseItemCommon.Fire(a, UsableKind.Scp420J); }
    }

    [HarmonyPatch(typeof(Scp458Item), Game.Scp458_InvokeHealMe)]
    internal static class Scp458UsePatch
    {
        private static void Prefix(out string __state) => __state = ItemReason.Set("use");
        private static void Postfix(NetworkBehaviour a, string __state, bool __runOriginal) { ItemReason.Restore(__state); if (__runOriginal) UseItemCommon.Fire(a, UsableKind.Scp458); }
    }

    [HarmonyPatch(typeof(Scp294Cup), Game.Scp294Cup_InvokeEffect)]
    internal static class Scp294UsePatch
    {
        private static void Prefix(out string __state) => __state = ItemReason.Set("use");
        private static void Postfix(NetworkBehaviour a, string __state, bool __runOriginal) { ItemReason.Restore(__state); if (__runOriginal) UseItemCommon.Fire(a, UsableKind.Scp294); }
    }

    /// <summary>SCP-714: событие только когда кольцо надели (не когда сняли).</summary>
    [HarmonyPatch(typeof(Scp714), Game.Scp714_InvokeWear)]
    internal static class Scp714UsePatch
    {
        internal struct St { public bool Was; public string Prev; }

        private static void Prefix(NetworkBehaviour a, out St __state)
        {
            __state = new St { Prev = ItemReason.Set("use") };
            try { var r = a?.TryCast<Scp714>(); __state.Was = r != null && r.wearingRing; } catch { }
        }

        private static void Postfix(NetworkBehaviour a, St __state, bool __runOriginal)
        {
            ItemReason.Restore(__state.Prev);
            if (!__runOriginal) return;   // команду заблокировал другой плагин (античит)
            try
            {
                var r = a?.TryCast<Scp714>();
                if (r != null && !__state.Was && r.wearingRing) UseItemCommon.Fire(a, UsableKind.Scp714);
            }
            catch (Exception e) { Log.Error($"[UseItem:Scp714] {e}"); }
        }
    }
}
