using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

public sealed record ParseContext(ParseKind Kind, CsLanguageVersion LanguageVersion = CsLanguageVersion.CSharp15,
    string? ContainingType = null, TypeKind? ContainingKind = null)
{
    public Values<string> PreprocessorSymbols { get; init; } = Values<string>.Empty;
}
public enum ParseKind { Document, Member, Expression, Statements }
