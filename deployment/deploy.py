#!/usr/bin/env python3
"""Single-entry Docker deployment for a new isolated classic installation."""
import argparse
import hashlib
import ipaddress
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import socket
import subprocess
import sys
import time


def validate(address, ports, project):
    if not re.fullmatch(r"[A-Za-z0-9.-]{1,30}", address):
        raise ValueError("Enter an IPv4 address or hostname of up to 30 ASCII characters")
    if re.fullmatch(r"[0-9.]+", address):
        ipaddress.IPv4Address(address)
    elif any(not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?", part)
             for part in address.split(".")):
        raise ValueError("Invalid hostname")
    if len(set(ports)) != 3 or any(not 1 <= p <= 65535 for p in ports):
        raise ValueError("Use three different ports between 1 and 65535")
    if not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,39}", project):
        raise ValueError("Use a lowercase project name of up to 40 letters, digits, underscores or hyphens")


def map_importer(directory):
    spec = importlib.util.spec_from_file_location("server_map_import", directory / "import-resources.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def verify_package(directory):
    manifest = json.loads((directory / "package-manifest.json").read_text())
    if manifest.get("project") != "Mir2Classic176":
        raise ValueError("Wrong package project")
    for name, expected in manifest["files"].items():
        relative = PurePosixPath(name)
        if relative.is_absolute() or ".." in relative.parts or "\\" in name or ":" in name:
            raise ValueError("Unsafe manifest path")
        path = directory / name
        if path.is_symlink() or not path.resolve().is_relative_to(directory.resolve()):
            raise ValueError("Package member escapes its directory")
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError("Package hash mismatch; extract a clean archive")
    required = {"compose.yaml", "Dockerfile", "configure.py", "deploy.py", "deploy.sh", ".dockerignore",
                "host/ClassicHost.dll", "services/DBSrv/DBSrv.Storage.MySQL.dll"}
    required |= {f"services/{service}/{service}.dll" for service in
                 ("LoginSrv", "DBSrv", "GameSrv", "LoginGate", "SelGate", "GameGate")}
    if not required <= manifest["files"].keys():
        raise ValueError("Incomplete Docker package")
    maps = {}
    if manifest.get("external_map_resources"):
        if not {"import-resources.py", "map-resources.json", "content-manifest.json"} <= manifest["files"].keys():
            raise ValueError("Incomplete external map resource inputs")
        helper = map_importer(directory)
        descriptor, maps = helper.expected_maps(directory)
        if descriptor["resource_set_id"] != manifest["external_map_resources"]:
            raise ValueError("Map resource set differs from package")
        content = json.loads((directory / "content-manifest.json").read_text())["files"]
        expected = {"services/" + name: sha for name, sha in content.items() if name.startswith("GameSrv/Map/")}
        if expected != {name: row["Sha256"] for name, row in maps.items()}:
            raise ValueError("Map resources differ from server content")
        helper.check_installed(directory, maps)
    # Only these verified trees enter the Docker build context.
    for base in ("host", "services"):
        for path in (directory / base).rglob("*"):
            if path.is_symlink() or (path.is_file() and path.relative_to(directory).as_posix() not in manifest["files"] and path.relative_to(directory).as_posix() not in maps):
                raise ValueError("Unexpected file in Docker build inputs; extract a clean archive")
    return manifest


def run(command, directory):
    result = subprocess.run(command, cwd=directory, capture_output=True, text=True)
    if result.returncode:
        # Operational output stays on the operator's console, never in the release.
        if result.stderr:
            print(result.stderr, file=sys.stderr)
        raise RuntimeError("Command failed; existing configuration and data were retained")
    return result.stdout.strip()


def check_ports(ports):
    sockets = []
    try:
        for port in ports:
            listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sockets.append(listener)
            listener.bind(("0.0.0.0", port))
    finally:
        for listener in sockets:
            listener.close()


def deploy(directory, address, ports, project, timeout=300, execute=run,
           port_check=check_ports, now=time.monotonic, sleep=time.sleep, environment=None, resource_archive=None):
    directory = Path(directory).resolve()
    validate(address, ports, project)
    if timeout < 1:
        raise ValueError("Health timeout must be positive")
    if any(key.startswith(("CLASSIC_", "COMPOSE_")) for key in
           (os.environ if environment is None else environment)):
        raise ValueError("Unset inherited CLASSIC_/COMPOSE_ variables before a new installation")
    manifest = verify_package(directory)
    version = manifest["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Invalid server component version")
    if (directory / ".env").exists() or (directory / ".secrets").exists():
        raise ValueError("Existing configuration found; use the documented maintenance/upgrade procedure")
    image = f"mir176/server:{project}-{version}"
    compose = ["docker", "compose", "--project-name", project, "--file", "compose.yaml"]
    lock = directory / ".deploy.lock"
    fd = os.open(lock, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    os.close(fd)
    try:
        if manifest.get("external_map_resources"):
            # Offline resources are imported only after refusing existing installs and acquiring the lock.
            map_importer(directory).prepare(directory, resource_archive)
            verify_package(directory)
        if execute(["docker", "info", "--format", "{{.OSType}}"], directory) != "linux":
            raise ValueError("Docker must use Linux containers")
        compose_version = execute(["docker", "compose", "version", "--short"], directory)
        match = re.match(r"v?(\d+)\.", compose_version)
        if not match or int(match[1]) < 2:
            raise ValueError("Docker Compose v2 or later is required")
        label = "label=com.docker.compose.project=" + project
        queries = [
            ["docker", "ps", "--all", "--filter", label, "--format", "{{.ID}}"],
            ["docker", "volume", "ls", "--filter", label, "--format", "{{.Name}}"],
            ["docker", "network", "ls", "--filter", label, "--format", "{{.ID}}"],
            ["docker", "image", "ls", "--quiet", "--filter", "reference=" + image],
        ]
        if any(execute(query, directory) for query in queries):
            raise ValueError("Project resources or image already exist; choose a new project or use maintenance")
        # Manually restored volumes/networks may have lost Compose labels.
        volume_names = set(execute(["docker", "volume", "ls", "--format", "{{.Name}}"], directory).splitlines())
        network_names = set(execute(["docker", "network", "ls", "--format", "{{.Name}}"], directory).splitlines())
        if volume_names & {project + "_classic_database", project + "_classic_game"} or project + "_default" in network_names:
            raise ValueError("Unlabelled project resources already exist; refusing to reuse existing data")
        port_check(ports)
        execute([sys.executable, "configure.py", "--directory", str(directory), "--address", address,
                 "--login-port", str(ports[0]), "--select-port", str(ports[1]), "--game-port", str(ports[2])], directory)
        with (directory / ".env").open("a") as env:
            env.write("CLASSIC_IMAGE=" + image + "\n")
            env.write("COMPOSE_PROJECT_NAME=" + project + "\n")
        execute(compose + ["config", "--quiet"], directory)
        print("Building the isolated Docker image...", flush=True)
        execute(["docker", "build", "--platform", "linux/amd64", "--tag", image, "."], directory)
        print("Starting the new Docker project...", flush=True)
        execute(compose + ["up", "--detach"], directory)
        deadline = now() + timeout
        while True:
            ids = execute(compose + ["ps", "--all", "--quiet"], directory).splitlines()
            states = [json.loads(execute(["docker", "inspect", "--format", "{{json .State}}", cid], directory)) for cid in ids]
            if len(states) == 2 and all(s.get("Status") == "running" and
                                       s.get("Health", {}).get("Status") == "healthy" for s in states):
                print("Docker deployment completed: both containers are healthy.")
                return
            if any(s.get("Status") in ("exited", "dead") or s.get("Health", {}).get("Status") == "unhealthy" for s in states):
                raise RuntimeError("Docker health failed; configuration and volumes were retained for inspection")
            if now() >= deadline:
                raise RuntimeError("Docker health timed out; configuration and volumes were retained for inspection")
            sleep(2)
    finally:
        lock.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument("--address")
    parser.add_argument("--map-resources", type=Path, help="Matching separate map ZIP; defaults to the file beside deploy.sh")
    parser.add_argument("--login-port", type=int, default=7000)
    parser.add_argument("--select-port", type=int, default=7100)
    parser.add_argument("--game-port", type=int, default=7200)
    parser.add_argument("--project-name", default="mir176-new")
    parser.add_argument("--wait-timeout", type=int, default=300)
    args = parser.parse_args()
    address = args.address
    if not address:
        if not sys.stdin.isatty():
            parser.error("Supply --address for non-interactive use")
        address = input("Client-reachable server IPv4 address / hostname: ").strip()
        args.project_name = input("New Docker project [mir176-new]: ").strip() or "mir176-new"
        for field, default in (("login_port", 7000), ("select_port", 7100), ("game_port", 7200)):
            while True:
                value = input(field.replace("_", " ") + f" [{default}]: ").strip()
                if not value or (value.isascii() and value.isdecimal() and 1 <= int(value) <= 65535):
                    setattr(args, field, int(value) if value else default)
                    break
                print("Enter a port between 1 and 65535.")
    try:
        deploy(args.directory, address.strip(), [args.login_port, args.select_port, args.game_port],
               args.project_name, args.wait_timeout, resource_archive=args.map_resources)
    except (OSError, ValueError, RuntimeError) as error:
        parser.exit(1, str(error) + "\nNo data or volumes were removed. Existing installations require the maintenance procedure.\n")


if __name__ == "__main__":
    main()
