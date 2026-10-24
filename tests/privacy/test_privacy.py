"""Synthetic privacy checks; no operational identities or endpoints."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('publication_privacy', ROOT / 'tools/check-privacy.py')
privacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(privacy)
release_spec = importlib.util.spec_from_file_location('release_privacy', ROOT / 'tools/check-release-package.py')
release = importlib.util.module_from_spec(release_spec)
release_spec.loader.exec_module(release)


class ValueTests(unittest.TestCase):
    def test_case_variants_and_encodings(self):
        with patch.object(privacy, 'LOCAL_RULES', [('LocalTester', 'all')]):
            for encoding in ('utf-8', 'utf-16le', 'utf-16be'):
                for value in ('LOCALTESTER', 'localtester', 'LocalTester'):
                    with self.subTest(encoding=encoding, value=value):
                        self.assertEqual(['known-private-value'], privacy.inspect('sample', value.encode(encoding)))

    def test_short_value_only_restricts_identity(self):
        with patch.object(privacy, 'LOCAL_RULES', [('day', 'identity')]):
            self.assertEqual([], privacy.inspect('sample.pas', b'NextDay; // public contributor Day'))
            self.assertIn('known-private-value', privacy.identity_reasons(b'DAY <example@example.invalid> 1 +0000'))

    def test_publication_identity(self):
        self.assertEqual([], privacy.identity_reasons(b'Mir2Classic176 Maintainers <maintainers@mir2classic176.invalid> 1 +0000'))
        self.assertIn('unapproved-publication-identity', privacy.identity_reasons(b'Example <example@example.invalid> 1 +0000'))
        self.assertIn('invalid-publication-identity', privacy.identity_reasons(b'not an identity'))

    def test_generic_categories_cover_both_utf16_orders_in_text(self):
        marker = chr(68) + ':' + chr(92) + 'synthetic-build'
        for encoding in ('utf-8', 'utf-16le', 'utf-16be'):
            with self.subTest(encoding=encoding):
                self.assertIn('personal-workspace', privacy.inspect('README.txt', marker.encode(encoding)))

    def test_release_uses_same_scoped_rules(self):
        with patch.object(release.privacy, 'LOCAL_RULES', [('LocalTester', 'all'), ('day', 'identity')]):
            self.assertEqual([], release.content_reasons('example.dll', b'NextDay', 'fixture'))
            for encoding in ('utf-8', 'utf-16le', 'utf-16be'):
                self.assertIn('known-private-value', release.content_reasons('example.dll', 'LOCALTESTER'.encode(encoding), 'fixture'))

    def test_release_rejects_encoded_value_crossing_stream_chunks(self):
        value = 'ExampleIdentity' * 400
        data = b'x' * (1024 * 1024 - 5000) + value.upper().encode('utf-16be') + b'x'
        files = {'Mir176Launcher.exe': data, 'README.txt': b'fixture', 'launcher-build.json': b'{}'}
        with tempfile.TemporaryDirectory() as d, patch.object(release.privacy, 'LOCAL_RULES', [(value, 'all')]):
            archive = Path(d) / 'synthetic.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                for name, payload in files.items():
                    z.writestr(name, payload)
                z.writestr('package-manifest.json', json.dumps({'project': 'Mir2Classic176', 'files': {
                    name: hashlib.sha256(payload).hexdigest() for name, payload in files.items()}}))
            with self.assertRaisesRegex(ValueError, 'known private value in release'):
                release.check(archive, 'launcher')

    def test_release_member_names_and_diagnostics(self):
        value = '示例Fixture'
        with tempfile.TemporaryDirectory() as d, patch.object(release.privacy, 'LOCAL_RULES', [(value, 'all')]):
            archive = Path(d) / 'synthetic.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr(value + '/', b'')
            with self.assertRaises(ValueError) as error:
                release.check(archive, 'launcher')
            self.assertNotIn(value, str(error.exception))
            self.assertIn('private archive member', str(error.exception))

    def test_release_hash_error_does_not_echo_member_name(self):
        value = '示例Fixture'
        name = 'Map/' + value + '.map'
        files = {'Mir2Classic176.exe': b'fixture', 'Configure-Classic.ps1': b'fixture', 'Data/README.txt': b'fixture', name: b'fixture'}
        with tempfile.TemporaryDirectory() as d, patch.object(release.privacy, 'LOCAL_RULES', []):
            archive = Path(d) / 'synthetic.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                for member, payload in files.items():
                    z.writestr(member, payload)
                z.writestr('package-manifest.json', json.dumps({'project': 'Mir2Classic176', 'files': {n: 'wrong' for n in files}}))
            with self.assertRaises(ValueError) as error:
                release.check(archive, 'client')
            self.assertNotIn(value, str(error.exception))


class GitTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.git('init', '-q')
        self.git('config', 'user.name', 'Mir2Classic176 Maintainers')
        self.git('config', 'user.email', 'maintainers@mir2classic176.invalid')
        (self.root / 'sample.txt').write_text('ordinary sample')
        self.git('add', 'sample.txt')
        self.git('commit', '-qm', 'Initial synthetic sample')

    def tearDown(self):
        self.tmp.cleanup()

    def git(self, *args, **kwargs):
        return subprocess.check_output(['git', '-C', str(self.root), *args], stderr=subprocess.PIPE, **kwargs)

    def run_guard(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with patch.object(privacy, 'ROOT', self.root), patch.object(sys, 'argv', ['guard', *args]), contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = privacy.main()
        return code, out.getvalue() + err.getvalue()

    def test_clean_history_and_staged_identities(self):
        self.assertEqual(0, self.run_guard('--history')[0])
        self.assertEqual(0, self.run_guard()[0])

    def test_author_and_committer_are_checked_without_disclosing_them(self):
        for field in ('AUTHOR', 'COMMITTER'):
            env = dict(os.environ, **{'GIT_' + field + '_NAME': 'Private Fixture', 'GIT_' + field + '_EMAIL': 'fixture@example.invalid'})
            self.git('commit', '--allow-empty', '-qm', 'Synthetic identity sample', env=env)
            code, output = self.run_guard('--history')
            self.assertEqual(1, code)
            self.assertIn('unapproved-publication-identity', output)
            self.assertNotIn('Private Fixture', output)
            self.assertNotIn('fixture@example.invalid', output)

    def test_tagger_and_explicit_tag_tip_are_checked(self):
        env = dict(os.environ, GIT_COMMITTER_NAME='Private Fixture', GIT_COMMITTER_EMAIL='fixture@example.invalid')
        self.git('tag', '-a', 'synthetic-tag', '-m', 'Synthetic tag', env=env)
        for args in (('--history',), ('--ref', 'refs/tags/synthetic-tag')):
            code, output = self.run_guard(*args)
            self.assertEqual(1, code)
            self.assertIn('unapproved-publication-identity', output)
            self.assertNotIn('Private Fixture', output)

    def test_prospective_identity_is_checked_before_commit(self):
        self.git('config', 'user.name', 'Private Fixture')
        code, output = self.run_guard()
        self.assertEqual(1, code)
        self.assertIn('prospective-identity', output)
        self.assertNotIn('Private Fixture', output)

    def test_messages_and_sensitive_filenames_are_not_echoed(self):
        (self.root / 'LocalTester.txt').write_text('ordinary sample')
        self.git('add', 'LocalTester.txt')
        with patch.object(privacy, 'LOCAL_RULES', [('LocalTester', 'all')]):
            code, output = self.run_guard()
            self.assertEqual(1, code)
            self.assertNotIn('LocalTester', output)
            self.git('commit', '-qm', 'LOCALTESTER')
            self.assertEqual(1, self.run_guard('--history')[0])

    def test_history_checks_every_duplicate_blob_filename(self):
        (self.root / 'zzLocalTester.txt').write_text('ordinary sample')
        self.git('add', 'zzLocalTester.txt')
        self.git('commit', '-qm', 'Duplicate payload sample')
        with patch.object(privacy, 'LOCAL_RULES', [('LocalTester', 'all')]):
            code, output = self.run_guard('--history')
            self.assertEqual(1, code)
            self.assertNotIn('LocalTester', output)

    def test_tag_names_are_checked(self):
        self.git('tag', '-a', 'LocalTester-tag', '-m', 'Synthetic tag')
        with patch.object(privacy, 'LOCAL_RULES', [('LocalTester', 'all')]):
            for args in (('--history',), ('--ref', 'refs/tags/LocalTester-tag')):
                code, output = self.run_guard(*args)
                self.assertEqual(1, code)
                self.assertNotIn('LocalTester', output)

    def test_malformed_registry_cli_hides_directory(self):
        tools = self.root / 'tools'
        tools.mkdir()
        (tools / 'check-privacy.py').write_bytes((ROOT / 'tools/check-privacy.py').read_bytes())
        (self.root / '.git/privacy-known-values.json').write_text('{invalid json')
        result = subprocess.run([sys.executable, str(tools / 'check-privacy.py'), '--history'], capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn(str(self.root), result.stderr)
        self.assertNotIn('Traceback', result.stderr)

    def test_registry_keeps_short_values_and_rejects_bad_shape(self):
        import json
        registry = self.root / '.git/privacy-known-values.json'
        with patch.object(privacy, 'ROOT', self.root):
            registry.write_text(json.dumps(['day', {'value': 'LocalTester', 'scope': 'all'}]))
            self.assertEqual([('day', 'identity'), ('LocalTester', 'all')], privacy.known_local_rules())
            registry.write_text(json.dumps({'unexpected': 'LocalTester'}))
            with self.assertRaisesRegex(ValueError, 'values withheld'):
                privacy.known_local_rules()


if __name__ == '__main__':
    unittest.main()
