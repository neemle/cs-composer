using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class OriginSafetyTests
{
    [Fact]
    public void Wrapped_types_do_not_duplicate_tokens_during_unrelated_edits()
    {
        const string source = "class C { List<int>[]? F; List<bool> M() { return F; } }";
        var document = Cs.Parse(source).Root;
        var type = document.Types[0];
        var method = Assert.IsType<CsMethod>(type.Members[1]);
        var edited = document with { Types = new[] { type with { Members = new CsMember[] {
            type.Members[0], method with { Name = "Changed" } } } } };
        Assert.Equal(source.Replace(" M()", " Changed()", StringComparison.Ordinal), edited.Render());
    }

    [Fact]
    public void Foreign_return_type_and_expression_origins_never_index_the_owner_snapshot()
    {
        var document = Cs.Parse("class C { int M() { return 1; } }").Root;
        var foreign = Cs.Parse("class Foreign { string Longer() { return \"foreign\"; } }").Root;
        var original = (CsMethod)document.Types[0].Members[0];
        var other = (CsMethod)foreign.Types[0].Members[0];
        var replacement = original with { ReturnType = other.ReturnType,
            Body = original.Body! with { Statements = new[] { original.Body!.Statements[0] with {
                Expression = other.Body!.Statements[0].Expression } } } };
        var edited = document with { Types = new[] { document.Types[0] with {
            Members = new CsMember[] { replacement } } } };
        Assert.Contains("string M()", edited.Render());
        Assert.Contains("return \"foreign\";", edited.Render());
        Assert.DoesNotContain("Longer", edited.Render());
    }
}
