using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    internal void Directive(CsDirective d, string path, int depth)
    {
        if (!Depth(depth, path)) return;
        if (!Enum.IsDefined(d.Kind) || !Enum.IsDefined(d.Action)) Error("GEN092", path, "Unknown directive or action.");
        bool conditional = d.Kind is DirectiveKind.If or DirectiveKind.Elif;
        bool symbol = d.Kind is DirectiveKind.Define or DirectiveKind.Undef;
        bool message = d.Kind is DirectiveKind.Region or DirectiveKind.EndRegion or DirectiveKind.Error or DirectiveKind.Warning or DirectiveKind.Shebang or
            DirectiveKind.Sdk or DirectiveKind.Package or DirectiveKind.Project or DirectiveKind.Property or DirectiveKind.Ignored or DirectiveKind.Nullable;
        bool file = d.Kind is DirectiveKind.Line or DirectiveKind.PragmaChecksum or DirectiveKind.Reference or DirectiveKind.Load;
        if (d.Name != "" || d.Modifiers.Count > 0 || d.Attributes.Count > 0 || d.ExplicitInterface != null ||
            !conditional && d.Condition != null || !symbol && d.Symbol != null || !message && d.Text != null || !file && d.File != null ||
            d.Kind != DirectiveKind.Line && (d.Line != null || d.Start != null || d.End != null || d.CharacterOffset != null) ||
            d.Kind != DirectiveKind.PragmaChecksum && (d.Checksum != null || d.ChecksumAlgorithm != null) ||
            d.Kind != DirectiveKind.PragmaWarning && d.Warnings?.Count > 0 || d.Kind is not (DirectiveKind.PragmaWarning or DirectiveKind.Nullable or DirectiveKind.Line) && d.Action != DirectiveAction.None)
            Error("GEN065", path, "Payload does not belong to this directive kind.");
        if (conditional)
        {
            Expression(d.Condition, path + ".Condition", depth + 1);
            if (d.Condition != null && !PreprocessorCondition(d.Condition)) Error("GEN092", path, "Preprocessor condition permits symbols, booleans, !, ==, !=, && and ||.");
        }
        if (symbol) { Identifier(d.Symbol, path + ".Symbol"); if (d.Symbol?.StartsWith('@') == true || d.Symbol is "true" or "false") Error("GEN092", path, "Invalid preprocessor symbol."); }
        if (d.Text?.IndexOfAny(new[] { '\r', '\n' }) >= 0 || d.Symbol?.IndexOfAny(new[] { '\r', '\n' }) >= 0) Error("GEN092", path, "Directive text must occupy one line.");
        if (d.Kind == DirectiveKind.Nullable)
        {
            Feature("Nullable directive", CsLanguageVersion.CSharp8, path);
            if (d.Action is not (DirectiveAction.Enable or DirectiveAction.Disable or DirectiveAction.Restore) || d.Text != null && d.Text is not ("warnings" or "annotations")) Error("GEN092", path, "Invalid nullable directive action or target.");
        }
        if (d.Kind == DirectiveKind.PragmaWarning)
        {
            if (d.Action is not (DirectiveAction.Disable or DirectiveAction.Restore)) Error("GEN092", path, "Warning pragma requires disable or restore.");
            if (d.Warnings == null) Required(null, path + ".Warnings");
            else foreach (string warning in d.Warnings) if (string.IsNullOrEmpty(warning) || !warning.All(char.IsLetterOrDigit)) Error("GEN092", path, "Invalid warning identifier.");
        }
        if (d.Kind == DirectiveKind.Line)
        {
            if (d.Action is not (DirectiveAction.None or DirectiveAction.Default or DirectiveAction.Hidden)) Error("GEN092", path, "Invalid line directive action.");
            if (d.Action == DirectiveAction.None && (d.Line == null) == (d.Start == null) || d.Action != DirectiveAction.None && (d.Line != null || d.Start != null || d.File != null)) Error("GEN092", path, "Line directive needs one mapping or default/hidden.");
            if (d.Line <= 0) Error("GEN092", path, "Mapped line must be positive.");
            if (d.Start != null)
            {
                Feature("Enhanced line mapping", CsLanguageVersion.CSharp10, path);
                if (d.End == null || d.Start.Line < 1 || d.Start.Column < 1 || d.End.Line < d.Start.Line || d.End.Column < 1 || d.End.Line == d.Start.Line && d.End.Column < d.Start.Column || d.CharacterOffset < 0 || d.File == null) Error("GEN092", path, "Invalid line span mapping.");
            }
            else if (d.End != null || d.CharacterOffset != null) Error("GEN065", path, "Line endpoint requires a start position.");
        }
        if (d.Kind == DirectiveKind.PragmaChecksum && (d.File == null || !Guid.TryParse(d.ChecksumAlgorithm, out _) || d.Checksum == null || d.Checksum.Length % 2 != 0 || !d.Checksum.All(Uri.IsHexDigit))) Error("GEN092", path, "Invalid checksum directive.");
        if (d.Kind is DirectiveKind.Reference or DirectiveKind.Load && d.File == null) Required(null, path + ".File");
        if (d.Kind is DirectiveKind.Sdk or DirectiveKind.Package or DirectiveKind.Project or DirectiveKind.Property or DirectiveKind.Ignored) Feature("File-based application directive", CsLanguageVersion.CSharp14, path);
    }

    void DirectiveSequence(CsNode root)
    {
        var conditions = new Stack<bool>(); int regions = 0;
        var directives = new List<(CsDirective Directive, SyntaxPosition? Position)>();
        void Collect(CsNode node, SyntaxPosition? parentPosition = null)
        {
            var position = node.SyntaxPosition ?? parentPosition;
            if (node is CsDirective directive) directives.Add((directive, position));
            foreach (var trivia in node.SyntaxTrivia.Where(t => t.Placement == SyntaxTriviaPlacement.Leading && t.Anchor == null)) Collect(trivia, position);
            foreach (var child in node.Children.Where(c => c is not CsSyntaxTrivia)) Collect(child, position);
            foreach (var trivia in node.SyntaxTrivia.Where(t => t.Placement == SyntaxTriviaPlacement.Trailing || t.Anchor != null))
                Collect(trivia, position is { } span && trivia.Placement == SyntaxTriviaPlacement.Trailing ? span with { Offset = span.End } : position);
        }
        Collect(root);
        // Attached directives and list directives share one lexical sequence.
        // Their source spans establish order even across nested semantic nodes.
        if (directives.Count > 0 && directives.All(d => d.Position != null) && directives.All(d => d.Position!.Value.Source == directives[0].Position!.Value.Source))
            directives = directives.OrderBy(d => d.Position!.Value.Offset).ToList();
        foreach (var entry in directives)
        {
            var d = entry.Directive;
            Directive(d, "Directives", 0);
            switch (d.Kind)
            {
                case DirectiveKind.If: conditions.Push(false); break;
                case DirectiveKind.Elif:
                    if (conditions.Count == 0 || conditions.Peek()) Error("GEN092", "Directives", "Elif requires a preceding if and cannot follow else."); break;
                case DirectiveKind.Else:
                    if (conditions.Count == 0 || conditions.Pop()) Error("GEN092", "Directives", "Else requires a preceding if and cannot repeat.");
                    conditions.Push(true); break;
                case DirectiveKind.EndIf:
                    if (conditions.Count == 0) Error("GEN092", "Directives", "Endif has no matching if."); else conditions.Pop(); break;
                case DirectiveKind.Region: regions++; break;
                case DirectiveKind.EndRegion: if (--regions < 0) { Error("GEN092", "Directives", "Endregion has no matching region."); regions = 0; } break;
            }
        }
        if (conditions.Count > 0 || regions > 0) Error("GEN092", "Directives", "Conditional or region directive is not closed.");
    }

    static bool PreprocessorCondition(CsExpression e) => e.Kind switch
    {
        ExpressionKind.Identifier => true,
        ExpressionKind.Literal => e.Text is "true" or "false",
        ExpressionKind.Parenthesized => e.Arguments.Count == 1 && PreprocessorCondition(e.Arguments[0]),
        ExpressionKind.Unary => e.Operator == CsOperator.Not && e.Arguments.All(PreprocessorCondition),
        ExpressionKind.Binary => e.Operator is CsOperator.Equal or CsOperator.NotEqual or CsOperator.AndAlso or CsOperator.OrElse && e.Arguments.All(PreprocessorCondition),
        _ => false
    };
}
