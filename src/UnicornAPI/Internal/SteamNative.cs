using System;
using System.Runtime.InteropServices;

namespace Unicorn.Internal
{
    /// <summary>Прямые вызовы steam_api64.dll (flat API) — то, что игра не использует и IL2CPP вырезал.</summary>
    internal static class SteamNative
    {
        private const string Dll = "steam_api64";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamUser_v023();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);

        /// <summary>SteamID64 аккаунта Steam, под которым запущен сервер. Именно к нему подключаются игроки.</summary>
        public static ulong UserSteamId()
        {
            try
            {
                var u = SteamAPI_SteamUser_v023();
                return u == IntPtr.Zero ? 0 : SteamAPI_ISteamUser_GetSteamID(u);
            }
            catch { return 0; }
        }
    }
}
