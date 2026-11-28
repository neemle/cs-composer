using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    // GENE-29 F5 (AD B6): a parsed node records only its source span, from its first token through the last one consumed.
    // Comments are attached once, after the whole parse, by the ownership pass (DocumentParser.Ownership.cs). A list owner
    // (a document, namespace, type, extension, body or switch statement) also records the span of its list, where a
    // directive or inactive text becomes a list item rather than trivia of the owner.
    // Kept out of line: inlined into every recursive parse method it multiplies their stack frames.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    T Bind<T>(T node, int start, int listStart = -1, int listEnd = -1) where T : CsNode
    {
        int coreStart = tokens[start].Start;
        if (listStart >= 0) lists[coreStart] = (listStart, listEnd);
        return (T)(node with { SyntaxPosition = new(syntaxIdentity, coreStart, Last.End) });
    }

    // The list span of each list owner, by the owner's start offset (no two list owners start at one token).
    readonly Dictionary<int, (int Start, int End)> lists = new();

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
