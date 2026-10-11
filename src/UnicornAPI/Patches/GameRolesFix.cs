using System;
using HarmonyLib;
using Unicorn.API;
using Unicorn.Internal;

namespace Unicorn.Patches
{
    /// <summary>
    /// При входе игра назначает игроку роль из своего префаба (CmdAssignRoleBySteamId) и перезаписывает
    /// бейдж/цвет — даже пустыми значениями. Возвращаем то, что поставили плагины.
    /// </summary>
    [HarmonyPatch(typeof(GameRoles), Game.GR_AssignRoleBySteamId)]
    internal static class GameRolesFix
    {
        private static void Postfix(GameRoles __instance)
        {
            try { Player.Get(__instance)?.ReapplyCustom(); }
            catch (Exception e) { Log.Debug($"[GameRolesFix] {e.Message}"); }
        }
    }
}
