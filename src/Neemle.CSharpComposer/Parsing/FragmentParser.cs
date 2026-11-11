using Neemle.CSharpComposer.Model;
using System.Text;
namespace Neemle.CSharpComposer.Parsing;
internal static class FragmentParser
{
    internal static ParseResult<CsNode> Parse(CsNode original, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var raw = new CsRaw(Text(original)) { Comments = original.Comments };
        var snapshot = SourceSnapshot.Decode(new UTF8Encoding(false, true).GetBytes(raw.Text), null);
        ParseResult<CsNode> result;
        if (context.Kind == ParseKind.Document)
        {
            var parsed = DocumentParser.Parse(snapshot, new() { LanguageVersion = context.LanguageVersion });
            result = new(parsed.Root, parsed.Diagnostics, parsed.Coverage);
        }
        else result = DocumentParser.Fragment(snapshot, raw, context);
        if (result.HasErrors || result.Coverage != ParseCoverage.Typed)
            return result with { Root = original };
        var unsupported = VersionErrors(result.Root, context).ToArray();
        if (unsupported.Length != 0) return new(original, new(unsupported), ParseCoverage.Opaque);
        return result with { Root = result.Root with { Comments = original.Comments.Count == 0 ?
            result.Root.Comments : original.Comments } };
    }
    static string Text(CsNode node) => node switch
    {
        CsRaw raw => raw.Text, CsRawMember raw => raw.Source,
        CsBody { Raw: not null } body => body.Raw,
        CsExpression { Kind: ExpressionKind.Raw } expression => expression.Text,
        CsStatement { Kind: StatementKind.Raw } statement => statement.Text,
        CsType { Form: TypeForm.Raw } type => type.Name,
        _ => throw new InvalidOperationException("Explicit Parse requires a raw node and a matching context.")
    };
    static IEnumerable<ParseDiagnostic> VersionErrors(CsNode node, ParseContext context)
    {
        CsMember member = node switch
        {
            CsMember declaration => declaration,
            CsExpression expression => new CsField(Cs.Int, "Value") { Initializer = expression },
            CsBody body => new CsMethod(Cs.Void, "Body") { Body = body },
            _ => new CsRawMember("")
        };
        var document = node as CsDocument ?? new CsDocument(context.LanguageVersion) { Types = new[] {
            new CsTypeDeclaration(context.ContainingKind ?? TypeKind.Class, context.ContainingType ?? "Context") {
                Members = new[] { member } } } };
        return document.Validate().Where(e => e.Code is "GEN072" or "GEN073").Select(e => new ParseDiagnostic(e.Code,
            DiagnosticSeverity.Error, 0, 0, 1, 1, e.Message));
    }
}
