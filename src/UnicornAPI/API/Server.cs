using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.Linq;
using Unicorn.Internal;
using UnityEngine;

namespace Unicorn.API
{
    public static class Server
    {
        public static IReadOnlyCollection<Player> Players => Player.List;
        public static int PlayerCount => Player.List.Count;

        /// <summary>Лимит игроков (из лобби Steam). 0 — неизвестно.</summary>
        public static int MaxPlayers { get { LobbyAdvertiser.Members(out var l); return l; } }

        /// <summary>
        /// Публичная информация плагинов для сайта (механики, награды, настройки). Ключ — имя плагина.
        /// Кладите только примитивы, строки, списки и Dictionary&lt;string, object&gt; — они без проблем уходят в JSON.
        /// </summary>
        public static System.Collections.Concurrent.ConcurrentDictionary<string, object> PublicInfo { get; } = new();

        /// <summary>Длительность броадкаста по умолчанию, сек.</summary>
        public static float DefaultBroadcastDuration = 7f;
        private static int _bcGen;

        /// <summary>Броадкаст всем на <see cref="DefaultBroadcastDuration"/> секунд.</summary>
        public static void Broadcast(string text) => Broadcast(text, DefaultBroadcastDuration);

        /// <summary>
        /// Броадкаст всем. В игре броадкаст висит на экране вечно (кнопка в админ-панели тоже),
        /// поэтому через duration секунд отправляем пустой текст. duration &lt;= 0 — не убирать.
        /// </summary>
        public static void Broadcast(string text, float duration)
        {
            if (!SendBroadcast(text)) return;
            int gen = ++_bcGen;
            if (duration > 0)
                Timing.CallDelayed(duration, () => { if (gen == _bcGen) SendBroadcast(""); });
        }

        /// <summary>Убрать текущий броадкаст с экрана.</summary>
        public static void ClearBroadcast() { _bcGen++; SendBroadcast(""); }

        private static bool SendBroadcast(string text)
        {
            var sender = Player.List.FirstOrDefault(p => p.Admin != null);
            if (sender == null) { Log.Warn("[Broadcast] нет игроков — некому отправить RPC"); return false; }
            sender.Admin.RpcDoBroadcast(text);
            return true;
        }

        /// <summary>Все роли игры (индекс = RoleId).</summary>
        public static IReadOnlyList<RoleInfo> Roles
        {
            get
            {
                var ccm = Object.FindObjectOfType<CharacterClassManager>();
                if (ccm == null || ccm.roles == null) return new List<RoleInfo>();
                var list = new List<RoleInfo>(ccm.roles.Length);
                for (int i = 0; i < ccm.roles.Length; i++) list.Add(ToInfo(i, ccm.roles[i]));
                return list;
            }
        }

        private static readonly Dictionary<string, string[]> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["d"] = new[] { "class-d", "class d", "classd" }, ["classd"] = new[] { "class-d", "class d" },
            ["ntf"] = new[] { "nine-tailed", "ntf", "mtf", "моб", "мог" }, ["mtf"] = new[] { "nine-tailed", "ntf", "mtf" },
            ["chaos"] = new[] { "chaos" }, ["ci"] = new[] { "chaos" },
            ["guard"] = new[] { "guard" }, ["sci"] = new[] { "scientist" }, ["scientist"] = new[] { "scientist" },
            ["spectator"] = new[] { "spectator" }, ["spec"] = new[] { "spectator" },
        };

        /// <summary>
        /// Роль по ID или имени. Число в пределах списка ролей — ID; иначе «173»/«scp173»/«scp-173»/«049-2» — SCP;
        /// точное название; псевдонимы (d, ntf, chaos, guard, sci, spec — берётся самое короткое подходящее название);
        /// иначе часть названия, но только если подходит ровно одна роль. null — не нашлось или неоднозначно.
        /// </summary>
        public static RoleInfo? FindRole(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            string q = query.Trim();
            var roles = Roles;
            bool isInt = int.TryParse(q, out var id);
            if (isInt && (id < 0 || (id < roles.Count && !q.StartsWith("0")) || (id == 0 && q == "0"))) return GetRole(id);
            if (roles.Count == 0) return isInt ? GetRole(id) : null;

            var exact = roles.Where(r => string.Equals(r.Name, q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];

            string scp = q.ToLowerInvariant().Replace("scp", "").Replace("-", "").Replace("_", "").Trim();
            if (scp.Length > 0 && scp.All(char.IsDigit))
            {
                var s = roles.Where(r => Norm(r.Name).Contains("scp" + scp)).OrderBy(r => r.Name.Length).ToList();
                if (s.Count > 0 && (s.Count == 1 || Norm(s[0].Name).EndsWith("scp" + scp))) return s[0];
                return null;
            }

            if (RoleAliases.TryGetValue(q, out var al))
            {
                var byAlias = roles.Where(r => al.Any(k => r.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(r => r.Name.Length).ToList();
                if (byAlias.Count > 0) return byAlias[0];
            }
            var part = roles.Where(r => r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            return part.Count == 1 ? part[0] : null;
        }

        private static string Norm(string n) => (n ?? "").Replace("-", "").Replace(" ", "").Replace("_", "").ToLowerInvariant();

        public static RoleInfo GetRole(int id)
        {
            var ccm = Object.FindObjectOfType<CharacterClassManager>();
            if (ccm?.roles == null || id < 0 || id >= ccm.roles.Length) return new RoleInfo(id, "Unknown", Team.None, 0);
            return ToInfo(id, ccm.roles[id]);
        }

        private static RoleInfo ToInfo(int id, Class c) =>
            c == null ? new RoleInfo(id, "Unknown", Team.None, 0) : new RoleInfo(id, c.fullName, (Team)(int)c.team, c.maxHP);
    }

    public static class Round
    {
        private static RoundManager RM => RoundManager.singleton;

        /// <summary>Сырое состояние раунда из игры: "Waiting for round start" / "started" / "ending".</summary>
        public static string State => RM?.info;

        public static bool IsStarted => State == "started";
        public static bool IsEnding => State == "ending";
        public static bool IsWaiting => !IsStarted && !IsEnding;

        /// <summary>Данные плагинов на текущий раунд (очищаются в начале каждого раунда, переживают reload плагинов).</summary>
        public static System.Collections.Concurrent.ConcurrentDictionary<string, object> SessionData { get; } = new();

        /// <summary>Сколько секунд идёт текущий раунд (свой таймер UnicornAPI). Во время «ending» — длительность закончившегося раунда.</summary>
        public static float ElapsedTime => RoundStateTracker.Elapsed;

        /// <summary>Длительность последнего закончившегося раунда, сек.</summary>
        public static float LastRoundDuration => RoundStateTracker.LastDuration;

        public static bool IsLocked
        {
            get => RM?.roundLock ?? false;
            set { if (RM != null) { RoundLockGuard.NoteManual(); RM.SetRoundLock(value); } }
        }

        public static void ForceStart() => RM?.ForceRoundStart();
        public static void Restart() => RM?.RestartRound();
        /// <summary>Завершить раунд как игра (экран Round Ended, рестарт через 10 с).</summary>
        public static void End() => RM?.cpc();
    }
}
