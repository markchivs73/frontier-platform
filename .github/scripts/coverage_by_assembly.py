#!/usr/bin/env python3
"""Per-assembly line/branch coverage from coverlet Cobertura reports (S9.24).

Vendored verbatim from frontier-workflow/.github/scripts/coverage_by_assembly.py @ 8df00ee.
Drift check:
  git -C ../frontier-workflow log --oneline 8df00ee.. -- .github/scripts/coverage_by_assembly.py
Do not "tidy" this file — the dedupe logic below encodes two real coverlet bugs.

Handles the two coverlet artifacts that distort naive parsing:
- each line element appears under BOTH <methods> and the class-level <lines>
  (dedupe by (filename, line number) per class, taking max hits), and
- duplicate AssemblyLoadContext modules named "Assembly 2", "Assembly 14", ...
  (normalized into the base assembly, max-merged per line).

Usage: coverage_by_assembly.py <results-dir> [threshold]
Exits non-zero if any assembly is below the threshold (default 95, line AND branch).
"""
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


def main(results_dir: str, threshold: float) -> int:
    lines: dict[str, dict] = defaultdict(dict)

    for report in Path(results_dir).glob("**/coverage.cobertura.xml"):
        for pkg in ET.parse(report).getroot().iter("package"):
            name = re.sub(r" \d+$", "", pkg.get("name", ""))
            # "Tests" suffix, not just ".Tests": Frontier.ArchitectureTests and
            # Frontier.Reason.Workflow.ContractTests are test-only projects too but
            # don't have a dot before "Tests", so a substring check missed them and
            # let them through as "production" assemblies at a false 0% coverage.
            if name.endswith("Tests") or name.startswith("testhost") or not name:
                continue
            for cls in pkg.iter("class"):
                # Key on the fully-qualified class name, not the filename: reports mix
                # absolute, src-relative, and bare filename forms for the same file,
                # which breaks cross-report merging; class names are stable and unique
                # within an assembly.
                cname = cls.get("name") or ""
                for line in cls.iter("line"):
                    key = (cname, line.get("number"))
                    hits = int(line.get("hits", "0"))
                    cond = line.get("condition-coverage")
                    bc = bt = 0
                    if cond:
                        frac = cond.split("(")[1].rstrip(")")
                        bc, bt = (int(x) for x in frac.split("/"))
                    prev = lines[name].get(key)
                    if prev:
                        hits = max(prev[0], hits)
                        bc = max(prev[1], bc)
                        bt = max(prev[2], bt)
                    lines[name][key] = (hits, bc, bt)

    rows = []
    for asm, ls in lines.items():
        lv = len(ls)
        lc = sum(1 for h, _, _ in ls.values() if h > 0)
        bv = sum(t for _, _, t in ls.values())
        bc = sum(c for _, c, _ in ls.values())
        rows.append((lc / lv * 100 if lv else 100.0, (bc / bv * 100 if bv else 100.0), lc, lv, bc, bv, asm))

    rows.sort()
    failed = []
    print(f"{'':1}{'assembly':52} {'line%':>7} {'branch%':>8}")
    for lp, bp, lc, lv, bc, bv, asm in rows:
        ok = lp >= threshold and bp >= threshold
        if not ok:
            failed.append(asm)
        print(f"{' ' if ok else 'X'}{asm:52} {lp:6.2f}% {bp:7.2f}%  ({lc}/{lv}L {bc}/{bv}B)")

    tlv = sum(r[3] for r in rows)
    tlc = sum(r[2] for r in rows)
    tbv = sum(r[5] for r in rows)
    tbc = sum(r[4] for r in rows)
    print("-" * 80)
    print(f" MERGED {tlc / tlv * 100:6.2f}% line, {tbc / tbv * 100:6.2f}% branch  ({tlc}/{tlv}L {tbc}/{tbv}B)")

    if failed:
        print(f"::error::{len(failed)} assemblies below {threshold}%: {', '.join(failed)}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], float(sys.argv[2]) if len(sys.argv) > 2 else 95.0))
