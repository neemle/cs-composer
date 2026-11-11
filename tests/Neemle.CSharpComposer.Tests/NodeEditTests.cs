using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class NodeEditTests
{
    [Fact]
    public void Move_carries_comments_and_does_not_change_original_snapshot()
    {
        const string source = "class C\n{\n    // A\n    int A() { return 1; }\n    int B() { return 2; }\n}\n";
        var document = Cs.Parse(source).Root;
        var typePath = document.Descendants().Single(x => x.Node is CsTypeDeclaration).Path;
        var sourcePath = document.Descendants().Single(x => x.Node is CsMethod { Name: "A" }).Path;
        var moved = document.Move(sourcePath, typePath, document.Types[0].Children.Count);
        Assert.Equal(source, document.Render());
        Assert.True(moved.Render().IndexOf("B()", StringComparison.Ordinal) < moved.Render().IndexOf("A()", StringComparison.Ordinal));
        Assert.Contains("// A\n    int A()", moved.Render());
        Assert.Throws<ArgumentException>(() => moved.At(sourcePath));
        var copied = (CsMethod)document.Types[0].Members[0].Detach();
        Assert.Equal(document.Types[0].Members[0], copied);
    }
    [Fact]
    public void Scoped_reformat_preserves_multiline_literal_value_and_outside_bytes()
    {
        const string source = "class C { string A() { return @\"first\nsecond\"; } int B(){return 2;} }";
        var document = Cs.Parse(source).Root;
        var path = document.Descendants().Single(x => x.Node is CsMethod { Name: "A" }).Path;
        var formatted = document.Reformat(path);
        Assert.Contains("@\"first\nsecond\"", formatted.Render());
        Assert.EndsWith(" int B(){return 2;} }", formatted.Render(), StringComparison.Ordinal);
        Assert.Equal(formatted.Render(), formatted.Reformat(new NodePath(path.Segments)).Render());
    }
}
