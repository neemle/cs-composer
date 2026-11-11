using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class ParenthesizedEditTests
{
    [Fact]
    public void Parenthesized_member_text_edit_is_not_discarded()
    {
        var document = Cs.Parse("class C { int M() { return (a.Member); } }").Root;
        var selected = document.Descendants().Single(x => x.Node is CsExpression { Kind: ExpressionKind.Member });
        var edited = document.Replace(selected.Path, ((CsExpression)selected.Node) with { Text = "Other" });
        Assert.Contains("a.Other", edited.Render());
        Assert.DoesNotContain("a.Member", edited.Render());
    }
}
