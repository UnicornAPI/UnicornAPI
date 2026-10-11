using System;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Internal;

namespace Unicorn.Patches
{
    /// <summary>
    /// Баг игры: CustomNetworkManager.OnServerConnect сравнивает conn с NetworkServer.localConnection,
    /// которого на dedicated нет (нет хост-клиента) → NullReferenceException → Mirror кикает КАЖДОГО игрока
    /// сразу после авторизации. Заменяем метод целиком: та же проверка бана, без обращения к localConnection.
    /// </summary>
    [HarmonyPatch(typeof(CustomNetworkManager), Game.CNM_OnServerConnect)]
    internal static class ServerConnectPatch
    {
        private static bool Prefix(gu a)
        {
            try
            {
                if (a == null) return false;
                var id = a.Address();
                Log.Info($"[Connect] {id}");
                if (!string.IsNullOrEmpty(id) && Game.IsBanned(id))
                {
                    Log.Warn($"[Connect] забаненный игрок {id} — кик");
                    try { a.SendBanReason("You have been banned on this server."); }
                    catch (Exception e) { Log.Debug($"[Connect] BanReasonMessage не отправился: {e.Message}"); }
                    var conn = a;
                    Timing.CallDelayed(1f, () => { try { conn.Disconnect(); } catch { } });
                }
            }
            catch (Exception e) { Log.Error($"[ServerConnectPatch] {e}"); }
            return false; // оригинал не вызываем — он падает
        }
    }
}
