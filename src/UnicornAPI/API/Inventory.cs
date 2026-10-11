using System.Collections.Generic;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.API
{
    /// <summary>Справочник предметов игры. ID = индекс в списке предметов игры (как в админ-панели Give).</summary>
    public static class Items
    {
        private static global::Inventory AnyInv => Object.FindObjectOfType<global::Inventory>();

        /// <summary>Все предметы: ID → название.</summary>
        public static IReadOnlyList<ItemInfo> All
        {
            get
            {
                var res = new List<ItemInfo>();
                var inv = AnyInv;
                if (inv?._items == null) return res;
                for (int i = 0; i < inv._items.Count; i++) res.Add(new ItemInfo(i, inv._items[i].Name));
                return res;
            }
        }

        public static string GetName(int id)
        {
            var inv = AnyInv;
            if (inv?._items == null || id < 0 || id >= inv._items.Count) return "Unknown";
            return inv._items[id].Name;
        }

        /// <summary>Доступы предмета (ключ-карты): строки, которые игра сверяет с AccessLevel двери.</summary>
        public static string[] GetPermissions(int id)
        {
            var inv = AnyInv;
            if (inv?._items == null || id < 0 || id >= inv._items.Count) return System.Array.Empty<string>();
            var perms = inv._items[id].permissions;
            if (perms == null) return System.Array.Empty<string>();
            var res = new string[perms.Length];
            for (int i = 0; i < perms.Length; i++) res[i] = perms[i];
            return res;
        }

        /// <summary>Даёт ли предмет доступ к уровню (как Door.CheckAccess в игре — точное совпадение строки).</summary>
        public static bool HasPermission(int id, string accessLevel)
        {
            if (string.IsNullOrEmpty(accessLevel)) return true;
            foreach (var p in GetPermissions(id)) if (p == accessLevel) return true;
            return false;
        }

        /// <summary>ID «пустого» предмета (пустой слот).</summary>
        public static int NoneId
        {
            get
            {
                var inv = AnyInv;
                if (inv?._items == null) return -1;
                for (int i = 0; i < inv._items.Count; i++) if (inv._items[i].Name == inv._noneItemName) return i;
                return -1;
            }
        }

        /// <summary>Найти ID по имени (без учёта регистра, можно часть). -1 если нет.</summary>
        public static int Find(string name)
        {
            if (int.TryParse(name, out var id)) return id;
            foreach (var it in All) if (it.Name != null && it.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return it.Id;
            foreach (var it in All) if (it.Name != null && it.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) return it.Id;
            return -1;
        }
    }

    public readonly struct ItemInfo
    {
        public int Id { get; }
        public string Name { get; }
        public ItemInfo(int id, string name) { Id = id; Name = name; }
        public override string ToString() => $"{Id}: {Name}";
    }

    /// <summary>Инвентарь конкретного игрока.</summary>
    public sealed class PlayerInventory
    {
        private readonly Player _p;
        internal PlayerInventory(Player p) { _p = p; }
        private global::Inventory Inv => _p.InventoryComponent;

        /// <summary>Слоты: ID предмета в каждом слоте (пустой слот = <see cref="Items.NoneId"/>).</summary>
        public int[] Slots
        {
            get
            {
                var arr = Inv?._currentItems;
                if (arr == null) return new int[0];
                var res = new int[arr.Length];
                for (int i = 0; i < arr.Length; i++) res[i] = arr[i];
                return res;
            }
        }

        /// <summary>Только занятые слоты: (слот, ID).</summary>
        public IReadOnlyList<(int Slot, int ItemId)> Items
        {
            get
            {
                var res = new List<(int, int)>();
                int none = API.Items.NoneId;
                var s = Slots;
                for (int i = 0; i < s.Length; i++) if (s[i] != none) res.Add((i, s[i]));
                return res;
            }
        }

        /// <summary>ID предмета в руках.</summary>
        public int CurrentItem => Inv?._currentItem ?? -1;
        /// <summary>Номер выбранного слота.</summary>
        public int CurrentSlot => Inv?._currentSlot ?? -1;
        public string CurrentItemName => API.Items.GetName(CurrentItem);

        public bool IsFull
        {
            get { int none = API.Items.NoneId; foreach (var id in Slots) if (id == none) return false; return true; }
        }

        public bool Has(int itemId) { foreach (var id in Slots) if (id == itemId) return true; return false; }

        /// <summary>Даёт ли предмет В РУКАХ доступ к уровню двери.</summary>
        public bool CurrentItemHasAccess(string accessLevel) => API.Items.HasPermission(CurrentItem, accessLevel);

        /// <summary>Есть ли в инвентаре (в любом слоте) предмет с доступом. slot/itemId — первый подходящий.</summary>
        public bool HasAccess(string accessLevel, out int slot, out int itemId)
        {
            slot = -1; itemId = -1;
            var s = Slots; int none = API.Items.NoneId;
            for (int i = 0; i < s.Length; i++)
                if (s[i] != none && API.Items.HasPermission(s[i], accessLevel)) { slot = i; itemId = s[i]; return true; }
            return false;
        }

        public bool HasAccess(string accessLevel) => HasAccess(accessLevel, out _, out _);

        /// <summary>Выдать предмет по ID (в первый свободный слот, как Give в админке).</summary>
        public void Add(int itemId) => Internal.ItemReason.With("plugin", () => Inv?.AddItem(itemId));

        /// <summary>Удалить предмет из слота.</summary>
        public void RemoveSlot(int slot) => Internal.ItemReason.With("plugin", () => Inv?.DeleteItem(slot));

        /// <summary>Удалить первый предмет с этим ID. true — нашёлся.</summary>
        public bool Remove(int itemId)
        {
            var s = Slots;
            for (int i = 0; i < s.Length; i++) if (s[i] == itemId) { RemoveSlot(i); return true; }
            return false;
        }

        /// <summary>Очистить инвентарь.</summary>
        public void Clear() => Internal.ItemReason.With("plugin", () => Inv?.ClearItems());

        /// <summary>Выбросить предмет из слота на пол.</summary>
        public void Drop(int slot) => Inv?.DropItem(slot);

        /// <summary>Выбросить всё на пол.</summary>
        public void DropAll() => Inv?.DropAllItems();
    }
}
