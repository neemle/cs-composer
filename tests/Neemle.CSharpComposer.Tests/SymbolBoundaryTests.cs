using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SymbolBoundaryTests
{
    [Fact]
    public void Graph_depth_null_collections_and_unknown_instruction_kinds_fail_with_diagnostics()
    {
        CsNode nested = Cs.Value(0);
        for (int i = 0; i < 150; i++) nested = Cs.Symbol(new() { Value = nested });
        var deep = Cs.Tree(Cs.Symbol("x", new() { AsVar = true, Value = nested }));
        Assert.Contains(deep.Validate(), d => d.Code == "GEN001");
        var method = Cs.Document().Class("C").Method(Cs.Void, "M").Body(deep).End().End().Build();
        Assert.Contains(method.Validate(), d => d.Code == "GEN001");
        foreach (var malformed in new[] {
            new CsTree(null!), Cs.Tree(Cs.Compound() with { Options = new() { AsCompound = true, Parts = null! } }),
            Cs.Tree(new CsSymbol(null, new() { AsCall = true, Arguments = new CsSymbol(null, null!) })) })
            Assert.Contains(malformed.Validate(), d => d.Code == "GEN002");
        Assert.Contains(Cs.Tree(Cs.Instruction((InstructionKind)999)).Validate(), d => d.Code == "SYM001");
    }

    [Fact]
    public void Invalid_sources_are_reported_and_retained_instead_of_disappearing()
    {
        const string source = "using var ms = new MemoryStream(;";
        var parsed = Cs.ParseTree(source);
        Assert.NotEmpty(parsed.Diagnostics);
        Assert.Equal(source, Assert.IsType<CsRaw>(Assert.Single(parsed.Root.Items)).Text);
        Assert.Throws<GenValidationException>(() => parsed.Root.Render());
    }

    [Fact]
    public void Parsed_constructor_values_can_move_to_a_statement_without_duplicate_source_ownership()
    {
        var parsed = Cs.ParseTree("var ms = new MemoryStream();").Root;
        var creation = Assert.IsType<CsSymbol>(Assert.IsType<CsSymbol>(parsed.Items[0]).Options.Value);
        var tree = Cs.Tree(creation);
        Assert.Empty(tree.Validate());
        Assert.Contains("new MemoryStream()", tree.Render());
    }

    [Fact]
    public void Reformatting_selected_subtrees_retains_outer_text_and_formats_comments()
    {
        const string source = "// start\r\nvar x=0; while(ready) { /* loop */ x+=2; } Tail();\r\n";
        var parsed = Cs.ParseTree(source).Root;
        var loop = parsed.Descendants().Single(n => n.Node is CsInstruction { Kind: InstructionKind.While });
        var formatted = parsed.Reformat(loop.Path);
        string rendered = formatted.Render();
        Assert.StartsWith("// start\r\nvar x=0; ", rendered);
        Assert.EndsWith(" Tail();\r\n", rendered);
        Assert.Contains("/* loop */", rendered);
    }
}
