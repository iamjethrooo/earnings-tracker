"""Offline release attestation using only the standard library. Never uploads or reads user storage."""
import argparse
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path, PurePosixPath

PACKAGE_ID = 'EarningsTracker.App'
CHANNEL = 'win-x64-app-stable'
PRODUCTION = 'iamjethrooo/earnings-tracker'
FORMER_PRODUCTION = 'iamjethrooo/nerve-earnings-tracker'
PRIVATE = re.compile(r'(^|/)(backups?|exports?|personaldata|credentials|tools|tests|obj|igdbcache|subscriptioncache|remainingcache)(/|$)|\.(db|db3|sqlite|sqlite3)(-|$)|\.(bak|backup|pfx|p12|pem|key|pvk|snk|jks|keystore|tmp|log)$|(^|/)\.env($|\.)', re.I)

def safe_path(name, delta=False):
    name = name.replace('\\', '/')
    if delta: name = re.sub(r'\.(diff|shasum)$', '', name)
    if PurePosixPath(name).is_absolute() or '..' in PurePosixPath(name).parts or PRIVATE.search(name):
        raise ValueError(f'Private/unsafe package path: {name}')
    if re.search(r'(^|/)Data/', name, re.I) and not re.search(r'(^|/)Data/(games\.json|catalog\.json|Artwork/[a-f0-9]{64}\.img)$', name, re.I):
        raise ValueError(f'Unknown bundled data: {name}')
    if name.lower().endswith('.json') and not re.search(r'(^|/)Data/(games|catalog)\.json$|(^|/)EarningsTracker\.(deps|runtimeconfig)\.json$', name, re.I):
        raise ValueError(f'Unknown JSON data: {name}')
    if re.search(r'\.(csv|xlsx|xls|docx|pdf)$', name, re.I): raise ValueError(f'Developer/personal document: {name}')

def assembly_metadata(data, key):
    # Compiler-emitted AssemblyMetadataAttribute blobs: prolog + two SerStrings + zero named arguments.
    # Keys/values here fit the one-byte ECMA-335 SerString length (repository is <= 140; handle two-byte values).
    def length_at(offset):
        first = data[offset]
        if first < 128: return first, offset + 1
        if first < 192: return ((first & 63) << 8) | data[offset + 1], offset + 2
        raise ValueError('Unexpected metadata string length')
    prefix = b'\x01\x00' + bytes([len(key)]) + key.encode()
    values = []
    offset = 0
    while (offset := data.find(prefix, offset)) != -1:
        size, start = length_at(offset + len(prefix))
        end = start + size
        if data[end:end+2] != b'\x00\x00': raise ValueError('Invalid metadata attribute')
        values.append(data[start:end].decode('utf-8'))
        offset = end + 2
    if len(values) != 1: raise ValueError(f'Missing/ambiguous assembly metadata: {key}')
    return values[0]

def validate_configuration(configuration, repository):
    if configuration == 'Local':
        if repository: raise ValueError('Local builds cannot retain GitHub repositories')
    elif configuration == 'Production':
        if repository != PRODUCTION: raise ValueError('Production repository mismatch')
    elif configuration == 'RemoteTest':
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}', repository) or repository.lower() in (PRODUCTION.lower(), FORMER_PRODUCTION.lower()) or repository.lower().endswith('.git'):
            raise ValueError('Invalid separate test repository')
    else: raise ValueError('Unknown configuration')

def validate_bundle(path, version, configuration, repository):
    with zipfile.ZipFile(path) as archive:
        for name in archive.namelist():
            if not name.endswith('/'): safe_path(name)
        assemblies = [name for name in archive.namelist() if name.endswith('/EarningsTracker.dll') or name == 'EarningsTracker.dll']
        if len(assemblies) != 1: raise ValueError('Main assembly missing or ambiguous')
        data = archive.read(assemblies[0])
        for key, expected in {'ReleaseVersion':version, 'UpdateConfiguration':configuration, 'UpdateRepository':repository}.items():
            if assembly_metadata(data, key) != expected: raise ValueError(f'Compiled {key} mismatch')
        if path.suffix == '.nupkg':
            spec = ET.fromstring(archive.read(PACKAGE_ID + '.nuspec'))
            fields = {node.tag.rsplit('}', 1)[-1]:node.text for node in spec.iter()}
            if fields.get('id') != PACKAGE_ID or fields.get('version') != version: raise ValueError('NuGet identity/version mismatch')

def prepare(output, version, configuration, repository, reconstructed=None):
    validate_configuration(configuration, repository)
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version): raise ValueError('Stable SemVer required')
    packages = output / 'packages'
    feed = json.loads((packages / f'releases.{CHANNEL}.json').read_text(encoding='utf-8-sig'))
    entries = [asset for asset in feed['Assets'] if asset['Version'] == version]
    if len([asset for asset in entries if asset['Type'] == 'Full']) != 1: raise ValueError('Exactly one full target package is required')
    for asset in entries:
        filename = asset['FileName']; safe_path(filename)
        if '/' in filename or '\\' in filename or asset['PackageId'] != PACKAGE_ID: raise ValueError('Invalid feed identity/path')
        data = (packages / filename).read_bytes()
        if len(data) != asset['Size'] or hashlib.sha256(data).hexdigest().lower() != asset['SHA256'].lower() or hashlib.sha1(data).hexdigest().lower() != asset['SHA1'].lower():
            raise ValueError('Feed checksum/size mismatch')
        if asset['Type'] == 'Full': validate_bundle(packages / filename, version, configuration, repository)
        elif asset['Type'] == 'Delta':
            with zipfile.ZipFile(packages / filename) as archive:
                for name in archive.namelist():
                    if not name.endswith('/'): safe_path(name, delta=True)
        else: raise ValueError('Unsupported package type')
    if reconstructed:
        validate_bundle(reconstructed, version, configuration, repository)
        full = packages / next(asset['FileName'] for asset in entries if asset['Type']=='Full')
        def contents(path):
            with zipfile.ZipFile(path) as archive:
                return {name:hashlib.sha256(archive.read(name)).digest() for name in archive.namelist() if not name.endswith('/')}
        if contents(full) != contents(reconstructed): raise ValueError('Delta reconstruction differs from target full package')
    assets = json.loads((packages / f'assets.{CHANNEL}.json').read_text(encoding='utf-8-sig'))
    upload = output / 'upload'
    upload.mkdir()  # Refuse to replace a prior preparation.
    selected = set(asset['FileName'] for asset in entries)
    for asset in assets:
        name = asset['RelativeFileName']; safe_path(name)
        if Path(name).name != name: raise ValueError('Invalid deployment asset path')
        if asset['Type'] in ('Installer', 'Portable'):
            selected.add(name)
            if asset['Type'] == 'Portable': validate_bundle(packages / name, version, configuration, repository)
    if not any(name.endswith('Setup.exe') for name in selected): raise ValueError('Installer missing')
    for name in sorted(selected): shutil.copyfile(packages / name, upload / name)
    (upload / f'releases.{CHANNEL}.json').write_text(json.dumps({'Assets':entries}), encoding='utf-8')
    (upload / f'assets.{CHANNEL}.json').write_text(json.dumps([asset for asset in assets if asset['RelativeFileName'] in selected]), encoding='utf-8')
    shutil.copyfile(output / 'RELEASE_NOTES.md', upload / 'RELEASE_NOTES.md')
    records = [{'File':p.name, 'Bytes':p.stat().st_size, 'Sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(upload.iterdir())]
    (upload / 'SHA256SUMS').write_text(''.join(f"{row['Sha256']}  {row['File']}\n" for row in records), encoding='utf-8')
    return records

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path); parser.add_argument('version')
    parser.add_argument('configuration', choices=['Local','RemoteTest','Production']); parser.add_argument('--repository', default='')
    parser.add_argument('--base', type=Path)
    parser.add_argument('--reconstructed', type=Path)
    args = parser.parse_args()
    if args.base:
        validate_configuration(args.configuration, args.repository)
        validate_bundle(args.base, args.version, args.configuration, args.repository)
        records = []
    else: records = prepare(args.output, args.version, args.configuration, args.repository, args.reconstructed)
    print(json.dumps({'Validated':True, 'Configuration':args.configuration, 'Repository':args.repository, 'Artifacts':records}, indent=2))
