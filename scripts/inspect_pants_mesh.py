"""Run with Blender --background --factory-startup --python scripts/inspect_pants_mesh.py.
Reads the original FBX and records geometry evidence. Never saves or modifies the FBX.
This is not a Unity render/import validation.
"""
import hashlib
import json
from pathlib import Path
import bpy

root=Path(__file__).resolve().parents[1]
source=root/'avatar-unity/Fenriru/Assets/ITEMS/Model/tda_dl_pouchy_v1_cyberpunk__by_kakaiya_ddawp25/untitled.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(source))
sections=[]
for obj in bpy.data.objects:
    if obj.type!='MESH':continue
    for index,material in enumerate(obj.data.materials):
        polygons=[p for p in obj.data.polygons if p.material_index==index]
        if not polygons:continue
        points=[obj.matrix_world@obj.data.vertices[i].co for p in polygons for i in p.vertices]
        sections.append({'mesh':obj.name,'material':material.name if material else None,
            'polygons':len(polygons),'triangles':sum(len(p.vertices)-2 for p in polygons),
            'bounds_world_meters':[[min(v[k] for v in points) for k in range(3)],
                                   [max(v[k] for v in points) for k in range(3)]],
            'surface_area_local':sum(p.area for p in polygons)})
result={'inspection':'Blender FBX mesh audit; no Unity import/render performed',
        'blender':bpy.app.version_string,'source':source.relative_to(root).as_posix(),
        'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'sections':sections,
        'repair_scope':'Generated copy only: omit unassigned EyeBase/Blush donor-face submeshes; retain Belts clothing geometry/material. Exact fingerprint and layout required.'}
output=root/'docs/validation/pants-mesh-audit.json';output.parent.mkdir(exist_ok=True,parents=True)
output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print('Read-only mesh report:',output)
