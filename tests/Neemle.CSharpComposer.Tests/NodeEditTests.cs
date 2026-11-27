using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
[Trait("Category", "Unit")]
public sealed class NodeEditTests
{
    [Fact]
    public void Move_carries_comments_and_does_not_change_original_snapshot()
    {
        const string source = "class C\n{\n    // A\n    int A()\n    {\n        return 1;\n    }\n\n    int B()\n    {\n        return 2;\n    }\n}\n";
        var document = Cs.Parse(source).Root;
        var typePath = document.Descendants().Single(x => x.Node is CsTypeDeclaration).Path;
        var sourcePath = document.Descendants().Single(x => x.Node is CsMethod { Name: "A" }).Path;
        var moved = document.Move(sourcePath, typePath, document.Types[0].Children.Count);
        FormatCheck.FixedPoint(document);
        Assert.True(moved.Render().IndexOf("B()", StringComparison.Ordinal) < moved.Render().IndexOf("A()", StringComparison.Ordinal));
        Assert.Contains("// A\n    int A()", moved.Render());
        Assert.Throws<ArgumentException>(() => moved.At(sourcePath));
        var copied = (CsMethod)document.Types[0].Members[0].Detach();
        Assert.Equal(document.Types[0].Members[0], copied);
    }

    [Fact]
    public void Formatting_preserves_a_multiline_literal_value()
    {
        const string source = "class C { string A() { return @\"first\nsecond\"; } int B(){return 2;} }";
        string formatted = Cs.Parse(source).Root.Render();
        Assert.Contains("@\"first\nsecond\"", formatted);
        Assert.EndsWith("    int B()\n    {\n        return 2;\n    }\n}\n", formatted, StringComparison.Ordinal);
        Assert.Equal(formatted, Cs.Parse(formatted).Root.Render());
    }
}
