using System;
using System.Collections.Generic;
using HarmonyLib;
using Unicorn.API;
using Unicorn.Events;
using Unicorn.Internal;

namespace Unicorn.Internal
{
    /// <summary>
    /// Баг игры: очереди команд на старте (CCM.classTeamQueues) — по 25 мест. bls берёт queue[i] для игрока №i,
    /// и при 26+ игроках List.get_Item бросает исключение — все оставшиеся остаются без роли.
    /// Перед раздачей дополняем каждую очередь до числа игроков. Добавляем только НЕ-SCP места, повторяя ту же очередь
    /// с начала (пропорции D / учёные / охрана сохраняются, лишних SCP не появляется — их всего 4 вида).
    /// </summary>
    internal static class StartQueueFix
    {
        internal static void Ensure(CharacterClassManager ccm)
        {
            var queues = ccm.classTeamQueues;
            if (queues == null) return;
            int need = 0;
            try { var pm = PlayerManager.singleton; need = pm?.players?.Length ?? 0; } catch { }
            need = Math.Max(need, Player.List.Count) + 2;   // небольшой запас
            for (int qi = 0; qi < queues.Length; qi++)
            {
                var q = queues[qi]?.classTeamQueue;
                if (q == null || q.Count == 0 || q.Count >= need) continue;
                int orig = q.Count, added = 0;
                var pattern = new List<global::Team>();
                for (int i = 0; i < orig; i++) { var t = q[i]; if ((int)t != (int)API.Team.SCP) pattern.Add(t); }
                if (pattern.Count == 0) continue;
                for (int i = 0; q.Count < need; i++) { q.Add(pattern[i % pattern.Count]); added++; }
                Log.Info($"[StartQueueFix] очередь {qi}: {orig} → {q.Count} мест (игроков {need - 2})");
            }
        }
    }
}

namespace Unicorn.Patches
{
    /// <summary>CCM.blu(Team) — случайная роль для команды при раздаче на старте (вызывается только из bls).</summary>
    [HarmonyPatch(typeof(CharacterClassManager), "blu")]
    internal static class StartRoleChoosingPatch
    {
        private static void Postfix(CharacterClassManager __instance, global::Team a, ref int __result)
        {
            if (__result < 0 || !ServerEvents.StartRoleChoosing.HasHandlers) return;
            var team = (API.Team)(int)a;
            if (team == API.Team.SCP) return;   // у SCP своя логика «без повторов» — не трогаем
            try
            {
                var ev = new StartRoleChoosingEventArgs(team, __result);
                ServerEvents.StartRoleChoosing.Invoke(ev);
                if (ev.RoleId == __result) return;
                var roles = __instance.roles;
                if (roles == null || ev.RoleId < 0 || ev.RoleId >= roles.Length || roles[ev.RoleId] == null)
                {
                    Log.Warn($"[StartRole] плагин вернул несуществующую роль {ev.RoleId} — оставляю {__result}");
                    return;
                }
                __result = ev.RoleId;
            }
            catch (Exception e) { Log.Error($"[StartRole] {e}"); }
        }
    }
}
