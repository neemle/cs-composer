#!/usr/bin/env bash
# Fails unless line, branch and method coverage of src/Neemle.CSharpComposer are all 100%.
#
#   tools/coverage-gate.sh <coverage.cobertura.xml> <category>
#
# <coverage.cobertura.xml> is a Coverlet "XPlat Code Coverage" report, or a directory that holds
# exactly one. <category> names the test run (Unit, Integration) in every message. In a directory,
# copies under an In/ directory are ignored: the trx logger copies each collector attachment into its
# deployment folder (<results-dir>/<run-name>/In/<machine>/coverage.cobertura.xml) next to the
# <results-dir>/<guid>/coverage.cobertura.xml the collector writes. The gate needs only bash and
# python3, both already on the CI runner; it adds no dependency.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  printf 'usage: %s <coverage.cobertura.xml|results-dir> <category>\n' "$0" >&2
  exit 2
fi

report=$1
category=$2
if [ -d "$report" ]; then
  found=
  count=0
  while IFS= read -r candidate; do
    case "/${candidate#"$report"}/" in
      */In/*) continue ;;  # trx logger deployment copy of the collector attachment
    esac
    found=$candidate
    count=$((count + 1))
  done < <(find "$report" -name coverage.cobertura.xml -type f)
  if [ "$count" -ne 1 ]; then
    printf 'coverage gate [%s]: expected exactly one coverage.cobertura.xml under %s, found %d.\n' \
      "$category" "$report" "$count" >&2
    exit 2
  fi
  report=$found
fi
if [ ! -f "$report" ]; then
  printf 'coverage gate [%s]: report %s does not exist.\n' "$category" "$report" >&2
  exit 2
fi

python3 -I - "$report" "$category" <<'PY'
import re
import sys
import xml.etree.ElementTree as ET

report, category = sys.argv[1], sys.argv[2]
PACKAGE = "Neemle.CSharpComposer"

packages = [p for p in ET.parse(report).getroot().iter("package") if p.get("name") == PACKAGE]
if not packages:
    print(f"coverage gate [{category}]: package {PACKAGE} is missing from {report}.", file=sys.stderr)
    sys.exit(2)

lines = {}      # (file, line) -> hit
branches = {}   # (file, line) -> (covered, total)
methods = {}    # (class, method, signature) -> hit
for package in packages:
    for cls in package.iter("class"):
        filename = cls.get("filename")
        for line in cls.find("lines").iter("line"):
            key = (filename, int(line.get("number")))
            lines[key] = lines.get(key, False) or int(line.get("hits")) > 0
            if line.get("branch") == "True":
                match = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage", ""))
                if match:
                    covered, total = int(match.group(1)), int(match.group(2))
                    previous = branches.get(key, (0, total))
                    branches[key] = (max(previous[0], covered), max(previous[1], total))
        for method in cls.iter("method"):
            key = (cls.get("name"), method.get("name"), method.get("signature"))
            hit = any(int(l.get("hits")) > 0 for l in method.iter("line"))
            methods[key] = methods.get(key, False) or hit

def metric(name, covered, total):
    percent = 100.0 if total == 0 else covered * 100.0 / total
    return name, covered, total, percent

results = [
    metric("line", sum(lines.values()), len(lines)),
    metric("branch", sum(c for c, _ in branches.values()), sum(t for _, t in branches.values())),
    metric("method", sum(methods.values()), len(methods)),
]

failed = False
for name, covered, total, percent in results:
    status = "ok" if covered == total else "FAIL"
    print(f"coverage gate [{category}]: {name} coverage {percent:.2f}% ({covered}/{total}) {status}")
    failed |= covered != total

if failed:
    gaps = sorted({k for k, hit in lines.items() if not hit} |
                  {k for k, (c, t) in branches.items() if c < t})
    print(f"coverage gate [{category}]: uncovered lines or branches ({len(gaps)}):")
    for filename, number in gaps[:400]:
        detail = ""
        if (filename, number) in branches:
            c, t = branches[(filename, number)]
            detail = f" branches {c}/{t}"
        if not lines.get((filename, number), True):
            detail = " not hit" + detail
        print(f"  {filename}:{number}{detail}")
    for (cls, name, signature), hit in sorted(methods.items()):
        if not hit:
            print(f"  method never entered: {cls}::{name}{signature}")
    for name, covered, total, percent in results:
        if covered != total:
            print(f"::error title=Coverage gate ({category})::{category} {name} coverage of {PACKAGE} "
                  f"is {percent:.2f}% ({covered}/{total}); the gate requires 100%.")
    sys.exit(1)

print(f"coverage gate [{category}]: {PACKAGE} line, branch and method coverage are all 100%.")
PY
