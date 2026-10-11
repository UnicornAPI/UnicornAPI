using HarmonyLib;
using Mirror;

namespace Unicorn.Patches
{
    /// <summary>Диагностика сети: сколько пакетов пришло от клиентов по каналам (0 = reliable, 1 = unreliable).</summary>
    [HarmonyPatch(typeof(NetworkServer), "gzv")] // UnpackAndInvoke(conn, reader, channelId) -> bool
    internal static class NetDiagInvokePatch
    {
        internal static long Ok0, Ok1, Fail0, Fail1;
        private static void Postfix(int c, bool __result)
        {
            if (c == 1) { if (__result) Ok1++; else Fail1++; }
            else { if (__result) Ok0++; else Fail0++; }
        }
    }
}
