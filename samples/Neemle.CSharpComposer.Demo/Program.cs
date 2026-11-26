// Neemle.CSharpComposer demo: uses only the public API. Exit code 1 means a check failed.
// dotnet run --project samples/Neemle.CSharpComposer.Demo -c Release [-- --iterations N] [--skip-compile]
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Neemle.CSharpComposer;
using Neemle.CSharpComposer.Model;

int iterations = args.SkipWhile(a => a != "--iterations").Skip(1).Select(int.Parse).FirstOrDefault(10);
int failures = 0;
void Check(bool ok, string what) { failures += ok ? 0 : 1; Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}"); }
(NodePath Path, T Node) Find<T>(CsDocument d, Func<T, bool> match) where T : CsNode
{
    var x = d.Descendants().First(x => x.Node is T n && match(n));
    return (x.Path, (T)x.Node);
}

Console.WriteLine("== 1. New document from scratch (C# 14 target, so the .NET 10 compiler accepts it) ==");
CsDocument scratch = Cs.Document(CsLanguageVersion.CSharp14).Namespace("Demo.Generated")
    .Class("User").Public()
        .Property<string>("Name").Public().Required().Get().Init().End()
        .Method<int>("Twice").Public().Parameter<int>("input")
            .Body(Cs.Return(Cs.Binary(CsOperator.Multiply, Cs.Id("input"), Cs.Literal(2)))).End()
    .End();
byte[] scratchBytes = scratch.ToBytes();
using (var s = new MemoryStream()) { scratch.WriteTo(s); Check(s.ToArray().SequenceEqual(scratchBytes), "WriteTo(Stream) == ToBytes()"); }
Console.Write(Encoding.UTF8.GetString(scratchBytes));

Console.WriteLine("== 2. Parse CRLF source from byte[] and from a positioned Stream, edit, emit ==");
const string untouched = "    // Never edited.\r\n    public static class Untouched\r\n    {\r\n        public const string Text = \"a // b\";\r\n    }\r\n";
string source = "// Preamble owned by the document.\r\nnamespace Demo.Shop\r\n{\r\n    public class Orders\r\n    {\r\n" +
    "        private const int Tax = 20;\r\n\r\n        // Net plus tax.\r\n        public int Total(int net) => net + net * Tax / 100;\r\n\r\n" +
    "        /// <summary>Loyalty discount.</summary>\r\n        // Moves to Pricing.\r\n        public static int Discount(int amount)\r\n" +
    "        {\r\n            return amount - amount / 10; // ten percent\r\n        }\r\n    }\r\n\r\n" +
    "    public class Pricing\r\n    {\r\n        public int   Rate( ){return 20;}\r\n    }\r\n\r\n" + untouched + "}\r\n";
byte[] input = Encoding.UTF8.GetBytes(source);
var parsed = Cs.Parse(input);
Check(parsed.Diagnostics.Count == 0 && parsed.Coverage.ToString() == "Typed", $"Cs.Parse(byte[]): {parsed.Diagnostics.Count} diagnostics, {parsed.Coverage}");
Check(parsed.Root.ToBytes().SequenceEqual(input), "untouched parse -> ToBytes() is byte-identical");
using var inStream = new MemoryStream();
inStream.Write("HEADER|"u8);
inStream.Write(input);
inStream.Position = 7;
CsDocument doc = Cs.Parse(inStream).Root;
Check(inStream.CanRead && inStream.Position == inStream.Length && doc.ToBytes().SequenceEqual(input),
    "Cs.Parse(Stream) read from position 7 to EOF, left the stream open, same bytes as byte[]");

var total = Find<CsMethod>(doc, m => m.Name == "Total");
doc = doc.Replace(total.Path, total.Node with { Name = "GrossTotal" });
var orders = Find<CsTypeDeclaration>(doc, t => t.Name == "Orders");
doc = doc.Insert(orders.Path, orders.Node.Children.Count, new CsMethod(Cs.Int, "Half")
{
    Modifiers = new("public", "static"), Parameters = new(new CsParameter(Cs.Int, "amount")),
    Body = CsBody.FromExpression(Cs.Binary(CsOperator.Divide, Cs.Id("amount"), Cs.Literal(2)))
});
var discount = Find<CsMethod>(doc, m => m.Name == "Discount");
var pricing = Find<CsTypeDeclaration>(doc, t => t.Name == "Pricing");
doc = doc.Move(discount.Path, pricing.Path, pricing.Node.Children.Count);
doc = doc.Reformat(Find<CsMethod>(doc, m => m.Name == "Rate").Path);
byte[] edited = doc.ToBytes();
string text = Encoding.UTF8.GetString(edited);
Console.Write(text.Replace("\r\n", "¶\n"));
Console.WriteLine("(¶ = CRLF)");

int Count(string part) => (text.Length - text.Replace(part, "").Length) / part.Length;
Check(Count("// Preamble owned by the document.\r\nnamespace Demo.Shop\r\n") == 1, "preamble byte-identical");
Check(Count(untouched) == 1, "Untouched class (comment, string literal) byte-identical");
Check(Count("        private const int Tax = 20;\r\n\r\n        // Net plus tax.\r\n        public int GrossTotal(") == 1,
    "rename changed only the name token; neighbours and blank line kept");
Check(Count("        /// <summary>Loyalty discount.</summary>\r\n        // Moves to Pricing.\r\n") == 1 &&
    text.IndexOf("Discount(", StringComparison.Ordinal) > text.IndexOf("class Pricing", StringComparison.Ordinal),
    "Discount moved into Pricing with its doc and line comments, exactly once");
Check(Count("return amount - amount / 10; // ten percent\r\n") == 1, "moved body and trailing comment intact");
Check(text.Contains("public int Rate()\r\n", StringComparison.Ordinal) && text.Contains("public static int Half(int amount)", StringComparison.Ordinal),
    "Rate reformatted, Half inserted");
Check(!text.Replace("\r\n", "").Contains('\n'), "every line break is still CRLF");
using var outStream = new MemoryStream();
outStream.Write("KEEP|"u8);
doc.WriteTo(outStream);
Check(outStream.CanWrite && outStream.ToArray().SequenceEqual("KEEP|"u8.ToArray().Concat(edited)),
    "WriteTo(Stream) wrote at the current position, kept earlier bytes, left the stream open");

Console.WriteLine("== 3. Compile the emitted bytes with the real C# compiler (dotnet build; no Roslyn reference) ==");
if (args.Contains("--skip-compile")) Console.WriteLine("  skipped");
else
{
    var dir = Directory.CreateTempSubdirectory("composer-demo-");
    File.WriteAllText(Path.Combine(dir.FullName, "Check.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
        "<TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion><Nullable>enable</Nullable>" +
        "<TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
    File.WriteAllBytes(Path.Combine(dir.FullName, "Scratch.cs"), scratchBytes);
    File.WriteAllBytes(Path.Combine(dir.FullName, "Edited.cs"), edited);
    var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", "build -nologo -v q")
        { WorkingDirectory = dir.FullName, RedirectStandardOutput = true };
    using var build = Process.Start(info)!;
    string log = build.StandardOutput.ReadToEnd();
    build.WaitForExit();
    foreach (var line in log.Split('\n').Where(l => l.Contains("warn", StringComparison.OrdinalIgnoreCase) || l.Contains("error", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine("    " + line.Trim());
    Check(build.ExitCode == 0, $"dotnet build of Scratch.cs + Edited.cs: exit {build.ExitCode} (C# 14, warnings as errors)");
    dir.Delete(true);
}

Console.WriteLine("== 4. parse -> edit -> render timings (no threshold) ==");
string load = File.Exists("/proc/loadavg") ? string.Join(' ', File.ReadAllText("/proc/loadavg").Split(' ').Take(3)) : "n/a";
Console.WriteLine($"  {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.RuntimeIdentifier}, {Environment.ProcessorCount} CPUs, " +
    $"{RuntimeInformation.OSDescription}; load avg {load}");
Console.WriteLine($"  3 warm-up + {iterations} measured iterations, one thread. Workload: N classes x M commented methods.");
Console.WriteLine("  Edit = rename one method + insert one + move one commented method to another class.");
Console.WriteLine($"  {"workload",-10}{"bytes",8}  {"stage",-15}{"median ms",11}{"min ms",9}{"max ms",9}{"alloc/op",14}");
foreach (var (classes, methods) in new[] { (1, 5), (4, 10), (10, 20) })
{
    var sb = new StringBuilder("namespace Bench\n{\n");
    for (int c = 0; c < classes; c++)
    {
        sb.Append($"    public class C{c}\n    {{\n");
        for (int m = 0; m < methods; m++)
            sb.Append($"        /// <summary>M{m}.</summary>\n        // Leading.\n        public int M{m}(int v)\n        {{\n" +
                $"            int r = v * {m} + {c}; // trailing\n            return r;\n        }}\n\n");
        sb.Append("    }\n");
    }
    byte[] bench = Encoding.UTF8.GetBytes(sb.Append("}\n").ToString());
    var samples = new Dictionary<string, List<(double Ms, long Bytes)>>();
    var outputs = new HashSet<string>();
    T Time<T>(int i, string stage, Func<T> run)
    {
        long a = GC.GetAllocatedBytesForCurrentThread(), t = Stopwatch.GetTimestamp();
        T result = run();
        var sample = (Stopwatch.GetElapsedTime(t).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - a);
        if (i >= 3) (samples.TryGetValue(stage, out var l) ? l : samples[stage] = new()).Add(sample);
        return result;
    }
    for (int i = 0; i < 3 + iterations; i++)
    {
        CsDocument d = Time(i, "parse", () => Cs.Parse(bench).Root);
        byte[] same = Time(i, "emit untouched", () => d.ToBytes());
        CsDocument e = Time(i, "edit", () =>
        {
            var m0 = Find<CsMethod>(d, m => m.Name == "M0");
            var x = d.Replace(m0.Path, m0.Node with { Name = "Renamed" });
            var c0 = Find<CsTypeDeclaration>(x, t => t.Name == "C0");
            x = x.Insert(c0.Path, c0.Node.Children.Count, new CsMethod(Cs.Int, "Added") { Body = CsBody.FromExpression(Cs.Literal(1)) });
            var last = Find<CsTypeDeclaration>(x, t => t.Name == $"C{classes - 1}");
            return x.Move(Find<CsMethod>(x, m => m.Name == "M1").Path, last.Path, last.Node.Children.Count);
        });
        byte[] output = Time(i, "emit edited", () => e.ToBytes());
        outputs.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(output)) + same.SequenceEqual(bench) + d.ToBytes().SequenceEqual(bench));
    }
    foreach (var (stage, list) in samples)
        Console.WriteLine($"  {$"{classes}x{methods}",-10}{bench.Length,8}  {stage,-15}{list.Select(s => s.Ms).Order().ElementAt(list.Count / 2),11:F2}" +
            $"{list.Min(s => s.Ms),9:F2}{list.Max(s => s.Ms),9:F2}{list.Average(s => s.Bytes),14:N0}");
    Check(outputs.Count == 1 && outputs.Single().EndsWith("TrueTrue"),
        $"{classes}x{methods}: untouched emit == input, parsed snapshot unchanged by edits, identical edited output in all runs");
}
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
