using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class StructuredLogicTests
{
    internal static CsDocument Document(params CsStatement[] statements) => new CsDocument("Logic.g.cs")
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Class, "Logic")
        {
            Members = new CsMember[] { new CsMethod(Cs.Int, "Compute") { Body = Cs.Body(statements) } }
        } }
    };

    [Theory]
    [InlineData(CsOperator.Add, "+")]
    [InlineData(CsOperator.Subtract, "-")]
    [InlineData(CsOperator.Multiply, "*")]
    [InlineData(CsOperator.Divide, "/")]
    [InlineData(CsOperator.Modulo, "%")]
    [InlineData(CsOperator.Equal, "==")]
    [InlineData(CsOperator.NotEqual, "!=")]
    [InlineData(CsOperator.LessThan, "<")]
    [InlineData(CsOperator.LessThanOrEqual, "<=")]
    [InlineData(CsOperator.GreaterThan, ">")]
    [InlineData(CsOperator.GreaterThanOrEqual, ">=")]
    [InlineData(CsOperator.AndAlso, "&&")]
    [InlineData(CsOperator.OrElse, "||")]
    public void Binary_nodes_are_inspectable_and_preserve_operand_order(CsOperator op, string token)
    {
        var expression = Cs.Binary(op, Cs.Id("left"), Cs.Id("right"));
        Assert.Equal(ExpressionKind.Binary, expression.Kind);
        Assert.Equal(op, expression.Operator);
        Assert.Equal(new[] { Cs.Id("left"), Cs.Id("right") }, expression.Arguments);
        Assert.Contains("return left " + token + " right;", Document(Cs.Return(expression)).Render());
    }

    [Theory]
    [InlineData(CsOperator.UnaryPlus, "+")]
    [InlineData(CsOperator.Negate, "-")]
    [InlineData(CsOperator.Not, "!")]
    public void Unary_tokens_cannot_merge_into_increment_or_decrement(CsOperator op, string token)
    {
        var expression = Cs.Unary(op, Cs.Unary(op, Cs.Id("operand")));
        Assert.Contains("return " + token + "(" + token + "operand);", Document(Cs.Return(expression)).Render());
    }

    [Fact]
    public void Precedence_retains_right_nested_arithmetic_and_composite_receivers()
    {
        var right = Cs.Binary(CsOperator.Subtract, Cs.Id("a"),
            Cs.Binary(CsOperator.Subtract, Cs.Id("b"), Cs.Id("c")));
        var product = Cs.Binary(CsOperator.Multiply,
            Cs.Binary(CsOperator.Add, Cs.Id("a"), Cs.Id("b")), Cs.Id("c"));
        Assert.Contains("return a - (b - c);", Document(Cs.Return(right)).Render());
        Assert.Contains("return (a + b) * c;", Document(Cs.Return(product)).Render());
        Assert.Contains("return (a - (b - c)).Method();", Document(Cs.Return(right.Member("Method").Call())).Render());
        Assert.Contains("return await (a + b);", Document(Cs.Return(Cs.Await(
            Cs.Binary(CsOperator.Add, Cs.Id("a"), Cs.Id("b"))))).Render());
    }

    [Fact]
    public void Blocks_exceptions_and_both_using_resource_forms_keep_structure()
    {
        var handlers = new[] { Cs.Catch(Cs.Type<Exception>(), Cs.Body(Cs.Rethrow()), "error"),
            Cs.CatchAll(Cs.Body(Cs.Return(Cs.Literal(1)))) };
        var guarded = Cs.Try(Cs.Body(Cs.Local(Cs.Int, "count"), Cs.Var("answer", Cs.Literal(2))),
            handlers, Cs.Body(Cs.Statement(Cs.Id("Cleanup").Call())));
        var document = Document(Cs.Using(Cs.Id("resource"), Cs.Body(guarded)),
            Cs.Using(Cs.Var("owned", Cs.Id("Open").Call()), Cs.Body(Cs.Block(Cs.Return(Cs.Literal(3))))));
        string output = document.Render();
        Assert.Empty(document.Validate());
        Assert.Contains("using (resource)", output);
        Assert.Contains("using (var owned = Open())", output);
        Assert.Contains("catch (global::System.Exception error)", output);
        Assert.Contains("throw;", output);
        Assert.Contains("finally", output);
        Assert.Equal(Cs.Id("resource"), document.Types[0].Members.OfType<CsMethod>().Single()
            .Body!.Statements[0].Expression);
    }

    [Fact(DisplayName = "[SEC][UC-003] Invalid structured statement payloads reject with stable diagnostics")]
    public void Invalid_structured_shapes_reject_with_stable_paths()
    {
        var bad = new[] { Cs.Var("x", null!), Cs.Try(Cs.Body()), Cs.Rethrow(),
            Cs.Using(Cs.Local(Cs.Int, "resource"), Cs.Body()), Cs.If(Cs.Literal(true), Cs.Body(), Cs.Return()),
            Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body()), Cs.Catch(Cs.Type<Exception>(), Cs.Body()) }) };
        var codes = new[] { "GEN067", "GEN068", "GEN070", "GEN071", "GEN065", "GEN069" };
        for (int i = 0; i < bad.Length; i++)
        {
            var document = Document(bad[i]);
            Assert.Contains(document.Validate(), error => error.Code == codes[i] &&
                error.Path.StartsWith("Types[0].Members[0].Body.Statements[0]", StringComparison.Ordinal));
            Assert.Throws<GenValidationException>(() => document.Render());
        }
    }

    [Fact(DisplayName = "[SEC][UC-003] Rethrow context resets at finally and reenters nested catches")]
    public void Rethrow_context_is_lexical_and_does_not_leak_from_a_handler()
    {
        var nestedFinally = Cs.Try(Cs.Body(), finallyBody: Cs.Body(Cs.Rethrow()));
        var invalid = Document(Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body(nestedFinally)) }));
        Assert.Contains(invalid.Validate(), error => error.Code == "GEN070");
        var valid = Document(Cs.Try(Cs.Body(), finallyBody: Cs.Body(
            Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body(Cs.If(Cs.Literal(true), Cs.Body(Cs.Rethrow())))) }))));
        Assert.Empty(valid.Validate());
    }

    [Fact(DisplayName = "[SEC][UC-001] New handler snapshots and operator equality retain immutable intent")]
    public void Reused_handler_collections_are_frozen_and_operator_identity_affects_equality()
    {
        var original = Cs.CatchAll(Cs.Body(Cs.Return(Cs.Literal(1))));
        var supplied = new[] { original };
        var statement = Cs.Try(Cs.Body(), supplied);
        supplied[0] = Cs.CatchAll(Cs.Body(Cs.Return(Cs.Literal(9))));
        Assert.Equal(original, statement.Catches[0]);
        var sum = Cs.Binary(CsOperator.Add, Cs.Id("a"), Cs.Id("b"));
        Assert.Equal(sum, Cs.Binary(CsOperator.Add, Cs.Id("a"), Cs.Id("b")));
        Assert.NotEqual(sum, sum with { Operator = CsOperator.Subtract });
        Assert.Equal(Document(statement).Render(), Document(statement).Render());
    }
}
