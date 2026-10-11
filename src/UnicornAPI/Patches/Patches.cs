using System;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Commands;
using Unicorn.Events;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.Patches
{
    // ВАЖНО: имена параметров (a, b, c) должны совпадать с interop-сборкой — Harmony матчит по имени.
    // У сеттеров SyncVar-свойств параметр называется 'a', не 'value' — поэтому там __0.

    /// <summary>Joined: сервер получил от клиента SteamID и ник (UserCode_CmdSetSteamData).</summary>
    [HarmonyPatch(typeof(PlayerStatistics), Game.PS_UserCode_CmdSetSteamData)]
    internal static class JoinedPatch
    {
        private static void Postfix(PlayerStatistics __instance, bool __runOriginal)
        {
            try
            {
                if (!__runOriginal) return;   // команду заблокировал античит (подмена SteamID — игрока кикнут)
                var p = Player.Get(__instance.gameObject);
                if (p == null || p.IsVerified) return;
                p.IsVerified = true;
                Log.Info($"[Joined] {p}");
                Profiles.OnJoined(p);
                PlayerEvents.Joined.Invoke(new JoinedEventArgs(p));
            }
            catch (Exception e) { Log.Error($"[JoinedPatch] {e}"); }
        }
    }

    /// <summary>Left: NetworkManager.OnServerDisconnect(conn) — до уничтожения объекта игрока.</summary>
    [HarmonyPatch(typeof(NetworkManager), Game.NM_OnServerDisconnect)]
    internal static class LeftPatch
    {
        private static void Prefix(gu a)
        {
            try
            {
                var id = a?.Identity();
                if (id == null) return;
                var go = id.gameObject;
                var p = Player.Get(go);
                if (p != null)
                {
                    if (p.IsVerified)
                    {
                        Log.Info($"[Left] {p}");
                        PlayerEvents.Left.Invoke(new LeftEventArgs(p));
                    }
                    Player.Remove(go);
                    ItemsSetterPatch.Forget(go.GetComponent<global::Inventory>());
                }
            }
            catch (Exception e) { Log.Error($"[LeftPatch] {e}"); }
        }
    }

    /// <summary>ChangingRole/ChangedRole: сеттер SyncVar curClass (ловит ВСЕ смены роли на сервере).</summary>
    [HarmonyPatch(typeof(CharacterClassManager), Game.CCM_Prop_CurClass, MethodType.Setter)]
    internal static class RolePatch
    {
        private static bool Prefix(CharacterClassManager __instance, ref int __0, out int __state)
        {
            __state = int.MinValue;
            try
            {
                if (!__instance.IsServer()) return true;
                int old = __instance.curClass;
                if (old == __0) return true;
                var p = Player.Get(__instance.gameObject);
                if (p == null) return true;

                var ev = new ChangingRoleEventArgs(p, old, __0);
                PlayerEvents.ChangingRole.Invoke(ev);
                if (!ev.IsAllowed) return false;
                __0 = ev.NewRole;
                __state = old;
            }
            catch (Exception e) { Log.Error($"[RolePatch] {e}"); }
            return true;
        }

        private static void Postfix(CharacterClassManager __instance, int __state)
        {
            if (__state == int.MinValue) return;
            try
            {
                var p = Player.Get(__instance.gameObject);
                if (p != null && __instance.curClass != __state)
                    PlayerEvents.ChangedRole.Invoke(new ChangedRoleEventArgs(p, __state, __instance.curClass));
            }
            catch (Exception e) { Log.Error($"[RolePatch] {e}"); }
        }
    }

    /// <summary>Hurting/Died: PlayerStatistics.HurtPlayer(target, damage, hitInfo, ...) — вызывается на объекте атакующего.</summary>
    [HarmonyPatch(typeof(PlayerStatistics), Game.PS_HurtPlayer)]
    internal static class HurtPatch
    {
        internal struct State { public bool Track; public bool WasDead; public int Damage; }

        private static bool Prefix(PlayerStatistics __instance, GameObject a, ref int b, string c, out State __state)
        {
            __state = default;
            try
            {
                var target = Player.Get(a);
                if (target == null) return true;
                var attacker = Player.Get(__instance.gameObject);

                var ev = new HurtingEventArgs(target, attacker, b, c);
                PlayerEvents.Hurting.Invoke(ev);
                if (!ev.IsAllowed) return false;
                b = ev.Amount;

                __state = new State { Track = true, WasDead = target.IsDead, Damage = b };
            }
            catch (Exception e) { Log.Error($"[HurtPatch] {e}"); }
            return true;
        }

        private static void Postfix(PlayerStatistics __instance, GameObject a, string c, State __state)
        {
            if (!__state.Track || __state.WasDead) return;
            try
            {
                var target = Player.Get(a);
                if (target == null || !target.IsDead) return;
                var attacker = Player.Get(__instance.gameObject);
                PlayerEvents.Died.Invoke(new DiedEventArgs(target, attacker, __state.Damage, c));
            }
            catch (Exception e) { Log.Error($"[HurtPatch] {e}"); }
        }
    }

    /// <summary>Раунд: сеттер SyncVar RoundManager.info ("Waiting for round start" / "started" / "ending").</summary>
    [HarmonyPatch(typeof(RoundManager), Game.RM_Prop_Info, MethodType.Setter)]
    internal static class RoundStatePatch
    {
        private static void Prefix(RoundManager __instance, out string __state) => __state = __instance.info;

        private static void Postfix(RoundManager __instance, string __state)
        {
            try
            {
                if (!__instance.IsServer()) return;
                var now = __instance.info;
                if (now == __state) return;
                RoundStateTracker.FromSetter(__instance, now);
            }
            catch (Exception e) { Log.Error($"[RoundStatePatch] {e}"); }
        }
    }

    /// <summary>Консоль dedicated-сервера: свои команды поверх help/list/stop.</summary>
    [HarmonyPatch(typeof(ServerConsole), Game.SC_HandleCommand)]
    internal static class ConsolePatch
    {
        private static bool Prefix(string a) => !CommandManager.TryExecute(a);

        private static void Postfix(string a)
        {
            if (a != null && a.Trim().Equals("help", StringComparison.OrdinalIgnoreCase))
                CommandManager.PrintHelp();
        }
    }
}
