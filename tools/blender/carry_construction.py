"""Precast concrete and formwork kit for CarryItem; execute in Blender MCP."""
import bpy, bmesh, math, ast, json
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/CarryItem/HeistRoutes'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CarrySkyscraper'
scene=bpy.data.scenes.new('CarryConstruction_Build'); bpy.context.window.scene=scene
for previous in list(bpy.data.scenes):
 if previous!=scene and previous.name.startswith('CarryConstruction_'):
  for obj in list(previous.objects): bpy.data.objects.remove(obj,do_unlink=True)
  bpy.data.scenes.remove(previous)
scene.name='CarryConstruction_697'; materials=[]; _box_cache={}; STONE=0; PI=math.pi
for node in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
 if isinstance(node,(ast.ClassDef,ast.FunctionDef)) and node.name in {'Mesh','transform','box','lathe','tube'}:
  exec(compile(ast.Module(body=[node],type_ignores=[]),'geometry','exec'))
exec(compile((ROOT/'tools/blender/carry_geometry.py').read_text(), 'carry_geometry', 'exec'))
for entry in json.loads((ART.parent/'Roadworks/palette.json').read_text())['materials']:
 mat=bpy.data.materials.get(entry['name']) or bpy.data.materials.new(entry['name'])
 mat.diffuse_color=tuple(entry['color']);materials.append(mat)
STEEL,EDGE,YELLOW,DARK,IVORY,WOOD,BRICK,TEAL,CONCRETE,RUST=range(10)
def export(mesh,name):
 obj=mesh.object('HR_'+name);used=sorted(set(mesh.m));obj.data.materials.clear()
 for index in used:obj.data.materials.append(materials[index])
 for face,index in zip(obj.data.polygons,mesh.m):face.material_index=used.index(index)
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('HR_'+name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 obj.location=(len(scene.objects)*6,0,0)
# Four cast sections: bevelled real edges, lift recesses and longitudinal bearings.
m=Mesh()
for x in (-1,1):
 for y in (-1.1,1.1):
  box(m,(x,y,-.145),(1.995,2.195,.29),CONCRETE,.018)
  for dx in (-.66,.66):
   box(m,(x+dx,y,-.006),(.12,.20,.008),DARK,.025)
   tube(m,[(x+dx,y-.065,-.004),(x+dx,y+.065,-.004)],.013,EDGE,8)
for y in (-2.05,2.05):box(m,(0,y,-.34),(4,.16,.16),EDGE,.01)
for x in (-1.8,-.6,.6,1.8):
 for y in (-2.199,2.199):box(m,(x,y,-.14),(.10,.012,.06),DARK,.008)
export(m,'PrecastDeck')
# Stacked lintels with timber spacers and visible steel lift loops.
m=Mesh()
for level in range(3):
 for y in (-.48,.48):box(m,(0,y,.10+level*.37),(2.4,.14,.15),WOOD,.015)
 for y in (-.37,.37):
  box(m,(0,y,.27+level*.37),(2.7,.64,.22),CONCRETE,.035)
  for x in (-.9,.9):tube(m,[(x-.07,y,.38+level*.37),(x-.07,y,.44+level*.37),(x+.07,y,.44+level*.37),(x+.07,y,.38+level*.37)],.013,RUST,8)
export(m,'PrecastStack')
# A folded set of shutter panels, braced on trestles; hardware is the silhouette.
m=Mesh()
for x in (-.92,.92):
 for y in (-.42,.42):box(m,(x,y,.43),(.10,.10,.86),EDGE,.01)
 box(m,(x,0,.80),(.12,1.25,.12),EDGE,.01)
for layer in range(4):
 box(m,(0,0,.93+layer*.115),(2.4,1.4,.08),WOOD,.012)
 for y in (-.65,.65):box(m,(0,y,.94+layer*.115),(2.45,.07,.105),STEEL,.008)
 for x in (-1.16,0,1.16):box(m,(x,0,.94+layer*.115),(.07,1.4,.105),STEEL,.008)
export(m,'Formwork')
# Bent reinforcement cages stored horizontally on chocks.
m=Mesh()
for x in (-.95,.95):box(m,(x,0,.09),(.18,1.10,.18),WOOD,.015)
for y in (-.3,.3):
 for z in (.28,.85):tube(m,[(-1.5,y,z),(1.5,y,z)],.018,RUST,8)
for x in [-1.4+i*.28 for i in range(11)]:
 tube(m,[(x,-.3,.28),(x,-.3,.85),(x,.3,.85),(x,.3,.28),(x,-.3,.28)],.014,STEEL,8)
export(m,'RebarBundle')
bpy.data.libraries.write(str(SOURCE/'CarryConstruction697.blend'),{scene},fake_user=True)
print('Construction kit:',len(scene.objects),'models')
