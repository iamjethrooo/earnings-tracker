import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from validate_release import assembly_metadata, prepare, safe_path, validate_bundle, validate_configuration

def metadata(**values):
    return b'MZ' + b''.join(b'\x01\x00' + bytes([len(key)]) + key.encode() + bytes([len(value)]) + value.encode() + b'\x00\x00' for key,value in values.items())

class ReleaseValidationTests(unittest.TestCase):
    def test_private_records_sidecars_credentials_and_unknown_json_are_rejected(self):
        for name in ['lib/app/user.db', 'lib/app/user.db-wal', 'lib/app/user.db-shm', 'lib/app/.env', 'lib/app/key.pfx',
                     'lib/app/key.pvk', 'lib/app/key.snk', 'lib/app/key.jks', 'lib/app/key.keystore',
                     'lib/app/backups/copy', 'lib/app/Sessions.json', 'lib/app/payouts.csv', 'lib/app/Data/imports.json', '../escape']:
            with self.subTest(name=name), self.assertRaises(ValueError): safe_path(name)
        for name in ['lib/app/EarningsTracker.dll', 'lib/app/EarningsTracker.deps.json', 'lib/app/Data/games.json']:
            safe_path(name)
        safe_path('lib/app/Data/catalog.json.diff', delta=True)
        with self.assertRaises(ValueError): safe_path('lib/app/user.db-wal.diff', delta=True)

    def test_metadata_is_unique_and_source_separation_is_strict(self):
        data = metadata(ReleaseVersion='1.0.0', UpdateConfiguration='Local', UpdateRepository='')
        self.assertEqual('Local', assembly_metadata(data,'UpdateConfiguration'))
        with self.assertRaises(ValueError): assembly_metadata(data+data,'UpdateConfiguration')
        with self.assertRaises(ValueError): assembly_metadata(data,'Missing')
        for mode,repo in [('Production','test/updates'),('Local','test/updates'),('RemoteTest','iamjethrooo/earnings-tracker'),('RemoteTest','IAMJETHROOO/NERVE-EARNINGS-TRACKER'),('RemoteTest','test/updates?token=x')]:
            with self.subTest(mode=mode), self.assertRaises(ValueError): validate_configuration(mode,repo)

    def test_release_preparation_checks_actual_binary_nuspec_feed_and_upload_selection(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary); packages = output/'packages'; packages.mkdir()
            package = packages/'EarningsTracker.App-1.0.0-win-x64-app-stable-full.nupkg'
            with zipfile.ZipFile(package,'w') as archive:
                archive.writestr('lib/app/EarningsTracker.dll', metadata(ReleaseVersion='1.0.0',UpdateConfiguration='Local',UpdateRepository=''))
                archive.writestr('EarningsTracker.App.nuspec','<package><metadata><id>EarningsTracker.App</id><version>1.0.0</version></metadata></package>')
            data=package.read_bytes()
            asset={'PackageId':'EarningsTracker.App','Version':'1.0.0','Type':'Full','FileName':package.name,'SHA256':hashlib.sha256(data).hexdigest(),'SHA1':hashlib.sha1(data).hexdigest(),'Size':len(data)}
            (packages/'EarningsTracker.App-win-x64-app-stable-Setup.exe').write_bytes(b'test-installer')
            (packages/'releases.win-x64-app-stable.json').write_text(json.dumps({'Assets':[asset]}))
            (packages/'assets.win-x64-app-stable.json').write_text(json.dumps([{'Type':'Full','RelativeFileName':package.name},{'Type':'Installer','RelativeFileName':'EarningsTracker.App-win-x64-app-stable-Setup.exe'}]))
            (output/'RELEASE_NOTES.md').write_text('Synthetic test notes')
            with self.assertRaises(ValueError): validate_bundle(package,'1.0.1','Local','')
            with self.assertRaises(ValueError): validate_bundle(package,'1.0.0','Production','iamjethrooo/earnings-tracker')
            asset['Size']+=1; (packages/'releases.win-x64-app-stable.json').write_text(json.dumps({'Assets':[asset]}))
            with self.assertRaises(ValueError): prepare(output,'1.0.0','Local','')
            self.assertFalse((output/'upload').exists())
            asset['Size']-=1; (packages/'releases.win-x64-app-stable.json').write_text(json.dumps({'Assets':[asset]}))
            records=prepare(output,'1.0.0','Local','')
            self.assertIn('releases.win-x64-app-stable.json',[row['File'] for row in records])
            self.assertTrue((output/'upload/SHA256SUMS').exists())
            with self.assertRaises(FileExistsError): prepare(output,'1.0.0','Local','')

if __name__=='__main__': unittest.main()
