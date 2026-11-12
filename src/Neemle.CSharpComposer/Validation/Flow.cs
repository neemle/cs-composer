using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

// Transfers remain distinct until their enclosing loop/switch consumes them.
// Unknown syntax is conservative: it never proves that a section falls through.
internal static class Flow
{
    [Flags]
    enum Exit { None = 0, Normal = 1, Break = 2, Continue = 4, Abrupt = 8, Unknown = 16 }

    internal static bool? Reachable(CsBody? body, int depth = 0)
    {
        var exits = Body(body, depth);
        return (exits & Exit.Unknown) != 0 ? null : (exits & Exit.Normal) != 0;
    }

    static Exit Body(CsBody? body, int depth)
    {
        if (body == null || body.Raw != null || body.Statements == null || depth > 128) return Exit.Unknown;
        var exits = Exit.Normal;
        foreach (var statement in body.Statements)
        {
            if ((exits & Exit.Normal) == 0) break;
            exits = (exits & ~Exit.Normal) | Statement(statement, depth + 1);
        }
        return exits;
    }

    static Exit Statement(CsStatement? s, int depth)
    {
        if (s == null || depth > 128) return Exit.Unknown;
        switch (s.Kind)
        {
            case StatementKind.Return:
            case StatementKind.Throw:
            case StatementKind.Rethrow:
            case StatementKind.GotoCase:
            case StatementKind.GotoDefault: return Exit.Abrupt;
            case StatementKind.Break: return Exit.Break;
            case StatementKind.Continue: return Exit.Continue;
            case StatementKind.Block: case StatementKind.Using: return Body(s.Body, depth + 1);
            case StatementKind.If:
                var then = Body(s.Body, depth + 1);
                var alternative = s.Else == null ? Exit.Normal : Statement(s.Else, depth + 1);
                if (Constant(s.Expression, true)) return then;
                if (Constant(s.Expression, false)) return alternative;
                return then | alternative;
            case StatementKind.Try:
                var paths = Body(s.Body, depth + 1);
                if (s.Catches == null) paths |= Exit.Unknown;
                else foreach (var handler in s.Catches) paths |= Body(handler?.Body, depth + 1);
                if (s.Finally == null) return paths;
                var final = Body(s.Finally, depth + 1);
                return (final & ~Exit.Normal) | ((final & Exit.Normal) != 0 ? paths : Exit.None);
            case StatementKind.For:
            case StatementKind.ForEach:
            case StatementKind.While:
            case StatementKind.DoWhile:
                return Loop(s, depth + 1);
            case StatementKind.Raw: case StatementKind.Switch: return Exit.Unknown;
            default: return Exit.Normal;
        }
    }

    static Exit Loop(CsStatement statement, int depth)
    {
        bool pretest = statement.Kind is StatementKind.For or StatementKind.While;
        if (pretest && Constant(statement.Expression, false)) return Exit.Normal;
        var body = Body(statement.Body, depth + 1);
        var exits = body & (Exit.Abrupt | Exit.Unknown);
        bool always = Constant(statement.Expression, true) || statement.Kind == StatementKind.For && statement.Expression == null;
        if ((body & Exit.Break) != 0) exits |= Exit.Normal;
        if (statement.Kind == StatementKind.ForEach || pretest && !always)
            exits |= Exit.Normal;
        else if (statement.Kind == StatementKind.DoWhile && !always && (body & (Exit.Normal | Exit.Continue)) != 0)
            exits |= Exit.Normal;
        // Normal and continue paths repeat; neither exits a constant-true loop.
        return exits;
    }

    static bool Constant(CsExpression? expression, bool value) => expression is { Kind: ExpressionKind.Literal } &&
        expression.Text == (value ? "true" : "false");
}
