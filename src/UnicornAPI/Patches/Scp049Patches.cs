using System;
using HarmonyLib;
using Unicorn.API;
using Unicorn.Events;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// Удар SCP-049 → ccm.ForceClass(цель, 13 /*049-2*/). В этой версии игры удар идёт через
    /// ScpManager.Cmd049Force (UserCode czk); старый Scp049Player.CmdHurtPlayer (cte) тоже помечаем — на всякий случай.
    /// Пока идёт удар, в ForceClass(…, 13) спрашиваем плагины (PlayerEvents.Scp049Converting).
    /// </summary>
    internal static class Scp049State
    {
        public const int ZombieClassId = 13;   // так в игре: оба пути удара 049 зовут ForceClass(цель, 13)

        private static Player _scp;
        private static int _frame = -1;
        private static bool _inHandlers;

        /// <summary>SCP-049, чей удар сейчас обрабатывается (тот же кадр, не внутри обработчиков плагинов).</summary>
        internal static Player CurrentScp => !_inHandlers && _frame == Time.frameCount ? _scp : null;

        internal static void Enter(Mirror.NetworkBehaviour nb) { _scp = AntiCheat.Owner(nb); _frame = Time.frameCount; }
        internal static void Exit() { _scp = null; _frame = -1; }

        /// <summary>Пока работают обработчики, вложенные ForceClass (смерть → наблюдатель и т.п.) идут как обычно.</summary>
        internal static bool Ask(Scp049ConvertingEventArgs ev)
        {
            _inHandlers = true;
            try { PlayerEvents.Scp049Converting.Invoke(ev); }
            finally { _inHandlers = false; }
            return ev.IsAllowed;
        }
    }

    [HarmonyPatch(typeof(ScpManager), Game.SM_UserCode_Cmd049Force)]
    internal static class Scp049ForcePatch
    {
        private static void Prefix(ScpManager __instance) => Scp049State.Enter(__instance);
        private static void Postfix() => Scp049State.Exit();
    }

    [HarmonyPatch(typeof(Scp049Player), Game.S049_UserCode_HurtPlayer)]
    internal static class Scp049HurtPatch
    {
        private static void Prefix(Scp049Player __instance) => Scp049State.Enter(__instance);
        private static void Postfix() => Scp049State.Exit();
    }

    [HarmonyPatch(typeof(CharacterClassManager), nameof(CharacterClassManager.ForceClass))]
    internal static class Scp049ForceClassPatch
    {
        private static bool Prefix(GameObject ply, int classId)
        {
            if (classId != Scp049State.ZombieClassId) return true;
            var scp = Scp049State.CurrentScp;
            if (scp == null) return true;
            try
            {
                var target = Player.Get(ply);
                if (target == null) return true;
                if (!PlayerEvents.Scp049Converting.HasHandlers) { Log.Info($"[Scp049] {scp.Nickname} → {target.Nickname}: превращение (плагинов нет)"); return true; }
                var ev = new Scp049ConvertingEventArgs(scp, target);
                bool allowed = Scp049State.Ask(ev);
                Log.Info($"[Scp049] {scp.Nickname} → {target.Nickname}: {(allowed ? "превращение" : "превращение отменено плагином")}");
                return allowed;
            }
            catch (Exception e) { Log.Error($"[Scp049] {e}"); return true; }
        }
    }
}
