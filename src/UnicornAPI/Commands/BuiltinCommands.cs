using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using Unicorn.API;
using Unicorn.Internal;

namespace Unicorn.Commands
{
    /// <summary>Простая команда из лямбды.</summary>
    public sealed class SimpleCommand : ICommand
    {
        public delegate bool Handler(string[] args, out string response);
        private readonly Handler _h;
        public string Name { get; }
        public string[] Aliases { get; }
        public string Description { get; }
        public SimpleCommand(string name, string description, Handler h, params string[] aliases)
        { Name = name; Description = description; _h = h; Aliases = aliases; }
        public bool Execute(string[] args, out string response) => _h(args, out response);
        internal Delegate HandlerDelegate => _h;
    }

    internal static class BuiltinCommands
    {
        /// <summary>Игрок онлайн (ник/SteamID) или SteamID64 офлайн.</summary>
        private static bool Target(string q, out ulong id, out string name, out string r)
        {
            r = null; id = 0; name = q;
            var p = Player.Find(q);
            if (p != null) { id = p.SteamId; name = p.Nickname; return true; }
            if (ulong.TryParse(q, out id) && id > 76561197960265728UL) { name = $"{id} (офлайн)"; return true; }
            r = $"игрок '{q}' не найден (для офлайн укажи SteamID64)"; return false;
        }

        private static readonly Dictionary<ulong, string> _lookNickOrig = new();

        private static bool DbOk(out string r)
        {
            r = Database.IsAvailable ? null : "БД недоступна (см. лог [SQLite])";
            return r == null;
        }

        private static bool Need(string[] a, int count, out Player p, out string r)
        {
            p = null; r = null;
            if (a.Length < count) { r = "мало аргументов"; return false; }
            p = Player.Find(a[0]);
            if (p == null) { r = $"игрок '{a[0]}' не найден"; return false; }
            return true;
        }

        public static void Register()
        {
            CommandManager.Register(new SimpleCommand("sun", "версия UnicornAPI и список плагинов", (string[] a, out string r) =>
            {
                var sb = new StringBuilder($"UnicornAPI v{Loader.VersionString}, плагинов: {Loader.Plugins.Count}");
                foreach (var p in Loader.Plugins) sb.Append($"\n  - {p.Name} v{p.Version} ({p.Author})");
                r = sb.ToString(); return true;
            }, "unicorn", "p1sun"));

            CommandManager.Register(new SimpleCommand("grenades", "grenades [clean] — сколько гранат-снарядов на карте / удалить все", (string[] a, out string r) =>
            {
                if (a.Length > 0 && a[0].Equals("clean", StringComparison.OrdinalIgnoreCase))
                { r = $"удалено гранат: {Internal.GrenadeCleanupFix.RemoveAll()}"; return true; }
                int n = 0; try { n = UnityEngine.Object.FindObjectsOfType<global::Grenade>().Length; } catch { }
                int mines = 0; try { mines = UnityEngine.Object.FindObjectsOfType<global::Landmine>().Length; } catch { }
                r = $"гранат-снарядов на карте: {n}; удалено после взрыва с запуска: {Internal.GrenadeCleanupFix.Removed}\n" +
                    $"мин на карте: {mines}; взорвавшихся мин удалено: {Internal.LandmineExplodeFix.Removed}; лишних команд мин отброшено: {Internal.LandmineCommandFix.Blocked}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("players", "игроки: ник, SteamID, роль, HP", (string[] a, out string r) =>
            {
                var list = Player.List;
                var sb = new StringBuilder($"Игроков: {list.Count}");
                foreach (var p in list) sb.Append($"\n  {p.Nickname} | {p.SteamId} | {p.Role.Name} | {p.Health}/{p.MaxHealth}{(p.IsDead ? " | dead" : "")}");
                r = sb.ToString(); return true;
            }, "pl"));

            CommandManager.Register(new SimpleCommand("roles", "список ролей с ID", (string[] a, out string r) =>
            {
                r = string.Join("\n", Server.Roles.Select(x => $"  {x.Id,2}: {x.Name} [{x.Team}] HP {x.MaxHealth}"));
                if (r.Length == 0) r = "роли недоступны (карта ещё не загружена?)";
                return true;
            }));

            CommandManager.Register(new SimpleCommand("bc", "bc [сек] <текст> — броадкаст всем (по умолчанию 7 с; 0 = навсегда)", (string[] a, out string r) =>
            {
                if (a.Length == 0) { r = "bc [сек] <текст>"; return false; }
                float dur = Server.DefaultBroadcastDuration;
                if (a.Length > 1 && float.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) { dur = d; a = a.Skip(1).ToArray(); }
                Server.Broadcast(string.Join(" ", a), dur); r = $"отправлено на {(dur > 0 ? dur + " с" : "всегда")}"; return true;
            }, "broadcast"));

            CommandManager.Register(new SimpleCommand("clearbc", "убрать броадкаст с экрана", (string[] a, out string r) =>
            {
                Server.ClearBroadcast(); r = "очищено"; return true;
            }, "cbc"));

            CommandManager.Register(new SimpleCommand("giveroles", "раздать роли всем заново (как на старте раунда)", (string[] a, out string r) =>
            {
                r = Internal.RoundStartFix.Run(force: true) ? "роли розданы" : "нет игроков"; return true;
            }));

            CommandManager.Register(new SimpleCommand("setrole", "setrole <игрок> <roleId|название> [notp] — название: 173, ntf, chaos, d, guard, sci, spec...", (string[] a, out string r) =>
            {
                if (a.Length < 2) { r = "setrole <SteamID|ник> <roleId|название> [notp]"; return false; }
                var role = Server.FindRole(a[1]);
                if (role == null) { r = $"роль '{a[1]}' не найдена или подходит несколько (список: roles)"; return false; }
                int id = role.Value.Id;
                var p = Player.Find(a[0]); if (p == null) { r = "игрок не найден"; return false; }
                p.SetRole(id, !(a.Length > 2 && a[2].Equals("notp", StringComparison.OrdinalIgnoreCase)));
                r = $"{p.Nickname} -> {Server.GetRole(id).Name}"; return true;
            }, "fc"));

            CommandManager.Register(new SimpleCommand("hp", "hp <игрок> <значение>", (string[] a, out string r) =>
            {
                if (a.Length < 2 || !int.TryParse(a[1], out var hp)) { r = "hp <SteamID|ник> <значение>"; return false; }
                var p = Player.Find(a[0]); if (p == null) { r = "игрок не найден"; return false; }
                p.Health = hp; r = $"{p.Nickname}: HP = {hp}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("kick", "kick <игрок> [причина]", (string[] a, out string r) =>
            {
                if (a.Length < 1) { r = "kick <SteamID|ник> [причина]"; return false; }
                var p = Player.Find(a[0]); if (p == null) { r = "игрок не найден"; return false; }
                var reason = a.Length > 1 ? string.Join(" ", a.Skip(1)) : "Kicked";
                p.Kick(reason); r = $"{p.Nickname} кикнут: {reason}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("reload", "горячая перезагрузка плагинов из UnicornAPI/Plugins (без рестарта)", (string[] a, out string r) =>
            {
                r = Loader.ReloadPlugins(); return true;
            }));

            // ---------- Игрок ----------
            CommandManager.Register(new SimpleCommand("heal", "heal <игрок> [кол-во] — вылечить (без числа — полностью)", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                if (a.Length > 1 && int.TryParse(a[1], out var n)) p.Heal(n); else p.Health = p.MaxHealth > 0 ? p.MaxHealth : p.Health;
                r = $"{p.Nickname}: HP {p.Health}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("damage", "damage <игрок> <урон> [причина]", (string[] a, out string r) =>
            {
                if (!Need(a, 2, out var p, out r) || !int.TryParse(a[1], out var n)) { r ??= "damage <игрок> <урон> [причина]"; return false; }
                p.Damage(n, a.Length > 2 ? string.Join(" ", a.Skip(2)) : "Admin");
                r = $"{p.Nickname}: -{n}, HP {p.Health}"; return true;
            }, "dmg"));

            CommandManager.Register(new SimpleCommand("kill", "kill <игрок> [причина]", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                p.Kill(a.Length > 1 ? string.Join(" ", a.Skip(1)) : "Admin"); r = $"{p.Nickname} убит"; return true;
            }));

            // ---------- Оформление (сохраняется в БД, переживает перезапуск) ----------
            CommandManager.Register(new SimpleCommand("badge", "badge <игрок|SteamID> <HEX> <текст> | off — бейдж после ника в Tab (сохраняется)", (string[] a, out string r) =>
            {
                r = null; if (a.Length < 2 || !Target(a[0], out var id, out var name, out r)) { r ??= "badge <игрок|SteamID> <HEX> <текст> | off"; return false; }
                if (!DbOk(out r)) return false;
                if (a[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    Profiles.Update(id, p => { p.Badge = null; p.BadgeColor = null; });
                    r = $"{name}: бейдж снят"; return true;
                }
                if (a.Length < 3) { r = "badge <игрок|SteamID> <HEX> <текст>"; return false; }
                var color = a[1].TrimStart('#'); var text = string.Join(" ", a.Skip(2));
                Profiles.Update(id, p => { p.Badge = text; p.BadgeColor = color; });
                r = $"{name}: бейдж '{text}' #{color} (сохранён)"; return true;
            }));

            CommandManager.Register(new SimpleCommand("nick", "nick <игрок|SteamID> <ник> | reset — показываемый ник (сохраняется)", (string[] a, out string r) =>
            {
                r = null; if (a.Length < 2 || !Target(a[0], out var id, out var name, out r)) { r ??= "nick <игрок|SteamID> <ник|reset>"; return false; }
                if (!DbOk(out r)) return false;
                bool reset = a[1].Equals("reset", StringComparison.OrdinalIgnoreCase);
                var nick = reset ? null : string.Join(" ", a.Skip(1));
                Profiles.Update(id, p => p.Nick = nick);
                r = reset ? $"{name}: ник из Steam" : $"{name}: показывается как '{nick}' (сохранён)"; return true;
            }));

            CommandManager.Register(new SimpleCommand("admintools", "admintools <игрок|SteamID> <on|off|reset> — встроенная админ-панель игры (сохраняется)", (string[] a, out string r) =>
            {
                r = null; if (a.Length < 2 || !Target(a[0], out var id, out var name, out r)) { r ??= "admintools <игрок|SteamID> <on|off|reset>"; return false; }
                if (!DbOk(out r)) return false;
                var mode = a[1].ToLowerInvariant();
                bool? v = mode == "on" ? true : mode == "off" ? false : (bool?)null;
                Profiles.Update(id, p => p.AdminTools = v);
                if (v == null) { var pl = Player.Get(id); if (pl != null) pl.WantAdminTools = null; }
                r = $"{name}: админ-панель {(v == null ? "как решит игра" : v.Value ? "включена" : "выключена")}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("profile", "profile <игрок|SteamID> — сохранённое оформление", (string[] a, out string r) =>
            {
                r = null; if (a.Length < 1 || !Target(a[0], out var id, out var name, out r)) { r ??= "profile <игрок|SteamID>"; return false; }
                if (!DbOk(out r)) return false;
                var p = Profiles.Get(id);
                r = p == null ? $"{name}: профиля нет" : $"{name}: {p}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("profiles", "все сохранённые профили", (string[] a, out string r) =>
            {
                if (!DbOk(out r)) return false;
                var all = Profiles.All();
                r = $"Профилей: {all.Count}" + string.Concat(all.Select(p => $"\n  {p}")); return true;
            }));

            CommandManager.Register(new SimpleCommand("tp", "tp <игрок> <x> <y> <z> | tp <игрок> <к_игроку>", (string[] a, out string r) =>
            {
                if (!Need(a, 2, out var p, out r)) { r ??= "tp <игрок> <x y z | игрок>"; return false; }
                if (a.Length >= 4 && float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && float.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                { p.Teleport(new UnityEngine.Vector3(x, y, z)); r = $"{p.Nickname} -> {x} {y} {z}"; return true; }
                var to = Player.Find(a[1]); if (to == null) { r = "цель не найдена"; return false; }
                p.Teleport(to.Position); r = $"{p.Nickname} -> {to.Nickname}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("lookinfo", "lookinfo <игрок> <nick|rp> <текст|reset> — ТЕСТ: текст при наведении прицела на игрока (не сохраняется)", (string[] a, out string r) =>
            {
                if (!Need(a, 3, out var p, out r)) { r ??= "lookinfo <игрок> <nick|rp> <текст|reset>"; return false; }
                var what = a[1].ToLowerInvariant();
                bool reset = a[2].Equals("reset", StringComparison.OrdinalIgnoreCase);
                var text = string.Join(" ", a.Skip(2));
                if (what == "nick")
                {
                    if (!_lookNickOrig.ContainsKey(p.SteamId)) _lookNickOrig[p.SteamId] = p.Stats.mySteamNick;
                    var v = reset ? _lookNickOrig[p.SteamId] : text;
                    p.Stats.SetSteamNick(v);
                    r = $"{p.Nickname}: строка ника при наведении = '{v}'"; return true;
                }
                if (what == "rp")
                {
                    p.RolePlayDetails = reset ? "" : text;
                    r = $"{p.Nickname}: RP-строка = '{p.RolePlayDetails}' (видно при наведении и в Tab)"; return true;
                }
                r = "второй аргумент: nick или rp"; return false;
            }));

            CommandManager.Register(new SimpleCommand("msg", "msg <игрок|all> <текст> — личное сообщение на экране игрока (~5 с)", (string[] a, out string r) =>
            {
                r = null;
                if (a.Length < 2) { r = "msg <игрок|all> <текст>"; return false; }
                var text = string.Join(" ", a.Skip(1));
                if (a[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    int n = 0; foreach (var pl in Player.List) if (pl.ShowMessage(text)) n++;
                    r = $"отправлено {n} игрокам"; return true;
                }
                if (!Need(a, 2, out var p, out r)) return false;
                bool ok = p.ShowMessage(text);
                r = ok ? $"{p.Nickname}: отправлено" : $"{p.Nickname}: нет компонента Scp294Cup"; return ok;
            }));

            CommandManager.Register(new SimpleCommand("anticheat", "anticheat — статус античита P90Guard (отдельный плагин)", (string[] a, out string r) =>
            {
                r = Internal.GuardBridge.Status(); return true;
            }, "ac"));

            CommandManager.Register(new SimpleCommand("weapons", "weapons — все виды оружия: ID, урон, магазин, патроны", (string[] a, out string r) =>
            {
                var all = Weapons.All;
                if (all.Count == 0) { r = "оружие не найдено (нужен хотя бы один игрок на сервере)"; return false; }
                r = "Оружие:\n" + string.Join("\n", all.Select(w => $"  #{w.ItemId,-3} {w.Name,-22} урон {w.Damage,-4} магазин {w.Magazine,-4} патроны {w.AmmoType ?? "-"}"));
                return true;
            }));

            CommandManager.Register(new SimpleCommand("weapon", "weapon <игрок> — оружие в руках и характеристики", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) { r ??= "weapon <игрок>"; return false; }
                var w = p.CurrentWeapon;
                r = w == null ? $"{p.Nickname}: в руках не оружие ({p.Inventory.CurrentItemName})" : $"{p.Nickname}: {w}";
                return true;
            }));

            CommandManager.Register(new SimpleCommand("wset", "wset <оружие> <damage|mag> <значение> — всем игрокам до рестарта (надолго — плагин/конфиг)", (string[] a, out string r) =>
            {
                if (a.Length < 3 || !int.TryParse(a[2], out var v)) { r = "wset <оружие|ID> <damage|mag> <значение>"; return false; }
                int id = Weapons.FindItemId(a[0]);
                if (id < 0 || !Weapons.All.Any(x => x.ItemId == id)) { r = $"оружие '{a[0]}' не найдено (список: weapons)"; return false; }
                var what = a[1].ToLowerInvariant();
                if (what == "damage") Weapons.Configure(id, w => w.Damage = v);
                else if (what == "mag") Weapons.Configure(id, w => w.MagazineSize = v);
                else { r = "второй аргумент: damage или mag"; return false; }
                r = $"{Items.GetName(id)}: {what} = {v} (у всех игроков)"; return true;
            }));

            CommandManager.Register(new SimpleCommand("ammo", "ammo <игрок> [<тип> <set|add|take> <кол-во>] — патроны", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) { r ??= "ammo <игрок> [<тип> <set|add|take> <кол-во>]"; return false; }
                if (a.Length < 4)
                {
                    var all = p.Ammo.All();
                    r = $"{p.Nickname}: " + (all.Count == 0 ? "патронов нет" : string.Join(", ", all.Select(kv => $"{kv.Key} {kv.Value}")))
                        + $"\nтипы: {string.Join(", ", Weapons.AmmoTypes)}";
                    return true;
                }
                if (!int.TryParse(a[3], out var n)) { r = "кол-во — число"; return false; }
                bool ok = a[2].ToLowerInvariant() switch
                {
                    "set" => p.Ammo.Set(a[1], n),
                    "add" => p.Ammo.Add(a[1], n),
                    "take" => p.Ammo.Take(a[1], n),
                    _ => false,
                };
                r = ok ? $"{p.Nickname}: {a[1]} = {p.Ammo.Get(a[1])}" : $"не вышло (тип патронов: {string.Join(", ", Weapons.AmmoTypes)})";
                return ok;
            }));

            CommandManager.Register(new SimpleCommand("pos", "pos <игрок> — координаты на сервере и последние от клиента", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                string F(UnityEngine.Vector3 v) => $"{v.x.ToString("0.0", CultureInfo.InvariantCulture)} {v.y.ToString("0.0", CultureInfo.InvariantCulture)} {v.z.ToString("0.0", CultureInfo.InvariantCulture)}";
                r = $"{p.Nickname}: сервер {F(p.Position)}";
                if (p.TransformSync != null && Patches.PositionSyncPatch.Last.TryGetValue(p.TransformSync.Pointer, out var last))
                    r += $", клиент прислал {F(last.Pos)} ({UnityEngine.Time.realtimeSinceStartup - last.Time:0.0} с назад)";
                else r += ", от клиента позиция ещё не приходила";
                return true;
            }));

            CommandManager.Register(new SimpleCommand("lobby", "lobby [create|name <название>|name reset] — сервер в игровом списке серверов (create — новое лобби без рестарта)", (string[] a, out string r) =>
            {
                var sub = a.FirstOrDefault()?.ToLowerInvariant();
                if (sub == "create") { r = Internal.LobbyAdvertiser.Recreate(); return true; }   // без остановки сервера
                if (sub == "name")
                {
                    var name = string.Join(" ", a.Skip(1));
                    if (name.Equals("reset", StringComparison.OrdinalIgnoreCase)) name = "";
                    Internal.CoreConfig.Current.ServerListName = name; Internal.CoreConfig.Save();
                    bool ok = Internal.LobbyAdvertiser.ApplyName();
                    r = name.Length == 0 ? "название как в YourServerConfig.json (со следующего создания лобби)" : $"название: {name}{(ok ? " (применено)" : " (применится при создании лобби)")}";
                    return true;
                }
                var id = Internal.LobbyAdvertiser.LobbyId;
                if (id == 0) { r = "лобби нет — сервера нет в игровом списке. lobby create — создать"; return true; }
                int n = Internal.LobbyAdvertiser.Members(out var lim);
                r = $"лобби {id}, в лобби {n}/{lim}{(n == 0 ? " — сервер НЕ в лобби (выпал), lobby create — пересоздать" : "")}, название: {Internal.LobbyAdvertiser.CurrentName()}, пересоздано сторожем: {Internal.LobbyAdvertiser.Recreated}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("confilter", "перечитать UnicornAPI/console_filter.txt (фильтр мусора в консоли)", (string[] a, out string r) =>
            {
                int n = Patches.ConsoleFilter.Load();
                r = $"фильтров: {n}, скрыто сообщений с запуска: {Patches.ConsoleFilter.Hidden}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("netstat", "диагностика: пакеты от клиентов по каналам", (string[] a, out string r) =>
            {
                r = $"сообщений от клиентов: reliable ok={Patches.NetDiagInvokePatch.Ok0} fail={Patches.NetDiagInvokePatch.Fail0}; unreliable ok={Patches.NetDiagInvokePatch.Ok1} fail={Patches.NetDiagInvokePatch.Fail1}\n" +
                    $"  CmdSyncData (позиция) получено: {Patches.PositionSyncPatch.Calls}";
                return true;
            }));

            // ---------- Наручники ----------
            CommandManager.Register(new SimpleCommand("cuff", "cuff <игрок> [кто_ведёт]", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                var by = a.Length > 1 ? Player.Find(a[1]) : null;
                p.Cuff(by); r = $"{p.Nickname} в наручниках{(by != null ? $" (ведёт {by.Nickname})" : "")}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("uncuff", "uncuff <игрок>", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                p.Uncuff(); r = $"{p.Nickname} без наручников"; return true;
            }));

            // ---------- Предметы ----------
            CommandManager.Register(new SimpleCommand("items", "список предметов с ID", (string[] a, out string r) =>
            {
                var all = Items.All;
                r = all.Count == 0 ? "предметы недоступны (карта не загружена?)" : string.Join("\n", all.Select(x => $"  {x.Id,2}: {x.Name}"));
                return true;
            }));

            CommandManager.Register(new SimpleCommand("give", "give <игрок> <ID|название>", (string[] a, out string r) =>
            {
                if (!Need(a, 2, out var p, out r)) { r ??= "give <игрок> <ID|название>"; return false; }
                int id = Items.Find(string.Join(" ", a.Skip(1)));
                if (id < 0) { r = "предмет не найден (см. items)"; return false; }
                if (p.Inventory.IsFull) { r = $"{p.Nickname}: инвентарь полон"; return false; }
                p.Inventory.Add(id); r = $"{p.Nickname} получил {Items.GetName(id)}"; return true;
            }));

            CommandManager.Register(new SimpleCommand("inv", "inv <игрок> — инвентарь", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                var inv = p.Inventory;
                var sb = new StringBuilder($"{p.Nickname}: в руках {inv.CurrentItemName} (слот {inv.CurrentSlot}){(p.IsCuffed ? ", в наручниках" : "")}");
                foreach (var (slot, id) in inv.Items) sb.Append($"\n  [{slot}] {id}: {Items.GetName(id)}");
                r = sb.ToString(); return true;
            }));

            CommandManager.Register(new SimpleCommand("removeitem", "removeitem <игрок> <слот>", (string[] a, out string r) =>
            {
                if (!Need(a, 2, out var p, out r) || !int.TryParse(a[1], out var slot)) { r ??= "removeitem <игрок> <слот>"; return false; }
                p.Inventory.RemoveSlot(slot); r = $"{p.Nickname}: слот {slot} очищен"; return true;
            }));

            CommandManager.Register(new SimpleCommand("clearinv", "clearinv <игрок>", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                p.Inventory.Clear(); r = $"{p.Nickname}: инвентарь очищен"; return true;
            }));

            CommandManager.Register(new SimpleCommand("dropall", "dropall <игрок> — выбросить всё", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                p.Inventory.DropAll(); r = $"{p.Nickname}: всё выброшено"; return true;
            }));

            // ---------- Двери ----------
            CommandManager.Register(new SimpleCommand("doors", "doors [фильтр] — список дверей", (string[] a, out string r) =>
            {
                var list = a.Length > 0 ? API.Door.Find(a[0]).ToList() : API.Door.List.ToList();
                r = $"Дверей: {list.Count}\n" + string.Join("\n", list.Take(80).Select(d => $"  {d}"));
                if (list.Count > 80) r += $"\n  ... и ещё {list.Count - 80} (сузь фильтром)";
                return true;
            }));

            CommandManager.Register(new SimpleCommand("doorinfo", "doorinfo <игрок> — диагностика ближайшей к игроку двери", (string[] a, out string r) =>
            {
                if (!Need(a, 1, out var p, out r)) return false;
                var d = API.Door.Nearest(p.Position);
                r = d == null ? "дверей нет" : d.Diagnose(p); return d != null;
            }));

            CommandManager.Register(new SimpleCommand("door", "door <open|close|lock|unlock|destroy> <имя|all|near:игрок>", (string[] a, out string r) =>
            {
                if (a.Length < 2) { r = "door <open|close|lock|unlock|destroy> <имя|all|near:игрок>"; return false; }
                IEnumerable<API.Door> targets;
                if (a[1].Equals("all", StringComparison.OrdinalIgnoreCase)) targets = API.Door.List;
                else if (a[1].StartsWith("near:", StringComparison.OrdinalIgnoreCase))
                {
                    var p = Player.Find(a[1].Substring(5)); if (p == null) { r = "игрок не найден"; return false; }
                    var d = API.Door.Nearest(p.Position, 10f); targets = d != null ? new[] { d } : Array.Empty<API.Door>();
                }
                else targets = API.Door.Find(string.Join(" ", a.Skip(1)));
                int n = 0;
                foreach (var d in targets.ToList())
                {
                    switch (a[0].ToLowerInvariant())
                    {
                        case "open": d.Open(); break;
                        case "close": d.Close(); break;
                        case "lock": d.Lock(); break;
                        case "unlock": d.Unlock(); break;
                        case "destroy": d.Destroy(); break;
                        default: r = "действие: open/close/lock/unlock/destroy"; return false;
                    }
                    n++;
                }
                r = $"{a[0]}: дверей {n}"; return n > 0;
            }));

            CommandManager.Register(new SimpleCommand("id", "ID для подключения к серверу", (string[] a, out string r) =>
            {
                var id = Internal.SteamNative.UserSteamId();
                r = id != 0 ? $"ID для подключения: {id}" : "не определён (Steam не залогинен?)"; return id != 0;
            }, "connect"));

            CommandManager.Register(new SimpleCommand("steam", "steam [relogin] — статус Steam GameServer", (string[] a, out string r) =>
            {
                if (a.Length > 0 && a[0] == "relogin") { Internal.Game.SteamGS_LogOnAnonymous(); r = "LogOnAnonymous() отправлен, жди Steam GameServer connected"; return true; }
                r = $"LoggedOn={Internal.Game.SteamGS_LoggedOn()} Secure={Internal.Game.SteamGS_Secure()} SteamID={Internal.Game.SteamGS_SteamId()}";
                return true;
            }));

            CommandManager.Register(new SimpleCommand("round", "round <start|restart|end|lock|unlock|info>", (string[] a, out string r) =>
            {
                switch (a.FirstOrDefault()?.ToLowerInvariant())
                {
                    case "start": Round.ForceStart(); r = "force start"; return true;
                    case "restart": Round.Restart(); r = "restart"; return true;
                    case "end": Round.End(); r = "end"; return true;
                    case "lock": Round.IsLocked = true; r = "round lock ON"; return true;
                    case "unlock": Round.IsLocked = false; r = "round lock OFF"; return true;
                    default: r = $"state='{Round.State}' time={Round.ElapsedTime:0}s lock={Round.IsLocked}"; return true;
                }
            }));
        }
    }
}
