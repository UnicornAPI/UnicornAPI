using System;
using System.IO;
using System.Text.Json;
using Unicorn.Commands;

namespace Unicorn.API
{
    /// <summary>
    /// Базовый класс плагина UnicornAPI. Кладётся DLL в &lt;игра&gt;/UnicornAPI/Plugins.
    /// </summary>
    public abstract class Plugin
    {
        public abstract string Name { get; }
        public abstract string Author { get; }
        public virtual Version Version => GetType().Assembly.GetName().Version;
        /// <summary>Меньше — раньше грузится.</summary>
        public virtual int Priority => 0;

        public virtual void OnEnabled() { }
        public virtual void OnDisabled() { }

        protected void RegisterCommand(ICommand cmd) => CommandManager.Register(cmd);

        /// <summary>
        /// Загружает конфиг из UnicornAPI/Configs/&lt;Name&gt;.json. Если файла нет — создаёт с дефолтами.
        /// </summary>
        protected T LoadConfig<T>() where T : new()
        {
            var path = Path.Combine(Paths.Configs, $"{Name}.json");
            var opts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            try
            {
                if (File.Exists(path))
                {
                    var cfg = JsonSerializer.Deserialize<T>(File.ReadAllText(path), opts) ?? new T();
                    File.WriteAllText(path, JsonSerializer.Serialize(cfg, opts)); // дописать новые поля
                    return cfg;
                }
            }
            catch (Exception e) { Log.Error($"[{Name}] битый конфиг {path}: {e.Message}. Беру дефолт."); return new T(); }

            var def = new T();
            File.WriteAllText(path, JsonSerializer.Serialize(def, opts));
            return def;
        }
    }

    public static class Paths
    {
        public static string Root { get; internal set; }
        public static string Plugins => Path.Combine(Root, "Plugins");
        public static string Configs => Path.Combine(Root, "Configs");
    }
}
