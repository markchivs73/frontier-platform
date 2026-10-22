#!/usr/bin/env python3
"""Ship the public API: move every package's PublicAPI.Unshipped.txt entries into its
PublicAPI.Shipped.txt — the step a release needs before it is tagged (git-workflow skill,
"Releasing").

    python3 tools/ship-public-api.py            # move the entries, print what shipped per package
    python3 tools/ship-public-api.py --dry-run  # print what would ship, write nothing
    python3 tools/ship-public-api.py --check    # exit 1 while any entry is unshipped (the publish gate)

Why it matters: Microsoft.CodeAnalysis.PublicApiAnalyzers only guards what is in the Shipped
file. Deleting an unshipped entry is silent; deleting a shipped one is RS0017 unless the PR
writes a `*REMOVED*` marker. Shipping at every tag is what makes the next release's breaks
visible as more than a "!" in a commit subject.

The rules match the analyzer's file format:
  - a line starting with '#' is a header ('#nullable enable') and stays where it is; blank
    lines are ignored;
  - a `*REMOVED*<entry>` line deletes <entry> from the Shipped file, or cancels <entry> when it
    was added in the same Unshipped file (added and removed again between two tags, so it never
    ships); one naming an entry found in neither file has nothing to do and is reported rather
    than failed on, because the end state is the same either way;
  - every other line is an entry and is added to the Shipped file, which is kept in ordinal
    order (the order the analyzer's code fix writes) with duplicates dropped;
  - the Unshipped file keeps only its header lines — an empty file is equally valid to the
    analyzer, as src/Frontier.Platform.Resilience shows.

Exit codes: 0 shipped (or, with --check, nothing to ship); 1 the check found unshipped
entries; 2 a file is malformed. No dependencies beyond the standard library.
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass, field
from pathlib import Path

REMOVED = "*REMOVED*"
BOM = "﻿"


class MalformedApiFile(Exception):
    """A PublicAPI file that cannot be shipped as it stands."""


@dataclass
class Package:
    name: str
    shipped: Path
    unshipped: Path


@dataclass
class Shipment:
    name: str
    added: int = 0
    removed: int = 0
    cancelled: int = 0
    duplicates: int = 0
    dangling: list[str] = field(default_factory=list)

    @property
    def is_empty(self) -> bool:
        return not (self.added or self.removed or self.cancelled or self.duplicates or self.dangling)


def repo_root() -> Path:
    return Path(__file__).resolve().parent.parent


def packages(root: Path) -> list[Package]:
    found = []
    for unshipped in sorted((root / "src").glob("*/PublicAPI.Unshipped.txt")):
        shipped = unshipped.with_name("PublicAPI.Shipped.txt")
        if shipped.exists():
            found.append(Package(unshipped.parent.name, shipped, unshipped))
    return found


def read_lines(path: Path) -> tuple[bool, list[str]]:
    """The file's lines and whether it carried a byte-order mark, which is written back as found."""
    text = path.read_text(encoding="utf-8")
    has_bom = text.startswith(BOM)
    if has_bom:
        text = text[len(BOM):]
    return has_bom, text.splitlines()


def write_lines(path: Path, has_bom: bool, lines: list[str]) -> None:
    body = "\n".join(lines) + ("\n" if lines else "")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write((BOM if has_bom else "") + body)


def split(lines: list[str]) -> tuple[list[str], list[str], list[str]]:
    """Headers, entries and removal targets, in file order; blank lines dropped."""
    headers, entries, removals = [], [], []
    for raw in lines:
        line = raw.rstrip("\r")
        if not line.strip():
            continue
        if line.startswith("#"):
            headers.append(line)
        elif line.startswith(REMOVED):
            removals.append(line[len(REMOVED):])
        else:
            entries.append(line)
    return headers, entries, removals


def ship(package: Package, write: bool) -> Shipment:
    shipped_bom, shipped_lines = read_lines(package.shipped)
    unshipped_bom, unshipped_lines = read_lines(package.unshipped)
    shipped_headers, shipped_entries, stray = split(shipped_lines)
    if stray:
        raise MalformedApiFile(f"{package.shipped}: a Shipped file cannot hold {REMOVED} markers")
    unshipped_headers, entries, removals = split(unshipped_lines)

    shipped_set = set(shipped_entries)
    entry_set = set(entries)
    removal_set = set(removals)
    # The declared API after shipping is everything in either file minus every removal: a
    # marker deletes a shipped entry, or cancels one added in the same Unshipped file.
    current = (shipped_set | entry_set) - removal_set

    if write:
        write_lines(package.shipped, shipped_bom, shipped_headers + sorted(current))
        write_lines(package.unshipped, unshipped_bom, unshipped_headers)

    return Shipment(
        package.name,
        added=len(entry_set - shipped_set - removal_set),
        removed=len(removal_set & shipped_set),
        cancelled=len((removal_set & entry_set) - shipped_set),
        duplicates=len(entry_set & shipped_set),
        dangling=[target for target in removals if target not in shipped_set and target not in entry_set],
    )


def report(shipments: list[Shipment], wrote: bool) -> None:
    verb = "Shipped" if wrote else "Would ship"
    for s in shipments:
        if s.is_empty:
            continue
        line = f"  {s.name}: +{s.added} shipped, -{s.removed} removed"
        if s.cancelled:
            line += f", {s.cancelled} added then removed (never ships)"
        if s.duplicates:
            line += f", {s.duplicates} already shipped (dropped)"
        print(line)
        for target in s.dangling:
            print(f"    removal marker for an entry in neither file (nothing to do): {target}")
    added = sum(s.added for s in shipments)
    removed = sum(s.removed for s in shipments)
    touched = sum(1 for s in shipments if not s.is_empty)
    print(f"{verb} {added} entries and {removed} removals across {touched} packages.")


def check(root: Path) -> int:
    offenders = []
    for package in packages(root):
        _, entries, removals = split(read_lines(package.unshipped)[1])
        if entries or removals:
            offenders.append((package.name, len(entries), len(removals)))
    if not offenders:
        print("No unshipped public API.")
        return 0
    for name, entries, removals in offenders:
        print(
            f"::error::{name}: {entries} unshipped entries and {removals} removals — "
            "a tag publishes only after `python3 tools/ship-public-api.py` has run in a release PR."
        )
    return 1


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="fail if any entry is unshipped; write nothing")
    mode.add_argument("--dry-run", action="store_true", help="report what would ship; write nothing")
    parser.add_argument("--root", type=Path, default=repo_root(), help="repository root (default: this script's parent)")
    args = parser.parse_args(argv)

    if args.check:
        return check(args.root)
    try:
        shipments = [ship(package, write=not args.dry_run) for package in packages(args.root)]
    except MalformedApiFile as error:
        print(f"::error::{error}")
        return 2
    report(shipments, wrote=not args.dry_run)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
