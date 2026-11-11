using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public sealed class ConstructorBuilder<TParent> : MemberBuilder<ConstructorBuilder<TParent>>
{
    readonly TParent parent;
    readonly Action<CsConstructor> add;
    readonly string name;
    readonly List<CsParameter> parameters = new List<CsParameter>();
    CsBody body = new CsBody();
    string? initializerKind;
    Values<CsExpression> arguments = Values<CsExpression>.Empty;
    internal ConstructorBuilder(TParent parent, Action<CsConstructor> add, string name)
    {
        this.parent = parent;
        this.add = add;
        this.name = name;
    }

    public ConstructorBuilder<TParent> Parameter(CsType type, string name, string? modifier = null)
    {
        parameters.Add(new CsParameter(type, name) { Modifier = modifier });
        return this;
    }

    public ConstructorBuilder<TParent> Parameter(CsParameter parameter)
    {
        parameters.Add(parameter);
        return this;
    }

    public ConstructorBuilder<TParent> Base(params CsExpression[] args)
    {
        initializerKind = "base";
        arguments = args;
        return this;
    }

    public ConstructorBuilder<TParent> This(params CsExpression[] args)
    {
        initializerKind = "this";
        arguments = args;
        return this;
    }

    public ConstructorBuilder<TParent> Body(params CsStatement[] statements)
    {
        body = new CsBody(statements);
        return this;
    }

    public ConstructorBuilder<TParent> RawBody(string source)
    {
        body = CsBody.FromRaw(source);
        return this;
    }

    public CsConstructor Build() => new CsConstructor(name)
    {
        Modifiers = FrozenModifiers,
        Attributes = FrozenAttributes,
        Parameters = new Values<CsParameter>(parameters),
        InitializerKind = initializerKind,
        InitializerArguments = arguments,
        Body = body
    };
    public TParent End()
    {
        add(Build());
        return parent;
    }
}
