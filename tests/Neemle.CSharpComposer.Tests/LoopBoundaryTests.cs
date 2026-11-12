using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LoopBoundaryTests
{
    static CsStatement For(CsForInitializer? initializer = null, CsExpression? condition = null, params CsExpression[] iterators) =>
        Cs.For(initializer, condition, iterators, Cs.Body(Cs.Break()));

    public static IEnumerable<object[]> InvalidNodes()
    {
        yield return new object[] { Cs.Continue(), "GEN085" };
        yield return new object[] { Cs.Break(), "GEN082" };
        yield return new object[] { Cs.While(null!, Cs.Body()), "GEN002" };
        yield return new object[] { Cs.DoWhile(Cs.Body(), null!), "GEN002" };
        yield return new object[] { Cs.While(Cs.Literal(1), Cs.Body()), "GEN086" };
        yield return new object[] { Cs.DoWhile(Cs.Body(), Cs.Literal("yes")), "GEN086" };
        yield return new object[] { For(condition: Cs.Literal(null)), "GEN086" };
        yield return new object[] { Cs.ForEach(Cs.Void, "x", Cs.Id("source"), Cs.Body()), "GEN084" };
        yield return new object[] { Cs.ForEach((CsType)null!, "x", Cs.Id("source"), Cs.Body()), "GEN002" };
        yield return new object[] { Cs.ForEach("x", null!, Cs.Body()), "GEN002" };
        yield return new object[] { Cs.ForEach("x; Inject()", Cs.Id("source"), Cs.Body()), "GEN003" };
        yield return new object[] { Cs.While(Cs.Literal(true), null!), "GEN002" };
        yield return new object[] { Cs.While(Cs.Literal(true), CsBody.FromExpression(Cs.Literal(1))), "GEN066" };
        yield return new object[] { For(Cs.ForDeclaration(Cs.Int)), "GEN084" };
        yield return new object[] { For(Cs.ForDeclaration(Cs.Void, Cs.Variable("i"))), "GEN084" };
        yield return new object[] { For(Cs.ForVar("i", null!)), "GEN067" };
        yield return new object[] { For(new CsForInitializer { IsVar = true, Variables = new[] { Cs.Variable("i", Cs.Literal(0)), Cs.Variable("j", Cs.Literal(1)) } }), "GEN084" };
        yield return new object[] { For(new CsForInitializer { IsVar = true, Type = Cs.Int, Variables = new[] { Cs.Variable("i", Cs.Literal(0)) } }), "GEN065" };
        yield return new object[] { For(new CsForInitializer { Variables = new[] { Cs.Variable("i") } }), "GEN002" };
        yield return new object[] { For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i"), Cs.Variable("@i"))), "GEN084" };
        yield return new object[] { For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i; Inject()"))), "GEN003" };
        yield return new object[] { For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i")) with { Expressions = new[] { Cs.Id("Run").Call() } }), "GEN084" };
        yield return new object[] { For(Cs.ForExpressions(Cs.Literal(1))), "GEN087" };
        yield return new object[] { For(null, null, Cs.Binary(CsOperator.Add, Cs.Id("i"), Cs.Literal(1))), "GEN087" };
        yield return new object[] { For(null, null, Cs.Assign(Cs.Literal(1), Cs.Literal(2))), "GEN088" };
        yield return new object[] { For(null, null, Cs.PostIncrement(Cs.Id("Get").Call())), "GEN088" };
        yield return new object[] { For(null, null, Cs.PreDecrement(Cs.Literal(1))), "GEN088" };
        yield return new object[] { For() with { Iterators = null! }, "GEN002" };
        yield return new object[] { For(Cs.ForExpressions(new CsExpression[] { null! })), "GEN002" };
        yield return new object[] { For(new CsForInitializer { Variables = null! }), "GEN002" };
        yield return new object[] { For(new CsForInitializer { Expressions = null! }), "GEN002" };
        yield return new object[] { For(Cs.ForDeclaration(Cs.Int, new CsVariableDeclarator[] { null! })), "GEN002" };
        yield return new object[] { Cs.Continue() with { Body = Cs.Body() }, "GEN065" };
        yield return new object[] { Cs.Continue() with { Expression = Cs.Literal(1) }, "GEN065" };
        yield return new object[] { Cs.Empty() with { Expression = Cs.Literal(1) }, "GEN065" };
        yield return new object[] { Cs.Return() with { ForInitializer = Cs.ForVar("i", Cs.Literal(0)) }, "GEN065" };
        yield return new object[] { Cs.While(Cs.Literal(true), Cs.Body()) with { Iterators = new[] { Cs.PostIncrement(Cs.Id("i")) } }, "GEN065" };
        yield return new object[] { Cs.While(Cs.Literal(true), Cs.Body()) with { IsVar = true }, "GEN065" };
        yield return new object[] { Cs.ForEach("x", Cs.Id("source"), Cs.Body()) with { Type = Cs.Int }, "GEN065" };
        yield return new object[] { For(null, null, Cs.PostIncrement(Cs.Id("i")) with { Arguments = Values<CsExpression>.Empty }), "GEN013" };
        yield return new object[] { For(null, null, Cs.PostIncrement(Cs.Id("i")) with { Text = "++ Inject()" }), "GEN065" };
    }

    [Theory]
    [MemberData(nameof(InvalidNodes))]
    public void Invalid_payloads_fail_with_diagnostics_not_renderer_exceptions(CsStatement statement, string code)
    {
        var document = LoopTests.Document(statement);
        Assert.Contains(document.Validate(), d => d.Code == code && d.Path.Length != 0);
        Assert.Throws<GenValidationException>(() => document.Render());
    }

    [Fact]
    public void Continue_needs_a_loop_and_finally_resets_outer_jump_targets()
    {
        var onlySwitch = Cs.Switch(Cs.Id("kind"), Cs.Section(Cs.Body(Cs.Continue()), Cs.Default()));
        Assert.Contains(LoopTests.Document(onlySwitch).Validate(), d => d.Code == "GEN085");
        Assert.Empty(LoopTests.Document(Cs.While(Cs.Id("ready"), Cs.Body(onlySwitch, Cs.Break()))).Validate());
        foreach (var jump in new[] { Cs.Break(), Cs.Continue(), Cs.GotoDefault() })
        {
            var @try = Cs.Try(Cs.Body(), finallyBody: Cs.Body(jump));
            var invalid = Cs.While(Cs.Literal(true), Cs.Body(Cs.Switch(Cs.Id("kind"), Cs.Section(Cs.Body(@try, Cs.Break()), Cs.Default()))));
            Assert.Contains(LoopTests.Document(invalid).Validate(), d => d.Code is "GEN082" or "GEN085");
        }
        var inner = Cs.While(Cs.Literal(true), Cs.Body(Cs.If(Cs.Id("ready"), Cs.Body(Cs.Break())), Cs.Continue()));
        Assert.Empty(LoopTests.Document(Cs.Try(Cs.Body(), finallyBody: Cs.Body(inner))).Validate());
        var rethrow = Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body(Cs.While(Cs.Literal(true), Cs.Body(Cs.Rethrow())))) });
        Assert.Empty(LoopTests.Document(rethrow).Validate());
    }

    [Fact]
    public void Loop_nesting_is_bounded_and_null_iterators_are_guarded_even_without_optional_validation()
    {
        CsStatement nested = Cs.Empty();
        for (int i = 0; i < 150; i++) nested = Cs.While(Cs.Literal(true), Cs.Body(nested));
        Assert.Contains(LoopTests.Document(nested).Validate(), d => d.Code == "GEN001");
        Assert.Throws<GenValidationException>(() => LoopTests.Document(nested).Render());
        Assert.Throws<GenValidationException>(() => LoopTests.Document(For() with { Iterators = null! })
            .Render(new RenderOptions { Validate = false }));
    }

    [Theory]
    [InlineData("for(;;) { continue; }", false)]
    [InlineData("while(true) { continue; }", false)]
    [InlineData("do { continue; } while(true);", false)]
    [InlineData("while(false) { return; }", true)]
    [InlineData("while(true) { break; }", true)]
    [InlineData("do { continue; } while(false);", true)]
    [InlineData("do { return; } while(false);", false)]
    [InlineData("foreach(var item in source) { return; }", true)]
    [InlineData("while(true) { while(true) { break; } continue; }", false)]
    [InlineData("while(true) { if(false) { break; } continue; }", false)]
    [InlineData("while(true) { if(ready) { break; } continue; }", true)]
    public void Switch_fallthrough_distinguishes_loop_break_continue_and_unreachable_endpoints(string loop, bool fallthrough)
    {
        var parsed = Cs.Parse("class C { void M() { switch(kind) { default: " + loop + " } } }");
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(fallthrough, parsed.Root.Validate().Any(d => d.Code == "GEN081"));
    }
}
