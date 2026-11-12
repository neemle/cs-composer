using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class StatementValidator
{
    void Switch(CsStatement statement, string path, int depth)
    {
        validation.Expression(statement.Expression, path + ".Expression", depth + 1);
        bool hasDefault = false;
        var coverage = new Validator.PatternCoverage(validation);
        validation.List(statement.Sections, path + ".Sections", (section, child) =>
        {
            if (section.Labels?.Count == 0)
                validation.Error("GEN080", child + ".Labels", "A switch section requires at least one label.");
            validation.List(section.Labels, child + ".Labels", (label, labelPath) =>
            {
                if (label.Pattern == null)
                {
                    if (hasDefault) validation.Error("GEN080", labelPath, "Only one default label is permitted.");
                    hasDefault = true;
                    if (label.When != null) validation.Error("GEN080", labelPath, "Default cannot have a case guard.");
                }
                else
                {
                    int before = validation.ErrorCount;
                    validation.Pattern(label.Pattern, labelPath + ".Pattern", depth + 1);
                    if (label.Pattern.Kind == PatternKind.Discard)
                        validation.Error("GEN080", labelPath, "Use default or case var _ in a switch statement.");
                    if (label.When != null) validation.Guard(label.When, labelPath + ".When", depth + 1);
                    if (before == validation.ErrorCount) coverage.Add(label.Pattern, label.When, labelPath);
                }
            });
            if (section.Body?.Expression != null)
                validation.Error("GEN066", child + ".Body", "Switch sections require a statement list.");
            validation.Body(section.Body, child + ".Body", depth + 1, inCatch, statement, inLoop);
            if (Flow.Reachable(section.Body) == true)
                validation.Error("GEN081", child + ".Body", "A switch section cannot fall through; use break, return, throw or goto.");
        });
    }

    void Jump(CsStatement statement, string path, int depth)
    {
        if (statement.Kind == StatementKind.Break)
        {
            if (switchContext == null && !inLoop)
                validation.Error("GEN082", path, "Break requires an enclosing loop or switch outside finally.");
            return;
        }
        if (switchContext == null)
            validation.Error("GEN082", path, "Switch jump requires an enclosing switch outside finally.");
        if (statement.Kind != StatementKind.GotoCase)
        {
            if (statement.Kind == StatementKind.GotoDefault && switchContext?.Sections != null &&
                !Labels(switchContext).Any(l => l.Pattern == null))
                validation.Error("GEN082", path, "The enclosing switch has no default label.");
            return;
        }
        validation.Constant(statement.Expression, path + ".Expression", depth + 1);
        // Case target binding and conversions require symbols and remain the
        // compiler's responsibility (including conversions between integral constants).
    }

    static IEnumerable<CsSwitchLabel> Labels(CsStatement statement) => statement.Sections
        .Where(s => s?.Labels != null).SelectMany(s => s.Labels).Where(l => l != null);
}
