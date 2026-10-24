#!/usr/bin/env python3
"""Offline release contract guard. Never connects to game services."""
import hashlib
import json
from pathlib import Path
import re
import sys


def check(root):
    spec = json.loads((root / "compatibility.json").read_text())
    lock = json.loads((root / "contracts.lock.json").read_text())
    errors = []
    version = (root / "VERSION").read_text().strip()
    if not re.fullmatch(r"\d+\.\d+\.\d+", version) or spec["version"] != version:
        errors.append("VERSION and compatibility.json must contain the same semantic version")
    if lock["project"] != spec["project"]:
        errors.append("contract lock belongs to a different project")
    if not lock["files"]:
        errors.append("contract lock has no protected files")
    for name, expected in lock["files"].items():
        path = root / name
        if not path.resolve().is_relative_to(root.resolve()):
            errors.append(f"unsafe contract path: {name}")
        elif not path.is_file():
            errors.append(f"missing contract file: {name}")
        elif hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            errors.append(f"contract changed: {name}; review all three projects before updating the lock")
    review = root / "docs/RELEASE_COMPATIBILITY.md"
    if hashlib.sha256(review.read_bytes()).hexdigest() != lock["review_sha256"]:
        errors.append("release compatibility review changed; bind the reviewed text in contracts.lock.json")
    for field in ("provides", "requires"):
        for contract, revision in spec[field].items():
            if not contract or not re.fullmatch(r"v[1-9]\d*", revision):
                errors.append(f"invalid {field} declaration: {contract}")
    return spec, errors


if __name__ == "__main__":
    try:
        spec, errors = check(Path(__file__).resolve().parents[1])
    except (OSError, ValueError, KeyError) as error:
        print(f"FAIL: {type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(1)
    if errors:
        print("\n".join("FAIL: " + error for error in errors), file=sys.stderr)
        sys.exit(1)
    print(f"PASS {spec['project']} {spec['version']}: protected source and release review match")
