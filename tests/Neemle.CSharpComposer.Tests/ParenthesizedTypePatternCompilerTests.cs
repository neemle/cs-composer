using System.Text;
using System.Text.RegularExpressions;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-14: a keyword or array type in pattern position, with or without parentheses, is a type
// pattern. It must never become a constant whose Reformat escapes the keyword (`(@string)`).
public sealed class ParenthesizedTypePatternCompilerTests
{
    public enum Context { Is, Nested, Case, Arm, Controls }

    [Flags]
    public enum Wrapper { Plain = 1, Shadow = 2, Both = Plain | Shadow }

    public sealed record Row(string Id, Context Context, Wrapper Wrapper, string Body, string Shape);

    const string Parameters = "object o, int i, string s, (object, object) t, object[] a, bool b";
    const string Usings = "using System;\nusing System.Collections.Generic;\n";
    const string PlainDeclarations = "const int @class = 2;\nconst int K = 3;\n";
    // AC-2: an escaped keyword in scope must not capture the reformatted predefined type.
    const string ShadowDeclarations = PlainDeclarations + "const int @string = 1;\nclass @int { }\n";

    static readonly string[] KeywordTypes =
        ["bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double",
         "decimal", "char", "string", "object", "void", "nint", "nuint"];

    static readonly Regex EscapedKeywordType = new(@"@(string|int|object|bool|decimal|nint|nuint)\b");

    static readonly Row[] Rows =
    [
        // AC-1 / AC-3, is-expressions.
        new("A01", Context.Is, Wrapper.Both, "return o is (string);", "Parenthesized,Type:string"),
        new("A02", Context.Is, Wrapper.Both, "return o is (int);", "Parenthesized,Type:int"),
        new("A03", Context.Is, Wrapper.Both, "return o is (object);", "Parenthesized,Type:object"),
        new("A04", Context.Is, Wrapper.Both, "return o is (bool);", "Parenthesized,Type:bool"),
        new("A05", Context.Is, Wrapper.Both, "return o is (decimal);", "Parenthesized,Type:decimal"),
        new("A06", Context.Is, Wrapper.Both, "return o is (nint);", "Parenthesized,Type:nint"),
        new("A07", Context.Is, Wrapper.Both, "return o is ((string));", "Parenthesized,Parenthesized,Type:string"),
        new("A08", Context.Is, Wrapper.Both, "return o is not (string);", "Not,Parenthesized,Type:string"),
        new("A09", Context.Is, Wrapper.Both, "return o is (1) and (int);", "And,Constant,Parenthesized,Type:int"),
        new("A10", Context.Is, Wrapper.Both, "return o is nint;", "Is:nint"),
        new("A11", Context.Is, Wrapper.Both, "return o is nuint;", "Is:nuint"),
        new("A12", Context.Is, Wrapper.Both, "return o is (string) && b;", "Parenthesized,Type:string"),
        new("A13", Context.Is, Wrapper.Both, "return o is (int) ? 1 : 0;", "Parenthesized,Type:int"),
        new("A14", Context.Is, Wrapper.Both, "return o is (string) == b;", "Parenthesized,Type:string"),
        new("A15", Context.Is, Wrapper.Both, "return o is (bool) || b;", "Parenthesized,Type:bool"),
        new("A16", Context.Is, Wrapper.Both, "return o is (int[]);", "Parenthesized,Type:int[]"),
        new("A17", Context.Is, Wrapper.Both, "return o is (string[]);", "Parenthesized,Type:string[]"),
        new("A18", Context.Is, Wrapper.Both, "return o is (List<int>[]);", "Parenthesized,Type:List<int>[]"),
        new("A19", Context.Is, Wrapper.Both, "return o is (int[]) ? (int.MaxValue) : 0;", "Parenthesized,Type:int[]"),
        new("A20", Context.Is, Wrapper.Both, "return o is (List<List<int>>[]);", "Parenthesized,Type:List<List<int>>[]"),
        new("A21", Context.Is, Wrapper.Both, "return o is (List<int>[][]);", "Parenthesized,Type:List<int>[][]"),
        new("A22", Context.Is, Wrapper.Both, "return o is (List<List<List<int>>>[]);", "Parenthesized,Type:List<List<List<int>>>[]"),
        new("A23", Context.Is, Wrapper.Both, "return o is (int[][]);", "Parenthesized,Type:int[][]"),
        // AC-1, nested patterns.
        new("B01", Context.Nested, Wrapper.Both, "return s is { Length: (int) };", "Recursive,Property,Parenthesized,Type:int"),
        new("B02", Context.Nested, Wrapper.Both, "return t is ((string), (int));", "Recursive,Parenthesized,Type:string,Parenthesized,Type:int"),
        new("B03", Context.Nested, Wrapper.Both, "return a is [(string), ..];", "List,Parenthesized,Type:string,Slice"),
        new("B04", Context.Nested, Wrapper.Both, "return a is [(int[]), (nint), ..];", "List,Parenthesized,Type:int[],Parenthesized,Type:nint,Slice"),
        new("B05", Context.Nested, Wrapper.Both, "return a is ([]) or [(object[])];", "Or,Parenthesized,List,List,Parenthesized,Type:object[]"),
        // QA F1: a parenthesized positional pattern is a pattern, not a tuple constant.
        new("P01", Context.Nested, Wrapper.Both, "return t is ((int, string));", "Parenthesized,Recursive,Type:int,Type:string"),
        new("P02", Context.Nested, Wrapper.Both, "return t is ((string, int));", "Parenthesized,Recursive,Type:string,Type:int"),
        new("P03", Context.Nested, Wrapper.Both, "return t is ((int[], string));", "Parenthesized,Recursive,Type:int[],Type:string"),
        new("P04", Context.Nested, Wrapper.Both, "return t is (((int, string)));", "Parenthesized,Parenthesized,Recursive,Type:int,Type:string"),
        new("P05", Context.Nested, Wrapper.Both, "return t is not ((int, string));", "Not,Parenthesized,Recursive,Type:int,Type:string"),
        new("P06", Context.Nested, Wrapper.Both, "return t is ((nint, nuint));", "Parenthesized,Recursive,Type:nint,Type:nuint"),
        new("P07", Context.Nested, Wrapper.Both, "return t is ((1, string));", "Parenthesized,Recursive,Constant,Type:string"),
        new("P08", Context.Nested, Wrapper.Both, "return t is ((int[], int[]));", "Parenthesized,Recursive,Type:int[],Type:int[]"),
        new("P09", Context.Nested, Wrapper.Both, "return t is ((1, 2));", "Parenthesized,Recursive,Constant,Constant"),
        new("P10", Context.Nested, Wrapper.Both, "return t is ((K, @class));", "Parenthesized,Recursive,Constant,Constant"),
        new("P11", Context.Nested, Wrapper.Both, "return t is ((string x, int y)) ? x.Length + y : -1;", "Parenthesized,Recursive,Declaration:string,Declaration:int"),
        new("P12", Context.Nested, Wrapper.Both, "return t is ((var x, var y)) ? x : y;", "Parenthesized,Recursive,Var,Var"),
        new("P13", Context.Nested, Wrapper.Both, "return o is ((int, string)[]);", "Parenthesized,Type:Tuple[]"),
        new("P14", Context.Nested, Wrapper.Both, "return o is not ((int, string)[][]);", "Not,Parenthesized,Type:Tuple[][]"),
        // AC-1 / AC-3, case labels.
        new("C01", Context.Case, Wrapper.Both, "switch (o) { case (string): return 1; case (int): return 2; case (object): return 3; } return 0;",
            "Parenthesized,Type:string,Parenthesized,Type:int,Parenthesized,Type:object"),
        new("C02", Context.Case, Wrapper.Both, "switch (o) { case (int[]): return 1; case (string[]): return 2; case (List<int>[]): return 3; } return 0;",
            "Parenthesized,Type:int[],Parenthesized,Type:string[],Parenthesized,Type:List<int>[]"),
        new("C03", Context.Case, Wrapper.Both, "switch (o) { case nint: return 1; case not (string): return 2; } return 0;",
            "Type:nint,Not,Parenthesized,Type:string"),
        new("C04", Context.Case, Wrapper.Both, "switch (t) { case ((int, string)): return 1; case ((string, int)): return 2; } return 0;",
            "Parenthesized,Recursive,Type:int,Type:string,Parenthesized,Recursive,Type:string,Type:int"),
        // AC-1 / AC-3, switch-expression arms.
        new("D01", Context.Arm, Wrapper.Both, "return o switch { (string) => 1, (int) => 2, (object) => 3, _ => 0 };",
            "Parenthesized,Type:string,Parenthesized,Type:int,Parenthesized,Type:object,Discard"),
        new("D02", Context.Arm, Wrapper.Both, "return o switch { (int[]) => 1, (string[]) => 2, (List<int>[]) => 3, _ => 0 };",
            "Parenthesized,Type:int[],Parenthesized,Type:string[],Parenthesized,Type:List<int>[],Discard"),
        new("D03", Context.Arm, Wrapper.Both, "return o switch { (nint) => 1, nuint => 2, _ => 0 };",
            "Parenthesized,Type:nint,Type:nuint,Discard"),
        new("D04", Context.Arm, Wrapper.Both, "return t switch { ((int, string)) => 1, ((nint, nuint)) => 2, _ => 0 };",
            "Parenthesized,Recursive,Type:int,Type:string,Parenthesized,Recursive,Type:nint,Type:nuint,Discard"),
        // AC-5: escaped names stay identifiers. Shapes are the 4e547ef values.
        new("F01", Context.Controls, Wrapper.Shadow, "return i is (@string);", "Constant"),
        new("F02", Context.Controls, Wrapper.Shadow, "return o is (@int);", "Constant"),
        new("F03", Context.Controls, Wrapper.Shadow, "switch (i) { case (@string): return 1; } return 0;", "Constant"),
        new("F04", Context.Controls, Wrapper.Shadow, "return o switch { (@int) => 1, _ => 0 };", "Constant,Discard"),
        new("G01", Context.Controls, Wrapper.Both, "return i is @class;", "Constant"),
        new("G02", Context.Controls, Wrapper.Both, "return i is (@class);", "Constant"),
        new("G03", Context.Controls, Wrapper.Both, "var @class = 1; return @class + 1;", ""),
        new("G04", Context.Controls, Wrapper.Both, "return (@class);", ""),
        new("G05", Context.Controls, Wrapper.Both, "return (string)o;", ""),
        // AC-6: forms that already work keep their 4e547ef shape.
        new("H01", Context.Controls, Wrapper.Both, "return o is (List<int>);", "Constant"),
        new("H02", Context.Controls, Wrapper.Both, "return o is (System.String);", "Constant"),
        new("H03", Context.Controls, Wrapper.Both, "return o is (global::System.String);", "Constant"),
        new("H04", Context.Controls, Wrapper.Both, "return o is (int[,]);", "Parenthesized,Type:int[,]"),
        new("H05", Context.Controls, Wrapper.Both, "return o is (string x) && x != null;", "Parenthesized,Declaration:string"),
        new("H06", Context.Controls, Wrapper.Both, "switch (o) { case (List<int>): return 1; } return 0;", "Constant"),
        new("H07", Context.Controls, Wrapper.Both, "return o switch { (List<int>) => 1, _ => 0 };", "Constant,Discard"),
        new("H08", Context.Controls, Wrapper.Both, "return o is string;", "Is:string"),
        new("H09", Context.Controls, Wrapper.Both, "return i is (K);", "Constant"),
        new("H10", Context.Controls, Wrapper.Both, "return i is (1);", "Constant"),
        new("H11", Context.Controls, Wrapper.Both, "return i is (int.MaxValue);", "Constant"),
        new("H12", Context.Controls, Wrapper.Both, "return o is (1) && b;", "Constant"),
        new("H15", Context.Controls, Wrapper.Both, "return b is (K > 1);", "Constant"),
        new("H16", Context.Controls, Wrapper.Both, "return i is (1 + 2);", "Constant"),
        new("H13", Context.Controls, Wrapper.Both, "return int.MaxValue + (long)nint.MaxValue + string.Empty.Length;", ""),

    ];

    public static TheoryData<string> Ids() => new(Rows.Select(row => row.Id));

    static string Method(Row row) => "class C { object M(" + Parameters + ") { " + row.Body + " } }";

    [Theory]
    [MemberData(nameof(Ids))]
    public void Keyword_and_array_types_in_pattern_position_parse_as_type_patterns(string id)
    {
        var row = Rows.Single(r => r.Id == id);
        string document = Method(row);
        var whole = Cs.Parse(document);
        Assert.Equal(ParseCoverage.Typed, whole.Coverage);
        Assert.Empty(whole.Diagnostics);
        Assert.Empty(whole.Root.Validate());
        Assert.Equal(document, whole.Root.Render());
        var nodes = whole.Root.Descendants().Select(n => n.Node).ToArray();
        Assert.Equal(row.Shape, Shape(nodes));
        AssertNoUnescapedKeywordIdentifier(nodes);

        var tree = Cs.ParseTree(row.Body);
        Assert.Equal(ParseCoverage.Typed, tree.Coverage);
        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.Validate());
        Assert.Equal(row.Body, tree.Root.Render());
        var treeNodes = tree.Root.Descendants().Select(n => n.Node).ToArray();
        Assert.Equal(PatternShape(nodes), PatternShape(treeNodes));
        AssertNoUnescapedKeywordIdentifier(treeNodes);
    }

    [Theory]
    [InlineData(Context.Is)]
    [InlineData(Context.Nested)]
    [InlineData(Context.Case)]
    [InlineData(Context.Arm)]
    [InlineData(Context.Controls)]
    public async Task Reformat_compiles_and_keeps_the_original_pattern_meaning(Context context)
    {
        foreach (var wrapper in new[] { Wrapper.Plain, Wrapper.Shadow })
        {
            var rows = Rows.Where(r => r.Context == context && r.Wrapper.HasFlag(wrapper)).ToArray();
            string output = await CompilerFixture.Output(Program(rows, wrapper), "14.0");
            var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("@@", StringComparison.Ordinal)).ToArray();
            var original = Results(lines, "RowsOrig");
            Assert.Equal(rows.Length * ProbeCount(wrapper), original.Count);
            foreach (string variant in new[] { "RowsT", "RowsD" })
            {
                var reformatted = Results(lines, variant);
                var mismatches = original.Keys.Where(k => !reformatted.TryGetValue(k, out var v) || v != original[k])
                    .Select(k => wrapper + " " + variant + " " + k + ": original=" + original[k] + " reformatted=" +
                        (reformatted.TryGetValue(k, out var v) ? v : "<missing>"))
                    .ToArray();
                Assert.Empty(mismatches);
            }
            if (context == Context.Is)
            {
                // The probes discriminate: "s" is a string and 1 is not, even with `const int @string = 1` in scope.
                Assert.Equal("True", original["RA01|0"]);
                Assert.Equal("False", original["RA01|2"]);
            }
        }
    }

    static int ProbeCount(Wrapper wrapper) => wrapper == Wrapper.Shadow ? 24 : 23;

    static Dictionary<string, string> Results(string[] lines, string variant) => lines
        .Where(l => l.StartsWith("@@" + variant + "|", StringComparison.Ordinal))
        .Select(l => l[(variant.Length + 3)..].Split('|', 3))
        .ToDictionary(p => p[0] + "|" + p[1], p => p[2]);

    static string Program(Row[] rows, Wrapper wrapper)
    {
        string declarations = wrapper == Wrapper.Shadow ? ShadowDeclarations : PlainDeclarations;
        string document = Usings + RowsClass("RowsD", declarations, rows, r => r.Body);
        var whole = Cs.Parse(document);
        Assert.Equal(ParseCoverage.Typed, whole.Coverage);
        Assert.Empty(whole.Diagnostics);
        Assert.Equal(document, whole.Root.Render());
        string documentReformat = whole.Root.Reformat().Render();
        Assert.Equal(EscapedKeywordType.Matches(document).Count, EscapedKeywordType.Matches(documentReformat).Count);

        var treeReformat = new Dictionary<string, string>();
        foreach (var row in rows)
        {
            var tree = Cs.ParseTree(row.Body);
            Assert.Equal(ParseCoverage.Typed, tree.Coverage);
            Assert.Empty(tree.Diagnostics);
            Assert.Equal(row.Body, tree.Root.Render());
            string text = tree.Root.Reformat().Render();
            Assert.Equal(EscapedKeywordType.Matches(row.Body).Count, EscapedKeywordType.Matches(text).Count);
            treeReformat[row.Id] = text;
        }

        return new StringBuilder(documentReformat).Append('\n')
            .Append(RowsClass("RowsOrig", declarations, rows, r => r.Body))
            .Append(RowsClass("RowsT", declarations, rows, r => treeReformat[r.Id]))
            .Append(Harness)
            .ToString();
    }

    static string RowsClass(string name, string declarations, Row[] rows, Func<Row, string> body) =>
        "static class " + name + "\n{\n" + declarations +
        string.Concat(rows.Select(r => "static object R" + r.Id + "(" + Parameters + ")\n{\n" + body(r) + "\n}\n")) + "}\n";

    const string Harness = """
        static class Harness
        {
            static void Main()
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var rows in new[] { typeof(RowsOrig), typeof(RowsT), typeof(RowsD) })
                {
                    var probes = new List<object> { "s", "", 1, 2, 3, int.MaxValue, (nint)1, (nuint)1, true, false, 1m,
                        new object(), null, new int[0], new string[0], new List<int>[0], new List<int>(),
                        new object[] { "x", 1 }, new object[] { new int[0], (nint)1 }, ((object)"x", (object)1), ((object)1, (object)"x"),
                        ((object)new int[0], (object)"x"), ((object)(nint)1, (object)(nuint)1) };
                    var shadow = rows.GetNestedType("int", System.Reflection.BindingFlags.NonPublic);
                    if (shadow != null) probes.Add(Activator.CreateInstance(shadow, true));
                    foreach (var method in System.Linq.Enumerable.OrderBy(rows.GetMethods(flags), m => m.Name, StringComparer.Ordinal))
                        for (int k = 0; k < probes.Count; k++)
                        {
                            object p = probes[k];
                            object[] args = { p, p is int n ? n : -1, p as string,
                                p is ValueTuple<object, object> v ? v : (p, p), p as object[] ?? new[] { p }, p is true };
                            string result;
                            try { result = method.Invoke(null, args)?.ToString()?.Replace(rows.Name + "+", "") ?? "null"; }
                            catch (System.Reflection.TargetInvocationException e) { result = "!" + e.InnerException.GetType().Name; }
                            Console.WriteLine("@@" + rows.Name + "|" + method.Name + "|" + k + "|" + result);
                        }
                }
            }
        }

        """;

    // AC-7: inputs the compiler rejects keep their 4e547ef parse outcome and byte-identical Render.
    // `(string) when b` compiles from SDK 10.0.404 on; GENE-13 re-baselined those two rows as typed patterns.
    [Theory]
    [InlineData("return o is (int?);", ParseCoverage.Partial, "PARSE002", ParseCoverage.Opaque, "PARSE001", null)]
    [InlineData("return o is (string?);", ParseCoverage.Partial, "PARSE002", ParseCoverage.Opaque, "PARSE001", null)]
    [InlineData("return o is (dynamic);", ParseCoverage.Typed, "", ParseCoverage.Typed, "", "Constant")]
    [InlineData("switch (o) { case (string) when b: return 1; } return 0;", ParseCoverage.Typed, "", ParseCoverage.Typed, "", "Parenthesized,Type:string")]
    [InlineData("return o switch { (string) when b => 1, _ => 0 };", ParseCoverage.Typed, "", ParseCoverage.Typed, "", "Parenthesized,Type:string,Discard")]
    [InlineData("return o is (void);", ParseCoverage.Typed, "", ParseCoverage.Typed, "", "Constant")]
    public void Compiler_rejected_inputs_keep_their_parse_outcome(string body, ParseCoverage documentCoverage,
        string documentCodes, ParseCoverage treeCoverage, string treeCodes, string? shape)
    {
        string document = "class C { object M(" + Parameters + ") { " + body + " } }";
        var whole = Cs.Parse(document);
        Assert.Equal(documentCoverage, whole.Coverage);
        Assert.Equal(documentCodes, string.Join(",", whole.Diagnostics.Select(d => d.Code)));
        Assert.Equal(document, whole.Root.Render());
        if (shape != null) Assert.Equal(shape, Shape(whole.Root.Descendants().Select(n => n.Node)));
        var tree = Cs.ParseTree(body);
        Assert.Equal(treeCoverage, tree.Coverage);
        Assert.Equal(treeCodes, string.Join(",", tree.Diagnostics.Select(d => d.Code)));
    }

    // Only an element access WITHOUT indexes reads as `T[]`, and only a comparison with an EMPTY
    // collection reads as `G<T>[]`; other non-constant operands stay constant patterns, which the
    // compiler rejects (CS0150) as Validate does (GEN077).
    [Theory]
    [InlineData("return o is (x[0]);")]
    [InlineData("return o is (x[0, 1]);")]
    [InlineData("return o is (x > [1]);")]
    public void Non_type_operands_stay_constant_patterns(string body)
    {
        var whole = Cs.Parse("class C { object M(object o, int[] x) { " + body + " } }");
        Assert.Equal("Constant", Shape(whole.Root.Descendants().Select(n => n.Node)));
        Assert.Contains(whole.Root.Validate(), d => d.Code == "GEN077");
    }

    // A trailing operator after a parenthesized pattern operand belongs to the enclosing
    // expression, as in the compiler: `o is (1) && b` is `(o is (1)) && b`.
    [Theory]
    [InlineData("return o is (1) && b;", ExpressionKind.Binary, CsOperator.AndAlso)]
    [InlineData("return o is (string) && b;", ExpressionKind.Binary, CsOperator.AndAlso)]
    [InlineData("return o is (string) == b;", ExpressionKind.Binary, CsOperator.Equal)]
    [InlineData("return o is (bool) || b;", ExpressionKind.Binary, CsOperator.OrElse)]
    [InlineData("return o is (int) ? 1 : 0;", ExpressionKind.Conditional, CsOperator.None)]
    [InlineData("return o is (1) is bool;", ExpressionKind.Is, CsOperator.None)]
    public void Operators_after_a_parenthesized_pattern_apply_to_the_is_expression(string body, ExpressionKind kind, CsOperator op)
    {
        var whole = Cs.Parse("class C { object M(" + Parameters + ") { " + body + " } }");
        var value = whole.Root.Descendants().Select(n => n.Node).OfType<CsStatement>()
            .Single(s => s.Kind == StatementKind.Return).Expression!;
        Assert.Equal(kind, value.Kind);
        Assert.Equal(op, value.Operator);
        var test = value.Arguments[0];
        Assert.Equal(ExpressionKind.Is, test.Kind);
        Assert.Equal("o", test.Arguments[0].Text);
        var pattern = test.Pattern!;
        Assert.True(pattern.Kind == PatternKind.Parenthesized ||
            pattern is { Kind: PatternKind.Constant, Value.Kind: ExpressionKind.Parenthesized }, pattern.Kind + " " + pattern.Value?.Kind);
    }

    static string Shape(IEnumerable<CsNode> nodes) => string.Join(",", nodes.SelectMany(node => node switch
    {
        CsPattern pattern => [pattern.Kind + (pattern.Type is { } type ? ":" + TypeText(type) : "")],
        CsExpression { Kind: ExpressionKind.Is, Type: { } type } => ["Is:" + TypeText(type)],
        _ => Array.Empty<string>()
    }));

    static string PatternShape(IEnumerable<CsNode> nodes) => Shape(nodes.OfType<CsPattern>());

    static string TypeText(CsType type) => type.Form switch
    {
        TypeForm.Named => type.Name,
        TypeForm.Array => TypeText(type.Element!) + "[" + new string(',', type.Rank - 1) + "]",
        TypeForm.Generic => TypeText(type.Element!) + "<" + string.Join(",", type.Arguments.Select(TypeText)) + ">",
        _ => type.Form.ToString()
    };

    static void AssertNoUnescapedKeywordIdentifier(IEnumerable<CsNode> nodes) =>
        Assert.DoesNotContain(nodes.OfType<CsExpression>(), e => e.Kind == ExpressionKind.Identifier && KeywordTypes.Contains(e.Text));
}
