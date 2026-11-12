using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SwitchBoundaryTests
{
    static CsStatement Wrap(CsPattern pattern) => Cs.Return(Cs.SwitchExpression(Cs.Id("value"), Cs.Arm(pattern, Cs.Literal(1))));

    public static IEnumerable<object[]> InvalidNodes()
    {
        yield return new object[] { Cs.Break(), "GEN082" };
        yield return new object[] { Cs.GotoDefault(), "GEN082" };
        yield return new object[] { Cs.GotoCase(Cs.Literal(1)), "GEN082" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()))), "GEN080" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()), Cs.Default(), Cs.Default())), "GEN080" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(), Cs.Default())), "GEN081" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Statement(Cs.Id("Run").Call())), Cs.Default())), "GEN081" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.GotoDefault()), Cs.Case(Cs.Literal(1)))), "GEN082" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()), new CsSwitchLabel(null, Cs.Literal(true)))), "GEN080" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()), Cs.Case(Cs.DiscardPattern()))), "GEN080" };
        yield return new object[] { Cs.Return(Cs.SwitchExpression(Cs.Id("value"))), "GEN078" };
        yield return new object[] { Wrap(new CsPattern((PatternKind)999)), "GEN076" };
        yield return new object[] { Wrap(Cs.NotPattern(Cs.DiscardPattern()) with { Patterns = Values<CsPattern>.Empty }), "GEN076" };
        yield return new object[] { Wrap(Cs.RelationalPattern(CsOperator.Add, Cs.Literal(1))), "GEN076" };
        yield return new object[] { Wrap(Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal(null))), "GEN076" };
        yield return new object[] { Wrap(Cs.RelationalPattern(CsOperator.GreaterThan, Cs.Literal("text"))), "GEN076" };
        yield return new object[] { Wrap(Cs.ConstantPattern(Cs.Id("Get").Call())), "GEN077" };
        yield return new object[] { Wrap(Cs.DeclarationPattern(Cs.Int, "n; Inject()")), "GEN003" };
        yield return new object[] { Wrap(Cs.TypePattern(Cs.Int.Nullable())), "GEN076" };
        yield return new object[] { Wrap(Cs.TypePattern(Cs.Type("dynamic"))), "GEN076" };
        yield return new object[] { Wrap(Cs.OrPattern(Cs.VarPattern("x"), Cs.ConstantPattern(Cs.Literal(1)))), "GEN076" };
        yield return new object[] { Wrap(Cs.DiscardPattern() with { Value = Cs.Literal(1) }), "GEN065" };
        yield return new object[] { Cs.Return(Cs.Id("value") with { Arms = new[] { Cs.Arm(Cs.DiscardPattern(), Cs.Literal(1)) } }), "GEN065" };
        yield return new object[] { Cs.Break() with { Expression = Cs.Literal(1) }, "GEN065" };
        yield return new object[] { Cs.Return(Cs.ThrowExpression(Cs.Id("error"))), "GEN083" };
        yield return new object[] { Cs.Return(Cs.SwitchExpression(Cs.Id("value"), Cs.Arm(Cs.DiscardPattern(), Cs.Literal(1), Cs.Literal(2)))), "GEN077" };
        yield return new object[] { Wrap(Cs.ConstantPattern(null!)), "GEN002" };
        yield return new object[] { Wrap(Cs.NotPattern(null!)), "GEN002" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), new CsSwitchSection(null!, null!)), "GEN002" };
        yield return new object[] { Cs.Switch(Cs.Id("value"), new CsSwitchSection(new CsSwitchLabel[] { null! }, Cs.Body(Cs.Break()))), "GEN002" };
        yield return new object[] { Cs.Switch(Cs.Id("value")) with { Sections = null! }, "GEN002" };
        yield return new object[] { Cs.Return(Cs.SwitchExpression(Cs.Id("value")) with { Arms = null! }), "GEN002" };
    }

    [Theory]
    [MemberData(nameof(InvalidNodes))]
    public void Malformed_nodes_report_diagnostics_before_rendering(CsStatement statement, string code)
    {
        var document = SwitchTests.Document(statement);
        Assert.Contains(document.Validate(), d => d.Code == code && d.Path.Length != 0);
        Assert.Throws<GenValidationException>(() => document.Render());
    }

    [Fact]
    public void Unguarded_patterns_subsume_following_identical_patterns_but_guards_and_default_preserve_order()
    {
        var invalid = Cs.SwitchExpression(Cs.Id("value"),
            Cs.Arm(Cs.DiscardPattern(), Cs.Literal(0)), Cs.Arm(Cs.ConstantPattern(Cs.Literal(1)), Cs.Literal(1)));
        Assert.Contains(SwitchTests.Document(Cs.Return(invalid)).Validate(), d => d.Code == "GEN079");
        var duplicate = Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()),
            Cs.Case(Cs.Literal(1)), Cs.Case(Cs.Literal(1), Cs.Id("condition"))));
        Assert.Contains(SwitchTests.Document(duplicate).Validate(), d => d.Code == "GEN079");
        var valid = Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()), Cs.Default()),
            Cs.Section(Cs.Body(Cs.Break()), Cs.Case(Cs.Literal(1), Cs.Id("condition")), Cs.Case(Cs.Literal(1))));
        Assert.Empty(SwitchTests.Document(valid).Validate());
    }

    [Fact]
    public void Break_and_rethrow_context_propagates_and_finally_is_a_jump_boundary()
    {
        var invalid = Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Try(Cs.Body(), finallyBody: Cs.Body(Cs.Break()))), Cs.Default()));
        Assert.Contains(SwitchTests.Document(invalid).Validate(), d => d.Code == "GEN082");
        var nested = Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()), Cs.Default()));
        var valid = Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Try(Cs.Body(Cs.Break()),
            new[] { Cs.CatchAll(Cs.Body(Cs.Rethrow())) }, Cs.Body(nested))), Cs.Default()));
        Assert.Empty(SwitchTests.Document(valid).Validate());
        var catchSwitch = Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body(Cs.Switch(Cs.Id("value"),
            Cs.Section(Cs.Body(Cs.Rethrow()), Cs.Default())))) });
        Assert.Empty(SwitchTests.Document(catchSwitch).Validate());
    }

    [Fact]
    public void Version_gates_apply_to_construction_validation_rendering_and_raw_conversion()
    {
        var expression = Cs.SwitchExpression(Cs.Id("value"), Cs.Arm(Cs.DiscardPattern(), Cs.Literal(1)));
        var old = SwitchTests.Document(Cs.Return(expression), CsLanguageVersion.CSharp7_3);
        Assert.Contains(old.Validate(), d => d.Code == "GEN073");
        Assert.Throws<GenValidationException>(() => old.Render(new RenderOptions { Validate = false }));
        Assert.Empty(SwitchTests.Document(Cs.Return(expression), CsLanguageVersion.CSharp8).Validate());
        var modern = Cs.SwitchExpression(Cs.Id("value"), Cs.Arm(Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal(1)), Cs.Literal(0)));
        Assert.Contains(SwitchTests.Document(Cs.Return(modern), CsLanguageVersion.CSharp8).Validate(), d => d.Code == "GEN073");
        var raw = Cs.RawExpression("value switch { _ => 1 }");
        Assert.Same(raw, raw.Parse(new(ParseKind.Expression, CsLanguageVersion.CSharp7_3)).Root);
        Assert.IsType<CsExpression>(raw.Parse(new(ParseKind.Expression, CsLanguageVersion.CSharp8)).Root);
        Assert.Empty(SwitchTests.Document(Cs.Switch(Cs.Id("value"), Cs.Section(Cs.Body(Cs.Break()), Cs.Default())), CsLanguageVersion.CSharp7_3).Validate());
    }

    [Fact]
    public void Pattern_recursion_is_bounded_and_recursive_property_patterns_are_typed()
    {
        CsPattern pattern = Cs.DiscardPattern();
        for (int i = 0; i < 150; i++) pattern = Cs.NotPattern(pattern);
        Assert.Contains(SwitchTests.Document(Wrap(pattern)).Validate(), d => d.Code == "GEN001");
        var raw = Cs.RawExpression("value switch { { Length: > 0 } => 1, _ => 0 }");
        var result = raw.Parse(new(ParseKind.Expression));
        Assert.Equal(ExpressionKind.Switch, Assert.IsType<CsExpression>(result.Root).Kind);
        Assert.Empty(result.Diagnostics);
    }
}
