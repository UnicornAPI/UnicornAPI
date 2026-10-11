using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using HarmonyLib;
using Unicorn.API;
using UnityEngine;

namespace Unicorn.Patches
{
    /// <summary>
    /// Русские буквы в консоли сервера превращались в '?': игра читает ввод через Console.ReadLine рантайма IL2CPP,
    /// а там нет кодировки консоли Windows (cp866). Отключаем её поток чтения и читаем консоль сами через .NET
    /// (ReadConsoleW — Юникод, от кодовой страницы не зависит). Строки отдаём игре в ту же очередь команд.
    /// </summary>
    [HarmonyPatch(typeof(ServerConsole), "bhl")] // поток чтения: while(!stop) { line = Console.ReadLine(); queue.Enqueue(line); }
    internal static class ConsoleInputFix
    {
        private static readonly ConcurrentQueue<string> Lines = new();
        private static Thread _reader;
        private static ServerConsole _console;
        internal static bool Active;

        private static bool Prefix()
        {
            if (!Application.isBatchMode) return true;
            Start();
            return false; // игровой поток чтения не запускаем
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ReadConsoleW(IntPtr h, [Out] char[] buf, uint toRead, out uint read, IntPtr ctl);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr h, out uint mode);
        [DllImport("kernel32.dll")] private static extern uint GetConsoleCP();

        private static void Start()
        {
            if (_reader != null) return;
            Active = true;
            // 1) Настоящий ввод консоли (CONIN$) через ReadConsoleW — сразу UTF-16, кодовая страница не важна
            IntPtr con = IntPtr.Zero;
            try
            {
                con = CreateFileW("CONIN$", 0xC0000000 /*GENERIC_READ|WRITE*/, 3 /*share r|w*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);
                if (con == new IntPtr(-1) || !GetConsoleMode(con, out _)) con = IntPtr.Zero;
            }
            catch { con = IntPtr.Zero; }

            if (con != IntPtr.Zero)
            {
                Log.Info("[ConsoleInputFix] ввод консоли: ReadConsoleW (Юникод)");
                var h = con;
                _reader = new Thread(() => ReadLoopConsole(h)) { IsBackground = true, Name = "UnicornAPI console input" };
            }
            else
            {
                // 2) Запасной путь: stdin перенаправлен — читаем в кодировке консоли (cp866 и т.п.)
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    var enc = Encoding.GetEncoding((int)GetConsoleCP());
                    System.Console.InputEncoding = enc;
                    Log.Info($"[ConsoleInputFix] ввод консоли: stdin, кодировка {enc.WebName}");
                }
                catch (Exception e) { Log.Warn($"[ConsoleInputFix] кодировка консоли: {e.Message}"); }
                _reader = new Thread(ReadLoopStdin) { IsBackground = true, Name = "UnicornAPI console input" };
            }
            _reader.Start();
        }

        private static void ReadLoopConsole(IntPtr h)
        {
            var buf = new char[4096];
            var sb = new StringBuilder();
            while (true)
            {
                if (!ReadConsoleW(h, buf, (uint)buf.Length, out var n, IntPtr.Zero) || n == 0) { Thread.Sleep(100); continue; }
                for (int i = 0; i < n; i++)
                {
                    char c = buf[i];
                    if (c == '\r') continue;
                    if (c == '\n') { var line = sb.ToString(); sb.Clear(); if (line.Length > 0) Lines.Enqueue(line); }
                    else sb.Append(c);
                }
            }
        }

        private static void ReadLoopStdin()
        {
            while (true)
            {
                string line;
                try { line = System.Console.ReadLine(); }
                catch { Thread.Sleep(200); continue; }
                if (line == null) { Thread.Sleep(200); continue; }
                if (line.Length > 0) Lines.Enqueue(line);
            }
        }

        /// <summary>С главного потока: отдать прочитанные строки игре.</summary>
        internal static void Tick()
        {
            if (!Active || Lines.IsEmpty) return;
            if (_console == null) _console = UnityEngine.Object.FindObjectOfType<ServerConsole>();
            while (Lines.TryDequeue(out var line))
            {
                try
                {
                    if (_console?.kaq != null) _console.kaq.Enqueue(line);
                    else Commands.CommandManager.TryExecute(line);
                }
                catch (Exception e) { Log.Error($"[ConsoleInputFix] {e.Message}"); }
            }
        }
    }
}
