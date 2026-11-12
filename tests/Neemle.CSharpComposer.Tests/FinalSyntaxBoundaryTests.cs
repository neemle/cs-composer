using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class FinalSyntaxBoundaryTests
{
    [Fact]
    public async Task Throw_expressions_work_in_member_bodies_coalescing_and_conditional_branches()
    {
        const string source = "using System; class C { public C() => throw new Exception(); public int Value => throw new Exception(); " +
            "public int M() => throw new Exception(); } class Program { static string Pick(string s) => s ?? throw new Exception(); " +
            "static int Choose(bool ready) => ready ? 1 : throw new Exception(); static void Main() { if(Pick(\"ok\")!=\"ok\" || Choose(true)!=1) throw new Exception(); } }";
        var parsed = Cs.Parse(source, new() { LanguageVersion = CsLanguageVersion.CSharp7_3 });
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        await CompilerFixture.Run(parsed.Root.Reformat().Render(), "7.3");
    }

    [Fact]
    public void Null_scalar_names_and_collections_return_diagnostics()
    {
        Assert.NotEmpty((new CsDocument { Items = null! }).Validate());
        Assert.NotEmpty((new CsDocument { Items = new CsNode[] { new CsNamespace("N", null!) } }).Validate());
        Assert.NotEmpty((new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, null!) } }).Validate());
        var field = new CsField(new CsType("not a type").Nullable(), "field");
        Assert.NotEmpty((new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new[] { field } } } }).Validate());
    }
}
