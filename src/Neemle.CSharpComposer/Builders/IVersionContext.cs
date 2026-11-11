using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Builders;

internal interface IVersionContext
{
    CsLanguageVersion TargetVersion { get; }
    TypeKind? OwningType { get; }
}
