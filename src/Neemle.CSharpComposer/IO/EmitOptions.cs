using System.Text;
using Neemle.CSharpComposer.Rendering;
namespace Neemle.CSharpComposer.IO;
public sealed record EmitOptions
{
    public RenderOptions? Rendering { get; init; }
    public Encoding? Encoding { get; init; }
    public bool? EmitBom { get; init; }
}
