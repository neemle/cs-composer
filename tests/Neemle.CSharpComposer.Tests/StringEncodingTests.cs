using System.Text;
using Neemle.CSharpComposer.Parsing;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class StringEncodingTests
{
    [Fact]
    public void Already_decoded_string_uses_requested_encoding_without_garbling()
    {
        const string source = "class Café { string Value = \"привіт\"; }";
        var parsed = Cs.Parse(source, new ParseOptions { Encoding = Encoding.Unicode });
        Assert.Equal(source, parsed.Root.Render());
        Assert.Equal(source, Encoding.Unicode.GetString(parsed.Root.ToBytes()));
        Assert.Equal("Café", parsed.Root.Types[0].Name);
        Assert.Throws<EncoderFallbackException>(() => Cs.Parse("\ud800", new() { Encoding = Encoding.Unicode }));
    }
}
