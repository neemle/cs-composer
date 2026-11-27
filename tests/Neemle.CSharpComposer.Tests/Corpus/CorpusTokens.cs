namespace Neemle.CSharpComposer.Tests;

// GENE-29 AD 84939297: the gate's own small C# tokenizer. Since F1 it only finds the comment tokens of source text and
// of opaque and inactive text for OWN (CorpusComments). It is deliberately independent of the product lexer.
// Whitespace is dropped; comments and directive lines are tokens marked as trivia; a whole string literal (regular,
// verbatim, interpolated, raw, u8) is one token; punctuation is one character per token.
internal static class CorpusTokens
{
    internal readonly record struct Token(string Text, bool Trivia);

    internal static List<Token> Scan(string s)
    {
        var tokens = new List<Token>();
        int i = 0, n = s.Length;
        bool lineStart = true;
        while (i < n)
        {
            char c = s[i];
            if (c is '\n' or '\r') { lineStart = true; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            int start = i;
            bool trivia = false;
            if (c == '#' && lineStart) { i = LineEnd(s, i); trivia = true; }
            else if (c == '/' && At(s, i + 1) == '/') { i = LineEnd(s, i); trivia = true; }
            else if (c == '/' && At(s, i + 1) == '*')
            {
                int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? n : close + 2;
                trivia = true;
            }
            else if (StringStart(s, i)) i = SkipString(s, i);
            else if (c == '\'') i = SkipChar(s, i);
            else if (IdentifierStart(c) || c == '@' && IdentifierStart(At(s, i + 1)))
            {
                i++;
                while (i < n && IdentifierPart(s[i])) i++;
            }
            else if (char.IsAsciiDigit(c) || c == '.' && char.IsAsciiDigit(At(s, i + 1))) i = SkipNumber(s, i);
            else i++;
            lineStart = false;
            string text = s[start..i];
            tokens.Add(new(trivia ? text.TrimEnd() : text, trivia));
        }
        return tokens;
    }

    static char At(string s, int i) => i < s.Length ? s[i] : '\0';

    static int LineEnd(string s, int i)
    {
        while (i < s.Length && s[i] is not ('\n' or '\r')) i++;
        return i;
    }

    static bool IdentifierStart(char c) => char.IsLetter(c) || c == '_';

    static bool IdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    static bool StringStart(string s, int i)
    {
        int j = i;
        bool verbatim = false;
        while (j < s.Length && (s[j] == '$' || s[j] == '@' && !verbatim))
        {
            if (s[j] == '@') verbatim = true;
            j++;
        }
        return At(s, j) == '"';
    }

    static int SkipString(string s, int i)
    {
        int dollars = 0;
        bool verbatim = false;
        while (s[i] != '"')
        {
            if (s[i] == '$') dollars++; else verbatim = true;
            i++;
        }
        int quotes = 0;
        while (At(s, i + quotes) == '"') quotes++;
        int end;
        if (quotes == 2) end = i + 2;
        else if (quotes >= 3) end = SkipRaw(s, i + quotes, quotes, dollars);
        else end = SkipQuoted(s, i + 1, verbatim, dollars > 0);
        if (At(s, end) is 'u' or 'U' && At(s, end + 1) == '8') end += 2;
        return end;
    }

    static int SkipQuoted(string s, int i, bool verbatim, bool interpolated)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '"')
            {
                if (verbatim && At(s, i + 1) == '"') { i += 2; continue; }
                return i + 1;
            }
            if (!verbatim && c == '\\') { i += 2; continue; }
            if (!verbatim && !interpolated && c is '\n' or '\r') return i;
            if (interpolated && c == '{')
            {
                if (At(s, i + 1) == '{') { i += 2; continue; }
                i = SkipHole(s, i + 1);
                continue;
            }
            if (interpolated && c == '}' && At(s, i + 1) == '}') { i += 2; continue; }
            i++;
        }
        return s.Length;
    }

    static int SkipRaw(string s, int i, int quotes, int dollars)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '"')
            {
                int run = 0;
                while (At(s, i + run) == '"') run++;
                if (run >= quotes) return i + run;
                i += run;
                continue;
            }
            if (dollars > 0 && c == '{')
            {
                int run = 0;
                while (At(s, i + run) == '{') run++;
                if (run < dollars) { i += run; continue; }
                i = SkipHole(s, i + run);
                for (int extra = 1; extra < dollars && At(s, i) == '}'; extra++) i++;
                continue;
            }
            i++;
        }
        return s.Length;
    }

    // Skips an interpolation hole's code up to and including its closing brace.
    static int SkipHole(string s, int i)
    {
        int depth = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (StringStart(s, i)) { i = SkipString(s, i); continue; }
            if (c == '\'') { i = SkipChar(s, i); continue; }
            if (c == '/' && At(s, i + 1) == '*')
            {
                int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? s.Length : close + 2;
                continue;
            }
            if (c is '{' or '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == '}')
            {
                if (depth <= 0) return i + 1;
                depth--;
            }
            i++;
        }
        return s.Length;
    }

    static int SkipChar(string s, int i)
    {
        i++;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\') { i += 2; continue; }
            if (c == '\'') return i + 1;
            if (c is '\n' or '\r') return i;
            i++;
        }
        return s.Length;
    }

    static int SkipNumber(string s, int i)
    {
        bool hex = s[i] == '0' && At(s, i + 1) is 'x' or 'X' or 'b' or 'B';
        i++;
        while (i < s.Length)
        {
            char c = s[i];
            if (IdentifierPart(c) || c == '.' && char.IsAsciiDigit(At(s, i + 1)) ||
                c is '+' or '-' && !hex && s[i - 1] is 'e' or 'E') i++;
            else break;
        }
        return i;
    }
}
