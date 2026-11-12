using System.Globalization;
using System.Text;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsExpression InterpolatedString(string token, int start, int depth)
    {
        Guard(depth);
        int quote = token.IndexOf('"'); int dollars = token[..quote].Count(c => c == '$');
        bool verbatim = token[..quote].Contains('@'); int quotes = 1;
        while (quote + quotes < token.Length && token[quote + quotes] == '"') quotes++;
        bool raw = !verbatim && quotes >= 3;
        if (!raw) quotes = 1;
        string contents = token[(quote + quotes)..^quotes];
        if (raw) contents = RawContents(contents);
        int delimiter = raw ? dollars : 1;
        var parts = new List<CsExpression>(); var text = new StringBuilder(); int position = 0;
        void Flush()
        {
            if (text.Length == 0) return;
            string value = raw ? text.ToString() : DecodeStringText(text.ToString().Replace("{{", "{").Replace("}}", "}"), verbatim);
            parts.Add(new(ExpressionKind.StringText, value, Values<CsExpression>.Empty)); text.Clear();
        }
        while (position < contents.Length)
        {
            if (!raw && contents[position] == '\\' && !verbatim)
            { text.Append(contents[position++]); if (position < contents.Length) text.Append(contents[position++]); continue; }
            if (!raw && contents.AsSpan(position).StartsWith("{{")) { text.Append("{{"); position += 2; continue; }
            if (contents[position] != '{') { text.Append(contents[position++]); continue; }
            int run = 0; while (position + run < contents.Length && contents[position + run] == '{') run++;
            if (run < delimiter) { text.Append('{', run); position += run; continue; }
            text.Append('{', run - delimiter); position += run; Flush();
            int end = Lexer.InterpolationEnd(contents, position, delimiter, out bool malformed, depth + 1);
            if (malformed) throw new ParseFailure("Unterminated interpolation.");
            string hole = contents[position..(end - delimiter)];
            if (Lexer.Scan(hole).Any(t => !t.IsComment && t.Text.StartsWith('#'))) throw new ParseFailure("Directives cannot appear inside an interpolation.");
            var snapshot = SourceSnapshot.Decode(Encoding.UTF8.GetBytes(hole), null);
            var parser = new DocumentParser(snapshot, version) { inAsync = inAsync, inAccessor = inAccessor };
            var expression = (CsExpression)parser.Expression(0, depth + 1).Detach();
            CsExpression? alignment = parser.Take(",") ? (CsExpression)parser.Expression(0, depth + 1).Detach() : null;
            string? format = null;
            if (parser.Current.Text == ":")
            {
                string formatText = hole[parser.Current.End..];
                format = raw ? formatText : DecodeStringText(formatText.Replace("{{", "{").Replace("}}", "}"), verbatim);
            }
            else if (parser.Current.Text != "") throw new ParseFailure("Unexpected interpolation contents.");
            parts.Add(new(ExpressionKind.Interpolation, "", new[] { expression }) { Alignment = alignment, Format = format });
            position = end;
        }
        Flush();
        return Bind(new CsExpression(ExpressionKind.InterpolatedString, "", new(parts)) { StringStyle = raw ? CsStringStyle.Raw : verbatim ? CsStringStyle.Verbatim : CsStringStyle.Regular }, start);
    }

    static string RawContents(string text)
    {
        int firstNewline = text.StartsWith("\r\n", StringComparison.Ordinal) ? 2 : text.StartsWith('\n') || text.StartsWith('\r') ? 1 : 0;
        if (firstNewline == 0) return text;
        int lastNewline = Math.Max(text.LastIndexOf('\n'), text.LastIndexOf('\r'));
        if (lastNewline < 0) throw new ParseFailure("Multiline raw string requires a closing delimiter line.");
        string indent = text[(lastNewline + 1)..];
        if (indent.Any(c => c is not (' ' or '\t'))) throw new ParseFailure("Raw string closing delimiter must occupy its own line.");
        int end = lastNewline > 0 && text[lastNewline] == '\n' && text[lastNewline - 1] == '\r' ? lastNewline - 1 : lastNewline;
        if (end < firstNewline) return "";
        string body = text[firstNewline..end];
        var output = new StringBuilder(); bool lineStart = true;
        for (int index = 0; index < body.Length; index++)
        {
            if (lineStart && indent.Length > 0)
            {
                int removed = 0;
                while (removed < indent.Length && index < body.Length && body[index] == indent[removed]) { index++; removed++; }
                if (index >= body.Length) break;
                if (removed != indent.Length && body[index] is not ('\r' or '\n')) throw new ParseFailure("Raw string indentation is inconsistent.");
            }
            output.Append(body[index]); lineStart = body[index] is '\r' or '\n';
        }
        return output.ToString();
    }

    static string DecodeStringText(string text, bool verbatim)
    {
        if (verbatim) return text.Replace("\"\"", "\"");
        var output = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i]; if (c != '\\') { output.Append(c); continue; }
            if (++i == text.Length) throw new ParseFailure("Incomplete string escape.");
            c = text[i];
            if (c is 'u' or 'U' or 'x')
            {
                int required = c == 'u' ? 4 : c == 'U' ? 8 : 1; int maximum = c == 'U' ? 8 : 4;
                int begin = i + 1, count = 0;
                while (count < maximum && begin + count < text.Length && Uri.IsHexDigit(text[begin + count])) count++;
                if (count < required) throw new ParseFailure("Invalid Unicode string escape.");
                if (c != 'x') count = required;
                if (!uint.TryParse(text.AsSpan(begin, count), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value) || value > 0x10ffff) throw new ParseFailure("Invalid Unicode code point.");
                output.Append(value <= 0xffff ? ((char)value).ToString() : char.ConvertFromUtf32((int)value)); i += count; continue;
            }
            output.Append(c switch { '\\' => '\\', '"' => '"', '\'' => '\'', '0' => '\0', 'a' => '\a', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', 'v' => '\v', _ => throw new ParseFailure("Invalid string escape.") });
        }
        return output.ToString();
    }
}
