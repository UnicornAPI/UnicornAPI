using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.API
{
    /// <summary>Обёртка над игроком. Живёт, пока жив его GameObject на сервере.</summary>
    public sealed class Player
    {
        private static readonly Dictionary<IntPtr, Player> ByPtr = new();

        /// <summary>Все игроки, прошедшие Joined (прислали SteamID).</summary>
        public static IReadOnlyCollection<Player> List
        {
            get
            {
                Prune();
                return ByPtr.Values.Where(p => p.IsVerified).ToList();
            }
        }

        private static void Prune()
        {
            List<IntPtr> dead = null;
            foreach (var kv in ByPtr)
                if (!kv.Value.IsValid) (dead ??= new List<IntPtr>()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) ByPtr.Remove(k);
        }

        public GameObject GameObject { get; }
        public PlayerStatistics Stats { get; }
        public CharacterClassManager ClassManager { get; }
        public PlayerAdmin Admin { get; }
        public PlayerTransformSync TransformSync { get; }
        public global::Inventory InventoryComponent { get; }
        public HandCuffs HandCuffs { get; }
        public AnimationController Animation { get; }

        /// <summary>Инвентарь игрока: выдача/удаление/выброс предметов.</summary>
        public PlayerInventory Inventory { get; }

        // ---------------- Оружие и патроны ----------------

        public WeaponManager WeaponManager => _wm ??= GameObject.GetComponent<WeaponManager>() ?? GameObject.GetComponentInChildren<WeaponManager>(true);
        private WeaponManager _wm;
        public AmmoManager AmmoManager => _am ??= (WeaponManager?.am ?? GameObject.GetComponent<AmmoManager>() ?? GameObject.GetComponentInChildren<AmmoManager>(true));
        private AmmoManager _am;

        /// <summary>Патроны: Get/Set/Add/Take по имени типа.</summary>
        public PlayerAmmo Ammo => _ammo ??= new PlayerAmmo(this);
        private PlayerAmmo _ammo;

        /// <summary>Все виды оружия игрока (по одному на тип — у каждого игрока свои копии характеристик).</summary>
        public IEnumerable<PlayerWeapon> AllWeapons
        {
            get
            {
                var wm = WeaponManager;
                if (wm?.weapons == null) yield break;
                foreach (var w in wm.weapons) if (w != null) yield return new PlayerWeapon(this, w);
            }
        }

        /// <summary>Оружие в руках или null (в руках не оружие).</summary>
        public PlayerWeapon CurrentWeapon
        {
            get
            {
                int item = Inventory.CurrentItem;
                foreach (var w in AllWeapons) if (w.ItemId == item) return w;
                return null;
            }
        }

        /// <summary>Оружие игрока по ID предмета или null.</summary>
        public PlayerWeapon GetWeapon(int itemId) => AllWeapons.FirstOrDefault(w => w.ItemId == itemId);

        /// <summary>true после события Joined.</summary>
        public bool IsVerified { get; internal set; }

        /// <summary>Место для данных плагинов: player.SessionData["mykey"] = ...</summary>
        public Dictionary<string, object> SessionData { get; } = new();

        private Player(GameObject go)
        {
            GameObject = go;
            Stats = go.GetComponent<PlayerStatistics>();
            ClassManager = go.GetComponent<CharacterClassManager>();
            Admin = go.GetComponent<PlayerAdmin>();
            TransformSync = go.GetComponent<PlayerTransformSync>();
            InventoryComponent = go.GetComponent<global::Inventory>();
            HandCuffs = go.GetComponent<HandCuffs>();
            Animation = go.GetComponent<AnimationController>();
            Inventory = new PlayerInventory(this);
        }

        // ---------------- Получение ----------------

        public static Player Get(GameObject go)
        {
            if (go == null) return null;
            if (ByPtr.TryGetValue(go.Pointer, out var p)) return p;
            if (go.GetComponent<PlayerStatistics>() == null) return null; // не игрок
            p = new Player(go);
            ByPtr[go.Pointer] = p;
            return p;
        }

        public static Player Get(Component c) => c == null ? null : Get(c.gameObject);

        public static Player Get(ulong steamId) => List.FirstOrDefault(p => p.SteamId == steamId);

        /// <summary>Поиск по SteamID64 или части ника (без учёта регистра).</summary>
        public static Player Find(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            if (ulong.TryParse(query, out var id) && Get(id) is { } byId) return byId;
            return List.FirstOrDefault(p => p.Nickname != null && p.Nickname.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static void Remove(GameObject go)
        {
            if (go != null) ByPtr.Remove(go.Pointer);
        }

        internal static void Clear() => ByPtr.Clear();

        // ---------------- Свойства ----------------

        public bool IsValid => GameObject != null && Stats != null;

        /// <summary>Настоящий ник игрока (не меняется от DisplayNickname).</summary>
        public string Nickname
        {
            get
            {
                if (_originalNick != null) return _originalNick;
                var n = Stats.myNick;
                return string.IsNullOrEmpty(n) ? Stats.mySteamNick : n;
            }
        }
        private string _originalNick;

        public ulong SteamId => Stats.steamId.m_SteamID;

        public gu Connection => ClassManager.ConnectionToClient();

        public int Health
        {
            get => Stats.curHP;
            set => Stats.SetHealth(value);
        }

        /// <summary>Макс. HP: из статистики игрока, а если там 0 — из таблицы ролей.</summary>
        public int MaxHealth => Stats.maxHP > 0 ? Stats.maxHP : Role.MaxHealth;

        public bool IsDead => Stats.isDead;

        public int RoleId => ClassManager.curClass;

        public RoleInfo Role => Server.GetRole(RoleId);

        public Team Team => Role.Team;

        // ---------------- Команда для конца раунда ----------------

        private Team? _roundEndTeam; private int _roundEndTeamRole = int.MinValue;

        /// <summary>
        /// За какую команду игрок считается, когда игра проверяет конец раунда (null — как обычно, по роли).
        /// Действует только пока игрок в той же роли, что и в момент вызова: сменил роль или умер — сбрасывается само.
        /// Пример: SCP-035 (в игре команда Watcher, которую конец раунда игнорирует) считается за SCP.
        /// Меняет только подсчёт конца раунда, больше ничего (урон, спавн, сайт — по настоящей роли).
        /// </summary>
        public void SetRoundEndTeam(Team? team) { _roundEndTeam = team; _roundEndTeamRole = RoleId; }

        /// <summary>Действующая подмена команды для конца раунда или null.</summary>
        public Team? RoundEndTeam
        {
            get
            {
                if (_roundEndTeam == null) return null;
                try { return RoleId == _roundEndTeamRole ? _roundEndTeam : null; } catch { return null; }
            }
        }

        public Vector3 Position
        {
            get => GameObject.transform.position;
            set => Teleport(value);
        }

        /// <summary>Компонент игры с ролью/бейджем для списка игроков.</summary>
        public GameRoles GameRoles => _gameRoles ??= GameObject.GetComponent<GameRoles>();
        private GameRoles _gameRoles;

        // заданное плагином — переустанавливается, если игра перезапишет (при входе игра ставит свою роль из префаба)
        internal string WantBadge, WantColor, WantNick;
        internal bool? WantAdminTools;

        /// <summary>
        /// Бейдж после ника в списке игроков (Tab) и в лобби (сырой текст, без автопробела — см. SetBadge).
        /// Над головой у игры текста нет вообще. null/"" — убрать.
        /// </summary>
        public string BadgeText
        {
            get => GameRoles?.myBadgeText;
            set { WantBadge = value ?? ""; GameRoles?.SetRoleBadge(WantBadge); }
        }

        /// <summary>Цвет бейджа, HEX без # (например "FF0000").</summary>
        public string BadgeColor
        {
            get => GameRoles?.myColor;
            set { WantColor = (value ?? "FFFFFF").TrimStart('#'); GameRoles?.SetRoleColor(WantColor); }
        }

        /// <summary>
        /// Поставить бейдж. Игра рисует строку в Tab так: &lt;color=#ЦВЕТ&gt;НИК БЕЙДЖ&lt;/color&gt; — цвет красит и ник.
        /// Пробел перед бейджем добавляется сам. Пример: SetBadge("[Админ]", "FF0000") → «Dolph [Админ]» красным.
        /// </summary>
        public void SetBadge(string text, string colorHex = "FFFFFF")
        {
            if (!string.IsNullOrEmpty(text) && !text.StartsWith(" ")) text = " " + text;
            BadgeText = text; BadgeColor = colorHex;
        }

        /// <summary>Убрать бейдж.</summary>
        public void ClearBadge() { WantBadge = null; WantColor = null; GameRoles?.SetRoleBadge(""); GameRoles?.SetRoleColor("FFFFFF"); }

        /// <summary>Доступ к встроенной админ-панели игры.</summary>
        public bool HasAdminTools
        {
            get => GameRoles != null && GameRoles.hasAccessToAdminTools;
            set => GameRoles?.SetAdminToolsAccess(value);
        }

        /// <summary>Выдать/забрать админ-панель так, чтобы игра не перезаписала при входе (роль из префаба).</summary>
        public void SetAdminToolsPersistent(bool v) { WantAdminTools = v; HasAdminTools = v; }


        /// <summary>
        /// Показываемый ник (список игроков, трупы, убийства). Например "[5 ур.] Dolph".
        /// null — вернуть ник из Steam.
        /// </summary>
        public string DisplayNickname
        {
            get => string.IsNullOrEmpty(Stats.myNick) ? Nickname : Stats.myNick;
            set
            {
                _originalNick ??= Nickname;
                WantNick = string.IsNullOrEmpty(value) ? null : value;
                Stats.SetNick(WantNick ?? _originalNick);
            }
        }

        internal void ReapplyCustom()
        {
            if (WantBadge != null) GameRoles?.SetRoleBadge(WantBadge);
            if (WantColor != null) GameRoles?.SetRoleColor(WantColor);
            if (WantNick != null) Stats.SetNick(WantNick);
            if (WantAdminTools != null) GameRoles?.SetAdminToolsAccess(WantAdminTools.Value);
        }

        /// <summary>РП-описание персонажа (то, что ставит SetRolePlayPlayerDetails в админке).</summary>
        public string RolePlayDetails
        {
            get => Stats.rolePlayDetails;
            set => Stats.SetRolePlayDetails(value ?? "");
        }

        // ---------------- Наручники ----------------

        /// <summary>Закован ли игрок.</summary>
        /// <summary>
        /// Личное сообщение на экране игрока (~5 с). Идёт через текст SCP-294 — его рисует только сам игрок,
        /// остальные клиенты сообщение игнорируют. Поддерживает цветовые теги.
        /// </summary>
        public bool ShowMessage(string text)
        {
            try
            {
                _cup ??= GameObject.GetComponent<Scp294Cup>() ?? GameObject.GetComponentInChildren<Scp294Cup>(true);
                if (_cup == null) return false;
                _cup.RpcCupMessage(text ?? "");
                return true;
            }
            catch (System.Exception e) { Log.Error($"[ShowMessage] {e.Message}"); return false; }
        }
        private Scp294Cup _cup;

        /// <summary>Персонал: встроенная админ-панель, ранг из ServerAdmins.json или ExemptSteamIds античита.</summary>
        public bool IsStaff => Internal.AntiCheat.IsAdmin(this);

        /// <summary>
        /// Сообщить о подозрении на читы от плагина (как срабатывание античита): уйдёт в PlayerEvents.CheatSuspected,
        /// оттуда — в WebBridge (cheat_suspect) и на сайт.
        /// </summary>
        public void FlagCheat(string check, string details, int count = 1, bool blocked = false) =>
            Events.PlayerEvents.CheatSuspected.Invoke(new Events.CheatSuspectedEventArgs(this, check, details, count, blocked));

        public bool IsCuffed => InventoryComponent != null && InventoryComponent.cuffed;

        /// <summary>Кто держит игрока в наручниках (null — никто или закован командой).</summary>
        public Player Cuffer
        {
            get
            {
                if (InventoryComponent == null) return null;
                foreach (var p in List)
                    if (p.HandCuffs != null && p.HandCuffs.currentCuffedPlayer != null && p.HandCuffs.currentCuffedPlayer.Pointer == InventoryComponent.Pointer) return p;
                return null;
            }
        }

        /// <summary>
        /// Надеть наручники. cuffer — кто «держит» (дистанция, как в игре); null — просто заковать, без ведущего.
        /// </summary>
        public void Cuff(Player cuffer = null)
        {
            if (InventoryComponent == null) return;
            InventoryComponent.SetCuffedRaw(true);
            try { InventoryComponent.CuffedLocal(true); } catch { }
            if (cuffer?.HandCuffs != null) cuffer.HandCuffs.SetCuffer(InventoryComponent);
            try { Animation?.RpcSetAnimBool("Cuffed", true); } catch { }
            try { InventoryComponent.RpcCuffEffect(); } catch { }

        }

        /// <summary>Снять наручники.</summary>
        public void Uncuff()
        {
            if (InventoryComponent == null) return;
            var c = Cuffer;
            if (c?.HandCuffs != null) c.HandCuffs.SetCuffer(null);
            InventoryComponent.SetCuffedRaw(false);
            try { InventoryComponent.CuffedLocal(false); } catch { }
            try { Animation?.RpcSetAnimBool("Cuffed", false); } catch { }

        }

        // ---------------- Действия ----------------

        /// <summary>Вылечить на amount (не выше MaxHealth, если он известен).</summary>
        public void Heal(int amount)
        {
            if (amount <= 0 || IsDead) return;
            int max = MaxHealth > 0 ? MaxHealth : int.MaxValue;
            Health = Math.Min(Health + amount, Math.Max(max, Health));
        }

        /// <summary>
        /// Нанести урон как игра (с событиями Hurting/Died, рэгдоллом и смертью).
        /// attacker = null — урон «от мира».
        /// </summary>
        public void Damage(int amount, string reason = "UnicornAPI", Player attacker = null)
        {
            if (amount <= 0) return;
            (attacker ?? this).Stats.Hurt(GameObject, amount, reason);
        }

        /// <summary>Убить игрока.</summary>
        public void Kill(string reason = "UnicornAPI") => Damage(Math.Max(Health, 1) + 100000, reason);

        /// <summary>
        /// Сменить роль. useSpawnPosition=true — телепорт на спавн роли (как ForceClass в админке),
        /// false — остаться на месте (как ForceClassWithOutTp).
        /// </summary>
        public void SetRole(int roleId, bool useSpawnPosition = true)
        {
            ClassManager.SetUseClassPos(useSpawnPosition);
            ClassManager.ForceClass(GameObject, roleId);
        }

        public void Teleport(Vector3 position) => TransformSync.ServerSetPosition(position);

        /// <summary>Кикнуть с причиной (игрок увидит окно с текстом).</summary>
        public void Kick(string reason)
        {
            var conn = Connection;
            if (conn == null) return;
            ClassManager.TargetKick(conn, reason);
            Timing.CallDelayed(0.5f, () => { try { conn.Disconnect(); } catch { /* уже отключён */ } });
        }

        public override string ToString() => $"{Nickname} ({SteamId})";
    }
}
