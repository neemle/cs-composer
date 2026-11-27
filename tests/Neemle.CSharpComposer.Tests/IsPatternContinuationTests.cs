using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

using Row = (string Id, string Expression);

// GENE-16: an is-expression ends at its pattern. The compiler applies no operator that binds tighter
// than `is` to it (CS1073, CS1002), and after a bare type it reads a tighter binary operator as part of
// a constant (CS1525). Composer must leave such statements on its recovery path, not build typed nodes.
[Trait("Category", "Unit")]
public sealed class IsPatternContinuationTests
{
    const string Prefix = "public record R(int A){public int M()=>1;} public static class C{public static object F(object o,bool y){";

    // Rows A1..C6 are the refined-description matrix. B5, X01..X13 and Q01..Q38 are further
    // continuations the compiler rejects; X13, Q16, Q17 and Q38 hold the A1 shape inside parentheses.
    static readonly Row[] Rejected =
    [
        ("A1", "o is string s switch { _ => 1 }"),
        ("E1", "o is string s with { }"),
        ("A2", "o is R r with { }"),
        ("A5", "o is (string s) switch { _ => 1 }"),
        ("A6", "o is (R r) with { }"),
        ("A7", "o is { } switch { _ => 1 }"),
        ("A8", "o is R { A: 1 } r with { }"),
        ("E10", "o is [] switch { _ => 1 }"),
        ("E11", "o is string s with { } switch { _ => 1 }"),
        ("D15", "o is R r with { } is bool"),
        ("E7", "o switch { string s when o is string t switch { _ => true } => 1, _ => 2 }"),
        ("E8", "y ? o is string s switch { _ => 1 } : 0"),
        ("E9", "o is string s && o is string t switch { _ => true }"),
        ("B1", "o is string s.Length"),
        ("B2", "o is R r.M()"),
        ("B3", "o is string s[0]"),
        ("B6", "o is int i!"),
        ("B7", "o is R r?.M()"),
        ("B8", "o is int i..2"),
        ("C1", "o is int i + 1"),
        ("C2", "o is int i * 2"),
        ("C3", "o is int + 1"),
        ("C6", "o is int i << 1"),
        ("B5", "o is int i++"),
        ("X01", "o is int i--"),
        ("X02", "o is int i->x"),
        ("X03", "o is int i::x"),
        ("X04", "o is R r(1)"),
        ("X05", "o is string s?[0]"),
        ("X06", "o is string s with"),
        ("X07", "o is string s >> 1"),
        ("X08", "o is int i / 2"),
        ("X09", "o is int i % 2"),
        ("X10", "o is int i - 1"),
        ("X11", "o is int i >>> 1"),
        ("X12", "o is int[] + 1"),
        ("X13", "y is (o is string s switch { _ => 1 })"),
        ("Q01", "o is string s switch { _ => 1 } switch { _ => 2 }"),
        ("Q02", "o is string { Length: 0 } s with { }"),
        ("Q04", "o is string s ? o is int i switch { _ => 1 } : 0"),
        ("Q16", "(o is string s switch { _ => 1 })"),
        ("Q17", "string.Concat(o is string s switch { _ => 1 }, y)"),
        ("Q19", "o is string s with { } with { }"),
        ("Q38", "(object)(o is string s switch { _ => 1 })"),
    ];

    // AC-3 rows, plus X20/X21: after `is`, `?` followed by `[` opens a conditional whose branch is a
    // collection expression; it is not a conditional element access on the is-expression.
    static readonly Row[] Accepted =
    [
        ("D1", "(o is string s) switch { _ => 1 }"),
        ("D2", "(o is R r) with { }"),
        ("E12", "(o is string s) with { }"),
        ("D3", "(o is string s).ToString()"),
        ("D4", "o is string ? 1 : 2"),
        ("D5", "o is string s ? 1 : 2"),
        ("D6", "o is string && y"),
        ("D7", "o is string s && y"),
        ("D13", "o is string s || y"),
        ("C4", "o is string s as object"),
        ("C5", "o is string s is bool"),
        ("C7", "o is string s == y"),
        ("D8", "o switch { _ => 1 } is int"),
        ("D9", "new R(1) with { } is R"),
        ("D11", "o as string switch { _ => 1 }"),
        ("D12", "o as R with { }"),
        ("X20", "o is string s ? [1] : new int[0]"),
        ("X21", "o is string ? [1] : new int[0]"),
    ];

    // Syntactically accepted, rejected only by binding (CS0019, CS0120): the parse must stay typed and
    // its reformatted output must draw the same compiler diagnostics as the source.
    static readonly Row[] SemanticOnly =
    [
        ("D14", "o is string s ?? y"),
        ("X30", "o is string s < y"),
        ("X31", "o is string s <= y"),
        ("B4", "o is string.Length"),
    ];

    public static TheoryData<string> RejectedIds() => new(Rejected.Select(row => row.Id));
    public static TheoryData<string> AcceptedIds() => new(Accepted.Select(row => row.Id));
    public static TheoryData<string> SemanticOnlyIds() => new(SemanticOnly.Select(row => row.Id));

    static string Document(string expression) => Prefix + Statement(expression) + "}}";

    static string Statement(string expression) => "return " + expression + ";";

    static CsStatement[] Statements(CsDocument document) =>
        document.Descendants().Select(n => n.Node).OfType<CsStatement>().ToArray();

    static string Expression(Row[] rows, string id) => rows.Single(row => row.Id == id).Expression;

    [Theory]
    [MemberData(nameof(RejectedIds))]
    public void Rejected_continuations_end_on_the_statement_recovery_path(string id)
    {
        string expression = Expression(Rejected, id), statement = Statement(expression), document = Document(expression);
        var parsed = Cs.Parse(document);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        Assert.All(parsed.Diagnostics, d => Assert.Equal(("PARSE002", DiagnosticSeverity.Warning), (d.Code, d.Severity)));
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal((document.IndexOf("return ", StringComparison.Ordinal), statement.Length), (diagnostic.Offset, diagnostic.Length));
        var statements = Statements(parsed.Root);
        Assert.Equal(Statements(Cs.Parse(Document("0")).Root).Length, statements.Length);
        Assert.Equal(statement, Assert.Single(statements, s => s.Kind == StatementKind.Raw).Text);
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsExpression { Kind: ExpressionKind.Is });
        FormatCheck.FixedPoint(parsed.Root);

        var tree = Cs.ParseTree(statement);
        Assert.Equal(ParseCoverage.Opaque, tree.Coverage);
        Assert.Contains(tree.Diagnostics, d => d is { Code: "PARSE001", Severity: DiagnosticSeverity.Error });
        Assert.Equal(statement, Assert.IsType<CsRaw>(Assert.Single(tree.Root.Items)).Text);
    }

    // Statement recovery skips to the `;` outside all brackets; only a block statement ends at the
    // brace group that closes it, so the next statement still parses typed.
    [Theory]
    [InlineData("for (int i = 0; i < (o is string s switch { _ => 1 }); i++) { }")]
    [InlineData("if (o is string s switch { _ => true }) { y = !y; }")]
    [InlineData("var v = o is R { A: 1 } r with { };")]
    [InlineData("y |= o is string s with { } is bool;")]
    [InlineData("throw o is R { A: 1 } r with { };")]
    [InlineData("yield return o is R { A: 1 } r with { };")]
    public void Statement_recovery_keeps_a_rejected_statement_whole(string rejected)
    {
        string document = Prefix + rejected + " return 1;}}";
        var parsed = Cs.Parse(document);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal((document.IndexOf(rejected, StringComparison.Ordinal), rejected.Length), (diagnostic.Offset, diagnostic.Length));
        var statements = Statements(parsed.Root);
        Assert.Equal(rejected, Assert.Single(statements, s => s.Kind == StatementKind.Raw).Text);
        Assert.Equal(StatementKind.Return, statements[^1].Kind);
        Assert.Equal(Statements(Cs.Parse(Document("0")).Root).Length + 1, statements.Length);
        FormatCheck.FixedPoint(parsed.Root);
    }

    // A recovered statement also ends at a directive left in the token stream, before the `}` that
    // closes its body, or at the end of the source. At the end of the source the unclosed method is
    // itself recovered as a raw member, so only the statement's diagnostic remains.
    [Theory]
    [InlineData("y = o is string s switch { _ => true }\n#bogus\n; return 1;}}", "y = o is string s switch { _ => true }\n#bogus\n", true)]
    [InlineData("return o is string s switch { _ => 1 }}}", "return o is string s switch { _ => 1 }", true)]
    [InlineData("return o is string s switch { _ => 1 }", "return o is string s switch { _ => 1 }", false)]
    public void Statement_recovery_stops_at_a_directive_a_closing_brace_or_the_end(string body, string recovered, bool kept)
    {
        string document = Prefix + body;
        var parsed = Cs.Parse(document);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        Assert.Contains(parsed.Diagnostics, d => (d.Code, d.Offset, d.Length) == ("PARSE002", Prefix.Length, recovered.Length));
        Assert.Equal(kept, Statements(parsed.Root).Any(s => s.Kind == StatementKind.Raw && s.Text == recovered));
        FormatCheck.FixedPoint(parsed.Root);
    }

    [Theory]
    [MemberData(nameof(AcceptedIds))]
    public void Accepted_continuations_stay_typed(string id) => AssertTyped(Expression(Accepted, id));

    [Theory]
    [MemberData(nameof(SemanticOnlyIds))]
    public void Semantically_rejected_continuations_stay_typed(string id) => AssertTyped(Expression(SemanticOnly, id));

    static void AssertTyped(string expression)
    {
        string statement = Statement(expression), document = Document(expression);
        var parsed = Cs.Parse(document);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Diagnostics);
        FormatCheck.FixedPoint(parsed.Root);
        var tree = Cs.ParseTree(statement);
        Assert.Equal(ParseCoverage.Typed, tree.Coverage);
        Assert.Empty(tree.Diagnostics);
        FormatCheck.FixedPoint(tree.Root);
    }

    static string Reformat(string expression) => Cs.ParseTree(Statement(expression)).Root.Reformat().Render();

    static CsExpression Returned(string expression)
    {
        var parsed = Cs.Parse(Document(expression));
        return parsed.Root.Descendants().Select(n => n.Node).OfType<CsStatement>().Single(s => s.Kind == StatementKind.Return).Expression!;
    }

    [Theory]
    [InlineData("(o is string s) switch { _ => 1 }", ExpressionKind.Switch)]
    [InlineData("(o is R r) with { }", ExpressionKind.With)]
    [InlineData("(o is string s) with { }", ExpressionKind.With)]
    public void Parenthesized_is_expressions_take_switch_and_with(string expression, ExpressionKind kind)
    {
        var returned = Returned(expression);
        Assert.Equal(kind, returned.Kind);
        Assert.Equal(ExpressionKind.Parenthesized, returned.Arguments[0].Kind);
        Assert.Equal(ExpressionKind.Is, returned.Arguments[0].Arguments[0].Kind);
    }

    [Fact]
    public void Parenthesized_is_expression_takes_member_access()
    {
        var returned = Returned("(o is string s).ToString()");
        Assert.Equal(ExpressionKind.Invocation, returned.Kind);
        var member = returned.Arguments[0];
        Assert.Equal((ExpressionKind.Member, "ToString"), (member.Kind, member.Text));
        Assert.Equal(ExpressionKind.Parenthesized, member.Arguments[0].Kind);
        Assert.Equal(ExpressionKind.Is, member.Arguments[0].Arguments[0].Kind);
    }

    [Theory]
    [InlineData("o is string s ? 1 : 2", ExpressionKind.Conditional, CsOperator.None)]
    [InlineData("o is string s && y", ExpressionKind.Binary, CsOperator.AndAlso)]
    [InlineData("o is string s as object", ExpressionKind.As, CsOperator.None)]
    [InlineData("o is string s == y", ExpressionKind.Binary, CsOperator.Equal)]
    [InlineData("o is string s < y", ExpressionKind.Binary, CsOperator.LessThan)]
    [InlineData("o is string s ? [1] : new int[0]", ExpressionKind.Conditional, CsOperator.None)]
    [InlineData("o is string ? [1] : new int[0]", ExpressionKind.Conditional, CsOperator.None)]
    public void Operators_no_tighter_than_is_continue_after_an_is_pattern(string expression, ExpressionKind kind, CsOperator op)
    {
        var returned = Returned(expression);
        Assert.Equal((kind, op), (returned.Kind, returned.Operator));
        Assert.Equal(ExpressionKind.Is, returned.Arguments[0].Kind);
    }

    [Fact]
    public void Conditional_after_is_takes_a_collection_expression_branch()
    {
        var returned = Returned("o is string s ? [1] : new int[0]");
        Assert.Equal(ExpressionKind.Collection, returned.Arguments[1].Kind);
    }

    [Theory]
    [InlineData("o is 1 switch { _ => 1 }")]
    [InlineData("o is R with { }")]
    [InlineData("o is R switch { _ => 1 }")]
    [InlineData("o is not null switch { _ => 1 }")]
    [InlineData("o is string s or null switch { _ => 1 }")]
    public void Rows_both_sides_reject_stay_non_clean(string expression)
    {
        var parsed = Cs.Parse(Document(expression));
        Assert.True(parsed.Diagnostics.Count > 0 || parsed.Root.Validate().Count > 0, expression);
        Assert.NotEqual(ParseCoverage.Typed, Cs.ParseTree(Statement(expression)).Coverage);
    }

    [Theory]
    [InlineData("o is string switch { _ => 1 }")]
    [InlineData("o is int switch { _ => 1 }")]
    public void Type_tests_followed_by_switch_move_only_toward_the_compiler(string expression)
    {
        var tree = Cs.ParseTree(Statement(expression));
        if (tree.Coverage != ParseCoverage.Typed)
        {
            Assert.Contains(tree.Diagnostics, d => d.Code == "PARSE001");
            return;
        }
        var returned = Returned(expression);
        Assert.Equal(ExpressionKind.Switch, returned.Kind);
        Assert.Equal(ExpressionKind.Is, returned.Arguments[0].Kind);
        Assert.Null(returned.Arguments[0].Pattern);
    }

    static async Task<HashSet<string>[]> Compile(Row[] rows, Func<Row, string> body)
    {
        var source = new System.Text.StringBuilder("record R(int A){public int M()=>1;}\n")
            .Append("class Program { static void Main() { } }\nstatic class Rows\n{\n");
        for (int i = 0; i < rows.Length; i++)
            source.Append("#line ").Append((i + 1) * 1000).Append("\nstatic object Row").Append(rows[i].Id)
                .Append("(object o, bool y)\n{\n").Append(body(rows[i])).Append("\n}\n");
        source.Append("#line default\n}\n");
        var diagnostics = await CompilerFixture.Build(source.ToString(), "14.0");
        Assert.DoesNotContain(diagnostics, d => d.Line < 1000 || d.Line >= (rows.Length + 1) * 1000);
        return rows.Select((_, i) => diagnostics.Where(d => d.Line / 1000 == i + 1).Select(d => d.Code).ToHashSet()).ToArray();
    }

    // Integration: these tests build or run composed source with the real compiler host.
    [Trait("Category", "Integration")]
    public sealed class Compiler
    {
        [Fact]
        public async Task Compiler_rejects_every_row_Composer_recovers()
        {
            var codes = await Compile(Rejected, row => Statement(row.Expression));
            Assert.All(Rejected.Select((row, i) => (row.Id, Codes: codes[i])), x => Assert.True(x.Codes.Count > 0, x.Id + " compiled"));
            Assert.All(Rejected, row =>
            {
                Assert.Equal(ParseCoverage.Partial, Cs.Parse(Document(row.Expression)).Coverage);
                Assert.Contains(Cs.Parse(Document(row.Expression)).Diagnostics, d => d.Code == "PARSE002");
                var tree = Cs.ParseTree(Statement(row.Expression));
                Assert.Equal(ParseCoverage.Opaque, tree.Coverage);
                Assert.Contains(tree.Diagnostics, d => d.Code == "PARSE001");
            });
        }

        [Fact]
        public async Task Compiler_accepts_the_accepted_rows_and_their_reformatted_output()
        {
            var source = await Compile(Accepted, row => Statement(row.Expression));
            var reformatted = await Compile(Accepted, row => Reformat(row.Expression));
            Assert.All(Accepted.Select((row, i) => (row.Id, Codes: source[i].Concat(reformatted[i]))), x => Assert.True(!x.Codes.Any(), x.Id + ": " + string.Join(",", x.Codes)));
        }

        [Fact]
        public async Task Reformatted_semantic_rows_draw_the_compiler_diagnostics_of_their_source()
        {
            var source = await Compile(SemanticOnly, row => Statement(row.Expression));
            var reformatted = await Compile(SemanticOnly, row => Reformat(row.Expression));
            Assert.All(SemanticOnly.Select((row, i) => (row.Id, Source: source[i], Reformatted: reformatted[i])), x =>
            {
                Assert.True(x.Source.Count > 0, x.Id + " compiled");
                Assert.True(x.Source.SetEquals(x.Reformatted), x.Id + ": [" + string.Join(",", x.Source) + "] vs [" + string.Join(",", x.Reformatted) + "]");
            });
        }
    }
}
