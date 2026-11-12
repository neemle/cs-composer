namespace Neemle.CSharpComposer.Model;

// A null pattern denotes the default label; it is distinct from a discard pattern.
public sealed record CsSwitchLabel(CsPattern? Pattern = null, CsExpression? When = null) : CsNode;

public sealed record CsSwitchSection(Values<CsSwitchLabel> Labels, CsBody Body) : CsNode;

public sealed record CsSwitchArm(CsPattern Pattern, CsExpression Value, CsExpression? When = null) : CsNode;
