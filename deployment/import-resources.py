#!/usr/bin/env python3
"""Offline import of the separately distributed, hash-bound server maps."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat
import tempfile
import zipfile


def digest(path):
    sha = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            sha.update(chunk)
    return sha.hexdigest()


def read_descriptor(path):
    data = json.loads(path.read_text())
    archive = data['archive']
    if (data.get('project') != 'Mir2Classic176'
            or not re.fullmatch(r'assets-[0-9a-f]{12}', data.get('resource_set_id', ''))
            or not re.fullmatch(r'[A-Za-z0-9_.-]+[.]zip', archive['name'])
            or not re.fullmatch(r'[0-9a-f]{64}', archive['sha256'])
            or not isinstance(archive['bytes'], int) or archive['bytes'] <= 0):
        raise ValueError('Invalid map resource descriptor')
    files = {}
    folded = set()
    for row in data['files']:
        name = row['Path']
        parts = PurePosixPath(name).parts
        if (PurePosixPath(name).as_posix() != name or len(parts) != 2 or parts[0] != 'Map' or parts[1] in ('.', '..')
                or '\\' in name or ':' in name or not name.endswith('.map')
                or name.casefold() in folded or not re.fullmatch(r'[0-9a-f]{64}', row['Sha256'])
                or not isinstance(row['Size'], int) or row['Size'] < 1):
            raise ValueError('Invalid or duplicate map resource entry')
        folded.add(name.casefold())
        files[name] = row
    if not files:
        raise ValueError('Empty map resource descriptor')
    return data, files


def expected_maps(directory):
    descriptor, files = read_descriptor(directory / 'map-resources.json')
    return descriptor, {'services/GameSrv/' + name: row for name, row in files.items()}


def check_installed(directory, required):
    target = directory / 'services/GameSrv/Map'
    if not target.exists() and not target.is_symlink():
        return False
    if target.is_symlink() or not target.is_dir():
        raise ValueError('Unsafe existing map directory')
    actual = {}
    for path in target.rglob('*'):
        if path.is_symlink():
            raise ValueError('Map symlink is not permitted')
        if path.is_file():
            actual[path.relative_to(directory).as_posix()] = path
    if set(actual) != set(required):
        raise ValueError('Existing maps are incomplete or contain unexpected files; use a clean package')
    for name, path in actual.items():
        if path.stat().st_size != required[name]['Size'] or digest(path) != required[name]['Sha256']:
            raise ValueError('Existing map hash mismatch; no files were overwritten')
    return True


def prepare(directory, archive=None):
    directory = Path(directory).resolve()
    for parent in (directory / 'services', directory / 'services/GameSrv'):
        if parent.is_symlink() or not parent.resolve().is_relative_to(directory):
            raise ValueError('Unsafe server map parent directory')
    descriptor, required = expected_maps(directory)
    if check_installed(directory, required):
        return len(required)
    source = Path(archive) if archive is not None else directory / descriptor['archive']['name']
    if not source.is_file() or source.is_symlink():
        raise ValueError('Download ' + descriptor['archive']['name'] + ' from Mir176Client Release and place it beside deploy.sh')
    if source.stat().st_size != descriptor['archive']['bytes'] or digest(source) != descriptor['archive']['sha256']:
        raise ValueError('Map resource archive hash mismatch; no files were installed')
    with zipfile.ZipFile(source) as original, tempfile.TemporaryDirectory(prefix='.map-import-', dir=directory) as temporary:
        entries = original.infolist()
        names = [entry.filename for entry in entries]
        expected = {row['Path'] for row in required.values()} | {'map-package-manifest.json'}
        if set(names) != expected or len(names) != len(expected):
            raise ValueError('Map archive coverage mismatch')
        if any(stat.S_ISLNK(entry.external_attr >> 16) for entry in entries):
            raise ValueError('Map archive symlink is not permitted')
        embedded = json.loads(original.read('map-package-manifest.json'))
        if (embedded.get('resource_set_id') != descriptor['resource_set_id']
                or embedded.get('files') != descriptor['files']):
            raise ValueError('Map archive descriptor mismatch')
        stage = Path(temporary) / 'Map'
        stage.mkdir()
        for row in required.values():
            payload = original.read(row['Path'])
            if len(payload) != row['Size'] or hashlib.sha256(payload).hexdigest() != row['Sha256']:
                raise ValueError('Map payload hash mismatch')
            (stage / PurePosixPath(row['Path']).name).write_bytes(payload)
        target = directory / 'services/GameSrv/Map'
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists() or target.is_symlink():
            raise ValueError('Map destination appeared during import; no existing files were overwritten')
        stage.rename(target)
    if not check_installed(directory, required):
        raise ValueError('Map import verification failed')
    return len(required)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument('--archive', type=Path)
    args = parser.parse_args()
    try:
        count = prepare(args.directory, args.archive)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile):
        parser.exit(1, 'Map resource import failed; check the matching archive and use a clean server package. No existing maps were overwritten.\n')
    print(f'PASS {count} external server maps imported and verified; no game or Docker connection')


if __name__ == '__main__':
    main()
