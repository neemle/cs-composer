namespace Neemle.CSharpComposer.Model;

public enum XmlNodeKind { Fragment, Element, EmptyElement, Text, CData, Comment, ProcessingInstruction }
public enum CrefKind { Name, Indexer, Operator, Conversion, Extension }

public sealed record CsXmlNode(XmlNodeKind Kind, string? Name = null, string? Value = null) : CsNode
{
    public Values<CsXmlAttribute> Attributes { get; init; } = Values<CsXmlAttribute>.Empty;
    public Values<CsXmlNode> Content { get; init; } = Values<CsXmlNode>.Empty;
}

public sealed record CsXmlAttribute(string Name, string? Value = null, CsCref? Reference = null) : CsNode;

public sealed record CsCref(CrefKind Kind) : CsNode
{
    public CsType? Type { get; init; }
    public CsType? Qualifier { get; init; }
    public OverloadableOperator Operator { get; init; }
    public bool IsChecked { get; init; }
    public Values<CsType> TypeArguments { get; init; } = Values<CsType>.Empty;
    public Values<CsFunctionPointerParameter>? Parameters { get; init; }
    public CsCref? Member { get; init; }
}
