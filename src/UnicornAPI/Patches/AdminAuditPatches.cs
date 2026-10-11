using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using Unicorn.API;
using Unicorn.Events;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// Аудит встроенной админ-панели игры → PlayerEvents.AdminAction.
    /// Главное: UserCode_CmdExecuteAdminAction(action, arg, target, sender) — через него идут Kick/Ban/ForceClass/Give/Tp/...
    /// Ответ игры админу (TargetResponse) ловим, пока действие выполняется.
    /// </summary>
    internal static class AdminAudit
    {
        internal static int Depth;
        internal static string Response;

        internal static Player BySender(gu conn)
        {
            if (conn == null) return null;
            var ptr = conn.Pointer;
            return Player.List.FirstOrDefault(p => { try { return p.Connection != null && p.Connection.Pointer == ptr; } catch { return false; } });
        }

        internal static void Fire(Player admin, string action, string arg, Player target, string response)
        {
            if (admin == null) return;
            try { PlayerEvents.AdminAction.Invoke(new AdminActionEventArgs(admin, action, arg ?? "", target, response)); }
            catch (Exception e) { Log.Error($"[AdminAudit] {e}"); }
        }
    }

    [HarmonyPatch(typeof(PlayerAdmin), Game.PA_UserCode_ExecuteAdminAction)]
    internal static class AdminActionPatch
    {
        private static void Prefix(out string __state) { AdminAudit.Depth++; if (AdminAudit.Depth == 1) AdminAudit.Response = null; __state = ItemReason.Set("admin"); }

        private static void Postfix(PlayerAdmin __instance, string a, string b, GameObject c, string __state)
        {
            ItemReason.Restore(__state);
            try
            {
                if (AdminAudit.Depth != 1) return;
                var admin = Player.Get(__instance.gameObject);
                var target = c != null ? Player.Get(c) : null;
                if (a == "RoundLock") RoundLockGuard.NoteManual();
                AdminAudit.Fire(admin, a, b, target, AdminAudit.Response);
            }
            finally { AdminAudit.Depth = Math.Max(0, AdminAudit.Depth - 1); }
        }
    }

    /// <summary>TargetResponse(conn, text) — ответ игры админу.</summary>
    [HarmonyPatch(typeof(PlayerAdmin), Game.PA_TargetResponse)]
    internal static class AdminResponsePatch
    {
        private static void Prefix(string b)
        {
            if (AdminAudit.Depth > 0) AdminAudit.Response = AdminAudit.Response == null ? b : AdminAudit.Response + " | " + b;
        }
    }

    // ---------- Раунд из админ-панели (Command на объекте RoundManager, отправитель = conn) ----------

    [HarmonyPatch(typeof(RoundManager), Game.RM_InvokeRoundLockOn)]
    internal static class AuditRoundLockOn { private static void Postfix(gu c, bool __runOriginal) { if (!__runOriginal) return; RoundLockGuard.NoteManual(); AdminAudit.Fire(AdminAudit.BySender(c), "RoundLockOn", "", null, null); } }

    [HarmonyPatch(typeof(RoundManager), Game.RM_InvokeRoundLockOff)]
    internal static class AuditRoundLockOff { private static void Postfix(gu c, bool __runOriginal) { if (!__runOriginal) return; RoundLockGuard.NoteManual(); AdminAudit.Fire(AdminAudit.BySender(c), "RoundLockOff", "", null, null); } }

    [HarmonyPatch(typeof(RoundManager), Game.RM_InvokeForceRoundStart)]
    internal static class AuditForceRoundStart { private static void Postfix(gu c, bool __runOriginal) { if (__runOriginal) AdminAudit.Fire(AdminAudit.BySender(c), "ForceRoundStart", "", null, null); } }

    // ---------- CmdAddItem / CmdClearInv ----------
    // Это Command'ы КЛИЕНТА: игра шлёт их сама при появлении (GiveStartItems — стартовый набор класса себе,
    // очистка своего инвентаря при смене класса). Выдача из админ-панели идёт через ExecuteAdminAction (выше).
    // Поэтому в журнал админов пишем только то, что НЕ похоже на стартовый набор и отправлено игроком с админкой;
    // всё подозрительное от обычных игроков ловит античит (cheat_suspect).

    internal sealed class InvAuditState
    {
        public Player Sender;
        public bool SenderIsAdmin, SpawnWindow;
        public List<(Player P, int Item)> Changes = new();
        public Action<ItemAddedEventArgs> OnAdd;
        public Action<ItemRemovedEventArgs> OnRemove;
        public string PrevReason;
    }

    [HarmonyPatch(typeof(CharacterClassManager), Game.CCM_InvokeAddItem)]
    internal static class AuditAddItem
    {
        private static void Prefix(NetworkBehaviour a, gu c, out InvAuditState __state)
        {
            var sender = AdminAudit.BySender(c) ?? AntiCheat.Owner(a);
            var st = __state = new InvAuditState { Sender = sender };
            try { st.SenderIsAdmin = AntiCheat.IsAdmin(sender); st.SpawnWindow = sender != null && AntiCheat.InSpawnWindow(sender); } catch { }
            st.OnAdd = e => st.Changes.Add((e.Player, e.ItemId));
            PlayerEvents.ItemAdded += st.OnAdd;
            // стартовый набор → other (как задумано в API), иначе admin — только если у отправителя есть админка
            st.PrevReason = ItemReason.Set(!st.SpawnWindow && st.SenderIsAdmin ? "admin" : "other");
        }

        private static void Postfix(InvAuditState __state)
        {
            if (__state == null) return;
            PlayerEvents.ItemAdded -= __state.OnAdd;
            ItemReason.Restore(__state.PrevReason);
            if (!__state.SenderIsAdmin) return;
            foreach (var (p, item) in __state.Changes)
            {
                bool startKit = p == __state.Sender && __state.SpawnWindow && AntiCheat.IsStartItem(p, item);
                if (!startKit) AdminAudit.Fire(__state.Sender, "AddItem", Items.GetName(item), p, null);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterClassManager), Game.CCM_InvokeClearInv)]
    internal static class AuditClearInv
    {
        private static void Prefix(NetworkBehaviour a, gu c, out InvAuditState __state)
        {
            var sender = AdminAudit.BySender(c) ?? AntiCheat.Owner(a);
            var st = __state = new InvAuditState { Sender = sender };
            try { st.SenderIsAdmin = AntiCheat.IsAdmin(sender); } catch { }
            st.OnRemove = e => st.Changes.Add((e.Player, e.ItemId));
            PlayerEvents.ItemRemoved += st.OnRemove;
            st.PrevReason = ItemReason.Set("other");
        }

        private static void Postfix(InvAuditState __state)
        {
            if (__state == null) return;
            PlayerEvents.ItemRemoved -= __state.OnRemove;
            ItemReason.Restore(__state.PrevReason);
            if (!__state.SenderIsAdmin) return;
            // свой инвентарь игра чистит сама при смене класса — в журнал только чужой
            foreach (var g in __state.Changes.Where(x => x.P != __state.Sender).GroupBy(x => x.P))
                AdminAudit.Fire(__state.Sender, "ClearInv", string.Join(", ", g.Select(x => Items.GetName(x.Item))), g.Key, null);
        }
    }
}
