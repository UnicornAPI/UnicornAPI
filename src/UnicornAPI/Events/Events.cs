using System;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Events
{
    /// <summary>Событие с безопасным вызовом: исключение в одном обработчике не ломает остальные и игру.</summary>
    public sealed class Event<T>
    {
        private event Action<T> Handlers;

        /// <summary>Есть ли подписчики (чтобы не собирать аргументы зря).</summary>
        public bool HasHandlers => Handlers != null;

        public void Subscribe(Action<T> h) => Handlers += h;
        public void Unsubscribe(Action<T> h) => Handlers -= h;

        public static Event<T> operator +(Event<T> e, Action<T> h) { e.Subscribe(h); return e; }
        public static Event<T> operator -(Event<T> e, Action<T> h) { e.Unsubscribe(h); return e; }

        internal int RemoveOwnedBy(System.Reflection.Assembly asm)
        {
            var list = Handlers; if (list == null) return 0; int n = 0;
            foreach (var d in list.GetInvocationList())
                if (EventUtil.Owned(d, asm)) { Handlers -= (Action<T>)d; n++; }
            return n;
        }

        internal void Invoke(T args)
        {
            var list = Handlers;
            if (list == null) return;
            foreach (Action<T> h in list.GetInvocationList())
            {
                try { h(args); }
                catch (Exception ex) { Log.Error($"[Event {typeof(T).Name}] {h.Method.DeclaringType?.FullName}.{h.Method.Name}: {ex}"); }
            }
        }
    }

    public sealed class Event
    {
        private event Action Handlers;
        public static Event operator +(Event e, Action h) { e.Handlers += h; return e; }
        public static Event operator -(Event e, Action h) { e.Handlers -= h; return e; }
        public void Subscribe(Action h) => Handlers += h;
        public void Unsubscribe(Action h) => Handlers -= h;

        internal int RemoveOwnedBy(System.Reflection.Assembly asm)
        {
            var list = Handlers; if (list == null) return 0; int n = 0;
            foreach (var d in list.GetInvocationList())
                if (EventUtil.Owned(d, asm)) { Handlers -= (Action)d; n++; }
            return n;
        }

        internal void Invoke()
        {
            var list = Handlers;
            if (list == null) return;
            foreach (Action h in list.GetInvocationList())
            {
                try { h(); }
                catch (Exception ex) { Log.Error($"[Event] {h.Method.DeclaringType?.FullName}.{h.Method.Name}: {ex}"); }
            }
        }
    }

    internal static class EventUtil
    {
        public static bool Owned(Delegate d, System.Reflection.Assembly asm) =>
            d.Method.DeclaringType?.Assembly == asm || (d.Target != null && d.Target.GetType().Assembly == asm);

        /// <summary>Снять все обработчики, объявленные в сборке плагина (для горячей перезагрузки).</summary>
        public static int RemoveAllOwnedBy(System.Reflection.Assembly asm)
        {
            int n = 0;
            foreach (var holder in new[] { typeof(PlayerEvents), typeof(ServerEvents) })
                foreach (var prop in holder.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    var ev = prop.GetValue(null);
                    var m = ev?.GetType().GetMethod("RemoveOwnedBy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (m != null) n += (int)m.Invoke(ev, new object[] { asm });
                }
            return n;
        }
    }

    // ================= Аргументы =================

    public class PlayerEventArgs
    {
        public Player Player { get; }
        public PlayerEventArgs(Player p) { Player = p; }
    }

    /// <summary>Игрок прислал SteamID/ник — «зашёл окончательно».</summary>
    public sealed class JoinedEventArgs : PlayerEventArgs
    {
        public JoinedEventArgs(Player p) : base(p) { }
    }

    public sealed class LeftEventArgs : PlayerEventArgs
    {
        public LeftEventArgs(Player p) : base(p) { }
    }

    public sealed class ChangingRoleEventArgs : PlayerEventArgs
    {
        public int OldRole { get; }
        /// <summary>Можно подменить.</summary>
        public int NewRole { get; set; }
        public bool IsAllowed { get; set; } = true;
        public ChangingRoleEventArgs(Player p, int oldRole, int newRole) : base(p) { OldRole = oldRole; NewRole = newRole; }
    }

    public sealed class ChangedRoleEventArgs : PlayerEventArgs
    {
        public int OldRole { get; }
        public int NewRole { get; }
        public ChangedRoleEventArgs(Player p, int oldRole, int newRole) : base(p) { OldRole = oldRole; NewRole = newRole; }
    }

    public sealed class HurtingEventArgs : PlayerEventArgs
    {
        /// <summary>Кто нанёс урон. Может быть null или равен Player (падение, окружение, сам себя).</summary>
        public Player Attacker { get; }
        /// <summary>Урон. Можно менять.</summary>
        public int Amount { get; set; }
        /// <summary>Причина из игры (например, название оружия/SCP).</summary>
        public string HitInfo { get; }
        public bool IsAllowed { get; set; } = true;
        public HurtingEventArgs(Player target, Player attacker, int amount, string hitInfo) : base(target)
        { Attacker = attacker; Amount = amount; HitInfo = hitInfo; }
    }

    public sealed class DiedEventArgs : PlayerEventArgs
    {
        public Player Attacker { get; }
        public int Damage { get; }
        public string HitInfo { get; }
        public DiedEventArgs(Player target, Player attacker, int damage, string hitInfo) : base(target)
        { Attacker = attacker; Damage = damage; HitInfo = hitInfo; }
    }

    // ---------- Двери ----------

    public sealed class InteractingDoorEventArgs : PlayerEventArgs
    {
        public API.Door Door { get; }
        /// <summary>false — дверь не среагирует.</summary>
        public bool IsAllowed { get; set; } = true;
        public InteractingDoorEventArgs(Player p, API.Door d) : base(p) { Door = d; }
    }

    // ---------- Наручники ----------

    /// <summary>Игрок (Cuffer) надевает наручники на Player.</summary>
    public sealed class CuffingEventArgs : PlayerEventArgs
    {
        public Player Cuffer { get; }
        public bool IsAllowed { get; set; } = true;
        public CuffingEventArgs(Player target, Player cuffer) : base(target) { Cuffer = cuffer; }
    }

    /// <summary>Игрок (Cuffer) снимает наручники с Player.</summary>
    public sealed class UncuffingEventArgs : PlayerEventArgs
    {
        public Player Cuffer { get; }
        public bool IsAllowed { get; set; } = true;
        public UncuffingEventArgs(Player target, Player cuffer) : base(target) { Cuffer = cuffer; }
    }

    /// <summary>Состояние наручников изменилось (любым способом: игрок, плагин, игра).</summary>
    public sealed class CuffChangedEventArgs : PlayerEventArgs
    {
        public bool IsCuffed { get; }
        public CuffChangedEventArgs(Player p, bool cuffed) : base(p) { IsCuffed = cuffed; }
    }

    // ---------- Предметы ----------

    public sealed class PickingUpItemEventArgs : PlayerEventArgs
    {
        /// <summary>Объект предмета на полу.</summary>
        public GameObject Pickup { get; }
        public bool IsAllowed { get; set; } = true;
        public PickingUpItemEventArgs(Player p, GameObject pickup) : base(p) { Pickup = pickup; }
    }

    public sealed class DroppingItemEventArgs : PlayerEventArgs
    {
        public int Slot { get; }
        public int ItemId { get; }
        public bool IsAllowed { get; set; } = true;
        public DroppingItemEventArgs(Player p, int slot, int itemId) : base(p) { Slot = slot; ItemId = itemId; }
    }

    /// <summary>Предмет появился в слоте (подбор, выдача, стартовый инвентарь).</summary>
    public sealed class ItemAddedEventArgs : PlayerEventArgs
    {
        public int Slot { get; }
        public int ItemId { get; }
        public string ItemName => Items.GetName(ItemId);
        /// <summary>Причина: pickup, drop, use, admin, command, plugin, other.</summary>
        public string Reason { get; }
        public ItemAddedEventArgs(Player p, int slot, int itemId, string reason = "other") : base(p) { Slot = slot; ItemId = itemId; Reason = reason ?? "other"; }
    }

    /// <summary>Предмет пропал из слота (выброс, удаление, использование).</summary>
    public sealed class ItemRemovedEventArgs : PlayerEventArgs
    {
        public int Slot { get; }
        public int ItemId { get; }
        public string ItemName => Items.GetName(ItemId);
        /// <summary>Причина: pickup, drop, use, admin, command, plugin, other.</summary>
        public string Reason { get; }
        public ItemRemovedEventArgs(Player p, int slot, int itemId, string reason = "other") : base(p) { Slot = slot; ItemId = itemId; Reason = reason ?? "other"; }
    }

    // ================= Хендлеры =================

    /// <summary>Какой расходник использован.</summary>
    public enum UsableKind { Medkit, Scp420J, Scp458, Scp294, Scp714 }

    public sealed class UsedItemEventArgs : PlayerEventArgs
    {
        public UsableKind Kind { get; }
        public UsedItemEventArgs(Player p, UsableKind kind) : base(p) { Kind = kind; }
    }

    /// <summary>Действие админа из встроенной админ-панели игры (Kick, Ban, ForceClass, Give, Tp, RoundLock...).</summary>
    public sealed class AdminActionEventArgs : PlayerEventArgs
    {
        /// <summary>Кто сделал (= Player).</summary>
        public Player Admin => Player;
        /// <summary>Название действия как в игре: Kick, Ban, ForceClass, Give, SetHP, Tp, RoundLock, AddItem, ClearInv, ForceRoundStart...</summary>
        public string Action { get; }
        /// <summary>Аргумент (причина, ID класса, предмет...). Может быть пустым.</summary>
        public string Argument { get; }
        /// <summary>На кого (может быть null).</summary>
        public Player Target { get; }
        /// <summary>Ответ игры админу («Successfully changed», «Moderator+ only»...). Может быть null.</summary>
        public string Response { get; }
        public AdminActionEventArgs(Player admin, string action, string arg, Player target, string response) : base(admin)
        { Action = action; Argument = arg; Target = target; Response = response; }
    }

    /// <summary>Строка введена в консоль сервера (или пришла с сайта/от плагина).</summary>
    public sealed class CommandExecutedEventArgs
    {
        /// <summary>console, web, plugin...</summary>
        public string Source { get; }
        public string Line { get; }
        /// <summary>true — команда UnicornAPI; false — строка ушла игре.</summary>
        public bool Handled { get; }
        public bool Ok { get; }
        public string Response { get; }
        public CommandExecutedEventArgs(string source, string line, bool handled, bool ok, string response)
        { Source = source; Line = line; Handled = handled; Ok = ok; Response = response; }
    }

    /// <summary>Строка лога сервера (игра + BepInEx/UnicornAPI). Может прийти не с главного потока.</summary>
    public sealed class ConsoleLineEventArgs
    {
        /// <summary>game — сообщения игры (Unity), иначе имя источника BepInEx (UnicornAPI, BepInEx...).</summary>
        public string Source { get; }
        /// <summary>info, warning, error, debug.</summary>
        public string Level { get; }
        public string Text { get; }
        public ConsoleLineEventArgs(string source, string level, string text) { Source = source; Level = level; Text = text; }
    }

    /// <summary>Итог нажатия на дверь (после обработки).</summary>
    public sealed class DoorInteractedEventArgs : PlayerEventArgs
    {
        public API.Door Door { get; }
        /// <summary>opened, closed, denied (отказ по доступу/задержке/блокировке), blocked (отменил плагин).</summary>
        public string Result { get; }
        /// <summary>Предмет в руках в момент нажатия.</summary>
        public int ItemId { get; }
        public DoorInteractedEventArgs(Player p, API.Door d, string result, int itemId) : base(p) { Door = d; Result = result; ItemId = itemId; }
    }

    /// <summary>Античит заметил подозрительное действие (пока только лог, без санкций).</summary>
    public sealed class CheatSuspectedEventArgs : PlayerEventArgs
    {
        /// <summary>Код проверки: steamid_spoof, role_spoof, admin_cmd, add_item, set_hp, ...</summary>
        public string Check { get; }
        public string Details { get; }
        /// <summary>Сколько раз эта проверка сработала на игроке за сессию.</summary>
        public int Count { get; }
        /// <summary>Сервер не выполнил команду (заблокировано), а не только записал подозрение.</summary>
        public bool Blocked { get; }
        public CheatSuspectedEventArgs(Player p, string check, string details, int count, bool blocked = false) : base(p) { Check = check; Details = details; Count = count; Blocked = blocked; }
    }

    /// <summary>
    /// Игрок стреляет (сервер обрабатывает выстрел). Срабатывает только когда выстрел реально состоится: оружие в руках,
    /// в магазине есть патроны, нет перезарядки. IsAllowed = false — выстрела не будет (урона и расхода патрона тоже).
    /// </summary>
    public sealed class ShootingEventArgs : PlayerEventArgs
    {
        /// <summary>Оружие в руках (может быть null, если не нашлось).</summary>
        public PlayerWeapon Weapon { get; }
        public int ItemId { get; }
        public UnityEngine.Vector3 Origin { get; }
        public UnityEngine.Vector3 Direction { get; }
        public bool IsAllowed { get; set; } = true;
        public ShootingEventArgs(Player p, PlayerWeapon w, int itemId, UnityEngine.Vector3 origin, UnityEngine.Vector3 dir) : base(p)
        { Weapon = w; ItemId = itemId; Origin = origin; Direction = dir; }
    }

    /// <summary>Игрок начинает перезарядку (только если она реально начнётся: магазин не полон, есть запас). IsAllowed = false — не будет.</summary>
    public sealed class ReloadingEventArgs : PlayerEventArgs
    {
        public PlayerWeapon Weapon { get; }
        public bool IsAllowed { get; set; } = true;
        public ReloadingEventArgs(Player p, PlayerWeapon w) : base(p) { Weapon = w; }
    }

    /// <summary>Сменился предмет в руках (в том числе оружие). Weapon — новое оружие или null.</summary>
    public sealed class ChangedItemEventArgs : PlayerEventArgs
    {
        public int OldItemId { get; }
        public int NewItemId { get; }
        public PlayerWeapon Weapon { get; }
        public ChangedItemEventArgs(Player p, int oldId, int newId, PlayerWeapon w) : base(p) { OldItemId = oldId; NewItemId = newId; Weapon = w; }
    }

    /// <summary>Режим SCP-914 (значения как в игре).</summary>
    public enum Scp914Mode { OneToOne = 0, Fine = 1, Rough = 2 }

    /// <summary>
    /// SCP-914 перерабатывает предмет (через 14 с после запуска, в момент переработки).
    /// OutputItemId: null — как решит игра; ID предмета — выдать его вместо обычного результата; -1 — предмет просто исчезает.
    /// IsAllowed = false — предмет не трогать (остаётся во входной камере).
    /// </summary>
    public sealed class Scp914UpgradingItemEventArgs
    {
        public UnityEngine.GameObject Pickup { get; }
        public int ItemId { get; }
        public Scp914Mode Mode { get; }
        public int? OutputItemId { get; set; }
        public bool IsAllowed { get; set; } = true;
        public Scp914UpgradingItemEventArgs(UnityEngine.GameObject pickup, int itemId, Scp914Mode mode) { Pickup = pickup; ItemId = itemId; Mode = mode; }
    }

    /// <summary>
    /// SCP-049 ударил игрока, и игра сейчас превратит его в зомби (все проверки игры — дистанция, кольцо SCP-714 — уже прошли).
    /// IsAllowed = false — не превращать (плагин может, например, убить жертву сам).
    /// </summary>
    public sealed class Scp049ConvertingEventArgs : PlayerEventArgs
    {
        /// <summary>SCP-049 (= Player).</summary>
        public Player Scp049 => Player;
        public Player Target { get; }
        public bool IsAllowed { get; set; } = true;
        public Scp049ConvertingEventArgs(Player scp, Player target) : base(scp) { Target = target; }
    }

    public static class PlayerEvents
    {
        public static Event<JoinedEventArgs> Joined { get; set; } = new();
        public static Event<LeftEventArgs> Left { get; set; } = new();
        public static Event<ChangingRoleEventArgs> ChangingRole { get; set; } = new();
        public static Event<ChangedRoleEventArgs> ChangedRole { get; set; } = new();
        public static Event<HurtingEventArgs> Hurting { get; set; } = new();
        public static Event<DiedEventArgs> Died { get; set; } = new();

        public static Event<InteractingDoorEventArgs> InteractingDoor { get; set; } = new();
        public static Event<CuffingEventArgs> Cuffing { get; set; } = new();
        public static Event<UncuffingEventArgs> Uncuffing { get; set; } = new();
        public static Event<CuffChangedEventArgs> CuffChanged { get; set; } = new();
        public static Event<PickingUpItemEventArgs> PickingUpItem { get; set; } = new();
        public static Event<DroppingItemEventArgs> DroppingItem { get; set; } = new();
        public static Event<ItemAddedEventArgs> ItemAdded { get; set; } = new();
        public static Event<ItemRemovedEventArgs> ItemRemoved { get; set; } = new();
        /// <summary>Игрок использовал расходник: аптечку или карманный SCP (сервер уже применил эффект).</summary>
        public static Event<UsedItemEventArgs> UsedItem { get; set; } = new();
        /// <summary>Админ сделал что-то через встроенную админ-панель игры (после выполнения).</summary>
        public static Event<AdminActionEventArgs> AdminAction { get; set; } = new();
        /// <summary>Игрок нажал на дверь — итог.</summary>
        public static Event<DoorInteractedEventArgs> DoorInteracted { get; set; } = new();
        /// <summary>Античит: подозрение на читы.</summary>
        public static Event<CheatSuspectedEventArgs> CheatSuspected { get; set; } = new();
        /// <summary>Выстрел (сервер). Можно отменить.</summary>
        public static Event<ShootingEventArgs> Shooting { get; set; } = new();
        /// <summary>Начало перезарядки. Можно отменить.</summary>
        public static Event<ReloadingEventArgs> Reloading { get; set; } = new();
        /// <summary>Сменился предмет в руках (достал/убрал оружие и т.п.).</summary>
        public static Event<ChangedItemEventArgs> ChangedItem { get; set; } = new();
        /// <summary>SCP-049 превращает игрока в зомби ударом. Можно отменить.</summary>
        public static Event<Scp049ConvertingEventArgs> Scp049Converting { get; set; } = new();
    }

    /// <summary>
    /// Раздача ролей на старте раунда: игра выбрала роль для очередного места команды Team.
    /// RoleId можно заменить на другую роль (например, охранник → Riot Guard). Для команды SCP замена игнорируется
    /// (у игры своя защита от повторов SCP). Игрок здесь неизвестен — игра выбирает роль до того, как знает, кому.
    /// </summary>
    public sealed class StartRoleChoosingEventArgs
    {
        public API.Team Team { get; }
        public int OriginalRoleId { get; }
        public int RoleId { get; set; }
        public StartRoleChoosingEventArgs(API.Team team, int roleId) { Team = team; OriginalRoleId = roleId; RoleId = roleId; }
    }

    public static class ServerEvents
    {
        /// <summary>Лобби: "Waiting for round start".</summary>
        public static Event WaitingForPlayers { get; set; } = new();
        public static Event RoundStarted { get; set; } = new();
        public static Event RoundEnded { get; set; } = new();
        /// <summary>Каждый кадр сервера. Держи обработчик лёгким — для редких проверок лучше Timing.CallRepeating.</summary>
        public static Event Update { get; set; } = new();
        /// <summary>Строка из консоли сервера / с сайта (после выполнения).</summary>
        public static Event<CommandExecutedEventArgs> CommandExecuted { get; set; } = new();
        /// <summary>Строка лога. ВНИМАНИЕ: может вызываться с любого потока — только складывай в очередь.</summary>
        public static Event<ConsoleLineEventArgs> ConsoleLine { get; set; } = new();
        /// <summary>SCP-914 перерабатывает предмет — можно подменить результат (кастомные рецепты).</summary>
        public static Event<Scp914UpgradingItemEventArgs> Scp914UpgradingItem { get; set; } = new();
        /// <summary>Выбор роли при раздаче на старте раунда (можно подменить).</summary>
        public static Event<StartRoleChoosingEventArgs> StartRoleChoosing { get; set; } = new();
    }
}
