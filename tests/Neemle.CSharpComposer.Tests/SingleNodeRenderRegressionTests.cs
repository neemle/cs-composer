using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-24 pass 2: edits that used to emit invalid C#, drop part of a node or lose the edit. Each test renders the parsed
// and edited document and compares the whole output text with the render of the edited source (GENE-29 F4: every render
// formats; the per-node Reformat tests went with the preserving renderer).
[Trait("Category", "Unit")]
public sealed class SingleNodeRenderRegressionTests
{
    static CsDocument Parse(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        return parsed.Root;
    }

    static NodePath PathOf<T>(CsDocument document, Func<T, bool>? match = null) where T : CsNode =>
        document.Descendants().First(n => n.Node is T node && (match?.Invoke(node) ?? true)).Path;

    [Fact]
    public void A_changed_attribute_is_written_in_its_own_span()
    {
        var document = Parse("[return: A, B(1)]\nclass C { }\n");
        var path = PathOf<CsAttribute>(document, a => a.Type.Name == "B");
        var edited = document.Replace(path, ((CsAttribute)document.At(path)) with { Type = Cs.Type("D") });
        Assert.Equal(Formatted("[return: A, D(1)]\nclass C { }\n"), edited.Render());
    }

    [Fact]
    public void An_inserted_cref_parameter_reaches_the_rendered_comment()
    {
        var document = Parse("/// <see cref=\"M(int)\"/>\nclass C { void M(int a, int b) { } }");
        string rendered = document.Insert(PathOf<CsCref>(document), 1, new CsFunctionPointerParameter(Cs.Int)).Render();
        Assert.Equal(Formatted("/// <see cref=\"M(int, int)\" />\nclass C { void M(int a, int b) { } }"), rendered);
    }

    [Theory]
    [InlineData("class C\n{\n    // old\n    void M() { }\n}\n")]
    [InlineData("class C\n{\n    void M() { } // old\n}\n")]
    [InlineData("class C\n{\n    void M()\n    {\n        /* old */\n        F();\n    }\n}\n")]
    [InlineData("// old\nvar x = F(1);\n")]
    public void An_edited_comment_text_is_not_dropped(string source)
    {
        var document = Parse(source);
        var path = PathOf<CsComment>(document);
        var comment = (CsComment)document.At(path);
        Assert.Equal(Formatted(source.Replace("old", "new")), document.Replace(path, comment with { Text = comment.Text.Replace("old", "new") }).Render());
    }

    // A documentation comment re-rendered after an edit indents each `///` line and uses the source newline.
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void An_edited_documentation_comment_keeps_line_indentation_and_newlines(string newline)
    {
        string source = ("namespace N\n{\n    class C\n    {\n        /// <summary>\n        /// old\n        /// </summary>\n" +
            "        /// <param name=\"a\">x</param>\n        void M(int a) { }\n    }\n}\n").Replace("\n", newline);
        var document = Parse(source);
        var path = PathOf<CsXmlNode>(document, x => x.Kind == XmlNodeKind.Text && x.Value!.Contains("old"));
        var text = (CsXmlNode)document.At(path);
        string rendered = document.Replace(path, text with { Value = text.Value!.Replace("old", "new") }).Render();
        Assert.Equal(Formatted(source.Replace("old", "new")), rendered);
    }

    [Fact]
    public void An_edited_block_documentation_comment_keeps_its_form()
    {
        const string source = "class C\n{\n    /** <summary>old</summary>\n     * x */\n    void M() { }\n}\n";
        var document = Parse(source);
        var path = PathOf<CsXmlNode>(document, x => x.Kind == XmlNodeKind.Text && x.Value!.Contains("old"));
        var text = (CsXmlNode)document.At(path);
        Assert.Equal(Formatted(source.Replace("old", "new")), document.Replace(path, text with { Value = "new" }).Render());
    }

    // GENE-19 PR C residual: editing a call argument inside a top-level statement rendered the statement as a
    // scalar expression, which threw SYM001 for a declaration and dropped the `;` for an expression statement.
    [Theory]
    [InlineData("var x = a.B(1).C(2).D(3);\n", "1", "var x = a.B(7).C(2).D(3);\n")]
    [InlineData("var x = a.B(1).C(2).D(3);\n", "2", "var x = a.B(1).C(7).D(3);\n")]
    [InlineData("var x = a.B(1).C(2).D(3);\n", "3", "var x = a.B(1).C(2).D(7);\n")]
    [InlineData("var x = F(1);\n", "1", "var x = F(7);\n")]
    [InlineData("a.B(1).C(2).D(3);\n", "2", "a.B(1).C(7).D(3);\n")]
    [InlineData("F(1);\nG(2);\n", "2", "F(1);\nG(7);\n")]
    [InlineData("// c\nvar x = F(1); // t\n", "1", "// c\nvar x = F(7); // t\n")]
    [InlineData("for (int i = 0; i < 1; i++)\n{\n    F(1);\n}\n", "1", "for (int i = 0; i < 1; i++)\n{\n    F(7);\n}\n")]
    public void Editing_an_argument_in_a_top_level_statement_renders_the_statement(string source, string literal, string expected)
    {
        var document = Parse(source);
        var (path, node) = document.Descendants().Where(n => n.Node is CsExpression { Kind: ExpressionKind.Literal } e && e.Text == literal).Last();
        string rendered = document.Replace(path, ((CsExpression)node) with { Text = "7" }).Render();
        Assert.Equal(Formatted(expected), rendered);
        Assert.Equal(ParseCoverage.Typed, Cs.Parse(rendered).Coverage);
    }

    [Fact]
    public void Editing_inside_a_top_level_embedded_statement_renders_the_edited_statement()
    {
        var document = Parse("if (a) F(1);\nG(2);\n");
        var path = document.Descendants().First(n => n.Node is CsExpression { Text: "1" }).Path;
        string rendered = document.Replace(path, ((CsExpression)document.At(path)) with { Text = "7" }).Render();
        Assert.Equal(Formatted("if (a) F(7);\nG(2);\n"), rendered);
    }

    static string Formatted(string source) => Parse(source).Render();
}
