using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsUsing(string Name, string? Alias = null, bool IsStatic = false, bool IsGlobal = false) : CsNode;
