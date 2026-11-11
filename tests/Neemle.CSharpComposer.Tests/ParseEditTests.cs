using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class ParseEditTests
{
    [Fact]
    public void Typed_bodies_and_comments_roundtrip_then_rename_one_token()
    {
        const string input = "public class C\r\n{\r\n    /// Alpha\r\n    public int A(int x) { return x + 2; }\r\n    public int B() => 4;\r\n}";
        var result = Cs.Parse(Encoding.UTF8.GetBytes(input));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(input, Encoding.UTF8.GetString(result.Root.ToBytes()));
        var type = result.Root.Types[0];
        var method = Assert.IsType<CsMethod>(type.Members[0]);
        Assert.Single(method.Comments);
        Assert.Equal(ExpressionKind.Binary, method.Body!.Statements[0].Expression!.Kind);
        var renamed = result.Root with { Types = new[] { type with { Members = new CsMember[] {
            method with { Name = "Renamed" }, type.Members[1] } } } };
        Assert.Equal(input.Replace("int A(", "int Renamed(", StringComparison.Ordinal), renamed.Render());
        Assert.Equal(input, result.Root.Render());
    }

    [Fact]
    public void Explicit_raw_parse_is_typed_and_failure_preserves_original()
    {
        var raw = new CsRaw("await api.Run(x + 3)");
        Assert.IsType<CsExpression>(raw.Parse(new(ParseKind.Expression)).Root);
        var bad = new CsRaw("(");
        var failed = bad.Parse(new(ParseKind.Expression));
        Assert.Same(bad, failed.Root);
        Assert.True(failed.HasErrors);
    }

    [Fact(DisplayName = "[SEC][UC-003] Nested interpolation is bounded and escaped braces stay lexical")]
    public void Literal_boundaries_and_nested_inputs_are_safe()
    {
        var escaped = Cs.Parse("class C { string S = $\"{{ brace }}\"; int X; }");
        Assert.Equal(2, escaped.Root.Types[0].Members.Count);
        string hostile = string.Concat(Enumerable.Repeat("$\"{", 150)) + "0" +
            string.Concat(Enumerable.Repeat("}\"", 150));
        var result = Cs.Parse("class C { string S = " + hostile + "; }");
        Assert.True(result.HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bom_and_current_stream_position_are_preserved(bool bom)
    {
        var encoding = new UnicodeEncoding(false, bom, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("class C {}\r\n")).ToArray();
        using var input = new MemoryStream(new byte[] { 42 }.Concat(bytes).ToArray());
        input.ReadByte();
        var result = Cs.Parse(input, new() { Encoding = encoding });
        Assert.Equal(bytes, result.Root.ToBytes());
        Assert.True(input.CanRead);
        using var output = new MemoryStream();
        output.WriteByte(42);
        result.Root.WriteTo(output);
        Assert.Equal(new byte[] { 42 }.Concat(bytes).ToArray(), output.ToArray());
        Assert.True(output.CanWrite);
    }

    [Fact(DisplayName = "[SEC][UC-003] Input buffers and malformed encoding cannot alter a document")]
    public void Source_snapshot_and_strict_encoding_are_isolated()
    {
        var bytes = Encoding.UTF8.GetBytes("class C {}");
        var document = Cs.Parse(bytes).Root;
        bytes[0] = 0;
        Assert.Equal("class C {}", document.Render());
        Assert.Throws<DecoderFallbackException>(() => Cs.Parse(new byte[] { 0xff }));
    }
}
