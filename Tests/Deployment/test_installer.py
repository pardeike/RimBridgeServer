"""Exercise public and companion installation only in disposable directories."""
import argparse
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('rimbridge_installer', ROOT / 'scripts/rimworld_deploy_guard.py')
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class InstallationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='installer space-', dir=ROOT / 'artifacts')
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.mods = self.base / 'Mods'
        self.mods.mkdir()
        self.target = self.mods / 'RimBridgeServer'
        self.args = argparse.Namespace(mods_dir=str(self.mods), target_dir=str(self.target),
                                       zip_path=str(self.mods / 'RimBridgeServer.zip'), waylith='false')

    def install(self):
        with patch.object(installer, 'ensure_stopped'):
            installer.deploy(self.args)

    def snapshot(self):
        return {p.relative_to(self.base).as_posix(): (p.read_bytes(), p.stat().st_ino, p.stat().st_mtime_ns)
                for p in self.base.rglob('*') if p.is_file()}

    def test_first_install_preserves_published_payload_and_pairs_companion(self):
        self.install()
        installer.verify(self.args)
        for name, expected in installer.FROZEN.items():
            self.assertEqual(installer.sha((self.target / name).read_bytes()), expected)
        with ZipFile(self.args.zip_path) as archive:
            self.assertFalse(any('BridgeTools/' in name for name in archive.namelist()))
            self.assertIn('RimBridgeServer/1.7/Assemblies/RimBridgeServer.Sdk.dll', archive.namelist())

    def test_update_preserves_frozen_bytes_inodes_and_modification_times(self):
        self.install()
        frozen = {}
        for name in installer.FROZEN:
            path = self.target / name
            os.utime(path, ns=(1_600_000_000_000_000_000, 1_600_000_000_000_000_000))
            frozen[name] = path.read_bytes(), path.stat().st_ino, path.stat().st_mtime_ns
        (self.target / '1.7/Assemblies/RimBridgeServer.dll').write_bytes(b'previous bridge')
        companion = self.base / 'BridgeTools/Multiplayer/Multiplayer.BridgeTools.dll'
        companion.write_bytes(b'previous companion')
        self.install()
        for name, expected in frozen.items():
            path = self.target / name
            self.assertEqual((path.read_bytes(), path.stat().st_ino, path.stat().st_mtime_ns), expected)
        installer.verify(self.args)

    def test_mismatched_frozen_content_refuses_all_installed_changes(self):
        self.install()
        for name in ('About/Preview.png', 'LICENSE'):
            with self.subTest(name=name):
                path = self.target / name
                original = path.read_bytes()
                path.write_bytes(b'different old payload')
                before = self.snapshot()
                with self.assertRaisesRegex(ValueError, 'Installed shared frozen file differs'):
                    self.install()
                self.assertEqual(self.snapshot(), before)
                path.write_bytes(original)

    def test_nine_local_legacy_files_with_five_differences_stay_in_place(self):
        self.install()
        changed = ['RimBridgeServer.dll', 'RimBridgeServer.Core.dll', 'RimBridgeServer.Contracts.dll',
                   'RimBridgeServer.Sdk.dll', 'RimBridgeServer.Extensions.Abstractions.dll']
        for name in changed:
            (self.target / '1.6/Assemblies' / name).write_bytes(('local unpublished ' + name).encode())
        before = installer.local_legacy_snapshot(self.target)
        self.assertEqual(len(before), 9)
        self.assertEqual(sum(installer.sha(value[0]) != installer.FROZEN[name] for name, value in before.items()), 5)
        (self.target / '1.7/Assemblies/RimBridgeServer.dll').write_bytes(b'previous active bridge')
        self.install()
        self.assertEqual(installer.local_legacy_snapshot(self.target), before)
        installer.verify(self.args)
        with ZipFile(self.args.zip_path) as archive:
            for name in installer.LOCAL_LEGACY:
                self.assertEqual(installer.sha(archive.read('RimBridgeServer/' + name)), installer.FROZEN[name])

    def test_missing_frozen_file_is_not_recreated(self):
        self.install()
        (self.target / '1.6/Assemblies/RimBridgeServer.dll').unlink()
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, 'missing'):
            self.install()
        self.assertEqual(self.snapshot(), before)

    def test_additional_old_payload_is_not_removed(self):
        self.install()
        (self.target / '1.6/Assemblies/extra.dll').write_bytes(b'old content')
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, 'additional files'):
            self.install()
        self.assertEqual(self.snapshot(), before)

    def test_symlinked_target_is_rejected(self):
        other = self.base / 'other'
        other.mkdir()
        self.target.symlink_to(other, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'unsafe mod destination'):
            self.install()
        self.assertEqual(list(other.iterdir()), [])

    def test_nested_symlink_is_rejected(self):
        self.install()
        path = self.target / '1.6/Assemblies/RimBridgeServer.dll'
        path.unlink()
        path.symlink_to(ROOT / '1.6/Assemblies/RimBridgeServer.dll')
        with self.assertRaisesRegex(ValueError, 'symlink in payload'):
            self.install()
        self.assertTrue(path.is_symlink())

    def test_wrong_macos_mods_alias_is_rejected(self):
        app = self.base / 'RimWorldMac.app'
        app.mkdir()
        (app / 'Mods').symlink_to(self.mods, target_is_directory=True)
        self.args.mods_dir = str(app / 'Mods')
        self.args.target_dir = str(app / 'Mods/RimBridgeServer')
        with self.assertRaisesRegex(ValueError, 'RimWorld 1.7 uses'):
            self.install()

    def test_companion_sdk_copy_is_rejected_before_public_mod_changes(self):
        self.install()
        bundle = self.base / 'BridgeTools/Multiplayer'
        (bundle / 'RimBridgeServer.Sdk.dll').write_bytes(b'host supplies the SDK')
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, 'Unexpected files in companion bundle'):
            self.install()
        self.assertEqual(self.snapshot(), before)

    def test_exact_target_interface_retains_public_mod_only_behavior(self):
        self.args.mods_dir = ''
        self.args.target_dir = str(self.base / 'active-item')
        self.args.zip_path = str(self.base / 'active-item.zip')
        self.install()
        installer.verify(self.args)
        self.assertFalse((self.base / 'BridgeTools').exists())

    def test_optional_waylith_bundle_uses_matching_sdk_without_entering_zip(self):
        source = ROOT / 'artifacts/BridgeTools/Waylith/Waylith.BridgeTools.dll'
        if not source.is_file():
            self.skipTest('Build with BuildWaylithTools=true to exercise optional companion installation.')
        self.args.waylith = 'true'
        self.install()
        installer.verify(self.args)
        bundle = self.base / 'BridgeTools/Waylith'
        self.assertEqual(installer.read_tree(bundle), {source.name: source.read_bytes()})
        with ZipFile(self.args.zip_path) as archive:
            self.assertFalse(any('BridgeTools' in name for name in archive.namelist()))

    def test_concurrent_installation_is_rejected(self):
        with installer.deployment_lock(self.target):
            with self.assertRaisesRegex(ValueError, 'Another deployment holds'):
                with installer.deployment_lock(self.target):
                    self.fail('Second installer acquired the same lock.')

    def test_running_game_and_unknown_state_refuse_installation(self):
        for result in (subprocess.CompletedProcess([], 0, '123 /games/RimWorld\n', ''),
                       subprocess.CompletedProcess([], 1, '', 'inspection failed')):
            with patch.object(installer.subprocess, 'run', return_value=result):
                with self.assertRaisesRegex(ValueError, 'running|Cannot determine'):
                    installer.deploy(self.args)
            self.assertFalse(self.target.exists())

    def test_windows_process_list_detects_running_game(self):
        running = subprocess.CompletedProcess([], 0, '"explorer.exe","1","Console"\n"RimWorldWin64.exe","2","Console"\n', '')
        with patch.object(installer.os, 'name', 'nt'), patch.object(installer.subprocess, 'run', return_value=running) as run:
            with self.assertRaisesRegex(ValueError, 'running'):
                installer.ensure_stopped()
        self.assertEqual(run.call_args.args[0][0], 'tasklist')

    def test_normal_deployment_command_installs_only_disposable_target(self):
        binaries = self.base / 'bin'
        binaries.mkdir()
        ps = binaries / 'ps'
        ps.write_text("#!/bin/sh\nprintf '1 /usr/bin/launchd\\n'\n")
        ps.chmod(0o755)
        env = dict(os.environ, RIMWORLD_MOD_TARGET_DIR='', RIMWORLD_MOD_ZIP_PATH='')
        env['PATH'] = str(binaries) + os.pathsep + env['PATH']
        result = subprocess.run(['./scripts/deploy-local-mod.sh', '--mods-dir', str(self.mods), '--no-restore'],
                                cwd=ROOT, env=env, text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(result.stdout, 'ok\n')
        installer.verify(self.args)


if __name__ == '__main__':
    unittest.main()
