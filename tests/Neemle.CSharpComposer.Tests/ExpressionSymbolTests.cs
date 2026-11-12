using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class ExpressionSymbolTests
{
    static CsSymbol Ref(string name) => Cs.Symbol(name);
    static CsSymbol Init(params CsNode[] parts) => Cs.Symbol(new() { AsInitializer = true, Parts = parts });
    static CsSymbol Index(CsNode target, params CsNode[] indexes) => Cs.Symbol(new() { AsIndex = true, Target = target, Arguments = Cs.Compound(indexes) });
    static string Return(CsNode value) => Cs.Tree(Cs.Instruction(InstructionKind.Return, new() { Value = value })).Render().Trim();

    [Fact]
    public void Generator_expressions_are_composed_from_nested_symbols()
    {
        Assert.Equal("return state[i];", Return(Index(Ref("state"), Ref("i"))));
        Assert.Equal("return (int)input;", Return(Cs.Symbol(new() { AsCast = true, Type = Cs.Int, Value = Ref("input") })));
        Assert.Equal("return typeof(int);", Return(Cs.Symbol(new() { AsTypeOf = true, Type = Cs.Int })));
        Assert.Equal("return default(int);", Return(Cs.Symbol(new() { AsDefault = true, Type = Cs.Int })));
        Assert.Equal("return default;", Return(Cs.Symbol(new() { AsDefault = true })));
        Assert.Equal("return input is int;", Return(Cs.Symbol(new() { AsIs = true, Type = Cs.Int, Value = Ref("input") })));
        Assert.Equal("return input?.Length;", Return(Cs.Symbol("Length", new() { Target = Ref("input"), ConditionalAccess = true })));
        Assert.Equal("return input?[0];", Return(Index(Ref("input"), Cs.Value(0)) with { Options = Index(Ref("input"), Cs.Value(0)).Options with { ConditionalAccess = true } }));
        Assert.Equal("return ready ? 1 : 0;", Return(Cs.Symbol(new() { AsConditional = true, Parts = new CsNode[] { Ref("ready"), Cs.Value(1), Cs.Value(0) } })));
        Assert.Equal("return new int[3];", Return(Cs.Symbol(new() { AsArray = true, Type = Cs.Int, Arguments = Cs.Compound(Cs.Value(3)) })));
        Assert.Equal("return new [] { 1, 2 };", Return(Cs.Symbol(new() { AsArray = true, Initializer = Init(Cs.Value(1), Cs.Value(2)) })));
        Assert.Equal("return new Probe() { Value = 3 };", Return(Cs.Symbol(new() { AsNew = true, Type = Cs.Type("Probe"), Initializer = Init(Cs.Symbol("Value", new() { AsAssignment = true, Value = Cs.Value(3) })) })));
        Assert.Equal("state[i] ??= input;", Cs.Tree(Cs.Symbol(new() { AsAssignment = true, Target = Index(Ref("state"), Ref("i")), Value = Ref("input"), Operator = CsOperator.Coalesce })).Render().Trim());
    }

    [Theory]
    [InlineData("var result = state[i];")]
    [InlineData("var result = (int)input;")]
    [InlineData("var result = typeof(System.Collections.Generic.List<int>);")]
    [InlineData("var result = default(int);")]
    [InlineData("var result = value is int ? 1 : 0;")]
    [InlineData("var result = value is null ? 1 : 0;")]
    [InlineData("var result = value?.Items?[i].Length ?? 0;")]
    [InlineData("state[i] ??= new Probe { Value = 3, Child = { Value = 4 } };")]
    [InlineData("var result = new[] { 1, 2, 3 };")]
    [InlineData("var result = new int[2, 2] { { 1, 2 }, { 3, 4 } };")]
    [InlineData("var result = new int[2][] { new[] { 1 }, new[] { 2 } };")]
    [InlineData("var result = (value?.Child).Length;")]
    [InlineData("var result = a ?? b ? c : d;")]
    public void Parsing_retains_all_operations_as_semantic_symbols(string source)
    {
        var result = Cs.ParseTree(source);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, result.Coverage);
        Assert.Equal(source, result.Root.Render());
        Assert.DoesNotContain(result.Root.Descendants(), n => n.Node is CsRaw || n.Node is CsExpression { Kind: not ExpressionKind.Literal });
        var fresh = result.Root.Reformat().Render();
        var reparsed = Cs.ParseTree(fresh);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(fresh, reparsed.Root.Reformat().Render());
    }

    [Theory]
    [InlineData("var a = new int[];", "GEN092")]
    [InlineData("var a = new[] {};", "GEN092")]
    [InlineData("var a = new[3] { 1 };", "GEN092")]
    [InlineData("var a = { 1 };", "GEN092")]
    public void Invalid_creation_shapes_report_diagnostics(string source, string code) => Assert.Contains(Cs.ParseTree(source).Diagnostics, d => d.Code == code);

    [Fact]
    public void Features_have_language_version_boundaries()
    {
        Assert.Contains(Cs.ParseTree("x ??= y;", CsLanguageVersion.CSharp7_3).Diagnostics, d => d.Code == "GEN073");
        Assert.Empty(Cs.ParseTree("x ??= y;", CsLanguageVersion.CSharp8).Diagnostics);
        Assert.Contains(Cs.ParseTree("Probe x = new();", CsLanguageVersion.CSharp8).Diagnostics, d => d.Code == "GEN073");
        Assert.Empty(Cs.ParseTree("Probe x = new();", CsLanguageVersion.CSharp9).Diagnostics);
        Assert.Contains(Cs.ParseTree("x?.Value = 2;", CsLanguageVersion.CSharp13).Diagnostics, d => d.Code == "GEN073");
        Assert.Empty(Cs.ParseTree("x?.Value = 2;", CsLanguageVersion.CSharp14).Diagnostics);
    }

    [Theory]
    [InlineData("7.3", CsLanguageVersion.CSharp7_3)]
    [InlineData("14.0", CsLanguageVersion.CSharp14)]
    public async Task Parsed_and_reformatted_expressions_compile_and_preserve_behavior(string language, CsLanguageVersion version)
    {
        const string body = "int[] state = new int[] { 3, 5 }; object boxed = state[1]; " +
            "var probe = new Probe { Value = (int)boxed, Values = new[] { 1, 2 } }; " +
            "Probe absent = null; var length = absent?.Values?[0] ?? 7; " +
            "return (boxed is int ? probe.Value : default(int)) + length + (typeof(int) == boxed.GetType() ? 1 : 0);";
        var tree = Cs.ParseTree(body, version);
        Assert.Empty(tree.Diagnostics);
        string generated = tree.Root.Reformat().Render();
        await CompilerFixture.Run("class Probe { public int Value; public int[] Values; } class Program { static int Run() { " + generated +
            " } static void Main() { if(Run()!=13) throw new System.Exception(); } }", language);
    }
}
