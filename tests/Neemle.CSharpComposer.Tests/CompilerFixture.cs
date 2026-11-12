using System.Diagnostics;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

internal static class CompilerFixture
{
    internal static async Task Run(string source, string language)
    {
        string directory = Path.Combine(Path.GetTempPath(), "composer-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string project = Path.Combine(directory, "Smoke.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>" + language +
                "</LangVersion><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), source);
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
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr + "\nGenerated source:\n" + source);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
