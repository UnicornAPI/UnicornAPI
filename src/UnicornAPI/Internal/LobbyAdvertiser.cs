using System;
using Unicorn.API;
using Steamworks;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// Игровой список серверов — это Steam-лобби (не Steam Server Browser). Лобби создаёт SteamLobby.CreateLobby,
    /// который игра вызывает только по кнопке «создать матч» в меню. На dedicated этого не происходит —
    /// сервер в списке не виден. Вызываем тот же метод игры после старта сервера и ставим своё (цветное) название.
    /// </summary>
    internal static class LobbyAdvertiser
    {
        private static bool _requested;
        private static float _requestedAt = -1f;

        public static ulong LobbyId
        {
            get { try { return UnityEngine.Object.FindObjectOfType<SteamLobby>()?.currentLobbyId.m_SteamID ?? 0; } catch { return 0; } }
        }

        /// <summary>После старта сервера (DedicatedFix).</summary>
        public static void OnServerStarted()
        {
            if (!CoreConfig.Current.ShowInServerList) { Log.Info("[Lobby] показ в списке серверов выключен (unicorn.json)"); return; }
            Timing.CallDelayed(5f, () => Create());
        }

        public static string Create()
        {
            var sl = UnityEngine.Object.FindObjectOfType<SteamLobby>();
            if (sl == null) { Log.Error("[Lobby] SteamLobby не найден — не могу создать лобби"); return "SteamLobby не найден"; }
            _requested = true; _requestedAt = Time.realtimeSinceStartup;
            try { sl.bhp(); } // SteamLobby.CreateLobby(): Steam создаст лобби, игра заполнит HostAddress/AppVersion/LobbyName
            catch (Exception e) { Log.Error($"[Lobby] {e}"); return e.Message; }
            Log.Info("[Lobby] запрос на создание лобби отправлен в Steam");
            Timing.CallDelayed(10f, Check);
            return "запрос отправлен, результат через ~10 с в логе";
        }

        private static void Check()
        {
            var id = LobbyId;
            if (id == 0) { Log.Warn("[Lobby] лобби так и не создалось (Steam не ответил?). Команда: lobby create"); return; }
            ApplyName();
            Log.Info($"[Lobby] сервер в игровом списке серверов (лобби {id}), название: {CurrentName()}");
        }

        /// <summary>Поставить название из unicorn.json (если задано).</summary>
        public static bool ApplyName()
        {
            var name = CoreConfig.Current.ServerListName;
            var id = LobbyId;
            if (string.IsNullOrWhiteSpace(name) || id == 0) return false;
            return SteamMatchmaking_SetData(id, "LobbyName", name.Replace("\n", " "));
        }

        public static string CurrentName()
        {
            var id = LobbyId;
            if (id == 0) return null;
            try { return eg.env(new CSteamID { m_SteamID = id }, "LobbyName"); } catch { return null; }
        }

        // ---------------- Сторож ----------------
        // Лобби живёт, пока сервер (его Steam-аккаунт) в нём состоит. Если связь со Steam моргнула, Steam выкидывает
        // сервер из лобби — оно пропадает из списка, а сам сервер и игроки продолжают играть. Раз в минуту проверяем,
        // что сервер всё ещё в своём лобби; две проверки подряд «нет» — создаём новое.
        // ВАЖНО: игровой SteamLobby.CreateLobby (bhp) сначала ОСТАНАВЛИВАЕТ сервер Mirror (кикнет всех) — поэтому здесь
        // создаём лобби напрямую через SteamMatchmaking.CreateLobby; данные лобби (адрес, версия, название) заполнит
        // обработчик игры LobbyCreated (bhq), как при обычном создании. Его StartServer видит, что сервер уже запущен, и ничего не делает.

        private static float _nextCheck, _lastRecreate = -999f, _lastErr = -999f, _steamOffSince = -1f;
        private static int _misses, _lastLimit;
        public static int Recreated { get; private set; }

        internal static void Tick()
        {
            if (!Application.isBatchMode || !_requested) return;
            var cfg = CoreConfig.Current;
            if (!cfg.ShowInServerList || !cfg.LobbyWatchdog) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextCheck) return;
            _nextCheck = now + Math.Max(20f, cfg.LobbyWatchdogIntervalSeconds);
            if (now - _requestedAt < 90f) return;          // первое создание ещё в процессе
            try { CheckAlive(now); }
            catch (Exception e) { if (now - _lastErr > 600f) { _lastErr = now; Log.Warn($"[Lobby] сторож: {e.Message}"); } }
        }

        private static void CheckAlive(float now)
        {
            bool steamOn = ew.fdo();                          // SteamUser.BLoggedOn
            if (!steamOn)
            {
                if (_steamOffSince < 0f) { _steamOffSince = now; Log.Warn("[Lobby] Steam не в сети — жду переподключения"); }
                _misses = 0;
                return;
            }
            if (_steamOffSince >= 0f) { Log.Info($"[Lobby] Steam снова в сети (не было {now - _steamOffSince:0} с)"); _steamOffSince = -1f; }

            ulong id = LobbyId;
            int members = 0, limit = 0; ulong owner = 0;
            if (id != 0)
            {
                var c = new CSteamID { m_SteamID = id };
                members = eg.ent(c);                          // GetNumLobbyMembers: 0, если мы не в лобби
                limit = eg.eoi(c);
                try { owner = eg.eol(c).m_SteamID; } catch { }
            }
            if (id != 0 && members > 0) { _misses = 0; if (limit > 0) _lastLimit = limit; return; }

            _misses++;
            ulong self = 0; try { self = ew.fdp().m_SteamID; } catch { }
            Log.Warn($"[Lobby] сервера нет в своём лобби ({_misses}/2): lobby={id} участников={members} владелец={owner} сервер={self}");
            if (_misses < 2) return;
            if (now - _lastRecreate < 180f) return;           // не чаще раза в 3 минуты
            Log.Warn("[Lobby] лобби пропало — пересоздаю (игроков не трогаю)");
            Recreate();
        }

        /// <summary>Создать лобби заново, НЕ останавливая сервер (игроки остаются). Для сторожа и команды lobby create.</summary>
        public static string Recreate()
        {
            var sl = UnityEngine.Object.FindObjectOfType<SteamLobby>();
            if (sl == null) return "SteamLobby не найден";
            _requested = true; _misses = 0; _lastRecreate = Time.realtimeSinceStartup;
            if (_requestedAt < 0f) _requestedAt = _lastRecreate;
            ulong old = LobbyId;
            if (old != 0) { try { eg.enr(new CSteamID { m_SteamID = old }); } catch { } }   // LeaveLobby старого (если ещё в нём)
            var type = sl.lobbyType;
            if ((int)type == 0) type = (ELobbyType)2;         // Private → Public
            int max = MaxPlayersFromConfig();
            try { eg.enp(type, max); }                          // SteamMatchmaking.CreateLobby
            catch (Exception e) { Log.Error($"[Lobby] CreateLobby: {e.Message}"); return e.Message; }
            Recreated++;
            Log.Info($"[Lobby] запрос нового лобби отправлен (тип {(int)type}, мест {max})");
            Timing.CallDelayed(10f, Check);
            return "запрос отправлен, сервер не перезапускается; результат через ~10 с в логе";
        }

        private static int MaxPlayersFromConfig()
        {
            try
            {
                var path = System.IO.Path.Combine(Application.persistentDataPath, "Config", "YourServerConfig.json");
                if (System.IO.File.Exists(path))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(path), "\"MaxPlayers\"\\s*:\\s*\"?(\\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0) return n;
                }
            }
            catch { }
            return _lastLimit > 0 ? _lastLimit : 30;
        }

        public static int Members(out int limit)
        {
            limit = 0; var id = LobbyId; if (id == 0) return 0;
            try { var c = new CSteamID { m_SteamID = id }; limit = eg.eoi(c); return eg.ent(c); } catch { return 0; }
        }

        private static bool SteamMatchmaking_SetData(ulong lobby, string key, string value)
        {
            try { return eg.enw(new CSteamID { m_SteamID = lobby }, key, value); } // SteamMatchmaking.SetLobbyData
            catch (Exception e) { Log.Error($"[Lobby] SetLobbyData: {e.Message}"); return false; }
        }
    }
}
