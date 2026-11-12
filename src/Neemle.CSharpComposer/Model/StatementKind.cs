using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public enum StatementKind
{
    Expression,
    Return,
    Raw,
    Local,
    Block,
    If,
    Try,
    Throw,
    Rethrow,
    Using,
    Switch,
    Break,
    GotoCase,
    GotoDefault,
    For,
    ForEach,
    While,
    DoWhile,
    Continue,
    Empty,
    UsingDeclaration
}
