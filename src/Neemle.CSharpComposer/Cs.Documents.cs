using Neemle.CSharpComposer.Builders;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using System.Text;
namespace Neemle.CSharpComposer;
public static partial class Cs
{
    public static FileBuilder Document(CsLanguageVersion version = CsLanguageVersion.CSharp14) => new(version);

    public static ParseResult<CsDocument> Parse(byte[] source, ParseOptions? options = null) =>
        DocumentParser.Parse(SourceSnapshot.Decode(source, options?.Encoding), options ?? new());

    public static ParseResult<CsDocument> Parse(string source, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var encoding = (Encoding)(options?.Encoding ?? new UTF8Encoding(false, true)).Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        return Parse(encoding.GetBytes(source), options);
    }

    public static ParseResult<CsDocument> Parse(Stream source, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(source));
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        return Parse(bytes.ToArray(), options);
    }
}
