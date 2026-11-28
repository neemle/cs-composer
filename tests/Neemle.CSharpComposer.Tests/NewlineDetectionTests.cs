using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 F4 (AD 84939297 v2 C3/C4): every render formats the whole document, so a parsed document's line breaks are
// not kept one by one. RenderOptions.Newline null writes the source's dominant newline (CRLF when its layout holds more
// CRLF than LF line breaks, else LF; lone CR counts as LF), a built document LF; line breaks inside literals, comments and
// inactive text are never converted. The render is a fixed point either way.
[Trait("Category", "Unit")]
public sealed class NewlineDetectionTests
{
    const string Formatted = "class C\n{\n    int A()\n    {\n        return 1;\n    }\n\n    int B() => 2;\n}\n";

    [Theory]
    [InlineData("\n", "\n", "\n")]
    [InlineData("\r\n", "\r\n", "\r\n")]
    [InlineData("\r", "\r", "\n")]
    [InlineData("\r\n", "\n", "\r\n")]
    [InlineData("\n", "\r\n", "\n")]
    public void A_parsed_document_renders_with_its_dominant_newline(string most, string one, string expected)
    {
        string source = "class C" + most + "{" + most + "    int  A( ){return 1;}" + one + "    int B() => 2;" + most + "}" + most;
        string output = Cs.Parse(source).Root.Render();
        Assert.Equal(Formatted.Replace("\n", expected, StringComparison.Ordinal), output);
        Assert.Equal(output, Cs.Parse(output).Root.Render());
    }

    [Fact]
    public void A_tie_renders_with_lf_and_a_built_document_with_lf_unless_the_options_say_otherwise()
    {
        Assert.Equal("class C\n{\n    int A;\n}\n", Cs.Parse("class C\n{\r\n    int A;\n}\r\n").Root.Render());
        var built = Cs.Document().Class("C").End().Build();
        Assert.DoesNotContain('\r', built.Render());
        Assert.Equal("class C\r\n{\r\n}\r\n", built.Render(new RenderOptions { Newline = "\r\n" }));
        Assert.Equal("class C\n{\n}\n", Cs.Parse("class C\r\n{\r\n}\r\n").Root.Render(new RenderOptions { Newline = "\n" }));
        Assert.Throws<ArgumentException>(() => built.Render(new RenderOptions { Newline = "\r" }));
    }

    [Fact]
    public void A_parsed_tree_renders_with_its_dominant_newline()
    {
        Assert.Equal("Run();\r\nStop();\r\n", Cs.ParseTree("Run();\r\nStop();\r\n").Root.Render());
        Assert.Equal("Run();\nStop();\n", Cs.ParseTree("Run();\nStop();\n").Root.Render());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Line_breaks_inside_literals_are_never_converted(string payload)
    {
        string source = "class C\r\n{\r\n    /* c" + payload + "d */\r\n    string P()\r\n    {\r\n" +
            "        var v = @\"a" + payload + "b\";\r\n" +
            "        var r = \"\"\"" + payload + "  e" + payload + "  \"\"\";\r\n" +
            "        return v + r;\r\n    }\r\n}\r\n";
        string output = Cs.Parse(source).Root.Render();
        // GENE-29 F5 (AD B4): a comment's line breaks are the formatter's, written with the document's newline, and its
        // later lines start at the comment's indentation.
        foreach (string kept in new[] { "/* c\r\n    d */", "@\"a" + payload + "b\"", "\"\"\"" + payload + "  e" + payload + "  \"\"\"" })
        {
            Assert.Contains(kept, output, StringComparison.Ordinal);
            output = output.Replace(kept, "", StringComparison.Ordinal);
        }
        Assert.DoesNotContain(output.Replace("\r\n", "", StringComparison.Ordinal), c => c is '\r' or '\n');
    }

    [Fact]
    public void Line_breaks_inside_literals_do_not_choose_the_newline()
    {
        const string source = "class C\r\n{\r\n    string V = @\"a\nb\nc\nd\ne\nf\";\r\n}\r\n";
        Assert.Equal(source, Cs.Parse(source).Root.Render());
    }

    [Fact]
    public void A_unicode_line_separator_in_a_literal_is_kept_and_never_written_as_layout()
    {
        const string source = "class C\r\n{\r\n    string V = @\"a\u2028b\";\r\n    int  A( ){return 1;}\r\n}\r\n";
        Assert.Equal("class C\r\n{\r\n    string V = @\"a\u2028b\";\r\n\r\n    int A()\r\n    {\r\n        return 1;\r\n    }\r\n}\r\n",
            Cs.Parse(source).Root.Render());
    }
}
