using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

using Row = (string Id, NotPatternDesignationTests.Context Context, string Parameters, string Body,
    NotPatternDesignationTests.Verdict Compiler);

public sealed class NotPatternDesignationTests
{
    public enum Context { Is, CaseLabel, SwitchArm }

    public enum Verdict { Accept, CS8780, CS8518 }

    const string Obj = "object o", Rec = "P o", Arr = "object[] o", Two = "object o, object q";

    static readonly Row[] Rows =
    [
        ("I01", Context.Is, Obj, "return o is not string s;", Verdict.Accept),
        ("I02", Context.Is, Obj, "return o is not var v;", Verdict.CS8518),
        ("I03", Context.Is, Obj, "return o is not (string s);", Verdict.Accept),
        ("I04", Context.Is, Obj, "return o is not not string s;", Verdict.Accept),
        ("I05", Context.Is, Obj, "return o is not { } s;", Verdict.Accept),
        ("I06", Context.Is, Rec, "return o is not { A: var v };", Verdict.Accept),
        ("I07", Context.Is, Obj, "return o is string s or int;", Verdict.CS8780),
        ("I08", Context.Is, Obj, "return o is (not string s);", Verdict.Accept),
        ("I09", Context.Is, Obj, "return o is not (string s and { Length: > 0 });", Verdict.Accept),
        ("I10", Context.Is, Obj, "return o is not string _;", Verdict.Accept),
        ("I11", Context.Is, Rec, "return o is not P(var a, var b);", Verdict.Accept),
        ("I12", Context.Is, Rec, "return o is not var (a, b);", Verdict.Accept),
        ("I13", Context.Is, Obj, "return o is not (not string s);", Verdict.Accept),
        ("I14", Context.Is, Rec, "return o is { A: not string s };", Verdict.CS8780),
        ("I15", Context.Is, Obj, "return o is not string s and int;", Verdict.CS8780),
        ("I16", Context.Is, Obj, "return o is int and not string s;", Verdict.CS8780),
        ("I17", Context.Is, Obj, "return o is not int or string s;", Verdict.CS8780),
        ("I18", Context.Is, Obj, "return o is not (int or string s);", Verdict.CS8780),
        ("I19", Context.Is, Obj, "return !(o is not string s);", Verdict.Accept),
        ("S01", Context.CaseLabel, Obj, "switch (o) { case not string s: return 1; } return 0;", Verdict.CS8780),
        ("S02", Context.CaseLabel, Obj, "switch (o) { case not var v: return 1; } return 0;", Verdict.CS8780),
        ("S03", Context.CaseLabel, Obj, "switch (o) { case not (string s): return 1; } return 0;", Verdict.CS8780),
        ("S04", Context.CaseLabel, Obj, "switch (o) { case not not string s: return 1; } return 0;", Verdict.CS8780),
        ("S05", Context.CaseLabel, Rec, "switch (o) { case not { A: var v }: return 1; } return 0;", Verdict.CS8780),
        ("S06", Context.CaseLabel, Obj, "switch (o) { case string s or int: return 1; } return 0;", Verdict.CS8780),
        ("S07", Context.CaseLabel, Obj, "switch (o) { case not string _: return 1; } return 0;", Verdict.Accept),
        ("E01", Context.SwitchArm, Obj, "return o switch { not string s => 1, _ => 0 };", Verdict.CS8780),
        ("E02", Context.SwitchArm, Obj, "return o switch { not var v => 1, _ => 0 };", Verdict.CS8780),
        ("E03", Context.SwitchArm, Obj, "return o switch { not (string s) => 1, _ => 0 };", Verdict.CS8780),
        ("E04", Context.SwitchArm, Obj, "return o switch { not not string s => 1, _ => 0 };", Verdict.CS8780),
        ("E05", Context.SwitchArm, Rec, "return o switch { not { A: var v } => 1, _ => 0 };", Verdict.CS8780),
        ("E06", Context.SwitchArm, Obj, "return o switch { string s or int => 1, _ => 0 };", Verdict.CS8780),
        ("E07", Context.SwitchArm, Obj, "return o switch { not string _ => 1, _ => 0 };", Verdict.Accept),
        ("J01", Context.Is, Obj, "return o is not (string s or int);", Verdict.CS8780),
        ("J02", Context.Is, Obj, "return o is not not (string s or int);", Verdict.CS8780),
        ("J03", Context.Is, Obj, "return o is not ((not string s));", Verdict.Accept),
        ("J04", Context.Is, Obj, "return o is not (object and not string s);", Verdict.CS8780),
        ("J05", Context.Is, Rec, "return o is not P(not string a, _);", Verdict.CS8780),
        ("J06", Context.Is, Obj, "return o is not var _;", Verdict.CS8518),
        ("J07", Context.Is, Two, "switch (o) { case int when q is not string s: return 1; } return 0;", Verdict.Accept),
        ("J08", Context.Is, Two, "return o switch { int when q is not string s => 1, _ => 0 };", Verdict.Accept),
        ("J09", Context.Is, Rec, "return o is not P { A: var a } p;", Verdict.Accept),
        ("J10", Context.Is, Arr, "return o is not [var a, ..];", Verdict.Accept),
        ("J11", Context.Is, Arr, "return o is not [.. var r];", Verdict.Accept),
        ("J12", Context.Is, Rec, "return o is not (var a, var b);", Verdict.Accept),
        ("J13", Context.Is, Obj, "return o is not string s && true;", Verdict.Accept),
        ("J14", Context.Is, Obj, "if (o is not string s) return false; return s.Length > 0;", Verdict.Accept),
        ("J15", Context.Is, Rec, "return o is not { A: not string a };", Verdict.CS8780),
        ("J16", Context.Is, Obj, "return o is not (not (string s or int));", Verdict.CS8780),
        ("P01", Context.Is, Obj, "return o is (string s);", Verdict.Accept),
        ("P02", Context.CaseLabel, Obj, "switch (o) { case (string s): return 1; } return 0;", Verdict.Accept),
        ("P03", Context.SwitchArm, Obj, "return o switch { (string s) => 1, _ => 0 };", Verdict.Accept),
        ("P04", Context.Is, Obj, "return o is ((string s));", Verdict.Accept),
        ("P05", Context.Is, Obj, "return o is (var v);", Verdict.Accept),
        ("P06", Context.Is, Obj, "return o is (string s) and { Length: > 0 };", Verdict.Accept),
        ("P07", Context.Is, Obj, "return o is (1);", Verdict.Accept),
        ("P08", Context.CaseLabel, Obj, "switch (o) { case (A.B): return 1; } return 0;", Verdict.Accept),
        ("P09", Context.SwitchArm, Obj, "return o switch { (A.B) => 1, _ => 0 };", Verdict.Accept),
        ("F01", Context.Is, Obj, "return o is not (string s) || s.Length == 0;", Verdict.Accept),
        ("F02", Context.Is, Obj, "return o is not (string s) ? 0 : 1;", Verdict.Accept),
        ("F03", Context.Is, Obj, "return o is (string s) ? s : \"\";", Verdict.Accept),
        ("F04", Context.Is, Obj, "return o is (int x) ? x : 0;", Verdict.Accept),
        ("F05", Context.Is, Obj, "return o is ((string s)) ? s : \"\";", Verdict.Accept),
        ("F06", Context.Is, Obj, "return o is (string s) && s.Length > 0;", Verdict.Accept),
        ("F07", Context.Is, Obj, "return o is (string s) || o is int;", Verdict.Accept),
        ("F08", Context.Is, Obj, "return o is (string s) == true;", Verdict.Accept),
        ("F09", Context.Is, Obj, "return o is (string s) is bool;", Verdict.Accept),
        ("F10", Context.Is, Obj, "return o is ((string s)) and not null;", Verdict.Accept),
        ("F11", Context.Is, Obj, "return o is ((string s)) and { };", Verdict.Accept),
        ("C01", Context.Is, Obj, "return o is (null);", Verdict.Accept),
        ("C02", Context.Is, Obj, "return o is (-1);", Verdict.Accept),
        ("C03", Context.Is, Obj, "return o is ((int)1);", Verdict.Accept),
        ("C04", Context.Is, "(int, int) o", "return o is (1, 2);", Verdict.Accept),
    ];

    public static TheoryData<string> Ids() => new(Rows.Select(row => row.Id));

    [Theory]
    [MemberData(nameof(Ids))]
    public void Composer_reports_GEN076_exactly_where_the_compiler_reports_CS8780(string id)
    {
        var row = Rows.Single(r => r.Id == id);
        var parsed = Cs.ParseTree(row.Body);
        Assert.Equal(row.Compiler == Verdict.CS8780, parsed.Diagnostics.Any(d => d.Code == "GEN076"));
        if (row.Compiler == Verdict.CS8780) return;
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw);
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsPattern);
        Assert.Equal(row.Body, parsed.Root.Render());
        Assert.Empty(Cs.ParseTree(parsed.Root.Reformat().Render()).Diagnostics);
        string document = "class C { object M(" + row.Parameters + ") { " + row.Body + " } }";
        var whole = Cs.Parse(document);
        Assert.Empty(whole.Diagnostics);
        Assert.Equal(document, whole.Root.Render());
    }

    [Theory]
    [InlineData("return o is (string s);", PatternKind.Parenthesized, PatternKind.Declaration)]
    [InlineData("return o is ((string s));", PatternKind.Parenthesized, PatternKind.Parenthesized)]
    [InlineData("return o is (var v);", PatternKind.Parenthesized, PatternKind.Var)]
    [InlineData("return o is (1);", PatternKind.Constant, null)]
    [InlineData("return o is (A.B);", PatternKind.Constant, null)]
    [InlineData("return o is (string);", PatternKind.Constant, null)]
    [InlineData("return o is (null);", PatternKind.Constant, null)]
    [InlineData("return o is (-1);", PatternKind.Constant, null)]
    [InlineData("return o is ((int)1);", PatternKind.Constant, null)]
    [InlineData("return o is (string s) ? s : \"\";", PatternKind.Parenthesized, PatternKind.Declaration)]
    [InlineData("return o is ((string s)) and not null;", PatternKind.And, PatternKind.Parenthesized)]
    public void Parenthesized_declarations_are_patterns_and_parenthesized_constants_stay_constants(
        string source, PatternKind outer, PatternKind? inner)
    {
        var parsed = Cs.ParseTree(source);
        var patterns = parsed.Root.Descendants().Select(n => n.Node).OfType<CsPattern>().ToArray();
        Assert.Equal(outer, patterns[0].Kind);
        Assert.Equal(inner, patterns.Skip(1).Select(x => (PatternKind?)x.Kind).FirstOrDefault());
        Assert.Equal(source, parsed.Root.Render());
    }

    [Fact]
    public async Task Compiler_verdicts_match_the_matrix() => Assert.Empty(await Mismatches(Rows, row => row.Body));

    [Fact]
    public async Task Reformatted_rows_without_CS8780_keep_their_compiler_verdict()
    {
        var rows = Rows.Where(row => row.Compiler != Verdict.CS8780).ToArray();
        Assert.Empty(await Mismatches(rows, row => Cs.ParseTree(row.Body).Root.Reformat().Render()));
    }

    static async Task<string[]> Mismatches(Row[] rows, Func<Row, string> body)
    {
        var source = new System.Text.StringBuilder("record P(object A, object B);\n")
            .Append("static class A { public const int B = 1; }\nclass Program { static void Main() { } }\n")
            .Append("static class Rows\n{\n");
        for (int i = 0; i < rows.Length; i++)
            source.Append("#line ").Append((i + 1) * 1000).Append("\nstatic object Row").Append(rows[i].Id)
                .Append('(').Append(rows[i].Parameters).Append(")\n{\n").Append(body(rows[i])).Append("\n}\n");
        source.Append("#line default\n}\n");
        var diagnostics = await CompilerFixture.Build(source.ToString(), "14.0");
        Assert.DoesNotContain(diagnostics, d => d.Line < 1000 || d.Line >= (rows.Length + 1) * 1000);
        return rows.Select((row, i) => (row, codes: diagnostics
                .Where(d => d.Line / 1000 == i + 1).Select(d => d.Code).ToHashSet()))
            .Where(x => !Matches(x.row.Compiler, x.codes))
            .Select(x => x.row.Id + " (" + x.row.Context + ") expected " + x.row.Compiler +
                " got [" + string.Join(",", x.codes) + "]")
            .ToArray();
    }

    static bool Matches(Verdict verdict, HashSet<string> codes) => verdict switch
    {
        Verdict.Accept => codes.Count == 0,
        Verdict.CS8780 => codes.Contains("CS8780"),
        _ => codes.Contains("CS8518") && !codes.Contains("CS8780"),
    };

    static CsPattern NotString() => Cs.NotPattern(Cs.DeclarationPattern(Cs.Type<string>(), "s"));

    [Fact]
    public void Dom_is_expression_accepts_a_declaration_under_not()
    {
        var test = new CsExpression(ExpressionKind.Is, "", new[] { Cs.Id("input") }) { Pattern = NotString() };
        Assert.Empty(SwitchTests.Document(Cs.Return(test)).Validate());
    }

    [Fact]
    public void Dom_switch_arm_rejects_a_declaration_under_not()
    {
        var statement = Cs.Return(Cs.SwitchExpression(Cs.Id("input"),
            Cs.Arm(NotString(), Cs.Literal(1)), Cs.Arm(Cs.DiscardPattern(), Cs.Literal(0))));
        Assert.Contains(SwitchTests.Document(statement).Validate(), d => d.Code == "GEN076");
    }

    [Fact]
    public void Dom_case_label_rejects_a_declaration_under_not()
    {
        var statement = Cs.Switch(Cs.Id("input"), Cs.Section(Cs.Body(Cs.Return(Cs.Literal(1))), Cs.Case(NotString())));
        Assert.Contains(SwitchTests.Document(statement).Validate(), d => d.Code == "GEN076");
    }
}
