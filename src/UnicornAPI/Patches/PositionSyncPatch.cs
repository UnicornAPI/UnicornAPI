using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Mirror;
using Unicorn.API;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// Баг игры №4: на dedicated объект игрока на сервере не двигается — позиция «застревает» на спавне.
    /// Из-за этого не работают двери (игра проверяет дистанцию ≤ 15 м) и всё, что зависит от позиции на сервере.
    /// Клиент присылает свою позицию (CmdSyncData, unreliable) — после обработки ставим её объекту игрока.
    /// </summary>
    [HarmonyPatch(typeof(PlayerTransformSync), Game.PTS_InvokeCmdSyncData)]
    internal static class PositionSyncPatch
    {
        internal static readonly Dictionary<IntPtr, (Vector3 Pos, float Time)> Last = new();
        internal static long Calls;

        private static void Postfix(NetworkBehaviour a)
        {
            Calls++;
            try
            {
                var pts = a?.TryCast<PlayerTransformSync>();
                if (pts == null) return;
                var pos = pts.kko; // куда клиент сказал, что он стоит
                Last[pts.Pointer] = (pos, Time.realtimeSinceStartup);
                if (!Application.isBatchMode) return;
                var tr = pts.transform;
                if ((tr.position - pos).sqrMagnitude < 0.0001f) return;
                var cc = pts.GetComponent<CharacterController>();
                if (cc != null && cc.enabled) { cc.enabled = false; tr.position = pos; cc.enabled = true; }
                else tr.position = pos;
            }
            catch (Exception e) { Log.Debug($"[PositionSyncPatch] {e.Message}"); }
        }
    }
}
