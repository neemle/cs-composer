using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

using Row = (string Id, string Expression);

// GENE-16: an is-expression ends at its pattern. The compiler applies no operator that binds tighter
// than `is` to it (CS1073, CS1002), and after a bare type it reads a tighter binary operator as part of
// a constant (CS1525). Composer must leave such statements on its recovery path, not build typed nodes.
public sealed class IsPatternContinuationTests
{
    const string Prefix = "public record R(int A){public int M()=>1;} public static class C{public static object F(object o,bool y){";

    // Rows A1..C6 are the refined-description matrix. B5 and X01..X13 are further continuations the
    // compiler rejects; X13 nests the A1 shape inside a parenthesized pattern.
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
    ];

    // Statement recovery closes a statement at the first brace group that balances (RecoverNode), so
    // these rows recover as a raw prefix ending at that group, followed by the remaining tokens.
    static readonly HashSet<string> RecoverySplits = ["A7", "A8", "E8", "E11", "D15", "X13"];

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

    static string Expression(Row[] rows, string id) => rows.Single(row => row.Id == id).Expression;

    [Theory]
    [MemberData(nameof(RejectedIds))]
    public void Rejected_continuations_end_on_the_statement_recovery_path(string id)
    {
        string expression = Expression(Rejected, id), statement = Statement(expression), document = Document(expression);
        var parsed = Cs.Parse(document);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        Assert.All(parsed.Diagnostics, d => Assert.Equal(("PARSE002", DiagnosticSeverity.Warning), (d.Code, d.Severity)));
        var raw = parsed.Root.Descendants().Select(n => n.Node).OfType<CsStatement>().Where(s => s.Kind == StatementKind.Raw).ToArray();
        if (RecoverySplits.Contains(id))
        {
            Assert.Equal(document.IndexOf("return ", StringComparison.Ordinal), parsed.Diagnostics[0].Offset);
            Assert.True(raw.Length > 0 && statement.StartsWith(raw[0].Text, StringComparison.Ordinal) && raw[0].Text.EndsWith('}'), raw[0].Text);
        }
        else
        {
            var diagnostic = Assert.Single(parsed.Diagnostics);
            Assert.Equal((document.IndexOf("return ", StringComparison.Ordinal), statement.Length), (diagnostic.Offset, diagnostic.Length));
            Assert.Equal(statement, Assert.Single(raw).Text);
        }
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsExpression { Kind: ExpressionKind.Is });
        Assert.Equal(document, parsed.Root.Render());

        var tree = Cs.ParseTree(statement);
        Assert.Equal(ParseCoverage.Opaque, tree.Coverage);
        Assert.Contains(tree.Diagnostics, d => d is { Code: "PARSE001", Severity: DiagnosticSeverity.Error });
        Assert.Equal(statement, Assert.IsType<CsRaw>(Assert.Single(tree.Root.Items)).Text);
    }

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
        Assert.Equal(document, parsed.Root.Render());
        var tree = Cs.ParseTree(statement);
        Assert.Equal(ParseCoverage.Typed, tree.Coverage);
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(statement, tree.Root.Render());
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
}
