using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Rendering;
public sealed record RenderOptions
{
    public string Indent { get; init; } = "    ";

    public NamespaceStyle NamespaceStyle { get; init; } = NamespaceStyle.FileScoped;

    public NullableMode? Nullable { get; init; }

    public bool SortUsings { get; init; } = true;

    public bool Validate { get; init; } = true;

    // GENE-29 F2: the line break written between lines, "\n" or "\r\n". Null keeps a parsed document's own line breaks
    // and writes "\n" for a built one.
    public string? Newline { get; init; }

    // GENE-29 F2: the line width the formatter breaks lists, chains and logical operator chains to fit; 0 or less
    // writes every breakable group on one line unless a comment breaks it.
    public int MaxLineLength { get; init; } = 120;
}
