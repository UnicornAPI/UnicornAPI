using System;
using System.IO;
using System.Text.Json;
using Unicorn.API;

namespace Unicorn.Internal
{
    public sealed class AntiCheatConfig
    {
        public bool Enabled { get; set; } = true;
        /// <summary>Сколько секунд после появления/смены класса клиенту можно слать «стартовые» команды (предметы, HP, патроны).</summary>
        public float SpawnWindowSeconds { get; set; } = 30f;
        /// <summary>Не повторять одно и то же подозрение по игроку чаще, чем раз в N секунд (счётчик копится).</summary>
        public float RepeatLogSeconds { get; set; } = 10f;
        /// <summary>SteamID64, на которых античит не срабатывает (кроме админов игры и тех, у кого есть админ-панель).</summary>
        public System.Collections.Generic.List<string> ExemptSteamIds { get; set; } = new();

        /// <summary>Блокировать (сервер НЕ выполняет команду), а не только писать в лог. Действует на проверки из BlockChecks.</summary>
        public bool Block { get; set; } = true;
        /// <summary>Какие проверки блокируют. Остальные — только лог.</summary>
        public System.Collections.Generic.List<string> BlockChecks { get; set; } = new()
        {
            "steamid_spoof", "role_spoof", "admin_cmd", "add_item", "set_hp", "set_ammo", "set_dead", "spawn_tp",
            "clear_inv", "force_spectator", "door_close", "spawn_tool",
        };
        /// <summary>Новые проверки (появились после 0.10.0, например spawn_tool) блокируют, даже если их нет в BlockChecks.</summary>
        public bool BlockNewChecks { get; set; } = true;
        /// <summary>Кикать за подмену SteamID.</summary>
        public bool KickOnSpoof { get; set; } = true;
        /// <summary>Первые N секунд после входа игрока — только лог (клиент ещё загружается), кроме подмены SteamID.</summary>
        public float JoinGraceSeconds { get; set; } = 30f;
    }

    /// <summary>Настройки самого UnicornAPI: UnicornAPI/unicorn.json.</summary>
    public sealed class CoreConfig
    {
        /// <summary>Показывать сервер в игровом списке серверов (Steam-лобби).</summary>
        public bool ShowInServerList { get; set; } = true;

        /// <summary>Сторож лобби: если сервер выпал из своего Steam-лобби (пропал из списка), создать новое без рестарта.</summary>
        public bool LobbyWatchdog { get; set; } = true;
        /// <summary>Как часто проверять, сек (минимум 20).</summary>
        public float LobbyWatchdogIntervalSeconds { get; set; } = 60f;

        /// <summary>
        /// Название в игровом списке серверов. Поддерживает цвета TextMeshPro: &lt;color=#FF3B3B&gt;FLX&lt;/color&gt;.
        /// Пусто — берётся ServerName из YourServerConfig.json игры.
        /// </summary>
        public string ServerListName { get; set; } = "<color=#FF3B3B>FLX</color> <color=#9A9A9A>|</color> Project 90";

        /// <summary>Блокировка раунда при старте сервера. В сцене игры она включена — по умолчанию снимаем.</summary>
        public bool RoundLockDefault { get; set; } = false;

        /// <summary>
        /// Возвращать блокировку к RoundLockDefault в каждом новом раунде (лобби). Включил блокировку для теста или ивента —
        /// она действует до конца раунда, дальше снимется сама. false — блокировка остаётся, пока её не снимут вручную.
        /// </summary>
        public bool ResetRoundLockEachRound { get; set; } = true;

        /// <summary>
        /// Игра сама блокирует раунд, если на старте игроков 2 или меньше. Снимать такую блокировку, когда игроков
        /// станет столько (0 — не снимать). Блокировку, которую включил админ, это не трогает.
        /// </summary>
        public int AutoUnlockAtPlayers { get; set; } = 3;

        /// <summary>Античит (пока только наблюдение: пишет в лог красным, ничего не блокирует).</summary>
        public AntiCheatConfig AntiCheat { get; set; } = new();

        /// <summary>
        /// Проверка версии игры при запуске: "warn" — предупредить и работать, "safe" — при несовпадении не ставить
        /// патчи и не грузить плагины, "off" — не проверять.
        /// </summary>
        public string GameHashCheck { get; set; } = "warn";
        /// <summary>Дополнительные разрешённые SHA-256 GameAssembly.dll (если игра та же, а файл отличается).</summary>
        public System.Collections.Generic.List<string> AllowedGameHashes { get; set; } = new();

        /// <summary>Закрывать все двери при рестарте раунда (так задумано игрой, но на dedicated это не работало).</summary>
        public bool CloseDoorsOnRestart { get; set; } = true;

        /// <summary>Отпирать все запертые двери при рестарте раунда (игра сама двери не запирает; защита от «вечно запертых»).</summary>
        public bool UnlockDoorsOnRestart { get; set; } = true;

        internal static CoreConfig Current { get; private set; } = new();
        private static string FilePath => Path.Combine(Paths.Root, "unicorn.json");
        private static readonly JsonSerializerOptions Opts = new()
        {
            WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        internal static void Load()
        {
            try
            {
                if (File.Exists(FilePath)) Current = JsonSerializer.Deserialize<CoreConfig>(File.ReadAllText(FilePath), Opts) ?? new CoreConfig();
                Save(); // дописать новые поля
            }
            catch (Exception e) { Log.Error($"[unicorn.json] битый конфиг: {e.Message} — беру значения по умолчанию"); Current = new CoreConfig(); }
        }

        internal static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Opts)); }
            catch (Exception e) { Log.Error($"[unicorn.json] не записался: {e.Message}"); }
        }
    }
}
