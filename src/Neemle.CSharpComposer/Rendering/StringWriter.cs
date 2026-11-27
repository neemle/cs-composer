using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class StringWriter
{
    internal static string Literal(string text, CsStringStyle style) => style switch
    {
        CsStringStyle.Regular => Cs.LiteralText(text),
        CsStringStyle.Verbatim => "@\"" + text.Replace("\"", "\"\"") + "\"",
        CsStringStyle.Raw => Raw(text, "", 3),
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };

    static int Run(string text, char character)
    {
        int run = 0, longest = 0;
        foreach (char c in text) { run = c == character ? run + 1 : 0; longest = Math.Max(longest, run); }
        return longest;
    }

    static string Raw(string text, string dollars, int quotes)
    {
        string delimiter = new('"', Math.Max(quotes, Run(text, '"') + 1));
        bool multiline = text.Length == 0 || text.StartsWith('"') || text.EndsWith('"') || text.IndexOfAny(new[] { '\r', '\n' }) >= 0;
        return dollars + delimiter + (multiline ? "\n" : "") + text + (multiline ? "\n" : "") + delimiter;
    }

    // The value in an interpolation hole; a conditional's `:` would start the format there.
    internal static string Hole(CsExpression value) =>
        value.Kind == ExpressionKind.Conditional ? "(" + ExpressionWriter.Write(value) + ")" : ExpressionWriter.Write(value);

    // Text or a format as it is written inside an interpolated string of `style`. A raw string's delimiters depend on
    // its contents, so raw text is given only when it holds nothing that could reach them (a quote, a brace) and no
    // line break: null otherwise.
    internal static string? Segment(string text, CsStringStyle style) => style switch
    {
        CsStringStyle.Raw => text.IndexOfAny(['"', '{', '}', '\r', '\n']) < 0 ? text : null,
        CsStringStyle.Verbatim => text.Replace("\"", "\"\"").Replace("{", "{{").Replace("}", "}}"),
        _ => Cs.LiteralText(text)[1..^1].Replace("{", "{{").Replace("}", "}}")
    };

    internal static string Interpolated(CsExpression e)
    {
        int braces = e.StringStyle == CsStringStyle.Raw ? Math.Max(1, e.Arguments.Select(a => a.Kind == ExpressionKind.StringText ? a.Text : a.Format ?? "").Select(t => Math.Max(Run(t, '{'), Run(t, '}')) + 1).DefaultIfEmpty(1).Max()) : 1;
        var output = new System.Text.StringBuilder();
        string Escape(string text) => e.StringStyle == CsStringStyle.Raw ? text : Segment(text, e.StringStyle)!;
        foreach (var part in e.Arguments)
        {
            if (part.Kind == ExpressionKind.StringText) { output.Append(Escape(part.Text)); continue; }
            output.Append(new string('{', braces)).Append(Hole(part.Arguments[0]));
            if (part.Alignment != null) output.Append(", ").Append(ExpressionWriter.Write(part.Alignment));
            if (part.Format != null) output.Append(':').Append(Escape(part.Format));
            output.Append(new string('}', braces));
        }
        string contents = output.ToString();
        if (e.StringStyle == CsStringStyle.Raw) return Raw(contents, new string('$', braces), 3);
        return (e.StringStyle == CsStringStyle.Verbatim ? "$@\"" : "$\"") + contents + "\"";
    }
}
