namespace Neemle.CSharpComposer.Model;

public enum InstructionKind { Block, Return, Throw, Break, Continue, Empty, If, For, ForEach, While, DoWhile }

public sealed record CsInstruction(InstructionKind Kind, CsInstructionOptions Options) : CsNode;

public sealed record CsInstructionOptions : CsNode
{
    public CsNode? Value { get; init; }
    public CsNode? Condition { get; init; }
    public CsNode? Initializer { get; init; }
    public CsSymbol? Iterators { get; init; }
    public CsSymbol? Symbol { get; init; }
    public CsTree? Body { get; init; }
    public CsTree? Else { get; init; }
    public bool Await { get; init; }
}
