using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unicorn.API;

namespace Unicorn.Internal
{
    /// <summary>
    /// Подключение SQLite внутри BepInEx: управляемые сборки (Microsoft.Data.Sqlite, SQLitePCLRaw) и нативная
    /// e_sqlite3.dll лежат рядом с UnicornAPI.dll. Сами говорим .NET, где их искать.
    /// </summary>
    internal static class SqliteBootstrap
    {
        public static string Dir { get; private set; }
        public static bool Ready { get; private set; }

        public static void Init()
        {
            Dir = Path.GetDirectoryName(typeof(SqliteBootstrap).Assembly.Location);
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var file = Path.Combine(Dir, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            try { InitProvider(); Ready = true; }
            catch (Exception e) { Log.Error($"[SQLite] не запустился — БД недоступна: {e}"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitProvider()
        {
            var providerAsm = typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly;
            NativeLibrary.SetDllImportResolver(providerAsm, (name, asm, path) =>
            {
                if (!name.Contains("e_sqlite3")) return IntPtr.Zero;
                foreach (var f in new[] { "e_sqlite3.dll", "libe_sqlite3.so", "e_sqlite3.so" })
                {
                    var full = Path.Combine(Dir, f);
                    if (File.Exists(full)) return NativeLibrary.Load(full);
                }
                return IntPtr.Zero; // пусть .NET ищет сам
            });
            SQLitePCL.Batteries_V2.Init();
        }
    }
}
