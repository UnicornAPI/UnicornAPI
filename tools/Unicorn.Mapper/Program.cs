// Unicorn.Mapper — восстанавливает настоящие имена обфусцированных методов SCP Project 90
// по строкам, которые Mirror Weaver оставил в коде. Запускать после каждого апдейта игры.
//   dotnet run -- "D:\Steam\steamapps\common\SCP Project 90" out
// Результат: out/namemap.tsv (Класс \t обфусц.имя \t настоящее имя) и out/strings/<Класс>.txt
// (методы + строки + вызовы) — по ним обновляется src/Unicorn/Internal/Game.cs.
using System.Text;
using System.Text.RegularExpressions;
using Cpp2IL.Core;
using Iced.Intel;
using LibCpp2IL;

var game = args.Length > 0 ? args[0] : ".";
var outDir = args.Length > 1 ? args[1] : "mapper_out";
var exe = Directory.GetFiles(game, "*.exe").First(f => !f.Contains("UnityCrashHandler"));
var data = Path.Combine(game, Path.GetFileNameWithoutExtension(exe) + "_Data");

Cpp2IlApi.Init(Path.Combine(outDir, "_plugins"));
var ver = Cpp2IlApi.DetermineUnityVersion(exe, data);
Cpp2IlApi.ConfigureLib(false);
Cpp2IlApi.InitializeLibCpp2Il(Path.Combine(game, "GameAssembly.dll"), Path.Combine(data, "il2cpp_data/Metadata/global-metadata.dat"), ver, false);
var ctx = Cpp2IlApi.CurrentAppContext!;
Directory.CreateDirectory(Path.Combine(outDir, "strings"));

var rxGuard = new Regex(@"^\[(Server|Client)\] function '[^ ]+ ([\w\.\+]+)::(\w+)\(");
var rxSig = new Regex(@"^System\.Void ([\w\.\+]+)::((Rpc|Cmd|Target)\w+)\(");
var rxHandler = new Regex(@"^(Command|RPC|TargetRPC) ((Rpc|Cmd|Target)\w+) called on");
var skip = new HashSet<string> { "Start", "Awake", "Update", "OnDestroy", "OnEnable", "OnDisable", ".ctor", ".cctor", "LateUpdate", "FixedUpdate" };
var map = new List<string> { "# class\tobfuscated\treal" };

foreach (var asm in ctx.Assemblies.Where(a => a.Definition.AssemblyName.Name is "Assembly-CSharp" or "Mirror"))
foreach (var type in asm.Types)
{
    var sb = new StringBuilder($"class {type.FullName}\n");
    foreach (var f in type.Fields) sb.AppendLine($"  field {f.FieldType} {f.Name}");
    foreach (var m in type.Methods)
    {
        sb.AppendLine($"  method {m.ReturnType} {m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType + " " + p.Name))})  @0x{m.UnderlyingPointer:X}");
        if (m.UnderlyingPointer == 0) continue;
        byte[] raw; try { raw = m.RawBytes.ToArray(); } catch { continue; }
        var dec = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(raw)); dec.IP = m.UnderlyingPointer;
        var strs = new List<string>(); var calls = new List<LibCpp2IL.Metadata.Il2CppMethodDefinition>();
        while (dec.IP < m.UnderlyingPointer + (ulong)raw.Length)
        {
            var ins = dec.Decode();
            if (ins.IsIPRelativeMemoryOperand)
                try { var u = LibCpp2IlMain.GetAnyGlobalByAddress(ins.IPRelativeMemoryAddress); if (u?.Type == MetadataUsageType.StringLiteral) { strs.Add(u.AsLiteral()); sb.AppendLine($"      str \"{u.AsLiteral().Replace("\n", "\\n")}\""); } } catch { }
            if (ins.FlowControl == FlowControl.Call && ins.Op0Kind == OpKind.NearBranch64)
            {
                var ms = LibCpp2IlMain.GetManagedMethodImplementationsAtAddress(ins.NearBranchTarget);
                if (ms is { Count: 1 }) { calls.Add(ms[0]); sb.AppendLine($"      call {ms[0].DeclaringType?.FullName}::{ms[0].Name}"); }
            }
        }
        if (skip.Contains(m.Name)) continue;
        string found = null;
        foreach (var s in strs) { var mm = rxGuard.Match(s); if (mm.Success && mm.Groups[2].Value == type.FullName) { found = mm.Groups[3].Value; break; } }
        if (found == null)
        {
            var hs = strs.Select(s => rxHandler.Match(s)).Where(x => x.Success).Select(x => x.Groups[2].Value).Distinct().ToList();
            if (hs.Count == 1) found = "InvokeUserCode_" + hs[0];
        }
        if (found == null)
        {
            var ss = strs.Select(s => rxSig.Match(s)).Where(x => x.Success && x.Groups[1].Value == type.FullName).Select(x => x.Groups[2].Value).Distinct().ToList();
            if (ss.Count == 1) found = ss[0];
        }
        if (found != null && found != m.Name) map.Add($"{type.FullName}\t{m.Name}\t{found}");
    }
    File.WriteAllText(Path.Combine(outDir, "strings", Regex.Replace(type.FullName, @"[<>/\\:*?""|]", "_") + ".txt"), sb.ToString());
}
File.WriteAllLines(Path.Combine(outDir, "namemap.tsv"), map.Distinct());
Console.WriteLine($"Готово: {map.Count - 1} имён -> {outDir}");
