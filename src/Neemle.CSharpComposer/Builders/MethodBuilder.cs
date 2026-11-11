using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public sealed class MethodBuilder<TParent> : MemberBuilder<MethodBuilder<TParent>>
{
    readonly TParent parent;
    readonly Action<CsMethod> add;
    readonly CsType type;
    readonly string name;
    readonly List<CsParameter> parameters = new List<CsParameter>();
    readonly List<TypeParameter> generics = new List<TypeParameter>();
    CsBody? body;
    internal MethodBuilder(TParent parent, Action<CsMethod> add, CsType type, string name)
    {
        this.parent = parent;
        this.add = add;
        this.type = type;
        this.name = name;
    }

    public MethodBuilder<TParent> Parameter(
        CsType type, string name, string? modifier = null, CsExpression? @default = null) =>
        Parameter(new CsParameter(type, name) { Modifier = modifier, Default = @default });
    public MethodBuilder<TParent> Parameter<T>(
        string name, string? modifier = null, CsExpression? @default = null) =>
        Parameter(Cs.Type<T>(), name, modifier, @default);
    public MethodBuilder<TParent> Parameter(CsParameter parameter)
    {
        parameters.Add(parameter);
        return this;
    }

    public MethodBuilder<TParent> Generic(string name, params CsConstraint[] constraints)
    {
        generics.Add(new TypeParameter(name) { Constraints = constraints });
        return this;
    }

    public MethodBuilder<TParent> Body(CsBody value)
    {
        body = value;
        return this;
    }

    public MethodBuilder<TParent> Body(params CsStatement[] statements) => Body(new CsBody(statements));
    public MethodBuilder<TParent> RawBody(string source) => Body(CsBody.FromRaw(source));
    public MethodBuilder<TParent> Expression(CsExpression value) => Body(CsBody.FromExpression(value));
    public CsMethod Build() => new CsMethod(type, name)
    {
        Modifiers = FrozenModifiers,
        Attributes = FrozenAttributes,
        Parameters = new Values<CsParameter>(parameters),
        TypeParameters = new Values<TypeParameter>(generics),
        Body = body
    };
    public TParent End()
    {
        add(Build());
        return parent;
    }
}
