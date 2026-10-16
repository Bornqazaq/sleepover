"""Rebuild the supplied horse through Blender MCP.
Open art-source/HorseRingmaster/Meshy_Source.blend first, then run this script
in a separate MCP call so Blender has a valid view-layer context.
"""
import bpy,runpy
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
ob=bpy.data.objects.get('Mesh_0')
assert ob is not None and len(ob.data.vertices)>800000, 'Open the untouched Meshy_Source.blend first.'
bottom=min(v.co.z for v in ob.data.vertices);top=max(v.co.z for v in ob.data.vertices);scale=4.14/(top-bottom)
for v in ob.data.vertices:v.co=(v.co.x*scale,v.co.y*scale,(v.co.z-bottom)*scale)
ob.name='Horse_Meshy_Skin';bpy.context.view_layer.objects.active=ob;ob.select_set(True)
mod=ob.modifiers.new('Preserve sculpt silhouette','DECIMATE');mod.ratio=.06;bpy.ops.object.modifier_apply(modifier=mod.name)
for p in ob.data.polygons:p.use_smooth=True
for script in ['paint','mouth','rig','hands','finish','export']:
    runpy.run_path(str(ROOT/'tools/blender'/('meshy_ringmaster_'+script+'.py')))
