using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;
public sealed record ParseResult<T>(T Root, Values<ParseDiagnostic> Diagnostics, ParseCoverage Coverage)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
public enum ParseCoverage { Typed, Partial, Opaque }
public enum DiagnosticSeverity { Info, Warning, Error }
public sealed record ParseDiagnostic(string Code, DiagnosticSeverity Severity, int Offset, int Length,
    int Line, int Column, string Message);
