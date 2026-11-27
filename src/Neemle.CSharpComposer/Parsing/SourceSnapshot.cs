using Neemle.CSharpComposer.Model;
using System.Text;

namespace Neemle.CSharpComposer.Parsing;

// Holds no reference to the caller's buffer: Text is an immutable decoded string and Preamble is a fresh copy,
// so mutating the supplied bytes after Parse cannot change the snapshot.
internal sealed record SourceSnapshot(string Text, Encoding Encoding, byte[] Preamble)
{
    // GENE-29 F3: the preprocessor symbols the source was parsed with, so the meaning guard reads a render back with them.
    internal Values<string> Symbols { get; init; } = Values<string>.Empty;

    internal static SourceSnapshot Decode(byte[] bytes, Encoding? requested)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Encoding encoding = requested ?? new UTF8Encoding(false, true);
        int skip = 0;
        // UTF-32LE is tried before UTF-16LE because their marks share a prefix; an explicit request tries its own
        // mark first so UTF-16LE text that starts with U+0000 is not mistaken for UTF-32LE.
        foreach (var candidate in new Encoding[] { new UTF32Encoding(false, true, true),
            new UTF32Encoding(true, true, true), new UTF8Encoding(true, true),
            new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) }
            .OrderByDescending(candidate => candidate.CodePage == requested?.CodePage))
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
        return new(DecodeText(bytes, skip, encoding), encoding, bytes[..skip]);
    }

    // The BCL reports Index relative to the decoded range, which starts after the byte order mark. Callers see
    // offsets from the first supplied byte (or the stream position at the call), so the mark is added back.
    static string DecodeText(byte[] bytes, int skip, Encoding encoding)
    {
        try
        {
            return encoding.GetString(bytes, skip, bytes.Length - skip);
        }
        catch (DecoderFallbackException error)
        {
            int offset = skip + error.Index;
            throw new DecoderFallbackException($"Unable to decode {encoding.WebName} input: invalid byte sequence " +
                $"at byte offset {offset} from the start of the supplied input, including any byte order mark.",
                error.BytesUnknown, offset);
        }
    }
}
