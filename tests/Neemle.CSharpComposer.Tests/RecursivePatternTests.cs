using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class RecursivePatternTests
{
    [Theory]
    [InlineData("return item is { Value: > 1, Child: { Name: not null } };")]
    [InlineData("return item is Probe(1, var second) { Child.Value: >= 2 } named;")]
    [InlineData("return pair is (first: 1, second: > 2);")]
    [InlineData("return pair is (int first, int second);")]
    [InlineData("return values is [1, .. var middle, > 3] all;")]
    [InlineData("return values is [] or [_, ..];")]
    [InlineData("return value is not (int or string);")]
    public void Nested_patterns_parse_validate_and_format(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        var formatted = parsed.Root.Reformat().Render();
        Assert.Empty(Cs.ParseTree(formatted).Diagnostics);
    }

    [Theory]
    [InlineData("return values is [.., ..];")]
    [InlineData("return item is not { Value: var named };")]
    public void Invalid_pattern_bindings_are_rejected(string source) => Assert.NotEmpty(Cs.ParseTree(source).Diagnostics);

    [Theory]
    [InlineData("return item is { Value: 1 };", CsLanguageVersion.CSharp7_3)]
    [InlineData("return item is { Child.Value: 1 };", CsLanguageVersion.CSharp9)]
    [InlineData("return values is [1, ..];", CsLanguageVersion.CSharp10)]
    public void Pattern_features_are_versioned(string source, CsLanguageVersion version) => Assert.Contains(Cs.ParseTree(source, version).Diagnostics, d => d.Code == "GEN073");

    [Fact]
    public async Task Recursive_positional_and_list_patterns_compile_and_execute()
    {
        const string body = "var pair = (first: 1, second: 3); int[] values = [1, 2, 3, 4]; " +
            "return pair is (first: 1, second: > 2) && values is [1, .. var middle, > 3] && middle.Length == 2;";
        var parsed = Cs.ParseTree(body);
        Assert.Empty(parsed.Diagnostics);
        await CompilerFixture.Run("class Program { static bool Run() { " + parsed.Root.Reformat().Render() +
            " } static void Main() { if(!Run()) throw new System.Exception(); } }", "14.0");
    }
}
