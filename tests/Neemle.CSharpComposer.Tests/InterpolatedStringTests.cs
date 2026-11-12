using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class InterpolatedStringTests
{
    [Theory]
    [InlineData("var text = $\"Hello {name}, {count,4:D2}!\";")]
    [InlineData("var text = $@\"Hello \"\"{name}\"\"\nnext\";")]
    [InlineData("var text = $\"{{value}} = {Math.Max(1, 2)}\";")]
    [InlineData("var text = $\"{(ready ? yes : no)} {Format(\"x\")}\";")]
    [InlineData("var text = $$\"\"\"{ \"value\": {{count}} }\"\"\";")]
    [InlineData("var text = $$\"\"\"\n    Value: {{count}}\n    next\n    \"\"\";")]
    public void Interpolation_operands_alignment_and_formats_are_typed(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsSymbol { Options.Operation: SymbolOperation.Interpolation });
        var formatted = parsed.Root.Reformat().Render();
        Assert.Empty(Cs.ParseTree(formatted).Diagnostics);
    }

    [Fact]
    public async Task All_string_styles_preserve_literal_values_and_formatting_semantics()
    {
        const string source = "int n=3; var regular=$\"{{x}}:{n,3:D2}\\n\\u007b\\u007b\"; " +
            "var verbatim=$@\"\"\"{n}\"\"\"; var raw=$$\"\"\"\n    {\"n\": {{n}}}\n    next\n    \"\"\"; " +
            "if(regular != \"{x}: 03\\n{{\" || verbatim != \"\\\"3\\\"\" || raw != \"{\\\"n\\\": 3}\\nnext\") { throw new System.Exception(regular+verbatim+raw); }";
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        await CompilerFixture.Run("class Program { static void Main() { " + parsed.Root.Reformat().Render() + " } }", "14.0");
    }

    [Fact]
    public void Strings_can_be_constructed_without_quoted_source_or_tokens()
    {
        var text = Cs.Symbol(new()
        {
            Operation = SymbolOperation.InterpolatedString,
            StringStyle = CsStringStyle.Raw,
            Parts = new CsNode[] { Cs.Symbol(new() { Operation = SymbolOperation.StringText, Text = "Count: " }),
                Cs.Symbol(new() { Operation = SymbolOperation.Interpolation, Value = Cs.Symbol("count"), Alignment = Cs.Value(4), Format = "D2" }) }
        });
        var tree = Cs.Tree(Cs.Symbol("text", new() { AsVar = true, Value = text }));
        Assert.Empty(tree.Validate());
        Assert.Contains("Count: {count, 4:D2}", tree.Render());
        Assert.Contains((tree with { LanguageVersion = CsLanguageVersion.CSharp10 }).Validate(), d => d.Code == "GEN073");
    }
    [Theory]
    [InlineData("")]
    [InlineData("\"quoted\"")]
    [InlineData("tail\"")]
    public async Task Raw_string_construction_handles_empty_and_quote_boundaries(string value)
    {
        var literal = Cs.Symbol(new() { Operation = SymbolOperation.StringLiteral, StringStyle = CsStringStyle.Raw, Text = value });
        string body = Cs.Tree(Cs.Symbol("text", new() { AsVar = true, Value = literal }),
            Cs.Instruction(InstructionKind.Return, new() { Value = Cs.Symbol("text") })).Render();
        Assert.Empty(Cs.ParseTree(body).Diagnostics);
        await CompilerFixture.Run("class Program { static string Run() { " + body + " } static void Main() { if(Run() != " + "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" + ") throw new System.Exception(); } }", "14.0");
    }
}
