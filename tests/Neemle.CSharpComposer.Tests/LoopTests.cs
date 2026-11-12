using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LoopTests
{
    internal static CsDocument Document(params CsStatement[] statements) => new()
    {
        Types = new[] {
        new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] {
            new CsMethod(Cs.Void, "M") { Body = Cs.Body(statements) } } } }
    };

    [Theory]
    [InlineData("class C { void M() { for (;;) { break; } } }")]
    [InlineData("class C { void M() { for (int i = 0, j = 10; i < j; i++, --j) { if (i == 2) { continue; } } } }")]
    [InlineData("class C { void M() { for (var i = 0; i < 10; ++i) Run(i); } }")]
    [InlineData("class C { void M() { for (i = 0, Reset(); ; i = i + 1, Tick()) { break; } } }")]
    [InlineData("class C { void M() { for (int i; ; ) ; } }")]
    [InlineData("class C { void M() { foreach (int item in source.Items) { if (item < 0) { break; } } } }")]
    [InlineData("class C { void M() { foreach (var item in source) Process(item); } }")]
    [InlineData("class C { void M() { foreach (global::System.Collections.Generic.List<int> item in source) { } } }")]
    [InlineData("class C { void M() { while (ready) { if (skip) { continue; } Work(); } } }")]
    [InlineData("class C { void M() { while (true) ; } }")]
    [InlineData("class C { void M() { do { Work(); } while (ready); } }")]
    [InlineData("class C { void M() { do if (flag) Tick(); else Tock(); while (ready); } }")]
    [InlineData("class C { void M() { do ; while (false); } }")]
    [InlineData("class C { void M() { for (;;) { foreach (var x in source) { while (ready) { do { continue; } while (flag); break; } } break; } } }")]
    [InlineData("class C { void M() { while (true) { switch (kind) { case 1: continue; default: break; } break; } } }")]
    public void Supported_loops_parse_without_raw_and_preserve_source_and_typed_coverage(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Root.Validate());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember ||
            n.Node is CsStatement { Kind: StatementKind.Raw } || n.Node is CsExpression { Kind: ExpressionKind.Raw });
        Assert.Equal(source, parsed.Root.Render());
        var reformatted = Cs.Parse(parsed.Root.Reformat().Render());
        Assert.Empty(reformatted.Diagnostics);
        Assert.Empty(reformatted.Root.Validate());
    }

    [Fact]
    public void Constructed_headers_keep_initializer_forms_order_and_immutable_collections()
    {
        var variables = new[] { Cs.Variable("i", Cs.Literal(0)), Cs.Variable("j", Cs.Literal(5)) };
        var initializer = Cs.ForDeclaration(Cs.Int, variables);
        var iterators = new[] { Cs.PostIncrement(Cs.Id("i")), Cs.PreDecrement(Cs.Id("j")) };
        var loop = Cs.For(initializer, Cs.Binary(CsOperator.LessThan, Cs.Id("i"), Cs.Id("j")), iterators, Cs.Body(Cs.Continue()));
        variables[0] = Cs.Variable("changed"); iterators[0] = Cs.Id("changed");
        string source = Document(loop).Render();
        Assert.Contains("for (int i = 0, j = 5; i < j; i++, --j)", source);
        Assert.Equal("i", loop.ForInitializer!.Variables[0].Name);
        Assert.Equal(ExpressionKind.PostIncrement, loop.Iterators[0].Kind);
        Assert.Equal(loop, loop.Detach());
        Assert.NotEqual(loop, loop with { Iterators = Values<CsExpression>.Empty });
        Assert.Equal(new CsNode[] { loop.ForInitializer, loop.Expression!, loop.Iterators[0], loop.Iterators[1], loop.Body! }, loop.Children);
        var @do = Cs.DoWhile(Cs.Body(Cs.Continue()), Cs.Id("ready"));
        Assert.Equal(new CsNode[] { @do.Body!, @do.Expression! }, @do.Children);
    }

    [Fact]
    public void Mutation_precedence_and_tokens_do_not_change_evaluation_order()
    {
        var expression = Cs.Binary(CsOperator.Add, Cs.PostIncrement(Cs.Id("i")), Cs.PreDecrement(Cs.Id("j")));
        Assert.Contains("i++ + --j", Document(Cs.Statement(expression)).Render());
        Assert.Contains("+(++i)", Document(Cs.Statement(Cs.Unary(CsOperator.UnaryPlus, Cs.PreIncrement(Cs.Id("i"))))).Render());
        Assert.Contains("(i++).ToString()", Document(Cs.Statement(Cs.PostIncrement(Cs.Id("i")).Member("ToString").Call())).Render());
    }

    [Theory]
    [InlineData("for(;;) break;")]
    [InlineData("foreach(var item in source) continue;")]
    [InlineData("while(ready) break;")]
    [InlineData("do { continue; } while(ready);")]
    public void Raw_statement_sequences_convert_to_typed_loops_on_the_minimum_language_target(string source)
    {
        var result = Cs.RawStatement(source).Parse(new(ParseKind.Statements, CsLanguageVersion.CSharp7_3));
        Assert.Equal(ParseCoverage.Typed, result.Coverage);
        var body = Assert.IsType<CsBody>(result.Root);
        Assert.Single(body.Statements);
        var document = Document(body.Statements[0]) with { LanguageVersion = CsLanguageVersion.CSharp7_3 };
        Assert.Empty(document.Validate());
    }
}
