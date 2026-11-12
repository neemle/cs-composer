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
    DisabledText, Directive, LocalGroup, UsingDeclaration, Lock, Fixed, Unsafe, Checked, Unchecked, YieldReturn, YieldBreak, Goto, Label, LocalFunction
}
