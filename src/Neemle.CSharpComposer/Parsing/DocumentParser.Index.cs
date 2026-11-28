namespace Neemle.CSharpComposer.Parsing;

// Source indexes built once per parse, so that placing a comment costs time in proportion to the line breaks around it
// instead of to the whole token list or line (GENE-19, RC-3).
internal sealed partial class DocumentParser
{
    // Comment tokens in source order; comments never overlap, so both Start and End ascend.
    List<Token> comments = new();

    // Positions of every '\r' and '\n', and of every line terminator sequence ("\r\n" counts once).
    List<int> breaks = new();

    List<int> terminators = new();

    // lastText[i] is the position of the last non-whitespace character before i, or -1.
    int[] lastText = Array.Empty<int>();

    void BuildIndex()
    {
        comments = all.Where(t => t.IsComment).ToList();
        string text = source.Text;
        lastText = new int[text.Length + 1];
        int last = -1;
        for (int i = 0; i < text.Length; i++)
        {
            lastText[i] = last;
            char c = text[i];
            if (!char.IsWhiteSpace(c)) last = i;
            if (c is '\r' or '\n')
            {
                breaks.Add(i);
                if (c == '\r' || i == 0 || text[i - 1] != '\r') terminators.Add(i);
            }
        }
        lastText[text.Length] = last;
    }

    // Number of elements of an ascending list that are smaller than `value`.
    static int Below(List<int> values, int value)
    {
        int low = 0, high = values.Count;
        while (low < high) { int middle = (low + high) >> 1; if (values[middle] < value) low = middle + 1; else high = middle; }
        return low;
    }

    bool Blank(int start, int end) => lastText[end] < start;

    // Line terminator sequences in text[start..end], counted as CountLines counts that substring.
    int LinesBetween(int start, int end)
    {
        if (end <= start) return 0;
        // `start` is the end of a token or comment, and neither ends between the CR and LF of one line break.
        return Below(terminators, end) - Below(terminators, start);
    }

    int LineStart(int p)
    {
        int index = Below(breaks, p);
        return index == 0 ? 0 : breaks[index - 1] + 1;
    }

    (int Line, int Column) Position(int offset)
    {
        int index = Below(terminators, offset);
        if (index == 0) return (1, offset + 1);
        int start = terminators[index - 1];
        int lineStart = source.Text[start] == '\r' && start + 1 < offset && source.Text[start + 1] == '\n' ? start + 2 : start + 1;
        return (index + 1, offset - lineStart + 1);
    }
}
