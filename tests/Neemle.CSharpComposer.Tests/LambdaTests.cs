using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LambdaTests
{
    [Theory]
    [InlineData("class C { object M() => x => x + 1; }")]
    [InlineData("class C { object M() => (x, y) => x + y; }")]
    [InlineData("class C { object M() => (int x, int y) => x + y; }")]
    [InlineData("class C { object M() => () => 42; }")]
    [InlineData("class C { object M() => x => y => x + y; }")]
    [InlineData("class C { object M() => (ref int x) => { x++; return x; }; }")]
    [InlineData("class C { object M() => async x => await Fetch(x); }")]
    [InlineData("class C { object M() => static async (int x) => { return await Fetch(x); }; }")]
    [InlineData("class C { object M() => (_, _) => 1; }")]
    [InlineData("class C { object M() => async => async + 1; }")]
    [InlineData("class C { void M() { handler = (sender, args) => { Run(sender); }; Use(x => x > 0, x => { return x * 2; }); } }")]
    [InlineData("class C { object M() => (int x) => { for (var i = 0; i < x; i++) { if (i == 2) { continue; } } return x; }; }")]
    [InlineData("class C { object M(int x) => x switch { One when ready => y => y + 1, _ => () => 0 }; }")]
    public void Lambdas_are_typed_lossless_and_reformattable_in_general_expression_positions(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Root.Validate());
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsExpression { Kind: ExpressionKind.Lambda });
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsStatement { Kind: StatementKind.Raw } ||
            n.Node is CsExpression { Kind: ExpressionKind.Raw });
        Assert.Equal(source, parsed.Root.Render());
        var reformatted = Cs.Parse(parsed.Root.Reformat().Render());
        Assert.Empty(reformatted.Diagnostics);
        Assert.Empty(reformatted.Root.Validate());
    }

    [Fact]
    public void Lambda_parameters_are_immutable_children_and_edits_preserve_surrounding_source()
    {
        var parameters = new[] { Cs.LambdaParameter("x", Cs.Int) };
        var lambda = Cs.Lambda(Cs.Body(Cs.Return(Cs.Id("x"))), parameters);
        parameters[0] = Cs.LambdaParameter("changed");
        Assert.Equal("x", lambda.LambdaParameters[0].Name);
        Assert.Equal(new CsNode[] { lambda.LambdaParameters[0], lambda.LambdaBody! }, lambda.Children);
        Assert.Equal(lambda, lambda.Detach());
        Assert.NotEqual(lambda, lambda with { IsAsync = true });

        const string source = "// file\r\nclass C { object M() => ( int name ) => { return 7; }; }\r\n";
        var parsed = Cs.Parse(source).Root;
        var selected = parsed.Descendants().Single(n => n.Node is CsLambdaParameter);
        var renamed = parsed.Replace(selected.Path, (CsLambdaParameter)selected.Node with { Name = "other" });
        Assert.Equal(source.Replace("int name", "int other"), renamed.Render());
        var value = renamed.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "7" });
        var edited = renamed.Replace(value.Path, (CsExpression)value.Node with { Text = "8" });
        Assert.Equal(source.Replace("int name", "int other").Replace("return 7", "return 8"), edited.Render());
        var owner = edited.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Lambda });
        var inserted = edited.Insert(owner.Path, 1, Cs.LambdaParameter("second", Cs.Int));
        Assert.Contains("int second", inserted.Render());
        var second = inserted.Descendants().Single(n => n.Node is CsLambdaParameter { Name: "second" });
        Assert.DoesNotContain("second", inserted.Remove(second.Path).Render());
    }

    [Fact]
    public void Lambda_bodies_reset_loop_switch_and_catch_contexts()
    {
        foreach (var jump in new[] { Cs.Break(), Cs.Continue(), Cs.GotoDefault(), Cs.Rethrow() })
        {
            var lambda = Cs.Lambda(Cs.Body(jump));
            var outer = Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body(Cs.While(Cs.Literal(true), Cs.Body(
                Cs.Switch(Cs.Id("kind"), Cs.Section(Cs.Body(Cs.Var("callback", lambda), Cs.Break()), Cs.Default())))))) });
            Assert.Contains(LoopTests.Document(outer).Validate(), d => d.Code is "GEN082" or "GEN085" or "GEN070");
        }
        var valid = Cs.Lambda(Cs.Body(Cs.While(Cs.Literal(true), Cs.Body(Cs.If(Cs.Id("stop"), Cs.Body(Cs.Break())), Cs.Continue()))));
        Assert.Empty(LoopTests.Document(Cs.Var("callback", valid)).Validate());
    }

    public static IEnumerable<object[]> InvalidLambdas()
    {
        var valid = Cs.Lambda(Cs.Id("x"), Cs.LambdaParameter("x"));
        yield return new object[] { valid with { LambdaBody = null }, "GEN002" };
        yield return new object[] { valid with { LambdaParameters = null! }, "GEN002" };
        yield return new object[] { valid with { LambdaParameters = new CsLambdaParameter[] { null! } }, "GEN002" };
        yield return new object[] { valid with { Arguments = new[] { Cs.Literal(1) } }, "GEN013" };
        yield return new object[] { valid with { Text = "Injected" }, "GEN065" };
        yield return new object[] { Cs.Id("x") with { LambdaBody = Cs.Body() }, "GEN065" };
        yield return new object[] { Cs.Id("x") with { IsAsync = true }, "GEN065" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("x"), Cs.LambdaParameter("@x")), "GEN089" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("@_"), Cs.LambdaParameter("_")), "GEN089" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("x", Cs.Int), Cs.LambdaParameter("y")), "GEN089" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("x", Cs.Void)), "GEN089" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("x", modifier: "this")), "GEN089" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("x", Cs.Int, "ref")) with { IsAsync = true }, "GEN089" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("x", Cs.Int, "injected")), "GEN089" };
        yield return new object[] { Cs.Lambda(Cs.Literal(0), Cs.LambdaParameter("x) { Inject(); }")), "GEN003" };
    }

    [Theory]
    [MemberData(nameof(InvalidLambdas))]
    public void Invalid_lambda_shapes_fail_before_rendering(CsExpression lambda, string code)
    {
        var document = LoopTests.Document(Cs.Var("callback", lambda));
        Assert.Contains(document.Validate(), d => d.Code == code);
        Assert.Throws<GenValidationException>(() => document.Render());
    }

    [Fact]
    public void Static_and_discard_lambdas_enforce_versions_and_failed_raw_conversion_preserves_input()
    {
        foreach (var lambda in new[] { Cs.Lambda(Cs.Literal(1)) with { IsStatic = true },
            Cs.Lambda(Cs.Literal(1), Cs.LambdaParameter("_"), Cs.LambdaParameter("_")) })
        {
            var old = LoopTests.Document(Cs.Var("callback", lambda)) with { LanguageVersion = CsLanguageVersion.CSharp8 };
            Assert.Contains(old.Validate(), d => d.Code == "GEN073");
            Assert.Throws<GenValidationException>(() => old.Render(new RenderOptions { Validate = false }));
        }
        var raw = Cs.RawExpression("static x => x + 1");
        Assert.Same(raw, raw.Parse(new(ParseKind.Expression, CsLanguageVersion.CSharp8)).Root);
        Assert.IsType<CsExpression>(raw.Parse(new(ParseKind.Expression, CsLanguageVersion.CSharp9)).Root);
        var unsupported = Cs.RawExpression("(int x = 1) => x");
        Assert.Same(unsupported, unsupported.Parse(new(ParseKind.Expression, CsLanguageVersion.CSharp11)).Root);
        CsExpression nested = Cs.Literal(1);
        for (int i = 0; i < 150; i++) nested = Cs.Lambda(nested);
        Assert.Contains(LoopTests.Document(Cs.Var("callback", nested)).Validate(), d => d.Code == "GEN001");
    }

    [Fact]
    public void Lambda_header_and_block_comments_survive_formatting_and_natural_type_inference_is_versioned()
    {
        const string source = "class C { object M() => (/* parameter */ int x) => /* arrow */ {\n// body\nreturn x;\n}; }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        string formatted = parsed.Root.Reformat().Render();
        foreach (string comment in new[] { "/* parameter */", "/* arrow */", "// body" })
            Assert.Equal(1, formatted.Split(comment, StringSplitOptions.None).Length - 1);
        Assert.Empty(Cs.Parse(formatted).Diagnostics);
        var lambda = Cs.Lambda(Cs.Literal(1));
        var old = LoopTests.Document(Cs.Var("callback", lambda)) with { LanguageVersion = CsLanguageVersion.CSharp9 };
        Assert.Contains(old.Validate(), d => d.Code == "GEN073");
        var modern = old with { LanguageVersion = CsLanguageVersion.CSharp10 };
        Assert.Empty(modern.Validate());
    }
}
