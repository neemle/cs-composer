namespace Neemle.CSharpComposer.Parsing;

internal static class Lexer
{
    static readonly string[] compounds = { "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "++", "--",
        "::", "+=", "-=", "*=", "/=", "%=", "?.", "??=", ".." };

    internal static List<Token> Scan(string text)
    {
        var tokens = new List<Token>();
        int p = 0;
        while (p < text.Length)
        {
            if (char.IsWhiteSpace(text[p])) { p++; continue; }
            int start = p;
            bool comment = false, malformed = false;

            if (text.AsSpan(p).StartsWith("//"))
            {
                comment = true;
                while (p < text.Length && text[p] is not ('\r' or '\n')) p++;
            }
            else if (text.AsSpan(p).StartsWith("/*"))
            {
                comment = true;
                int end = text.IndexOf("*/", p + 2, StringComparison.Ordinal);
                malformed = end < 0;
                p = malformed ? text.Length : end + 2;
            }
            else if (IsQuoteStart(text, p))
            {
                p = LiteralEnd(text, p, out malformed);
                if (!malformed && p >= 1 && text[p - 1] == '"' && p + 1 < text.Length && text[p] == 'u' && text[p + 1] == '8') p += 2;
            }
            else if (text[p] == '#')
                while (p < text.Length && text[p] is not ('\r' or '\n')) p++;
            else if (char.IsDigit(text[p])) p = NumberEnd(text, p);
            else if (char.IsLetter(text[p]) || text[p] is '_' or '@' or '\\')
            {
                p++;
                while (p < text.Length && (char.IsLetterOrDigit(text[p]) || text[p] is '_' or '\\')) p++;
            }
            else
            {
                string? match = compounds.Where(c => text.AsSpan(p).StartsWith(c))
                    .OrderByDescending(c => c.Length).FirstOrDefault();
                p += match?.Length ?? 1;
            }
            tokens.Add(new(text[start..p], start, p, comment, malformed));
        }
        tokens.Add(new("", text.Length, text.Length));
        return tokens;
    }

    static int NumberEnd(string text, int p)
    {
        if (p + 1 < text.Length && text[p] == '0' && text[p + 1] is 'x' or 'X' or 'b' or 'B')
        {
            bool hex = text[p + 1] is 'x' or 'X'; p += 2;
            while (p < text.Length && (text[p] == '_' || (hex ? Uri.IsHexDigit(text[p]) : text[p] is '0' or '1'))) p++;
        }
        else
        {
            while (p < text.Length && (char.IsDigit(text[p]) || text[p] == '_')) p++;
            if (p + 1 < text.Length && text[p] == '.' && char.IsDigit(text[p + 1]))
            {
                p++;
                while (p < text.Length && (char.IsDigit(text[p]) || text[p] == '_')) p++;
            }
            if (p < text.Length && text[p] is 'e' or 'E')
            {
                p++;
                if (p < text.Length && text[p] is '+' or '-') p++;
                while (p < text.Length && (char.IsDigit(text[p]) || text[p] == '_')) p++;
            }
        }
        if (p < text.Length && text[p] is 'f' or 'F' or 'd' or 'D' or 'm' or 'M') return p + 1;
        if (p < text.Length && text[p] is 'u' or 'U' or 'l' or 'L')
        {
            char first = char.ToUpperInvariant(text[p++]);
            if (p < text.Length && (first == 'U' && text[p] is 'l' or 'L' || first == 'L' && text[p] is 'u' or 'U')) p++;
        }
        return p;
    }

    static bool IsQuoteStart(string text, int p) => text[p] is '\'' or '"' ||
        text[p] is '@' or '$' && p + 1 < text.Length && text[p + 1] is '"' or '@' or '$';

    static int LiteralEnd(string text, int start, out bool malformed, int depth = 0)
    {
        malformed = false;
        if (depth > 128) { malformed = true; return text.Length; }

        int p = start;
        while (p < text.Length && text[p] is '$' or '@') p++;
        if (p == text.Length || text[p] is not ('"' or '\'')) { malformed = true; return start + 1; }
        bool verbatim = text.AsSpan(start, p - start).Contains('@');
        int dollars = text.AsSpan(start, p - start).Count('$');
        char quote = text[p++];
        int count = 1;
        while (quote == '"' && p < text.Length && text[p] == '"') { count++; p++; }
        if (count == 2) return p;
        int delimiter = count >= 3 ? count : 1;
        while (p < text.Length)
        {
            if (dollars > 0 && text[p] == '{')
            {
                int braces = Run(text, p, '{');
                if (delimiter == 1 && braces % 2 == 0) { p += braces; continue; }

                if (braces >= (delimiter == 1 ? 1 : dollars))
                {
                    p = InterpolationEnd(text, p + braces, delimiter == 1 ? 1 : dollars, out malformed, depth + 1);
                    if (malformed) return p;
                    continue;
                }
            }
            if (delimiter == 1 && !verbatim && text[p] == '\\') { p = Math.Min(p + 2, text.Length); continue; }
            if (text[p] != quote) { p++; continue; }
            int run = Run(text, p, quote);
            if (delimiter > 1) { if (run >= delimiter) return p + run; p += run; continue; }
            p++;
            if (verbatim && p < text.Length && text[p] == quote) { p++; continue; }
            return p;
        }
        malformed = true;
        return p;
    }

    static int Run(string text, int p, char value)
    {
        int start = p;
        while (p < text.Length && text[p] == value) p++;
        return p - start;
    }

    static int InterpolationEnd(string text, int p, int closeCount, out bool malformed, int depth)
    {
        malformed = false;
        if (depth > 128) { malformed = true; return text.Length; }
        int braceDepth = 0;

        while (p < text.Length)
        {
            if (IsQuoteStart(text, p))
            {
                p = LiteralEnd(text, p, out malformed, depth + 1);
                if (malformed) return p;
                continue;
            }
            if (text.AsSpan(p).StartsWith("/*"))
            { int end = text.IndexOf("*/", p + 2, StringComparison.Ordinal); p = end < 0 ? text.Length : end + 2; continue; }
            if (text.AsSpan(p).StartsWith("//"))
            { while (p < text.Length && text[p] is not ('\r' or '\n')) p++; continue; }
            if (text[p] == '}' && braceDepth == 0 && Run(text, p, '}') >= closeCount) return p + closeCount;
            if (text[p] == '{') braceDepth++;
            if (text[p] == '}') braceDepth--;
            p++;
        }
        malformed = true;
        return p;
    }
}
