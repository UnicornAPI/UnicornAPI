using System;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Events;
using Unicorn.Internal;
using UnityEngine;
using GameDoor = global::Door;
using GameInventory = global::Inventory;

namespace Unicorn.Patches
{
    /// <summary>Игрок нажал на дверь (сервер: ServerRequestChangeState).</summary>
    [HarmonyPatch(typeof(GameDoor), Game.Door_ServerRequestChangeState)]
    internal static class DoorInteractPatch
    {
        internal struct St { public bool Track, WasOpen, Allowed; public int Item; }

        private static void Postfix(GameDoor __instance, GameObject a, St __state)
        {
            if (!__state.Track) return;
            try
            {
                var p = Player.Get(a); if (p == null) return;
                bool open = __instance.isOpen;
                string res = open != __state.WasOpen ? (open ? "opened" : "closed") : (__state.Allowed ? "denied" : "blocked");
                PlayerEvents.DoorInteracted.Invoke(new DoorInteractedEventArgs(p, API.Door.Get(__instance), res, __state.Item));
            }
            catch (Exception e) { Log.Error($"[DoorInteractPatch] {e}"); }
        }

        private static bool Prefix(GameDoor __instance, GameObject a, out St __state)
        {
            __state = default;
            try { var pp = Player.Get(a); if (pp != null) __state = new St { Track = true, WasOpen = __instance.isOpen, Item = pp.Inventory.CurrentItem, Allowed = true }; } catch { }
            // Фикс dedicated: флаг «дверь в движении» (MovingStatus.inMove) выставляет анимация двери. На сервере она
            // иногда «застревает» в true, и игра навсегда отказывает открывать дверь. Если задержка двери уже прошла —
            // считаем, что дверь стоит на месте (у игры это то же время, что и анимация).
            if (Application.isBatchMode)
            {
                try
                {
                    var st = __instance.status;
                    if (st != null && st.inMove && __instance.kec <= 0f) st.inMove = false;
                }
                catch { }
            }
            try
            {
                var p = Player.Get(a);
                if (p == null) return true;
                var ev = new InteractingDoorEventArgs(p, API.Door.Get(__instance));
                PlayerEvents.InteractingDoor.Invoke(ev);
                __state.Allowed = ev.IsAllowed;
                return ev.IsAllowed;
            }
            catch (Exception e) { Log.Error($"[DoorInteractPatch] {e}"); return true; }
        }
    }

    /// <summary>
    /// Игрок надевает наручники (UserCode_CmdCuffPlayer на HandCuffs того, кто надевает).
    /// + фикс dedicated: игра шлёт анимацию через Command (CmdSetBool/CmdCuffEffect), что работает только у хоста.
    /// </summary>
    [HarmonyPatch(typeof(HandCuffs), Game.HC_UserCode_CmdCuffPlayer)]
    internal static class CuffPatch
    {
        private static bool Prefix(HandCuffs __instance, GameInventory a)
        {
            try
            {
                var target = Player.Get(a); var cuffer = Player.Get(__instance);
                if (target == null) return true;
                var ev = new CuffingEventArgs(target, cuffer);
                PlayerEvents.Cuffing.Invoke(ev);
                return ev.IsAllowed;
            }
            catch (Exception e) { Log.Error($"[CuffPatch] {e}"); return true; }
        }

    }

    // ВНИМАНИЕ: у игроков UserCode_CmdUnCuffPlayer встроен компилятором в invoker, поэтому это событие срабатывает
    // только при прямых вызовах. Надёжно ловить снятие наручников — через CuffChanged.
    [HarmonyPatch(typeof(HandCuffs), Game.HC_UserCode_CmdUnCuffPlayer)]
    internal static class UncuffPatch
    {
        private static bool Prefix(HandCuffs __instance, GameInventory a)
        {
            try
            {
                var target = Player.Get(a); var cuffer = Player.Get(__instance);
                if (target == null) return true;
                var ev = new UncuffingEventArgs(target, cuffer);
                PlayerEvents.Uncuffing.Invoke(ev);
                return ev.IsAllowed;
            }
            catch (Exception e) { Log.Error($"[UncuffPatch] {e}"); return true; }
        }

    }

    /// <summary>CuffChanged: сеттер SyncVar cuffed.</summary>
    [HarmonyPatch(typeof(GameInventory), Game.Inv_Prop_Cuffed, MethodType.Setter)]
    internal static class CuffedSetterPatch
    {
        private static void Prefix(GameInventory __instance, out bool __state) => __state = __instance.cuffed;

        private static void Postfix(GameInventory __instance, bool __state)
        {
            try
            {
                if (!__instance.IsServer() || __instance.cuffed == __state) return;
                var p = Player.Get(__instance);
                if (p == null) return;
                bool now = __instance.cuffed;
                PlayerEvents.CuffChanged.Invoke(new CuffChangedEventArgs(p, now));
            }
            catch (Exception e) { Log.Error($"[CuffedSetterPatch] {e}"); }
        }
    }

    /// <summary>Подбор предмета (UserCode_CmdCollect).</summary>
    [HarmonyPatch(typeof(GameInventory), Game.Inv_UserCode_CmdCollect)]
    internal static class PickupPatch
    {
        private static void Postfix(string __state) => ItemReason.Restore(__state);

        private static bool Prefix(GameInventory __instance, NetworkIdentity a, out string __state)
        {
            __state = ItemReason.Set("pickup");
            try
            {
                var p = Player.Get(__instance);
                if (p == null || a == null) return true;
                var ev = new PickingUpItemEventArgs(p, a.gameObject);
                PlayerEvents.PickingUpItem.Invoke(ev);
                return ev.IsAllowed;
            }
            catch (Exception e) { Log.Error($"[PickupPatch] {e}"); return true; }
        }
    }

    /// <summary>Выброс предмета (ServerDropItem: и игрок, и DropAll, и плагины).</summary>
    [HarmonyPatch(typeof(GameInventory), Game.Inv_ServerDropItem)]
    internal static class DropPatch
    {
        private static void Postfix(string __state) => ItemReason.Restore(__state);

        private static bool Prefix(GameInventory __instance, int a, out string __state)
        {
            __state = ItemReason.Set("drop");
            try
            {
                var p = Player.Get(__instance);
                if (p == null) return true;
                var slots = p.Inventory.Slots;
                int id = a >= 0 && a < slots.Length ? slots[a] : -1;
                var ev = new DroppingItemEventArgs(p, a, id);
                PlayerEvents.DroppingItem.Invoke(ev);
                return ev.IsAllowed;
            }
            catch (Exception e) { Log.Error($"[DropPatch] {e}"); return true; }
        }
    }

    /// <summary>
    /// ItemAdded/ItemRemoved: сеттер SyncVar _currentItems — сравниваем слоты с последним известным состоянием.
    /// Серверные операции игры (AddItem и др.) внутри одного вызова могут переписать массив несколько раз
    /// (сначала очистить, потом заполнить заново) — в лог это шло как «всё потерял и снова получил».
    /// Поэтому внутри таких операций изменения копим и сравниваем один раз — в конце внешней операции.
    /// </summary>
    [HarmonyPatch(typeof(GameInventory), Game.Inv_Prop_Items, MethodType.Setter)]
    internal static class ItemsSetterPatch
    {
        private static int[] Copy(GameInventory inv)
        {
            var arr = inv._currentItems;
            if (arr == null) return Array.Empty<int>();
            var r = new int[arr.Length];
            for (int i = 0; i < r.Length; i++) r[i] = arr[i];
            return r;
        }

        // Последнее известное (уже разосланное в события) состояние слотов.
        private static readonly System.Collections.Generic.Dictionary<IntPtr, int[]> Last = new();

        // Открытые операции по инвентарю: глубина вложенности и кадр (если игра бросила исключение и постфикс
        // операции не выполнился — на следующем кадре «зависшая» операция не мешает).
        private static readonly System.Collections.Generic.Dictionary<IntPtr, (int Depth, int Frame)> Scope = new();

        internal static void BeginOp(GameInventory inv)
        {
            if (inv == null) return;
            var k = inv.Pointer; int f = Time.frameCount;
            Scope.TryGetValue(k, out var sc);
            if (sc.Frame != f) sc = (0, f);
            Scope[k] = (sc.Depth + 1, f);
        }

        internal static void EndOp(GameInventory inv)
        {
            if (inv == null) return;
            var k = inv.Pointer;
            if (!Scope.TryGetValue(k, out var sc)) return;
            if (sc.Frame != Time.frameCount || sc.Depth <= 1) { Scope.Remove(k); Flush(inv); }
            else Scope[k] = (sc.Depth - 1, sc.Frame);
        }

        private static bool InOp(GameInventory inv) =>
            Scope.TryGetValue(inv.Pointer, out var sc) && sc.Depth > 0 && sc.Frame == Time.frameCount;

        private static void Postfix(GameInventory __instance)
        {
            try
            {
                if (!__instance.IsServer()) return;
                if (InOp(__instance)) return;          // внутри операции — сравним в её конце
                Flush(__instance);
            }
            catch (Exception e) { Log.Error($"[ItemsSetterPatch] {e}"); }
        }

        private static void Flush(GameInventory inv)
        {
            try
            {
                if (inv == null || !inv.IsServer()) return;
                var now = Copy(inv);
                Last.TryGetValue(inv.Pointer, out var before);
                Last[inv.Pointer] = now;
                var p = Player.Get(inv);
                if (p == null) return;
                int none = Items.NoneId;
                before ??= Array.Empty<int>();
                int n = Math.Max(now.Length, before.Length);
                for (int i = 0; i < n; i++)
                {
                    int b = i < before.Length ? before[i] : none;
                    int a = i < now.Length ? now[i] : none;
                    if (b == a) continue;
                    var why = ItemReason.Current ?? "other";
                    if (b != none) PlayerEvents.ItemRemoved.Invoke(new ItemRemovedEventArgs(p, i, b, why));
                    if (a != none) PlayerEvents.ItemAdded.Invoke(new ItemAddedEventArgs(p, i, a, why));
                }
            }
            catch (Exception e) { Log.Error($"[ItemsSetterPatch] {e}"); }
        }

        internal static void Forget(GameInventory inv) { if (inv != null) { Last.Remove(inv.Pointer); Scope.Remove(inv.Pointer); } }
    }

    // Серверные операции игры с инвентарём — границы для ItemsSetterPatch.
    [HarmonyPatch(typeof(GameInventory), "bvy")]   // [Server] AddItem(int)
    internal static class InvOpAddItem
    {
        private static void Prefix(GameInventory __instance) { try { ItemsSetterPatch.BeginOp(__instance); } catch { } }
        private static void Postfix(GameInventory __instance) { try { ItemsSetterPatch.EndOp(__instance); } catch { } }
    }

    [HarmonyPatch(typeof(GameInventory), "bvw")]   // [Server] удалить предмет (слот/текущий)
    internal static class InvOpRemoveItem
    {
        private static void Prefix(GameInventory __instance) { try { ItemsSetterPatch.BeginOp(__instance); } catch { } }
        private static void Postfix(GameInventory __instance) { try { ItemsSetterPatch.EndOp(__instance); } catch { } }
    }

    [HarmonyPatch(typeof(GameInventory), "bvx")]   // [Server] очистить инвентарь
    internal static class InvOpClear
    {
        private static void Prefix(GameInventory __instance) { try { ItemsSetterPatch.BeginOp(__instance); } catch { } }
        private static void Postfix(GameInventory __instance) { try { ItemsSetterPatch.EndOp(__instance); } catch { } }
    }

    [HarmonyPatch(typeof(GameInventory), "bwm")]   // [Server] выбросить предмет из слота
    internal static class InvOpDrop
    {
        private static void Prefix(GameInventory __instance) { try { ItemsSetterPatch.BeginOp(__instance); } catch { } }
        private static void Postfix(GameInventory __instance) { try { ItemsSetterPatch.EndOp(__instance); } catch { } }
    }
}
