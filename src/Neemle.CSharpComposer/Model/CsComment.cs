namespace Neemle.CSharpComposer.Model;

public sealed record CsComment(string Text, CommentKind Kind = CommentKind.Line,
    CommentPlacement Placement = CommentPlacement.Leading) : CsNode;
public enum CommentKind { Line, Block, Documentation }
public enum CommentPlacement { Leading, Trailing, Container }
