using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class QueryAndFunctionTests
{
    [Theory]
    [InlineData("var q = from x in items where x > 0 orderby x descending select x;")]
    [InlineData("var q = from int x in items let y = x * 2 from z in others where y == z select new { x, z };")]
    [InlineData("var q = from x in items join y in others on x.Id equals y.Id into matches select matches;")]
    [InlineData("var q = from x in items group x by x.Key into groups where groups.Count() > 0 select groups;")]
    [InlineData("var f = delegate(int x) { return x + 1; };")]
    [InlineData("Action f = delegate { Run(); };")]
    [InlineData("var f = static int (int x = 1) => x + 1;")]
    [InlineData("var f = ([Marker] int x) => x;")]
    [InlineData("var f = [Marker] (int x) => x;")]
    [InlineData("F f = (ref x) => x++;")]
    [InlineData("F f = ref int (ref int x) => ref x;")]
    [InlineData("var f = (params int[] values) => values.Length;")]
    public void Query_and_function_forms_are_typed_and_roundtrip(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        var formatted = parsed.Root.Reformat().Render();
        Assert.Empty(Cs.ParseTree(formatted).Diagnostics);
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw || n.Node is CsExpression { Kind: ExpressionKind.Raw });
    }

    [Fact]
    public async Task Queries_anonymous_methods_and_optional_lambdas_compile_and_run()
    {
        const string source = "int[] items = [1, 2, 3, 4]; var q = from x in items let y = x * 2 where y > 4 orderby y descending select y; " +
            "Func<int, int> old = delegate(int x) { return x + 1; }; var optional = (int x = 1) => x + 2; return q.Sum() + old(2) + optional();";
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        await CompilerFixture.Run("using System; using System.Linq; class Program { static int Run() { " + parsed.Root.Reformat().Render() +
            " } static void Main() { if(Run()!=20) throw new Exception(Run().ToString()); } }", "14.0");
    }

    [Theory]
    [InlineData("var f = int (int x) => x;", CsLanguageVersion.CSharp9)]
    [InlineData("var f = (int x = 1) => x;", CsLanguageVersion.CSharp11)]
    [InlineData("F f = (ref x) => x++;", CsLanguageVersion.CSharp13)]
    public void Function_features_are_versioned(string source, CsLanguageVersion version) => Assert.Contains(Cs.ParseTree(source, version).Diagnostics, d => d.Code == "GEN073");
}
