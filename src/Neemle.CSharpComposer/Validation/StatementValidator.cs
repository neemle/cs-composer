using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class StatementValidator
{
    readonly Validator validation;

    readonly bool inCatch;

    readonly bool inLoop;

    readonly CsStatement? switchContext;

    readonly IReadOnlyDictionary<string, bool> labels;

    internal StatementValidator(Validator validation, bool inCatch, CsStatement? switchContext = null, bool inLoop = false, IReadOnlyDictionary<string, bool>? labels = null)
    {
        this.validation = validation;
        this.inCatch = inCatch;
        this.inLoop = inLoop;
        this.switchContext = switchContext;
        this.labels = labels ?? new Dictionary<string, bool>();
    }

    internal void Validate(CsStatement statement, string path, int depth)
    {
        if (!validation.Depth(depth, path)) return;
        if (!Enum.IsDefined(typeof(StatementKind), statement.Kind))
            validation.Error("GEN023", path, "Unknown statement kind.");
        Payload(statement, path);
        switch (statement.Kind)
        {
            case StatementKind.DisabledText: validation.Required(statement.Disabled, path + ".Disabled"); break;
            case StatementKind.Directive: if (validation.Required(statement.Directive, path + ".Directive")) validation.Directive(statement.Directive!, path + ".Directive", depth + 1); break;
            case StatementKind.LocalFunction:
                if (validation.Required(statement.Function, path + ".Function"))
                {
                    if (statement.Function!.Modifiers.Any(m => m is not ("static" or "async" or "unsafe" or "extern"))) validation.Error("GEN031", path, "Invalid local function modifier.");
                    if (statement.Function.Modifiers.Contains("static")) validation.Feature("Static local function", CsLanguageVersion.CSharp8, path);
                    new MemberValidator(validation).Validate(statement.Function, path + ".Function", null, depth + 1);
                }
                break;
            case StatementKind.Label: Label(statement, path, depth); break;
            case StatementKind.Goto: validation.Identifier(statement.Text, path + ".Text"); break;
            case StatementKind.YieldReturn: validation.Expression(statement.Expression, path + ".Expression", depth + 1); break;
            case StatementKind.YieldBreak: break;
            case StatementKind.Lock: validation.Expression(statement.Expression, path + ".Expression", depth + 1); Block(statement.Body, path + ".Body", depth + 1, inCatch); break;
            case StatementKind.Fixed:
                if (validation.Required(statement.Resource, path + ".Resource")) Validate(statement.Resource!, path + ".Resource", depth + 1);
                Block(statement.Body, path + ".Body", depth + 1, inCatch); break;
            case StatementKind.Unsafe:
            case StatementKind.Checked:
            case StatementKind.Unchecked:
                Block(statement.Body, path + ".Body", depth + 1, inCatch); break;
            case StatementKind.For:
            case StatementKind.ForEach:
            case StatementKind.While:
            case StatementKind.DoWhile:
                Loop(statement, path, depth); break;
            case StatementKind.Continue: if (statement.Text != "") LabeledJump(statement, path); else Continue(path); break;
            case StatementKind.Switch: Switch(statement, path, depth); break;
            case StatementKind.Break:
            case StatementKind.GotoCase:
            case StatementKind.GotoDefault:
                Jump(statement, path, depth); break;
            case StatementKind.LocalGroup:
                if (validation.Required(statement.ForInitializer, path + ".ForInitializer")) Initializer(statement.ForInitializer!, path + ".ForInitializer", depth + 1, allowConst: true); break;
            case StatementKind.Local: Local(statement, path, depth); break;
            case StatementKind.Block: Block(statement.Body, path + ".Body", depth + 1, inCatch); break;
            case StatementKind.If: If(statement, path, depth); break;
            case StatementKind.Try: Try(statement, path, depth); break;
            case StatementKind.Using: Using(statement, path, depth); break;
            case StatementKind.UsingDeclaration: UsingDeclaration(statement, path, depth); break;
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
        validation.VariableModifiers(statement.VariableModifiers, path + ".VariableModifiers", statement.IsVar, allowConst: true);
        validation.Identifier(statement.Text, path + ".Text");
        if (!statement.IsVar) validation.Type(statement.Type, path + ".Type", depth + 1);
        if (statement.IsVar && statement.Expression == null)
            validation.Error("GEN067", path + ".Expression", "A var declaration requires an initializer.");
        if (statement.IsVar && statement.Expression is { Kind: ExpressionKind.Lambda })
            validation.Feature("Natural lambda type inference", CsLanguageVersion.CSharp10, path + ".Expression");
        if ((statement.VariableModifiers & CsVariableModifiers.Const) != 0) validation.Constant(statement.Expression, path + ".Expression", depth + 1);
        else validation.VariableInitializer(statement.Expression, statement.Type, path + ".Expression", depth + 1);
    }

    void Block(CsBody? body, string path, int depth, bool catchContext)
    {
        if (body?.Expression != null)
            validation.Error("GEN066", path, "Embedded blocks cannot use an expression body.");
        validation.Body(body, path, depth, catchContext, switchContext, inLoop, labels: labels);
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
            catchAll = handler.Type == null && handler.Filter == null;
            if (handler.Filter != null) validation.Guard(handler.Filter, child + ".Filter", depth + 1);
            if (handler.Type != null) validation.Type(handler.Type, child + ".Type", depth + 1);
            if (handler.Name != null) validation.Identifier(handler.Name, child + ".Name");
            Block(handler.Body, child + ".Body", depth + 1, true);
        });
        if (statement.Finally != null)
            new StatementValidator(validation, false).Block(statement.Finally, path + ".Finally", depth + 1, false);
    }

    void Using(CsStatement statement, string path, int depth)
    {
        if ((statement.Expression == null) == (statement.Resource == null))
            validation.Error("GEN071", path, "Using requires exactly one resource expression or declaration.");
        if (statement.Expression != null) Expression(statement, path, depth);
        if (statement.Resource != null)
        {
            if (statement.Resource.Kind is not (StatementKind.Local or StatementKind.LocalGroup) || statement.Resource.Kind == StatementKind.Local && statement.Resource.Expression == null || statement.Resource.Kind == StatementKind.LocalGroup && statement.Resource.ForInitializer?.Variables.Any(v => v.Initializer == null) == true)
                validation.Error("GEN071", path + ".Resource", "Using requires an initialized local declaration.");
            Validate(statement.Resource, path + ".Resource", depth + 1);
        }
        Block(statement.Body, path + ".Body", depth + 1, inCatch);
    }

    void UsingDeclaration(CsStatement statement, string path, int depth)
    {
        validation.Feature("Using declaration", CsLanguageVersion.CSharp8, path);
        if (statement.Resource == null) { validation.Required(null, path + ".Resource"); return; }
        if (statement.Resource.Kind is not (StatementKind.Local or StatementKind.LocalGroup) || statement.Resource.Kind == StatementKind.Local && statement.Resource.Expression == null || statement.Resource.Kind == StatementKind.LocalGroup && statement.Resource.ForInitializer?.Variables.Any(v => v.Initializer == null) == true)
            validation.Error("GEN071", path + ".Resource", "Using declaration needs an initialized local.");
        Validate(statement.Resource, path + ".Resource", depth + 1);
    }

    void Label(CsStatement statement, string path, int depth)
    {
        validation.Identifier(statement.Text, path + ".Text");
        var target = statement.Body?.Statements.FirstOrDefault();
        bool loop = target?.Kind is StatementKind.For or StatementKind.ForEach or StatementKind.While or StatementKind.DoWhile;
        if (loop || target?.Kind == StatementKind.Switch)
        {
            var nested = new Dictionary<string, bool>(labels);
            if (!nested.TryAdd(statement.Text.TrimStart('@'), loop)) validation.Error("GEN092", path, "Duplicate enclosing label.");
            new StatementValidator(validation, inCatch, switchContext, inLoop, nested).Block(statement.Body, path + ".Body", depth + 1, inCatch);
        }
        else Block(statement.Body, path + ".Body", depth + 1, inCatch);
    }

    void LabeledJump(CsStatement statement, string path)
    {
        validation.Feature("Labeled break/continue", CsLanguageVersion.CSharp15, path);
        validation.Identifier(statement.Text, path + ".Text");
        if (!labels.TryGetValue(statement.Text.TrimStart('@'), out bool loop) || statement.Kind == StatementKind.Continue && !loop)
            validation.Error("GEN092", path, "Named jump requires an enclosing labeled loop or switch; continue requires a loop.");
    }

    void Rethrow(string path)
    {
        if (!inCatch) validation.Error("GEN070", path, "Rethrow requires an enclosing catch outside finally.");
    }

    void Payload(CsStatement statement, string path)
    {
        bool loop = statement.Kind is StatementKind.For or StatementKind.ForEach or StatementKind.While or StatementKind.DoWhile;
        bool variable = statement.Kind is StatementKind.Local or StatementKind.ForEach;
        bool block = loop || statement.Kind is StatementKind.Block or StatementKind.If or StatementKind.Try or StatementKind.Using or StatementKind.Lock or StatementKind.Fixed or StatementKind.Unsafe or StatementKind.Checked or StatementKind.Unchecked or StatementKind.Label;
        bool invalid = statement.IsAwait && statement.Kind is not (StatementKind.ForEach or StatementKind.Using or StatementKind.UsingDeclaration);
        if (statement.IsAwait) validation.Feature("Asynchronous iteration/disposal", CsLanguageVersion.CSharp8, path + ".IsAwait");
        invalid |= statement.Body != null && !block || statement.Type != null &&
            (!variable || statement.IsVar) || statement.IsVar && !variable;
        invalid |= statement.Else != null && statement.Kind != StatementKind.If ||
            (statement.Finally != null || statement.Catches?.Count > 0) && statement.Kind != StatementKind.Try ||
            statement.Resource != null && statement.Kind is not (StatementKind.Using or StatementKind.UsingDeclaration or StatementKind.Fixed);
        invalid |= statement.Expression != null && statement.Kind is StatementKind.Block or StatementKind.Try or
            StatementKind.Rethrow or StatementKind.Break or StatementKind.GotoDefault or StatementKind.Continue or StatementKind.Empty or StatementKind.UsingDeclaration or StatementKind.YieldBreak or StatementKind.Goto or StatementKind.Label or StatementKind.Unsafe or StatementKind.Checked or StatementKind.Unchecked or StatementKind.Fixed or StatementKind.LocalFunction;
        invalid |= statement.Kind > StatementKind.Raw && !variable && statement.Kind is not (StatementKind.Goto or StatementKind.Label or StatementKind.Break or StatementKind.Continue) && statement.Text != "";
        invalid |= statement.Sections?.Count > 0 && statement.Kind != StatementKind.Switch;
        invalid |= (statement.ForInitializer != null || statement.Iterators?.Count > 0) && statement.Kind is not (StatementKind.For or StatementKind.LocalGroup);
        if (statement.Sections == null && statement.Kind != StatementKind.Switch)
            validation.Required(null, path + ".Sections");
        if (statement.Iterators == null && statement.Kind != StatementKind.For)
            validation.Required(null, path + ".Iterators");
        if (invalid) validation.Error("GEN065", path, "Payload does not belong to this node kind.");
        if (statement.Disabled != null && statement.Kind != StatementKind.DisabledText) validation.Error("GEN065", path, "Disabled text belongs to inactive source.");
        if (statement.Directive != null && statement.Kind != StatementKind.Directive) validation.Error("GEN065", path, "Directive payload belongs to a directive.");
        if (statement.Binding != null && statement.Kind != StatementKind.ForEach) validation.Error("GEN065", path, "Binding belongs to foreach.");
        if (statement.VariableModifiers != CsVariableModifiers.None && !variable) validation.Error("GEN065", path, "Variable modifiers belong to a local or foreach binding.");
        if (statement.Function != null && statement.Kind != StatementKind.LocalFunction) validation.Error("GEN065", path, "Function payload belongs to a local function.");
        if (statement.Catches == null && statement.Kind != StatementKind.Try)
            validation.Required(null, path + ".Catches");
    }
}
