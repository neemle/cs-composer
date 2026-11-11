using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsStatement(StatementKind Kind, CsExpression? Expression = null, string Text = "");
