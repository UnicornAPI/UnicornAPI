namespace Unicorn.API
{
    /// <summary>Команда (зеркало игрового enum Team).</summary>
    public enum Team
    {
        None = 0,
        ClassD = 1,
        Scientist = 2,
        FacilityGuard = 3,
        NineTailedFox = 4,
        ChaosInsurgency = 5,
        SCP = 6,
        Spectator = 7,
        Watcher = 8,
    }

    /// <summary>Описание роли (класса) из CharacterClassManager.roles.</summary>
    public readonly struct RoleInfo
    {
        public int Id { get; }
        public string Name { get; }
        public Team Team { get; }
        public int MaxHealth { get; }

        internal RoleInfo(int id, string name, Team team, int maxHp)
        {
            Id = id; Name = name; Team = team; MaxHealth = maxHp;
        }

        public override string ToString() => $"{Name} (#{Id}, {Team})";
    }
}
