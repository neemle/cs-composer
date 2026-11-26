using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

using Row = (string Id, string Source, string Compiler, ParenthesizedCastTests.Outcome Outcome, string Shape);

// GENE-18: a parenthesized cast receiver such as ((string)a).Length parsed as Parenthesized(Declaration<1-tuple>),
// reported Typed and then threw GEN011 on untouched emit. Compiler verdicts come from CompilerFixture (C# 14).
public sealed class ParenthesizedCastTests
{
    public enum Outcome { Typed, Raw }

    const string Usings = "using System; using System.Collections.Generic; using System.Threading.Tasks; ";
    const string Accept = "Accept";

    // Shape: pre-order Parenthesized / Cast<T> / Declaration<T> / Tuple expression nodes, T as written in source.
    static readonly Row[] Rows =
    [
        ("A01", "class C { object M(object a) => ((string)a).Length; }", Accept, Outcome.Typed, "Parenthesized,Cast<string>"),
        ("A02", "class C { object M(object a) => ((int)a); }", Accept, Outcome.Typed, "Parenthesized,Cast<int>"),
        ("A03", "class C { string M(object a) => ((System.IDisposable)a).ToString(); }", Accept, Outcome.Typed, "Parenthesized,Cast<System.IDisposable>"),
        ("A04", Usings + "class C { IEnumerator<T> M<T>(object items) => ((IEnumerable<T>)items).GetEnumerator(); }", Accept, Outcome.Typed, "Parenthesized,Cast<IEnumerable<T>>"),
        ("A05", "class C { string M<T>(object x) where T : class => ((T)x).ToString(); }", Accept, Outcome.Typed, "Parenthesized,Cast<T>"),
        ("A06", "class C { int M(object a) => ((int[])a).Length; }", Accept, Outcome.Typed, "Parenthesized,Cast<int[]>"),
        ("A07", "class C { bool M(object a) => ((int?)a).HasValue; }", Accept, Outcome.Typed, "Parenthesized,Cast<int?>"),
        ("A08", Usings + "class C { int M(object a) => ((List<int>)a).Count; }", Accept, Outcome.Typed, "Parenthesized,Cast<List<int>>"),
        ("A09", "class C { void M(object a) { var n = ((string)a).Length; } }", Accept, Outcome.Typed, "Parenthesized,Cast<string>"),
        ("A10", "class C { void M(object a) { System.Console.WriteLine(((string)a).Length); } }", Accept, Outcome.Typed, "Parenthesized,Cast<string>"),
        ("A11", Usings + "class C { async Task M(object t) { await ((Task)t); } }", Accept, Outcome.Typed, "Parenthesized,Cast<Task>"),
        ("B01", "class C { int M(object a) => ((string)(object)a).Length; }", Accept, Outcome.Typed, "Parenthesized,Cast<string>,Cast<object>"),
        ("B02", "class C { int M(object a) => ((int)(long)a); }", Accept, Outcome.Typed, "Parenthesized,Cast<int>,Cast<long>"),
        ("B03", "class C { int M(object a) => (((string)a)).Length; }", Accept, Outcome.Typed, "Parenthesized,Parenthesized,Cast<string>"),
        ("B04", "class C { int M(object a) => ((string)(a)).Length; }", Accept, Outcome.Typed, "Parenthesized,Cast<string>,Parenthesized"),
        ("B05", "class C { char M(object a) => ((string)a)[0]; }", Accept, Outcome.Typed, "Parenthesized,Cast<string>"),
        ("B06", "class C { int? M(object a) => ((string)a)?.Length; }", Accept, Outcome.Typed, "Parenthesized,Cast<string>"),
        ("B07", "class C { char? M(object a) => ((string)a)?[0]; }", Accept, Outcome.Typed, "Parenthesized,Cast<string>"),
        ("B08", "#nullable enable\nclass C { string M(object? a) => ((string?)a)!; }", Accept, Outcome.Typed, "Parenthesized,Cast<string?>"),
        ("B09", "#nullable enable\nclass C { int M(object? a) => ((string?)a)!.Length; }", Accept, Outcome.Typed, "Parenthesized,Cast<string?>"),
        ("B10", "class C { int M(object f) => ((System.Func<int>)f)(); }", Accept, Outcome.Typed, "Parenthesized,Cast<System.Func<int>>"),
        ("B11", "class C { int M(object a, object b) => ((int)a) + ((int)b); }", Accept, Outcome.Typed, "Parenthesized,Cast<int>,Parenthesized,Cast<int>"),
        ("B12", "unsafe class C { int M(void* p) => ((int*)p)[0]; }", Accept, Outcome.Typed, "Parenthesized,Cast<int*>"),
        ("C01", "class C { string M(object a) => (a).ToString(); }", Accept, Outcome.Typed, "Parenthesized"),
        ("C02", "class C { string M(int a, int b) => (a + b).ToString(); }", Accept, Outcome.Typed, "Parenthesized"),
        ("C03", "class C { int M(int x) => (x) - 1; }", Accept, Outcome.Typed, "Parenthesized"),
        ("C04", Usings + "class C { Func<int,int> M() => (int x) => x; }", Accept, Outcome.Typed, ""),
        ("C05", "class C { bool M(object o) => o is (int); }", Accept, Outcome.Typed, "Parenthesized"),
        ("C06", "class C { bool M(object o) => o is (int or long); }", Accept, Outcome.Typed, ""),
        ("C07", "class C { System.Type M() => typeof((int, string)); }", Accept, Outcome.Typed, ""),
        ("D01", "class C { (int, string) M() => (1, \"a\"); }", Accept, Outcome.Typed, "Tuple"),
        ("D02", "class C { void M() { (int a, string b) t = default; } }", Accept, Outcome.Typed, ""),
        ("D03", "class C { void M() { (int x, int y) = (1, 2); } }", Accept, Outcome.Typed, "Tuple,Declaration<int>,Declaration<int>,Tuple"),
        ("D04", "class C { (int, string) M(object o) => ((int, string))o; }", Accept, Outcome.Typed, "Cast<(int, string)>"),
        ("D05", "class C { int M(object o) => (((int, string))o).Item1; }", Accept, Outcome.Typed, "Parenthesized,Cast<(int, string)>"),
        ("D06", Usings + "class C { void M(List<(int, int)> l) { foreach ((int k, int v) in l) { } } }", Accept, Outcome.Typed, "Tuple,Declaration<int>,Declaration<int>"),
        ("D07", "class C { object M(int a, int b) => (a, b); }", Accept, Outcome.Typed, "Tuple"),
        ("D08", "class C { (int, string)? M() => null; }", Accept, Outcome.Typed, ""),
        ("D09", "class C { (int, string)[] M() => new (int, string)[0]; }", Accept, Outcome.Typed, ""),
        ("D10", Usings + "class C { List<(int a, int b)> M() => new(); }", Accept, Outcome.Typed, ""),
        ("D11", "class C { (int, string) M() => default((int, string)); }", Accept, Outcome.Typed, ""),
        ("E01", "class C { (int) M() => 1; }", "CS8124", Outcome.Raw, ""),
        ("E02", "class C { void M() { var r = ((string) a); } }", "CS0103", Outcome.Typed, "Parenthesized,Cast<string>"),
        ("E03", "class C { int M(object a) => ((int))a; }", "CS1525", Outcome.Raw, ""),
        ("P01", "class C { bool M(object o) => o is (int x) && x > 0; }", Accept, Outcome.Typed, ""),
        ("P02", "class C { bool M(object o) => o is ((int x)) && x > 0; }", Accept, Outcome.Typed, ""),
        ("P04", "class C { void M() { foreach (int x in new int[0]) { } } }", Accept, Outcome.Typed, ""),
        ("Q01", "class Foo {} class C { object M(object a) => (Foo a); }", "CS1073", Outcome.Raw, ""),
        ("Q02", "class C { void N(object o){} void M() { N((int x)); } }", "CS1525", Outcome.Raw, ""),
        ("Q03", "class C { object M(object a) => (var a); }", "CS1073", Outcome.Raw, ""),
        ("Q04", "class C { System.Func<int,int> M() => (int x) => x; }", Accept, Outcome.Typed, ""),
        ("Q05", "class C { object M(object a) => ((string, int)a); }", "CS1525", Outcome.Raw, ""),
    ];

    public static TheoryData<string> Ids() => new(Rows.Select(row => row.Id));

    public static TheoryData<string> TypedIds() => new(Rows.Where(r => r.Outcome == Outcome.Typed).Select(r => r.Id));

    public static TheoryData<string> RawIds() => new(Rows.Where(r => r.Outcome == Outcome.Raw).Select(r => r.Id));

    static Row Find(string id) => Rows.Single(r => r.Id == id);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Typed_with_no_errors_implies_byte_identical_untouched_emit(string id)
    {
        var row = Find(id);
        byte[] source = Encoding.UTF8.GetBytes(row.Source);
        var parsed = Cs.Parse(source);
        Assert.Equal(row.Outcome == Outcome.Typed, parsed.Coverage == ParseCoverage.Typed);
        AssertNoInvalidTupleShapes(parsed.Root);
        if (parsed.Coverage == ParseCoverage.Typed && !parsed.HasErrors) Assert.Equal(source, parsed.Root.ToBytes());
        AssertUntouchedEmit(parsed.Root, row.Source);
    }

    [Theory]
    [MemberData(nameof(TypedIds))]
    public void Valid_rows_parse_typed_with_the_compiler_shape(string id)
    {
        var row = Find(id);
        var parsed = Cs.Parse(row.Source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.DoesNotContain(parsed.Root.Descendants(), n => IsRaw(n.Node));
        Assert.Equal(row.Shape, Shape(parsed.Root));
        AssertUntouchedEmit(parsed.Root, row.Source);
    }

    [Theory]
    [MemberData(nameof(RawIds))]
    public void Invalid_rows_are_partial_with_an_existing_positioned_diagnostic(string id)
    {
        var row = Find(id);
        var parsed = Cs.Parse(row.Source);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal("PARSE002", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.True(diagnostic.Length > 0 && diagnostic.Line == 1 && diagnostic.Column > 1, diagnostic.ToString());
        Assert.Contains(parsed.Root.Descendants(), n => IsRaw(n.Node));
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsExpression { Kind: ExpressionKind.Declaration or ExpressionKind.Cast });
        AssertUntouchedEmit(parsed.Root, row.Source);
    }

    [Theory]
    [InlineData("class C { (int) M() => 1; int N() => 2; }", "(int) M() => 1;", 10)]
    [InlineData("class C { int M(object a) => ((int))a; int N() => 2; }", "int M(object a) => ((int))a;", 10)]
    [InlineData("class Foo {} class C { object M(object a) => (Foo a); int N() => 2; }", "object M(object a) => (Foo a);", 23)]
    [InlineData("class C { object M(object a) => ((string, int)a); int N() => 2; }", "object M(object a) => ((string, int)a);", 10)]
    [InlineData("class C { void M() { N((int x)); return; } int N() => 2; }", "N((int x));", 21)]
    public void Only_the_smallest_enclosing_member_or_statement_is_held_as_raw(string source, string raw, int offset)
    {
        var parsed = Cs.Parse(source);
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal(("PARSE002", offset), (diagnostic.Code, diagnostic.Offset));
        var held = Assert.Single(parsed.Root.Descendants().Select(n => n.Node).Where(IsRaw));
        Assert.Equal(raw, (held switch { CsRawMember m => m.Source, CsStatement s => s.Text, _ => ((CsRaw)held).Text }).Trim());
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsMethod { Name: "N" });
        Assert.Equal(source, parsed.Root.Render());
    }

    static bool IsRaw(CsNode node) => node is CsRaw or CsRawMember or CsStatement { Kind: StatementKind.Raw };

    [Theory]
    [InlineData("A01", ".Length", ".Count", "Length", "Count")]
    [InlineData("A04", ".GetEnumerator()", ".GetAsyncEnumerator()", "GetEnumerator", "GetAsyncEnumerator")]
    [InlineData("B06", "?.Length", "?.Count", "Length", "Count")]
    public void Renaming_the_member_after_a_cast_receiver_rewrites_only_that_token(string id, string before, string after, string from, string to)
    {
        var row = Find(id);
        var document = Cs.Parse(row.Source).Root;
        var member = document.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Member } m && m.Text == from);
        var edited = document.Replace(member.Path, ((CsExpression)member.Node) with { Text = to });
        Assert.Empty(edited.Validate());
        string expected = row.Source.Replace(before, after, StringComparison.Ordinal);
        Assert.NotEqual(row.Source, expected);
        Assert.Equal(expected, edited.Render());
        Assert.Equal(Encoding.UTF8.GetBytes(expected), edited.ToBytes());
        var cast = Assert.Single(edited.Descendants().Select(n => n.Node).OfType<CsExpression>(), e => e.Kind == ExpressionKind.Cast);
        Assert.NotEqual(TypeForm.Tuple, cast.Type!.Form);
    }

    [Fact]
    public void Constructed_one_element_tuple_type_is_still_rejected_with_GEN011()
    {
        var document = Cs.Parse("class C { (int, string) M() => default; }").Root;
        var tuple = document.Descendants().First(n => n.Node is CsType { Form: TypeForm.Tuple });
        var thrown = Assert.Throws<GenValidationException>(() => document.Replace(tuple.Path, Cs.Tuple(new TupleElement(CsType.Int32))));
        Assert.Contains(thrown.Diagnostics, d => d.Code == "GEN011");
        var built = Cs.Document().Class("C").Method(Cs.Tuple(new TupleElement(CsType.Int32)), "M").Expression(Cs.Id("x")).End().End().Build();
        Assert.Contains(built.Validate(), d => d.Code == "GEN011");
        Assert.Contains(Assert.Throws<GenValidationException>(() => built.Render()).Diagnostics, d => d.Code == "GEN011");
    }

    [Fact]
    public async Task Compiler_verdicts_match_the_matrix()
    {
        // Syntax-valid rows share one build: each sits in its own namespace behind a #line marker.
        var batched = Rows.Where(r => r.Compiler is Accept or "CS0103").ToArray();
        var source = new StringBuilder("class Program { static void Main() { } }\n");
        for (int i = 0; i < batched.Length; i++)
            source.Append("namespace Row").Append(batched[i].Id).Append("\n{\n#line ").Append((i + 1) * 1000)
                .Append('\n').Append(batched[i].Source).Append("\n#line default\n}\n");
        var diagnostics = await CompilerFixture.Build(source.ToString(), "14.0");
        Assert.DoesNotContain(diagnostics, d => d.Line < 1000 || d.Line >= (batched.Length + 1) * 1000);
        var mismatches = batched.Select((row, i) => (row, codes: diagnostics.Where(d => d.Line / 1000 == i + 1).Select(d => d.Code).ToArray()))
            .Where(x => x.row.Compiler == Accept ? x.codes.Length != 0 : !x.codes.SequenceEqual(new[] { x.row.Compiler }))
            .Select(x => x.row.Id + " expected " + x.row.Compiler + " got [" + string.Join(",", x.codes) + "]").ToList();
        // Syntax-invalid rows build alone, so their error recovery cannot leak into another row. They build one at a
        // time so this test does not starve the suite's other compiler builds of their 60-second budget.
        var alone = Rows.Except(batched).ToArray();
        foreach (var row in alone)
        {
            var codes = (await CompilerFixture.Build(row.Source + "\nclass Program { static void Main() { } }\n", "14.0")).Select(d => d.Code).ToArray();
            if (!codes.Contains(row.Compiler)) mismatches.Add(row.Id + " expected " + row.Compiler + " got [" + string.Join(",", codes) + "]");
        }
        Assert.Empty(mismatches);
        Assert.Equal(Rows.Length, batched.Length + alone.Length);
    }

    static void AssertUntouchedEmit(CsDocument document, string source)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        Assert.Equal(source, document.Render());
        Assert.Equal(bytes, document.ToBytes());
        using var stream = new MemoryStream();
        document.WriteTo(stream);
        Assert.Equal(bytes, stream.ToArray());
    }

    internal static void AssertNoInvalidTupleShapes(CsNode root)
    {
        foreach (var node in Walk(root))
        {
            Assert.False(node is CsType { Form: TypeForm.Tuple } tuple && tuple.Elements.Count < 2, "Tuple type with fewer than two elements.");
            Assert.False(node is CsExpression { Kind: ExpressionKind.Parenthesized } p && p.Arguments.Count == 1 &&
                p.Arguments[0] is { Kind: ExpressionKind.Declaration, Arguments.Count: 0 }, "Declaration directly inside a parenthesized expression.");
        }
    }

    internal static IEnumerable<CsNode> Walk(CsNode root)
    {
        var pending = new Stack<CsNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--) pending.Push(node.Children[i]);
        }
    }

    static string Shape(CsDocument document) => string.Join(",", Walk(document).OfType<CsExpression>()
        .Where(e => e.Kind is ExpressionKind.Parenthesized or ExpressionKind.Cast or ExpressionKind.Declaration or ExpressionKind.Tuple)
        .Select(e => e.Kind + (e.Type == null ? "" : "<" + TypeText(e.Type) + ">")));

    static string TypeText(CsType type) => type.Form switch
    {
        TypeForm.Generic => TypeText(type.Element!) + "<" + string.Join(", ", type.Arguments.Select(TypeText)) + ">",
        TypeForm.Array => TypeText(type.Element!) + "[" + new string(',', type.Rank - 1) + "]",
        TypeForm.Nullable => TypeText(type.Element!) + "?",
        TypeForm.Pointer => TypeText(type.Element!) + "*",
        TypeForm.Tuple => "(" + string.Join(", ", type.Elements.Select(e => TypeText(e.Type) + (e.Name == null ? "" : " " + e.Name))) + ")",
        TypeForm.Nested => TypeText(type.Element!) + "." + type.Name,
        _ => type.Name,
    };
}
