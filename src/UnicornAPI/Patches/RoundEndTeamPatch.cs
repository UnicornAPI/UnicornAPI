using System;
using System.Collections.Generic;
using HarmonyLib;
using Unicorn.API;

namespace Unicorn.Patches
{
    /// <summary>
    /// RoundManager.TryToEndRound (раз в секунду): игра собирает команды живых игроков из roles[curClass].team своего
    /// CharacterClassManager и пропускает Spectator (7) и Watcher (8). Раунд кончается, если команд осталось 0–1
    /// или только охрана/МОГ. Для игроков с Player.SetRoundEndTeam на время этого вызова подменяем team их роли
    /// и сразу возвращаем (Finalizer — даже если игра бросит исключение).
    /// </summary>
    [HarmonyPatch(typeof(RoundManager), "TryToEndRound")]
    internal static class RoundEndTeamPatch
    {
        private static readonly Stack<(global::Class Cls, global::Team Team)> Saved = new();

        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            Saved.Clear();
            try
            {
                foreach (var p in Player.List)
                {
                    try
                    {
                        if (!p.IsValid) continue;
                        var t = p.RoundEndTeam;
                        if (t == null) continue;
                        var ccm = p.ClassManager;
                        var roles = ccm.roles;
                        int id = ccm.curClass;
                        if (roles == null || id < 0 || id >= roles.Length) continue;
                        var cls = roles[id];
                        if (cls == null) continue;
                        Saved.Push((cls, cls.team));
                        cls.team = (global::Team)(int)t.Value;
                    }
                    catch (Exception e) { Log.Debug($"[RoundEndTeam] {e.Message}"); }
                }
            }
            catch (Exception e) { Log.Debug($"[RoundEndTeam] {e.Message}"); }
        }

        private static void Finalizer()
        {
            // в обратном порядке: если у двух игроков один и тот же объект роли, вернётся исходное значение
            while (Saved.Count > 0)
            {
                var (cls, team) = Saved.Pop();
                try { cls.team = team; } catch { }
            }
        }
    }
}
