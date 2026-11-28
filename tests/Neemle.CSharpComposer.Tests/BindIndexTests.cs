using System.Diagnostics;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-19 RC-3 (AC-9): binding a node looks comments, line starts and diagnostic positions up in indexes
// built once per parse, so large flat files parse in time proportional to their size.
// GENE-29 F5: the large files run in a class of their own (LargeFlatFileTests), in parallel with this one.
[Trait("Category", "Unit")]
public sealed class BindIndexTests
{

    [Fact]
    public void Comments_between_members_keep_their_owner_and_placement()
    {
        string source = "class C\r\n{\r\n    // lead\r\n    int a; // trail\r\n\r\n    /* free */\r\n\r\n    int b;\r\n    void M() { /* inner */ }\r\n}\r\n";
        var parsed = Cs.Parse(source);
        var type = (CsTypeDeclaration)parsed.Root.Items[0];
        Assert.Equal(new[] { ("// lead", CommentPlacement.Leading), ("// trail", CommentPlacement.Trailing) },
            type.Members[0].Comments.Select(c => (c.Text, c.Placement)));
        // GENE-29 F5 (AD B2): a comment before a member is its leading comment, whatever blank lines lie between.
        Assert.Empty(type.Comments);
        Assert.Equal(new[] { ("/* free */", CommentPlacement.Leading) }, type.Members[1].Comments.Select(c => (c.Text, c.Placement)));
        Assert.Equal("/* inner */", Assert.Single(((CsMethod)type.Members[2]).Body!.Comments).Text);
        FormatCheck.FixedPoint(parsed.Root);
    }

    [Theory]
    [InlineData("class C {}\r\n\r\n@", 3, 1)]
    [InlineData("class C {}\n\r\n  @", 3, 3)]
    [InlineData("class C {}\r@", 2, 1)]
    [InlineData("@", 1, 1)]
    public void Diagnostic_line_and_column_count_each_line_break_sequence_once(string source, int line, int column)
    {
        var diagnostic = Cs.Parse(source).Diagnostics.First();
        Assert.Equal((line, column), (diagnostic.Line, diagnostic.Column));
    }
}

// GENE-19 RC-3 (AC-9) at size: 32 000 fields parse in linear time. GENE-29 F5: one checked render shows the result renders
// and reads back as the model (its fixed point is checked on the small files); the rows run in parallel with the rest.
[Trait("Category", "Unit")]
public sealed class LargeFlatFileTests
{
    static string Fields(int count, string prefix, string separator) =>
        "class C {" + separator + string.Join(separator, Enumerable.Range(0, count).Select(i => prefix + "int f" + i + ";")) + separator + "}";

    [Theory]
    [InlineData("", " ")]
    [InlineData("/* c */ ", " ")]
    [InlineData("", "\n")]
    [InlineData("// c\n", "\r\n")]
    public void Large_flat_files_parse_in_linear_time_with_unchanged_comment_ownership(string prefix, string separator)
    {
        // 32 000 fields took minutes before the indexes (quadratic token and line scans per bound node).
        string source = Fields(32_000, prefix, separator);
        var watch = Stopwatch.StartNew();
        var parsed = Cs.Parse(source);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), watch.Elapsed.ToString());
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Contains("int f31999;", parsed.Root.Render(), StringComparison.Ordinal);
        var fields = ((CsTypeDeclaration)parsed.Root.Items[0]).Members;
        Assert.Equal(32_000, fields.Count);
        // Every comment is owned exactly once (leading, or trailing the previous field on the same line).
        Assert.Equal(prefix.Length == 0 ? 0 : 32_000, parsed.Root.Descendants().Count(x => x.Node is CsComment));
    }
}
