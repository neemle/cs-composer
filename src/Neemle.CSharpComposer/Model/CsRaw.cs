using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Model;
public sealed record CsRaw(string Text) : CsNode
{
    public ParseResult<CsNode> Parse(ParseContext context) => FragmentParser.Parse(this, context);
}
