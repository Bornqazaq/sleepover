"""Derive the circus mascot from the approved sandbox; atlas, skin and authored clips.
Run through Blender MCP with the saved sandbox open. Never overwrites the sandbox.
"""
import bpy, math, json, runpy
from pathlib import Path
from mathutils import Vector, Quaternion
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'art-source/CircusMascot'
GAME=ROOT/'igruha/Assets/_Project/Art/CircusNight/Mascot'
SOURCE.mkdir(parents=True,exist_ok=True);GAME.mkdir(parents=True,exist_ok=True)
assert 'CharacterSandbox' in bpy.data.filepath, 'Start from the original sandbox.'
if bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
s=bpy.context.scene;s.frame_set(1);rig=bpy.data.objects['CavemanSponge_Rig']
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'CircusMascot.blend'))
rig.animation_data_clear()
for a in list(bpy.data.actions):bpy.data.actions.remove(a)
for p in rig.pose.bones:
    p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
    for c in list(p.constraints):p.constraints.remove(c)
for p in rig.pose.bones:p.custom_shape=None
rig.animation_data_clear()
for side in ['L','R']:
    rig['Blink_'+side]=0
    keys=bpy.data.objects['UpperLid.'+side].data.shape_keys
    keys.animation_data_clear();keys.key_blocks['Blink'].value=0
face_tools=runpy.run_path(str(ROOT/'tools/blender/circus_mascot_face.py'))
face_tools['prepare_face'](rig)
meshes=[o for o in bpy.data.collections['01_CHARACTER'].objects if o.type=='MESH']
lids=[o for o in meshes if o.data.shape_keys]
main=[o for o in meshes if o not in lids]
for ob in main:
    bpy.context.view_layer.objects.active=ob
    for m in list(ob.modifiers):
        if m.type!='ARMATURE':bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.select_all(action='DESELECT')
for ob in main:ob.select_set(True)
bpy.context.view_layer.objects.active=bpy.data.objects['SpongeBody • rounded porous skin'];bpy.ops.object.join()
body=bpy.context.object;body.name='MascotSkin';meshes=[body]+lids
for i,ob in enumerate(meshes):
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.003)
    bpy.ops.object.mode_set(mode='OBJECT')
    for uv in ob.data.uv_layers.active.data:
        if i==0:uv.uv.x=uv.uv.x*.875+.004;uv.uv.y=uv.uv.y*.992+.004
        else:uv.uv.x=.89+uv.uv.x*.105;uv.uv.y=.01+(i-1)*.5+uv.uv.y*.48
image=bpy.data.images.new('CN_Mascot_Albedo',width=2048,height=2048,alpha=False)
image.generated_color=(.85,.72,.02,1)
for mat in {m for o in meshes for m in o.data.materials if m}:
    n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=image;mat.node_tree.nodes.active=n
s.render.engine='CYCLES';s.cycles.samples=1;s.render.bake.use_clear=False;s.render.bake.margin=8
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=body
bpy.ops.object.bake(type='DIFFUSE',pass_filter={'COLOR'})
image.filepath_raw=str(GAME/'CN_Mascot_Albedo.png');image.file_format='PNG';image.save();image.pack()
mat=bpy.data.materials.new('CN_Mascot_Atlas');mat.use_nodes=True
principled=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
mat.node_tree.links.new(tex.outputs['Color'],principled.inputs['Base Color']);principled.inputs['Roughness'].default_value=.56
for o in meshes:
    o.data.materials.clear();o.data.materials.append(mat)
    for p in o.data.polygons:p.material_index=0
bpy.context.view_layer.objects.active=body
dec=body.modifiers.new('Game silhouette reduction','DECIMATE');dec.ratio=.29;bpy.ops.object.modifier_apply(modifier=dec.name)
smile,opened=face_tools['add_expressions'](body)
# Keep four normalized weights after collapse interpolation.
for o in meshes:
    for v in o.data.vertices:
        values=sorted([(g.group,g.weight) for g in v.groups],key=lambda x:-x[1])[:4]
        for idx in [g.group for g in v.groups]:o.vertex_groups[idx].remove([v.index])
        total=sum(w for _,w in values)
        for idx,w in values:o.vertex_groups[idx].add([v.index],w/total,'REPLACE')

def rotate(name,axis,angle):
    b=rig.data.bones[name];v=b.matrix_local.to_quaternion().inverted()@Vector(axis)
    rig.pose.bones[name].rotation_quaternion=Quaternion(v,angle)
def aim(name,direction):
    bpy.context.view_layer.update();p=rig.pose.bones[name]
    delta=(p.tail-p.head).rotation_difference(Vector(direction))
    matrix=p.matrix.copy();matrix=delta.to_matrix().to_4x4()@matrix;matrix.translation=p.head
    p.matrix=matrix;bpy.context.view_layer.update()
def solve_arm(goal,side='R'):
    p=rig.pose.bones['UpperArm.'+side];q=rig.pose.bones['Forearm.'+side]
    bpy.context.view_layer.update();start=p.head.copy();delta=goal-start
    a=p.bone.length;b=q.bone.length;d=min(delta.length,a+b-.015);direction=delta.normalized()
    pole=Vector((-1 if side=='R' else 1,0,-.45));pole=(pole-direction*pole.dot(direction)).normalized()
    along=(a*a+d*d-b*b)/(2*d);elbow=start+direction*along+pole*math.sqrt(max(.001,a*a-along*along))
    aim('UpperArm.'+side,elbow-start);aim('Forearm.'+side,goal-rig.pose.bones['Forearm.'+side].head)
    if side=='R':
        aim('Hand.R',(0,-1,.06))
        aim('Index1.R',(0,-1,.05));aim('Index2.R',(0,-1,.05))
def reset():
    for p in rig.pose.bones:p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
def key(frame):
    for p in rig.pose.bones:
        if p.bone.use_deform:
            p.keyframe_insert('rotation_quaternion',frame=frame,group=p.name)
            p.keyframe_insert('location',frame=frame,group=p.name)
def smooth(x):x=max(0,min(1,x));return x*x*(3-2*x)
rig.animation_data_create()
for name,length in [('Mascot_Idle',4),('Mascot_Taunt',3.6)]:
    action=bpy.data.actions.new(name);action.use_fake_user=True;rig.animation_data.action=action
    for f in range(int(length*30)+1):
        t=f/30;reset()
        rotate('UpperArm.L',(0,1,0),.38);rotate('UpperArm.R',(0,1,0),-.38)
        rotate('Head',(0,0,1),.025*math.sin(t*1.7));rotate('Chest',(1,0,0),.018*math.sin(t*2))
        if name=='Mascot_Taunt':
            envelope=smooth(t/.48)*(1-smooth((t-3.05)/.55))
            beat=max(0,math.sin((t-.52)*math.pi*5.5))*envelope
            rotate('Head',(1,0,0),(-.12+.10*beat)*envelope)
            rotate('Chest',(1,0,0),.035*beat);rotate('Spine',(0,1,0),.04*envelope)
            rotate('Jaw',(1,0,0),(.10+.28*beat)*envelope)
            rotate('UpperArm.L',(0,1,0),.55);rotate('Forearm.L',(0,0,1),-.95*envelope)
            for finger in ['Middle','Ring','Thumb']:
                rotate(finger+'1.R',(1,0,0),1.1*envelope);rotate(finger+'2.R',(1,0,0),1.45*envelope)
            old={n:rig.pose.bones[n].rotation_quaternion.copy() for n in ['UpperArm.R','Forearm.R','Hand.R','Index1.R','Index2.R']}
            solve_arm(Vector((-1.35,-1.15,2.0+.025*beat)))
            for n,q in old.items():rig.pose.bones[n].rotation_quaternion=q.slerp(rig.pose.bones[n].rotation_quaternion,envelope)
            solve_arm(Vector((.78,-.62,1.65)),'L')
        key(f+1)
rig.animation_data.action=bpy.data.actions['Mascot_Idle'];s.frame_set(1)
# Preview drivers use the same shared timeline as CircusMascotActor in Unity.
for shape,expression in [(smile,'env'),(opened,'env*(0.12+0.88*max(0,sin((frame/30-0.40)*17.27876))**0.7)')]:
    driver=shape.driver_add('value').driver;driver.type='SCRIPTED'
    var=driver.variables.new();var.name='env';var.type='SINGLE_PROP'
    var.targets[0].id=rig;var.targets[0].data_path='["LaughPreview"]'
    driver.expression=expression
rig['LaughPreview']=0.0
s.frame_start=1;s.frame_end=121;s.render.fps=30
# Only export deform bones plus their hierarchy. Eye shape keys remain independently controlled.
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'CircusMascot.blend'))
bpy.ops.export_scene.fbx(filepath=str(GAME/'CN_Mascot.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},
    axis_forward='-Z',axis_up='Y',add_leaf_bones=False,use_armature_deform_only=True,
    bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,
    use_mesh_modifiers=False,path_mode='AUTO')
report={'meshes':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes),
        'actions':[a.name for a in bpy.data.actions],'blendshapes':{o.name:[k.name for k in o.data.shape_keys.key_blocks] for o in meshes if o.data.shape_keys},'atlas':'2048x2048'}
(SOURCE/'export-check.json').write_text(json.dumps(report,indent=2));print(report)
