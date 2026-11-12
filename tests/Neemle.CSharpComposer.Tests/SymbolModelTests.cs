using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SymbolModelTests
{
    internal static CsSymbol NewStream(params CsNode[] arguments) => Cs.Symbol(new()
    {
        Type = Cs.Type<MemoryStream>(),
        AsNew = true,
        Arguments = Cs.Compound(arguments)
    });

    [Fact]
    public void Symbol_options_own_inference_disposal_and_nested_constructor_arguments()
    {
        var ms = Cs.Symbol("ms", new() { AsVar = true, Using = true, Value = NewStream() });
        var tree = Cs.Tree(ms, Cs.Symbol("WriteByte", new() { Target = Cs.Symbol("ms"), AsCall = true, Arguments = Cs.Compound(Cs.Value(42)) }));
        Assert.Empty(tree.Validate());
        string source = tree.Render();
        Assert.Contains("using var ms = new global::System.IO.MemoryStream();", source);
        Assert.Contains("ms.WriteByte(42);", source);
        Assert.Same(ms.Options, ms.Children[0]);
        Assert.Same(ms.Options.Value, ms.Options.Children.Single());
        var initializer = Assert.IsType<CsSymbol>(ms.Options.Value);
        Assert.True(initializer.Options.Arguments!.Options.AsCompound);
        Assert.Empty(initializer.Options.Arguments.Options.Parts);
    }

    [Theory]
    [InlineData("using var ms = new MemoryStream(); ms.WriteByte(42);")]
    [InlineData("var ms = new MemoryStream(32); ms.WriteByte(42);")]
    [InlineData("int i = 0; i += 2; return i;")]
    [InlineData("for (var i=0; i<8; i+=2) { if(i==4) { continue; } Visit(i); }")]
    [InlineData("foreach (var item in items) { Visit(item); }")]
    [InlineData("await foreach (var item in items) { Visit(item); }")]
    [InlineData("await using var resource = new Resource();")]
    [InlineData("while (ready) { Work(); break; } do { Tick(); } while (again);")]
    public void Parsing_projects_symbols_and_instructions_into_the_same_tree(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Root.Validate());
        Assert.Equal(source, parsed.Root.Render());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsStatement or CsRaw);
        var formatted = parsed.Root.Reformat().Render();
        Assert.Empty(Cs.ParseTree(formatted).Diagnostics);
    }

    [Fact]
    public void Parsing_preserves_compound_argument_structure_and_edits_are_bound_to_the_snapshot()
    {
        const string source = "// before\r\nusing var ms = new MemoryStream(32); ms.WriteByte(7);\r\n";
        var parsed = Cs.ParseTree(source).Root;
        var ms = Assert.IsType<CsSymbol>(parsed.Items[0]);
        var creation = Assert.IsType<CsSymbol>(ms.Options.Value);
        Assert.Single(creation.Options.Arguments!.Options.Parts);
        var literal = parsed.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "7" });
        var changed = parsed.Replace(literal.Path, (CsExpression)literal.Node with { Text = "8" });
        Assert.Equal(source.Replace("(7)", "(8)"), changed.Render());
        Assert.Equal(source, parsed.Render());
        Assert.Throws<ArgumentException>(() => changed.At(literal.Path));
        Assert.Equal(parsed, parsed.Detach());
        Assert.NotEqual(parsed, changed);
    }

    [Fact]
    public void Compound_symbols_are_immutable_and_support_insertion_removal_and_move()
    {
        var supplied = new CsNode[] { Cs.Value(1), Cs.Value(2) };
        var args = Cs.Compound(supplied);
        var call = Cs.Symbol("Call", new() { AsCall = true, Arguments = args });
        var tree = Cs.Tree(call, Cs.Instruction(InstructionKind.Return));
        supplied[0] = Cs.Value(99);
        Assert.Contains("Call(1, 2);", tree.Render());
        var options = tree.Descendants().Single(n => ReferenceEquals(n.Node, args.Options));
        var inserted = tree.Insert(options.Path, 1, Cs.Value(3));
        Assert.Contains("Call(1, 3, 2);", inserted.Render());
        var item = inserted.Descendants().Single(n => n.Node is CsExpression { Text: "3" });
        var symbolPath = new NodePath(new Values<int>(item.Path.Segments.Take(item.Path.Segments.Count - 2)));
        var removed = inserted.Remove(symbolPath);
        Assert.Contains("Call(1, 2);", removed.Render());
        var returnPath = tree.Descendants().Single(n => n.Node is CsInstruction).Path;
        var moved = tree.Move(returnPath, NodePath.Root, 0);
        Assert.StartsWith("return;", moved.Render());
    }

    public static IEnumerable<object[]> InvalidModels()
    {
        yield return new object[] { Cs.Symbol("ms", new() { AsVar = true }), "SYM001" };
        yield return new object[] { Cs.Symbol("ms", new() { Using = true }), "SYM001" };
        yield return new object[] { Cs.Symbol("ms", new() { AsVar = true, Type = Cs.Int, Value = Cs.Value(0) }), "SYM001" };
        yield return new object[] { Cs.Symbol(new() { AsNew = true }), "SYM001" };
        yield return new object[] { Cs.Symbol(new() { AsNew = true, AsCall = true, Type = Cs.Int }), "SYM001" };
        yield return new object[] { Cs.Symbol("Call", new() { AsCall = true, Arguments = Cs.Symbol("notCompound") }), "SYM001" };
        yield return new object[] { Cs.Symbol("x; Inject()", new() { AsVar = true, Value = Cs.Value(0) }), "SYM001" };
        yield return new object[] { Cs.Symbol("x", new() { AsVar = true, Value = Cs.Instruction(InstructionKind.Break) }), "SYM001" };
        yield return new object[] { Cs.Instruction(InstructionKind.While), "SYM001" };
        yield return new object[] { Cs.Instruction(InstructionKind.Break, new() { Value = Cs.Value(0) }), "SYM001" };
        yield return new object[] { Cs.Instruction(InstructionKind.Continue), "GEN085" };
        yield return new object[] { Cs.Symbol("refOnly"), "SYM001" };
        yield return new object[] { new CsSymbol("bad", null!), "GEN002" };
        yield return new object[] { new CsInstruction(InstructionKind.Return, null!), "GEN002" };
        yield return new object[] { Cs.Compound(new CsNode[] { null! }), "GEN002" };
    }

    [Theory]
    [MemberData(nameof(InvalidModels))]
    public void Invalid_shapes_are_diagnostics_not_silent_option_loss(CsNode node, string code)
    {
        var tree = Cs.Tree(node);
        Assert.Contains(tree.Validate(), d => d.Code == code);
        Assert.Throws<GenValidationException>(() => tree.Render());
    }

    [Fact]
    public void Formatting_and_version_checks_apply_to_the_common_tree_and_to_method_bodies()
    {
        var each = Cs.Instruction(InstructionKind.ForEach, new()
        {
            Symbol = Cs.Symbol("item", new() { AsVar = true }),
            Value = Cs.Symbol("items"),
            Await = true,
            Body = Cs.Tree(Cs.Instruction(InstructionKind.Break))
        });
        var tree = Cs.Tree(each);
        Assert.Contains("await foreach (var item in items)", tree.Render());
        Assert.Contains("\n  break;", tree.Reformat(new RenderOptions { Indent = "  " }).Render());
        Assert.Contains((tree with { LanguageVersion = CsLanguageVersion.CSharp7_3 }).Validate(), d => d.Code == "GEN073");
        var document = Cs.Document().Class("C").Method(Cs.Void, "M").Body(tree).End().End().Build();
        Assert.Contains(document.Descendants(), n => n.Node is CsTree);
        Assert.Empty(document.Validate());
        Assert.Contains("await foreach", document.Render());
    }
}
