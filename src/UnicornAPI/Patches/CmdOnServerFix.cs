using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Mirror.RemoteCalls;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// Баг игры №7 (общий): серверный код игры местами вызывает Command (Cmd…) — у хоста это работает, потому что
    /// хост ещё и клиент. На dedicated клиента нет, Mirror пишет ошибку и команда пропадает. Так ломались, например:
    /// старт раунда (ворота/свет CmdStartRound, погода), закрытие дверей при рестарте, экипировка после
    /// удаления предмета, анимация наручников.
    /// Фикс: если Command отправляется на самом сервере — выполняем его на сервере сразу, как это сделал бы хост.
    /// </summary>
    [HarmonyPatch(typeof(NetworkBehaviour), "gif")] // SendCommandInternal(string name, int hash, NetworkWriter, int channel, bool requiresAuthority)
    internal static class CmdOnServerFix
    {
        private static readonly HashSet<string> Logged = new();
        internal static long Executed, Failed;

        private static bool Prefix(NetworkBehaviour __instance, string a, int b, hj c, bool e)
        {
            if (!Application.isBatchMode) return true;
            try
            {
                if (__instance == null || !__instance.IsServerSafe()) return true;
                // requiresAuthority=true: у хоста такой Command на чужом объекте Mirror тоже отбросил бы — не выполняем
                if (e) { if (Logged.Add("auth:" + a)) Log.Debug($"[CmdOnServerFix] {a} пропущен (requiresAuthority, как у хоста)"); return false; }
                var reader = new hd(c.hcm());
                var conn = __instance.njj; // connectionToClient (у объектов сцены — null)
                bool ok;
                Internal.AntiCheat.ServerOrigin++;   // команду шлёт сам сервер — античит её не проверяет
                try { ok = @is.Invoke((ushort)b, RemoteCallType.Command, reader, __instance, conn); }
                finally { Internal.AntiCheat.ServerOrigin--; }
                if (ok) Executed++; else Failed++;
                if (Logged.Add(a)) Log.Info($"[CmdOnServerFix] {a} выполнен на сервере (ok={ok})");
            }
            catch (Exception ex)
            {
                Failed++;
                if (Logged.Add("!" + a)) Log.Warn($"[CmdOnServerFix] {a}: {ex.InnerException?.Message ?? ex.Message}");
            }
            return false; // оригинал на dedicated всё равно ничего бы не отправил, только ошибку в лог
        }

        private static bool IsServerSafe(this NetworkBehaviour nb)
        {
            try { return nb.nja; } catch { return false; } // NetworkBehaviour.isServer
        }
    }
}
