using Mirror;
using UnityEngine;

namespace Unicorn.Internal
{
    /// <summary>
    /// ЕДИНСТВЕННОЕ место, где упоминаются обфусцированные имена игры (GUPS Obfuscator).
    /// После апдейта игры: пересдампить, обновить имена тут — остальной фреймворк и плагины не трогаются.
    /// Сборка игры: SCP Project 90, Unity 2022.3.16f1, build-guid 41df4bd797234d67a8f33c7846379e12.
    /// Настоящие имена восстановлены по строкам Mirror Weaver ("[Server] function '...::HurtPlayer(...)'" и т.п.).
    /// </summary>
    internal static class Game
    {
        // ---- Имена для Harmony-патчей ----
        // PlayerStatistics
        public const string PS_HurtPlayer = "cjd";                 // void HurtPlayer(GameObject target, int damage, string hitInfo, bool, bool)  (сервер)
        public const string PS_UserCode_CmdSetSteamData = "ckl";   // void UserCode_CmdSetSteamData(ulong steamId, string nick)   (сервер, клиент прислал свои данные)
        // CharacterClassManager
        public const string CCM_Prop_CurClass = "mzo";             // SyncVar property Networkcurclass (get bmr / set bms)
        // RoundManager
        public const string RM_Prop_Info = "ndo";                  // SyncVar property Networkinfo (get cpm / set cpn): "Waiting for round start" / "started" / "ending"
        // Mirror.NetworkManager
        public const string NM_OnServerDisconnect = "gtw";         // virtual void OnServerDisconnect(NetworkConnectionToClient)
        // ServerConsole
        public const string SC_HandleCommand = "bhm";
        // CustomNetworkManager.OnServerConnect(conn) — проверка бана; в игре падает NRE на dedicated (NetworkServer.localConnection == null)
        public const string CNM_OnServerConnect = "bph";              // void HandleCommand(string line)  (главный поток, Update)

        // Door
        public const string Door_ServerRequestChangeState = "bqx"; // void ServerRequestChangeState(GameObject player) — игрок нажал на дверь (сервер)
        // HandCuffs (компонент на том, КТО надевает)
        public const string HC_UserCode_CmdCuffPlayer = "cbh";     // void UserCode_CmdCuffPlayer(Inventory target)
        public const string HC_UserCode_CmdUnCuffPlayer = "cbf";   // void UserCode_CmdUnCuffPlayer(Inventory target)
        // Inventory
        public const string Inv_Prop_Cuffed = "nbd";               // SyncVar Networkcuffed
        public const string Inv_Prop_Items = "nba";                // SyncVar Network_currentItems (int[] слотов, значение = индекс в _items)
        public const string Inv_UserCode_CmdCollect = "bxn";       // void UserCode_CmdCollect(NetworkIdentity pickup)
        public const string Inv_ServerDropItem = "bwm";            // [Server] ServerDropItem(int slot, float) — UserCode_CmdDropItem встроен в invoker и прыгает сюда

        // PlayerTransformSync
        // ВНИМАНИЕ: UserCode_CmdSyncData (cld) компилятор IL2CPP встроил в invoker — патчим invoker.
        public const string PTS_InvokeCmdSyncData = "cle";         // static void InvokeUserCode_CmdSyncData(NetworkBehaviour, NetworkReader, NetworkConnectionToClient)

        // ZoneGenerator
        public static bool GenerateZone(this ZoneGenerator zg, int key) => zg.baw(key);             // генерация зоны по ключу (в игре — только из SyncVar-хука _key)

        // GameRoles — роль/цвет/бейдж, которые игра показывает в списке игроков (Tab) и в лобби:
        // "<color=#{myColor}>{myBadgeText}</color> {myNick}"
        // Использование предметов: static invoker'ы Command (UserCode встроен в них)
        public const string Medkit_InvokeHealMe = "cbt";           // Medkit: InvokeUserCode_HealMe
        public const string Scp420j_InvokeDelete = "cbm";          // Scp420j: InvokeUserCode_CmdDeleteItem (выкурил)
        public const string Scp458_InvokeHealMe = "cxn";           // Scp458Item: InvokeUserCode_HealMe (съел пиццу)
        public const string Scp294Cup_InvokeEffect = "cco";        // Scp294Cup: InvokeUserCode_CmdDoRandomEffect (выпил)
        public const string Scp714_InvokeWear = "cxw";             // Scp714: InvokeUserCode_CmdSetWearingRing(bool)
        // Аудит админ-панели
        public const string PA_UserCode_ExecuteAdminAction = "cfj"; // PlayerAdmin: UserCode_CmdExecuteAdminAction(string action, string arg, GameObject target, conn) — вызывается из invoker'а cfk
        public const string PA_TargetResponse = "cer";             // PlayerAdmin: TargetResponse(conn, string) — ответ админу
        public const string RM_InvokeRoundLockOn = "cpt";          // RoundManager: invoker CmdSetRoundLockOn
        public const string RM_InvokeRoundLockOff = "cpv";         // RoundManager: invoker CmdSetRoundLockOff
        public const string RM_InvokeForceRoundStart = "cpx";      // RoundManager: invoker CmdForceRoundStart
        public const string CCM_InvokeAddItem = "bns";             // CCM: invoker CmdAddItem(Inventory, int)
        public const string CCM_InvokeClearInv = "bno";            // CCM: invoker CmdClearInv(Inventory)
        // Античит: invoker'ы Command (static, NetworkBehaviour a, NetworkReader b, conn c)
        public const string CCM_InvokeSetImDedicated = "bmy";     // CmdSetImDedicatedServer — обычный клиент не шлёт никогда
        public const string CCM_InvokeSp = "bnc";                 // CmdSp(Vector3) — позиция при появлении
        public const string CCM_InvokeSpD = "bnw";                // CmdSpD() — отложенная позиция при появлении
        public const string CCM_InvokeForceInSpectator = "bng";   // CmdForceInSpectator(GameObject)
        public const string CCM_InvokeSetDead = "bni";            // CmdSetDead(bool)
        public const string CCM_InvokeSetAmmo = "bnu";            // CmdSetAmmo(int)
        public const string ZG_InvokeReGenerateMap = "bbf";       // ZoneGenerator: invoker ReGenerateMap() (rA=0)
        public const string Door_InvokeCloseDoor = "bro";         // Door: invoker CmdCloseDoor() (rA=0)
        public const string PS_InvokeSetHP = "ckq";               // PlayerStatistics.CmdSetHP(int)
        public const string RMM_InvokeSetBool = "cqi";            // RoundModeManager.CmdSetBool(bool) — зомби-режим
        public const string Inv_InvokeSendInvToAdmin = "byi";     // Inventory.CmdSendInventoryToAdmin
        public const string GM_InvokeSpawnGrenade = "bvb";        // GrenadeManager.CmdSpawnGrenade(Vector3)
        // Оружие
        public const string WM_ServerShoot = "dcw";               // WeaponManager: [Server] обработка выстрела (Vector3 origin, Vector3 dir) — зовётся из invoker'а CmdShoot (dcx)
        public const string WM_InvokeStartReload = "ddb";         // WeaponManager: invoker CmdStartReload
        public const string SM_UserCode_Cmd049Force = "czk";     // ScpManager: UserCode_Cmd049Force(NetworkIdentity) — НАСТОЯЩИЙ удар 049 в этой версии → ForceClass(цель, 13)
        public const string S049_UserCode_HurtPlayer = "cte";     // Scp049Player: UserCode_CmdHurtPlayer(NetworkIdentity) — проверки и ForceClass(цель, 13)
        public const string GR_AssignRoleBySteamId = "bdb";        // [Server] UserCode_CmdAssignRoleBySteamId — ставит роль из префаба (или пустые) при входе
        public static void SetRoleName(this GameRoles gr, string v) => gr.myh = v;           // NetworkmyRole
        public static void SetRoleColor(this GameRoles gr, string v) => gr.myi = v;          // NetworkmyColor (HEX без #)
        public static void SetRoleBadge(this GameRoles gr, string v) => gr.myj = v;          // NetworkmyBadgeText
        public static void SetAdminToolsAccess(this GameRoles gr, bool v) => gr.myk = v;     // NetworkhasAccessToAdminTools

        // ---- Вызовы ----
        public static bool IsServer(this NetworkBehaviour nb) => nb.nja;                 // NetworkBehaviour.isServer
        public static gu ConnectionToClient(this NetworkBehaviour nb) => nb.njj;        // NetworkBehaviour.connectionToClient
        public static NetworkIdentity Identity(this gt conn) => conn.llq;               // NetworkConnection.identity
        public static void Disconnect(this gt conn) => conn.ggj();                      // NetworkConnection.Disconnect()
        public static string Address(this gu conn) => conn.llw;                         // NetworkConnectionToClient.address (SteamID64 для Fizzy)
        public static bool IsBanned(string steamId) => s.bdq(steamId);                 // BanManager.IsBanned (Bans.json)
        public static void SendBanReason(this gt conn, string reason) => conn.gos(new BanReasonMessage { reason = reason }, 0);

        public static void SetHealth(this PlayerStatistics ps, int hp) => ps.ncj = hp;  // NetworkcurHP
        public static void SetCurClassRaw(this CharacterClassManager ccm, int id) => ccm.mzo = id;
        public static void SetUseClassPos(this CharacterClassManager ccm, bool v) => ccm.mzq = v; // NetworkuseClassPos
        public static void TargetKick(this CharacterClassManager ccm, gt conn, string reason) => ccm.blq(conn, reason);

        public static void ServerSetPosition(this PlayerTransformSync pts, Vector3 pos) => pts.ckw(pos);

        // PlayerStatistics
        public static void SetHpAmount(this PlayerStatistics ps, int hp) => ps.cjl(hp);                 // [Server] SetHPAmount (ставит и isDead)
        public static void Hurt(this PlayerStatistics attacker, GameObject target, int dmg, string info) => attacker.cjd(target, dmg, info, true, false);
        public static string GetBadgeText(this PlayerStatistics ps) => ps.badgeText;
        public static void SetBadgeText(this PlayerStatistics ps, string v) => ps.ncp = v;              // NetworkbadgeText
        public static string GetBadgeColor(this PlayerStatistics ps) => ps.badgeColorHex;
        public static void SetBadgeColor(this PlayerStatistics ps, string v) => ps.ncq = v;             // NetworkbadgeColorHex
        public static void SetRolePlayDetails(this PlayerStatistics ps, string v) => ps.ncr = v;        // NetworkrolePlayDetails
        public static void SetNick(this PlayerStatistics ps, string v) => ps.ncl = v;                   // NetworkmyNick
        public static void SetSteamNick(this PlayerStatistics ps, string v) => ps.nco = v;
        public static void RpcCupMessage(this Scp294Cup cup, string text) => cup.cch(text);           // Scp294Cup.RpcMessage: текст 5 с, показывает только владелец (isLocalPlayer)              // NetworkmySteamNick (строка 1 в тексте при наведении)

        // Inventory
        public static void AddItem(this Inventory inv, int itemId) => inv.bvy(itemId);                 // [Server] AddItem(int)
        public static void DeleteItem(this Inventory inv, int slot) => inv.bvw(slot);                  // [Server] ServerDeleteItem(int slot)
        public static void ClearItems(this Inventory inv) => inv.bvx();                                // [Server] ServerClearInventory()
        public static void DropAllItems(this Inventory inv) => inv.bvz();                              // [Server] ServerDropAllItems()
        public static void DropItem(this Inventory inv, int slot) => inv.bwm(slot, 0f);                // [Server] ServerDropItem(int slot, float)
        public static void SetCuffedRaw(this Inventory inv, bool v) => inv.nbd = v;                    // Networkcuffed
        public static void CuffedLocal(this Inventory inv, bool v) => inv.bvp(v);                      // локальная логика "Cuffed" (как в игре при надевании)
        public static void RpcCuffEffect(this Inventory inv) => inv.bvt();                             // RpcSyncCuffEffect()
        public static void SetCuffer(this HandCuffs hc, Inventory target) => hc.nbl = target;          // NetworkcurrentCuffedPlayer
        public static void RpcSetAnimBool(this AnimationController ac, string name, bool v) => ac.bjw(name, v); // RpcSetBool

        // Door
        public static void SetOpen(this global::Door d, bool v) => d.nac = v;                         // NetworkisOpen (hook OnDoorStateChanged анимирует у клиентов)
        public static bool IsLockedRaw(this global::Door d) => d.nad;                                 // private SyncVar locked (0x71)
        public static void SetLocked(this global::Door d, bool v) => d.nad = v;
        public static bool IsDestroyedRaw(this global::Door d) => d.nae;                              // Networkdestroyed
        public static void RpcPlaySound(this global::Door d, bool open) => d.bqq(open);
        public static void ServerDestroy(this global::Door d) => d.brj();                             // UserCode_CmdDestroyDoor
        public static void RpcButtonBlink(this global::Door d) => d.bqj();                           // RpcAnimateButtonBlink
        public static void RpcButtonBlinkRed(this global::Door d) => d.bqh();                        // RpcAnimateButtonBlinkRed
        public static void RpcAccepted(this global::Door d) => d.bqt();                              // RpcAccepted (звук «доступ разрешён»)
        public static void RpcDenied(this global::Door d) => d.bqr();                                // RpcDenied (звук «отказано»)

        public static void RpcDoBroadcast(this PlayerAdmin pa, string text) => pa.cfh(text);

        // Steamworks.SteamGameServer (обфусцирован как Steamworks.dq)
        public static bool SteamGS_LoggedOn() => Steamworks.dq.dsv();          // BLoggedOn
        public static bool SteamGS_Secure() => Steamworks.dq.dsw();            // BSecure
        public static ulong SteamGS_SteamId() => Steamworks.dq.dsx().m_SteamID; // GetSteamID
        public static void SteamGS_LogOnAnonymous() => Steamworks.dq.dst();     // LogOnAnonymous

        // CustomNetworkManager.OnSteamServersConnected(SteamServersConnected_t) — поднимает Mirror-сервер и включает показ в браузере
        public static void CNM_OnSteamServersConnected(this CustomNetworkManager cnm) => cnm.bpb(new Steamworks.SteamServersConnected_t());

        // Раздача ролей на старте раунда. Игра вызывает её из ForceRoundStart только для ХОСТ-игрока
        // (bln -> bls, внутри проверка isLocalPlayer && isServer). На dedicated хоста нет — роли никто не раздаёт.
        public static void DistributeRoles(this CharacterClassManager ccm)
        {
            var id = ccm.GetComponent<NetworkIdentity>();
            bool old = id.lmn;                 // NetworkIdentity.isLocalPlayer (backing field)
            id.lmn = true;
            try { StartQueueFix.Ensure(ccm); } catch (System.Exception e) { Unicorn.API.Log.Warn($"[StartQueueFix] {e.Message}"); }
            try { ccm.bls(); }                 // SetRandomRoles: ForceClass всем из PlayerManager
            finally { id.lmn = old; }
        }

        public static void SetRoundLock(this RoundManager rm, bool v) => rm.ndp = v;    // NetworkroundLock
        public static void ForceRoundStart(this RoundManager rm) => rm.cpe();          // [Server] ForceRoundStart()
    }
}
