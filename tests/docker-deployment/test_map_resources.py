"""External map import: atomic offline preparation and no player/Docker access."""
import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import tempfile
import unittest
import zipfile

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('maps',ROOT/'deployment/import-resources.py')
maps=importlib.util.module_from_spec(spec);spec.loader.exec_module(maps)


class MapTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.payload=b'isolated map fixture';self.archive=self.root/'maps.zip'
        self.rows=[{'Path':'Map/0.map','Size':len(self.payload),'Sha256':hashlib.sha256(self.payload).hexdigest()}]
        self.write_archive()

    def tearDown(self):self.temp.cleanup()

    def write_archive(self,extra=None,symlink=False,payload=None):
        with zipfile.ZipFile(self.archive,'w') as z:
            if symlink:
                entry=zipfile.ZipInfo('Map/0.map');entry.create_system=3;entry.external_attr=(stat.S_IFLNK|0o777)<<16
                z.writestr(entry,self.payload)
            else:z.writestr('Map/0.map',self.payload if payload is None else payload)
            if extra:z.writestr(extra,b'not a map')
            z.writestr('map-package-manifest.json',json.dumps({'resource_set_id':'assets-'+'a'*12,'files':self.rows}))
        self.descriptor={'project':'Mir2Classic176','resource_set_id':'assets-'+'a'*12,
            'archive':{'name':'maps.zip','bytes':self.archive.stat().st_size,'sha256':maps.digest(self.archive)},'files':self.rows}
        (self.root/'map-resources.json').write_text(json.dumps(self.descriptor))

    def test_import_is_complete_idempotent_and_does_not_touch_other_files(self):
        (self.root/'private-setting.ini').write_text('preserve')
        self.assertEqual(maps.prepare(self.root),1)
        self.assertEqual((self.root/'services/GameSrv/Map/0.map').read_bytes(),self.payload)
        self.archive.unlink()
        self.assertEqual(maps.prepare(self.root),1)
        self.assertEqual((self.root/'private-setting.ini').read_text(),'preserve')

    def test_missing_or_corrupt_archive_does_not_create_maps(self):
        self.archive.write_bytes(b'corrupt')
        with self.assertRaisesRegex(ValueError,'hash mismatch'):maps.prepare(self.root)
        self.archive.unlink()
        with self.assertRaisesRegex(ValueError,'Download'):maps.prepare(self.root)
        self.assertFalse((self.root/'services').exists())

    def test_payload_mismatch_leaves_no_partial_destination(self):
        self.write_archive(payload=b'changed map')
        with self.assertRaisesRegex(ValueError,'payload hash'):maps.prepare(self.root)
        self.assertFalse((self.root/'services/GameSrv/Map').exists())
        self.assertFalse(list(self.root.glob('.map-import-*')))

    def test_extra_paths_and_archive_symlinks_are_rejected(self):
        for extra in ('../outside','Map/../outside','Mir2Classic176.exe'):
            self.write_archive(extra=extra)
            with self.assertRaisesRegex(ValueError,'coverage'):maps.prepare(self.root)
        self.write_archive(symlink=True)
        with self.assertRaisesRegex(ValueError,'symlink'):maps.prepare(self.root)
        self.assertFalse((self.root/'services').exists())

    def test_existing_changed_or_extra_maps_are_not_overwritten(self):
        maps.prepare(self.root);target=self.root/'services/GameSrv/Map/0.map';target.write_bytes(b'preserve')
        with self.assertRaisesRegex(ValueError,'hash mismatch'):maps.prepare(self.root)
        self.assertEqual(target.read_bytes(),b'preserve')
        target.write_bytes(self.payload);(target.parent/'extra.map').write_bytes(b'preserve')
        with self.assertRaisesRegex(ValueError,'unexpected'):maps.prepare(self.root)

    def test_parent_symlink_does_not_write_outside_package(self):
        outside=self.root/'outside';outside.mkdir();(self.root/'services').symlink_to(outside,target_is_directory=True)
        with self.assertRaisesRegex(ValueError,'parent'):maps.prepare(self.root)
        self.assertFalse(list(outside.iterdir()))

    def test_descriptor_rejects_duplicate_or_unsafe_resource_entries(self):
        for name in ('Map/../outside.map','Map\\outside.map','/outside.map'):
            self.descriptor['files']=[dict(self.rows[0],Path=name)]
            (self.root/'map-resources.json').write_text(json.dumps(self.descriptor))
            with self.assertRaises(ValueError):maps.prepare(self.root)
        self.descriptor['files']=[self.rows[0],self.rows[0]]
        (self.root/'map-resources.json').write_text(json.dumps(self.descriptor))
        with self.assertRaisesRegex(ValueError,'duplicate'):maps.prepare(self.root)

if __name__=='__main__':unittest.main()
