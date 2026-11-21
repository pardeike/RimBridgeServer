#!/usr/bin/env python3
"""Package the active bridge and preserve the frozen published payload during installation."""
import argparse
import contextlib
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parent.parent
FROZEN = json.loads((ROOT / 'scripts/frozen-payload.json').read_text())
LOCAL_LEGACY = {name for name in FROZEN if name.startswith('1.6/')}
ACTIVE_DLLS = {Path(name).name for name in FROZEN if name.startswith('1.6/Assemblies/')}
METADATA = {'About/About.xml', 'About/Manifest.xml', 'LoadFolders.xml', 'README.md'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_tree(root):
    result = {}
    for path in root.rglob('*'):
        require(not path.is_symlink(), f'Refusing symlink in payload: {path}')
        if path.is_file() and path.name not in {'.DS_Store', 'Thumbs.db'} and not path.name.startswith('._'):
            result[path.relative_to(root).as_posix()] = path.read_bytes()
    return result


def validate_source():
    actual_old = read_tree(ROOT / '1.6')
    expected_old = {n.removeprefix('1.6/') for n in FROZEN if n.startswith('1.6/')}
    require(set(actual_old) == expected_old, 'Frozen source 1.6 payload has missing or additional files.')
    for name, expected in FROZEN.items():
        path = ROOT / name
        require(path.is_file() and not path.is_symlink() and sha(path.read_bytes()) == expected,
                f'Frozen published source file changed: {name}')


def payload():
    validate_source()
    current = read_tree(ROOT / '1.7/Assemblies')
    dlls = {name: data for name, data in current.items() if name.endswith('.dll')}
    require(set(dlls) == ACTIVE_DLLS, f'Active runtime DLLs differ from the reviewed set: {sorted(dlls)}')
    version = ET.parse(ROOT / 'Directory.Build.props').findtext('.//ModVersion')
    require(ET.parse(ROOT / 'About/About.xml').findtext('modVersion') == version
            and ET.parse(ROOT / 'About/Manifest.xml').findtext('version') == version,
            'Project, About, and Manifest versions differ.')
    require(ET.parse(ROOT / 'About/About.xml').findtext('packageId') == 'brrainz.rimbridgeserver', 'Unexpected mod package ID.')
    folders = ET.parse(ROOT / 'LoadFolders.xml')
    for version in ('1.6', '1.7'):
        require([item.text for item in folders.findall(f'v{version}/li')] == ['/', version], f'Incorrect load folders for {version}.')
    result = {name: (ROOT / name).read_bytes() for name in FROZEN}
    result.update({name: (ROOT / name).read_bytes() for name in METADATA})
    result.update({'1.7/Assemblies/' + name: data for name, data in dlls.items()})
    return result


def write_zip(files, destination):
    destination.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix='.rimbridge-zip-', dir=destination.parent)
    os.close(descriptor)
    try:
        with ZipFile(temporary, 'w', ZIP_DEFLATED) as archive:
            for name in sorted(files):
                archive.writestr('RimBridgeServer/' + name, files[name])
        verify_zip(Path(temporary), files)
        os.replace(temporary, destination)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def verify_zip(path, expected):
    with ZipFile(path) as archive:
        actual = {name: archive.read(name) for name in archive.namelist()}
    require(actual == {'RimBridgeServer/' + n: data for n, data in expected.items()}, 'ZIP differs from the verified public payload.')


def package():
    files = payload()
    output = ROOT / 'artifacts/package/RimBridgeServer.zip'
    write_zip(files, output)
    return files


def absolute(value):
    return Path(os.path.abspath(os.path.expanduser(str(value).replace('\\', os.sep))))


def destinations(args):
    mods = absolute(args.mods_dir) if args.mods_dir else None
    if mods is not None:
        require(mods.is_dir() and mods.name.casefold() == mods.resolve().name.casefold() == 'mods', 'Use an existing Mods directory.')
        if mods.parent.suffix.casefold() == '.app':
            outer = mods.parent.parent / 'Mods'
            require(not outer.is_dir() or outer.resolve() != mods.resolve(), f'RimWorld 1.7 uses RIMWORLD_MOD_DIR={outer}')
        if mods.parent.name.endswith('-UserData'):
            alias = mods.parent.parent / (mods.parent.name.removesuffix('-UserData') + '.app') / 'Mods'
            require(not alias.is_dir() or alias.resolve() != mods.resolve(), 'Use the canonical RimWorld 1.7 Mods path, not a UserData alias.')
    target = absolute(args.target_dir) if args.target_dir else mods / 'RimBridgeServer' if mods else None
    require(target is not None and target.parent.is_dir(), 'An existing deployment parent directory is required.')
    require(not target.is_symlink() and (not target.exists() or target.is_dir()), f'Refusing unsafe mod destination: {target}')
    require(target != ROOT and target not in ROOT.parents, 'Deployment would overwrite the source checkout.')
    if mods is not None:
        require(target == mods / 'RimBridgeServer', 'Mods and exact target settings point to different installations.')
    archive = absolute(args.zip_path) if args.zip_path else target.with_suffix('.zip')
    require(archive.suffix.lower() == '.zip' and not archive.is_symlink() and target not in archive.parents,
            'Use a non-symlink ZIP path outside the installed mod.')
    require(not archive.exists() or archive.is_file(), 'ZIP destination is not a file.')
    return mods, target, archive


def running_process_names():
    windows = os.name == 'nt'
    command = ['tasklist', '/FO', 'CSV', '/NH'] if windows else ['ps', '-A', '-o', 'pid=,comm=']
    result = subprocess.run(command, text=True, capture_output=True)
    require(result.returncode == 0, 'Cannot determine whether RimWorld is running. Installation refused.')
    if windows:
        return [row[0] for row in csv.reader(result.stdout.splitlines()) if row]
    return [parts[1] for parts in (line.strip().split(None, 1) for line in result.stdout.splitlines()) if len(parts) == 2]


def ensure_stopped():
    for name in running_process_names():
        if re.fullmatch(r'RimWorld(?:Mac|Win64|Linux)?(?:\.exe)?|RimWorld by Ludeon Studios', Path(name).name, re.I):
            raise ValueError('RimWorld is running. Installation refused. This workflow never starts or stops the game.')


def validate_installed(target, allowed=None):
    if not target.exists():
        return {}
    files = read_tree(target)
    for name, expected in FROZEN.items():
        require(name in files, f'Installed frozen file is missing: {target / name}. No payload files changed.')
        if name not in LOCAL_LEGACY:
            require(sha(files[name]) == expected, f'Installed shared frozen file differs: {target / name}. No payload files changed.')
    permitted = set(FROZEN) | METADATA | {'1.7/Assemblies/' + name for name in ACTIVE_DLLS}
    require(set(files) <= (set(allowed) if allowed is not None else permitted), 'Installed payload has additional files. No payload files changed.')
    return files


def companion_payloads(mods, waylith):
    if mods is None:
        return {}
    result = {}
    bridge_root = mods.parent / 'BridgeTools'
    require(not bridge_root.is_symlink() and (not bridge_root.exists() or bridge_root.is_dir()), 'Refusing unsafe BridgeTools root.')
    for name in (['Multiplayer', 'Waylith'] if waylith else ['Multiplayer']):
        destination = bridge_root / name
        require(not destination.is_symlink() and (not destination.exists() or destination.is_dir()), f'Unsafe companion destination: {destination}')
        installed = read_tree(destination) if destination.exists() else {}
        dll = name + '.BridgeTools.dll'
        require(set(installed) <= {dll}, f'Unexpected files in companion bundle {destination}')
        result[destination] = ROOT / 'artifacts/BridgeTools' / name / dll
    return result


@contextlib.contextmanager
def deployment_lock(target):
    key = sha(f'{target.parent.resolve()}\n{target.name}\n'.encode())
    path = Path(tempfile.gettempdir()) / f'steam-mods-deploy-{key}.lock'
    require(not path.is_symlink(), 'Refusing symlinked deployment lock.')
    with path.open('a+b') as handle:
        try:
            if os.name == 'nt':
                import msvcrt
                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            raise ValueError(f'Another deployment holds {path}')
        yield


def atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix='.rimbridge-', dir=path.parent)
    try:
        with os.fdopen(descriptor, 'wb') as handle:
            handle.write(data)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def check(args):
    mods, target, archive = destinations(args)
    ensure_stopped()
    validate_source()
    validate_installed(target)
    companion_payloads(mods, args.waylith == 'true')


def local_legacy_snapshot(target):
    if not target.exists():
        return {}
    return {name: ((target / name).read_bytes(), (target / name).stat().st_ino,
                   (target / name).stat().st_mtime_ns) for name in LOCAL_LEGACY}


def verify(args, legacy_before=None):
    mods, target, archive = destinations(args)
    expected = payload()
    installed = validate_installed(target, expected)
    expected_local = dict(expected)
    expected_local.update({name: installed[name] for name in LOCAL_LEGACY})
    require(installed == expected_local, 'Installed 1.7 files or metadata differ from the current public payload.')
    if legacy_before:
        require(local_legacy_snapshot(target) == legacy_before, 'Existing local 1.6 files changed during installation.')
    verify_zip(archive, expected)
    for destination, source in companion_payloads(mods, args.waylith == 'true').items():
        require(read_tree(destination) == {source.name: source.read_bytes()}, f'Companion differs from the matching build: {destination}')


def deploy(args):
    mods, target, archive = destinations(args)
    with deployment_lock(target):
        check(args)
        files = package()
        installed = validate_installed(target, files)
        legacy_before = local_legacy_snapshot(target)
        companions = companion_payloads(mods, args.waylith == 'true')
        for destination, source in companions.items():
            source_dlls = {name for name in read_tree(source.parent) if name.endswith('.dll')}
            require(source.is_file() and source_dlls == {source.name}, f'Build the sole matching companion DLL before installation: {source}')
        ensure_stopped()
        if not target.exists():
            with tempfile.TemporaryDirectory(prefix='.rimbridge-stage-', dir=target.parent) as temporary:
                stage = Path(temporary) / target.name
                for name, data in files.items():
                    path = stage / name
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(data)
                stage.rename(target)
        else:
            for name, data in files.items():
                if name not in FROZEN and installed.get(name) != data:
                    atomic_write(target / name, data)
        for destination, source in companions.items():
            path = destination / source.name
            data = source.read_bytes()
            if not path.is_file() or path.read_bytes() != data:
                atomic_write(path, data)
        expected_local = dict(files)
        if legacy_before:
            expected_local.update({name: value[0] for name, value in legacy_before.items()})
        require(read_tree(target) == expected_local, 'Installed current files differ from the verified staged payload.')
        write_zip(files, archive)
        verify(args, legacy_before)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['validate-source', 'package', 'check', 'deploy', 'verify'])
    parser.add_argument('--mods-dir', default='')
    parser.add_argument('--target-dir', default='')
    parser.add_argument('--zip-path', default='')
    parser.add_argument('--waylith', choices=['true', 'false'], default='false')
    args = parser.parse_args()
    try:
        {'validate-source': validate_source, 'package': package}.get(args.command, lambda: globals()[args.command](args))()
    except (OSError, ValueError) as error:
        print(f'error: RimBridgeServer: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
