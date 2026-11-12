namespace Neemle.CSharpComposer.Model;

public sealed record CsLambdaParameter(string Name, CsType? Type = null, string? Modifier = null) : CsNode;
