using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class CrossIndentMoveTests
{
    [Fact]
    public void Moving_to_two_space_container_preserves_unmoved_siblings_exactly()
    {
        const string source = "class A\n{\n\t// moving\n\tint M() { return 1; }\n}\nclass B\n{\n  int Kept()  { return 2; } // unchanged\n}\n";
        var document = Cs.Parse(source).Root;
        var moving = document.Descendants().Single(x => x.Node is CsMethod { Name: "M" });
        var destination = document.Descendants().Single(x => x.Node is CsTypeDeclaration { Name: "B" });
        var edited = document.Move(moving.Path, destination.Path, destination.Node.Children.Count);
        string output = edited.Render();
        Assert.Contains("  int Kept()  { return 2; } // unchanged\n", output);
        Assert.Contains("  // moving\n  int M() { return 1; }", output);
        Assert.Equal(1, output.Split("// moving", StringSplitOptions.None).Length - 1);
        Assert.Equal(source, document.Render());
    }
}
