using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unicorn.API;
using Unicorn.Commands;
using UnityEngine;

namespace Unicorn
{
    [BepInPlugin(Guid, "UnicornAPI", VersionString)]
    public sealed class Loader : BasePlugin
    {
        public const string Guid = "flx.unicornapi";
        public const string VersionString = "1.0.0";

        public static Harmony Harmony { get; private set; }
        public static IReadOnlyList<API.Plugin> Plugins => LoadedPlugins;
        private static readonly List<API.Plugin> LoadedPlugins = new();

        public override void Load()
        {
            API.Log.Source = Log;
            // старое название: P1-Sun. Если старый фреймворк ещё стоит — не стартуем (иначе все патчи встанут дважды)
            if (Internal.Migration.OldFrameworkPresent(BepInEx.Paths.GameRootPath, Log)) return;
            API.Paths.Root = Path.Combine(BepInEx.Paths.GameRootPath, "UnicornAPI");
            try { Internal.Migration.MoveDataFolder(BepInEx.Paths.GameRootPath, API.Paths.Root, Log); }
            catch (Exception e) { Log.LogError($"Переезд P1-Sun → UnicornAPI: {e}"); }
            Directory.CreateDirectory(API.Paths.Plugins);
            Directory.CreateDirectory(API.Paths.Configs);
            Directory.CreateDirectory(Path.Combine(API.Paths.Root, "Dependencies"));

            Log.LogInfo($"UnicornAPI v{VersionString} — batchmode={Application.isBatchMode}");
            if (!Application.isBatchMode)
                Log.LogWarning("Игра запущена НЕ как dedicated-сервер. События сервера срабатывать не будут.");

            Internal.CoreConfig.Load();
            Internal.SqliteBootstrap.Init();
            if (Internal.SqliteBootstrap.Ready)
                try { Log.LogInfo($"SQLite: {API.Database.FilePath} (v{API.Database.Scalar("select sqlite_version()")})"); }
                catch (Exception e) { Log.LogError($"SQLite: не открылась БД: {e.Message}"); }

            if (!Internal.GameVersionCheck.Run())
            {
                Log.LogError("UnicornAPI в безопасном режиме: игра другой версии, патчи, фиксы и плагины не загружены.");
                return;
            }

            AddComponent<Ticker>();

            Harmony = new Harmony(Guid);
            // Патчим по одному классу: если после апдейта игры одно имя устарело — остальные всё равно работают.
            int ok = 0, fail = 0;
            // UnicornAPI/disabled_patches.txt — имена классов патчей (по одному в строке), которые НЕ ставить (для диагностики).
            var disabledFile = Path.Combine(API.Paths.Root, "disabled_patches.txt");
            var disabled = File.Exists(disabledFile)
                ? new HashSet<string>(File.ReadAllLines(disabledFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
            foreach (var t in typeof(Loader).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
            {
                if (disabled.Contains(t.Name)) { Log.LogWarning($"Патч {t.Name} отключён через disabled_patches.txt"); continue; }
                try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception e)
                {
                    fail++;
                    Log.LogError($"Патч {t.Name} не применился (игра обновилась? см. Internal/Game.cs): {e.InnerException?.Message ?? e.Message}");
                }
            }
            Log.LogInfo($"Патчей применено: {ok}{(fail > 0 ? $", с ошибкой: {fail}" : "")}");

            Patches.ConsoleFilter.Load();
            Internal.ConsoleStream.Install();
            Internal.AntiCheat.Init();
            Timing.CallDelayed(3f, Internal.GuardBridge.TryConnect);   // античит P90Guard (отдельный плагин), если установлен
            BuiltinCommands.Register();
            LoadPlugins();
        }

        private static bool _depsLoaded;

        /// <summary>Загрузить все плагины из UnicornAPI/Plugins (каждый в свой выгружаемый контекст — для горячей перезагрузки).</summary>
        internal static void LoadPlugins()
        {
            if (!_depsLoaded)
            {
                _depsLoaded = true;
                foreach (var dll in Directory.GetFiles(Path.Combine(API.Paths.Root, "Dependencies"), "*.dll"))
                {
                    try { Assembly.LoadFrom(dll); API.Log.Info($"Зависимость: {Path.GetFileName(dll)}"); }
                    catch (Exception e) { API.Log.Error($"Не загрузилась зависимость {dll}: {e.Message}"); }
                }
            }

            var found = new List<(API.Plugin P, PluginContext Ctx, string File)>();
            foreach (var dll in Directory.GetFiles(API.Paths.Plugins, "*.dll"))
            {
                PluginContext ctx = null;
                try
                {
                    ctx = new PluginContext(Path.GetFileNameWithoutExtension(dll));
                    // грузим из байтов — DLL не блокируется, её можно заменить на лету и сделать reload
                    var pdb = Path.ChangeExtension(dll, ".pdb");
                    using var ms = new MemoryStream(File.ReadAllBytes(dll));
                    using var ps = File.Exists(pdb) ? new MemoryStream(File.ReadAllBytes(pdb)) : null;
                    var asm = ctx.LoadFromStream(ms, ps);
                    int n = 0;
                    foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && typeof(API.Plugin).IsAssignableFrom(t)))
                    { found.Add(((API.Plugin)Activator.CreateInstance(t), ctx, dll)); n++; }
                    if (n == 0) { API.Log.Warn($"{Path.GetFileName(dll)}: нет класса-наследника Plugin"); ctx.Unload(); }
                }
                catch (ReflectionTypeLoadException e)
                {
                    API.Log.Error($"Плагин {Path.GetFileName(dll)} не загрузился: {string.Join("; ", e.LoaderExceptions.Select(x => x?.Message))}");
                    ctx?.Unload();
                }
                catch (Exception e) { API.Log.Error($"Плагин {Path.GetFileName(dll)} не загрузился: {e}"); ctx?.Unload(); }
            }

            foreach (var (p, ctx, file) in found.OrderBy(x => x.P.Priority))
            {
                try
                {
                    p.OnEnabled();
                    LoadedPlugins.Add(p);
                    Contexts[p] = ctx;
                    API.Log.Info($"Плагин включён: {p.Name} v{p.Version} by {p.Author}");
                }
                catch (Exception e) { API.Log.Error($"{p.Name}.OnEnabled упал: {e}"); CleanupAssembly(p.GetType().Assembly); }
            }
        }

        private static readonly Dictionary<API.Plugin, PluginContext> Contexts = new();

        private static void CleanupAssembly(Assembly asm)
        {
            int ev = Events.EventUtil.RemoveAllOwnedBy(asm);
            int tm = API.Timing.CancelOwnedBy(asm);
            int cm = CommandManager.UnregisterOwnedBy(asm);
            int wo = 0;
            try { wo = API.Weapons.RemoveOwnedBy(asm); } catch { }
            try { wo += API.Map.ElevatorBlockers.RemoveAll(f => f.Method.DeclaringType?.Assembly == asm || (f.Target != null && f.Target.GetType().Assembly == asm)); } catch { }
            try { foreach (var k in API.Server.PublicInfo.Keys.ToList()) if (API.Server.PublicInfo[k]?.GetType().Assembly == asm) API.Server.PublicInfo.TryRemove(k, out _); } catch { }
            if (ev + tm + cm + wo > 0) API.Log.Debug($"[Reload] {asm.GetName().Name}: снято событий {ev}, таймеров {tm}, команд {cm}, настроек оружия {wo}");
        }

        /// <summary>
        /// Горячая перезагрузка: выключить все плагины, снять их события/таймеры/команды, выгрузить сборки
        /// и загрузить DLL из папки заново (новые версии подхватятся без рестарта сервера).
        /// </summary>
        internal static string ReloadPlugins()
        {
            int before = LoadedPlugins.Count;
            foreach (var p in LoadedPlugins.ToList())
            {
                try { p.OnDisabled(); } catch (Exception e) { API.Log.Error($"{p.Name}.OnDisabled упал: {e}"); }
                CleanupAssembly(p.GetType().Assembly);
                if (Contexts.TryGetValue(p, out var ctx)) { try { ctx.Unload(); } catch { } Contexts.Remove(p); }
            }
            LoadedPlugins.Clear();
            GC.Collect();
            LoadPlugins();
            return $"выгружено {before}, загружено {LoadedPlugins.Count}";
        }

        public override bool Unload()
        {
            foreach (var p in LoadedPlugins) { try { p.OnDisabled(); } catch { } }
            Harmony?.UnpatchSelf();
            API.Database.Close();
            return true;
        }
    }

    /// <summary>Выгружаемый контекст плагина. Всё, чего нет в плагине, берётся из общего контекста (Unicorn, игра, BepInEx).</summary>
    internal sealed class PluginContext : System.Runtime.Loader.AssemblyLoadContext
    {
        public PluginContext(string name) : base("Unicorn:" + name, isCollectible: true) { }
        protected override Assembly Load(AssemblyName name) => null;
    }

    /// <summary>MonoBehaviour в IL2CPP-мире: тикает Timing.</summary>
    public sealed class Ticker : MonoBehaviour
    {
        public Ticker(IntPtr ptr) : base(ptr) { }
        private void Update()
        {
            Internal.DedicatedFix.Tick();
            Internal.HookEmulator.Tick();
            Internal.RoundLockGuard.Tick();
            Internal.AntiCheat.Tick();
            Internal.RoundStateTracker.Tick();
            try { Internal.LobbyAdvertiser.Tick(); } catch { }
            Internal.WeaponWatcher.Tick();
            Patches.ConsoleInputFix.Tick();
            Timing.Tick();
            Events.ServerEvents.Update.Invoke();
        }
    }
}
