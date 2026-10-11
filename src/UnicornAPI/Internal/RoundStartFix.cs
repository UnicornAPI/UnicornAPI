using System;
using System.Linq;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Баг игры №3: ForceRoundStart раздаёт роли только через CCM хост-игрока (isLocalPlayer).
    /// На dedicated хоста нет → раунд стартует, а игроки остаются в лобби без роли.
    /// Раздаём роли сами тем же методом игры.
    /// </summary>
    internal static class RoundStartFix
    {
        public static bool Run(bool force = false)
        {
            if (!force && !Application.isBatchMode) return false;
            try
            {
                var p = Player.List.FirstOrDefault(x => x.ClassManager != null);
                if (p == null) return false;
                p.ClassManager.DistributeRoles();
                Log.Info($"[RoundStartFix] роли розданы ({Player.List.Count} игр.)");
                return true;
            }
            catch (Exception e) { Log.Error($"[RoundStartFix] {e}"); return false; }
        }
    }
}
