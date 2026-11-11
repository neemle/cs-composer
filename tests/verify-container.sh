#!/usr/bin/env bash
set -euo pipefail
mkdir -p .dev-temp/tmp .dev-temp/packages artifacts/gene7-tests
chmod 700 .dev-temp/tmp
export VSTEST_DISABLE_ARTIFACTS_DIRECTORY_VERIFICATION=1
run() {
  printf '\nCOMMAND:'; printf ' %q' "$@"; printf '\n'
  set +e; "$@"; local result=$?; set -e
  printf 'EXIT=%s\n' "$result"; return "$result"
}
date -u +%FT%TZ
dotnet --version
git rev-parse HEAD
find src tests -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.sh' -o -name '*.runsettings' \) \
  -print0 | sort -z | xargs -0 sha256sum > artifacts/gene7-tests/sources.sha256
run dotnet restore tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj --use-lock-file --force-evaluate
run dotnet restore tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj --locked-mode --force
set +e
grep -E 'Microsoft.CodeAnalysis|Neemle.GeneratorEngine' \
  .dev-temp/obj/Neemle.CSharpComposer.Tests/project.assets.json \
  .dev-temp/obj/Neemle.CSharpComposer/project.assets.json
graph_result=$?
set -e
if [ "$graph_result" = 0 ]; then
  printf 'Forbidden target dependency\n'; exit 1
fi
if [ "$graph_result" != 1 ]; then printf 'Dependency graph check failed\n'; exit "$graph_result"; fi
printf 'PURE_GRAPH_CHECK=PASS\n'
run dotnet test tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj \
  -c Release --no-restore --settings tests/coverage.runsettings \
  --collect:'XPlat Code Coverage' --results-directory /work/.dev-temp/gene7-results \
  --logger 'trx;LogFileName=core.trx'
run dotnet pack src/Neemle.CSharpComposer/Neemle.CSharpComposer.csproj -c Release --no-restore \
  -o /work/.dev-temp/packages --nologo
run dotnet list tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj package \
  --include-transitive --format json
sha256sum .dev-temp/packages/Neemle.CSharpComposer.0.1.0.nupkg \
  .dev-temp/bin/Neemle.CSharpComposer/Release/netstandard2.0/Neemle.CSharpComposer.dll
