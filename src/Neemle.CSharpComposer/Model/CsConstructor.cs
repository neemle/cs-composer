using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsConstructor(string Name) : CsMember(Name)
{
    public Values<CsParameter> Parameters { get; init; } = Values<CsParameter>.Empty;
    public string? InitializerKind { get; init; }
    public Values<CsExpression> InitializerArguments { get; init; } = Values<CsExpression>.Empty;
    public CsBody Body { get; init; } = new CsBody();
}
