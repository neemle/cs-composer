using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class CompilationSyntaxTests
{
    [Theory]
    [InlineData("extern alias Dependency; using Alias = Dependency::Namespace.Type; class C { }")]
    [InlineData("using Point = (int X, int Y); using unsafe Ptr = int*; class C { }")]
    [InlineData("using static System.Collections.Generic.EqualityComparer<int>; class C { }")]
    [InlineData("using System; var value = 1; Console.WriteLine(value); class C { }")]
    [InlineData("#define DEBUG\n#if DEBUG\nclass C { }\n#else\nclass D { }\n#endif\n")]
    [InlineData("#nullable enable annotations\n#pragma warning disable CS0169, 0219\nclass C {\n#region Values\nint x;\n#endregion\n}\n#pragma warning restore\n")]
    [InlineData("#line 42 \"generated.cs\"\nclass C { }\n#line default\n")]
    [InlineData("#line (1, 2)-(3, 4) 5 \"generated.cs\"\nclass C { }\n#line hidden\n")]
    public void Compilation_items_are_typed_and_roundtrip(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember);
        Assert.Equal(source, parsed.Root.Render());
        Assert.Empty(Cs.Parse(parsed.Root.Reformat().Render()).Diagnostics);
    }

    [Fact]
    public async Task Top_level_statements_aliases_and_directives_compile()
    {
        const string source = "#nullable enable\nusing System; using Point = (int X, int Y);\n#pragma warning disable CS0219\nPoint point = (2,3); if(point.X+point.Y!=5) { throw new Exception(); }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "14.0");
    }
}
