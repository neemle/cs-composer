using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 AD 84939297 v2 section F, stage F1: the gate's own view of comment ownership (OWN). It never calls product
// comment code: it walks CsNode.Children and reads public properties only.
// - Text is compared after a dedent: line breaks normalised to \n and every line trimmed. This is a superset of the AD
//   B4 dedent, so a comment that only moved to another indentation compares equal.
// - An owner is named by a structural path: each step is the child's kind plus its index among the parent's children
//   that are neither comments nor syntax trivia (trivia steps are counted separately), so adding or moving a comment
//   never renames another comment's owner.
// - Comment tokens inside opaque text (CsRaw, CsRawMember, raw statements, expressions and types, CsBody.Raw) and
//   inside inactive text (CsDisabledText) are lexed with CorpusTokens and counted as comments of that node.
// - On the source side, the comments of a directive line are lexed from the line after its command, except for the
//   directives whose rest is free text (#region, #endregion, #error, #warning, #!, #:), as the product parser does.
// - For OWN-a only (the source side cannot see them), comments inside an interpolated string's holes are excluded:
//   CorpusTokens reads a whole string literal as one token.
internal static class CorpusComments
{
    // Key is "Kind|Placement|text" for a comment node and "Raw|text" for a comment token in opaque or inactive text;
    // Label is "Kind Placement" or "Raw"; Text is the dedented text.
    internal readonly record struct Entry(string Owner, string OwnerKind, string Label, string Text, bool HiddenFromSource)
    {
        internal string Key => Label.Replace(' ', '|') + "|" + Text;

        internal string Id => Owner + "\u0001" + Key;
    }

    sealed record Frame(CsNode Node, Frame? Parent, string Step, bool InInterpolation)
    {
        internal string Path()
        {
            var steps = new List<string>();
            for (var f = this; f != null; f = f.Parent) steps.Add(f.Step);
            steps.Reverse();
            return string.Join("/", steps);
        }
    }

    internal static string Normalize(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(line => line.Trim()));

    internal static IEnumerable<string> Lines(string text) => Normalize(text).Split('\n');

    static readonly string[] FreeTextDirectives = ["region", "endregion", "error", "warning"];

    // Comment lines lexed from source text, including the comments at the end of a directive line.
    internal static IEnumerable<string> SourceLines(string text)
    {
        foreach (var token in CorpusTokens.Scan(text).Where(t => t.Trivia))
        {
            if (!token.Text.StartsWith('#'))
            {
                foreach (var line in Lines(token.Text)) yield return line;
                continue;
            }
            string directive = token.Text[1..].TrimStart();
            int space = directive.IndexOfAny([' ', '\t']);
            string command = space < 0 ? directive : directive[..space];
            if (space < 0 || directive.StartsWith('!') || directive.StartsWith(':') || FreeTextDirectives.Contains(command)) continue;
            foreach (var comment in CorpusTokens.Scan(directive[space..]).Where(t => t.Trivia && !t.Text.StartsWith('#')))
                foreach (var line in Lines(comment.Text)) yield return line;
        }
    }

    // Every comment the model holds below `root` (root included), in a depth-first walk with an explicit stack.
    internal static List<Entry> Entries(CsNode root)
    {
        var result = new List<Entry>();
        var stack = new Stack<Frame>();
        stack.Push(new(root, null, GuardEquivalence.Kind(root), false));
        while (stack.Count != 0)
        {
            var frame = stack.Pop();
            var node = frame.Node;
            if (Raw(node) is { } raw)
                foreach (var token in CorpusTokens.Scan(raw).Where(t => t.Trivia && !t.Text.StartsWith('#')))
                    result.Add(new(frame.Path(), GuardEquivalence.Kind(node), "Raw", Normalize(token.Text), frame.InInterpolation));
            bool interpolation = frame.InInterpolation || node is CsExpression { Kind: ExpressionKind.InterpolatedString } ||
                node is CsSymbolOptions { Operation: SymbolOperation.InterpolatedString };
            var children = node.Children;
            int index = 0, trivia = 0;
            var pending = new List<Frame>();
            foreach (var child in children)
            {
                if (child is CsComment comment)
                {
                    // A comment is owned by `node`; it is hidden from the source when it sits in a hole of an interpolated
                    // string that encloses `node` (a comment on the string itself is outside it).
                    result.Add(new(frame.Path(), GuardEquivalence.Kind(node), comment.Kind + " " + comment.Placement, Normalize(comment.Text),
                        frame.InInterpolation));
                    continue;
                }
                string step = child is CsSyntaxTrivia ? "Trivia#" + trivia++ : GuardEquivalence.Kind(child) + "#" + index++;
                pending.Add(new(child, frame, step, interpolation));
            }
            for (int i = pending.Count - 1; i >= 0; i--) stack.Push(pending[i]);
        }
        return result;
    }

    internal static string? Raw(CsNode node) => node switch
    {
        CsRaw raw => raw.Text,
        CsRawMember member => member.Source,
        CsDisabledText disabled => disabled.Text,
        CsBody { Raw: { } body } => body,
        CsStatement { Kind: StatementKind.Raw } statement => statement.Text,
        CsExpression { Kind: ExpressionKind.Raw } expression => expression.Text,
        CsType { Form: TypeForm.Raw } type => type.Name,
        _ => null
    };

    internal static Dictionary<string, int> Bag(IEnumerable<string> keys)
    {
        var bag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in keys) bag[key] = bag.GetValueOrDefault(key) + 1;
        return bag;
    }

    // Keys whose count differs, with (expected, actual) counts, in ordinal order.
    internal static List<(string Key, int Expected, int Actual)> Difference(Dictionary<string, int> expected, Dictionary<string, int> actual) =>
        expected.Keys.Union(actual.Keys).Order(StringComparer.Ordinal)
            .Select(key => (Key: key, Expected: expected.GetValueOrDefault(key), Actual: actual.GetValueOrDefault(key)))
            .Where(d => d.Expected != d.Actual).ToList();
}
