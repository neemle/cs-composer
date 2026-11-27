using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Xunit;
namespace Neemle.CSharpComposer.Tests;

[Trait("Category", "Unit")]
public sealed class GoldenRenderTests
{
    const string Parsed = "// document\n\nusing System;\n\nnamespace N\n{\n    // container\n\n    class A\n    {\n" +
        "        int  M( ){return 1;}\n\n        /// <summary>K</summary>\n        int K() => 2; // trailing\n" +
        "        class C { void P() { if (true) { x(); } } }\n    }\n}\n";

    // GENE-27 R3: a container comment is followed by a blank line, so that it reads back as the container's own
    // instead of as the leading comment of the item after it. GENE-29 F2: one blank line follows the using block.
    const string Reformatted = "// document\n\nusing System;\n\nnamespace N\n{\n    // container\n\n    class A\n    {\n" +
        "        int M()\n        {\n            return 1;\n        }\n\n        /// <summary>K</summary>\n" +
        "        int K() => 2; // trailing\n\n        class C\n        {\n            void P()\n            {\n" +
        "                if (true)\n                {\n                    x();\n                }\n            }\n" +
        "        }\n    }\n}\n";

    static CsDocument Sample() => Cs.Document().Namespace("Example")
        .Class("User").Public().Partial()
        .Property<int>("Id").Public().Get().Init().End()
        .Property<string>("Name").Public().Required().Get().Init().End()
        .End();

    [Fact]
    public void New_documents_render_as_at_4e547ef()
    {
        const string fileScoped = "namespace Example;\n\npublic partial class User\n{\n" +
            "    public int Id { get; init; }\n\n" +
            "    public required string Name { get; init; }\n}\n";
        Assert.Equal(fileScoped, Sample().Render());
        Assert.Equal(fileScoped, Sample().Reformat(new RenderOptions { Indent = "  " }).Render());
        Assert.Equal("namespace Example\n{\n\tpublic partial class User\n\t{\n\t\tpublic int Id { get; init; }\n\n" +
            "\t\tpublic required string Name { get; init; }\n\t}\n}\n",
            Sample().Render(new RenderOptions { Indent = "\t", NamespaceStyle = NamespaceStyle.BlockScoped }));
    }

    [Fact]
    public void New_method_and_tree_render_as_at_4e547ef()
    {
        var body = Cs.Body(
            Cs.Var("result", Cs.Binary(CsOperator.Multiply, Cs.Id("input"), Cs.Literal(2))),
            Cs.If(Cs.Binary(CsOperator.GreaterThan, Cs.Id("result"), Cs.Literal(0)),
                Cs.Body(Cs.Return(Cs.Id("result"))), Cs.Block(Cs.Return(Cs.Literal(0)))));
        var document = Cs.Document().Class("C").Add(new CsMethod(Cs.Type<int>(), "Run") { Body = body }).End().Build();
        Assert.Equal("class C\n{\n    int Run()\n    {\n        var result = input * 2;\n        if (result > 0)\n" +
            "        {\n            return result;\n        }\n        else\n        {\n" +
            "            return 0;\n        }\n" +
            "    }\n}\n", document.Render());
        var tree = Cs.Tree(Cs.Symbol("WriteByte", new()
        {
            Target = Cs.Symbol("ms"),
            AsCall = true,
            Arguments = Cs.Compound(Cs.Value(42))
        }));
        Assert.Equal("ms.WriteByte(42);\n", tree.Render());
    }

    [Fact]
    public void Whole_document_reformat_of_lf_source_renders_as_at_4e547ef()
    {
        Assert.Equal(Reformatted, Cs.Parse(Parsed).Root.Reformat().Render());
        Assert.Equal(Reformatted, Cs.Parse(Parsed.Replace("    ", "\t")).Root.Reformat().Render());
        Assert.Equal(Reformatted.Replace("    ", "\t"),
            Cs.Parse(Parsed).Root.Reformat(new RenderOptions { Indent = "\t" }).Render());
    }
}
