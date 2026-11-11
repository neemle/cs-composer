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
}
