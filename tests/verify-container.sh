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
results_dir=${TEST_RESULTS_DIRECTORY:-$(mktemp -d /work/.dev-temp/gene7-results.XXXXXX)}
printf 'CORE_RESULTS_DIRECTORY=%s\n' "$results_dir"
run dotnet test tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj \
  -c Release --no-restore --settings tests/coverage.runsettings \
  --collect:'XPlat Code Coverage' --results-directory "$results_dir" \
  --logger 'trx;LogFileName=core.trx'
coverage_files="$results_dir/coverage-files.txt"
find "$results_dir" -type f -name coverage.cobertura.xml > "$coverage_files"
coverage_count=$(wc -l < "$coverage_files")
if [ "$coverage_count" -ne 1 ]; then
  printf 'Expected one current coverage report, found %s\n' "$coverage_count" >&2; exit 1
fi
IFS= read -r coverage_report < "$coverage_files"
printf '%s\n' "$coverage_report" > "$results_dir/coverage-path.txt"
run dotnet pack src/Neemle.CSharpComposer/Neemle.CSharpComposer.csproj -c Release --no-restore \
  -o /work/.dev-temp/packages --nologo
run dotnet list tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj package \
  --include-transitive --format json
sha256sum .dev-temp/packages/Neemle.CSharpComposer.0.1.0.nupkg \
  .dev-temp/bin/Neemle.CSharpComposer/Release/netstandard2.0/Neemle.CSharpComposer.dll
