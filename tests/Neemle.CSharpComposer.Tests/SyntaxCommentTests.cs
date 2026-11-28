using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

[Trait("Category", "Unit")]
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
        // GENE-29 F4: every render formats; the comment is written once and the output is a fixed point.
        string formatted = parsed.Root.Render();
        string marker = source.Contains("//", StringComparison.Ordinal) ? "// first" : source[source.IndexOf("/*", StringComparison.Ordinal)..(source.IndexOf("*/", StringComparison.Ordinal) + 2)];
        Assert.Equal(1, formatted.Split(marker, StringSplitOptions.None).Length - 1);
        var reparsed = Cs.ParseTree(formatted);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(formatted, reparsed.Root.Reformat().Render());
    }
    [Theory]
    [InlineData("class C { List</* type */ int> values; }")]
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

    // GENE-29 F5 (AD B2): a comment after a parameter modifier is the leading comment of the parameter's type, which the
    // formatter writes after the modifier, where it reads back (F3 and F4 refused this render with EMIT001).
    [Fact]
    public void Parameter_comment_after_a_modifier_is_the_type_s_leading_comment()
    {
        var parsed = Cs.Parse("class C { void M(ref /* parameter */ int value) { } }");
        var type = parsed.Root.Descendants().Select(found => found.Node).OfType<CsParameter>().Single().Type!;
        Assert.Equal(CommentPlacement.Leading, Assert.Single(type.Comments).Placement);
        Assert.Contains("void M(ref /* parameter */ int value)", parsed.Root.Render(), StringComparison.Ordinal);
    }
}
