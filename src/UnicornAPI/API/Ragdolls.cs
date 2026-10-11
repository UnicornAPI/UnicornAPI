using System;
using UnityEngine;

namespace Unicorn.API
{
    /// <summary>Трупы (Ragdoll).</summary>
    public static class Ragdolls
    {
        /// <summary>
        /// Ближайший не сгнивший труп игрока с этим ником в радиусе от точки. nick = null — любой.
        /// </summary>
        public static GameObject FindNear(Vector3 pos, float radius, string nick = null)
        {
            GameObject best = null; float bestD = radius;
            try
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Ragdoll>())
                {
                    if (r == null || r.decayed) continue;
                    if (nick != null && !string.IsNullOrEmpty(r.ownerNickName) && !string.Equals(r.ownerNickName, nick, StringComparison.Ordinal)) continue;
                    float d = Vector3.Distance(r.transform.position, pos);
                    if (d <= bestD) { bestD = d; best = r.gameObject; }
                }
            }
            catch (Exception e) { Log.Debug($"[Ragdolls] {e.Message}"); }
            return best;
        }

        /// <summary>Сгнил ли труп (игра помечает decayed).</summary>
        public static bool IsDecayed(GameObject ragdoll)
        {
            try { var r = ragdoll != null ? ragdoll.GetComponent<Ragdoll>() : null; return r == null || r.decayed; }
            catch { return true; }
        }

        /// <summary>Ник игрока, чей это труп (может быть пустым).</summary>
        public static string OwnerNick(GameObject ragdoll)
        {
            try { var r = ragdoll != null ? ragdoll.GetComponent<Ragdoll>() : null; return r != null ? r.ownerNickName : null; }
            catch { return null; }
        }

        /// <summary>Убрать труп у всех игроков.</summary>
        public static void Destroy(GameObject ragdoll) => Pickups.Destroy(ragdoll);
    }
}
