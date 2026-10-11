using HarmonyLib;
using UnityEngine;

namespace Unicorn.Patches
{
    // На dedicated-сервере Discord RPC игры бесконечно переподключается к discord-ipc-0..9 и засыпает консоль.
    // В batchmode глушим его полностью. Имена не обфусцированы (Unity-сообщения и интерфейсный Connect).

    [HarmonyPatch(typeof(Lachee.Discord.DiscordManager))]
    internal static class DiscordManagerPatch
    {
        [HarmonyPatch("OnEnable"), HarmonyPrefix] private static bool OnEnable() => !Application.isBatchMode;
        [HarmonyPatch("Start"), HarmonyPrefix] private static bool Start() => !Application.isBatchMode;
        [HarmonyPatch("FixedUpdate"), HarmonyPrefix] private static bool FixedUpdate() => !Application.isBatchMode;
    }

    [HarmonyPatch(typeof(Lachee.Discord.Control.nd), nameof(Lachee.Discord.Control.nd.Connect))]
    internal static class DiscordPipePatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Application.isBatchMode) return true;
            __result = false;
            return false;
        }
    }
}
