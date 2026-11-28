using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
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
            // A statement sequence has no span of its own: its whole source is its list (GENE-29 F5).
            bool list = root.SyntaxPosition == null;
            cancellation.ThrowIfCancellationRequested();
            root = parser.AttachSyntaxTrivia(root, outside: true, list);
            root = parser.Own(root, list, 0, snapshot.Text.Length);
            if (parser.Current.Text.Length != 0) throw new ParseFailure("Unexpected trailing tokens.");
            // A fragment has no recovery site of its own: a too-deep fragment is kept whole as raw source. That
            // includes one whose too-deep type reading was abandoned for a reading that names a keyword type or a
            // non-identifier, such as a generic argument list re-read as comparisons (GENE-19 D10).
            if (parser.tooDeepTypes != 0 && Misread(root)) throw new NestingFailure();
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

    // Whether the tree has a name no parse of valid source produces: a predefined type keyword as a simple name (a
    // keyword type before `.` is a type reference), or a name that is not an identifier, which validation rejects.
    static bool Misread(CsNode root)
    {
        var stack = new Stack<CsNode>();
        stack.Push(root);
        while (stack.Count != 0)
        {
            var node = stack.Pop();
            if (node is CsExpression { Kind: ExpressionKind.Identifier or ExpressionKind.Member } name &&
                (!Names.IsIdentifier(name.Text) || name.Kind == ExpressionKind.Identifier && Cs.PrimitiveNames.Values.Contains(name.Text)))
                return true;
            foreach (var child in node.Children) stack.Push(child);
        }
        return false;
    }

    CsBody StatementSequence()
    {
        var statements = new List<CsStatement>();
        while (Current.Text.Length != 0) { cancellation.ThrowIfCancellationRequested(); statements.Add(Statement(1)); }
        return new(statements.ToArray());
    }
}
