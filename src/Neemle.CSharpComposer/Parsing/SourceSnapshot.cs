using System.Text;

namespace Neemle.CSharpComposer.Parsing;

internal sealed record SourceSnapshot(string Text, byte[] Bytes, Encoding Encoding, byte[] Preamble)
{
    internal static SourceSnapshot Decode(byte[] bytes, Encoding? requested)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        bytes = (byte[])bytes.Clone();
        Encoding encoding = requested ?? new UTF8Encoding(false, true);
        int skip = 0;
        foreach (var candidate in new Encoding[] { new UTF32Encoding(false, true, true),
            new UTF32Encoding(true, true, true), new UTF8Encoding(true, true),
            new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
        {
            var prefix = candidate.GetPreamble();
            if (!bytes.AsSpan().StartsWith(prefix)) continue;
            if (requested != null && requested.CodePage != candidate.CodePage)
                throw new ArgumentException("The supplied encoding conflicts with the byte order mark.");
            encoding = candidate;
            skip = prefix.Length;
            break;
        }
        encoding = (Encoding)encoding.Clone();
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        return new(encoding.GetString(bytes, skip, bytes.Length - skip), bytes, encoding, bytes[..skip]);
    }
}
