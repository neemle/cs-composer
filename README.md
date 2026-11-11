# Neemle.CSharpComposer

An independent structured C# composition library. The `netstandard2.0` production project owns the DOM, fluent
builders, validation and deterministic direct rendering. Its target dependency graph contains no Roslyn,
Microsoft.CodeAnalysis or generator adapter dependency. The SDK compiler remains build tooling.

```csharp
using Neemle.CSharpComposer;
using Neemle.CSharpComposer.Model;

CsDocument document = Cs.File("User.g.cs").Namespace("Example")
    .Class("User").Public().Partial()
    .Property<int>("Id").Public().Get().Init().End()
    .Property<string>("Name").Public().Required().Get().Init().End().End();

string source = document.Render();
```

The root `Cs` entry point composes types from `Model`, `Builders`, `Rendering` and `Validation` namespaces.
Built documents are immutable snapshots with structural equality. Rendering validates by default, preserves
member order, uses LF/final newline and reports stable diagnostics. Explicit raw snippets remain source data;
the library never executes them. Callers own documents/output and their generated program's semantics.

Build the local package with only this repository mounted:

```bash
mkdir -p .dev-temp/tmp .dev-temp/packages
docker run --rm -v "$PWD:/work" -w /work \
  -e DOTNET_CLI_HOME=/work/.dev-temp/dotnet \
  -e NUGET_PACKAGES=/work/.dev-temp/nuget -e TMPDIR=/work/.dev-temp/tmp \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet pack \
  src/Neemle.CSharpComposer/Neemle.CSharpComposer.csproj -c Release -o /work/.dev-temp/packages
```

The first checkpoint builds/renders the DTO independently and produces a local0.1.0 package. Migration of the
original test assertions and full independent-unit coverage is a following step, not a completed claim here.
There is no public NuGet publication or hosted service. Rollback is a forward source change or consumer version pin.

Roslyn `ToSourceText` and `AddSource` belong to the separate `Neemle.GeneratorEngine` adapter repository/package.
