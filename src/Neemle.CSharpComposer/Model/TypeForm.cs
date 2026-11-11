using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public enum TypeForm
{
    Named,
    Generic,
    Nullable,
    Array,
    Tuple,
    Pointer,
    Parameter,
    Raw,
    Nested
}
