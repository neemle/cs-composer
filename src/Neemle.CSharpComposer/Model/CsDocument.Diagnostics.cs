using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument
{
    public IReadOnlyList<ParseDiagnostic> EmissionDiagnostics => Formatting == null &&
        !Descendants().Any(n => n.Node.Formatting != null) ? Array.Empty<ParseDiagnostic>() :
        Descendants().Where(n => IsOpaque(n.Node)).Select(n => new ParseDiagnostic("FORMAT001",
            DiagnosticSeverity.Warning, n.Node.Origin?.CoreStart ?? 0, 0, 1, 1,
            "Opaque syntax is preserved verbatim; formatting coverage excludes this region.")).ToArray();
    static bool IsOpaque(CsNode node) => node is CsRaw or CsRawMember || node is CsBody { Raw: not null } ||
        node is CsExpression { Kind: ExpressionKind.Raw } || node is CsStatement { Kind: StatementKind.Raw } ||
        node is CsType { Form: TypeForm.Raw };
}
