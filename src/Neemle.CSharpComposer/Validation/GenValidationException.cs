using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Validation;

public sealed class GenValidationException : Exception
{
    public IReadOnlyList<GenDiagnostic> Diagnostics { get; }

    public GenValidationException(IReadOnlyList<GenDiagnostic> diagnostics)
        : base(string.Join("\n", diagnostics.Select(Describe)))
    {
        Diagnostics = new Values<GenDiagnostic>(diagnostics);
    }

    static string Describe(GenDiagnostic diagnostic) =>
        diagnostic.Code + " " + diagnostic.Path + ": " + diagnostic.Message;
}
