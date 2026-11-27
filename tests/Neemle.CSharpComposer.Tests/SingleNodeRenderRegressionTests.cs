using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-24 pass 2: edits and per-node reformats that used to emit invalid C#, drop part of a node or lose the
// edit. Each test renders the parsed and edited document and compares the whole output text.
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

    // A parsed attribute's span is its type and arguments; the list brackets and the target stay in the source.
    [Theory]
    [InlineData("[Obsolete(\"x\")]\nclass C { }\n", "Obsolete")]
    [InlineData("[return: A, B(1, Name = 2)]\nclass C { }\n", "A")]
    [InlineData("[return: A, B(1, Name = 2)]\nclass C { }\n", "B")]
    [InlineData("class C\n{\n    void M([In] int a) { }\n}\n", "In")]
    public void Reformat_of_one_attribute_writes_it_once_inside_the_existing_list(string source, string name)
    {
        var document = Parse(source);
        string rendered = document.Reformat(PathOf<CsAttribute>(document, a => a.Type.Name == name)).Render();
        Assert.Equal(source, rendered);
        Assert.Empty(Cs.Parse(rendered).Diagnostics);
    }

    [Fact]
    public void A_changed_attribute_is_written_in_its_own_span()
    {
        var document = Parse("[return: A, B(1)]\nclass C { }\n");
        var path = PathOf<CsAttribute>(document, a => a.Type.Name == "B");
        var edited = document.Replace(path, ((CsAttribute)document.At(path).Reformat()) with { Type = Cs.Type("D") });
        Assert.Equal("[return: A, D(1)]\nclass C { }\n", edited.Render());
    }

    [Theory]
    [InlineData("catch (System.Exception e) when (e != null)\n{\n    throw;\n}")]
    [InlineData("catch (Exception)\n{\n    throw;\n}")]
    [InlineData("catch\n{\n    throw;\n}")]
    public void Reformat_of_one_catch_keeps_its_type_variable_and_filter(string clause)
    {
        string source = "class C\n{\n    void M()\n    {\n        try { }\n        " + clause.Replace("{\n    throw;\n}", "{ throw; }") + "\n    }\n}\n";
        var document = Parse(source);
        string rendered = document.Reformat(PathOf<CsCatch>(document)).Render();
        string expected = "class C\n{\n    void M()\n    {\n        try { }\n        " + clause.Replace("\n", "\n        ") + "\n    }\n}\n";
        Assert.Equal(expected, rendered);
        var reparsed = Cs.Parse(rendered);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(document, reparsed.Root);
    }

    [Fact]
    public void An_inserted_cref_parameter_reaches_the_rendered_comment()
    {
        var document = Parse("/// <see cref=\"M(int)\"/>\nclass C { void M(int a, int b) { } }");
        string rendered = document.Insert(PathOf<CsCref>(document), 1, new CsFunctionPointerParameter(Cs.Int)).Render();
        // The comment is re-rendered; the declaration after it keeps its bytes.
        Assert.Equal("/// <see cref=\"M(int, int)\" />\nclass C { void M(int a, int b) { } }", rendered);
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
        Assert.Equal(source.Replace("old", "new"), document.Replace(path, comment with { Text = comment.Text.Replace("old", "new") }).Render());
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
        Assert.Equal(source.Replace("old", "new"), rendered);
    }

    [Fact]
    public void An_edited_block_documentation_comment_keeps_its_form()
    {
        const string source = "class C\n{\n    /** <summary>old</summary>\n     * x */\n    void M() { }\n}\n";
        var document = Parse(source);
        var path = PathOf<CsXmlNode>(document, x => x.Kind == XmlNodeKind.Text && x.Value!.Contains("old"));
        var text = (CsXmlNode)document.At(path);
        Assert.Equal(source.Replace("old", "new"), document.Replace(path, text with { Value = "new" }).Render());
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
        Assert.Equal(expected, rendered);
        Assert.Equal(ParseCoverage.Typed, Cs.Parse(rendered).Coverage);
    }

    [Fact]
    public void Editing_inside_a_top_level_embedded_statement_renders_it_whole()
    {
        var document = Parse("if (a) F(1);\nG(2);\n");
        var path = document.Descendants().First(n => n.Node is CsExpression { Text: "1" }).Path;
        string rendered = document.Replace(path, ((CsExpression)document.At(path)) with { Text = "7" }).Render();
        Assert.Equal("if (a)\n{\n    F(7);\n}\nG(2);\n", rendered);
    }

    [Theory]
    [InlineData("var x   =   F( 1 );\n", "var x = F(1);\n")]
    [InlineData("F( 1 ) ;\n", "F(1);\n")]
    public void Reformat_of_a_top_level_statement_keeps_its_terminator(string source, string expected)
    {
        var document = Parse(source);
        var path = document.Descendants().First(n => n.Node is CsGlobalStatement).Path;
        var statement = (CsGlobalStatement)document.At(path);
        Assert.Equal(expected, document.Replace(path, statement with { Value = statement.Value.Reformat() }).Render());
    }
}
