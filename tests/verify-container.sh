#!/usr/bin/env bash
set -euo pipefail
mkdir -p .dev-temp/tmp .dev-temp/packages artifacts/gene8-tests
chmod 700 .dev-temp/tmp
results_dir=${TEST_RESULTS_DIRECTORY:-$(mktemp -d /work/.dev-temp/verification.XXXXXX)}
evidence_dir=${EVIDENCE_DIRECTORY:-$(mktemp -d /work/artifacts/gene8-tests/run.XXXXXX)}
mkdir -p "$evidence_dir"
export VSTEST_DISABLE_ARTIFACTS_DIRECTORY_VERIFICATION=1
run() {
  printf '\nCOMMAND:'; printf ' %q' "$@"; printf '\n'
  set +e; "$@"; local result=$?; set -e
  printf 'EXIT=%s\n' "$result"; return "$result"
}
date -u +%FT%TZ
dotnet --version
git rev-parse HEAD
run dotnet restore tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj --use-lock-file --force-evaluate
run dotnet restore tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj --locked-mode --force
find src tests -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.sh' \
  -o -name '*.runsettings' -o -name 'packages.lock.json' \) -print0 | sort -z | xargs -0 sha256sum \
  > "$evidence_dir/executed-inputs.sha256"
sha256sum Directory.Build.props NuGet.Config README.md >> "$evidence_dir/executed-inputs.sha256"
set +e
grep -E 'Microsoft.CodeAnalysis|Neemle.GeneratorEngine' \
  .dev-temp/obj/Neemle.CSharpComposer.Tests/project.assets.json \
  .dev-temp/obj/Neemle.CSharpComposer/project.assets.json
graph_result=$?
set -e
if [ "$graph_result" != 1 ]; then printf 'Dependency graph rejected, grep exit=%s\n' "$graph_result"; exit 1; fi
printf 'PURE_GRAPH_CHECK=PASS\n'
run dotnet test tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj \
  -c Release --no-restore --settings tests/coverage.runsettings --collect:'XPlat Code Coverage' \
  --results-directory "$results_dir" --logger 'trx;LogFileName=core.trx'
coverage_files="$results_dir/coverage-files.txt"
find "$results_dir" -mindepth 2 -maxdepth 2 -type f -name coverage.cobertura.xml > "$coverage_files"
coverage_count=$(wc -l < "$coverage_files")
if [ "$coverage_count" -ne 1 ]; then printf 'Expected one current report, found %s\n' "$coverage_count"; exit 1; fi
IFS= read -r coverage_report < "$coverage_files"
find "$results_dir" -mindepth 3 -type f -name coverage.cobertura.xml > "$results_dir/mirrors.txt"
while IFS= read -r mirror; do cmp "$coverage_report" "$mirror"; done < "$results_dir/mirrors.txt"
printf '%s\n' "$coverage_report" > "$results_dir/coverage-path.txt"
run dotnet pack src/Neemle.CSharpComposer/Neemle.CSharpComposer.csproj -c Release --no-restore \
  -o /work/.dev-temp/packages --nologo
run dotnet list tests/Neemle.CSharpComposer.Tests/Neemle.CSharpComposer.Tests.csproj package \
  --include-transitive --format json
sha256sum .dev-temp/packages/Neemle.CSharpComposer.0.2.0.nupkg \
  .dev-temp/bin/Neemle.CSharpComposer/Release/net10.0/Neemle.CSharpComposer.dll
