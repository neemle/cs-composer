using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class BindingSyntaxTests
{
    [Theory]
    [InlineData("int first = 1, second = 2;")]
    [InlineData("const int first = 1, second = 2;")]
    [InlineData("ref int item = ref items[0]; item += 1;")]
    [InlineData("scoped ref readonly int item = ref items[0];")]
    [InlineData("using Stream first = Open(), second = Open();")]
    [InlineData("using (Stream first = Open(), second = Open()) { Run(); }")]
    [InlineData("var (first, second) = pair;")]
    [InlineData("(int first, string second) = pair;")]
    [InlineData("foreach (var (first, second) in pairs) { Visit(first); }")]
    [InlineData("foreach ((int first, int second) in pairs) { Visit(second); }")]
    [InlineData("foreach (ref readonly var item in values) { Visit(item); }")]
    public void Bindings_and_groups_roundtrip_as_semantic_nodes(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        Assert.Empty(Cs.ParseTree(parsed.Root.Reformat().Render()).Diagnostics);
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw || n.Node is CsExpression { Kind: ExpressionKind.Raw });
    }

    [Fact]
    public async Task Deconstruction_reference_bindings_and_grouped_declarations_compile_and_execute()
    {
        const string body = "int first=1, second=2; var (x,y) = (first,second); (int a,int b)=(3,4); int[] data=[1,2]; " +
            "ref int item = ref data[0]; item += 4; int sum=x+y+a+b+data[0]; " +
            "foreach (var (left,right) in new[] {(1,2),(3,4)}) { sum += left+right; } return sum;";
        var parsed = Cs.ParseTree(body);
        Assert.Empty(parsed.Diagnostics);
        await CompilerFixture.Run("class Program { static int Run() { " + parsed.Root.Reformat().Render() +
            " } static void Main() { if(Run()!=25) throw new System.Exception(Run().ToString()); } }", "14.0");
    }
}
