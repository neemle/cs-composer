using System.Globalization;
using System.Text;

namespace Neemle.CSharpComposer.Rendering;

internal static class IdentifierText
{
    internal static string? Decode(string text)
    {
        if (!text.Contains('\\')) return text;
        var output = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\') { output.Append(text[i]); continue; }
            if (++i >= text.Length || text[i] is not ('u' or 'U')) return null;
            int count = text[i] == 'u' ? 4 : 8;
            if (i + count >= text.Length || !uint.TryParse(text.AsSpan(i + 1, count), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value) || value > 0x10ffff) return null;
            output.Append(value <= 0xffff ? ((char)value).ToString() : char.ConvertFromUtf32((int)value)); i += count;
        }
        return output.ToString();
    }

    internal static bool Start(Rune rune) => rune.Value == '_' || Rune.IsLetter(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.LetterNumber;
    internal static bool Part(Rune rune) => Start(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.DecimalDigitNumber or
        UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format;
}
