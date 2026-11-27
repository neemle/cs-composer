// Fails unless line, branch and method coverage of src/Neemle.CSharpComposer are all 100%.
//
//   dotnet run tools/CoverageGate.cs -- <coverage.cobertura.xml|results-dir> <category>
//
// <coverage.cobertura.xml> is a Coverlet "XPlat Code Coverage" report, or a directory that holds exactly one.
// <category> names the test run (Unit, Integration) in every message. In a directory, copies under an In/ directory
// are ignored: the trx logger copies each collector attachment into its deployment folder
// (<results-dir>/<run-name>/In/<machine>/coverage.cobertura.xml) next to the
// <results-dir>/<guid>/coverage.cobertura.xml the collector writes.
//
// This is a .NET 10 file-based app. It uses the base class library only, so it restores no package, and it is not part
// of the solution or of any project. Run it from the repository root so that global.json selects the .NET 10 SDK.
//
// Exit codes: 0 when all three metrics are 100%; 1 when one is below 100% (or the report cannot be read);
// 2 on a usage error, a missing report, a missing package, or a results directory that does not hold exactly one
// report.
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property TreatWarningsAsErrors=true

using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

const string Package = "Neemle.CSharpComposer";
const string ReportName = "coverage.cobertura.xml";

if (args.Length != 2)
{
    Console.Error.Write($"usage: dotnet run tools/CoverageGate.cs -- <{ReportName}|results-dir> <category>\n");
    return 2;
}

var report = args[0];
var category = args[1];

if (Directory.Exists(report))
{
    string? found = null;
    var count = 0;
    foreach (var candidate in FindReports(report))
    {
        // A trx logger deployment copy of the collector attachment: some directory below the results dir is named In.
        var relative = Path.GetRelativePath(report, candidate);
        if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).SkipLast(1).Contains("In"))
        {
            continue;
        }

        found = report.EndsWith('/') ? report + relative : report + "/" + relative;
        count++;
    }

    if (count != 1)
    {
        Console.Error.Write(
            $"coverage gate [{category}]: expected exactly one {ReportName} under {report}, found {count}.\n");
        return 2;
    }

    report = found!;
}

if (!File.Exists(report))
{
    Console.Error.Write($"coverage gate [{category}]: report {report} does not exist.\n");
    return 2;
}

try
{
    return Gate(report, category);
}
catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException
                                      or FormatException or OverflowException or InvalidDataException)
{
    // An unreadable report fails the gate with 1, as the earlier bash and python3 gate did.
    Console.Error.Write($"coverage gate [{category}]: cannot read {report}: {exception.Message}\n");
    return 1;
}

// Every regular coverage.cobertura.xml below root, like `find root -name coverage.cobertura.xml -type f`: symbolic
// links are neither listed nor followed, hidden directories are searched, and the name match is case-sensitive.
static IEnumerable<string> FindReports(string root)
{
    // find does not descend into a starting point that is itself a symbolic link, unless it is spelled with a
    // trailing slash.
    if (new DirectoryInfo(root).LinkTarget is not null && !root.EndsWith('/'))
    {
        return [];
    }

    var options = new EnumerationOptions
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseSensitive,
        MatchType = MatchType.Simple,
    };
    return Directory.EnumerateFiles(root, ReportName, options);
}

static int Gate(string report, string category)
{
    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
    XElement root;
    using (var reader = XmlReader.Create(report, settings))
    {
        root = XDocument.Load(reader).Root!;
    }

    var packages = root.DescendantsAndSelf("package").Where(p => (string?)p.Attribute("name") == Package).ToList();
    if (packages.Count == 0)
    {
        Console.Error.Write($"coverage gate [{category}]: package {Package} is missing from {report}.\n");
        return 2;
    }

    var lines = new Dictionary<(string? File, long Line), bool>();
    var branches = new Dictionary<(string? File, long Line), (long Covered, long Total)>();
    var methods = new Dictionary<(string? Class, string? Method, string? Signature), bool>();
    var condition = new Regex(@"\((\d+)/(\d+)\)", RegexOptions.CultureInvariant);
    foreach (var package in packages)
    {
        foreach (var cls in package.Descendants("class"))
        {
            var filename = (string?)cls.Attribute("filename");
            var classLines = cls.Element("lines")
                ?? throw new InvalidDataException($"class {(string?)cls.Attribute("name")} has no lines element");
            foreach (var line in classLines.Descendants("line"))
            {
                var key = (filename, ParseInt(line, "number"));
                lines[key] = (lines.TryGetValue(key, out var hit) && hit) || ParseInt(line, "hits") > 0;
                if ((string?)line.Attribute("branch") == "True")
                {
                    var match = condition.Match((string?)line.Attribute("condition-coverage") ?? "");
                    if (match.Success)
                    {
                        var covered = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        var total = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                        var previous = branches.TryGetValue(key, out var seen) ? seen : (0, total);
                        branches[key] = (Math.Max(previous.Covered, covered), Math.Max(previous.Total, total));
                    }
                }
            }

            foreach (var method in cls.Descendants("method"))
            {
                var key = ((string?)cls.Attribute("name"), (string?)method.Attribute("name"),
                    (string?)method.Attribute("signature"));
                var entered = method.Descendants("line").Any(l => ParseInt(l, "hits") > 0);
                methods[key] = (methods.TryGetValue(key, out var hit) && hit) || entered;
            }
        }
    }

    (string Name, long Covered, long Total, string Percent)[] results =
    [
        Metric("line", lines.Values.LongCount(hit => hit), lines.Count),
        Metric("branch", branches.Values.Sum(b => b.Covered), branches.Values.Sum(b => b.Total)),
        Metric("method", methods.Values.LongCount(hit => hit), methods.Count),
    ];

    var failed = false;
    foreach (var (name, covered, total, percent) in results)
    {
        var status = covered == total ? "ok" : "FAIL";
        Console.Out.Write($"coverage gate [{category}]: {name} coverage {percent}% ({covered}/{total}) {status}\n");
        failed |= covered != total;
    }

    if (!failed)
    {
        Console.Out.Write(
            $"coverage gate [{category}]: {Package} line, branch and method coverage are all 100%.\n");
        return 0;
    }

    var gaps = lines.Where(entry => !entry.Value).Select(entry => entry.Key)
        .Union(branches.Where(entry => entry.Value.Covered < entry.Value.Total).Select(entry => entry.Key))
        .Order(Comparer<(string? File, long Line)>.Create(CompareLocation))
        .ToList();
    Console.Out.Write($"coverage gate [{category}]: uncovered lines or branches ({gaps.Count}):\n");
    foreach (var gap in gaps.Take(400))
    {
        var detail = "";
        if (branches.TryGetValue(gap, out var branch))
        {
            detail = $" branches {branch.Covered}/{branch.Total}";
        }

        if (lines.TryGetValue(gap, out var hit) && !hit)
        {
            detail = " not hit" + detail;
        }

        Console.Out.Write($"  {gap.File ?? "None"}:{gap.Line}{detail}\n");
    }

    var unentered = methods.Where(entry => !entry.Value).Select(entry => entry.Key)
        .Order(Comparer<(string? Class, string? Method, string? Signature)>.Create(CompareMethod));
    foreach (var (cls, name, signature) in unentered)
    {
        Console.Out.Write($"  method never entered: {cls ?? "None"}::{name ?? "None"}{signature ?? "None"}\n");
    }

    foreach (var (name, covered, total, percent) in results.Where(r => r.Covered != r.Total))
    {
        Console.Out.Write($"::error title=Coverage gate ({category})::{category} {name} coverage of {Package} "
            + $"is {percent}% ({covered}/{total}); the gate requires 100%.\n");
    }

    return 1;
}

static long ParseInt(XElement element, string attribute)
{
    var text = (string?)element.Attribute(attribute)
        ?? throw new InvalidDataException($"<{element.Name}> has no {attribute} attribute");
    return long.Parse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}

static (string Name, long Covered, long Total, string Percent) Metric(string name, long covered, long total)
{
    var percent = total == 0 ? 100.0 : covered * 100.0 / total;
    return (name, covered, total, FormatTwoDecimals(percent));
}

// Formats a finite, non-negative double with two decimals exactly as Python's f"{value:.2f}": the exact binary value is
// rounded half to even. .NET's "F2" rounds a tie away from zero instead (0.125 gives 0.13 there, 0.12 in Python).
static string FormatTwoDecimals(double value)
{
    var bits = BitConverter.DoubleToInt64Bits(value);
    var exponent = (int)((bits >> 52) & 0x7FF);
    var fraction = bits & 0xF_FFFF_FFFF_FFFFL;
    var mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
    var power = exponent == 0 ? -1074 : exponent - 1075;

    // value = mantissa * 2^power, so value * 100 = scaled * 2^power.
    var scaled = new BigInteger(mantissa) * 100;
    BigInteger hundredths;
    if (power >= 0)
    {
        hundredths = scaled << power;
    }
    else
    {
        hundredths = scaled >> -power;
        var remainder = scaled - (hundredths << -power);
        var half = BigInteger.One << (-power - 1);
        if (remainder > half || (remainder == half && !hundredths.IsEven))
        {
            hundredths += 1;
        }
    }

    var whole = BigInteger.DivRem(hundredths, 100, out var cents);
    return whole.ToString(CultureInfo.InvariantCulture) + "."
        + ((int)cents).ToString("D2", CultureInfo.InvariantCulture);
}

// Python orders strings by code point; ordinal comparison of UTF-16 code units gives the same order for every path
// without characters outside the Basic Multilingual Plane.
static int CompareLocation((string? File, long Line) x, (string? File, long Line) y)
{
    var byFile = string.CompareOrdinal(x.File, y.File);
    return byFile != 0 ? byFile : x.Line.CompareTo(y.Line);
}

static int CompareMethod((string? Class, string? Method, string? Signature) x,
    (string? Class, string? Method, string? Signature) y)
{
    var byClass = string.CompareOrdinal(x.Class, y.Class);
    if (byClass != 0)
    {
        return byClass;
    }

    var byMethod = string.CompareOrdinal(x.Method, y.Method);
    return byMethod != 0 ? byMethod : string.CompareOrdinal(x.Signature, y.Signature);
}
