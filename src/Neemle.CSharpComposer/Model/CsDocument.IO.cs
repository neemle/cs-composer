using Neemle.CSharpComposer.IO;
using System.Text;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument
{
    public byte[] ToBytes(EmitOptions? options = null)
    {
        Encoding encoding = options?.Encoding ?? Origin?.Snapshot.Encoding ?? new UTF8Encoding(false, true);
        encoding = (Encoding)encoding.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        var body = encoding.GetBytes(Render(options?.Rendering));
        byte[] prefix = options?.EmitBom switch
        {
            false => Array.Empty<byte>(),
            true => ForcedPreamble(encoding),
            _ => options?.Encoding == null ? Origin?.Snapshot.Preamble ?? Array.Empty<byte>() : encoding.GetPreamble()
        };
        return prefix.Concat(body).ToArray();
    }

    static byte[] ForcedPreamble(Encoding encoding) => encoding.CodePage switch
    {
        65001 => new UTF8Encoding(true, true).GetPreamble(),
        1200 => new UnicodeEncoding(false, true, true).GetPreamble(),
        1201 => new UnicodeEncoding(true, true, true).GetPreamble(),
        12000 => new UTF32Encoding(false, true, true).GetPreamble(),
        12001 => new UTF32Encoding(true, true, true).GetPreamble(),
        _ => throw new ArgumentException("The selected encoding has no supported byte order mark.", nameof(encoding))
    };

    public void WriteTo(Stream destination, EmitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Output stream must be writable.", nameof(destination));
        destination.Write(ToBytes(options));
    }
}
