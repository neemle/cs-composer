using System.Diagnostics;
using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SwitchCompilerTests
{
    static CsDocument Fixture(CsLanguageVersion version)
    {
        var document = Cs.Document(version).Class("SwitchDemo").Public().Static();
        document.Method<int>("Statement").Public().Static().Parameter<int>("input").Body(
            Cs.Switch(Cs.Id("input"),
                Cs.Section(Cs.Body(Cs.GotoCase(Cs.Literal(2))), Cs.Case(Cs.Literal(0)), Cs.Case(Cs.Literal(1))),
                Cs.Section(Cs.Body(Cs.Return(Cs.Literal(20))), Cs.Case(Cs.Literal(2))),
                Cs.Section(Cs.Body(Cs.GotoDefault()), Cs.Case(Cs.Literal(3))),
                Cs.Section(Cs.Body(Cs.Return(Cs.Literal(-1))), Cs.Default()))).End();
        document.Method<int>("Breaks").Public().Static().Parameter<int>("input").Body(
            Cs.Var("result", Cs.Literal(0)),
            Cs.Switch(Cs.Id("input"), Cs.Section(Cs.Body(Cs.Block(
                Cs.Statement(Cs.Assign(Cs.Id("result"), Cs.Literal(42))), Cs.Break())), Cs.Case(Cs.Literal(1))),
                Cs.Section(Cs.Body(Cs.Break()), Cs.Default())), Cs.Return(Cs.Id("result"))).End();
        document.Method<int>("Pattern").Public().Static().Parameter<object>("input").Body(
            Cs.Switch(Cs.Id("input"),
                Cs.Section(Cs.Body(Cs.Return(Cs.Literal(-1))), Cs.Default()),
                Cs.Section(Cs.Body(Cs.Return(Cs.Id("n"))), Cs.Case(Cs.DeclarationPattern(Cs.Int, "n"),
                    Cs.Binary(CsOperator.GreaterThan, Cs.Id("n"), Cs.Literal(0)))),
                Cs.Section(Cs.Body(Cs.Return(Cs.Literal(0))), Cs.Case(Cs.Literal(null))))).End();
        if (version >= CsLanguageVersion.CSharp8)
        {
            var expression = Cs.SwitchExpression(Cs.Binary(CsOperator.Add, Cs.Id("input"), Cs.Literal(1)),
                Cs.Arm(Cs.ConstantPattern(Cs.Literal(1)), Cs.Literal(10)), Cs.Arm(Cs.DiscardPattern(), Cs.Literal(20)));
            document.Method<int>("Expression").Public().Static().Parameter<int>("input").Expression(expression).End();
            document.Method<int>("Receiver").Public().Static().Parameter<int>("input")
                .Expression(expression.Member("CompareTo").Call(Cs.Literal(10))).End();
            document.Method<int>("Throwing").Public().Static().Parameter<int>("input").Parameter<Exception>("error")
                .Expression(Cs.SwitchExpression(Cs.Id("input"), Cs.Arm(Cs.ConstantPattern(Cs.Literal(1)), Cs.Literal(1)),
                    Cs.Arm(Cs.DiscardPattern(), Cs.ThrowExpression(Cs.Id("error"))))).End();
        }
        if (version >= CsLanguageVersion.CSharp9)
        {
            document.Method<int>("Modern").Public().Static().Parameter<int>("input").Expression(
                Cs.SwitchExpression(Cs.Id("input"),
                    Cs.Arm(Cs.AndPattern(Cs.RelationalPattern(CsOperator.GreaterThanOrEqual, Cs.Literal(0)),
                        Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal(10))), Cs.Literal(1)),
                    Cs.Arm(Cs.NotPattern(Cs.OrPattern(Cs.ConstantPattern(Cs.Literal(10)), Cs.ConstantPattern(Cs.Literal(20)))), Cs.Literal(2)),
                    Cs.Arm(Cs.DiscardPattern(), Cs.Literal(3)))).End();
            document.Method<int>("TypeOnly").Public().Static().Parameter<object>("input").Expression(
                Cs.SwitchExpression(Cs.Id("input"), Cs.Arm(Cs.TypePattern(CsType.String), Cs.Literal(1)),
                    Cs.Arm(Cs.VarPattern("other"), Cs.Literal(0)))).End();
        }
        return document.End().Build();
    }

    [Theory]
    [InlineData(CsLanguageVersion.CSharp7_3, "7.3")]
    [InlineData(CsLanguageVersion.CSharp8, "8.0")]
    [InlineData(CsLanguageVersion.CSharp9, "9.0")]
    public async Task Generated_switches_compile_and_execute_with_the_declared_language_version(CsLanguageVersion version, string language)
    {
        string directory = Path.Combine(Path.GetTempPath(), "composer-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string project = Path.Combine(directory, "Smoke.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>" + language +
                "</LangVersion><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup></Project>");
            string harness = "class Program { static void Check(bool result) { if (!result) throw new System.Exception(\"Switch behavior changed\"); } " +
                "static void Main() { Check(SwitchDemo.Statement(0) == 20); Check(SwitchDemo.Statement(1) == 20); " +
                "Check(SwitchDemo.Statement(3) == -1); Check(SwitchDemo.Statement(9) == -1); " +
                "Check(SwitchDemo.Breaks(1) == 42); Check(SwitchDemo.Breaks(0) == 0); " +
                "Check(SwitchDemo.Pattern(7) == 7); Check(SwitchDemo.Pattern(null) == 0); Check(SwitchDemo.Pattern(\"text\") == -1); ";
            if (version >= CsLanguageVersion.CSharp8)
                harness += "Check(SwitchDemo.Expression(0) == 10); Check(SwitchDemo.Expression(1) == 20); " +
                    "Check(SwitchDemo.Receiver(0) == 0); Check(SwitchDemo.Receiver(1) > 0); " +
                    "var error = new System.Exception(); try { SwitchDemo.Throwing(0, error); Check(false); } " +
                    "catch (System.Exception actual) { Check(object.ReferenceEquals(actual, error)); } ";
            if (version >= CsLanguageVersion.CSharp9)
                harness += "Check(SwitchDemo.Modern(5) == 1); Check(SwitchDemo.Modern(-1) == 2); Check(SwitchDemo.Modern(10) == 3); " +
                    "Check(SwitchDemo.TypeOnly(\"text\") == 1); Check(SwitchDemo.TypeOnly(null) == 0); ";
            var fixture = Fixture(version);
            Assert.Empty(fixture.Validate());
            // Compile the canonical rendering and the parsed/reformatted tree.
            string generated = Cs.Parse(fixture.Render(), new() { LanguageVersion = version }).Root.Reformat().Render();
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), generated + harness + "} }");
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory
            };
            foreach (string arg in new[] { "run", "--project", project, "-c", "Release", "--verbosity", "quiet" }) start.ArgumentList.Add(arg);
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr + "\nGenerated source:\n" + generated);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
