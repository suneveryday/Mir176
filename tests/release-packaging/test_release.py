"""Release guards: hostile archives, manifest drift and diagnostic-only redaction."""
import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


guard = load("guard", "check-release-package.py")


class ArchiveTests(unittest.TestCase):
    def test_launcher_archive_rejects_game_and_local_configuration(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "launcher.zip"
            files = {"Mir176Launcher.exe": b"MZfixture", "README.txt": b"Launcher only", "launcher-build.json": b"{}"}
            def write():
                with zipfile.ZipFile(path, "w") as z:
                    for name, data in files.items():
                        z.writestr(name, data)
                    z.writestr("package-manifest.json", json.dumps({"project": "Mir2Classic176", "files": {
                        n: hashlib.sha256(b).hexdigest() for n, b in files.items()}}))
            write()
            self.assertEqual(guard.check(path, "launcher"), 3)
            files["Mir2Classic176.exe"] = b"excluded game"
            write()
            with self.assertRaisesRegex(ValueError, "standalone launcher"):
                guard.check(path, "launcher")
            del files["Mir2Classic176.exe"]
            files["Mir2Classic176.ini"] = b"[Server]"
            write()
            with self.assertRaisesRegex(ValueError, "standalone launcher"):
                guard.check(path, "launcher")

    def test_server_assembler_omits_host_and_service_symbols(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            built, host, content, out = [root / n for n in ("build", "host", "content", "out")]
            for service in ("LoginSrv", "DBSrv", "GameSrv", "LoginGate", "SelGate", "GameGate"):
                directory = built / service
                directory.mkdir(parents=True)
                for name in (service + ".dll", "OpenMir2.dll", service + ".pdb"):
                    (directory / name).write_bytes(b"fixture")
                for name in (service + ".deps.json", service + ".runtimeconfig.json"):
                    (directory / name).write_text("{}")
            plugin = built / "DBSvr.Storage.MySQL"
            plugin.mkdir()
            (plugin / "OpenMir2.dll").write_bytes(b"fixture")
            (plugin / "DBSrv.Storage.MySQL.dll").write_bytes(b"fixture")
            host.mkdir()
            (host / "ClassicHost.dll").write_bytes(b"fixture")
            (host / "ClassicHost.pdb").write_bytes(b"private symbols")
            (content / "mysql-init").mkdir(parents=True)
            (content / "GameSrv").mkdir()
            (content / "GameSrv/Map").mkdir()
            payload = b'isolated map fixture'
            (content / "GameSrv/Map/0.map").write_bytes(payload)
            sha = hashlib.sha256(payload).hexdigest()
            (content / "content-manifest.json").write_text(json.dumps({'files': {'GameSrv/Map/0.map': sha}}))
            descriptor = root / 'map-resources.json'
            descriptor.write_text(json.dumps({'project': 'Mir2Classic176', 'resource_set_id': 'assets-' + 'a'*12,
                'archive': {'name': 'maps.zip', 'bytes': 1, 'sha256': 'b'*64},
                'files': [{'Path': 'Map/0.map', 'Size': len(payload), 'Sha256': sha}]}))
            subprocess.run([sys.executable, str(ROOT / "tools/prepare-docker-release.py"),
                            "--output", str(out), "--server-build", str(built),
                            "--host-build", str(host), "--content", str(content), "--map-resource-descriptor", str(descriptor)],
                           check=True, capture_output=True)
            self.assertFalse(list(out.rglob("*.pdb")))
            self.assertFalse(list(out.rglob("*.map")))
            self.assertTrue((out / "map-resources.json").is_file())
            self.assertTrue((out / "LICENSE.txt").is_file())
            manifest = json.loads((out / "package-manifest.json").read_text())
            self.assertIn("host/ClassicHost.dll", manifest["files"])
            archive = root / "server.zip"
            with zipfile.ZipFile(archive, "w") as z:
                for file in out.rglob("*"):
                    if file.is_file():
                        z.write(file, file.relative_to(out).as_posix())
            self.assertGreater(guard.check(archive, "server"), 20)

    def archive(self, directory, extra=None, manifest_change=None):
        files = {"Mir2Classic176.exe": b"MZfixture", "Configure-Classic.ps1": b"# offline configure\n",
                 "Data/README.txt": b"Keep this directory.\n"}
        files.update(extra or {})
        manifest = {"project": "Mir2Classic176", "version": "0.1.5", "files": {
            n: hashlib.sha256(b).hexdigest() for n, b in files.items()}}
        if manifest_change:
            manifest_change(manifest)
        path = Path(directory) / "candidate.zip"
        with zipfile.ZipFile(path, "w") as z:
            for name, data in files.items():
                z.writestr(name, data)
            z.writestr("package-manifest.json", json.dumps(manifest))
        return path

    def test_clean_complete_manifest_passes(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(guard.check(self.archive(d), "client"), 3)

    def test_hash_drift_fails(self):
        with tempfile.TemporaryDirectory() as d:
            path = self.archive(d, manifest_change=lambda m: m["files"].update({"Mir2Classic176.exe": "0" * 64}))
            with self.assertRaisesRegex(ValueError, "hash mismatch"):
                guard.check(path, "client")

    def test_undeclared_file_fails(self):
        with tempfile.TemporaryDirectory() as d:
            path = self.archive(d, manifest_change=lambda m: m["files"].pop("Data/README.txt"))
            with self.assertRaisesRegex(ValueError, "coverage"):
                guard.check(path, "client")

    def test_install_configuration_fails(self):
        with tempfile.TemporaryDirectory() as d:
            path = self.archive(d, {"Mir2Classic176.ini": b"[Server]\n"})
            with self.assertRaisesRegex(ValueError, "unexpected client"):
                guard.check(path, "client")

    def test_unsafe_name_symlink_and_case_collision(self):
        for bad in ("../outside", "/outside", "Q:/outside", "Map/../outside", "Map\\outside"):
            self.assertFalse(guard.safe_name(bad))
        with tempfile.TemporaryDirectory() as d:
            path = self.archive(d)
            with zipfile.ZipFile(path, "a") as z:
                entry = zipfile.ZipInfo("Map/link")
                entry.create_system = 3
                entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                z.writestr(entry, "target")
            with self.assertRaisesRegex(ValueError, "symlink"):
                guard.check(path, "client")
        with tempfile.TemporaryDirectory() as d:
            path = self.archive(d, {"data/readme.TXT": b"duplicate"})
            with self.assertRaisesRegex(ValueError, "duplicate"):
                guard.check(path, "client")

    def test_utf16_local_diagnostic_path_is_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            marker = ("D" + ":" + chr(92) + "local-build" + chr(92)).encode("utf-16le")
            path = self.archive(d, {"Mir2Classic176.exe": b"MZ" + marker})
            with self.assertRaisesRegex(ValueError, "privacy rejection"):
                guard.check(path, "client")

    def test_vendor_exception_cannot_be_used_by_changed_binary(self):
        sample = b"D" + b":" + bytes([92]) + b"a" + bytes([92]) + b"fixture"
        self.assertIn("personal-workspace", guard.content_reasons(
            "Collections.Pooled.dll", sample, hashlib.sha256(sample).hexdigest()))
        marker = ("-----BEGIN " + "PRIVATE KEY-----").encode()
        self.assertIn("private-key", guard.content_reasons(
            "MySqlConnector.dll", marker, hashlib.sha256(marker).hexdigest()))


if __name__ == "__main__":
    unittest.main()
