using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SwitchTests
{
    internal static CsDocument Document(CsStatement statement, CsLanguageVersion version = CsLanguageVersion.CSharp15) =>
        new(version)
        {
            Types = new[] { new CsTypeDeclaration(TypeKind.Class, "Logic") {
            Members = new CsMember[] { new CsMethod(Cs.Int, "Compute") {
                Parameters = new[] { new CsParameter(Cs.Int, "input") }, Body = Cs.Body(statement) } } } }
        };

    [Fact]
    public void Sections_have_multiple_labels_and_explicit_control_flow_without_added_scopes()
    {
        var statement = Cs.Switch(Cs.Id("input"),
            Cs.Section(Cs.Body(Cs.GotoCase(Cs.Literal(2))), Cs.Case(Cs.Literal(0)), Cs.Case(Cs.Literal(1))),
            Cs.Section(Cs.Body(Cs.Return(Cs.Literal(20))), Cs.Case(Cs.Literal(2))),
            Cs.Section(Cs.Body(Cs.GotoDefault()), Cs.Case(Cs.Literal(3))),
            Cs.Section(Cs.Body(Cs.Return(Cs.Literal(-1))), Cs.Default()));
        var document = Document(statement);
        string source = document.Render();
        Assert.Empty(document.Validate());
        Assert.Contains("case 0:\n            case 1:\n                goto case 2;", source);
        Assert.Contains("goto default;", source);
        Assert.DoesNotContain("case 2:\n            {", source);
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        var result = Assert.IsType<CsMethod>(parsed.Root.Types[0].Members[0]).Body!.Statements[0];
        Assert.Equal(StatementKind.Switch, result.Kind);
        Assert.Equal(2, result.Sections[0].Labels.Count);
        Assert.Equal(StatementKind.GotoCase, result.Sections[0].Body.Statements[0].Kind);
        Assert.Equal(source, parsed.Root.Render());
        Assert.Equal(source, parsed.Root.Reformat().Render());
    }

    [Theory]
    [InlineData("class C { int M(object x) { switch(x) { default: return -1; case int n when n > 0: return n; case null: return 0; } } }")]
    [InlineData("class C { void M(int x) { switch(x) { case 1: case 2: if(x == 1) { break; } else { return; } default: break; } } }")]
    [InlineData("class C { int M(int x) => x switch { < 0 => -1, >= 0 and < 10 => 1, _ => 2, }; }")]
    [InlineData("class C { int M(object x) => x switch { int n when n > 0 => n, string => 1, null => 0, var other => 2 }; }")]
    [InlineData("class C { int M(int x) => x switch { not (1 or 2) => 0, 1 or 2 => 1 }; }")]
    [InlineData("class C { int M(int x) => (x + 1) switch { (1 + 2) => 3, _ => x switch { 0 => 0, _ => 1 } }; }")]
    [InlineData("class C { int M(int x, Exception error) => x switch { 1 => 1, _ => throw error }; }")]
    [InlineData("class C { void M(int x) { switch(x) {} } }")]
    public void Structured_switch_grammar_is_typed_lossless_and_reformattable(string source)
    {
        var result = Cs.Parse(source);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, result.Coverage);
        Assert.DoesNotContain(result.Root.Descendants(), n => n.Node is CsRaw or CsRawMember ||
            n.Node is CsStatement { Kind: StatementKind.Raw } || n.Node is CsExpression { Kind: ExpressionKind.Raw });
        Assert.Empty(result.Root.Validate());
        Assert.Equal(source, result.Root.Render());
        var reformatted = Cs.Parse(result.Root.Reformat().Render());
        Assert.Empty(reformatted.Diagnostics);
        Assert.Empty(reformatted.Root.Validate());
    }

    [Fact]
    public void Switch_expression_precedence_and_pattern_grouping_preserve_meaning()
    {
        var expression = Cs.SwitchExpression(Cs.Binary(CsOperator.Add, Cs.Id("input"), Cs.Literal(1)),
            Cs.Arm(Cs.NotPattern(Cs.OrPattern(Cs.ConstantPattern(Cs.Literal(1)), Cs.ConstantPattern(Cs.Literal(2)))), Cs.Literal(3)),
            Cs.Arm(Cs.DiscardPattern(), Cs.Literal(4)));
        string source = Document(Cs.Return(expression.Member("CompareTo").Call(Cs.Literal(4)))).Render();
        Assert.Contains("((input + 1) switch { not (1 or 2) => 3, _ => 4 }).CompareTo(4)", source);
        var unary = Cs.Unary(CsOperator.Negate, expression);
        Assert.Contains("-((input + 1) switch", Document(Cs.Return(unary)).Render());
        Assert.Empty(Cs.Parse(source).Diagnostics);
        var parsed = Cs.RawExpression("a + b switch { _ => 1 } * c").Parse(new(ParseKind.Expression));
        var sum = Assert.IsType<CsExpression>(parsed.Root);
        Assert.Equal(CsOperator.Add, sum.Operator);
        Assert.Equal(CsOperator.Multiply, sum.Arguments[1].Operator);
        Assert.Equal(ExpressionKind.Switch, sum.Arguments[1].Arguments[0].Kind);
    }

    [Fact]
    public void New_collections_are_snapshots_and_children_include_every_editable_part()
    {
        var labels = new[] { Cs.Case(Cs.Literal(1)) };
        var section = Cs.Section(Cs.Body(Cs.Break()), labels);
        var sections = new[] { section };
        var statement = Cs.Switch(Cs.Id("input"), sections);
        labels[0] = Cs.Default(); sections[0] = Cs.Section(Cs.Body(Cs.Return()), Cs.Default());
        Assert.Equal(PatternKind.Constant, statement.Sections[0].Labels[0].Pattern!.Kind);
        Assert.Contains(section, statement.Children);
        var arms = new[] { Cs.Arm(Cs.DiscardPattern(), Cs.Literal(1)) };
        var expression = Cs.SwitchExpression(Cs.Id("input"), arms);
        arms[0] = Cs.Arm(Cs.DiscardPattern(), Cs.Literal(2));
        Assert.Equal(Cs.Literal(1), expression.Arms[0].Value);
        Assert.Contains(expression.Arms[0], expression.Children);
        Assert.NotEqual(expression, expression with { Arms = arms });
        Assert.Equal(expression, expression.Detach());
    }
}
