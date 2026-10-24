"""Read-only inventory of the uploaded project; mesh import still requires Unity."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess


def audit(project: Path):
    project = project.resolve()
    if 'avatar-unity' not in (p.name for p in (project, *project.parents)):
        raise ValueError('Only the designated avatar-unity duplicate is allowed')
    version = (project / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ', 1)[1]
    assets = list((project / 'Assets').rglob('*'))
    guids = {}
    for path in assets:
        if path.suffix == '.meta':
            match = re.search(r'^guid: (\w+)', path.read_text(errors='replace'), re.M)
            if match:
                guids[match[1]] = path.relative_to(project).as_posix()[:-5]
    scenes = []
    for path in (project / 'Assets').glob('*.unity'):
        text = path.read_text()
        for block in re.split(r'^--- !u!', text, flags=re.M):
            if '  VisemeBlendShapes:' not in block:
                continue
            refs = sorted(set(re.findall(r'guid: (\w+)', block)))
            scenes.append({'scene': path.relative_to(project).as_posix(),
                'descriptor_references': {g: guids.get(g, 'unresolved package/generated GUID') for g in refs},
                'visemes': re.findall(r'^  - (.+)$', block.split('  VisemeBlendShapes:', 1)[1].split('  unityVersion:')[0], re.M)})
    lfs = []
    for line in subprocess.check_output(['git', 'lfs', 'ls-files', '--json'], cwd=project, text=True).splitlines():
        lfs.append(line)
    objects = json.loads('\n'.join(lfs))['files']
    repo = Path(subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], cwd=project, text=True).strip())
    verified = []
    for entry in objects:
        path = repo / entry['name']
        digest = hashlib.sha256()
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                digest.update(chunk)
        if digest.hexdigest() != entry['oid']:
            raise ValueError(f'LFS content missing or corrupt: {entry["name"]}')
        verified.append({'path': entry['name'], 'bytes': path.stat().st_size, 'sha256': entry['oid']})
    vpm = json.loads((project / 'Packages/vpm-manifest.json').read_text())
    return {'inspection': 'static file/GUID inventory and actual LFS SHA256 verification; no Unity import or render',
        'source_commit': subprocess.check_output(['git', 'rev-parse', 'origin/main'], cwd=project, text=True).strip(),
        'project': project.relative_to(repo).as_posix(), 'unity_version': version,
        'scenes_with_descriptors': scenes,
        'counts': {suffix: sum(p.suffix == suffix for p in assets) for suffix in ('.prefab', '.controller', '.mat', '.anim', '.shader', '.fbx')},
        'missing_vpm_packages': {name: data['version'] for name, data in vpm['locked'].items() if not (project / 'Packages' / name / 'package.json').exists()},
        'lfs_verified': verified,
        'face_animation_bindings': sorted(set(re.findall(r'attribute: blendShape\.(.+)', '\n'.join(p.read_text() for p in (project / 'Assets/Proppa/Animations').rglob('*.anim'))))),
        'limitations': ['Actual FBX blendshape names/rig/material rendering require Unity 2022.3.22f1 import.',
            'VRC PhysBones/contacts/state drivers require standalone replacements.',
            'No original scene, material, mesh or controller was modified by this audit.']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parents[1] / 'avatar-unity/Fenriru')
    parser.add_argument('--output', type=Path, default=Path('docs/validation/unity-source.json'))
    args = parser.parse_args()
    result = audit(args.project)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: result[k] for k in ('project', 'unity_version', 'counts', 'missing_vpm_packages')}, indent=2))
    print(f'Verified {len(result["lfs_verified"])} actual LFS files; report: {args.output}')
