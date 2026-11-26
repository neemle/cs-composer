namespace Neemle.CSharpComposer.Parsing;

// Source indexes built once per parse, so that binding a node costs time in proportion to the comments
// and line breaks around it instead of to the whole token list or line (GENE-19, RC-3).
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

    // Index of the first comment that starts at or after `offset`.
    int FirstCommentFrom(int offset)
    {
        int low = 0, high = comments.Count;
        while (low < high) { int middle = (low + high) >> 1; if (comments[middle].Start < offset) low = middle + 1; else high = middle; }
        return low;
    }

    // Comment tokens with start >= `start` and end <= `end`, in source order, excluding directive trivia comments.
    IEnumerable<Token> CommentsWithin(int start, int end)
    {
        for (int i = FirstCommentFrom(start); i < comments.Count && comments[i].Start < end; i++)
            if (comments[i].End <= end && !triviaComments.Contains((comments[i].Start, comments[i].End))) yield return comments[i];
    }

    // Every comment token with start >= `start` and end <= `end`, directive trivia comments included.
    IEnumerable<Token> CommentsIn(int start, int end)
    {
        for (int i = FirstCommentFrom(start); i < comments.Count && comments[i].Start < end; i++)
            if (comments[i].End <= end) yield return comments[i];
    }

    // Comment tokens that end at or before `offset`, nearest first.
    IEnumerable<Token> CommentsBefore(int offset)
    {
        int low = 0, high = comments.Count;
        while (low < high) { int middle = (low + high) >> 1; if (comments[middle].End <= offset) low = middle + 1; else high = middle; }
        for (int i = low - 1; i >= 0; i--) yield return comments[i];
    }

    // Comment tokens that start at or after `offset`, in source order.
    IEnumerable<Token> CommentsFrom(int offset)
    {
        for (int i = FirstCommentFrom(offset); i < comments.Count; i++) yield return comments[i];
    }

    bool Blank(int start, int end) => lastText[end] < start;

    // Line terminator sequences in text[start..end], counted as CountLines counts that substring.
    int LinesBetween(int start, int end)
    {
        if (end <= start) return 0;
        int count = Below(terminators, end) - Below(terminators, start);
        return source.Text[start] == '\n' && start > 0 && source.Text[start - 1] == '\r' ? count + 1 : count;
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
