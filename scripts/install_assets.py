"""Verified, resumable installation downloads. Never used during normal operation."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tarfile
import time
import urllib.request
import zipfile


def digest(path, algorithm='sha256'):
    result = hashlib.new(algorithm)
    with Path(path).open('rb') as f:
        while block := f.read(1024 * 1024): result.update(block)
    return result.hexdigest()


def download(url, destination, sha256, expected_size=None):
    destination = Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists() and digest(destination) == sha256:
        print('Verified existing:', destination.name, flush=True)
        return
    partial = destination.with_suffix(destination.suffix + '.part')
    for attempt in range(4):
        try:
            offset = partial.stat().st_size if partial.exists() else 0
            request = urllib.request.Request(url, headers={'Range': f'bytes={offset}-'} if offset else {})
            with urllib.request.urlopen(request, timeout=90) as response:
                append = offset > 0 and response.status == 206
                if not append: offset = 0
                total = expected_size or offset + int(response.headers.get('Content-Length', 0))
                with partial.open('ab' if append else 'wb') as output:
                    last = time.monotonic()
                    while block := response.read(1024 * 1024):
                        output.write(block)
                        offset += len(block)
                        if time.monotonic() - last > 3:
                            print(f'{destination.name}: {offset/1048576:.0f} / {total/1048576:.0f} MiB', flush=True)
                            last = time.monotonic()
            if expected_size and partial.stat().st_size != expected_size:
                raise IOError('Downloaded size differs from manifest')
            if digest(partial) != sha256:
                partial.unlink(missing_ok=True)
                raise IOError('SHA-256 mismatch; artifact rejected')
            os.replace(partial, destination)
            print('Verified:', destination.name, flush=True)
            return
        except Exception:
            if attempt == 3: raise
            time.sleep(min(2 ** attempt, 8))


def safe_extract(archive, destination):
    destination = Path(destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    if zipfile.is_zipfile(archive):
        with zipfile.ZipFile(archive) as z:
            for member in z.infolist():
                target = (destination / member.filename).resolve()
                if not target.is_relative_to(destination) or (member.external_attr >> 16) & 0o170000 == 0o120000:
                    raise ValueError('Unsafe ZIP member')
            z.extractall(destination)
    else:
        with tarfile.open(archive) as t:
            for member in t.getmembers():
                if member.issym() or member.islnk() or not (member.isfile() or member.isdir()):
                    raise ValueError('Archive links/devices are not allowed')
                if not (destination / member.name).resolve().is_relative_to(destination):
                    raise ValueError('Archive escapes destination')
            t.extractall(destination, filter='data')


def json_url(url):
    with urllib.request.urlopen(url, timeout=40) as response:
        return json.load(response)


def atomic(path, value):
    path = Path(path)
    temp = path.with_suffix('.new')
    temp.write_text(json.dumps(value, indent=2), encoding='utf-8')
    temp.replace(path)


def resolve_models(root):
    lock_path = root / 'config' / 'model-lock.json'
    if lock_path.exists():
        return json.loads(lock_path.read_text(encoding='utf-8'))
    # Freeze immutable revisions and artifact hashes before downloading any weights.
    # The distributed model lock is filled during release validation when access is available.
    bundled = root / 'assets' / 'model-lock.json'
    if bundled.exists():
        lock = json.loads(bundled.read_text(encoding='utf-8'))
        atomic(lock_path, lock)
        return lock
    repos = [('chat', 'Qwen/Qwen3-VL-4B-Instruct-GGUF'),
             ('whisper', 'Systran/faster-whisper-base.en')]
    lock = {'schema': 1, 'models': [], 'resolution': 'first-install immutable revision lock'}
    for kind, repo in repos:
        data = json_url('https://huggingface.co/api/models/' + repo + '?blobs=true')
        revision = data['sha']
        siblings = data['siblings']
        if kind == 'chat':
            weights = [f for f in siblings if f['rfilename'].lower().endswith('.gguf') and 'q4_k_m' in f['rfilename'].lower() and 'mmproj' not in f['rfilename'].lower()]
            projectors = [f for f in siblings if 'mmproj' in f['rfilename'].lower() and f['rfilename'].endswith('.gguf')]
            if len(weights) != 1 or not projectors:
                raise RuntimeError('Model layout changed; release needs a reviewed model manifest')
            projector = next((f for f in projectors if 'f16' in f['rfilename'].lower()), projectors[0])
            selected = weights + [projector]
        else:
            selected = [f for f in siblings if f['rfilename'] in ('model.bin','config.json','tokenizer.json','vocabulary.json','vocabulary.txt','preprocessor_config.json')]
            if not {'model.bin','config.json','tokenizer.json'}.issubset({f['rfilename'] for f in selected}):
                raise RuntimeError('Whisper repository layout changed')
        files = []
        for f in selected:
            name = f['rfilename']
            url = f'https://huggingface.co/{repo}/resolve/{revision}/{name}'
            lfs = f.get('lfs') or {}
            sha = lfs.get('sha256')
            size = lfs.get('size') or f.get('size')
            if not sha:
                # Small Git-tracked configs are fetched by immutable commit and checked
                # against the catalog's Git blob SHA-1 before freezing SHA-256.
                with urllib.request.urlopen(url, timeout=40) as response: content = response.read(10_000_000)
                git_hash = hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest()
                if f.get('blobId') and git_hash != f['blobId']:
                    raise IOError('Git object checksum mismatch')
                sha, size = hashlib.sha256(content).hexdigest(), len(content)
            files.append({'name': Path(name).name, 'url': url, 'sha256': sha, 'bytes': size})
        lock['models'].append({'kind': kind, 'repository': repo, 'revision': revision,
                               'license': data.get('cardData',{}).get('license','see upstream model card'), 'files': files})
    atomic(lock_path, lock)
    return lock


def tree_manifest(path):
    return {str(p.relative_to(path)):digest(p) for p in path.rglob('*') if p.is_file()}


def tree_matches(path, manifest):
    return bool(manifest) and all((path/name).is_file() and digest(path/name)==sha for name,sha in manifest.items())


def install(root, cpu=False):
    manifest = json.loads((root/'assets/bootstrap-artifacts.json').read_text())
    state_path = root/'config/assets-state.json'
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    names = ('llama_cpu',) if cpu else ('llama_cuda','llama_cudart')
    runtime_key = ':'.join(manifest[name]['sha256'] for name in names)
    active, staging, previous = root/'runtime/llama', root/'runtime/llama-stage', root/'runtime/llama-previous'
    runtime_ok = state.get('runtime_key')==runtime_key and tree_matches(active,state.get('runtime_files',{}))
    if not runtime_ok:
        if staging.exists(): shutil.rmtree(staging)
        for name in names:
            entry=manifest[name]
            archive=root/'downloads'/entry['filename']
            download(entry['url'],archive,entry['sha256'],entry['bytes'])
            safe_extract(archive,staging)
        executables=list(staging.rglob('llama-server.exe'))
        if len(executables)!=1:raise RuntimeError('Unexpected llama runtime layout')
        server_dir=executables[0].parent
        for dll in list(staging.rglob('*.dll')):
            if dll.parent!=server_dir and not (server_dir/dll.name).exists():shutil.copy2(dll,server_dir/dll.name)
    voice_entry=manifest['voice']
    voice=root/'models/voice'
    voice_ok=state.get('voice_key')==voice_entry['sha256'] and tree_matches(voice,state.get('voice_files',{}))
    if not voice_ok:
        archive=root/'downloads'/voice_entry['filename']
        download(voice_entry['url'],archive,voice_entry['sha256'],voice_entry['bytes'])
        voice_stage=root/'models/voice-stage'
        if voice_stage.exists():shutil.rmtree(voice_stage)
        safe_extract(archive,voice_stage)
    lock=resolve_models(root)
    required=sum(f.get('bytes') or 0 for m in lock['models'] for f in m['files'])
    missing=sum(f.get('bytes') or 0 for m in lock['models'] for f in m['files'] if not (root/'models'/m['kind']/f['name']).exists())
    if shutil.disk_usage(root).free<missing+2*1024**3:
        raise RuntimeError(f'Insufficient free storage: models require {missing/1024**3:.1f} GiB plus 2 GiB working headroom')
    print(f'Model storage: {required/1024**3:.2f} GiB; approximate GPU budget 6 GiB with 4096 context',flush=True)
    for model in lock['models']:
        for f in model['files']:download(f['url'],root/'models'/model['kind']/f['name'],f['sha256'],f.get('bytes'))
    if not runtime_ok:
        if previous.exists():shutil.rmtree(previous)
        if active.exists():active.rename(previous)
        try:staging.rename(active)
        except Exception:
            if previous.exists():previous.rename(active)
            raise
    if not voice_ok:
        voice_previous=root/'models/voice-previous'
        if voice_previous.exists():shutil.rmtree(voice_previous)
        if voice.exists():voice.rename(voice_previous)
        try:voice_stage.rename(voice)
        except Exception:
            if voice_previous.exists():voice_previous.rename(voice)
            raise
    if state_path.exists():shutil.copy2(state_path,root/'config/assets-state.previous.json')
    atomic(state_path,{'schema':2,'backend':'cpu' if cpu else 'cuda12.4','models':lock,
                      'runtime_key':runtime_key,'runtime_files':tree_manifest(active),
                      'voice_key':voice_entry['sha256'],'voice_files':tree_manifest(voice),
                      'changed_runtime':not runtime_ok,'changed_voice':not voice_ok})


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,required=True)
    parser.add_argument('--cpu',action='store_true')
    args=parser.parse_args()
    install(args.root.resolve(), args.cpu)
