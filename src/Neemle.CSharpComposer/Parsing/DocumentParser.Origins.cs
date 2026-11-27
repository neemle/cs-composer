using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    // Kept out of line: inlined into every recursive parse method it multiplies their stack frames.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    T Bind<T>(T node, int start, string? property = null, int tokenStart = -1, int tokenLength = 0,
        int listStart = -1, int listEnd = -1) where T : CsNode
    {
        var clauseBindings = clauses.Count == 0 ? null : ClauseBindings(node);
        int coreStart = tokens[start].Start, coreEnd = Last.End;
        int envelopeStart = LeadingStart(coreStart);
        int envelopeEnd = TrailingEnd(coreEnd);
        var inferred = CommentsWithin(envelopeStart, coreStart).Select(t => Comment(t, CommentPlacement.Leading))
            .Concat(CommentsWithin(coreEnd, envelopeEnd).Select(t => Comment(t, CommentPlacement.Trailing))).ToArray();
        // Without envelope comments no descendant can own one, so the subtree is left as it is.
        if (inferred.Length != 0)
        {
            node = (T)NodeRewrite.Children(node, child => StripOwnedComments(child, inferred));
            node = (T)(node with { Comments = new(node.Comments.Concat(inferred)) });
        }
        bool opaque = node is CsRaw or CsRawMember || node is CsExpression { Kind: ExpressionKind.Raw } || node is CsStatement { Kind: StatementKind.Raw };
        if (!opaque && CommentsWithin(coreStart, coreEnd).Any())
        {
            var internalComments = InternalComments(node, coreStart, coreEnd).Select(t => Comment(t, CommentPlacement.Container));
            node = (T)(node with { Comments = new(node.Comments.Concat(internalComments)) });
        }
        var bindings = property == null || tokenStart < 0 ? Values<TokenBinding>.Empty :
            new Values<TokenBinding>(new TokenBinding(property, tokenStart, tokenStart + tokenLength,
                source.Text.Substring(tokenStart, tokenLength)));
        if (clauseBindings != null) bindings = new(bindings.Concat(clauseBindings));
        var bound = (T)(node with
        {
            SyntaxPosition = new(syntaxIdentity, coreStart, coreEnd),
            Origin = new(source, node, envelopeStart, envelopeEnd)
            {
                CoreStart = coreStart,
                CoreEnd = coreEnd,
                NameStart = tokenStart,
                NameLength = tokenLength,
                ListStart = listStart,
                ListEnd = listEnd,
                Bindings = bindings
            }
        });
        if (!opaque) complete.Add(bound);
        return bound;
    }

    // Nodes exactly as Bind returned them (raw nodes aside). Every comment token within such a node's
    // envelope is owned somewhere in its subtree, and records never change, so binding a parent need not
    // walk into them again: a long chain binds each link in constant time (GENE-19 D10).
    readonly HashSet<CsNode> complete = new(ReferenceEqualityComparer.Instance);

    // Comment tokens within the core that no node of the subtree owns yet, in source order.
    List<Token> InternalComments(CsNode node, int coreStart, int coreEnd)
    {
        var owned = new HashSet<(int, int)>();
        var covered = new List<(int Start, int End)>();
        var stack = new Stack<CsNode>();
        stack.Push(node);
        while (stack.Count != 0)
        {
            var current = stack.Pop();
            if (current is CsComment { Origin: { } commentOrigin } && ReferenceEquals(commentOrigin.Snapshot, source)) owned.Add((commentOrigin.CoreStart, commentOrigin.CoreEnd));
            foreach (var child in current.Children)
            {
                if (complete.Contains(child) && child.Origin is { } envelope && ReferenceEquals(envelope.Snapshot, source)) covered.Add((envelope.Start, envelope.End));
                else stack.Push(child);
            }
        }
        covered.Sort();
        var result = new List<Token>();
        int position = coreStart;
        foreach (var (start, end) in covered)
        {
            if (start > position) result.AddRange(CommentsWithin(position, Math.Min(start, coreEnd)).Where(t => !owned.Contains((t.Start, t.End))));
            position = Math.Max(position, end);
        }
        if (position < coreEnd) result.AddRange(CommentsWithin(position, coreEnd).Where(t => !owned.Contains((t.Start, t.End))));
        return result;
    }

    // Removes `owner` comments from the subtree (a parent took them into its envelope). Only nodes whose
    // envelope holds one of them can own one, so other subtrees are kept as they are; the walk uses an explicit
    // stack, never a frame per chain link (GENE-19 D10).
    CsNode StripOwnedComments(CsNode node, CsComment[] owner)
    {
        bool Owned(CsComment comment) => owner.Any(c => c.Origin?.CoreStart == comment.Origin?.CoreStart &&
            c.Origin?.CoreEnd == comment.Origin?.CoreEnd && c.Origin != null);
        bool Holds(CsNode candidate) => candidate.Origin is not { } envelope || !ReferenceEquals(envelope.Snapshot, source) ||
            owner.Any(c => c.Origin is { } span && span.CoreStart >= envelope.Start && span.CoreEnd <= envelope.End);
        return Spine.Rewrite(node, Holds, (original, rewritten) =>
        {
            var retained = new Values<CsComment>(original.Comments.Where(c => !Owned(c)));
            bool leading = original.Comments.Any(c => Owned(c) && c.Placement == CommentPlacement.Leading);
            bool trailing = original.Comments.Any(c => Owned(c) && c.Placement == CommentPlacement.Trailing);
            var cleaned = rewritten with { Comments = retained };
            if (original.Origin is { } origin && (leading || trailing))
                cleaned = cleaned with
                {
                    Origin = origin with
                    {
                        Original = cleaned with { Origin = null },
                        Start = leading ? origin.CoreStart : origin.Start,
                        End = trailing ? origin.CoreEnd : origin.End
                    }
                };
            // The copy of a bound node stays complete when its envelope no longer holds a removed comment.
            if (complete.Contains(original) && !Holds(cleaned)) complete.Add(cleaned);
            return cleaned;
        });
    }

    int LeadingStart(int start)
    {
        int line = LineStart(start);
        int result = Blank(line, start) ? line : start;
        foreach (var comment in CommentsBefore(start))
        {
            if (!Blank(comment.End, result) || LinesBetween(comment.End, result) > 1) break;
            int beginning = LineStart(comment.Start);
            if (!Blank(beginning, comment.Start)) break;
            result = beginning;
        }
        return result;
    }

    int TrailingEnd(int end)
    {
        int result = end;
        foreach (var comment in CommentsFrom(end))
        {
            if (!Blank(result, comment.Start) || LinesBetween(result, comment.Start) != 0) break;
            result = comment.End;
        }
        int p = result;
        while (p < source.Text.Length && source.Text[p] is ' ' or '\t') p++;
        if (p < source.Text.Length && source.Text[p] == '\r') p++;
        if (p < source.Text.Length && source.Text[p] == '\n') p++;
        return p > result && source.Text[result..p].Any(c => c is '\r' or '\n') ? p : result;
    }


    CsComment Comment(Token token, CommentPlacement placement)
    {
        var comment = new CsComment(token.Text,
            token.Text.StartsWith("///", StringComparison.Ordinal) || token.Text.StartsWith("/**", StringComparison.Ordinal)
                ? CommentKind.Documentation : token.Text.StartsWith("/*", StringComparison.Ordinal)
                    ? CommentKind.Block : CommentKind.Line, placement);
        if (comment.Kind == CommentKind.Documentation) comment = DocumentationParser.Parse(comment, version);
        return comment with
        {
            Origin = new(source, comment, token.Start, token.End)
            { CoreStart = token.Start, CoreEnd = token.End }
        };
    }

    Values<CsComment> ContainerComments(int start, int end, IEnumerable<CsNode> children)
    {
        // A comment is occupied when some child envelope contains it: among the envelopes that start at or
        // before it, the one reaching furthest decides, so a sorted prefix maximum answers each comment.
        var occupied = children.Select(c => c.Origin).Where(o => o != null).Select(o => (o!.Start, o.End)).OrderBy(o => o.Start).ToArray();
        var reach = new int[occupied.Length];
        for (int i = 0; i < occupied.Length; i++) reach[i] = Math.Max(occupied[i].End, i == 0 ? int.MinValue : reach[i - 1]);
        bool Occupied(Token t)
        {
            int low = 0, high = occupied.Length;
            while (low < high) { int middle = (low + high) >> 1; if (occupied[middle].Start <= t.Start) low = middle + 1; else high = middle; }
            return low != 0 && reach[low - 1] >= t.End;
        }
        return new(CommentsWithin(start, end).Where(t => !Occupied(t)).Select(t => Comment(t, CommentPlacement.Container)));
    }

    CsRaw RecoverNode(string message, bool malformed = true)
    {
        int start = p, depth = 0;
        do
        {
            cancellation.ThrowIfCancellationRequested();
            string text = Current.Text;
            if (text.Length == 0) break;
            if (depth == 0 && text == "}") break;
            p++;
            if (text.StartsWith('#')) break;
            if (text == "{") depth++;
            // An `else` goes on with the if before it, as in a statement (RecoverStatement).
            if (text == "}") { depth--; if (depth == 0) { Take(";"); if (!Take("else")) break; } }
            if (depth == 0 && text == ";" && !Take("else")) break;
        } while (Current.Text.Length != 0);
        // Callers recover only before a token (never at the end of input), so a closing brace the loop stopped
        // at is consumed here and the raw node always spans at least one token.
        if (p == start) p++;
        int end = Last.End;
        Error(malformed ? "PARSE001" : "PARSE002", message, tokens[start].Start, end - tokens[start].Start);
        return Bind(new CsRaw(source.Text[tokens[start].Start..end]), start);
    }

    static readonly HashSet<string> ExpressionStatementStarts = new(StringComparer.Ordinal) { "return", "throw", "yield" };
    static readonly HashSet<string> ExpressionBraceOpeners = new(StringComparer.Ordinal) { "switch", "with", "is", "and", "or", "not", "new", "]" };

    // A statement that failed to parse ends at a `;` outside every bracket, before an unmatched `}`,
    // at a directive, or after the brace group of a block statement. Braces inside brackets, in a
    // return, throw, yield or assignment, or after switch, with, is, a pattern combinator, new or `]`
    // belong to an expression, so the statement continues past them, as do braces anywhere after an
    // `is`, `with` or non-leading `switch` outside brackets. The body loop starts recovery on a token
    // that is neither `}` nor the end, so at least one token is consumed.
    CsRaw RecoverStatement(string message)
    {
        int start = p, braces = 0, brackets = 0;
        bool expression = ExpressionStatementStarts.Contains(Current.Text), block = false;
        // An `else` never starts a statement: the statement before it is an if's then-branch, so the if goes on with
        // its else branch, which the raw statement keeps too (GENE-27 L1). Kept apart, a raw `else` would attach to
        // whatever an edit puts before it.
        bool Else()
        {
            if (!Take("else")) return false;
            expression = ExpressionStatementStarts.Contains(Current.Text); block = false;
            return true;
        }
        while (Current.Text.Length != 0 && !(braces == 0 && Current.Text == "}"))
        {
            cancellation.ThrowIfCancellationRequested();
            string text = tokens[p++].Text;
            if (text.StartsWith('#')) break;
            if (text is "(" or "[") brackets++;
            else if (text is ")" or "]") brackets = Math.Max(0, brackets - 1);
            else if (text == "{")
            {
                if (braces++ == 0) block = brackets == 0 && !expression && !ExpressionBraceOpeners.Contains(tokens[p - 2].Text);
            }
            else if (text == "}") { if (--braces == 0 && block) { Take(";"); if (Else()) continue; break; } }
            else if (braces == 0 && brackets == 0)
            {
                if (text == ";") { if (Else()) continue; break; }
                if (text == "=" || CompoundOperator(text) != CsOperator.None || text is "is" or "with" || text == "switch" && p - 1 > start) expression = true;
            }
        }
        Error("PARSE002", message, tokens[start].Start, Last.End - tokens[start].Start);
        return Bind(new CsRaw(source.Text[tokens[start].Start..Last.End]), start);
    }
}
