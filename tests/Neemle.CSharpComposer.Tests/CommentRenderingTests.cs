using Neemle.CSharpComposer.Model;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class CommentRenderingTests
{
    [Fact]
    public void Rerender_preserves_document_type_body_statement_and_trailing_comments_once()
    {
        const string source = "// document\n\nclass C\n{\n    // container\n\n    void A()\n    {\n        // body\n\n        // statement\n        Run(); // trailing\n    }\n}\n\n// EOF\n";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        var formatted = parsed.Root.Reformat().Render();
        foreach (string marker in new[] { "// document", "// container", "// body", "// statement", "// trailing", "// EOF" })
            Assert.Equal(1, formatted.Split(marker, StringSplitOptions.None).Length - 1);
        Assert.True(formatted.IndexOf("// document", StringComparison.Ordinal) < formatted.IndexOf("class C", StringComparison.Ordinal));
        Assert.True(formatted.IndexOf("// body", StringComparison.Ordinal) > formatted.IndexOf("void A", StringComparison.Ordinal));
        Assert.Contains("Run(); // trailing", formatted);
        Assert.EndsWith("// EOF\n", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Container_comments_stay_owned_when_members_are_reordered()
    {
        var document = Cs.Parse("class C\n{\n // owner\n\n int A;\n int B;\n}\n").Root;
        var type = document.Types[0];
        var changed = document with { Types = new[] { type with { Members = new[] { type.Members[1], type.Members[0] } } } };
        Assert.Equal(1, changed.Render().Split("// owner", StringSplitOptions.None).Length - 1);
        Assert.True(changed.Render().IndexOf("int B", StringComparison.Ordinal) < changed.Render().IndexOf("int A", StringComparison.Ordinal));
    }

    [Fact]
    public void Raw_member_and_body_rerender_preserve_multiline_literal_lexemes()
    {
        const string literal = "@\"a\r\nb\"";
        var method = new CsMethod(Cs.Type<string>(), "M") { Body = Cs.RawBody("return " + literal + ";") };
        var document = Cs.Document().Class("C").Add(method).Add(Cs.RawMember("string Value = " + literal + ";")).End().Build();
        string output = document.Reformat().Render();
        Assert.Equal(2, output.Split(literal, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Formatting_diagnostics_are_limited_to_the_selected_subtree()
    {
        var document = Cs.Parse("class C { int A() { return 1; } int B() { for(;;){} } }").Root;
        var a = document.Descendants().Single(x => x.Node is CsMethod { Name: "A" });
        Assert.Empty(document.Reformat(a.Path).EmissionDiagnostics);
        var b = document.Descendants().Single(x => x.Node is CsMethod { Name: "B" });
        Assert.All(document.Reformat(b.Path).EmissionDiagnostics, diagnostic => Assert.Equal("FORMAT001", diagnostic.Code));
    }
}
