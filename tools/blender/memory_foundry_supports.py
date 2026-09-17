"""Original hydraulic plate supports; run via Blender MCP. Does not rebuild other kits."""
import bpy, bmesh, math, ast
from pathlib import Path
from mathutils import Vector, Matrix
ROOT = Path(__file__).resolve().parents[2]
ART = ROOT/'igruha/Assets/_Project/Art/MemoryFoundry'
SOURCE = ROOT/'igruha/Assets/_Project/Art/Source/MemoryFoundry'
scene = bpy.data.scenes.get('MemoryFoundry_Supports') or bpy.data.scenes.new('MemoryFoundry_Supports')
bpy.context.window.scene = scene
for obj in list(scene.objects): bpy.data.objects.remove(obj, do_unlink=True)
materials=[]
for name,color,metal in [('Iron',(.13,.17,.18),.65),('Steel',(.52,.56,.56),.55),('SafetyOchre',(.90,.59,.14),.3)]:
    material=bpy.data.materials.get('MF_'+name) or bpy.data.materials.new('MF_'+name)
    material.use_nodes=True
    bs=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Metallic'].default_value=metal
    materials.append(material)
STONE=IRON=0; STEEL=1; YELLOW=2; PI=math.pi; _box_cache={}
for node in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
    if isinstance(node,(ast.ClassDef,ast.FunctionDef)) and node.name in {'Mesh','box','lathe','tube'}:
        exec(compile(ast.Module(body=[node],type_ignores=[]),'own_geometry','exec'))
m=Mesh()
# Broad load head, piston and bolted bearing at the lower truss; top is under the plate.
for y in (-.96,.96):
    box(m,(0,y,-.47),(2.68,.17,.22),IRON)
    for x in (-1.12,1.12):
        tube(m,[(x,y,-.49),(0,y*.22,-1.48)],.085,STEEL,8)
box(m,(0,0,-.55),(.8,2.22,.24),IRON)
lathe(m,(0,0,-6.7),[(0,.48),(.16,.48),(.22,.31),(3.6,.31),(3.72,.4),(3.9,.4)],YELLOW,20)
lathe(m,(0,0,-3),[(0,.19),(2.4,.19),(2.52,.30)],STEEL,20)
for z in (-6.57,-3.12):
    lathe(m,(0,0,z),[(0,.42),(.1,.42)],IRON,20)
    for j in range(8):
        a=j*PI/4
        lathe(m,(math.cos(a)*.35,math.sin(a)*.35,z+.1),[(0,.045),(.045,.045)],STEEL,6)
for side in (-1,1):
    tube(m,[(side*.35,0,-6.4),(side*.48,0,-5.9),(side*.48,0,-3.5),(side*.25,0,-2.9)],.035,IRON,8)
box(m,(0,0,-6.8),(1.1,1,.22),IRON)
obj=m.object('MF_PlateSupport')
bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
bpy.ops.export_scene.fbx(filepath=str(ART/'Models/MF_PlateSupport.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'MemoryFoundry_Supports.blend'))
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        region=area.spaces.active.region_3d
        region.view_location=(0,0,-3.1); region.view_distance=10
print('PlateSupport',len(m.v),'vertices',len(m.f),'faces')
