using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument
{
    // GENE-29 F4 (AD E): every render formats the whole document, so every opaque node is reported.
    public IReadOnlyList<ParseDiagnostic> EmissionDiagnostics =>
        Descendants().Where(x => IsOpaque(x.Node)).Select(x => OpaqueDiagnostic(x.Node)).ToArray();

    // An opaque node of this document's own parse is reported at its source span (GENE-29 F5: by its SyntaxPosition); any
    // other one at the start.
    ParseDiagnostic OpaqueDiagnostic(CsNode node)
    {
        int offset = 0, length = 0, line = 1, column = 1;
        if (SourceInfo is { } source && node.SyntaxPosition is { } at && at.Source == source.Identity)
        {
            (offset, length) = (at.Offset, at.End - at.Offset);
            string text = source.Snapshot.Text;
            for (int i = 0; i < offset; i++)
            {
                if (text[i] == '\r') { line++; column = 1; if (i + 1 < offset && text[i + 1] == '\n') i++; }
                else if (text[i] == '\n') { line++; column = 1; }
                else column++;
            }
        }
        return new("FORMAT001", DiagnosticSeverity.Warning, offset, length, line, column,
            "Opaque syntax is preserved verbatim; formatting coverage excludes this region.");
    }

    static bool IsOpaque(CsNode node) => node is CsRaw or CsRawMember || node is CsBody { Raw: not null } ||
        node is CsExpression { Kind: ExpressionKind.Raw } || node is CsStatement { Kind: StatementKind.Raw } ||
        node is CsType { Form: TypeForm.Raw };
}
