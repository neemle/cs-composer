using System.Text;
using Neemle.CSharpComposer.IO;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class ExplicitBomTests
{
    [Theory]
    [InlineData(65001)] [InlineData(1200)] [InlineData(1201)] [InlineData(12000)] [InlineData(12001)]
    public void Explicit_bom_overrides_an_encoding_constructed_without_one(int codePage)
    {
        Encoding encoding = codePage switch { 1200 => new UnicodeEncoding(false, false, true),
            1201 => new UnicodeEncoding(true, false, true), 12000 => new UTF32Encoding(false, false, true),
            12001 => new UTF32Encoding(true, false, true), _ => new UTF8Encoding(false, true) };
        var document = Cs.Parse("class C {}").Root;
        var bytes = document.ToBytes(new EmitOptions { Encoding = encoding, EmitBom = true });
        Assert.True(bytes.Length > encoding.GetByteCount("class C {}"));
        Assert.Equal("class C {}", Cs.Parse(bytes).Root.Render());
        Assert.Equal(bytes, Cs.Parse(bytes).Root.ToBytes());
        Assert.Equal(encoding.GetBytes("class C {}"), document.ToBytes(new EmitOptions { Encoding = encoding, EmitBom = false }));
    }
    [Fact]
    public void Unsupported_bom_request_fails_clearly()
    {
        var error = Assert.Throws<ArgumentException>(() => Cs.Document().Build()
            .ToBytes(new EmitOptions { Encoding = Encoding.ASCII, EmitBom = true }));
        Assert.Contains("byte order mark", error.Message);
    }
}
