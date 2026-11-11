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
            true => encoding.GetPreamble(),
            _ => options?.Encoding == null ? Origin?.Snapshot.Preamble ?? Array.Empty<byte>() : encoding.GetPreamble()
        };
        return prefix.Concat(body).ToArray();
    }
    public void WriteTo(Stream destination, EmitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Output stream must be writable.", nameof(destination));
        destination.Write(ToBytes(options));
    }
}
