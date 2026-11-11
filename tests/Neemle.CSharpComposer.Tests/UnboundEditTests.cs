using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class UnboundEditTests
{
    [Fact]
    public void Unbound_modifier_return_type_and_body_edits_cannot_disappear()
    {
        var document = Cs.Parse("class C { int M() { return 1; } }").Root;
        var type = document.Types[0];
        var method = (CsMethod)type.Members[0];
        var edited = document with { Types = new[] { type with { Members = new CsMember[] {
            method with { Modifiers = new[] { "public" }, ReturnType = Cs.Type<string>(),
                Body = Cs.Body(Cs.Return(Cs.String("changed"))) } } } } };
        Assert.Contains("public string M()", edited.Render());
        Assert.Contains("return \"changed\";", edited.Render());
        Assert.Equal("class C { int M() { return 1; } }", document.Render());
    }
}
