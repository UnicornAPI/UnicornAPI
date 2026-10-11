using System;

namespace Unicorn.Internal
{
    /// <summary>
    /// Почему меняется инвентарь: pickup, drop, use, admin, command, plugin. Ставится на время вызова,
    /// ItemsSetterPatch кладёт его в ItemAdded/ItemRemoved. Вложенные вызовы: внутренняя причина важнее,
    /// кроме «plugin» — он не перебивает уже заданную причину (команда give → API → plugin: останется command).
    /// </summary>
    internal static class ItemReason
    {
        internal static string Current;

        internal static string Set(string reason) { var prev = Current; Current = reason; return prev; }
        internal static string SetIfEmpty(string reason) { var prev = Current; Current ??= reason; return prev; }
        internal static void Restore(string prev) => Current = prev;

        internal static void With(string reason, Action a) { var p = SetIfEmpty(reason); try { a(); } finally { Restore(p); } }
        internal static T With<T>(string reason, Func<T> f) { var p = SetIfEmpty(reason); try { return f(); } finally { Restore(p); } }
    }
}
