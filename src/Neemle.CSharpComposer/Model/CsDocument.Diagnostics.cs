using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument
{
    public IReadOnlyList<ParseDiagnostic> EmissionDiagnostics
    {
        get
        {
            var locations = Descendants().ToArray();
            var scopes = locations.Where(x => x.Node.Formatting != null).Select(x => x.Path.Segments).ToArray();
            return locations.Where(x => IsOpaque(x.Node) && scopes.Any(path =>
                x.Path.Segments.Take(path.Count).SequenceEqual(path))).Select(x => OpaqueDiagnostic(x.Node)).ToArray();
        }
    }

    static ParseDiagnostic OpaqueDiagnostic(CsNode node)
    {
        int offset = node.Origin?.CoreStart ?? 0, line = 1, column = 1;
        string text = node.Origin?.Snapshot.Text ?? "";
        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\r') { line++; column = 1; if (i + 1 < offset && text[i + 1] == '\n') i++; }
            else if (text[i] == '\n') { line++; column = 1; }
            else column++;
        }
        return new("FORMAT001", DiagnosticSeverity.Warning, offset,
            node.Origin == null ? 0 : node.Origin.CoreEnd - offset, line, column,
            "Opaque syntax is preserved verbatim; formatting coverage excludes this region.");
    }

    static bool IsOpaque(CsNode node) => node is CsRaw or CsRawMember || node is CsBody { Raw: not null } ||
        node is CsExpression { Kind: ExpressionKind.Raw } || node is CsStatement { Kind: StatementKind.Raw } ||
        node is CsType { Form: TypeForm.Raw };
}
