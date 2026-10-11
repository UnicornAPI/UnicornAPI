using Unicorn.API;
using Unicorn.Events;

namespace HelloUnicorn
{
    public sealed class Config
    {
        public string WelcomeText = "<b>{name}</b> зашёл на сервер!";
        public bool AnnounceDeaths = true;
        public int ClassDStartHp = 0; // 0 = не трогать
    }

    /// <summary>Тестовый плагин: проверка всех событий UnicornAPI.</summary>
    public sealed class HelloUnicornPlugin : Plugin
    {
        public override string Name => "HelloUnicorn";
        public override string Author => "FLX";

        private Config _cfg;

        public override void OnEnabled()
        {
            _cfg = LoadConfig<Config>();

            PlayerEvents.Joined += OnJoined;
            PlayerEvents.Left += e => Log.Info($"[HelloUnicorn] вышел: {e.Player}");
            PlayerEvents.ChangingRole += e => Log.Info($"[HelloUnicorn] {e.Player.Nickname}: роль {e.OldRole} -> {e.NewRole}");
            PlayerEvents.ChangedRole += OnChangedRole;
            PlayerEvents.Hurting += e => Log.Debug($"[HelloUnicorn] {e.Attacker?.Nickname ?? "мир"} -> {e.Player.Nickname}: {e.Amount} ({e.HitInfo})");
            PlayerEvents.Died += OnDied;

            ServerEvents.WaitingForPlayers += () => Log.Info("[HelloUnicorn] лобби");
            ServerEvents.RoundStarted += () => { Log.Info("[HelloUnicorn] раунд начался"); Timing.CallDelayed(3f, () => Server.Broadcast("Раунд начался! Удачи.")); };
            ServerEvents.RoundEnded += () => Log.Info("[HelloUnicorn] раунд закончился");

            // v0.2: двери, наручники, предметы
            PlayerEvents.InteractingDoor += e => Log.Info($"[HelloUnicorn] {e.Player.Nickname} жмёт дверь {e.Door}");
            PlayerEvents.Cuffing += e => Log.Info($"[HelloUnicorn] {e.Cuffer?.Nickname} надевает наручники на {e.Player.Nickname}");
            PlayerEvents.Uncuffing += e => Log.Info($"[HelloUnicorn] {e.Cuffer?.Nickname} снимает наручники с {e.Player.Nickname}");
            PlayerEvents.CuffChanged += e => Log.Info($"[HelloUnicorn] {e.Player.Nickname}: наручники = {e.IsCuffed}");
            PlayerEvents.PickingUpItem += e => Log.Info($"[HelloUnicorn] {e.Player.Nickname} подбирает {e.Pickup?.name}");
            PlayerEvents.DroppingItem += e => Log.Info($"[HelloUnicorn] {e.Player.Nickname} выбрасывает слот {e.Slot} ({Items.GetName(e.ItemId)})");
            PlayerEvents.ItemAdded += e => Log.Info($"[HelloUnicorn] {e.Player.Nickname} +{e.ItemName} (слот {e.Slot})");
            PlayerEvents.ItemRemoved += e => Log.Info($"[HelloUnicorn] {e.Player.Nickname} -{e.ItemName} (слот {e.Slot})");

        }

        public override void OnDisabled()
        {
            Log.Info("[HelloUnicorn] выключен");
        }

        private void OnJoined(JoinedEventArgs e)
        {
            // Небольшая задержка, чтобы клиент успел прогрузиться и увидел броадкаст.
            Timing.CallDelayed(2f, () => Server.Broadcast(_cfg.WelcomeText.Replace("{name}", e.Player.Nickname)));
        }

        private void OnChangedRole(ChangedRoleEventArgs e)
        {
            if (_cfg.ClassDStartHp > 0 && e.Player.Team == Team.ClassD)
                Timing.NextFrame(() => e.Player.Health = _cfg.ClassDStartHp);
        }

        private void OnDied(DiedEventArgs e)
        {
            Log.Info($"[HelloUnicorn] умер {e.Player.Nickname} от {e.Attacker?.Nickname ?? "мира"} ({e.HitInfo})");
            if (_cfg.AnnounceDeaths && e.Attacker != null && e.Attacker != e.Player)
                Server.Broadcast($"{e.Attacker.Nickname} убил {e.Player.Nickname}");
        }
    }
}
