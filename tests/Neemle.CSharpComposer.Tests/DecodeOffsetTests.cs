using System.Text;
using Neemle.CSharpComposer.Parsing;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
[Trait("Category", "Integration")]
public sealed class DecodeOffsetTests
{
    const string Source = "class C {}";

    static readonly byte[] Utf8Invalid = { 0xFF };

    public static TheoryData<string, int> Placements()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in new[] { "utf-8", "utf-8-bom", "utf-16le-bom", "utf-16be-bom", "utf-32le-bom", "utf-32be-bom" })
            foreach (var split in new[] { 0, 5, Source.Length })
                data.Add(name, split);
        return data;
    }

    static (Encoding Encoding, byte[] Preamble, byte[] Invalid) Shape(string name) => name switch
    {
        "utf-8" => (new UTF8Encoding(false, true), Array.Empty<byte>(), Utf8Invalid),
        "utf-8-bom" => (new UTF8Encoding(true, true), new byte[] { 0xEF, 0xBB, 0xBF }, Utf8Invalid),
        "utf-16le-bom" => (new UnicodeEncoding(false, true, true), new byte[] { 0xFF, 0xFE }, new byte[] { 0x00, 0xDC }),
        "utf-16be-bom" => (new UnicodeEncoding(true, true, true), new byte[] { 0xFE, 0xFF }, new byte[] { 0xDC, 0x00 }),
        "utf-32le-bom" => (new UTF32Encoding(false, true, true), new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, new byte[] { 0x00, 0x00, 0x11, 0x00 }),
        _ => (new UTF32Encoding(true, true, true), new byte[] { 0x00, 0x00, 0xFE, 0xFF }, new byte[] { 0x00, 0x11, 0x00, 0x00 })
    };

    static (byte[] Bytes, int Offset) Build(string name, int split)
    {
        var (encoding, preamble, invalid) = Shape(name);
        var head = preamble.Concat(encoding.GetBytes(Source[..split])).ToArray();
        return (head.Concat(invalid).Concat(encoding.GetBytes(Source[split..])).ToArray(), head.Length);
    }

    [Theory(DisplayName = "[GENE-21] Invalid bytes report the absolute byte offset including any BOM")]
    [MemberData(nameof(Placements))]
    public void Invalid_bytes_report_absolute_offset(string name, int split)
    {
        var (bytes, offset) = Build(name, split);
        var error = Assert.Throws<DecoderFallbackException>(() => Cs.Parse(bytes));
        Assert.Equal(offset, error.Index);
        Assert.Equal(Shape(name).Invalid, error.BytesUnknown!.AsSpan(0, Shape(name).Invalid.Length).ToArray());
        Assert.Contains($"byte offset {offset}", error.Message);
    }

    [Theory(DisplayName = "[GENE-21] Stream input reports the same offset, counted from the position at the call")]
    [MemberData(nameof(Placements))]
    public void Stream_offset_matches_byte_array_offset(string name, int split)
    {
        var (bytes, offset) = Build(name, split);
        var fromArray = Assert.Throws<DecoderFallbackException>(() => Cs.Parse(bytes));
        using var input = new MemoryStream(new byte[] { 42, 42, 42 }.Concat(bytes).ToArray());
        input.Position = 3;
        var fromStream = Assert.Throws<DecoderFallbackException>(() => Cs.Parse(input));
        Assert.Equal(offset, fromStream.Index);
        Assert.Equal(fromArray.Index, fromStream.Index);
        Assert.Equal(fromArray.Message, fromStream.Message);
        Assert.True(input.CanRead);
        Assert.Equal(input.Length, input.Position);
    }

    [Fact(DisplayName = "[GENE-21] An explicit encoding without a BOM reports the unshifted offset")]
    public void Explicit_encoding_offset_is_unshifted()
    {
        var encoding = new UnicodeEncoding(false, false, true);
        var bytes = encoding.GetBytes("class").Concat(new byte[] { 0x00, 0xDC }).Concat(encoding.GetBytes(" C {}")).ToArray();
        var error = Assert.Throws<DecoderFallbackException>(() => Cs.Parse(bytes, new ParseOptions { Encoding = encoding }));
        Assert.Equal(10, error.Index);
    }

    [Fact(DisplayName = "[GENE-21] The UTF-8 BOM repro from QA F5 reports offset 13")]
    public void Qa_f5_repro_reports_thirteen()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.ASCII.GetBytes(Source)).Append((byte)0xFF).ToArray();
        Assert.Equal(13, Assert.Throws<DecoderFallbackException>(() => Cs.Parse(bytes)).Index);
        Assert.Equal(13, Assert.Throws<DecoderFallbackException>(() => Cs.Parse(new MemoryStream(bytes))).Index);
    }

    [Fact(DisplayName = "[GENE-21] An explicit UTF-16 request keeps its own BOM when the text starts with NUL")]
    public void Explicit_utf16_bom_followed_by_nul_is_not_read_as_utf32()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("\0class C {}")).ToArray();
        var document = Cs.Parse(bytes, new ParseOptions { Encoding = encoding }).Root;
        // GENE-29 F4: every render formats; the output keeps the UTF-16 BOM and reads back to the same bytes.
        var output = document.ToBytes();
        Assert.Equal(encoding.GetPreamble(), output.Take(2));
        Assert.Equal(output, Cs.Parse(output, new ParseOptions { Encoding = encoding }).Root.ToBytes());
    }

    [Fact(DisplayName = "[GENE-21] Unencodable text reports a UTF-16 character index, as documented")]
    public void Unencodable_text_reports_character_index()
    {
        var ascii = Encoding.ASCII;
        var parse = Assert.Throws<EncoderFallbackException>(() => Cs.Parse("class Ä {}", new ParseOptions { Encoding = ascii }));
        Assert.Equal(6, parse.Index);
        var document = Cs.Parse("class Ä {}").Root;
        var emit = Assert.Throws<EncoderFallbackException>(() => document.ToBytes(new() { Encoding = ascii }));
        Assert.Equal(6, emit.Index);
    }

    public static TheoryData<string> Encodings() => new() { "utf-8", "utf-8-bom", "utf-16le-bom", "utf-16be-bom", "utf-32le-bom", "utf-32be-bom" };

    [Theory(DisplayName = "[GENE-22] Mutating every supplied byte after Parse cannot change the document or its BOM")]
    [MemberData(nameof(Encodings))]
    public void Caller_buffer_mutation_after_parse_is_isolated(string name)
    {
        var (encoding, preamble, _) = Shape(name);
        var bytes = preamble.Concat(encoding.GetBytes(Source)).ToArray();
        // GENE-29 F4: what the untouched document renders to (every render formats), taken before the mutation.
        var expected = Cs.Parse((byte[])bytes.Clone()).Root.ToBytes();
        var fromArray = Cs.Parse(bytes).Root;
        using var stream = new MemoryStream(bytes, writable: true);
        var fromStream = Cs.Parse(stream).Root;
        Array.Fill(bytes, (byte)0x55);
        FormatCheck.FixedPoint(fromArray);
        Assert.Equal(expected, fromArray.ToBytes());
        Assert.Equal(expected, fromStream.ToBytes());
        var emitted = fromArray.ToBytes();
        Array.Fill(emitted, (byte)0x66);
        Assert.Equal(expected, fromArray.ToBytes());
    }

    [Fact(DisplayName = "[GENE-22] A BOM-only buffer is copied, not aliased")]
    public void Bom_only_buffer_is_copied()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF };
        var document = Cs.Parse(bytes).Root;
        bytes[0] = 0;
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'\n' }, document.ToBytes());
    }

    [Fact(DisplayName = "[GENE-22] A null byte array is rejected with the public parameter name")]
    public void Null_byte_array_names_source()
    {
        var error = Assert.Throws<ArgumentNullException>(() => Cs.Parse((byte[])null!));
        Assert.Equal("source", error.ParamName);
    }
}
