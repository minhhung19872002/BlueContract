#!/usr/bin/env bash
# Merges the Cobertura reports of all backend test projects and checks line coverage of Domain + Application
# (spec §13: ≥ 70 %). A line counts as covered when any test project hit it.
# Usage: scripts/coverage.sh [results_dir] [min_percent]
set -euo pipefail
dir="${1:-backend/TestResults}"
min="${2:-70}"
python3 - "$dir" "$min" <<'PY'
import glob, sys, xml.etree.ElementTree as ET
directory, minimum = sys.argv[1], float(sys.argv[2])
hits = {}
for path in glob.glob(f"{directory}/**/coverage.cobertura.xml", recursive=True):
    for pkg in ET.parse(path).getroot().iter("package"):
        name = pkg.get("name")
        if name not in ("BlueContract.Domain", "BlueContract.Application"):
            continue
        for cls in pkg.iter("class"):
            for line in cls.iter("line"):
                key = (name, cls.get("filename"), line.get("number"))
                hits[key] = max(hits.get(key, 0), int(line.get("hits")))
if not hits:
    sys.exit("Không tìm thấy báo cáo coverage")
for name in ("BlueContract.Domain", "BlueContract.Application"):
    lines = [v for k, v in hits.items() if k[0] == name]
    print(f"{name}: {100 * sum(1 for v in lines if v) / len(lines):.1f}% ({len(lines)} dòng)")
total = 100 * sum(1 for v in hits.values() if v) / len(hits)
print(f"Domain + Application: {total:.1f}% (ngưỡng {minimum:.0f}%)")
sys.exit(0 if total >= minimum else 1)
PY