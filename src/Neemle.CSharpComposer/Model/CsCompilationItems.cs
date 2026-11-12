namespace Neemle.CSharpComposer.Model;

public sealed record CsExternAlias(string Name) : CsNode;

public sealed record CsGlobalStatement(CsNode Value) : CsNode;

/// <summary>Text excluded by conditional compilation; it is not executable C#.</summary>
public sealed record CsDisabledText(string Text) : CsMember("");
