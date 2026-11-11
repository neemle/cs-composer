namespace Neemle.CSharpComposer.Model;
public sealed record CsNamespace(string Name, Values<CsNode> Items, bool IsFileScoped = false) : CsNode;
