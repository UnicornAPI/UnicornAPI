using System;
using System.IO;
using System.Linq;

namespace Unicorn.Internal
{
    /// <summary>
    /// Переезд со старого названия (P1-Sun → UnicornAPI).
    /// 1) Старый фреймворк BepInEx/plugins/P1Sun/P1Sun.dll ещё лежит — два фреймворка в одной игре поставили бы все
    ///    патчи дважды. Отключаем старый файл (переименовываем) и в этот запуск НЕ стартуем — нужен перезапуск.
    /// 2) Папка данных &lt;игра&gt;/P1-Sun: переносим всё в &lt;игра&gt;/UnicornAPI (конфиги, базу, плагины, фильтры).
    ///    Что уже есть в новой папке (например, новые DLL плагинов из архива обновления) — не трогаем.
    ///    p1sun.json → unicorn.json, p1sun.db → unicorn.db. Старая папка остаётся как P1-Sun.old (ничего не удаляем).
    /// </summary>
    internal static class Migration
    {
        /// <summary>true — найден старый фреймворк: этот запуск пропускаем.</summary>
        internal static bool OldFrameworkPresent(string gameRoot, BepInEx.Logging.ManualLogSource log)
        {
            bool loaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => { try { return a.GetName().Name == "P1Sun"; } catch { return false; } });
            var oldDll = Path.Combine(BepInEx.Paths.PluginPath, "P1Sun", "P1Sun.dll");
            bool file = File.Exists(oldDll);
            if (!loaded && !file) return false;

            log.LogError("==============================================================");
            log.LogError(" Найден старый P1-Sun (BepInEx/plugins/P1Sun). UnicornAPI — это он же под новым названием,");
            log.LogError(" два сразу работать не могут. В этот запуск UnicornAPI НЕ включён.");
            if (file)
            {
                try
                {
                    var disabled = oldDll + ".disabled";
                    if (File.Exists(disabled)) File.Delete(disabled);
                    File.Move(oldDll, disabled);
                    log.LogError(" Старый P1Sun.dll отключён (переименован в P1Sun.dll.disabled). ПЕРЕЗАПУСТИ СЕРВЕР.");
                }
                catch (Exception e)
                {
                    log.LogError($" Не получилось отключить {oldDll}: {e.Message}");
                    log.LogError(" Выключи сервер, удали папку BepInEx/plugins/P1Sun и запусти снова.");
                }
            }
            else log.LogError(" Старый P1Sun уже загружен в игру — перезапусти сервер.");
            log.LogError("==============================================================");
            return true;
        }

        /// <summary>Перенести данные из &lt;игра&gt;/P1-Sun в новую папку (один раз).</summary>
        internal static void MoveDataFolder(string gameRoot, string newRoot, BepInEx.Logging.ManualLogSource log)
        {
            var oldRoot = Path.Combine(gameRoot, "P1-Sun");
            if (!Directory.Exists(oldRoot)) return;

            int moved = 0, kept = 0, failed = 0;
            log.LogWarning($"Переезд P1-Sun → UnicornAPI: переношу данные из {oldRoot}");
            foreach (var src in Directory.GetFiles(oldRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(oldRoot, src);
                var name = Path.GetFileName(rel);
                var dir = Path.GetDirectoryName(rel) ?? "";
                if (dir.Length == 0)
                {
                    if (name.Equals("p1sun.json", StringComparison.OrdinalIgnoreCase)) name = "unicorn.json";
                    else if (name.StartsWith("p1sun.db", StringComparison.OrdinalIgnoreCase)) name = "unicorn.db" + name.Substring("p1sun.db".Length);   // + -wal / -shm
                }
                var dst = Path.Combine(newRoot, dir, name);
                try
                {
                    if (File.Exists(dst)) { kept++; continue; }   // новое уже есть (например, DLL из архива обновления)
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Move(src, dst);
                    moved++;
                }
                catch (Exception e) { failed++; log.LogError($"  не перенёсся {rel}: {e.Message}"); }
            }

            var backup = oldRoot + ".old";
            try
            {
                if (Directory.Exists(backup)) backup += "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(oldRoot, backup);
            }
            catch (Exception e) { log.LogWarning($"  старую папку не переименовать: {e.Message}"); backup = oldRoot; }
            log.LogWarning($"Переезд готов: перенесено файлов {moved}, оставлено новых {kept}, ошибок {failed}. Остатки старой папки: {backup} (её можно удалить, когда всё проверишь)");
        }
    }
}
