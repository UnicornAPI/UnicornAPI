using System;
using HarmonyLib;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Баг игры №6: конец раунда → сервер шлёт RpcEndRound, и уже ВНУТРИ этого Rpc (у хоста) стоит
    /// Invoke("EndRound", 10) → EndRound → Invoke("RestartRound"). На dedicated Rpc на сервере не выполняется —
    /// раунд висит на «Restarting round in 10 seconds...» вечно. Делаем то же самое на сервере.
    /// Аналогично RpcRestartRound (чистка объектов DeleteAfterRestart) — вызываем его тело на сервере.
    /// </summary>
    internal static class RoundEndFix
    {
        public const float EndRoundDelay = 10f; // как в игре (UserCode_RpcEndRound)

        public static void OnEnding(RoundManager rm)
        {
            if (!Application.isBatchMode || rm == null) return;
            try
            {
                rm.CancelInvoke("EndRound");
                rm.Invoke("EndRound", EndRoundDelay);
                Log.Info($"[RoundEndFix] рестарт раунда через {EndRoundDelay:0} с");
            }
            catch (Exception e) { Log.Error($"[RoundEndFix] {e}"); }
        }
    }

    [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.RestartRound))]
    internal static class RestartRoundFix
    {
        private static void Postfix(RoundManager __instance)
        {
            if (!Application.isBatchMode) return;
            try { __instance.cqa(); } // UserCode_RpcRestartRound: SetActive UI + удалить объекты с тегом DeleteAfterRestart
            catch (Exception e) { Log.Debug($"[RestartRoundFix] {e.Message}"); }

            // гранаты, которые не удалились после взрыва (см. GrenadeCleanupFix), — убрать до нового раунда
            try { int g = GrenadeCleanupFix.RemoveAll(); if (g > 0) Log.Info($"[RestartRoundFix] удалено гранат: {g}"); } catch { }

            // Игра в RestartRound закрывает все двери через CmdCloseDoor — это Command, на dedicated без клиента он не уходит,
            // и двери, открытые в прошлом раунде, так и оставались открытыми. Закрываем на сервере, как задумано игрой.
            // Замки: игра сама двери не запирает никогда, а объекты дверей живут между раундами — если плагин/админ
            // запер дверь и не отпер (выгрузка, ошибка), она осталась бы запертой навсегда. Снимаем в новом раунде.
            var cfg = Internal.CoreConfig.Current;
            if (!cfg.CloseDoorsOnRestart && !cfg.UnlockDoorsOnRestart) return;
            int n = 0, u = 0;
            try
            {
                foreach (var d in UnityEngine.Object.FindObjectsOfType<global::Door>())
                {
                    try
                    {
                        if (d == null || d.destroyed) continue;
                        if (cfg.UnlockDoorsOnRestart && d.IsLockedRaw()) { d.SetLocked(false); u++; }
                        if (cfg.CloseDoorsOnRestart && d.isOpen) { d.SetOpen(false); n++; }
                    }
                    catch { }
                }
            }
            catch (Exception e) { Log.Warn($"[RestartRoundFix] двери: {e.Message}"); }
            if (n > 0 || u > 0) Log.Info($"[RestartRoundFix] после раунда: закрыто дверей {n}, отперто {u}");
        }
    }
}
