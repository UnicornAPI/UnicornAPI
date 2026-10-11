using System;
using System.Collections.Generic;
using System.Linq;
using Unicorn.API;
using Unicorn.Events;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Античит. Игра доверяет клиенту ряд команд (выдать предмет, HP, патроны, позицию при появлении...).
    /// Обычный клиент шлёт их только при смене своего класса (хук CurClassHook → bmg и его корутины), только себе,
    /// только предметы стартового набора. Всё остальное — подозрение (лог красным); проверки из BlockChecks ещё и
    /// блокируются (сервер не выполняет команду).
    /// Не срабатывает: на админах, на командах, которые отправил сам сервер (CmdOnServerFix), и на игровых объектах
    /// вроде SCP-1162 / коробки пиццы — они выдают предметы на сервере, а не через эти команды.
    /// </summary>
    internal static class AntiCheat
    {
        /// <summary>&gt;0 — сейчас выполняется команда, отправленная самим сервером.</summary>
        internal static int ServerOrigin;

        private static AntiCheatConfig Cfg => CoreConfig.Current.AntiCheat ?? new AntiCheatConfig();

        /// <summary>
        /// Состояние игрока для «стартовых» команд. При каждой смене класса выдаются РАЗРЕШЕНИЯ ровно под то, что шлёт
        /// настоящий клиент (разобрано по коду bmg/GiveStartItems/DelayedSetPos):
        ///   наблюдатель: CmdSp 1, CmdSetHP 1 (=макс. HP класса), CmdClearInv 1, CmdSetDead 1 (true), CmdSpD 1;
        ///   любой другой класс: CmdSp 1, CmdSetHP 1, CmdClearInv 2, CmdSetDead 1 (false), CmdSetAmmo 2 (аргумент = id класса),
        ///   CmdAddItem — по одному на предмет набора.
        /// Смены класса чаще MergeSpawnsSeconds складываются (корутина прошлого класса может ещё досылать своё).
        /// Разрешения живут TokenLifetimeSeconds — хватит медленному клиенту, но «запас» на потом не копится.
        /// </summary>
        private sealed class SpawnState
        {
            public Player Owner;
            public float FirstSeen = -1f, SpawnAt = -1f;
            public int LastRole = int.MinValue;
            public readonly Dictionary<string, int> Tokens = new();
            public readonly Dictionary<int, int> Kit = new(), Given = new();
            public readonly HashSet<int> Roles = new(), Hp = new();
            public bool KitKnown;
            public int Merges;
        }

        private static readonly Dictionary<ulong, SpawnState> States = new();
        private const float MergeSpawnsSeconds = 60f;
        private const float TokenLifetimeSeconds = 120f;
        private const int MaxMerges = 10;              // складываем не больше стольких смен класса подряд (менять класс может только сервер)
        private const int SpectatorRoleId = 1;         // «Spectator Class»

        private static readonly Dictionary<(ulong, string), (float Last, int Count)> Hits = new();
        private static readonly Dictionary<ulong, float> MedkitGoneAt = new(), LastHeal = new();
        private static readonly Dictionary<(ulong, int), float> ItemGoneAt = new();   // когда предмет пропал из инвентаря
        internal static readonly List<string> Recent = new();

        internal static void Init()
        {
            PlayerEvents.ChangedRole += e => { try { OnSpawn(e.Player); } catch { } };
            PlayerEvents.Joined += e => { try { OnJoined(e.Player); } catch { } };
            PlayerEvents.Left += e => { try { Forget(e.Player); } catch { } };
            PlayerEvents.ItemRemoved += e =>
            {
                try { ItemGoneAt[(Key(e.Player), e.ItemId)] = Time.realtimeSinceStartup; } catch { }
                try
                {
                    var m = e.Player.GameObject.GetComponentInChildren<Medkit>(true);
                    if (m != null && m.medkitID == e.ItemId) MedkitGoneAt[Key(e.Player)] = Time.realtimeSinceStartup;
                }
                catch { }
            };
        }

        private static float _nextPoll;

        /// <summary>
        /// Страховка: смена класса, которую не поймал патч, — ловим опросом раз в 0.25 с. Заодно чистим состояние
        /// ушедших игроков (уход до подтверждения SteamID событие Left не даёт).
        /// </summary>
        internal static void Tick()
        {
            if (!Cfg.Enabled) return;
            float t = Time.realtimeSinceStartup;
            if (t < _nextPoll) return;
            _nextPoll = t + 0.25f;
            foreach (var p in Player.List)
            {
                try
                {
                    if (!p.IsValid) continue;
                    var st = Get(p);
                    if (st.LastRole != p.RoleId) OnSpawn(p);
                }
                catch { }
            }
            if (States.Count > 0 && (int)t % 30 == 0)
            {
                try
                {
                    foreach (var k in States.Where(kv => kv.Value.Owner == null || !kv.Value.Owner.IsValid).Select(kv => kv.Key).ToList())
                        States.Remove(k);
                }
                catch { }
            }
        }

        private static ulong PtrKey(Player p) => (ulong)p.GameObject.Pointer.ToInt64();
        private static ulong Key(Player p) => p.SteamId != 0 ? p.SteamId : PtrKey(p);

        /// <summary>
        /// Состояние игрока — по объекту игрока (адрес GameObject), а не по SteamID: не зависит от того, пришёл ли SteamID,
        /// и перезаход (старое соединение ещё висит) не смешивает два объекта. Если по адресу лежит состояние другого,
        /// уже ушедшего объекта (адрес переиспользован) — начинаем заново.
        /// </summary>
        private static SpawnState Get(Player p)
        {
            var k = PtrKey(p);
            if (!States.TryGetValue(k, out var st) || st.Owner != p)
                States[k] = st = new SpawnState { Owner = p, FirstSeen = Time.realtimeSinceStartup };
            return st;
        }

        private static void OnJoined(Player p) { if (p != null) Get(p); }

        private static void Forget(Player p)
        {
            try { var pk = PtrKey(p); if (States.TryGetValue(pk, out var st) && st.Owner == p) States.Remove(pk); } catch { }
            var k = Key(p);
            if (Player.List.Any(x => x != p && x.IsValid && Key(x) == k)) return;
            MedkitGoneAt.Remove(k); LastHeal.Remove(k);
            foreach (var key in ItemGoneAt.Keys.Where(x => x.Item1 == k).ToList()) ItemGoneAt.Remove(key);
        }

        internal static bool Active => Cfg.Enabled && ServerOrigin == 0 && Application.isBatchMode;

        // ---------------- Смена класса ----------------

        private static void Add(Dictionary<string, int> d, string k, int n) => d[k] = (d.TryGetValue(k, out var v) ? v : 0) + n;

        internal static void OnSpawn(Player p)
        {
            if (p == null || !p.IsValid) return;
            var st = Get(p);
            float now = Time.realtimeSinceStartup;
            int role = p.RoleId;
            st.LastRole = role;
            bool merge = st.SpawnAt >= 0f && now - st.SpawnAt <= MergeSpawnsSeconds && st.Merges < MaxMerges;
            if (!merge)
            {
                st.Tokens.Clear(); st.Kit.Clear(); st.Given.Clear(); st.Roles.Clear(); st.Hp.Clear();
                st.KitKnown = false; st.Merges = 0;
            }
            else st.Merges++;
            st.SpawnAt = now;
            st.Roles.Add(role);

            bool spectator = role == SpectatorRoleId;
            Add(st.Tokens, "sp", 1);
            Add(st.Tokens, "hp", 1);
            Add(st.Tokens, "dead", 1);
            Add(st.Tokens, "clear", spectator ? 1 : 2);
            if (spectator) Add(st.Tokens, "spd", 1);
            else Add(st.Tokens, "ammo", 2);

            try
            {
                var ccm = UnityEngine.Object.FindObjectOfType<CharacterClassManager>();
                if (ccm?.roles != null && role >= 0 && role < ccm.roles.Length && ccm.roles[role] != null)
                {
                    var c = ccm.roles[role];
                    st.Hp.Add(c.maxHP);
                    if (c.startItems != null) foreach (var it in c.startItems) st.Kit[it] = (st.Kit.TryGetValue(it, out var n) ? n : 0) + 1;
                    st.KitKnown = true;
                }
            }
            catch { }
        }

        private static bool Fresh(SpawnState st) => st.SpawnAt >= 0f && Time.realtimeSinceStartup - st.SpawnAt <= TokenLifetimeSeconds;

        /// <summary>Потратить разрешение (hp, dead, sp, spd, clear, ammo). false — разрешений нет.</summary>
        internal static bool TakeToken(Player p, string name)
        {
            var st = Get(p);
            if (!Fresh(st) || !st.Tokens.TryGetValue(name, out var n) || n <= 0) return false;
            st.Tokens[name] = n - 1;
            return true;
        }

        /// <summary>HP, который клиент ставит при смене класса, = максимум HP одного из классов этой смены.</summary>
        internal static bool IsSpawnHp(Player p, int hp) { var st = Get(p); return Fresh(st) && st.Hp.Contains(hp); }

        /// <summary>CmdSetAmmo(id класса): id — один из классов этой смены.</summary>
        internal static bool IsSpawnRole(Player p, int roleId) { var st = Get(p); return Fresh(st) && st.Roles.Contains(roleId); }

        /// <summary>CmdSetDead(значение): true — только если среди классов смены был наблюдатель, false — если был живой класс.</summary>
        internal static bool IsSpawnDeadState(Player p, bool dead)
        {
            var st = Get(p);
            if (!Fresh(st)) return false;
            return dead ? st.Roles.Contains(SpectatorRoleId) : st.Roles.Any(r => r != SpectatorRoleId);
        }

        internal static bool InSpawnWindow(Player p)
        {
            var st = Get(p);
            return st.SpawnAt >= 0f && Time.realtimeSinceStartup - st.SpawnAt <= Math.Max(30f, Cfg.SpawnWindowSeconds);
        }

        /// <summary>Предмет входит в стартовый набор класса этой смены.</summary>
        internal static bool IsStartItem(Player p, int itemId) => Get(p).Kit.ContainsKey(itemId);

        /// <summary>Набор класса прочитан (иначе блокировать по предметам нельзя — только лог).</summary>
        internal static bool StartItemsKnown(Player p) => Get(p).KitKnown;

        /// <summary>Клиент просит ещё один стартовый предмет: укладывается ли в количество из набора (и засчитать).</summary>
        internal static bool TakeStartItem(Player p, int itemId)
        {
            var st = Get(p);
            if (!Fresh(st) || !st.Kit.TryGetValue(itemId, out var max)) return false;
            st.Given.TryGetValue(itemId, out var n);
            if (n >= max) return false;
            st.Given[itemId] = n + 1;
            return true;
        }

        // ---------------- Блокировка ----------------

        /// <summary>
        /// Блокировать ли эту проверку для игрока: блокировка включена, проверка в списке, игрок не админ и не в первые
        /// JoinGraceSeconds после появления на сервере. Подмена SteamID — без поблажек и до проверки админки.
        /// </summary>
        private static readonly HashSet<string> NewChecks = new() { "spawn_tool" };

        internal static bool ShouldBlock(Player p, string check)
        {
            try
            {
                if (!Active || !Cfg.Block || p == null) return false;
                // проверки, добавленные после 0.10.0, в сохранённом unicorn.json ещё не перечислены — включаем их через BlockNewChecks
                bool listed = Cfg.BlockChecks != null && Cfg.BlockChecks.Contains(check);
                if (!listed && !(Cfg.BlockNewChecks && NewChecks.Contains(check))) return false;
                if (check == "steamid_spoof" || check == "role_spoof") return true;   // до IsAdmin: админку как раз и подделывают
                if (IsAdmin(p)) return false;
                var st = Get(p);
                float since = Time.realtimeSinceStartup - st.FirstSeen;
                // первые секунды после появления на сервере — только лог (клиент грузится); без SteamID — минута
                // (дальше блокируем: иначе читер просто не шлёт свой SteamID и остаётся неприкосновенным)
                float grace = p.SteamId == 0 ? 60f : Math.Max(0f, Cfg.JoinGraceSeconds);
                return since > grace;
            }
            catch { return false; }
        }

        // Отметка «этот вызов заблокирован» — чтобы постфиксы аудита/событий того же вызова не считали его выполненным.
        private static (string Cmd, IntPtr Who, int Frame) _blocked;
        internal static void MarkBlocked(string cmd, IntPtr who) => _blocked = (cmd, who, Time.frameCount);
        internal static bool WasBlocked(string cmd, IntPtr who) => _blocked.Cmd == cmd && _blocked.Who == who && _blocked.Frame == Time.frameCount;

        // ---------------- Кто админ ----------------

        internal static bool IsAdmin(Player p)
        {
            if (p == null) return false;
            try { if (p.HasAdminTools) return true; } catch { }
            try
            {
                var upm = p.GameObject.GetComponent<UserPermisionManager>();
                if (upm != null && (int)upm.jzw > 0) return true;   // ранг из ServerAdmins.json: Helper и выше
            }
            catch { }
            return Cfg.ExemptSteamIds != null && Cfg.ExemptSteamIds.Contains(p.SteamId.ToString());
        }

        internal static Player Owner(Mirror.NetworkBehaviour nb)
        {
            if (nb == null) return null;
            var p = Player.Get(nb.gameObject);
            if (p != null) return p;
            var ps = nb.GetComponentInParent<PlayerStatistics>();
            return ps != null ? Player.Get(ps.gameObject) : null;
        }

        // ---------------- Сработка ----------------

        internal static void Flag(Player p, string check, string details, string nickFallback = null) => Flag(p, check, details, false, nickFallback);

        internal static void Flag(Player p, string check, string details, bool blocked, string nickFallback = null)
        {
            try
            {
                var k = p != null ? Key(p) : 0UL;
                var key = (k, check);
                float now = Time.realtimeSinceStartup;
                Hits.TryGetValue(key, out var h);
                h.Count++;
                bool show = now - h.Last >= Math.Max(0f, Cfg.RepeatLogSeconds) || h.Count == 1;
                if (show) h.Last = now;
                Hits[key] = h;
                if (!show) return;

                string who = p != null ? $"{p.Nickname} ({p.SteamId}, {p.Role.Name})" : nickFallback ?? "?";
                string line = $"[AntiCheat] {(blocked ? "ЗАБЛОКИРОВАНО" : "ПОДОЗРЕНИЕ НА ЧИТЫ")}: {who} — {details}{(h.Count > 1 ? $" (×{h.Count})" : "")}";
                Log.Error(line);
                lock (Recent)
                {
                    Recent.Add($"{DateTime.Now:HH:mm:ss} {line.Substring(12)}");
                    if (Recent.Count > 100) Recent.RemoveAt(0);
                }
                if (p != null) PlayerEvents.CheatSuspected.Invoke(new CheatSuspectedEventArgs(p, check, details, h.Count, blocked));
            }
            catch (Exception e) { Log.Warn($"[AntiCheat] {e.Message}"); }
        }

        internal static string Summary()
        {
            var byPlayer = Hits.Where(kv => kv.Value.Count > 0)
                .GroupBy(kv => kv.Key.Item1)
                .Select(g =>
                {
                    var p = Player.List.FirstOrDefault(x => Key(x) == g.Key);
                    var name = p != null ? $"{p.Nickname} ({p.SteamId})" : g.Key.ToString();
                    return $"  {name}: " + string.Join(", ", g.Select(x => $"{x.Key.Item2}×{x.Value.Count}"));
                }).ToList();
            List<string> recent;
            lock (Recent) recent = Recent.Skip(Math.Max(0, Recent.Count - 15)).ToList();
            return $"античит: {(!Cfg.Enabled ? "выключен" : Cfg.Block ? $"включён, блокирует: {string.Join(", ", Cfg.BlockChecks ?? new List<string>())}" : "включён (только лог)")}, окно появления {Cfg.SpawnWindowSeconds} с\n" +
                   (byPlayer.Count == 0 ? "  подозрений нет" : string.Join("\n", byPlayer)) +
                   (recent.Count == 0 ? "" : "\nпоследние:\n  " + string.Join("\n  ", recent));
        }

        internal static void Reset() { Hits.Clear(); lock (Recent) Recent.Clear(); }

        // ---------------- Аптечки ----------------

        /// <summary>
        /// Предмет был у игрока и пропал не дольше seconds назад. Команды клиента могут прийти уже после того,
        /// как игра убрала предмет (SCP-420-J шлёт CmdDeleteItem, пока зажата кнопка; граната — бросок в момент смерти).
        /// </summary>
        internal static bool ItemGoneRecently(Player p, int itemId, float seconds) =>
            ItemGoneAt.TryGetValue((Key(p), itemId), out var t) && Time.realtimeSinceStartup - t <= seconds;

        internal static bool MedkitGoneRecently(Player p, float seconds) =>
            MedkitGoneAt.TryGetValue(Key(p), out var t) && Time.realtimeSinceStartup - t <= seconds;

        /// <summary>Возвращает секунды с прошлого HealMe и запоминает текущий.</summary>
        internal static float SinceLastHeal(Player p)
        {
            var k = Key(p); float now = Time.realtimeSinceStartup;
            float d = LastHeal.TryGetValue(k, out var t) ? now - t : float.MaxValue;
            LastHeal[k] = now;
            return d;
        }
    }
}
