using System.Text;
using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

public sealed record ParseOptions
{
    public CsLanguageVersion LanguageVersion { get; init; } = CsLanguageVersion.CSharp15;

    public Values<string> PreprocessorSymbols { get; init; } = Values<string>.Empty;

    public Encoding? Encoding { get; init; }
}
