using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;
public sealed record ParseContext(ParseKind Kind, CsLanguageVersion LanguageVersion = CsLanguageVersion.CSharp15,
    string? ContainingType = null, TypeKind? ContainingKind = null);
public enum ParseKind { Document, Member, Expression, Statements }
