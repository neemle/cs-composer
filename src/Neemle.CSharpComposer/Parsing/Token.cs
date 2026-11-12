namespace Neemle.CSharpComposer.Parsing;

internal readonly record struct Token(string Text, int Start, int End, bool IsComment = false,
    bool IsMalformed = false, bool IsDisabled = false);
