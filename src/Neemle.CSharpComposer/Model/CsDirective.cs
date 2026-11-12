namespace Neemle.CSharpComposer.Model;

public enum DirectiveKind
{
    If, Elif, Else, EndIf, Define, Undef, Region, EndRegion, Error, Warning, Line,
    PragmaWarning, PragmaChecksum, Nullable, Reference, Load, Shebang,
    Sdk, Package, Project, Property, Ignored
}

public enum DirectiveAction { None, Enable, Disable, Restore, Default, Hidden }

public sealed record CsLinePosition(int Line, int Column) : CsNode;

public sealed record CsDirective(DirectiveKind Kind) : CsMember("")
{
    public CsExpression? Condition { get; init; }
    public string? Symbol { get; init; }
    public string? Text { get; init; }
    public string? File { get; init; }
    public string? ChecksumAlgorithm { get; init; }
    public string? Checksum { get; init; }
    public int? Line { get; init; }
    public CsLinePosition? Start { get; init; }
    public CsLinePosition? End { get; init; }
    public int? CharacterOffset { get; init; }
    public DirectiveAction Action { get; init; }
    public Values<string> Warnings { get; init; } = Values<string>.Empty;
}
