using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Xunit;
namespace Neemle.CSharpComposer.Tests;

[Trait("Category", "Unit")]
public sealed class IndentPreservationTests
{
    static CsDocument Parse(string source) => Cs.Parse(source).Root;

    static NodePath PathOf(CsDocument document, string name) => IndentPreservationMatrixTests.Find(document, name).Path;

    static CsNode NodeOf(CsDocument document, string name) => IndentPreservationMatrixTests.Find(document, name).Node;

    [Fact]
    public void Ac1_tab_remove_keeps_container_closing_line()
    {
        var document = Parse("namespace N\n{\n\tclass A\n\t{\n\t\tint M() => 1;\n\t\tint K() => 2;\n\t}\n}\n");
        Assert.Equal("namespace N\n{\n\tclass A\n\t{\n\t\tint K() => 2;\n\t}\n}\n",
            document.Remove(PathOf(document, "M")).Render());
    }

    [Fact]
    public void Ac2_two_space_move_keeps_both_closing_lines_and_moves_comment_once()
    {
        var document = Parse("namespace N\n{\n  class A\n  {\n    int M() => 1;\n    int K() => 2;\n  }\n" +
            "  class B\n  {\n    int Z() => 3;\n  }\n}\n");
        var b = IndentPreservationMatrixTests.Find(document, "B");
        Assert.Equal("namespace N\n{\n  class A\n  {\n    int K() => 2;\n  }\n  class B\n  {\n    int Z() => 3;\n" +
            "    int M() => 1;\n  }\n}\n",
            document.Move(PathOf(document, "M"), b.Path, b.Node.Children.Count).Render());
        var commented = Parse("class A\n{\n  // owned\n  int M() => 1;\n  int K() => 2;\n}\n" +
            "class B\n{\n  int Z() => 3;\n}\n");
        var target = IndentPreservationMatrixTests.Find(commented, "B");
        string moved = commented.Move(PathOf(commented, "M"), target.Path, target.Node.Children.Count).Render();
        Assert.Equal("class A\n{\n  int K() => 2;\n}\nclass B\n{\n  int Z() => 3;\n  // owned\n  int M() => 1;\n}\n",
            moved);
    }

    [Fact]
    public void Ac3_three_space_scoped_reformat_uses_source_base_and_is_idempotent()
    {
        var document = Parse("class A\n{\n   int R(){return 1;}\n   int K() => 2;\n}\n");
        var formatted = document.Reformat(PathOf(document, "R"));
        const string expected = "class A\n{\n   int R()\n   {\n       return 1;\n   }\n   int K() => 2;\n}\n";
        Assert.Equal(expected, formatted.Render());
        Assert.Equal(expected, formatted.Reformat(PathOf(formatted, "R")).Render());
        var reparsed = Parse(expected);
        Assert.Equal(expected, reparsed.Reformat(PathOf(reparsed, "R")).Render());
    }

    [Theory]
    [InlineData("class A\n  {\n    int M() => 1;\n    int K() => 2;\n  }\n", "class A\n  {\n    int K() => 2;\n  }\n")]
    [InlineData("namespace N {\n\tclass A {\n\t\tint M() => 1;\n\t\tint K() => 2;\n\t}\n}\n",
        "namespace N {\n\tclass A {\n\t\tint K() => 2;\n\t}\n}\n")]
    [InlineData("namespace N\n{\n\tclass A\n\t{\n\t\tint M() => 1;\n\t}\n}\n",
        "namespace N\n{\n\tclass A\n\t{\n\t}\n}\n")]
    public void Ac4_remove_keeps_closing_line_whitespace_for_gnu_kr_and_last_member(string source, string expected)
    {
        var document = Parse(source);
        Assert.Equal(expected, document.Remove(PathOf(document, "M")).Render());
    }

    [Theory]
    [InlineData("class A\n  {\n    int M() => 1;\n  }\n", "class A\n  {\n    int M() => 1;\n    int F() => 9;\n  }\n")]
    [InlineData("namespace N {\n\tclass A {\n\t\tint M() => 1;\n\t}\n}\n",
        "namespace N {\n\tclass A {\n\t\tint M() => 1;\n\t\tint F() => 9;\n\t}\n}\n")]
    public void Ac4_insert_keeps_closing_line_whitespace_for_gnu_and_kr(string source, string expected)
    {
        var document = Parse(source);
        var a = IndentPreservationMatrixTests.Find(document, "A");
        Assert.Equal(expected, document.Insert(a.Path, a.Node.Children.Count, Cs.RawMember("int F() => 9;")).Render());
    }

    [Theory]
    [InlineData("namespace N\n{\n\tclass A\n\t{\n\t}\n}\n",
        "namespace N\n{\n\tclass A\n\t{\n\t\tint F() => 9;\n\t}\n}\n")]
    [InlineData("namespace N\n{\n  class A\n  {\n  }\n}\n",
        "namespace N\n{\n  class A\n  {\n    int F() => 9;\n  }\n}\n")]
    [InlineData("class A\n{\n}\nclass B\n{\n  int M() => 1;\n}\n",
        "class A\n{\n  int F() => 9;\n}\nclass B\n{\n  int M() => 1;\n}\n")]
    [InlineData("class A\n{\n}\n", "class A\n{\n    int F() => 9;\n}\n")]
    [InlineData("namespace N\n{\n\tclass B\n\t{\n\t  int M() => 1;\n\t}\n\tclass A\n\t{\n\t}\n}\n",
        "namespace N\n{\n\tclass B\n\t{\n\t  int M() => 1;\n\t}\n\tclass A\n\t{\n\t\tint F() => 9;\n\t}\n}\n")]
    public void Ac5_insert_into_empty_container_uses_container_indent_plus_inferred_unit(string source, string expected)
    {
        var document = Parse(source);
        Assert.Equal(expected, document.Insert(PathOf(document, "A"), 0, Cs.RawMember("int F() => 9;")).Render());
    }

    [Fact]
    public void Ac5_insert_takes_previous_sibling_indent_before_next_sibling()
    {
        var document = Parse("class A\n{\n   int M() => 1;\n     int K() => 2;\n}\n");
        var a = IndentPreservationMatrixTests.Find(document, "A");
        Assert.Equal("class A\n{\n   int M() => 1;\n   int F() => 9;\n     int K() => 2;\n}\n",
            document.Insert(a.Path, 1, Cs.RawMember("int F() => 9;")).Render());
        Assert.Equal("class A\n{\n   int F() => 9;\n   int M() => 1;\n     int K() => 2;\n}\n",
            document.Insert(a.Path, 0, Cs.RawMember("int F() => 9;")).Render());
    }

    [Fact]
    public void Ac6_multiline_move_replaces_only_the_base_indent()
    {
        const string method = "{0}// note\n{0}string M()\n{0}{{\n{0}\tvar v = @\"a\n  b\";\n{0}\t/* c\n   d */\n" +
            "{0}\treturn \"s\" + v;\n{0}}}\n";
        var document = Parse("class A\n{\n" + string.Format(method, "\t") + "\tint K() => 2;\n}\n" +
            "class B\n{\n   int Z() => 3;\n}\n");
        var b = IndentPreservationMatrixTests.Find(document, "B");
        string output = document.Move(PathOf(document, "M"), b.Path, b.Node.Children.Count).Render();
        Assert.Equal("class A\n{\n\tint K() => 2;\n}\nclass B\n{\n   int Z() => 3;\n" +
            string.Format(method, "   ") + "}\n", output);
        Assert.Single(output.Split("// note", StringSplitOptions.None).Skip(1));
    }

    [Fact]
    public void Ac7_unbound_rerender_in_tab_source_keeps_tab_layout()
    {
        const string tail = "\tint K() => 2;\n}\n";
        var document = Parse("class A\n{\n\tint M()\n\t{\n\t\treturn 1;\n\t}\n" + tail);
        var method = (CsMethod)NodeOf(document, "M");
        Assert.Equal("class A\n{\n\tpublic int M()\n\t{\n\t\treturn 1;\n\t}\n" + tail,
            document.Replace(PathOf(document, "M"), method with { Modifiers = new[] { "public" } }).Render());
    }

    [Fact]
    public void Ac7_reformat_with_tab_option_keeps_eight_space_base()
    {
        var document = Parse("class A\n{\n        int R(){return 1;}\n}\n");
        Assert.Equal("class A\n{\n        int R()\n        {\n        \treturn 1;\n        }\n}\n",
            document.Reformat(PathOf(document, "R"), new RenderOptions { Indent = "\t" }).Render());
    }

    [Fact]
    public void Ac7_scoped_reformat_of_type_and_statement_keeps_source_base()
    {
        var document = Parse("namespace N\n{\n\tclass C\n\t{\n\t\tint P(){x();return 1;}\n\t}\n}\n");
        Assert.Equal("namespace N\n{\n\tclass C\n\t{\n\t    int P()\n\t    {\n\t        x();\n\t        return 1;\n" +
            "\t    }\n\t}\n}\n", document.Reformat(PathOf(document, "C")).Render());
        var body = Parse("class C\n{\n\tvoid P()\n\t{\n\t\tif(true){x();}\n\t\treturn;\n\t}\n}\n");
        var statement = body.Descendants().First(x => x.Node is CsStatement { Kind: StatementKind.If }).Path;
        Assert.Equal("class C\n{\n\tvoid P()\n\t{\n\t\tif (true)\n\t\t{\n\t\t    x();\n\t\t}\n\t\treturn;\n\t}\n}\n",
            body.Reformat(statement).Render());
    }

    [Fact]
    public void Mid_line_rerender_does_not_repeat_the_line_indent()
    {
        var document = Parse("namespace N\n{\n    class C { int M() { return 1; } }\n}\n");
        var method = (CsMethod)NodeOf(document, "M");
        Assert.Equal("namespace N\n{\n    class C { public int M()\n    {\n        return 1;\n    } }\n}\n",
            document.Replace(PathOf(document, "M"), method with { Modifiers = new[] { "public" } }).Render());
    }

    [Theory]
    [InlineData("K", "class A\n{\n    int M() => 1;\n\n    // z\n\n    int Z() => 3;\n}\n")]
    [InlineData("M", "class A\n{\n    int K() => 2;\n\n    // z\n\n    int Z() => 3;\n}\n")]
    [InlineData("Z", "class A\n{\n    int M() => 1;\n\n    int K() => 2;\n    // z\n}\n")]
    public void Remove_keeps_blank_lines_and_container_comments_once(string name, string expected)
    {
        var document = Parse("class A\n{\n    int M() => 1;\n\n    int K() => 2;\n\n    // z\n\n" +
            "    int Z() => 3;\n}\n");
        Assert.Equal(expected, document.Remove(PathOf(document, name)).Render());
    }

    [Fact]
    public void Insert_and_replace_keep_blank_lines_between_retained_members()
    {
        var document = Parse("using System;\n\nclass A\n{\n}\n\nclass B\n{\n}\n");
        Assert.Equal("using System;\n\nclass B\n{\n}\n", document.Remove(PathOf(document, "A")).Render());
        var type = Parse("class A\n{\n    int M() => 1;\n\n    int K() => 2;\n}\n");
        Assert.Equal("class A\n{\n    int M() => 1;\n\n    int F() => 9;\n}\n",
            type.Replace(PathOf(type, "K"), Cs.RawMember("int F() => 9;")).Render());
        var a = IndentPreservationMatrixTests.Find(type, "A");
        Assert.Equal("class A\n{\n    int M() => 1;\n    int F() => 9;\n\n    int K() => 2;\n}\n",
            type.Insert(a.Path, 1, Cs.RawMember("int F() => 9;")).Render());
    }

    [Fact]
    public void File_scoped_namespace_and_single_line_container_keep_their_layout()
    {
        var scoped = Parse("namespace N;\n\nclass A\n{\n}\n\nclass B\n{\n}\n");
        Assert.Equal("namespace N;\n\nclass B\n{\n}\n", scoped.Remove(PathOf(scoped, "A")).Render());
        var single = Parse("class C { int M() => 1; int K() => 2; }\n");
        Assert.Equal("class C { int K() => 2; }\n", single.Remove(PathOf(single, "M")).Render());
    }

    [Fact]
    public void Removed_container_comment_is_not_restored_from_source()
    {
        var document = Parse("class C\n{\n    // gone\n\n    int A;\n    int B;\n}\n");
        var type = document.Types[0];
        var changed = type with { Comments = Values<CsComment>.Empty, Members = new[] { type.Members[1] } };
        var edited = document with { Types = new[] { changed } };
        Assert.Equal("class C\n{\n\n    int B;\n}\n", edited.Render());
    }
}
