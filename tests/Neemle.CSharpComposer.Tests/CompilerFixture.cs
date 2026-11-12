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
                "<TargetFramework>" + (language is "15.0" or "preview" ? "net11.0" : "net10.0") + "</TargetFramework><OutputType>Exe</OutputType><LangVersion>" + language +
                "</LangVersion>" + (language == "preview" ? "<Features>updated-memory-safety-rules</Features>" : "") + "<EnableNETAnalyzers>false</EnableNETAnalyzers><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), source);
            var start = new ProcessStartInfo(language is "15.0" or "preview" ? Environment.GetEnvironmentVariable("COMPOSER_TEST_DOTNET11") ?? "dotnet" : "dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory
            };
            foreach (string inherited in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "DOTNET_HOST_PATH", "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER" }) start.Environment.Remove(inherited);
            if (Path.IsPathRooted(start.FileName))
            {
                string runtime = Path.GetDirectoryName(start.FileName)!;
                start.Environment["DOTNET_ROOT"] = runtime;
                start.Environment["DOTNET_ROOT_" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()] = runtime;
            }
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
