using System;
using UnityEngine;

namespace Unicorn.API
{
    /// <summary>Предметы на полу (pickup): создать и убрать.</summary>
    public static class Pickups
    {
        private static readonly System.Collections.Generic.HashSet<int> NoOwnModelLogged = new();
        /// <summary>Префаб предмета на полу (из базы предметов игры) или null.</summary>
        public static GameObject GetPrefab(int itemId)
        {
            var inv = UnityEngine.Object.FindObjectOfType<global::Inventory>();
            if (inv?._items == null || itemId <= 0 || itemId >= inv._items.Count) return null;
            var holder = inv._items[itemId].Holder;
            return holder != null ? holder.gameObject : null;
        }

        /// <summary>Создать предмет на полу у всех игроков. null — не вышло (нет префаба).</summary>
        public static GameObject Spawn(int itemId, Vector3 position, Quaternion rotation)
        {
            var prefab = GetPrefab(itemId);
            if (prefab == null) { Log.Warn($"[Pickups] нет префаба для предмета {itemId} ({Items.GetName(itemId)})"); return null; }
            var go = UnityEngine.Object.Instantiate(prefab, position, rotation);
            // У некоторых предметов нет своей модели на полу (напр. FPV Drone использует префаб монеты) —
            // тогда модель будет чужая, но при подборе выдастся нужный предмет.
            var h = go.GetComponent<ItemHolder>();
            if (h != null && h._item != itemId)
            {
                h._item = itemId;
                if (NoOwnModelLogged.Add(itemId))
                    Log.Info($"[Pickups] у «{Items.GetName(itemId)}» нет своей модели на полу — лежит как «{Items.GetName(Pickups.GetItemId(prefab))}», подбирается правильно");
            }
            Mirror.NetworkServer.hbf(go, null);   // NetworkServer.Spawn
            return go;
        }

        /// <summary>Убрать предмет с пола у всех игроков.</summary>
        public static void Destroy(GameObject pickup)
        {
            if (pickup == null) return;
            try { Mirror.NetworkServer.hbk(pickup); }   // NetworkServer.Destroy
            catch (Exception e) { Log.Warn($"[Pickups] {e.Message}"); }
        }

        /// <summary>ID предмета, лежащего на полу, или -1.</summary>
        public static int GetItemId(GameObject pickup)
        {
            if (pickup == null) return -1;
            var h = pickup.GetComponent<ItemHolder>();
            return h != null ? h._item : -1;
        }
    }
}
