#!/usr/bin/env python3
"""Validate a clean release ZIP, its complete manifest and source privacy rules."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath
import re
import stat
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("source_privacy", ROOT / "tools/check-privacy.py")
privacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(privacy)

# Immutable upstream assemblies: only their public CI CodeView locator is exempt.
VENDOR_HASHES = {
    "McMaster.Extensions.CommandLineUtils.dll": "e2ede6013683588c60f502cd236db4e43b65c53be9ca150b29cf7218f0bda203",
    "Collections.Pooled.dll": "defd808d64e1d9201ffc994b2e7f76ca1f6715d84815e022e781c0a069654879",
}
MYSQL_PARSER_HASH = "84d297e722db70c78db88ed5f8b273686cb06851ad4e1c14cc7465c4bc482302"
TEXT = {".md", ".txt", ".json", ".yaml", ".yml", ".ini", ".sql", ".py", ".ps1", ".cmd", ".sh"}


def safe_name(name):
    path = PurePosixPath(name)
    return (bool(name) and not path.is_absolute() and "\\" not in name
            and ":" not in name and "\x00" not in name
            and all(part not in ("", ".", "..") for part in name.rstrip("/").split("/")))


def private_name(name):
    parts = PurePosixPath(name).parts
    return (any(x.lower() in {"private", "access", ".secrets", ".git", "logs", "backup"} for x in parts)
            or any(x.lower().startswith(".env") for x in parts)
            or PurePosixPath(name).suffix.lower() in {".pdb", ".dcu", ".log", ".pem", ".pfx", ".key", ".bak"}
            or re.search(r"credentials|classic-gm-settings|deployment[.]local|classic-gm-instance|pending-command", name, re.I))


def content_reasons(name, data, digest):
    reasons = privacy.inspect(name, data)
    if VENDOR_HASHES.get(PurePosixPath(name).name) == digest:
        pattern = privacy.RULES["personal-workspace"]
        # Validate every flagged locator, never suppress an arbitrary local path.
        matches = list(pattern.finditer(data))
        ci_prefix = ("D" + ":" + chr(92) + "a" + chr(92)).encode()
        if matches and all(data[m.start():].startswith(ci_prefix) for m in matches):
            reasons = [x for x in reasons if x != "personal-workspace"]
    # This pinned dependency contains PEM parser delimiters, not a key body.
    # A different binary (including any appended credential) cannot use this exception.
    if PurePosixPath(name).name == "MySqlConnector.dll" and digest == MYSQL_PARSER_HASH:
        reasons = [x for x in reasons if x != "private-key"]
    return sorted(set(reasons))


def check(path, kind):
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        seen = set()
        for entry in entries:
            if privacy.known_value_reasons(entry.filename.encode()):
                raise ValueError("known private archive member")
            if not safe_name(entry.filename):
                raise ValueError("unsafe archive member")
            if stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError("archive symlink")
            folded = entry.filename.rstrip("/").casefold()
            if folded in seen:
                raise ValueError("duplicate archive member")
            seen.add(folded)
            if private_name(entry.filename):
                raise ValueError("private archive member")
        manifest_name = "resource-package-manifest.json" if kind == "client-resources" else "package-manifest.json"
        manifest = json.loads(archive.read(manifest_name))
        files = manifest["files"]
        actual = {x.filename for x in entries if not x.is_dir() and x.filename != manifest_name}
        if set(files) != actual:
            raise ValueError("manifest coverage mismatch")
        if manifest.get("project") != "Mir2Classic176":
            raise ValueError("wrong project manifest")
        if kind == "client":
            allowed = {"Mir2Classic176.exe", "Mir176Launcher.exe", "Configure-Classic.ps1", "README.txt", "build-evidence.json",
                       "distribution-transform.json", "resource-provenance.json", "VALIDATION_SCOPE.txt", "THIRD_PARTY_NOTICES.txt"}
            if not {"Mir2Classic176.exe", "Configure-Classic.ps1", "Data/README.txt"} <= actual:
                raise ValueError("missing client installation input")
            if any(n not in allowed and n.split("/")[0] not in {"Map", "Resource", "Wav", "Data"} for n in actual):
                raise ValueError("unexpected client installation file")
        elif kind == "server":
            required = {"Dockerfile", "compose.yaml", "configure.py", "host/ClassicHost.dll",
                        "deploy.py", "deploy.sh", ".dockerignore", "services/DBSrv/DBSrv.Storage.MySQL.dll"}
            required |= {f"services/{service}/{service}.dll" for service in
                         ("LoginSrv", "DBSrv", "GameSrv", "LoginGate", "SelGate", "GameGate")}
            required |= {"import-resources.py", "map-resources.json", "content-manifest.json"}
            if not required <= actual:
                raise ValueError("missing server component")
            if any(PurePosixPath(name).suffix.lower() in {".map", ".wil", ".wix", ".wav"} for name in actual):
                raise ValueError("game assets belong in the independent client resource repository")
            descriptor = json.loads(archive.read("map-resources.json"))
            content = json.loads(archive.read("content-manifest.json"))["files"]
            map_rows = descriptor["files"]
            expected_maps = {name: sha for name, sha in content.items() if name.startswith("GameSrv/Map/")}
            if (manifest.get("external_map_resources") != descriptor.get("resource_set_id")
                    or not expected_maps
                    or expected_maps != {"GameSrv/" + row["Path"]: row["Sha256"] for row in map_rows}):
                raise ValueError("external map resources do not match server content")
        else:
            if actual != {"Mir176Launcher.exe", "README.txt", "launcher-build.json"}:
                raise ValueError("unexpected or missing standalone launcher file")
        for name in [manifest_name, *sorted(actual)]:
            sha = hashlib.sha256()
            payload = bytearray()
            is_inspected = (PurePosixPath(name).suffix.lower() in TEXT | {".exe", ".dll"}
                            or PurePosixPath(name).name == "Dockerfile")
            with archive.open(name) as stream:
                tail = b""
                while chunk := stream.read(1024 * 1024):
                    sha.update(chunk)
                    if is_inspected:
                        payload.extend(chunk)
                    scan = tail + chunk
                    if privacy.known_value_reasons(scan):
                        raise ValueError("known private value in release")
                    tail = scan[-max(4096, privacy.known_value_overlap()):]
            digest = sha.hexdigest()
            if name in actual and files[name] != digest:
                raise ValueError("file hash mismatch")
            if is_inspected and content_reasons(name, bytes(payload), digest):
                # Print reason categories and relative artifact names, never matched contents.
                raise ValueError("privacy rejection: " + ",".join(content_reasons(name, bytes(payload), digest)))
        if kind == "server":
            shared = {files[f"services/{service}/OpenMir2.dll"] for service in
                      ("LoginSrv", "DBSrv", "GameSrv", "LoginGate", "SelGate", "GameGate")}
            if len(shared) != 1:
                raise ValueError("inconsistent shared server assemblies")
        return len(actual)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kind", choices=("server", "launcher"), required=True)
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    print(f"PASS {args.kind} release: {check(args.archive, args.kind)} declared files, hashes and privacy verified")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, zipfile.BadZipFile):
        raise SystemExit("FAIL release check could not complete; details withheld") from None
