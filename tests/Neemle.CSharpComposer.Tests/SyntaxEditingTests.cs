using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SyntaxEditingTests
{
    [Theory]
    [InlineData("var result = state[1];\r\n")]
    [InlineData("var result = new Probe { Value = 1 };\r\n")]
    [InlineData("var result = new int[] { 1 };\r\n")]
    [InlineData("var result = ready ? 1 : 2;\r\n")]
    [InlineData("var result = $\"Count: {1}\";\r\n")]
    [InlineData("var result = from item in items where item > 1 select item;\r\n")]
    public void Scalar_operand_edits_retain_surrounding_source_and_line_endings(string source)
    {
        var tree = Cs.ParseTree(source).Root;
        var selected = tree.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "1" });
        var changed = tree.Replace(selected.Path, (CsExpression)selected.Node with { Text = "3" });
        string rendered = changed.Render();
        Assert.Contains("3", rendered);
        Assert.EndsWith("\r\n", rendered);
        Assert.DoesNotContain("\n", rendered.Replace("\r\n", ""));
        Assert.Equal(source, tree.Render());
        Assert.Empty(Cs.ParseTree(rendered).Diagnostics);
    }

    [Fact]
    public void Array_initializer_compounds_support_insertion_and_removal()
    {
        var tree = Cs.ParseTree("var values = new[] { 1, 2 };").Root;
        var options = tree.Descendants().Single(n => n.Node is CsSymbolOptions { AsInitializer: true });
        var changed = tree.Insert(options.Path, options.Node.Children.Count, Cs.Value(3));
        Assert.Contains("{ 1, 2, 3 }", changed.Render());
        var inserted = changed.Descendants().Single(n => n.Node is CsSymbol { Options.Value: CsExpression { Text: "3" } });
        var removed = changed.Remove(inserted.Path);
        Assert.Contains("{ 1, 2 }", removed.Render());
    }

    [Fact]
    public void Conditional_directives_are_idempotent_inside_indented_members()
    {
        const string source = "class C {\r\n    #if false\r\n    invalid \" source\r\n    #else\r\n    int value;\r\n    #endif\r\n}";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        string formatted = parsed.Root.Reformat().Render();
        var reparsed = Cs.Parse(formatted);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(formatted, reparsed.Root.Reformat().Render());
    }

    [Fact]
    public void Caller_supplied_preprocessor_symbols_select_typed_branches()
    {
        const string source = "#if FEATURE\nvar value=1;\n#else\ninvalid source\n#endif\n";
        var parsed = Cs.ParseTree(source, new ParseOptions { PreprocessorSymbols = new[] { "FEATURE" } });
        Assert.Empty(parsed.Diagnostics);
        Assert.Contains(parsed.Root.Items, n => n is CsSymbol { Name: "value" });
        Assert.Contains(parsed.Root.Items, n => n is CsDisabledText);
        Assert.Equal(source, parsed.Root.Render());
    }
}
