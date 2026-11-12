using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SyntaxCommentTests
{
    [Theory]
    [InlineData("var x = Call(/* argument */ 1);")]
    [InlineData("var x = a /* operator */ + b;")]
    [InlineData("var x = value is var (/* first */ a, b);")]
    [InlineData("var x = new Probe { Value /* equals */ = 1 };")]
    [InlineData("var x = new[] {\n// first\n1, 2 };")]
    [InlineData("return value switch { /* arm */ _ => 1 };")]
    public void Comments_inside_new_forms_survive_once_after_formatting(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        string formatted = parsed.Root.Reformat().Render();
        string marker = source.Contains("//", StringComparison.Ordinal) ? "// first" : source[source.IndexOf("/*", StringComparison.Ordinal)..(source.IndexOf("*/", StringComparison.Ordinal) + 2)];
        Assert.Equal(1, formatted.Split(marker, StringSplitOptions.None).Length - 1);
        var reparsed = Cs.ParseTree(formatted);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(formatted, reparsed.Root.Reformat().Render());
    }
    [Theory]
    [InlineData("class C { List</* type */ int> values; }")]
    [InlineData("class C { void M(ref /* parameter */ int value) { } }")]
    [InlineData("[Marker(/* attribute */ 1)] class C { }")]
    [InlineData("class C { int Value { get /* accessor */; set; } }")]
    public void Declaration_comments_survive_formatting(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        string marker = source[source.IndexOf("/*", StringComparison.Ordinal)..(source.IndexOf("*/", StringComparison.Ordinal) + 2)];
        string formatted = parsed.Root.Reformat().Render();
        Assert.Equal(1, formatted.Split(marker, StringSplitOptions.None).Length - 1);
        Assert.Empty(Cs.Parse(formatted).Diagnostics);
    }
}
