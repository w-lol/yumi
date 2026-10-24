"""Explicit, reversible local model selection; never silently mix vision projectors."""
import json
from pathlib import Path


def model_paths(root, profile='current'):
    root=Path(root)
    if profile=='current':
        files=sorted((root/'models/chat').glob('*.gguf'))
        models=[p for p in files if 'mmproj' not in p.name.lower()]
        projectors=[p for p in files if 'mmproj' in p.name.lower()]
        if len(models)!=1 or len(projectors)>1:raise RuntimeError('Current local model is missing or ambiguous; run setup/repair.')
        return models[0],projectors[0] if projectors else None
    if profile!='qwen3-vl-8b':raise ValueError('Unknown local conversation profile')
    data=json.loads((root/'assets/conversation-model.json').read_text(encoding='utf-8'))
    folder=root/'models/chat-profiles'/profile
    found=[]
    for entry in data['files']:
        if Path(entry['name']).name!=entry['name']:raise ValueError('Unsafe model filename')
        path=folder/entry['name']
        if not path.is_file() or path.stat().st_size!=entry['bytes']:raise RuntimeError('The 8B profile is not fully installed. Select Current or install the conversation upgrade.')
        found.append(path)
    models=[p for p in found if 'mmproj' not in p.name.lower()];projectors=[p for p in found if 'mmproj' in p.name.lower()]
    if len(models)!=1 or len(projectors)!=1:raise RuntimeError('Invalid 8B model/projector pair')
    return models[0],projectors[0]
