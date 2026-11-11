using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public enum ExpressionKind
{
    Identifier,
    Literal,
    Member,
    Invocation,
    Await,
    Assignment,
    Raw,
    Binary,
    Unary
}
