using System.Text;
using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;
public sealed record ParseOptions
{
    public CsLanguageVersion LanguageVersion { get; init; } = CsLanguageVersion.CSharp15;

    public Encoding? Encoding { get; init; }
}
