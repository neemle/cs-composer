using Neemle.CSharpComposer.Builders;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using System.Text;
namespace Neemle.CSharpComposer;
public static partial class Cs
{
    public static FileBuilder Document(CsLanguageVersion version = CsLanguageVersion.CSharp15) => new(version);

    public static ParseResult<CsDocument> Parse(byte[] source, ParseOptions? options = null) => Parse(source, options, CancellationToken.None);

    // Cancellation is cooperative: it is checked between items, members and statements, along chains
    // and token scans, and before each post-parse pass, and surfaces as OperationCanceledException.
    public static ParseResult<CsDocument> Parse(byte[] source, ParseOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        return DocumentParser.Parse(SourceSnapshot.Decode(source, options?.Encoding), options ?? new(), cancellationToken);
    }

    public static ParseResult<CsDocument> Parse(string source, ParseOptions? options = null) => Parse(source, options, CancellationToken.None);

    public static ParseResult<CsDocument> Parse(string source, ParseOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var encoding = (Encoding)(options?.Encoding ?? new UTF8Encoding(false, true)).Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        return Parse(encoding.GetBytes(source), options, cancellationToken);
    }

    public static ParseResult<CsDocument> Parse(Stream source, ParseOptions? options = null) => Parse(source, options, CancellationToken.None);

    // The caller's stream is never closed, including when parsing is cancelled.
    public static ParseResult<CsDocument> Parse(Stream source, ParseOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(source));
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bytes.Write(buffer, 0, read);
        }
        return Parse(bytes.ToArray(), options, cancellationToken);
    }
}
