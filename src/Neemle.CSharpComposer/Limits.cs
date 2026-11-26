namespace Neemle.CSharpComposer;

internal static class Limits
{
    // The one nesting limit. A node's depth is the number of Children edges from the root of the graph
    // (the document, or the node a fragment parse returns); every node, comments and trivia included,
    // must be at most this deep. The parser, both validators, edits and recursive helpers read it.
    internal const int MaxNestingDepth = 128;
}
