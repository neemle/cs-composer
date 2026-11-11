using System.Text;
using Neemle.CSharpComposer.IO;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class StreamContractTests
{
    sealed class ProbeStream(byte[] bytes, bool failRead = false, bool failWrite = false) : MemoryStream(bytes)
    {
        public bool Closed { get; private set; }

        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException("Seek forbidden");

        public override void SetLength(long value) => throw new InvalidOperationException("Truncate forbidden");

        public override void Flush() => throw new InvalidOperationException("Flush forbidden");

        public override int Read(byte[] buffer, int offset, int count) => failRead ?
            throw new IOException("read witness") : base.Read(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (failWrite) { base.Write(buffer[..Math.Min(buffer.Length, 1)]); throw new IOException("write witness"); }
            base.Write(buffer);
        }

        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }

    [Fact]
    public void Nonseekable_stream_and_throwing_reads_remain_open()
    {
        using var input = new ProbeStream(Encoding.UTF8.GetBytes("class C {}"));
        Assert.Equal("class C {}", Cs.Parse(input).Root.Render());
        Assert.False(input.Closed);
        using var throwing = new ProbeStream(new byte[] { 1 }, failRead: true);
        Assert.Throws<IOException>(() => Cs.Parse(throwing));
        Assert.False(throwing.Closed);
    }

    [Fact]
    public void Write_uses_current_position_keeps_tail_and_exposes_partial_failure()
    {
        var document = Cs.Parse("class C {}").Root;
        using var output = new ProbeStream(Enumerable.Repeat((byte)'!', 40).ToArray());
        output.ReadByte();
        document.WriteTo(output);
        Assert.Equal((byte)'!', output.ToArray()[0]);
        Assert.Equal((byte)'!', output.ToArray()[^1]);
        Assert.False(output.Closed);
        using var throwing = new ProbeStream(new byte[40], failWrite: true);
        Assert.Throws<IOException>(() => document.WriteTo(throwing));
        Assert.False(throwing.Closed);
        Assert.Equal((byte)'c', throwing.ToArray()[0]);
    }

    [Theory]
    [InlineData(1200)] [InlineData(1201)] [InlineData(12000)] [InlineData(12001)] [InlineData(65001)]
    public void Marked_unicode_encodings_roundtrip_exactly(int codePage)
    {
        Encoding encoding = codePage switch { 1200 => new UnicodeEncoding(false, true, true),
            1201 => new UnicodeEncoding(true, true, true), 12000 => new UTF32Encoding(false, true, true),
            12001 => new UTF32Encoding(true, true, true), _ => new UTF8Encoding(true, true) };
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("class Café {}\r\n\n")).ToArray();
        var document = Cs.Parse(bytes).Root;
        Assert.Equal(bytes, document.ToBytes());
        Assert.Throws<ArgumentException>(() => Cs.Parse(bytes, new() { Encoding = Encoding.ASCII }));
        Assert.DoesNotContain((byte)0xff, document.ToBytes(new EmitOptions { Encoding = new UTF8Encoding(false), EmitBom = false }));
    }
}
