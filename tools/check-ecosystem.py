#!/usr/bin/env python3
"""Check the actual multi-repository workspace and pinned dependency commits."""
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]


def main():
    lock = json.loads((ROOT / "ecosystem.lock.json").read_text())
    primary = json.loads((ROOT / "compatibility.json").read_text())["project"]
    specs, errors = {}, []
    for project, entry in lock["projects"].items():
        root = ROOT / entry["path"]
        guard = root / "tools/check-contract.py"
        if not guard.is_file():
            errors.append(f"missing checkout: {project} at {entry['path']}")
            continue
        module_spec = importlib.util.spec_from_file_location(project, guard)
        module = importlib.util.module_from_spec(module_spec)
        module_spec.loader.exec_module(module)
        spec, issues = module.check(root)
        specs[project] = spec
        errors.extend(f"{project}: {issue}" for issue in issues)
        if spec["project"] != project or spec["version"] != entry["version"]:
            errors.append(f"{project}: identity/version differs from ecosystem.lock.json")
        if entry["revision"] != "self":
            head = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
            if head != entry["revision"]:
                errors.append(f"{project}: HEAD differs from pinned commit; record and review the new combination")
            if subprocess.run(["git", "-C", str(root), "diff", "--quiet", "HEAD", "--"], check=False).returncode:
                errors.append(f"{project}: uncommitted tracked changes; commit before locking the combination")
    if len(specs) == len(lock["projects"]):
        provider = specs[primary]["provides"]
        for project, spec in specs.items():
            for contract, revision in spec["requires"].items():
                if provider.get(contract) != revision:
                    errors.append(f"{project}: requires {contract}/{revision}, unavailable in pinned primary project")
        server = (ROOT / "server/src/OpenMir2/Messages.cs").read_text(encoding="utf-8-sig")
        client = (ROOT / lock["projects"]["Mir176Client"]["path"] / "client/current-source/Source/Common/Grobal2.pas").read_text(errors="replace")
        bot = (ROOT / "Mir2bot/bot/protocol.py").read_text()
        server_ids = dict(re.findall(r"const int ((?:CM|SM)_\w+)\s*=\s*(\d+)\s*;", server))
        client_ids = dict(re.findall(r"\b((?:CM|SM)_\w+)\s*=\s*(\d+)\s*;", client))
        bot_ids = dict(re.findall(r"^((?:CM|SM)_\w+)\s*=\s*(\d+)\s*$", bot, re.M))
        for name, value in bot_ids.items():
            canonical = lock.get("message_aliases", {}).get(name, name)
            if server_ids.get(canonical) != value or client_ids.get(canonical) != value:
                errors.append(f"wire message mismatch: {name} across server, PC and bot")
        for name in ("SM_EAT_OK", "SM_ADDITEM"):
            if server_ids.get(name) != client_ids.get(name):
                errors.append(f"scroll wire message mismatch: {name}")
        print(f"Checked {len(bot_ids)} shared bot message IDs and PC scroll message IDs")
    if errors:
        print("\n".join("FAIL: " + error for error in errors), file=sys.stderr)
        return 1
    print("PASS all-project source contracts, dependency declarations and pinned commits")
    print("Runtime evidence is separate; this source checker performs no live operations")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(1)
