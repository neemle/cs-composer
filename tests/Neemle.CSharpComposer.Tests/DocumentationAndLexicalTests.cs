using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class DocumentationAndLexicalTests
{
    [Fact]
    public void Documentation_xml_crefs_and_text_are_traversable_and_editable()
    {
        const string source = "/// <summary>Use <see cref=\"System.Collections.Generic.List{T}.Add(T)\" /> &amp; text.</summary>\n" +
            "/// <remarks><![CDATA[a < b]]><!-- note --></remarks>\nclass C { }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsCref);
        var selected = parsed.Root.Descendants().Single(n => n.Node is CsXmlNode { Kind: XmlNodeKind.Text, Value: "Use " });
        var changed = parsed.Root.Replace(selected.Path, (CsXmlNode)selected.Node with { Value = "Prefer " });
        Assert.Contains("Prefer ", changed.Render());
        Assert.Equal(source, parsed.Root.Render());
        Assert.Empty(Cs.Parse(changed.Reformat().Render()).Root.Validate());
    }

    [Theory]
    [InlineData("operator +(C, C)")]
    [InlineData("explicit operator checked int(C)")]
    [InlineData("C.this[int]")]
    [InlineData("extension{T}(T).Member(int)")]
    public void Every_cref_family_has_a_structured_representation(string cref)
    {
        var parsed = Cs.Parse("/// <see cref=\"" + cref + "\" />\nclass C { }");
        Assert.Empty(parsed.Diagnostics);
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsCref);
        Assert.Empty(parsed.Root.Validate());
        Assert.Empty(Cs.Parse(parsed.Root.Reformat().Render()).Root.Validate());
    }

    [Theory]
    [InlineData("class C { int \\u0061 = 1; double M() => .5; }")]
    [InlineData("class 𐐀 { int 𐐁 = 1; }")]
    [InlineData("unsafe struct C { public fixed int data[32], more[16]; }")]
    [InlineData("class C { static void M(int x, __arglist) { } }")]
    [InlineData("class C { public int Value { get => field; set { field = value; } } }")]
    [InlineData("class C<T> where T : allows ref struct { }")]
    [InlineData("class C { string M() => nameof(System.Collections.Generic.List<>); }")]
    [InlineData("class C { object M() => (int First, int Second) (int x) => (x,x); }")]
    public void Remaining_elementary_forms_are_typed(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsRaw or CsRawMember);
        Assert.Empty(Cs.Parse(parsed.Root.Reformat().Render()).Diagnostics);
    }

    [Fact]
    public void Disabled_source_is_preserved_as_conditional_text_even_when_it_is_not_valid_code()
    {
        const string source = "#if false\nthis is not valid C# \"unterminated\n#else\nclass C {}\n#endif\n";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.Contains(parsed.Root.Items, n => n is CsDisabledText);
        Assert.DoesNotContain(parsed.Root.Items, n => n is CsRaw);
        Assert.Equal(source, parsed.Root.Render());
        Assert.Empty(Cs.Parse(parsed.Root.Reformat().Render()).Diagnostics);
    }
}
