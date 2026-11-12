using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    internal static ParseResult<CsNode> Fragment(SourceSnapshot snapshot, CsRaw raw, ParseContext context)
    {
        LanguageFeatures.EnsureSupported(context.LanguageVersion);
        var parser = new DocumentParser(snapshot, context.LanguageVersion, context.PreprocessorSymbols);
        try
        {
            CsNode root = context.Kind switch
            {
                ParseKind.Expression => parser.Expression(),
                ParseKind.Member => parser.Member(context.ContainingType ?? "", context.ContainingKind ?? TypeKind.Class, 0),
                ParseKind.Statements => parser.StatementSequence(),
                _ => throw new ArgumentOutOfRangeException(nameof(context))
            };
            if (root.Origin == null) root = root with { Origin = new(snapshot, root, 0, snapshot.Text.Length) { CoreStart = 0, CoreEnd = snapshot.Text.Length, ListStart = 0, ListEnd = snapshot.Text.Length } };
            root = parser.AttachSyntaxTrivia(root, includeOutside: true);
            if (parser.Current.Text.Length != 0) throw new ParseFailure("Unexpected trailing tokens.");
            if (parser.diagnostics.Count != 0) return new(raw, new(parser.diagnostics), ParseCoverage.Partial);
            return new(root with { Comments = raw.Comments.Count == 0 ? root.Comments : raw.Comments },
                Values<ParseDiagnostic>.Empty, ParseCoverage.Typed);
        }
        catch (ParseFailure failure)
        {
            parser.Error("PARSE001", failure.Message, parser.Current.Start);
            return new(raw, new(parser.diagnostics), ParseCoverage.Opaque);
        }
    }

    CsBody StatementSequence()
    {
        var statements = new List<CsStatement>();
        while (Current.Text.Length != 0) statements.Add(Statement());
        return new(statements.ToArray());
    }
}
