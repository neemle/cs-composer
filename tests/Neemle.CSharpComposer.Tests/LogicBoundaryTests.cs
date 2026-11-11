using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LogicBoundaryTests
{
    [Fact]
    public void Nested_branches_throw_and_plain_blocks_render_without_changing_scope()
    {
        var branches = Cs.If(Cs.Id("first"), Cs.Body(Cs.Throw(Cs.Id("failure"))),
            Cs.If(Cs.Id("second"), Cs.Body(Cs.Return(Cs.Literal(2))), Cs.Block(Cs.Return(Cs.Literal(3)))));
        string output = StructuredLogicTests.Document(Cs.Block(Cs.Local(Cs.Int, "answer", Cs.Literal(1))),
            branches, Cs.If(Cs.Id("last"), Cs.Body(Cs.Return()))).Render();
        Assert.Contains("int answer = 1;", output);
        Assert.Contains("throw failure;", output);
        Assert.Contains("else\n        {\n            if (second)", output);
        Assert.Contains("return;", output);
        Assert.Contains("if (last)", output);
    }

    public static IEnumerable<object[]> InvalidPayloads()
    {
        yield return new object[] { Cs.Return() with { Body = Cs.Body() }, "GEN065" };
        yield return new object[] { Cs.Return() with { Type = Cs.Int }, "GEN065" };
        yield return new object[] { Cs.Return() with { IsVar = true }, "GEN065" };
        yield return new object[] { Cs.Return() with { Else = Cs.Block() }, "GEN065" };
        yield return new object[] { Cs.Return() with { Finally = Cs.Body() }, "GEN065" };
        yield return new object[] { Cs.Return() with { Catches = new[] { Cs.CatchAll(Cs.Body()) } }, "GEN065" };
        yield return new object[] { Cs.Return() with { Resource = Cs.Var("r", Cs.Id("r")) }, "GEN065" };
        yield return new object[] { Cs.Block() with { Expression = Cs.Literal(1) }, "GEN065" };
        yield return new object[]
            { Cs.Try(Cs.Body(), finallyBody: Cs.Body()) with { Expression = Cs.Id("x") }, "GEN065" };
        yield return new object[] { Cs.Rethrow() with { Expression = Cs.Id("x") }, "GEN065" };
        yield return new object[] { Cs.Var("x", Cs.Literal(1)) with { Type = Cs.Int }, "GEN065" };
        yield return new object[] { Cs.Block() with { Text = "unused" }, "GEN065" };
        yield return new object[] { Cs.Block() with { Body = CsBody.FromExpression(Cs.Literal(1)) }, "GEN066" };
        yield return new object[] { Cs.Using(Cs.Block(), Cs.Body()), "GEN071" };
        yield return new object[] { Cs.Using((CsExpression)null!, Cs.Body()), "GEN071" };
        yield return new object[] { Cs.Return() with { Catches = null! }, "GEN002" };
    }

    [Theory(DisplayName = "[SEC][UC-003] Direct malformed statement payloads fail through diagnostics")]
    [MemberData(nameof(InvalidPayloads))]
    public void Direct_payloads_cannot_smuggle_unused_or_missing_structure(CsStatement statement, string code)
    {
        var document = StructuredLogicTests.Document(statement);
        Assert.Contains(document.Validate(), error => error.Code == code);
        Assert.Throws<GenValidationException>(() => document.Render());
    }

    [Fact(DisplayName = "[SEC][UC-003] Invalid operators never become arbitrary C# tokens")]
    public void Closed_operator_kinds_and_unchecked_renderer_defense_are_independent()
    {
        var valid = Cs.Binary(CsOperator.Add, Cs.Id("a"), Cs.Id("b"));
        var malformed = new[] { valid with { Operator = CsOperator.None }, valid with { Operator = (CsOperator)999 },
            Cs.Unary(CsOperator.Add, Cs.Id("a")), valid with { Text = "injected" },
            Cs.Id("a") with { Operator = CsOperator.Add } };
        foreach (var expression in malformed)
        {
            var document = StructuredLogicTests.Document(Cs.Return(expression));
            Assert.Contains(document.Validate(), error => error.Code == "GEN064" || error.Code == "GEN065");
            Assert.Throws<GenValidationException>(() => document.Render());
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => StructuredLogicTests.Document(Cs.Return(
            valid with { Operator = CsOperator.None })).Render(new RenderOptions { Validate = false }));
        Assert.Throws<ArgumentOutOfRangeException>(() => StructuredLogicTests.Document(Cs.Return(
            Cs.Unary((CsOperator)999, Cs.Id("a")))).Render(new RenderOptions { Validate = false }));
    }

    [Fact(DisplayName = "[SEC][UC-003] New recursion and null handlers are bounded and path-stable")]
    public void Deep_blocks_and_null_handlers_do_not_hang_or_dereference()
    {
        CsStatement nested = Cs.Return();
        for (int i = 0; i < 150; i++) nested = Cs.Block(nested);
        var malformed = new[] { nested, Cs.Try(Cs.Body(), new CsCatch[] { null! }),
            Cs.Try(Cs.Body(), new[] { new CsCatch(null!) }), Cs.Try(Cs.Body()) with { Catches = null! },
            Cs.Try(Cs.Body(), new[] { new CsCatch(Cs.Body(), Name: "invalid") }) };
        foreach (var statement in malformed)
        {
            var document = StructuredLogicTests.Document(statement);
            Assert.Contains(document.Validate(), error => error.Code is "GEN001" or "GEN002" or "GEN069");
            Assert.Throws<GenValidationException>(() => document.Render());
        }
    }

    [Fact(DisplayName = "[SEC][UC-002] Local and catch identifiers reject injection beside trusted raw")]
    public void Structured_names_are_not_raw_escape_hatches()
    {
        var badLocal = Cs.Var("x; Inject()", Cs.Literal(1));
        var badCatch = Cs.Try(Cs.Body(), new[] { Cs.Catch(Cs.Type<Exception>(), Cs.Body(), "e) { Inject(); }") });
        foreach (var node in new[] { badLocal, badCatch })
        {
            var document = StructuredLogicTests.Document(Cs.RawStatement("Trusted();"), node);
            Assert.Contains(document.Validate(), error => error.Code == "GEN003");
            Assert.Throws<GenValidationException>(() => document.Render());
        }
        var trusted = StructuredLogicTests.Document(Cs.RawStatement("CallerOwnsGrammar???;"));
        Assert.Contains("CallerOwnsGrammar???;", trusted.Render());
    }
}
