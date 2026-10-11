using System;
using System.Collections.Generic;
using System.Linq;
using Unicorn.API;

namespace Unicorn.Commands
{
    /// <summary>Команда консоли сервера.</summary>
    public interface ICommand
    {
        string Name { get; }
        string[] Aliases { get; }
        string Description { get; }
        /// <param name="args">Аргументы без имени команды.</param>
        /// <param name="response">Текст, который будет выведен в консоль.</param>
        /// <returns>true — успех.</returns>
        bool Execute(string[] args, out string response);
    }

    public static class CommandManager
    {
        private static readonly Dictionary<string, ICommand> Map = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<ICommand> All = new();

        public static void Register(ICommand cmd)
        {
            foreach (var n in new[] { cmd.Name }.Concat(cmd.Aliases ?? Array.Empty<string>()))
            {
                if (Map.ContainsKey(n)) { Log.Warn($"[Commands] '{n}' уже занята, пропускаю ({cmd.GetType().FullName})"); continue; }
                Map[n] = cmd;
            }
            All.Add(cmd);
        }

        public static void Unregister(ICommand cmd)
        {
            foreach (var k in Map.Where(kv => kv.Value == cmd).Select(kv => kv.Key).ToList()) Map.Remove(k);
            All.Remove(cmd);
        }

        /// <summary>Снять все команды, объявленные в сборке плагина (горячая перезагрузка).</summary>
        internal static int UnregisterOwnedBy(System.Reflection.Assembly asm)
        {
            var own = All.Where(c => c.GetType().Assembly == asm || (c is SimpleCommand sc && Events.EventUtil.Owned(sc.HandlerDelegate, asm))).ToList();
            foreach (var c in own) Unregister(c);
            return own.Count;
        }

        /// <summary>Выполнить строку из консоли. false — команда не наша (отдать игре).</summary>
        public static bool TryExecute(string line) => TryExecute(line, "console", out _, out _);

        /// <summary>
        /// Выполнить строку. source — откуда (console, web, plugin): уходит в событие ServerEvents.CommandExecuted.
        /// false — команда не UnicornAPI (для source=console строку дальше обработает игра).
        /// </summary>
        public static bool TryExecute(string line, string source, out bool ok, out string response)
        {
            ok = false; response = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!Map.TryGetValue(parts[0], out var cmd))
            {
                Events.ServerEvents.CommandExecuted.Invoke(new Events.CommandExecutedEventArgs(source, line.Trim(), false, false, null));
                return false;
            }

            var prevReason = Internal.ItemReason.Set("command");
            try { ok = cmd.Execute(parts.Skip(1).ToArray(), out response); }
            catch (Exception e) { ok = false; response = e.ToString(); }
            finally { Internal.ItemReason.Restore(prevReason); }

            var prev = System.Console.ForegroundColor;
            System.Console.ForegroundColor = ok ? System.ConsoleColor.Green : System.ConsoleColor.Red;
            System.Console.WriteLine($"[{parts[0]}{(source == "console" ? "" : " · " + source)}] {response}");
            System.Console.ForegroundColor = prev;
            Events.ServerEvents.CommandExecuted.Invoke(new Events.CommandExecutedEventArgs(source, line.Trim(), true, ok, response));
            return true;
        }

        internal static void PrintHelp()
        {
            if (All.Count == 0) return;
            System.Console.WriteLine("--- UnicornAPI ---");
            foreach (var c in All.OrderBy(c => c.Name))
            {
                var al = c.Aliases is { Length: > 0 } ? $" ({string.Join(", ", c.Aliases)})" : "";
                System.Console.WriteLine($"{(c.Name + al).PadRight(22)} - {c.Description}");
            }
            System.Console.WriteLine("--------------\n");
        }
    }
}
