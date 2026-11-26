namespace Neemle.CSharpComposer.Parsing;
// `Kept` marks valid source that is kept as raw source (PARSE002) rather than malformed source (PARSE001).
internal sealed class ParseFailure(string message, bool kept = false) : Exception(message)
{
    internal bool Kept => kept;
}

// A node would sit deeper than Limits.MaxNestingDepth. No speculative parse catches it: it unwinds to
// the nearest recovery site that can keep the construct as raw source (GENE-19).
internal sealed class NestingFailure() : Exception(DocumentParser.NestingMessage);
