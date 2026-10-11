using System;
using System.Collections.Generic;
using HarmonyLib;
using Unicorn.API;
using Unicorn.Events;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// SCP-914: корутина переработки (Scp914.co). Состояние 1 = прошло 14 с, сейчас игра переработает предметы
    /// из itemsInTrigger. До этого спрашиваем плагины (ServerEvents.Scp914UpgradingItem): предметы с подменённым
    /// результатом убираем из списка игры и после её прохода перерабатываем сами — в ту же точку выхода,
    /// что и игра: tpPos.TransformPoint(trigger.InverseTransformPoint(pos) + (0, 0.3, 0)), поворот как у входа.
    /// </summary>
    [HarmonyPatch(typeof(Scp914.co), "MoveNext")]
    internal static class Scp914Patch
    {
        internal sealed class Job { public GameObject Pickup; public int Output; public Vector3 Pos; public Quaternion Rot; }

        private static void Prefix(Scp914.co __instance, out List<Job> __state)
        {
            __state = null;
            try
            {
                if (__instance.kmb != 1 || !ServerEvents.Scp914UpgradingItem.HasHandlers) return;
                var scp = __instance.kmd;
                if (scp == null || scp.itemsInTrigger == null || scp.itemsInTrigger.Count == 0) return;
                var mode = (Scp914Mode)(int)scp.curState;
                var list = scp.itemsInTrigger;
                var remove = new List<GameObject>();
                for (int i = 0; i < list.Count; i++)
                {
                    var go = list[i];
                    if (go == null) continue;
                    int id = Pickups.GetItemId(go);
                    if (id < 0) continue;
                    var ev = new Scp914UpgradingItemEventArgs(go, id, mode);
                    ServerEvents.Scp914UpgradingItem.Invoke(ev);
                    if (!ev.IsAllowed) { remove.Add(go); continue; }
                    if (ev.OutputItemId == null) continue;
                    var local = scp.trigger.transform.InverseTransformPoint(go.transform.position) + new Vector3(0f, 0.3f, 0f);
                    (__state ??= new List<Job>()).Add(new Job
                    {
                        Pickup = go, Output = ev.OutputItemId.Value,
                        Pos = scp.tpPos.transform.TransformPoint(local), Rot = go.transform.rotation,
                    });
                    remove.Add(go);
                }
                foreach (var go in remove) list.Remove(go);
            }
            catch (Exception e) { Log.Error($"[Scp914Patch] {e}"); }
        }

        private static void Postfix(List<Job> __state)
        {
            if (__state == null) return;
            foreach (var j in __state)
            {
                try
                {
                    Pickups.Destroy(j.Pickup);
                    if (j.Output > 0) Pickups.Spawn(j.Output, j.Pos, j.Rot);
                }
                catch (Exception e) { Log.Error($"[Scp914Patch] {e}"); }
            }
        }
    }
}
