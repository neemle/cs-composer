using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed class StatementValidator
{
    readonly Validator validation;
    readonly bool inCatch;
    internal StatementValidator(Validator validation, bool inCatch)
    {
        this.validation = validation;
        this.inCatch = inCatch;
    }

    internal void Validate(CsStatement statement, string path, int depth)
    {
        if (!validation.Depth(depth, path)) return;
        if (!Enum.IsDefined(typeof(StatementKind), statement.Kind))
            validation.Error("GEN023", path, "Unknown statement kind.");
        Payload(statement, path);
        switch (statement.Kind)
        {
            case StatementKind.Local: Local(statement, path, depth); break;
            case StatementKind.Block: Block(statement.Body, path + ".Body", depth + 1, inCatch); break;
            case StatementKind.If: If(statement, path, depth); break;
            case StatementKind.Try: Try(statement, path, depth); break;
            case StatementKind.Using: Using(statement, path, depth); break;
            case StatementKind.Rethrow: Rethrow(path); break;
            default: Expression(statement, path, depth); break;
        }
    }

    void Expression(CsStatement statement, string path, int depth)
    {
        if (statement.Kind == StatementKind.Expression || statement.Kind == StatementKind.Throw ||
            statement.Expression != null)
            validation.Expression(statement.Expression, path + ".Expression", depth + 1);
    }

    void Local(CsStatement statement, string path, int depth)
    {
        validation.Identifier(statement.Text, path + ".Text");
        if (!statement.IsVar) validation.Type(statement.Type, path + ".Type", depth + 1);
        if (statement.IsVar && statement.Expression == null)
            validation.Error("GEN067", path + ".Expression", "A var declaration requires an initializer.");
        if (statement.Expression != null) Expression(statement, path, depth);
    }

    void Block(CsBody? body, string path, int depth, bool catchContext)
    {
        if (body?.Expression != null)
            validation.Error("GEN066", path, "Embedded blocks cannot use an expression body.");
        validation.Body(body, path, depth, catchContext);
    }

    void If(CsStatement statement, string path, int depth)
    {
        validation.Expression(statement.Expression, path + ".Expression", depth + 1);
        Block(statement.Body, path + ".Body", depth + 1, inCatch);
        if (statement.Else == null) return;
        if (statement.Else.Kind != StatementKind.Block && statement.Else.Kind != StatementKind.If)
            validation.Error("GEN065", path + ".Else", "Else must be a block or an if statement.");
        Validate(statement.Else, path + ".Else", depth + 1);
    }

    void Try(CsStatement statement, string path, int depth)
    {
        Block(statement.Body, path + ".Body", depth + 1, inCatch);
        if (statement.Catches?.Count == 0 && statement.Finally == null)
            validation.Error("GEN068", path, "Try requires a catch or finally block.");
        bool catchAll = false;
        validation.List(statement.Catches, path + ".Catches", (handler, child) =>
        {
            if (catchAll || handler.Type == null && handler.Name != null)
                validation.Error("GEN069", child, "Catch-all must be unnamed, unique and last.");
            catchAll = handler.Type == null;
            if (handler.Type != null) validation.Type(handler.Type, child + ".Type", depth + 1);
            if (handler.Name != null) validation.Identifier(handler.Name, child + ".Name");
            Block(handler.Body, child + ".Body", depth + 1, true);
        });
        if (statement.Finally != null) Block(statement.Finally, path + ".Finally", depth + 1, false);
    }

    void Using(CsStatement statement, string path, int depth)
    {
        if ((statement.Expression == null) == (statement.Resource == null))
            validation.Error("GEN071", path, "Using requires exactly one resource expression or declaration.");
        if (statement.Expression != null) Expression(statement, path, depth);
        if (statement.Resource != null)
        {
            if (statement.Resource.Kind != StatementKind.Local || statement.Resource.Expression == null)
                validation.Error("GEN071", path + ".Resource", "Using requires an initialized local declaration.");
            Validate(statement.Resource, path + ".Resource", depth + 1);
        }
        Block(statement.Body, path + ".Body", depth + 1, inCatch);
    }

    void Rethrow(string path)
    {
        if (!inCatch) validation.Error("GEN070", path, "Rethrow requires an enclosing catch outside finally.");
    }

    void Payload(CsStatement statement, string path)
    {
        bool block = statement.Kind == StatementKind.Block || statement.Kind == StatementKind.If ||
            statement.Kind == StatementKind.Try || statement.Kind == StatementKind.Using;
        bool invalid = statement.Body != null && !block || statement.Type != null &&
            (statement.Kind != StatementKind.Local || statement.IsVar) ||
            statement.IsVar && statement.Kind != StatementKind.Local;
        invalid |= statement.Else != null && statement.Kind != StatementKind.If ||
            (statement.Finally != null || statement.Catches?.Count > 0) && statement.Kind != StatementKind.Try ||
            statement.Resource != null && statement.Kind != StatementKind.Using;
        invalid |= statement.Expression != null && (statement.Kind == StatementKind.Block ||
            statement.Kind == StatementKind.Try || statement.Kind == StatementKind.Rethrow);
        invalid |= statement.Kind > StatementKind.Raw && statement.Kind != StatementKind.Local && statement.Text != "";
        if (invalid) validation.Error("GEN065", path, "Payload does not belong to this node kind.");
        if (statement.Catches == null && statement.Kind != StatementKind.Try)
            validation.Required(null, path + ".Catches");
    }
}
