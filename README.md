# UnicornAPI

**Серверный фреймворк плагинов для SCP: Project 90** - аналог EXILED / LabAPI из SCP: Secret Laboratory.

Работает поверх BepInEx 6 (IL2CPP), файлы игры не изменяет. Делает из «голого» выделенного сервера игры нормальную платформу: чинит баги игры в режиме dedicated, даёт удобный API на C# с событиями и командами консоли, грузит плагины, хранит данные в SQLite.

> Версия **1.0.0**. Раньше назывался **P1-Sun**, история версий - в [CHANGELOG.md](CHANGELOG.md).
> Собран под игру **0.4.2 [F05]** (Unity 2022.3.16f1, SHA-256 GameAssembly.dll `ba69d16edce7…`).

---

## Содержание

1. [Что умеет](#что-умеет)
2. [Установка на сервер](#установка-на-сервер)
3. [Структура файлов](#структура-файлов)
4. [Настройки (unicorn.json)](#настройки-unicornjson)
5. [Команды консоли](#команды-консоли)
6. [Пишем плагин: быстрый старт](#пишем-плагин-быстрый-старт)
7. [Справочник API](#справочник-api)
   - [Plugin, Paths, Log](#plugin-paths-log)
   - [Player](#player)
   - [Инвентарь, предметы, предметы на полу](#инвентарь-предметы-предметы-на-полу)
   - [Оружие и патроны](#оружие-и-патроны)
   - [Server, Round, роли](#server-round-роли)
   - [Двери](#двери)
   - [Карта: зоны, свет, лифты](#карта-зоны-свет-лифты)
   - [Трупы](#трупы)
   - [Timing - таймеры](#timing--таймеры)
   - [Database - SQLite](#database--sqlite)
   - [Profiles - оформление игроков](#profiles--оформление-игроков)
   - [Команды](#команды)
8. [События](#события)
9. [Что UnicornAPI чинит в игре](#что-unicornapi-чинит-в-игре)
10. [Официальные плагины](#официальные-плагины)
11. [Сборка](#сборка)
12. [После обновления игры](#после-обновления-игры)
13. [Правила и ограничения](#правила-и-ограничения)
14. [Лицензия](#лицензия)

---

## Что умеет

- **Плагины без рестарта сервера.** DLL кладутся в `UnicornAPI/Plugins`, команда `reload` перезагружает их на лету.
- **События** игроков, раунда, предметов, дверей, оружия, SCP. Многие можно отменить или изменить: урон, смену роли, выстрел, подбор предмета, результат SCP-914 и т.д.
- **API игрока:** HP, роль, позиция, инвентарь, оружие, патроны, наручники, бейдж и ник в Tab, личные сообщения на экране, кик.
- **Управление сервером:** броадкасты, раунд (старт, рестарт, блокировка), двери, свет по зонам, лифты, предметы на полу.
- **Команды консоли** для плагинов. Работают из консоли сервера и с сайта через плагин WebBridge.
- **SQLite-база** для плагинов и сохранённое оформление игроков (бейджи, ники, админ-панель).
- **Конфиги плагинов** в JSON, создаются сами с настройками по умолчанию.
- **Фиксы игры:** без UnicornAPI выделенный сервер игры почти не работает - не стартует сеть, не раздаются роли, не открываются двери, раунд не перезапускается. Список - в разделе [Что UnicornAPI чинит](#что-unicornapi-чинит-в-игре).
- **Проверка версии игры:** если игра обновилась и имена в коде поменялись, UnicornAPI предупредит или не станет ставить патчи.
- **Работа с античитом P90Guard** (отдельный плагин): если он установлен, его срабатывания идут в события UnicornAPI, а его команды доступны в консоли.

---

## Установка на сервер

1. Установить **BepInEx 6 IL2CPP be.788** в папку игры (рядом с `SCP Project 90.exe`):
   https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip
2. Распаковать архив UnicornAPI в ту же папку.
3. Настроить игру: `%USERPROFILE%\AppData\LocalLow\ydanovvv\Scp project 90\Config\YourServerConfig.json` - название, описание, лимит игроков, `FriendsOnlyServer: "false"`.
4. Запустить Steam под **отдельным серверным аккаунтом** с игрой и запустить сервер (`SCP Project 90.exe -batchmode -nographics` или прилагаемый `.bat`).
5. **Первый запуск долгий (1-3 минуты):** BepInEx генерирует `BepInEx/interop`. Дальше быстро.
6. В консоли: `sun` - версия и список плагинов.

Важно знать про выделенный сервер этой игры:

- Игроки подключаются **по SteamID64 серверного Steam-аккаунта**, порты открывать не нужно: трафик идёт через Steam. С того же аккаунта, что и сервер, зайти нельзя.
- В игровом списке серверов сервер виден через Steam-лобби, которое UnicornAPI создаёт сам. Название - `ServerListName` в `unicorn.json`.

Удалить фреймворк: удалить `BepInEx/plugins/UnicornAPI` (а весь BepInEx - файл `winhttp.dll`). Игра снова ванильная.

**Переезд с P1-Sun:** при первом запуске UnicornAPI сам переносит папку `P1-Sun` в `UnicornAPI` (конфиги, базу, плагины). Старый `BepInEx/plugins/P1Sun` нужно удалить: если он остался, UnicornAPI отключит его и попросит перезапуск. Плагины под P1-Sun нужно пересобрать (`using Unicorn.API` вместо `using P1Sun.API`).

---

## Структура файлов

```
SCP Project 90/
├─ winhttp.dll, doorstop_config.ini, dotnet/     загрузчик BepInEx
├─ BepInEx/
│  ├─ plugins/UnicornAPI/UnicornAPI.dll           сам фреймворк (+ библиотеки SQLite рядом)
│  └─ LogOutput.log                               лог сервера и плагинов
└─ UnicornAPI/
   ├─ unicorn.json            настройки UnicornAPI
   ├─ unicorn.db              база SQLite (профили игроков, данные плагинов)
   ├─ Plugins/                DLL плагинов
   ├─ Configs/                <ИмяПлагина>.json - конфиги плагинов, создаются сами
   ├─ Dependencies/           общие библиотеки, нужные плагинам (грузятся до плагинов)
   ├─ console_filter.txt      подстроки мусорных сообщений, которые не показывать в консоли
   └─ disabled_patches.txt    имена патчей, которые не ставить (для диагностики)
```

---

## Настройки (unicorn.json)

| Поле | По умолчанию | Что делает |
|---|---|---|
| `ShowInServerList` | `true` | показывать сервер в игровом списке (создавать Steam-лобби) |
| `ServerListName` | `<color=#FF3B3B>FLX</color> <color=#9A9A9A>\|</color> Project 90` | название в списке, поддерживает цвета `<color=#…>…</color>`. Пусто: `ServerName` из конфига игры |
| `LobbyWatchdog` | `true` | сторож лобби: если сервер выпал из списка, создать лобби заново без рестарта |
| `LobbyWatchdogIntervalSeconds` | `60` | как часто проверять |
| `RoundLockDefault` | `false` | блокировка раунда при старте сервера |
| `ResetRoundLockEachRound` | `true` | в каждом новом раунде возвращать блокировку к `RoundLockDefault` |
| `AutoUnlockAtPlayers` | `3` | игра сама блокирует раунд, если на старте ≤2 игроков; снимать такую блокировку при этом числе игроков (0 - не снимать) |
| `CloseDoorsOnRestart` | `true` | закрывать все двери при рестарте раунда |
| `UnlockDoorsOnRestart` | `true` | отпирать запертые двери при рестарте раунда |
| `GameHashCheck` | `"warn"` | проверка версии игры: `warn` - предупредить, `safe` - не ставить патчи на чужой версии, `off` - не проверять |
| `AllowedGameHashes` | `[]` | дополнительные разрешённые хэши `GameAssembly.dll` |

---

## Команды консоли

`<игрок>` - SteamID64 или часть ника. `<игрок|SteamID>` - можно и игрока, которого нет на сервере.

**Игроки**

| Команда | Что делает |
|---|---|
| `players` | список: ник, SteamID, роль, HP |
| `kick <игрок> [причина]` | кик с окном причины |
| `msg <игрок\|all> <текст>` | личное сообщение на экране (~5 с), видит только адресат |
| `bc [сек] <текст>` / `clearbc` | броадкаст всем (7 с по умолчанию, 0 - навсегда) / убрать |
| `setrole <игрок> <ID\|название> [notp]` | сменить роль (173, ntf, chaos, d, guard, sci, spec…); `notp` - без телепорта |
| `roles` | список ролей с ID |
| `hp <игрок> <n>` / `heal <игрок> [n]` / `damage <игрок> <n> [причина]` / `kill <игрок>` | здоровье |
| `tp <игрок> <x y z \| игрок>` / `pos <игрок>` | телепорт / координаты |
| `cuff <игрок> [кто_ведёт]` / `uncuff <игрок>` | наручники |

**Оформление (сохраняется в базе)**

| Команда | Что делает |
|---|---|
| `badge <игрок\|SteamID> <HEX> <текст>` / `... off` | бейдж после ника в Tab (цвет красит ник и бейдж) |
| `nick <игрок\|SteamID> <ник>` / `... reset` | показываемый ник (Tab, трупы, убийства) |
| `admintools <игрок\|SteamID> <on\|off\|reset>` | встроенная админ-панель игры |
| `profile <игрок\|SteamID>` / `profiles` | сохранённое оформление |

**Предметы и оружие**

| Команда | Что делает |
|---|---|
| `items` | все предметы с ID |
| `give <игрок> <ID\|название>` / `inv <игрок>` | выдать / посмотреть инвентарь |
| `removeitem <игрок> <слот>` / `clearinv <игрок>` / `dropall <игрок>` | удалить / очистить / выбросить всё |
| `weapons` / `weapon <игрок>` | всё оружие с характеристиками / оружие в руках |
| `wset <оружие\|ID> <damage\|mag> <значение>` | урон или магазин оружия у всех (до рестарта) |
| `ammo <игрок> [<тип> <set\|add\|take> <n>]` | патроны |

**Раунд и карта**

| Команда | Что делает |
|---|---|
| `round` / `round start\|restart\|end\|lock\|unlock` | состояние и управление раундом |
| `giveroles` | раздать роли заново |
| `doors [фильтр]` / `doorinfo <игрок>` | список дверей / диагностика ближайшей |
| `door <open\|close\|lock\|unlock\|destroy> <имя\|all\|near:игрок>` | управление дверьми |
| `grenades` / `grenades clean` | сколько гранат-снарядов на карте / удалить все |

**Сервер**

| Команда | Что делает |
|---|---|
| `sun` (`unicorn`) | версия и плагины |
| `id` / `steam [relogin]` | ID для подключения / статус Steam |
| `lobby` / `lobby create` / `lobby name <название>` | игровой список серверов |
| `reload` | перезагрузить плагины без рестарта |
| `ac` | статус античита P90Guard |
| `pg …`, `cheatban`, `cheatunban`, `cheatcheck` | команды P90Guard (если установлен) |
| `netstat` / `confilter` | сетевая диагностика / перечитать фильтр консоли |

Команды официальных плагинов (`xp`, `decont`, `scp035`, `web`) - в разделе [Официальные плагины](#официальные-плагины).

---

## Пишем плагин: быстрый старт

**1. Проект.** Библиотека .NET 6 со ссылкой на `UnicornAPI.dll`. Проще всего взять за основу любой официальный плагин: в каждом репозитории лежит готовый `Directory.Build.props`, который сам находит BepInEx, сборки игры и `UnicornAPI.dll` в папке игры. Тогда `.csproj` плагина совсем короткий:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MyPlugin</AssemblyName>
    <UnicornPlugin>true</UnicornPlugin>   <!-- не подключать Assembly-CSharp/Mirror: нет конфликтов имён вроде Team -->
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

Без `Directory.Build.props` ссылку добавляют вручную:

```xml
<Reference Include="UnicornAPI">
  <HintPath>D:\Steam\steamapps\common\SCP Project 90\BepInEx\plugins\UnicornAPI\UnicornAPI.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Ещё один пример с комментариями: `examples/HelloUnicorn` в этом репозитории.

Нужен прямой доступ к классам игры - убери `UnicornPlugin` (тогда подключится Assembly-CSharp, и игровые типы пишутся как `global::Door`, `global::Team`).

**2. Код.**

```csharp
using System.Collections.Generic;
using Unicorn.API;
using Unicorn.Commands;
using Unicorn.Events;

namespace MyPlugin
{
    public sealed class Config
    {
        public bool Enabled = true;
        public string WelcomeText = "Добро пожаловать, {nick}!";
        public float ClassDDamageMultiplier = 1.5f;
    }

    public sealed class MyPlugin : Plugin
    {
        public override string Name => "MyPlugin";
        public override string Author => "Dolph";

        private Config _cfg;
        private TimerHandle _timer;

        public override void OnEnabled()
        {
            _cfg = LoadConfig<Config>();                       // UnicornAPI/Configs/MyPlugin.json
            if (!_cfg.Enabled) return;

            PlayerEvents.Joined += OnJoined;
            PlayerEvents.Hurting += OnHurting;
            ServerEvents.RoundStarted += OnRoundStarted;

            _timer = Timing.CallRepeating(60f, () => Server.Broadcast("Не забудьте зайти в наш Discord!", 5f));

            RegisterCommand(new SimpleCommand("hello", "hello <игрок> - поприветствовать", (string[] a, out string r) =>
            {
                var p = a.Length > 0 ? Player.Find(a[0]) : null;
                if (p == null) { r = "игрок не найден"; return false; }
                p.ShowMessage($"<color=#FFC940>Привет, {p.Nickname}!</color>");
                r = "отправлено";
                return true;
            }));
        }

        public override void OnDisabled()
        {
            PlayerEvents.Joined -= OnJoined;
            PlayerEvents.Hurting -= OnHurting;
            ServerEvents.RoundStarted -= OnRoundStarted;
            _timer?.Cancel();
        }

        private void OnJoined(JoinedEventArgs e) =>
            e.Player.ShowMessage(_cfg.WelcomeText.Replace("{nick}", e.Player.Nickname));

        private void OnHurting(HurtingEventArgs e)
        {
            if (e.Attacker != null && e.Attacker != e.Player && e.Attacker.Team == Team.ClassD)
                e.Amount = (int)(e.Amount * _cfg.ClassDDamageMultiplier);
        }

        private void OnRoundStarted() =>
            Timing.CallDelayed(5f, () => Server.Broadcast("Раунд начался, удачи!"));
    }
}
```

**3. Установка.** Собрать, положить `MyPlugin.dll` в `UnicornAPI/Plugins`, ввести `reload` (или перезапустить сервер). В логе появится `Плагин включён: MyPlugin`.

**Правила:**
- Все события и таймеры вызываются **на главном потоке игры**. Исключение `ServerEvents.ConsoleLine` - он может прийти с любого потока.
- Исключение в обработчике логируется и **не роняет сервер** и другие плагины.
- При `reload` UnicornAPI сам снимает все обработчики событий, таймеры и команды выгружаемого плагина. `OnDisabled` всё равно стоит писать аккуратно.
- Порядок включения плагинов - по `Priority` (меньше - раньше).

---

## Справочник API

Пространства имён: `Unicorn.API`, `Unicorn.Events`, `Unicorn.Commands`.

### Plugin, Paths, Log

```csharp
public abstract class Plugin
{
    string Name { get; }                 // обязательно
    string Author { get; }               // обязательно
    Version Version { get; }             // по умолчанию - версия сборки
    int Priority { get; }                // порядок включения, по умолчанию 0
    void OnEnabled();  void OnDisabled();
    protected T LoadConfig<T>();         // UnicornAPI/Configs/<Name>.json; нет файла - создаётся с дефолтами,
                                         // новые поля дописываются сами; поддерживает и поля, и свойства
    protected void RegisterCommand(ICommand cmd);
}

Paths.Root / Paths.Plugins / Paths.Configs   // пути к папкам UnicornAPI
Log.Info(x) / Log.Warn(x) / Log.Error(x) / Log.Debug(x)
```

### Player

Обёртка над игроком. Существует, пока жив его объект на сервере.

**Поиск**

| Член | Описание |
|---|---|
| `Player.List` | все игроки, прошедшие `Joined` (прислали SteamID) |
| `Player.Get(steamId)` / `Player.Get(GameObject)` / `Player.Get(Component)` | по SteamID64 или объекту |
| `Player.Find("ник или SteamID")` | поиск по SteamID64 или части ника |

**Свойства**

| Член | Описание |
|---|---|
| `Nickname` | настоящий ник из Steam |
| `DisplayNickname` (get/set) | показываемый ник (Tab, трупы, убийства); `null` - вернуть ник из Steam |
| `SteamId` | SteamID64 |
| `IsValid` / `IsVerified` | объект жив / игрок прислал SteamID |
| `Health` (get/set), `MaxHealth`, `IsDead` | здоровье |
| `RoleId`, `Role` (`RoleInfo`), `Team` | роль и команда |
| `Position` (get/set) | позиция; запись = телепорт |
| `IsStaff` | персонал: админ-панель, ранг из `ServerAdmins.json` или исключения античита |
| `HasAdminTools` (get/set) | доступ к встроенной админ-панели игры |
| `BadgeText`, `BadgeColor` | бейдж после ника в Tab |
| `RolePlayDetails` (get/set) | RP-описание персонажа (в Tab и при наведении прицела) |
| `IsCuffed`, `Cuffer` | в наручниках ли, кто ведёт |
| `Inventory` | инвентарь (см. ниже) |
| `CurrentWeapon`, `AllWeapons`, `GetWeapon(itemId)` | оружие |
| `Ammo` | патроны |
| `SessionData` | `Dictionary<string, object>` для данных плагинов на время сессии игрока |
| `RoundEndTeam` | подмена команды для подсчёта конца раунда (см. `SetRoundEndTeam`) |
| `GameObject`, `Stats`, `ClassManager`, `InventoryComponent`, `WeaponManager`… | компоненты игры для продвинутых плагинов |

**Действия**

| Метод | Описание |
|---|---|
| `SetRole(roleId, useSpawnPosition = true)` | сменить роль; `false` - остаться на месте |
| `Teleport(Vector3)` | телепорт |
| `Heal(n)` | вылечить (не выше максимума) |
| `Damage(n, reason, attacker = null)` | урон как в игре: с событиями `Hurting` / `Died`, трупом и смертью |
| `Kill(reason)` | убить |
| `ShowMessage(text)` | личное сообщение на экране ~5 с, видит только он; цветовые теги работают |
| `SetBadge(text, "FF0000")` / `ClearBadge()` | бейдж в Tab (цвет красит и ник) |
| `SetAdminToolsPersistent(bool)` | выдать/забрать админ-панель так, чтобы игра не перезаписала при входе |
| `Cuff(cuffer = null)` / `Uncuff()` | наручники |
| `Kick(reason)` | кик с окном причины |
| `SetRoundEndTeam(Team?)` | за какую команду считать игрока в проверке конца раунда, пока он в текущей роли (пример: SCP-035 за SCP) |
| `FlagCheat(check, details, count, blocked)` | сообщить о подозрении на читы (уходит в `CheatSuspected` и на сайт) |

### Инвентарь, предметы, предметы на полу

```csharp
// предметы игры
Items.All                     // IReadOnlyList<ItemInfo>: Id, Name
Items.GetName(id)             // "Five-Seven"
Items.Find("five")            // ID по точному или частичному названию, -1 - нет
Items.GetPermissions(id) / Items.HasPermission(id, "Armory")   // уровни доступа карт

// инвентарь игрока: player.Inventory
Slots, Items                  // ID по слотам / (Slot, ItemId) занятых слотов
CurrentItem, CurrentSlot, CurrentItemName, IsFull
Has(itemId), HasAccess("Armory"), CurrentItemHasAccess("Armory")
Add(itemId), Remove(itemId), RemoveSlot(slot), Clear()
Drop(slot), DropAll()

// предметы на полу
Pickups.Spawn(itemId, position, rotation)   // GameObject предмета
Pickups.GetItemId(pickup)
Pickups.Destroy(pickup)
```

Предметы, выданные плагином, помечаются причиной `plugin` в событиях `ItemAdded` / `ItemRemoved` (и в логах сайта).

### Оружие и патроны

Урон, магазин и перезарядку считает **сервер**, поэтому изменения реально меняют игру. Скорострельность и отдачу рисует клиент.

```csharp
var w = player.CurrentWeapon;        // PlayerWeapon или null
w.Damage = 40;                       // урон за попадание
w.MagazineSize = 30;                 // размер магазина
w.AmmoInMag = 30;                    // патронов в магазине сейчас
w.ReloadTime, w.FireRate, w.Cooldown, w.MaxAmmo, w.AmmoType, w.FullAuto, w.Stats()

player.Ammo.Get("9mm") / Set / Add / Take;  player.Ammo.All()

Weapons.All                           // все виды оружия: ItemId, Name, Damage, Magazine, AmmoType
Weapons.AmmoTypes
Weapons.FindItemId("P90")
Weapons.Configure(Weapons.FindItemId("P90"), w => w.Damage = 30);   // глобально: всем сейчас и всем, кто появится
Weapons.ClearConfiguration(itemId)
```

### Server, Round, роли

```csharp
Server.Players, Server.PlayerCount, Server.MaxPlayers
Server.Broadcast("текст")                  // на DefaultBroadcastDuration (7 с)
Server.Broadcast("текст", 15f)             // 0 - висит, пока не уберут
Server.ClearBroadcast()
Server.Roles                                // все роли: Id, Name, Team, MaxHealth
Server.GetRole(id), Server.FindRole("173" | "ntf" | "Scientist")
Server.PublicInfo["MyPlugin"] = new Dictionary<string, object> { ... };   // публичные данные для сайта (WebBridge)

Round.State                                 // "Waiting for round start" / "started" / "ending"
Round.IsStarted, Round.IsEnding, Round.IsWaiting, Round.IsLocked
Round.ElapsedTime, Round.LastRoundDuration
Round.ForceStart(), Round.Restart(), Round.End()
Round.SessionData                           // данные плагинов на текущий раунд (очищаются в начале раунда)
```

`Team`: `None`, `ClassD`, `Scientist`, `FacilityGuard`, `NineTailedFox`, `ChaosInsurgency`, `SCP`, `Spectator`, `Watcher`.

Роли игры 0.4.2:

| ID | Роль | Команда | HP |
|---|---|---|---|
| 0 | Class-D Personnel | ClassD | 100 |
| 1 | Spectator Class | None | 100 |
| 2 | SCP-106 | SCP | 3500 |
| 3 | Scientist | Scientist | 100 |
| 4 | Security Officer | FacilityGuard | 100 |
| 5 | SCP-173 | SCP | 4000 |
| 6 | SCP-457 | SCP | 3500 |
| 7 | SCP-049 | SCP | 3000 |
| 8 | Epsillon-11 Unit | NTF | 100 |
| 9 | Chaos Insurgency | Chaos | 100 |
| 10 | Nu-7 Unit | NTF | 100 |
| 11 | ETA-10 Unit | NTF | 100 |
| 12 | SCP-017 [INDEV] | SCP | 1900 |
| 13 | SCP-049-2 | SCP | 500 |
| 14 | SCP-035 | Watcher | 780 |
| 15 | Riot Guard [INDEV] | FacilityGuard | 120 |
| 16 | Janitor | Scientist | 100 |
| 17 | Watcher | Watcher | 100 |
| 18 | Bobik 939 | SCP | 939 |

### Двери

```csharp
Door.List                         // все двери
Door.Find("LCZ_Armory")           // по части имени
Door.Nearest(position, 5f)
door.Name, door.Position, door.Zone, door.AccessLevel (get/set), door.Cooldown (get/set)
door.IsOpen (get/set), door.IsLocked (get/set), door.IsDestroyed, door.IsAirlock, door.CanChange
door.Open(), Close(), Toggle(), Lock(), Unlock(), Destroy()
door.InteractAllowed()            // открыть «как игра»: звук, подсветка, задержка
door.InteractDenied()             // сигнал «доступ запрещён»
door.Diagnose(player)             // текст для отладки
```

### Карта: зоны, свет, лифты

```csharp
ZoneType: Unknown, Light, Heavy, Surface, Entrance
Map.GetZone(position)             // зона точки (по ближайшим лампам)
player.Zone()                     // расширение для Player
Map.ZoneByRoot(gameObject)        // зона объекта карты по корню сцены (точнее для дверей и комнат)
Map.ZonesNear(position, radius)
Map.ToggleZoneLights(ZoneType.Heavy)               // свет зоны вкл/выкл
Map.ToggleZoneLights(zone, targets: игроки)        // только этим игрокам
Map.IsBlackoutRound
Map.Elevators                     // лифты: PointA/PointB, ZoneA/ZoneB
Map.ElevatorBlockers.Add((elevator, player) => elevator.Touches(ZoneType.Light));   // запрет вызова лифта
```

### Трупы

```csharp
Ragdolls.FindNear(position, radius, nick = null)  // ближайший не сгнивший труп
Ragdolls.OwnerNick(ragdoll)
Ragdolls.IsDecayed(ragdoll)
Ragdolls.Destroy(ragdoll)
```

### Timing - таймеры

```csharp
Timing.CallDelayed(5f, () => ...);                 // один раз через 5 с
var h = Timing.CallRepeating(1f, () => ..., firstDelay: 0f);   // каждую секунду
h.Cancel();
Timing.NextFrame(() => ...);
```

Таймеры работают на главном потоке и автоматически отменяются при выгрузке плагина.

### Database - SQLite

Общая база `UnicornAPI/unicorn.db`. Таблицы плагина называйте с префиксом имени плагина (`myplugin_stats`). Параметры - `$1`, `$2`… Вызывать с главного потока.

```csharp
if (!Database.IsAvailable) return;
Database.Execute("CREATE TABLE IF NOT EXISTS myplugin_kills (steam_id INTEGER PRIMARY KEY, kills INTEGER NOT NULL)");
Database.Execute("INSERT INTO myplugin_kills VALUES ($1, 1) ON CONFLICT(steam_id) DO UPDATE SET kills = kills + 1", (long)p.SteamId);
long kills = (long)(Database.Scalar("SELECT kills FROM myplugin_kills WHERE steam_id=$1", (long)p.SteamId) ?? 0L);
foreach (var row in Database.Query("SELECT * FROM myplugin_kills ORDER BY kills DESC LIMIT 10"))
    Log.Info($"{row["steam_id"]}: {row["kills"]}");
```

### Profiles - оформление игроков

Сохранённые бейдж, цвет, ник и админ-панель, применяются при каждом входе.

```csharp
Profiles.Get(steamId)                      // PlayerProfile или null
Profiles.Update(steamId, p => { p.Badge = "[VIP]"; p.BadgeColor = "FFD700"; });
Profiles.Delete(steamId), Profiles.All()
```

### Команды

```csharp
public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }
    string Description { get; }
    bool Execute(string[] args, out string response);   // true - успех
}

RegisterCommand(new SimpleCommand("name", "описание для help", (string[] a, out string r) => { r = "ok"; return true; }, "алиас1", "алиас2"));
CommandManager.TryExecute("bc 10 Привет", "plugin", out bool ok, out string response);   // выполнить из кода
```

Команды доступны в консоли сервера, в `help` и с сайта (через WebBridge, если разрешено в его конфиге).

---

## События

Подписка: `PlayerEvents.Joined += handler;`, отписка: `-=`. События с `IsAllowed` можно отменить, поля с `set` - изменить.

### PlayerEvents

| Событие | Аргументы | Можно |
|---|---|---|
| `Joined` | `Player` | игрок прислал SteamID и ник - зашёл окончательно |
| `Left` | `Player` | вышел |
| `ChangingRole` | `OldRole`, `NewRole` | заменить `NewRole`, отменить |
| `ChangedRole` | `OldRole`, `NewRole` | - |
| `Hurting` | `Attacker`, `Amount`, `HitInfo` | изменить урон, отменить |
| `Died` | `Attacker`, `Damage`, `HitInfo` | - |
| `InteractingDoor` | `Door` | отменить |
| `DoorInteracted` | `Door`, `Result` (opened / closed / denied / blocked), `ItemId` | - |
| `Cuffing` / `Uncuffing` | `Cuffer` | отменить |
| `CuffChanged` | `IsCuffed` | - |
| `PickingUpItem` | `Pickup` | отменить |
| `DroppingItem` | `Slot`, `ItemId` | отменить |
| `ItemAdded` / `ItemRemoved` | `Slot`, `ItemId`, `ItemName`, `Reason` (pickup, drop, use, admin, command, plugin, other) | - |
| `UsedItem` | `Kind`: Medkit, Scp420J, Scp458, Scp294, Scp714 | - |
| `ChangedItem` | `OldItemId`, `NewItemId`, `Weapon` | - |
| `Shooting` | `Weapon`, `ItemId`, `Origin`, `Direction` | отменить выстрел |
| `Reloading` | `Weapon` | отменить перезарядку |
| `Scp049Converting` | `Scp049`, `Target` | отменить превращение в зомби |
| `AdminAction` | `Action`, `Argument`, `Target`, `Response` | - (действия во встроенной админ-панели игры) |
| `CheatSuspected` | `Check`, `Details`, `Count`, `Blocked` | - (срабатывания античита) |

### ServerEvents

| Событие | Аргументы | Можно |
|---|---|---|
| `WaitingForPlayers` | - | лобби |
| `RoundStarted` / `RoundEnded` | - | - |
| `Update` | - | каждый кадр (держите обработчик лёгким) |
| `StartRoleChoosing` | `Team`, `OriginalRoleId`, `RoleId` | заменить роль при раздаче на старте |
| `Scp914UpgradingItem` | `Pickup`, `ItemId`, `Mode`, `OutputItemId` | свой результат переработки, отменить |
| `CommandExecuted` | `Source`, `Line`, `Handled`, `Ok`, `Response` | - |
| `ConsoleLine` | `Source`, `Level`, `Text` | - (может прийти **не с главного потока**) |

---

## Что UnicornAPI чинит в игре

Выделенный сервер SCP: Project 90 написан с расчётом на «хоста-игрока», поэтому в режиме dedicated многое не работает. UnicornAPI исправляет:

| Проблема игры | Что делает UnicornAPI |
|---|---|
| Сервер логинится в Steam, но сеть Mirror не стартует | запускает сам (`[DedicatedFix]`) |
| Каждого игрока кикало сразу после входа (ошибка в `OnServerConnect`) | заменяет метод, проверка банов `Bans.json` сохранена |
| Роли на старте раздаёт только хост - все оставались в лобби | раздаёт роли на сервере (`giveroles` вручную) |
| При >25 игроках лишние оставались без роли | очереди спавна расширены |
| Позиция игрока «застревала» на спавне - не открывались двери | сервер берёт позицию, которую шлёт клиент |
| Карта строилась только у клиентов - у сервера другая карта | строит зоны на сервере тем же ключом (`[MapGenFix]`) |
| Рестарт после раунда шёл только у хоста - раунд висел | запускает рестарт на сервере |
| Серверный код игры вызывает Command (работает только у хоста): ворота, свет, погода, двери, экипировка | выполняет такие Command на сервере (`[CmdOnServerFix]`) |
| SyncVar-хуки на сервере не вызываются | эмулирует нужные: смена класса, SCP-173, двери, лифты |
| Броадкаст висит на экране вечно | убирает через 7 с |
| Сервер не виден в игровом списке, иногда «выпадает» из него | создаёт Steam-лобби сам, сторож пересоздаёт |
| Взорванные гранаты и мины навсегда остаются на сервере, мины бесконечно плодят гранаты | удаляет на сервере |
| Блокировка раунда «залипает» | возвращает к настройке в каждом раунде, снимает автоблокировку игры при наборе игроков |
| Двери не закрывались и могли остаться запертыми после рестарта | закрывает и отпирает при рестарте |
| Русские буквы в консоли превращаются в «?» | читает консоль в Юникоде |
| Мусор и спам Discord RPC в консоли | фильтр консоли, Discord RPC в batchmode отключён |
| Конец раунда не учитывает роли команды Watcher (SCP-035) | `SetRoundEndTeam` для плагинов |

---

## Официальные плагины

Каждый плагин лежит в своём репозитории в этой же организации: там исходники, README с настройками и готовая DLL в релизах.

| Плагин | Что делает | Команды |
|---|---|---|
| **Levels** | уровни 1-100 за убийства, урон по SCP и предметы; уровень в RP-строке игрока; хранение в базе | `xp`, `xp add/set/level/reset/show/top` |
| **WebBridge** | связь с сайтом: снимок сервера, события (входы, смерти, урон, предметы, двери, античит, действия админов, лог консоли), команды с сайта (броадкаст, сообщение, кик, консоль) | `web`, `web flush` |
| **Scp035** | SCP-035 «Маска»: с шансом заменяет одного из SCP, теряет HP каждую секунду, восстанавливает, поглощая трупы; считается за SCP | `scp035 [игрок]` |
| **Scp049Revive** | SCP-049 как в SCP:SL: удар убивает, а свежий труп можно воскресить зомби, постояв рядом | - |
| **Decontamination** | деконтаминация лёгкой зоны на N-й минуте: объявления, закрытие КПП, урон оставшимся | `decont …` |
| **Scp914Recipes** | свои рецепты SCP-914 с шансами | - |
| **SpawnTweaks** | Riot Guard вместо охранника с шансом | - |
| **WeaponTweaks** | урон и магазины оружия из конфига | - |
| **KeycardAnywhere** | двери открываются картой из инвентаря, не обязательно держать её в руках | - |
| **Announcer** | периодические объявления | - |

Отдельно (не плагин UnicornAPI, а свой плагин BepInEx): **P90Guard** - серверный античит с сетью читеров.

Пример минимального плагина: `examples/HelloUnicorn` в этом репозитории.

---

## Сборка

Нужно: .NET 6 SDK или новее и папка игры с BepInEx, запущенным хотя бы раз (нужны `BepInEx/core` и `BepInEx/interop`).

```
dotnet build UnicornAPI.sln -c Release -p:GameDir="D:\Steam\steamapps\common\SCP Project 90"
```

Без `-p:GameDir` сборка ищет `./libs` (та же структура: `libs/BepInEx/core`, `libs/BepInEx/interop`). DLL игры в репозиторий не входят.

Результат:
- `src/UnicornAPI/bin/Release/net6.0/` → `UnicornAPI.dll` и библиотеки SQLite (`Microsoft.Data.Sqlite.dll`, `SQLitePCLRaw.*.dll`, `runtimes/win-x64/native/e_sqlite3.dll`) → всё в `BepInEx/plugins/UnicornAPI/`;
- `examples/HelloUnicorn/bin/Release/net6.0/HelloUnicorn.dll` → `UnicornAPI/Plugins/` (если нужен пример).

Структура репозитория:

```
src/UnicornAPI/        сам фреймворк
examples/HelloUnicorn/ пример плагина
tools/Unicorn.Mapper/  карта обфусцированных имён после обновления игры
```

Официальные плагины собираются в своих репозиториях, см. их README.

---

## После обновления игры

Игра обфусцирована: методы называются `cjd`, `bhm`… и меняются с каждым билдом. Все такие имена собраны **только** в `src/UnicornAPI/Internal/Game.cs` и атрибутах патчей.

1. Запустить сервер с BepInEx один раз, чтобы пересоздались interop-сборки.
2. `dotnet run --project tools/Unicorn.Mapper -- "<папка игры>" mapper_out` - карта имён.
3. В `mapper_out/namemap.tsv` найти новые имена, поправить `Game.cs` и патчи, пересобрать.
4. Добавить новый хэш `GameAssembly.dll` в проверку версии.

Плагины, которые пользуются только API UnicornAPI (`UnicornPlugin=true`), пересобирать не нужно.

---

## Правила и ограничения

- Только сервер: клиент игры не меняется, поэтому нельзя добавить новые модели, звуки, интерфейс. Всё, что видят игроки, - это возможности самой игры (сообщения на экране, броадкасты, бейджи, роли, предметы).
- Движение и прицел считает клиент: скорость, прыжок и замедление с сервера не меняются.
- `Joined` срабатывает, когда клиент прислал SteamID, а не в момент подключения.
- Версия игры: фреймворк привязан к конкретному билду. На другой версии - режим `GameHashCheck`.
- Игра и все её ресурсы принадлежат их правообладателям. UnicornAPI не распространяет файлы игры.

---

## Лицензия

UnicornAPI распространяется под GNU LGPL-3.0 (см. `LICENSE` и `COPYING`).
Плагины на UnicornAPI можно выпускать под любой лицензией.
Изменённые версии самого UnicornAPI нужно публиковать под LGPL-3.0.

Проект не связан с разработчиками SCP: Project 90.
