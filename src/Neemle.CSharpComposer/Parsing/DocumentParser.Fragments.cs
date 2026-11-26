using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    internal static ParseResult<CsNode> Fragment(SourceSnapshot snapshot, CsRaw raw, ParseContext context, CancellationToken cancellation)
    {
        LanguageFeatures.EnsureSupported(context.LanguageVersion);
        cancellation.ThrowIfCancellationRequested();
        var parser = new DocumentParser(snapshot, context.LanguageVersion, context.PreprocessorSymbols, cancellation);
        try
        {
            CsNode root = context.Kind switch
            {
                ParseKind.Expression => parser.Expression(0, 0),
                ParseKind.Member => parser.Member(context.ContainingType ?? "", context.ContainingKind ?? TypeKind.Class, 0),
                ParseKind.Statements => parser.StatementSequence(),
                _ => throw new ArgumentOutOfRangeException(nameof(context))
            };
            if (root.Origin == null) root = root with { Origin = new(snapshot, root, 0, snapshot.Text.Length) { CoreStart = 0, CoreEnd = snapshot.Text.Length, ListStart = 0, ListEnd = snapshot.Text.Length } };
            cancellation.ThrowIfCancellationRequested();
            root = parser.AttachSyntaxTrivia(root, includeOutside: true);
            if (parser.Current.Text.Length != 0) throw new ParseFailure("Unexpected trailing tokens.");
            // A fragment has no recovery site of its own: a too-deep fragment is kept whole as raw source.
            parser.Fit(root, 0);
            if (parser.diagnostics.Count != 0) return new(raw, new(parser.diagnostics), ParseCoverage.Partial);
            return new(root with { Comments = raw.Comments.Count == 0 ? root.Comments : raw.Comments },
                Values<ParseDiagnostic>.Empty, ParseCoverage.Typed);
        }
        catch (ParseFailure failure) when (!failure.Kept && parser.tooDeepTypes == 0)
        {
            parser.Error("PARSE001", failure.Message, parser.Current.Start);
            return new(raw, new(parser.diagnostics), ParseCoverage.Opaque);
        }
        catch (Exception failure) when (failure is ParseFailure or NestingFailure)
        {
            parser.Error("PARSE002", NestingMessage, 0, snapshot.Text.Length);
            return new(raw, new(parser.diagnostics), ParseCoverage.Opaque);
        }
    }

    CsBody StatementSequence()
    {
        var statements = new List<CsStatement>();
        while (Current.Text.Length != 0) { cancellation.ThrowIfCancellationRequested(); statements.Add(Statement(1)); }
        return new(statements.ToArray());
    }
}
