namespace Neemle.CSharpComposer.Model;

public enum FunctionPointerConvention { Default, Managed, Unmanaged }

public sealed record CsFunctionPointerParameter(CsType Type, string? Modifier = null) : CsNode;
