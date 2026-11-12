using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsType : CsNode
{
    public static CsType Int32 => Cs.Type<int>();

    public static CsType String => Cs.Type<string>();

    public static CsType Boolean => Cs.Type<bool>();

    public static CsType Void => Cs.Type(typeof(void));

    public TypeForm Form { get; init; }

    public string Name { get; init; }

    public CsType? Element { get; init; }

    public Values<CsType> Arguments { get; init; } = Values<CsType>.Empty;

    public Values<TupleElement> Elements { get; init; } = Values<TupleElement>.Empty;

    public int Rank { get; init; } = 1;

    public CsType(string name)
    {
        Name = name;
    }

    public CsType Generic(params CsType[] arguments) => new CsType("")
    {
        Form = TypeForm.Generic,
        Element = this,
        Arguments = arguments
    };

    public CsType Nullable() => Form == TypeForm.Nullable ? this : new CsType("")
    {
        Form = TypeForm.Nullable,
        Element = this
    };

    public CsType Array(int rank = 1) => new CsType("")
    {
        Form = TypeForm.Array,
        Element = this,
        Rank = rank
    };

    public CsType Pointer() => new CsType("")
    {
        Form = TypeForm.Pointer,
        Element = this
    };

    public static implicit operator CsType(string name) => new CsType(name);
}
