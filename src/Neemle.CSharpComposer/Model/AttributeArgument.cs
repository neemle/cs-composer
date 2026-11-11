using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record AttributeArgument(CsExpression Value, string? Name = null, bool IsConstructorName = false);
