using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;

[Trait("Category", "Unit")]
public sealed class ListGapTests
{
    static CsDocument Parse(string source) => Cs.Parse(source).Root;

    static CsDocument WithMembers(CsDocument document, Func<CsTypeDeclaration, CsTypeDeclaration> change) =>
        document with { Types = new[] { change(document.Types[0]) } };

    [Fact]
    public void New_container_comment_is_placed_after_the_opening_line()
    {
        var document = Parse("class C\n{\n    int A;\n    int B;\n}\n");
        var added = new CsComment("// added", CommentKind.Line, CommentPlacement.Container);
        var edited = WithMembers(document, type => type with
        {
            Comments = new(type.Comments.Append(added)),
            Members = new[] { type.Members[1] }
        });
        Assert.Equal("class C\n{\n    // added\n    int B;\n}\n", edited.Render());
    }

    [Fact]
    public void Comment_left_by_a_removed_member_moves_to_its_place_once()
    {
        var document = Parse("// lead\nclass A\n{\n    int M;\n\n    // one\n\n    int K;\n\n    // two\n\n" +
            "    int Z;\n}\n");
        var edited = WithMembers(document, type => type with { Members = new[] { type.Members[0], type.Members[2] } });
        Assert.Equal("// lead\nclass A\n{\n    int M;\n    // one\n\n    // two\n\n    int Z;\n}\n", edited.Render());
    }

    [Theory]
    [InlineData("// c\n", "// c\nclass B\n{\n}\n")]
    [InlineData("namespace N;\n", "namespace N;\nclass B\n{\n}\n")]
    [InlineData("namespace N\n{\n}\n", "namespace N\n{\n    class B\n    {\n    }\n}\n")]
    public void Type_inserted_into_an_empty_document_or_namespace_takes_its_level(string source, string expected)
    {
        var document = Parse(source);
        var target = document.Descendants().Last(x => x.Node is CsDocument or CsNamespace);
        Assert.Equal(expected, document.Insert(target.Path, target.Node.Children.Count,
            new CsTypeDeclaration(TypeKind.Class, "B")).Render());
    }

    [Fact]
    public void Type_inserted_before_the_first_document_item_starts_the_document()
    {
        var document = Parse("class A\n{\n}\n");
        Assert.Equal("class B\n{\n}\nclass A\n{\n}\n",
            document.Insert(new NodePath(Values<int>.Empty), 0, new CsTypeDeclaration(TypeKind.Class, "B")).Render());
    }

    [Theory]
    [InlineData("class A {}\n", "class A {\n    int F() => 9;\n}\n")]
    [InlineData("class A { int M() => 1; }\n", "class A { int M() => 1;\n    int F() => 9;\n }\n")]
    public void Insert_into_a_single_line_container_starts_a_new_line(string source, string expected)
    {
        var document = Parse(source);
        var a = document.Descendants().Single(x => x.Node is CsTypeDeclaration);
        Assert.Equal(expected, document.Insert(a.Path, a.Node.Children.Count, Cs.RawMember("int F() => 9;")).Render());
    }

    [Fact]
    public void Edited_container_comment_is_rerendered_in_place()
    {
        var document = Parse("class C\n{\n    // old\n\n    int A;\n    int B;\n}\n");
        var edited = WithMembers(document, type => type with
        {
            Comments = new(type.Comments.Select(c => c with { Text = "// new" })),
            Members = new[] { type.Members[1] }
        });
        Assert.Equal("class C\n{\n    // new\n\n    int B;\n}\n", edited.Render());
    }

    [Theory]
    [InlineData("class C\n{\n    // gone   \n    int A;\n    int B;\n}\n", "class C\n{\n    int B;\n}\n")]
    [InlineData("class C\r\n{\r\n    // gone\r\n    int A;\r\n    int B;\r\n}\r\n",
        "class C\r\n{\r\n    int B;\r\n}\r\n")]
    [InlineData("class C\n{ // gone\n    int A;\n    int B;\n}\n", "class C\n{\n    int B;\n}\n")]
    [InlineData("class C\n{\n    int A;\n    int B;\n    /* gone */ }\n", "class C\n{\n    int B;\n}\n")]
    [InlineData("class C\n{\n    /* gone */ /* kept */\n    int A;\n    int B;\n}\n",
        "class C\n{\n    /* kept */\n    int B;\n}\n")]
    public void Removed_container_comment_is_cut_with_its_line_when_alone_on_it(string source, string expected)
    {
        var document = Parse(source);
        var edited = WithMembers(document, type => type with
        {
            Comments = new(type.Comments.Where(c => !c.Text.Contains("gone", StringComparison.Ordinal))),
            Members = new[] { type.Members[1] }
        });
        Assert.Equal(expected, edited.Render());
    }
}
