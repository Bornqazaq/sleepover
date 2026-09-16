"""Original school hall kit. Run in a separate Blender --background process.
Metres, Blender Z up / Unity Y up. --prototype exports only the lectern.
Reuses our project's geometric primitives, never any purchased mesh.
"""
import bpy, math, random, json, ast, sys, bmesh
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/ExamHall'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/ExamHall'
for p in (ART/'Models',ART/'Textures',SOURCE):p.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
# This standalone generator owns its source; avoid Unity importing .blend1 copies.
bpy.context.preferences.filepaths.save_version = 0
scene=bpy.context.scene;scene.name='OldSchool_ExaminationHall';PI=math.pi
rng=random.Random(150926);materials=[];palette=[]
def mat(name,color,rough=.7,metal=0,texture=None,emission=0):
    m=bpy.data.materials.new('EH_'+name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes['Principled BSDF'];p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
    if emission:p.inputs['Emission Color'].default_value=(*color,1);p.inputs['Emission Strength'].default_value=emission
    materials.append(m);palette.append(dict(name=m.name,color=list(color)+[1],roughness=rough,metallic=metal,texture=texture or '',emission=emission));return len(materials)-1
WOOD=mat('Walnut',(.25,.125,.067),.62,texture='Wood')
OAK=mat('Oak',(.44,.272,.137),.67,texture='Wood')
OAK2=mat('OakLight',(.48,.303,.16),.7,texture='Wood')
DARK=mat('Recess',(.052,.041,.031),.86)
GOLD=mat('Brass',(.67,.45,.19),.36,.65)
IRON=mat('Iron',(.083,.101,.108),.5,.55)
STONE=mat('Limestone',(.73,.667,.524),.88,texture='Plaster')
CREAM=mat('Plaster',(.86,.801,.658),.95,texture='Plaster')
GREEN=mat('Chalkboard',(.044,.142,.111),.96,texture='Slate')
BLUE=mat('EnamelA',(.16,.37,.48),.57)
AMBER=mat('EnamelB',(.69,.352,.09),.57)
IVORY=mat('Ivory',(.94,.877,.688),.8)
RED=mat('Leather',(.30,.07,.052),.79)
GLASS=mat('Window',(.66,.81,.82),.42,emission=.65)
BULB=mat('Opal',(.99,.81,.46),.4,emission=2.2)
PIT=mat('PitStone',(.19,.23,.225),.94,texture='Plaster')
PAPER=mat('Paper',(.79,.75,.60),.97)
# Same verified bevels, lathes, UV projection and FBX conventions as our circus kit.
for node in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
    if isinstance(node,(ast.ClassDef,ast.FunctionDef)) and node.name in {'Mesh','transform','box','lathe','ellipsoid','tube','ring','arc'}:
        exec(compile(ast.Module(body=[node],type_ignores=[]),'own_geometry','exec'))
_box_cache={}
_orig_sphere=ellipsoid
def ellipsoid(m,*a,**kw):
    first=len(m.f);_orig_sphere(m,*a,**kw)
    for i in range(first,len(m.f)):m.f[i]=tuple(reversed(m.f[i]))
exports=[]
def export(m,name):
    obj=m.object('EH_'+name)
    # Remove unused material slots: otherwise every prop reserves the whole palette.
    used=sorted(set(m.m));remap={old:i for i,old in enumerate(used)}
    obj.data.materials.clear()
    for idx in used:obj.data.materials.append(materials[idx])
    for poly,idx in zip(obj.data.polygons,m.m):poly.material_index=remap[idx]
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'EH_{name}.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
    exports.append(dict(name=obj.name,vertices=len(m.v),polygons=len(m.f)))
    return obj

def book(m,x,y,z,w=.28,d=.39,h=.075,ma=RED):
    box(m,(x,y,z+h/2),(w,d,h*.7),PAPER,.005)
    for zz in (z,z+h):box(m,(x,y,zz),(w+.025,d+.025,.015),ma,.004)
    box(m,(x-w/2,y,z+h/2),(.025,d,h),ma,.005)

# Hero lectern: inset panel, turned corner columns, pitched writing surface,
# discreet terminal facing the host (+Y), ink pot, brass bell and ledger.
m=Mesh()
box(m,(0,0,.08),(1.74,.94,.16),WOOD,.04)
box(m,(0,0,.61),(1.48,.74,1.06),WOOD,.035)
box(m,(0,-.391,.60),(1.16,.045,.74),DARK,.02)
box(m,(0,-.421,.60),(1.02,.035,.60),OAK,.025)
for x in (-.68,.68):
    lathe(m,(x,-.38,.15),[(0,.10),(.08,.10),(.13,.07),(.83,.07),(.90,.10),(.96,.10)],WOOD,16,flutes=8)
for z in (.19,1.03):box(m,(0,-.415,z),(1.49,.07,.065),GOLD,.012)
# Open-book brass emblem on the front.
for s in (-1,1):
    box(m,(s*.13,-.46,.63),(.24,.02,.23),GOLD,.008,Matrix.Rotation(s*.08,4,'Y'))
    for z in (.57,.62,.67):box(m,(s*.13,-.479,z),(.17,.01,.006),DARK,0)
box(m,(0,0,1.13),(1.85,1.04,.14),OAK,.04,Matrix.Rotation(-.11,4,'X'))
box(m,(0,.42,1.20),(1.8,.04,.055),GOLD,.008)
book(m,-.39,-.08,1.24,.39,.45,.045,GREEN)
# Low terminal tilted toward the host, no public text or secret state.
box(m,(.36,.07,1.26),(.43,.39,.11),IRON,.026)
box(m,(.36,.095,1.322),(.35,.27,.012),GREEN,.012)
for i in range(3):box(m,(.36,.09+i*.045,1.332),(.24-i*.045,.008,.005),GOLD,0)
lathe(m,(.70,-.18,1.23),[(0,.105),(.03,.105),(.10,.085),(.15,.025)],GOLD,24)
ellipsoid(m,(.70,-.18,1.40),(.025,.025,.025),GOLD,12,8)
export(m,'Lectern')
if '--prototype' not in sys.argv:
    exec(compile((ROOT/'tools/blender/exam_hall_architecture.py').read_text(),'exam_hall_architecture','exec'))
if '--prototype' not in sys.argv:
    exec(compile((ROOT/'tools/blender/exam_hall_props.py').read_text(),'exam_hall_props','exec'))
# Periodic own textures, packed in the editable Blender source.
import numpy as np
N=512;u,v=np.meshgrid(np.arange(N)/N,np.arange(N)/N);noise=np.random.default_rng(159).random((N,N))
textures={'Wood':.91+.012*np.sin(u*PI*30+np.sin(v*PI*4))+.008*np.sin(u*PI*110)+.016*noise,
'Plaster':.92+.05*noise+.012*np.sin(u*PI*8)*np.sin(v*PI*6),
'Slate':.87+.065*noise+.018*np.sin(u*PI*5+np.sin(v*PI*8))}
for name,data in textures.items():
    rgba=np.ones((N,N,4),dtype=np.float32);rgba[:,:,:3]=data[:,:,None]
    im=bpy.data.images.new('EH_'+name,width=N,height=N);im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(ART/'Textures'/f'EH_{name}.png');im.file_format='PNG';im.save()
    for idx,pal in enumerate(palette):
        if pal['texture']!=name:continue
        nodes=materials[idx].node_tree;tex=nodes.nodes.new('ShaderNodeTexImage');tex.image=im
        mult=nodes.nodes.new('ShaderNodeMixRGB');mult.blend_type='MULTIPLY';mult.inputs[0].default_value=1;mult.inputs[2].default_value=materials[idx].diffuse_color
        nodes.links.new(tex.outputs['Color'],mult.inputs[1]);nodes.links.new(mult.outputs[0],nodes.nodes['Principled BSDF'].inputs['Base Color'])
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'ExamHallKit.blend'))
(ART/'palette.json').write_text(json.dumps({'materials':palette},indent=2))
(ART/'geometry.json').write_text(json.dumps(exports,indent=2))
print('EXAM_HALL_EXPORT_COMPLETE',flush=True)
