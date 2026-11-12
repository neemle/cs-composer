using System.Text;
using System.Text.RegularExpressions;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsDirective Directive()
    {
        if (!source.Text[LineStart(Current.Start)..Current.Start].All(char.IsWhiteSpace)) throw new ParseFailure("A directive must begin its line.");
        int start = p; string text = tokens[p++].Text[1..].Trim();
        if (text.StartsWith('!')) return Bind(new CsDirective(DirectiveKind.Shebang) { Text = text[1..] }, start);
        bool fileBased = text.StartsWith(':'); if (fileBased) text = text[1..];
        int separator = text.IndexOfAny(new[] { ' ', '\t' });
        string command = separator < 0 ? text : text[..separator];
        string rest = separator < 0 ? "" : text[(separator + 1)..].Trim();
        if (command == "pragma")
        {
            int space = rest.IndexOfAny(new[] { ' ', '\t' });
            command = "pragma" + (space < 0 ? rest : rest[..space]); rest = space < 0 ? "" : rest[(space + 1)..].Trim();
        }
        var found = Enum.GetValues<DirectiveKind>().Where(k => k.ToString().ToLowerInvariant() == command).ToArray();
        if (command == "r") found = new[] { DirectiveKind.Reference };
        if (found.Length == 0 && fileBased) return Bind(new CsDirective(DirectiveKind.Ignored) { Text = text }, start);
        if (found.Length == 0) throw new ParseFailure("Unknown preprocessor directive.");
        var kind = found[0];
        if (fileBased != (kind is DirectiveKind.Sdk or DirectiveKind.Package or DirectiveKind.Project or DirectiveKind.Property)) throw new ParseFailure("Invalid file-based directive prefix.");
        CsDirective result = new(kind);
        if (kind is not (DirectiveKind.Region or DirectiveKind.EndRegion or DirectiveKind.Error or DirectiveKind.Warning or DirectiveKind.Shebang or DirectiveKind.Sdk or DirectiveKind.Package or DirectiveKind.Project or DirectiveKind.Property))
        {
            var comments = Lexer.Scan(rest).Where(t => t.IsComment).ToArray();
            if (comments.Length > 0)
            {
                result = result with { Comments = new(comments.Select(c => new CsComment(c.Text, c.Text.StartsWith("//", StringComparison.Ordinal) ? CommentKind.Line : CommentKind.Block, CommentPlacement.Trailing))) };
                rest = rest[..comments[0].Start].TrimEnd();
            }
        }
        if (kind is DirectiveKind.If or DirectiveKind.Elif)
        {
            var snapshot = SourceSnapshot.Decode(Encoding.UTF8.GetBytes(rest), null);
            var conditionParser = new DocumentParser(snapshot, version);
            var condition = conditionParser.Expression();
            if (conditionParser.Current.Text != "") throw new ParseFailure("Invalid preprocessor condition.");
            result = result with { Condition = (CsExpression)condition.Detach() };
        }
        else if (kind is DirectiveKind.Define or DirectiveKind.Undef) result = result with { Symbol = rest };
        else if (kind is DirectiveKind.PragmaWarning or DirectiveKind.Nullable)
        {
            var args = rest.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0 || !Enum.TryParse<DirectiveAction>(args[0], true, out var action) || args[0] != action.ToString().ToLowerInvariant()) throw new ParseFailure("Directive action expected.");
            result = result with
            {
                Action = action,
                Warnings = kind == DirectiveKind.PragmaWarning && args.Length > 1 ?
                args[1].Split(',').Select(s => s.Trim()).ToArray() : Values<string>.Empty,
                Text = kind == DirectiveKind.Nullable && args.Length > 1 ? args[1] : null
            };
        }
        else if (kind is DirectiveKind.Reference or DirectiveKind.Load) result = result with { File = DirectiveString(rest) };
        else if (kind == DirectiveKind.PragmaChecksum)
        {
            var args = Lexer.Scan(rest).Where(t => t.Text != "" && !t.IsComment).ToArray();
            if (args.Length != 3) throw new ParseFailure("Checksum requires a file, algorithm and checksum.");
            result = result with { File = DirectiveString(args[0].Text), ChecksumAlgorithm = DirectiveString(args[1].Text), Checksum = DirectiveString(args[2].Text) };
        }
        else if (kind == DirectiveKind.Line)
        {
            var args = Lexer.Scan(rest).Where(t => t.Text != "" && !t.IsComment).ToArray(); int index = 0;
            string Next() => index < args.Length ? args[index++].Text : throw new ParseFailure("Incomplete line directive.");
            int Number() => int.TryParse(Next(), out int n) ? n : throw new ParseFailure("Line directive requires an integer.");
            void Punctuation(string token) { if (Next() != token) throw new ParseFailure("Invalid line directive."); }
            if (rest is "default" or "hidden") result = result with { Action = rest == "default" ? DirectiveAction.Default : DirectiveAction.Hidden };
            else if (args.Length > 0 && args[0].Text == "(")
            {
                Punctuation("("); int sl = Number(); Punctuation(","); int sc = Number(); Punctuation(")"); Punctuation("-");
                Punctuation("("); int el = Number(); Punctuation(","); int ec = Number(); Punctuation(")");
                int? offset = index < args.Length && int.TryParse(args[index].Text, out _) ? Number() : null;
                result = result with { Start = new(sl, sc), End = new(el, ec), CharacterOffset = offset, File = DirectiveString(Next()) };
                if (index != args.Length) throw new ParseFailure("Unexpected line directive arguments.");
            }
            else
            {
                int line = Number(); result = result with { Line = line, File = index < args.Length ? DirectiveString(Next()) : null };
                if (index != args.Length) throw new ParseFailure("Unexpected line directive arguments.");
            }
        }
        else if (rest.Length > 0) result = result with { Text = rest };
        return Bind(result, start);
    }

    static string DirectiveString(string text)
    {
        if (text.Length < 2 || text[0] != '"' || text[^1] != '"') throw new ParseFailure("Directive requires a quoted string.");
        try { return Regex.Unescape(text[1..^1]); }
        catch (ArgumentException) { throw new ParseFailure("Invalid directive string escape."); }
    }
}
