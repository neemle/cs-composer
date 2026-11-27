using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-13: in a pattern, `(X) or|and <pattern>` combines a parenthesized pattern with the next one; it is
// not a cast of the contextual keyword `or`/`and`. In a case label or switch arm, `(X) when` starts the
// when clause. A type keyword followed by `switch`, `is` or `as` continues the expression instead of
// declaring a variable named after the keyword. Every verdict below is the C# 14 compiler's (SDK 10.0.404):
// the [Fact]s compile the rows and compare.
[Trait("Category", "Unit")]
public sealed class PatternCombinatorCastTests
{
    public enum Verdict { Accept, Reject, Semantic }

    // Shape: the first pattern in the tree (" when" when its label or arm has a when clause), or the kinds
    // of the returned expression and its first operand for "Expr:" rows. Cast: the tree holds a cast of
    // `or`/`and` (an expression-context cast, or the compiler's cast reading of a rejected row).
    public sealed record Row(string Id, Verdict Verdict, string Body, string Shape, bool Cast = false, string Code = "");

    const string Parameters = "object o, bool b, int[] arr";
    const string Declarations = "record R(int A);\nclass A { public class B { public const int C = 1; } }\n";

    static IEnumerable<Row> Contexts(string id, string pattern, string shape, string scrutinee = "o")
    {
        yield return new(id + "i", Verdict.Accept, "return " + scrutinee + " is " + pattern + ";", shape);
        yield return new(id + "c", Verdict.Accept, "switch (" + scrutinee + ") { case " + pattern + ": return 1; } return 0;", shape);
        yield return new(id + "a", Verdict.Accept, "return " + scrutinee + " switch { " + pattern + " => 1, _ => 0 };", shape + ",Discard");
    }

    static readonly Row[] Rows =
    [
        // AC-1 / AC-2 (BA matrix rows A01-A23 and O1).
        .. Contexts("A01", "(A.B.C) or (null)", "Or(Constant,Constant)"),
        .. Contexts("A02", "(null) or (1)", "Or(Constant,Constant)"),
        .. Contexts("A03", "(string) or (int)", "Or(Parenthesized(Type:string),Parenthesized(Type:int))"),
        .. Contexts("A04", "(A.B.C) or null", "Or(Constant,Constant)"),
        .. Contexts("A05", "(K) or (1)", "Or(Constant,Constant)"),
        .. Contexts("A06", "(K) or 2", "Or(Constant,Constant)"),
        .. Contexts("A07", "(K) or K", "Or(Constant,Constant)"),
        .. Contexts("A08", "(null) or null", "Or(Constant,Constant)"),
        .. Contexts("A09", "(string) or null", "Or(Parenthesized(Type:string),Constant)"),
        .. Contexts("A10", "(int) and not (1)", "And(Parenthesized(Type:int),Not(Constant))"),
        .. Contexts("A11", "(A.B.C) and (int)", "And(Constant,Parenthesized(Type:int))"),
        .. Contexts("A12", "not (null) or (1)", "Or(Not(Constant),Constant)"),
        .. Contexts("A13", "(int) or (long) or (string)", "Or(Or(Parenthesized(Type:int),Parenthesized(Type:long)),Parenthesized(Type:string))"),
        .. Contexts("A14", "(K) and not 2", "And(Constant,Not(Constant))"),
        .. Contexts("A15", "(string) and not null", "And(Parenthesized(Type:string),Not(Constant))"),
        .. Contexts("A16", "((string)) or (int)", "Or(Parenthesized(Parenthesized(Type:string)),Parenthesized(Type:int))"),
        .. Contexts("A17", "(int[]) or (string)", "Or(Parenthesized(Type:int[]),Parenthesized(Type:string))"),
        .. Contexts("A18", "(K) or (-1)", "Or(Constant,Constant)"),
        .. Contexts("A19", "(K) or \"s\"", "Or(Constant,Constant)"),
        .. Contexts("A20", "(K) or ~1", "Or(Constant,Constant)"),
        .. Contexts("A21", "(K) or {}", "Or(Constant,Recursive)"),
        .. Contexts("A22", "(K) or not 2", "Or(Constant,Not(Constant))"),
        .. Contexts("A23", "(K) or int", "Or(Constant,Type:int)"),
        .. Contexts("D01", "(float) or (double)", "Or(Parenthesized(Type:float),Parenthesized(Type:double))"),
        .. Contexts("D02", "((int[]) or (string))", "Parenthesized(Or(Parenthesized(Type:int[]),Parenthesized(Type:string)))"),
        .. Contexts("D03", "(int[]) and not null", "And(Parenthesized(Type:int[]),Not(Constant))"),
        .. Contexts("D04", "(int[]) and { Length: 0 }", "And(Parenthesized(Type:int[]),Recursive)"),
        // AC-3: relational operands after the combinator, once a silent misparse in is-expressions.
        .. Contexts("B01", "(A.B.C) or < 0", "Or(Constant,Relational)"),
        .. Contexts("B02", "(K) or > 1", "Or(Constant,Relational)"),
        .. Contexts("B03", "(K) or <= 1", "Or(Constant,Relational)"),
        .. Contexts("B04", "(K) or >= 1", "Or(Constant,Relational)"),
        // Other tokens that start a pattern after the combinator (ARCH-2, probed on SDK 10.0.404).
        .. Contexts("F01", "(K) or .5", "Or(Constant,Constant)"),
        .. Contexts("F02", "(K) or 'c'", "Or(Constant,Constant)"),
        .. Contexts("F03", "(K) or true", "Or(Constant,Constant)"),
        .. Contexts("F04", "(K) or (long)2", "Or(Constant,Constant)"),
        .. Contexts("F05", "(K) or A.B.C", "Or(Constant,Constant)"),
        .. Contexts("F06", "(K) or @\"s\"", "Or(Constant,Constant)"),
        .. Contexts("F07", "(K) or $\"s\"", "Or(Constant,Constant)"),
        .. Contexts("F08", "(A.B.C) or (A.B.C)", "Or(Constant,Constant)"),
        .. Contexts("F09", "(int) or 2", "Or(Parenthesized(Type:int),Constant)"),
        .. Contexts("F10", "(K) or (string) or null", "Or(Or(Constant,Parenthesized(Type:string)),Constant)"),
        .. Contexts("F11", "(K) or (K) and (K)", "Or(Constant,And(Constant,Constant))"),
        .. Contexts("F12", "(K) and (K) or (2)", "Or(And(Constant,Constant),Constant)"),
        .. Contexts("F13", "((K) or (2)) and not (3)", "And(Parenthesized(Or(Constant,Constant)),Not(Constant))"),
        .. Contexts("F14", "not (K) and not (2)", "And(Not(Constant),Not(Constant))"),
        // `[` starts a list pattern on SDK 10.0.404 (SDK 10.0.100 read a cast).
        .. Contexts("L01", "(null) or [1]", "Or(Constant,List)", "arr"),
        .. Contexts("L02", "(null) or []", "Or(Constant,List)", "arr"),
        .. Contexts("L03", "(int[]) or [1, ..]", "Or(Parenthesized(Type:int[]),List)", "arr"),
        // AC-5: forms that already parsed keep their shape.
        .. Contexts("E01", "not ((1) or (2))", "Not(Parenthesized(Or(Constant,Constant)))"),
        .. Contexts("E02", "((1)) or 2", "Or(Constant,Constant)"),
        .. Contexts("E03", "(1) or (2)", "Or(Constant,Constant)"),
        .. Contexts("E04", "(1)", "Constant"),
        .. Contexts("E05", "(A.B.C)", "Constant"),
        .. Contexts("E06", "(K) and (> 0)", "And(Constant,Parenthesized(Relational))"),
        new("E07", Verdict.Accept, "return o is (K) or (2) ? 1 : 0;", "Or(Constant,Constant)"),
        new("E08", Verdict.Accept, "return o is (K) or (2) && b;", "Or(Constant,Constant)"),
        // AC-5: `or`/`and` as cast operands outside patterns, and a keyword run whose last keyword is the operand.
        new("X01", Verdict.Accept, "int or = 1; long y = (long)or; return y;", "", Cast: true),
        new("X02", Verdict.Accept, "A.B or = null; object y = (A.B)or; return y;", "", Cast: true),
        new("X03", Verdict.Accept, "int and = 1; long y = (long)and + 1; return y;", "", Cast: true),
        new("X04", Verdict.Accept, "const int or = 2; return o is (int)or;", "Constant", Cast: true),
        new("X05", Verdict.Accept, "const int or = 2; return o is (int)or or > 5;", "Or(Constant,Relational)", Cast: true),
        new("X06", Verdict.Accept, "const int or = 2; return o is (K) or or;", "Or(Constant,Constant)"),
        new("X07", Verdict.Accept, "const int or = 2; return o is (1 + (int)or);", "Constant", Cast: true),
        new("X08", Verdict.Accept, "const int or = 2; return o is (int)or is bool;", "Expr:Is(Is)", Cast: true),
        new("X09", Verdict.Accept, "const int and = 2; return o is (K) or and;", "Or(Constant,Constant)"),
        // O2: `(X) when` in a case label or switch arm (compiler-accepted on SDK 10.0.404).
        new("W01", Verdict.Accept, "switch (o) { case (string) when b: return 1; } return 0;", "Parenthesized(Type:string) when"),
        new("W02", Verdict.Accept, "return o switch { (string) when b => 1, _ => 0 };", "Parenthesized(Type:string) when,Discard"),
        new("W03", Verdict.Accept, "switch (o) { case (K) when b: return 1; } return 0;", "Constant when"),
        new("W04", Verdict.Accept, "switch (o) { case (int[]) when b: return 1; } return 0;", "Parenthesized(Type:int[]) when"),
        new("W05", Verdict.Accept, "return o switch { (K) when b => 1, _ => 0 };", "Constant when,Discard"),
        new("W06", Verdict.Accept, "return o switch { (A.B.C) when b => 1, _ => 0 };", "Constant when,Discard"),
        new("W07", Verdict.Accept, "switch (o) { case (K) or (2) when b: return 1; } return 0;", "Or(Constant,Constant) when"),
        new("W08", Verdict.Accept, "switch (o) { case (A.B.C) when b: return 1; } return 0;", "Constant when"),
        new("W09", Verdict.Accept, "switch (o) { case (K) when (b): return 1; } return 0;", "Constant when"),
        new("W10", Verdict.Accept, "switch (o) { case (string) or (int) when b: return 1; case (K) and not (2) when !b: return 2; } return 0;",
            "Or(Parenthesized(Type:string),Parenthesized(Type:int)) when,And(Constant,Not(Constant)) when"),
        // e6519c0a: `switch`, `is` and `as` after a type continue the expression (CS8848 is a warning).
        new("S01", Verdict.Accept, "return o is string switch { _ => 1 };", "Expr:Switch(Is)"),
        new("S02", Verdict.Accept, "return o is int switch { _ => 1 };", "Expr:Switch(Is)"),
        new("S03", Verdict.Accept, "return o is int[] switch { _ => 1 };", "Expr:Switch(Is)"),
        new("S04", Verdict.Accept, "return o is string is bool;", "Expr:Is(Is)"),
        new("S05", Verdict.Accept, "return o is string as object;", "Expr:As(Is)"),
        new("S06", Verdict.Accept, "return o is A.B is bool;", "Expr:Is(Is)"),
        new("S07", Verdict.Accept, "return o is A.B as object;", "Expr:As(Is)"),
        new("S08", Verdict.Accept, "return o is string with;", "Declaration:string"),
        new("S09", Verdict.Accept, "return o is string @switch;", "Declaration:string"),
        // O3: `T?[]` after `is` is an array of a nullable type, not a conditional element access.
        new("N01", Verdict.Accept, "return o is int?[];", "Is:int?[]"),
        new("N02", Verdict.Accept, "return o is string?[];", "Is:string?[]"),
        new("N03", Verdict.Accept, "return o is int?[][];", "Is:int?[][]"),
        new("N04", Verdict.Accept, "return o is int?[,];", "Is:int?[,]"),
        new("N05", Verdict.Accept, "return o is int?[] && b;", "Is:int?[]"),
        new("N06", Verdict.Accept, "return o is int?[] || b;", "Is:int?[]"),
        new("N07", Verdict.Accept, "return o is int?[] == b;", "Is:int?[]"),

        // AC-4: the compiler reads a cast before `+`, `-`, `*` (and `&`, `^`) and rejects the row; Composer keeps the cast.
        .. Rejected("C01", "(K) or -1"),
        .. Rejected("C02", "(K) or +1"),
        .. Rejected("C03", "(K) and -1"),
        .. Rejected("C04", "(int) or -1"),
        .. Rejected("C05", "(K) or *K"),
        new("C06", Verdict.Reject, "return o is (A.B.C) or (A.B.C) or -1;", "Or(Constant,Constant)", Cast: true),
        new("C07", Verdict.Reject, "switch (o) { case (K) or &K: return 1; } return 0;", "Partial"),
        new("C08", Verdict.Reject, "return o switch { (K) or ^1 => 1, _ => 0 };", "Partial"),
        // A relational operand is a plain expression: `> (K) or 2` is the cast `(K)or` followed by `2`.
        new("C09", Verdict.Reject, "return o is > (K) or 2;", "Partial"),
        new("C10", Verdict.Reject, "return o is (string) or (int) switch { _ => 1 };", "Partial"),
        new("C11", Verdict.Reject, "return o is string with { };", "Partial"),
        new("C12", Verdict.Reject, "return o is int with { };", "Partial"),
        new("C13", Verdict.Reject, "return o is R with { };", "Partial"),
        // Before `:`, the compiler reads `int ? [] : false` as a conditional (O3).
        new("C14", Verdict.Reject, "return b ? o is int?[] : false;", "Partial"),

        // Semantic errors only: Composer reads what the compiler reads.
        new("M01", Verdict.Semantic, "return o is (K) or [1];", "Or(Constant,List)", Code: "CS8985"),
        new("M02", Verdict.Semantic, "return o is (K) or var x;", "Or(Constant,Var)", Code: "CS8780"),
        // adb6386d: like every GENE-11 designation under `or`, Validate reports GEN076 where the compiler reports CS8780.
        new("M03", Verdict.Semantic, "return o is (string s or null);", "Parenthesized(Or(Declaration:string,Constant))", Code: "CS8780"),
        new("M04", Verdict.Semantic, "return o is string s or null;", "Or(Declaration:string,Constant)", Code: "CS8780"),
        // After a bare name, the compiler reads `R switch { … }` as a switch-expression constant (CS0119).
        new("M05", Verdict.Semantic, "return o is R switch { _ => 1 };", "Partial", Code: "CS0119"),
    ];

    static IEnumerable<Row> Rejected(string id, string pattern) =>
        Contexts(id, pattern, "Constant").Select(row => row with { Verdict = Verdict.Reject, Cast = true });

    public static TheoryData<string> Accepted() => new(Rows.Where(r => r.Verdict == Verdict.Accept).Select(r => r.Id));

    public static TheoryData<string> Others() => new(Rows.Where(r => r.Verdict != Verdict.Accept).Select(r => r.Id));

    static string Document(Row row) => Declarations + "static class C\n{\n    const int K = 1;\n    static object M(" + Parameters +
        ")\n    {\n        " + row.Body + "\n    }\n}\n";

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Compiler_accepted_rows_parse_typed_and_emit_untouched_source(string id)
    {
        var row = Rows.Single(r => r.Id == id);
        byte[] source = Encoding.UTF8.GetBytes(Document(row));
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        FormatCheck.FixedPoint(parsed.Root);
        var nodes = parsed.Root.Descendants().Select(n => n.Node).ToArray();
        Assert.Equal(row.Shape, Shape(nodes));
        Assert.Equal(row.Cast, nodes.Any(CastOfCombinatorKeyword));

        var tree = Cs.ParseTree(row.Body);
        Assert.Equal(ParseCoverage.Typed, tree.Coverage);
        Assert.Empty(tree.Diagnostics);
        FormatCheck.FixedPoint(tree.Root);
        // A symbol tree holds no statements, so only the patterns are compared.
        Assert.Equal(Shape(nodes, statements: false), Shape(tree.Root.Descendants().Select(n => n.Node).ToArray(), statements: false));
    }

    [Theory]
    [MemberData(nameof(Others))]
    public void Compiler_rejected_rows_keep_the_cast_reading_or_recover(string id)
    {
        var row = Rows.Single(r => r.Id == id);
        string document = Document(row);
        var parsed = Cs.Parse(document);
        var nodes = parsed.Root.Descendants().Select(n => n.Node).ToArray();
        var tree = Cs.ParseTree(row.Body);
        if (row.Shape == "Partial")
        {
            Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
            Assert.Equal(new[] { "PARSE002" }, parsed.Diagnostics.Select(d => d.Code));
            Assert.Contains(nodes, n => n is CsStatement { Kind: StatementKind.Raw });
            FormatCheck.FixedPoint(parsed.Root);
            Assert.Equal(ParseCoverage.Opaque, tree.Coverage);
            Assert.Equal(new[] { "PARSE001" }, tree.Diagnostics.Select(d => d.Code));
            return;
        }
        if (row.Code == "CS8780")
        {
            // GENE-19: the statement validation rejects (GEN076) is kept as raw source, never typed then thrown.
            Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
            Assert.Equal(new[] { ("PARSE002", "Variables cannot be declared within not or or patterns.") }, parsed.Diagnostics.Select(d => (d.Code, d.Message)));
            Assert.Contains(nodes, n => n is CsStatement { Kind: StatementKind.Raw });
            Assert.Empty(parsed.Root.Validate());
            FormatCheck.FixedPoint(parsed.Root);
            Assert.Contains(tree.Diagnostics, d => d.Code == "GEN076");
            return;
        }
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(row.Shape, Shape(nodes));
        Assert.Equal(row.Cast, nodes.Any(CastOfCombinatorKeyword));
        Assert.Empty(parsed.Root.Validate());
        FormatCheck.FixedPoint(parsed.Root);
        Assert.Empty(tree.Diagnostics);
        FormatCheck.FixedPoint(tree.Root);
    }

    // AC-3 / AC-6 / AC-7: the compiler accepts every Accept row, and the reformatted document compiles and
    // computes the same result for every probe.

    // Input that ends right after the type or the combinator stays a recovered fragment.
    [Theory]
    [InlineData("return o is string")]
    [InlineData("return o is (K) or")]
    [InlineData("return o is (K) or or")]
    public void Truncated_patterns_are_not_typed(string body)
    {
        var tree = Cs.ParseTree(body);
        Assert.Equal(ParseCoverage.Opaque, tree.Coverage);
        Assert.Contains(tree.Diagnostics, d => d.Code == "PARSE001");
    }

    const int ProbeCount = 20;

    static string RowsClass(string name, Row[] rows) => "static class " + name + "\n{\n    const int K = 1;\n" +
        string.Concat(rows.Select(r => "    static object R" + r.Id + "(" + Parameters + ")\n    {\n        " + r.Body + "\n    }\n")) + "}\n";

    const string Harness = """
        static class Harness
        {
            static void Main()
            {
                object[] probes = { 1, 2, 3, 0, -1, "s", "", null, 1L, 2.5f, 2.5d, 0.5d, 'c', true, false, new int[0], new[] { 1 }, new[] { 1, 2 }, new object(), 1u };
                int[][] arrays = { null, new int[0], new[] { 1 }, new[] { 1, 2 } };
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                foreach (var rows in new[] { typeof(RowsOrig), typeof(RowsD) })
                    foreach (var method in rows.GetMethods(flags))
                        for (int k = 0; k < probes.Length; k++)
                        {
                            string result;
                            try { result = method.Invoke(null, new object[] { probes[k], k % 2 == 0, arrays[k % 4] })?.ToString() ?? "null"; }
                            catch (System.Reflection.TargetInvocationException e) { result = "!" + e.InnerException.GetType().Name; }
                            Console.WriteLine("@@" + rows.Name + "|" + method.Name + "|" + k + "|" + result);
                        }
            }
        }

        """;

    static bool CastOfCombinatorKeyword(CsNode node)
    {
        if (node is not CsExpression { Kind: ExpressionKind.Cast } cast) return false;
        var operand = cast.Arguments[0];
        while (operand.Kind is not ExpressionKind.Identifier && operand.Arguments.Count > 0) operand = operand.Arguments[0];
        return operand is { Kind: ExpressionKind.Identifier, Text: "or" or "and" };
    }

    static string Shape(CsNode[] nodes, bool statements = true)
    {
        if (statements && nodes.OfType<CsStatement>().FirstOrDefault(s => s.Kind == StatementKind.Return)?.Expression is
            { Kind: ExpressionKind.Switch or ExpressionKind.Is or ExpressionKind.As } value && value.Arguments[0].Kind == ExpressionKind.Is)
            return "Expr:" + value.Kind + "(Is)";
        var labels = nodes.OfType<CsSwitchLabel>().Where(l => l.Pattern != null).Select(l => Pattern(l.Pattern!) + (l.When != null ? " when" : ""))
            .Concat(nodes.OfType<CsSwitchArm>().Select(a => Pattern(a.Pattern) + (a.When != null ? " when" : ""))).ToArray();
        if (labels.Length > 0) return string.Join(",", labels);
        if (statements && nodes.OfType<CsExpression>().FirstOrDefault(e => e is { Kind: ExpressionKind.Is, Type: not null }) is { } test)
            return "Is:" + TypeText(test.Type!);
        return nodes.OfType<CsPattern>().FirstOrDefault() is { } first ? Pattern(first) : "";
    }

    static string Pattern(CsPattern pattern) => pattern.Kind switch
    {
        PatternKind.Type or PatternKind.Declaration => pattern.Kind + ":" + TypeText(pattern.Type!),
        PatternKind.Constant or PatternKind.Relational or PatternKind.List or PatternKind.Recursive or PatternKind.Var or
            PatternKind.Discard => pattern.Kind.ToString(),
        _ => pattern.Kind + "(" + string.Join(",", pattern.Patterns.Select(Pattern)) + ")"
    };

    static string TypeText(CsType type) => type.Form switch
    {
        TypeForm.Array => TypeText(type.Element!) + "[" + new string(',', type.Rank - 1) + "]",
        TypeForm.Nullable => TypeText(type.Element!) + "?",
        _ => type.Name
    };

    // Integration: these tests build or run composed source with the real compiler host.
    [Trait("Category", "Integration")]
    public sealed class Compiler
    {
        [Fact]
        public async Task Compiler_accepts_every_accepted_row_and_its_reformat_keeps_the_meaning()
        {
            var rows = Rows.Where(r => r.Verdict == Verdict.Accept).ToArray();
            string document = RowsClass("RowsD", rows);
            var parsed = Cs.Parse(document);
            Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
            Assert.Empty(parsed.Diagnostics);
            FormatCheck.FixedPoint(parsed.Root);
            string program = "using System;\nusing System.Collections.Generic;\n" + Declarations + RowsClass("RowsOrig", rows) +
                parsed.Root.Reformat().Render() + "\n" + Harness;
            string output = await CompilerFixture.Output(program, "14.0");
            var results = output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("@@", StringComparison.Ordinal))
                .Select(l => l[2..].Split('|', 4)).ToLookup(p => p[0], p => (Key: p[1] + "|" + p[2], Value: p[3]));
            var original = results["RowsOrig"].ToDictionary(r => r.Key, r => r.Value);
            var reformatted = results["RowsD"].ToDictionary(r => r.Key, r => r.Value);
            Assert.Equal(rows.Length * ProbeCount, original.Count);
            Assert.Empty(original.Where(r => reformatted.GetValueOrDefault(r.Key) != r.Value)
                .Select(r => r.Key + ": original=" + r.Value + " reformatted=" + reformatted.GetValueOrDefault(r.Key)));
            // The probes discriminate: `(string) or (int)` accepts "s" and 1 but not 1L.
            Assert.Equal(new[] { "True", "True", "False" }, new[] { 5, 0, 8 }.Select(k => original["RA03i|" + k]));
        }

        [Theory]
        [InlineData(Verdict.Reject)]
        [InlineData(Verdict.Semantic)]
        public async Task Compiler_rejects_every_rejected_row(Verdict verdict)
        {
            var rows = Rows.Where(r => r.Verdict == verdict).ToArray();
            var source = new StringBuilder(Declarations).Append("static class Program { static void Main() { } }\nstatic class Rows\n{\nconst int K = 1;\n");
            for (int i = 0; i < rows.Length; i++)
                source.Append("#line ").Append((i + 1) * 1000).Append("\nstatic object R").Append(rows[i].Id).Append('(')
                    .Append(Parameters).Append(")\n{\n").Append(rows[i].Body).Append("\n}\n");
            source.Append("#line default\n}\n");
            var diagnostics = await CompilerFixture.Build(source.ToString(), "14.0");
            Assert.DoesNotContain(diagnostics, d => d.Line < 1000 || d.Line >= (rows.Length + 1) * 1000);
            var missing = rows.Select((row, i) => (row, codes: diagnostics.Where(d => d.Line / 1000 == i + 1).Select(d => d.Code).ToArray()))
                .Where(x => x.codes.Length == 0 || verdict == Verdict.Semantic && !x.codes.Contains(x.row.Code))
                .Select(x => x.row.Id + " [" + string.Join(",", x.codes) + "]");
            Assert.Empty(missing);
        }
    }
}
