"""No Docker connection: verify preparation, namespace isolation and failure paths."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("deploy", ROOT / "deployment/deploy.py")
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        for name in ("Dockerfile", "compose.yaml", "deploy.py", "deploy.sh", ".dockerignore"):
            shutil.copyfile(ROOT / "deployment" / name, self.directory / name)
        shutil.copyfile(ROOT / "tools/configure-classic-release.py", self.directory / "configure.py")
        for service in ("LoginSrv", "DBSrv", "GameSrv", "LoginGate", "SelGate", "GameGate"):
            path = self.directory / "services" / service / (service + ".dll")
            path.parent.mkdir(parents=True)
            path.write_bytes(b"isolated fixture")
        for name in ("host/ClassicHost.dll", "services/DBSrv/DBSrv.Storage.MySQL.dll"):
            path = self.directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"isolated fixture")
        self.manifest()
        self.commands = []
        self.conflict = None
        self.failure = None
        self.state = {"Status": "running", "Health": {"Status": "healthy"}}

    def tearDown(self):
        self.temp.cleanup()

    def manifest(self):
        files = {p.relative_to(self.directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in self.directory.rglob("*") if p.is_file() and p.name != "package-manifest.json" and p.suffix != ".zip"}
        (self.directory / "package-manifest.json").write_text(json.dumps({
            "project": "Mir2Classic176", "version": "0.1.4", "files": files}))

    def execute(self, command, directory):
        self.commands.append(command)
        if command[0] == sys.executable:
            subprocess.run(command, cwd=directory, check=True, capture_output=True)
            return ""
        self.assertEqual(command[0], "docker")
        if command[1:2] == ["info"]:
            return "linux"
        if command[1:4] == ["compose", "version", "--short"]:
            return "2.30.0"
        if self.failure and self.failure in command:
            raise RuntimeError("simulated Docker failure")
        if self.conflict and command[1:1 + len(self.conflict)] == self.conflict:
            return "existing-resource"
        if command[-3:] == ["ps", "--all", "--quiet"]:
            return "fixture-database\nfixture-game"
        if command[1] == "inspect":
            return json.dumps(self.state)
        return ""

    def start(self, **kwargs):
        deploy.deploy(self.directory, "play.example", [7000, 7100, 7200], "fresh-classic",
                      execute=self.execute, port_check=lambda ports: None, environment={}, **kwargs)

    def external_maps(self):
        payload = b'isolated external map'
        rows = [{'Path': 'Map/0.map', 'Size': len(payload), 'Sha256': hashlib.sha256(payload).hexdigest()}]
        archive = self.directory / 'maps.zip'
        with zipfile.ZipFile(archive, 'w') as z:
            z.writestr('Map/0.map', payload)
            z.writestr('map-package-manifest.json', json.dumps({'resource_set_id': 'assets-'+'a'*12, 'files': rows}))
        descriptor = {'project': 'Mir2Classic176', 'resource_set_id': 'assets-'+'a'*12,
            'archive': {'name': archive.name, 'bytes': archive.stat().st_size,
                        'sha256': hashlib.sha256(archive.read_bytes()).hexdigest()}, 'files': rows}
        (self.directory / 'map-resources.json').write_text(json.dumps(descriptor))
        (self.directory / 'content-manifest.json').write_text(json.dumps({'files': {'GameSrv/Map/0.map': rows[0]['Sha256']}}))
        shutil.copyfile(ROOT/'deployment/import-resources.py', self.directory/'import-resources.py')
        self.manifest()
        manifest_path = self.directory/'package-manifest.json'
        manifest = json.loads(manifest_path.read_text());manifest['external_map_resources'] = descriptor['resource_set_id']
        manifest_path.write_text(json.dumps(manifest))
        return archive

    def test_external_maps_import_before_new_docker_deployment(self):
        archive = self.external_maps()
        self.start(resource_archive=archive)
        self.assertEqual((self.directory/'services/GameSrv/Map/0.map').read_bytes(), b'isolated external map')
        deploy.verify_package(self.directory)

    def test_bad_external_maps_stop_before_docker_or_secrets(self):
        archive = self.external_maps();archive.write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'archive hash'):
            self.start(resource_archive=archive)
        self.assertFalse(self.commands)
        self.assertFalse((self.directory/'.env').exists())
        self.assertFalse((self.directory/'services/GameSrv/Map').exists())

    def test_existing_configuration_and_lock_refuse_map_import(self):
        archive = self.external_maps()
        for name in ('.env', '.deploy.lock'):
            path = self.directory/name;path.write_text('preserve')
            with self.assertRaises((ValueError, FileExistsError)):
                self.start(resource_archive=archive)
            self.assertEqual(path.read_text(), 'preserve')
            self.assertFalse((self.directory/'services/GameSrv/Map').exists())
            path.unlink()

    def test_new_install_prepares_then_builds_starts_and_checks_health(self):
        self.start()
        env = (self.directory / ".env").read_text()
        self.assertIn("CLASSIC_PUBLIC_ADDRESS=play.example", env)
        self.assertIn("CLASSIC_IMAGE=mir176/server:fresh-classic-0.1.4", env)
        self.assertIn("COMPOSE_PROJECT_NAME=fresh-classic", env)
        self.assertEqual(len((self.directory / ".secrets/db_password").read_text().strip()), 64)
        self.assertNotEqual((self.directory / ".secrets/db_password").read_bytes(),
                            (self.directory / ".secrets/db_root_password").read_bytes())
        build = next(i for i, c in enumerate(self.commands) if "build" in c)
        up = next(i for i, c in enumerate(self.commands) if "up" in c)
        self.assertLess(build, up)
        self.assertEqual(sum(c[1] == "inspect" for c in self.commands), 2)
        for command in self.commands:
            if "up" in command or "config" in command or command[-3:] == ["ps", "--all", "--quiet"]:
                self.assertIn("fresh-classic", command)
            self.assertFalse(set(command) & {"down", "rm", "prune", "--volumes"})
        self.assertFalse((self.directory / ".deploy.lock").exists())

    def test_existing_configuration_refused_without_any_docker_command(self):
        (self.directory / ".env").write_text("KEEP")
        with self.assertRaisesRegex(ValueError, "Existing configuration"):
            self.start()
        self.assertEqual((self.directory / ".env").read_text(), "KEEP")
        self.assertFalse(self.commands)

    def test_existing_container_volume_network_or_image_refused(self):
        for conflict in (["ps"], ["volume", "ls"], ["network", "ls"], ["image", "ls"]):
            self.conflict = conflict
            with self.assertRaisesRegex(ValueError, "already exist"):
                self.start()
            self.assertFalse((self.directory / ".env").exists())
            self.assertFalse(any("build" in c or "up" in c for c in self.commands))

    def test_unlabelled_restored_volumes_or_networks_are_not_reused(self):
        for kind, name in (("volume", "fresh-classic_classic_database"), ("network", "fresh-classic_default")):
            def execute(command, directory):
                if command[1:3] == [kind, "ls"] and "--filter" not in command:
                    return name
                return self.execute(command, directory)
            with self.assertRaisesRegex(ValueError, "Unlabelled"):
                deploy.deploy(self.directory, "play.example", [7000, 7100, 7200], "fresh-classic",
                              execute=execute, environment={}, port_check=lambda ports: None)
            self.assertFalse((self.directory / ".env").exists())

    def test_package_corruption_stops_before_docker_or_configuration(self):
        (self.directory / "Dockerfile").write_text("changed")
        with self.assertRaisesRegex(ValueError, "hash mismatch"):
            self.start()
        self.assertFalse(self.commands)

    def test_unlisted_build_input_is_not_sent_to_docker(self):
        (self.directory / "services/GameSrv/local-settings.ini").write_text("local only")
        with self.assertRaisesRegex(ValueError, "Unexpected file"):
            self.start()
        self.assertFalse(self.commands)

    def test_failed_build_retains_configuration_and_does_not_start(self):
        self.failure = "build"
        with self.assertRaises(RuntimeError):
            self.start()
        self.assertTrue((self.directory / ".env").is_file())
        self.assertTrue((self.directory / ".secrets/db_password").is_file())
        self.assertFalse(any("up" in c for c in self.commands))
        self.assertFalse((self.directory / ".deploy.lock").exists())

    def test_unhealthy_or_timed_out_containers_retained(self):
        self.state["Health"]["Status"] = "unhealthy"
        with self.assertRaisesRegex(RuntimeError, "health failed"):
            self.start()
        self.assertFalse(any("down" in c or "rm" in c for c in self.commands))
        self.assertTrue((self.directory / ".env").is_file())

    def test_timeout_without_deleting_any_data(self):
        self.state["Health"]["Status"] = "starting"
        clock = iter([0, 5])
        with self.assertRaisesRegex(RuntimeError, "timed out"):
            self.start(timeout=1, now=lambda: next(clock), sleep=lambda _: None)
        self.assertTrue((self.directory / ".secrets").is_dir())

    def test_existing_lock_is_not_removed(self):
        lock = self.directory / ".deploy.lock"
        lock.write_text("other owner")
        with self.assertRaises(FileExistsError):
            self.start()
        self.assertEqual(lock.read_text(), "other owner")

    def test_address_ports_and_namespace_validation(self):
        for address, ports, project in [
            ("https://play.example", [7000, 7100, 7200], "fresh"),
            ("999.1.2.3", [7000, 7100, 7200], "fresh"),
            ("play.example", [7000, 7000, 7200], "fresh"),
            ("play.example", [0, 7100, 7200], "fresh"),
            ("play.example", [7000, 7100, 7200], "../other"),
        ]:
            with self.assertRaises(ValueError):
                deploy.validate(address, ports, project)

    def test_inherited_environment_cannot_override_fresh_project(self):
        with self.assertRaisesRegex(ValueError, "inherited"):
            deploy.deploy(self.directory, "play.example", [7000, 7100, 7200], "fresh",
                          environment={"CLASSIC_IMAGE": "other"}, execute=self.execute)
        self.assertFalse(self.commands)

    def test_port_conflict_stops_before_generating_secrets(self):
        def conflict(ports):
            raise OSError("occupied test port")
        with self.assertRaises(OSError):
            deploy.deploy(self.directory, "play.example", [7000, 7100, 7200], "fresh",
                          environment={}, execute=self.execute, port_check=conflict)
        self.assertFalse((self.directory / ".secrets").exists())


if __name__ == "__main__":
    unittest.main()
