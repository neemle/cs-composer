using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class PrimitiveExpressionTests
{
    [Theory]
    [InlineData("var x = (a & b) | (c ^ d); x <<= 2; x >>= 1; x >>>= 1;")]
    [InlineData("var x = a + b << 2; var y = a >>> b;")]
    [InlineData("var x = ~a; var y = *ptr; var z = &x;")]
    [InlineData("var x = this.Value; base.Run();")]
    [InlineData("var x = input as string; var y = sizeof(int); var z = nameof(this.Value);")]
    [InlineData("var x = checked(a + b); var y = unchecked((byte)300);")]
    [InlineData("var x = input!.Length; Update(ref x, y: 2, result: out var result);")]
    [InlineData("var x = Factory.Create<int, string>(1, text: \"x\");")]
    [InlineData("var x = (first: 1, second: 2); (a, b) = (b, a);")]
    [InlineData("var x = input[1..^1]; var y = input[..]; var z = input[..2];")]
    [InlineData("int[] x = [1, ..other, 2];")]
    [InlineData("var x = new { Name = \"x\", Count = 2 }; var y = x with { Count = 3 };")]
    [InlineData("var x = new Dictionary<int, int> { [1] = 2, [3] = 4 };")]
    [InlineData("var x = stackalloc int[3]; var y = stackalloc[] { 1, 2 };")]
    [InlineData("var x = a ?? b ?? c; var y = a ?? b ? c : d;")]
    [InlineData("return ref state[i];")]
    public void Operations_roundtrip_as_symbols_without_raw_or_token_nodes(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Equal(source, parsed.Root.Render());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw || n.Node is CsExpression { Kind: not ExpressionKind.Literal });
        var formatted = parsed.Root.Reformat().Render();
        var reparsed = Cs.ParseTree(formatted);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(formatted, reparsed.Root.Reformat().Render());
    }

    [Theory]
    [InlineData("var x = input[1..];", CsLanguageVersion.CSharp7_3)]
    [InlineData("int[] x = [1, 2];", CsLanguageVersion.CSharp11)]
    [InlineData("var x = a >>> b;", CsLanguageVersion.CSharp10)]
    [InlineData("var x = other with {};", CsLanguageVersion.CSharp8)]
    public void New_operations_respect_language_versions(string source, CsLanguageVersion version) =>
        Assert.Contains(Cs.ParseTree(source, version).Diagnostics, d => d.Code == "GEN073");

    [Fact]
    public async Task Operations_compile_and_preserve_associativity_and_argument_semantics()
    {
        const string source = "int[] items = [1, 2, 3, 4]; var part = items[1..^1]; int x = 4; x <<= 2; x >>= 1; x >>>= 1; " +
            "Apply(ref x, add: 3, result: out var result); var tuple = (first: result, second: part.Length); " +
            "var anonymous = new { Value = tuple.first }; var changed = anonymous with { Value = 10 }; " +
            "return checked(changed.Value + tuple.second) + Generic<int>(x) + sizeof(int) + (8 | 3 & 1);";
        var parsed = Cs.ParseTree(source, CsLanguageVersion.CSharp14);
        Assert.Empty(parsed.Diagnostics);
        string body = parsed.Root.Reformat().Render();
        await CompilerFixture.Run("class Program { static void Apply(ref int x, int add, out int result) { x += add; result=x; } " +
            "static T Generic<T>(T x) { return x; } static int Run() { " + body + " } static void Main() { if(Run()!=32) throw new System.Exception(Run().ToString()); } }", "14.0");
    }

    [Fact]
    public void Primitive_operations_are_constructible_with_nested_options()
    {
        var tuple = Cs.Symbol(new()
        {
            Operation = SymbolOperation.Tuple,
            Parts = new CsNode[] {
            Cs.Symbol("first", new() { Operation = SymbolOperation.Argument, Value = Cs.Value(1) }), Cs.Value(2) }
        });
        var result = Cs.Tree(Cs.Symbol("pair", new() { AsVar = true, Value = tuple }));
        Assert.Empty(result.Validate());
        Assert.Contains("(first: 1, 2)", result.Render());
    }
}
