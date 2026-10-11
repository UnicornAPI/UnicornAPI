using System;
using System.Collections.Generic;
using System.Linq;

namespace Unicorn.API
{
    /// <summary>Сохранённое оформление игрока (переживает перезапуск сервера). null в поле — «не задано».</summary>
    public sealed class PlayerProfile
    {
        public ulong SteamId { get; set; }
        public string Badge { get; set; }
        public string BadgeColor { get; set; }
        public string Nick { get; set; }
        public bool? AdminTools { get; set; }
        public string UpdatedAt { get; set; }

        public bool IsEmpty => Badge == null && BadgeColor == null && Nick == null && AdminTools == null;

        public override string ToString() =>
            $"{SteamId}: бейдж={(Badge == null ? "-" : $"'{Badge.Trim()}' #{BadgeColor}")} ник={Nick ?? "-"} админ-панель={(AdminTools == null ? "-" : AdminTools.Value ? "да" : "нет")}";
    }

    /// <summary>Профили игроков в БД (таблица player_profiles). При входе игрока применяются автоматически.</summary>
    public static class Profiles
    {
        private static bool _init;

        private static void Ensure()
        {
            if (_init) return;
            Database.Execute(@"CREATE TABLE IF NOT EXISTS player_profiles(
                steam_id    INTEGER PRIMARY KEY,
                badge       TEXT,
                badge_color TEXT,
                nick        TEXT,
                admin_tools INTEGER,
                updated_at  TEXT NOT NULL)");
            _init = true;
        }

        public static PlayerProfile Get(ulong steamId)
        {
            Ensure();
            var r = Database.Query("SELECT * FROM player_profiles WHERE steam_id=$1", (long)steamId).FirstOrDefault();
            return r == null ? null : Map(r);
        }

        public static IReadOnlyList<PlayerProfile> All()
        {
            Ensure();
            return Database.Query("SELECT * FROM player_profiles ORDER BY updated_at DESC").Select(Map).ToList();
        }

        /// <summary>Сохранить (пустой профиль удаляется).</summary>
        public static void Save(PlayerProfile p)
        {
            Ensure();
            if (p.IsEmpty) { Delete(p.SteamId); return; }
            p.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Database.Execute(@"INSERT INTO player_profiles(steam_id,badge,badge_color,nick,admin_tools,updated_at)
                               VALUES($1,$2,$3,$4,$5,$6)
                               ON CONFLICT(steam_id) DO UPDATE SET badge=$2,badge_color=$3,nick=$4,admin_tools=$5,updated_at=$6",
                (long)p.SteamId, p.Badge, p.BadgeColor, p.Nick, p.AdminTools == null ? null : (object)(p.AdminTools.Value ? 1 : 0), p.UpdatedAt);
        }

        public static void Delete(ulong steamId) { Ensure(); Database.Execute("DELETE FROM player_profiles WHERE steam_id=$1", (long)steamId); }

        /// <summary>Изменить профиль (создаётся, если нет) и сразу применить к игроку, если он онлайн.</summary>
        public static PlayerProfile Update(ulong steamId, Action<PlayerProfile> change)
        {
            var p = Get(steamId) ?? new PlayerProfile { SteamId = steamId };
            change(p);
            Save(p);
            var online = Player.Get(steamId);
            if (online != null) Apply(online, p);
            return p;
        }

        /// <summary>Применить профиль к игроку онлайн.</summary>
        public static void Apply(Player pl, PlayerProfile p)
        {
            if (p == null) return;
            if (p.Badge != null) pl.SetBadge(p.Badge, p.BadgeColor ?? "FFFFFF");
            else if (pl.WantBadge != null) pl.ClearBadge();
            if (p.Nick != null) pl.DisplayNickname = p.Nick;
            else if (pl.WantNick != null) pl.DisplayNickname = null;
            if (p.AdminTools != null) pl.SetAdminToolsPersistent(p.AdminTools.Value);
        }

        internal static void OnJoined(Player pl)
        {
            if (!Database.IsAvailable) return;
            try
            {
                var p = Get(pl.SteamId);
                if (p == null) return;
                Apply(pl, p);
                Log.Info($"[Profiles] {pl.Nickname}: применён профиль ({p})");
            }
            catch (Exception e) { Log.Error($"[Profiles] {e.Message}"); }
        }

        private static PlayerProfile Map(Dictionary<string, object> r) => new()
        {
            SteamId = (ulong)Convert.ToInt64(r["steam_id"]),
            Badge = r["badge"] as string,
            BadgeColor = r["badge_color"] as string,
            Nick = r["nick"] as string,
            AdminTools = r["admin_tools"] == null ? null : Convert.ToInt64(r["admin_tools"]) != 0,
            UpdatedAt = r["updated_at"] as string,
        };
    }
}
