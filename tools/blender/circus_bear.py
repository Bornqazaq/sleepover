"""Original circus brown bear. Blender 5.2, no external models or animation clips.
Run: blender --background --python tools/blender/circus_bear.py
Metres, +Y forward, Z up; source rig and six editable actions are preserved.
"""
import bpy, math, random, json
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/CircusNight'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/CircusNight'
REVIEW = ROOT / 'docs/art/circus-night'
for p in (ART/'Models', SOURCE, REVIEW): p.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.name = 'Bruno_BrownBear'
scene.unit_settings.system = 'METRIC'
rng = random.Random(791)

def material(name, color, rough=.8):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=rough
    return m
fur=material('CN_UmberFur',(.235,.105,.039))
tip=material('CN_FurTips',(.30,.15,.064))
muzzle=material('CN_Muzzle',(.36,.235,.125))
black=material('CN_Nose',(.018,.011,.008),.48)
darkfur=material('CN_DarkFur',(.054,.028,.012),.93)
eye=material('CN_AmberEyes',(.085,.039,.010),.32)
claw=material('CN_Claws',(.61,.49,.31),.55)
mouth=material('CN_Mouth',(.083,.018,.014),.62)
import numpy as np
N=1024
u,v=np.meshgrid(np.arange(N)/N,np.arange(N)/N)
noise=np.random.default_rng(88)
# Soft overlapping directional fibres; continuous at both texture seams.
hairs=np.full((N,N),.85)
for frequency,amplitude in ((157,.018),(239,.021),(313,.018),(431,.016),(487,.012)):
    phase=noise.uniform(0,math.tau)
    hairs+=amplitude*np.sin(u*math.tau*frequency+np.sin(v*math.tau*3+phase)*1.4+phase)*(.65+.35*np.cos(v*math.tau*7+phase))
hairs+=noise.random((N,N))*.04
rgba=np.ones((N,N,4),dtype=np.float32);rgba[:,:,:3]=hairs[:,:,None]
im=bpy.data.images.new('CN_FurGrain',width=N,height=N);im.pixels.foreach_set(rgba.ravel())
(ART/'Textures').mkdir(exist_ok=True);im.filepath_raw=str(ART/'Textures/CN_FurGrain.png');im.file_format='PNG';im.save()
dx=(np.roll(hairs,1,1)-np.roll(hairs,-1,1))*1.5;dy=(np.roll(hairs,1,0)-np.roll(hairs,-1,0))*1.5
norm=np.stack((dx,dy,np.ones_like(dx)),axis=-1);norm/=np.linalg.norm(norm,axis=-1)[:,:,None]
rgba[:,:,:3]=norm*.5+.5
normalmap=bpy.data.images.new('CN_FurNormal',width=N,height=N);normalmap.colorspace_settings.name='Non-Color';normalmap.pixels.foreach_set(rgba.ravel())
normalmap.filepath_raw=str(ART/'Textures/CN_FurNormal.png');normalmap.file_format='PNG';normalmap.save()
for m in (fur,tip,muzzle,darkfur):
    tree=m.node_tree;tex=tree.nodes.new('ShaderNodeTexImage');tex.image=im
    mix=tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[1].default_value=m.diffuse_color
    tree.links.new(tex.outputs['Color'],mix.inputs[2]);tree.links.new(mix.outputs[0],tree.nodes['Principled BSDF'].inputs['Base Color'])
    bump=tree.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.08;bump.inputs['Distance'].default_value=.004
    tree.links.new(tex.outputs['Color'],bump.inputs['Height']);tree.links.new(bump.outputs['Normal'],tree.nodes['Principled BSDF'].inputs['Normal'])
parts=[]

def oval(name, pos, scale, mat=fur, bone=None, segments=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=rings,location=pos)
    o=bpy.context.object; o.name=name; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(mat)
    for p in o.data.polygons: p.use_smooth=True
    if bone: o['bone']=bone
    parts.append(o); return o

# Interlocking anatomical masses are voxel-unioned, smoothed and decimated into a continuous skin.
oval('Ribcage',(0,-.17,1.26),(.65,1.05,.61))
oval('Haunches',(0,-.94,1.15),(.61,.60,.62))
oval('ShoulderHump',(0,.32,1.51),(.67,.76,.61))
oval('Neck',(0,.91,1.44),(.55,.68,.48))
oval('Skull',(0,1.40,1.54),(.43,.50,.33))
oval('Brow',(0,1.60,1.70),(.37,.30,.145))
oval('SnoutBase',(0,1.78,1.43),(.29,.44,.225))
for sign in (-1,1):
    oval('OrbitalRidge',(sign*.286,1.744,1.681),(.10,.087,.071))
for side,x in [('L',.51),('R',-.51)]:
    oval('ForeShoulder'+side,(x,.37,1.25),(.31,.40,.62))
    oval('ForeShin'+side,(x,.37,.67),(.235,.28,.46))
    oval('ForeWrist'+side,(x,.58,.31),(.22,.27,.25))
    oval('ForePaw'+side,(x,.79,.205),(.29,.39,.20))
    oval('HindThigh'+side,(x,-.88,.99),(.34,.41,.58))
    oval('HindShin'+side,(x,-.90,.47),(.235,.31,.37))
    oval('HindPaw'+side,(x,-.91,.18),(.28,.39,.18))
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join(); skin=bpy.context.object; skin.name='Bruno_ContinuousSkin'
remesh=skin.modifiers.new('Anatomy union','REMESH'); remesh.mode='VOXEL'; remesh.voxel_size=.037; remesh.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=remesh.name)
smooth=skin.modifiers.new('Sculpt smoothing','SMOOTH'); smooth.factor=.8; smooth.iterations=6; bpy.ops.object.modifier_apply(modifier=smooth.name)
# Inset sockets belong to the continuous face, rather than stacked eyelid beads.
for sign in (-1,1):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=(sign*.286,1.806,1.679))
    cutter=bpy.context.object;cutter.scale=(.057,.038,.043)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bpy.context.view_layer.objects.active=skin
    socket=skin.modifiers.new('Inset eye socket','BOOLEAN');socket.operation='DIFFERENCE';socket.object=cutter
    bpy.ops.object.modifier_apply(modifier=socket.name);bpy.data.objects.remove(cutter,do_unlink=True)
    bpy.context.view_layer.objects.active=skin
dec=skin.modifiers.new('Game topology','DECIMATE'); dec.ratio=.36; bpy.ops.object.modifier_apply(modifier=dec.name)
bpy.ops.object.select_all(action='DESELECT');skin.select_set(True);bpy.context.view_layer.objects.active=skin
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
parts=[skin]

# Small ears sit in the fur; narrower eyes and a tapered, flat-nosed muzzle.
for side,s in [('L',1),('R',-1)]:
    ear=oval('Ear'+side,(s*.365,1.16,1.875),(.135,.10,.15),fur,'Ear.'+side);ear.rotation_euler.y=s*.23
    oval('EarInner'+side,(s*.365,1.259,1.895),(.080,.023,.085),darkfur,'Ear.'+side)
    oval('Eye'+side,(s*.286,1.798,1.679),(.033,.018,.026),eye,'Head')
    oval('Pupil'+side,(s*.286,1.813,1.679),(.020,.005,.022),black,'Head')
    oval('BlinkLid'+side,(s*.286,1.816,1.712),(.034,.009,.004),fur,'Lid.'+side)
    oval('EyeGlint'+side,(s*.278,1.818,1.689),(.003,.002,.003),claw,'Head',12,8)
# Muzzle colouring lives on the continuous skin, avoiding a floating circular mask.
skin.data.materials.append(muzzle)
for poly in skin.data.polygons:
    c=poly.center
    if c.y>1.87+abs(c.x)*.40 and 1.30<c.z<1.69:poly.material_index=1
oval('Nose',(0,2.125,1.49),(.185,.10,.10),black,'Head')
for side,s in [('L',1),('R',-1)]:
    oval('Nostril'+side,(s*.11,2.215,1.48),(.025,.008,.015),black,'Head',16,10)
oval('MouthLine',(0,1.96,1.286),(.215,.24,.022),black,'Head')
oval('MouthInterior',(0,1.77,1.29),(.185,.26,.085),mouth,'Head')
oval('LowerJaw',(0,1.85,1.251),(.24,.32,.072),muzzle,'Jaw')
oval('Tongue',(0,1.97,1.305),(.115,.14,.015),mouth,'Jaw')
for side,s in [('L',1),('R',-1)]:
    oval('Canine'+side,(s*.15,2.05,1.325),(.025,.033,.042),claw,'Head',12,8)
oval('Tail',(0,-1.49,1.29),(.14,.23,.14),fur,'Tail')
for side,s in [('L',1),('R',-1)]:
    for limb,y in [('Fore',.79),('Hind',-.91)]:
        for i in range(4):
            x=s*.51+(i-1.5)*.105
            oval(limb+'Toe'+side+str(i),(x,y+.255,.17),(.087,.18,.10),fur,limb+'Toes.'+side,12,8)
            o=oval(limb+'Claw'+side+str(i),(x,y+.396,.134),(.034,.118,.045),claw,limb+'Toes.'+side,12,8); o.rotation_euler.x=-.23

# Broad, curved locks lie against the coat and follow the anatomy. Their tapered
# ends break up the silhouette; they do not read as isolated triangular rivets.
from mathutils.bvhtree import BVHTree
bvh=BVHTree.FromObject(skin,bpy.context.evaluated_depsgraph_get())
verts=[]; faces=[]
for k in range(430):
    a=rng.uniform(0,math.tau); y=rng.uniform(-1.32,1.44)
    ray=Vector((math.cos(a),0,math.sin(a)))
    c,n,idx,dist=bvh.ray_cast(Vector((0,y,1.32))+ray*2,-ray,3)
    if c is None or c.z<1.75:continue
    direction=Vector((n.x*.3,-1,-.35));direction-=n*direction.dot(n);direction.normalize()
    tangent=n.cross(direction).normalized();length=rng.uniform(.08,.13);width=rng.uniform(.012,.022)
    start=len(verts)
    for row,(along,w,lift) in enumerate(((0,.7,-.015),(.33,1,.005),(.72,.6,.012),(1,0,.020))):
        for side in (-1,0,1):
            point=c+direction*(along*length)+tangent*(side*width*w)+n*(lift+(1-abs(side))*.014)
            surface,surface_normal,_,_=bvh.find_nearest(point)
            point=surface+surface_normal*(max(-.008,lift)+(1-abs(side))*.004)
            verts.append(tuple(point))
    for row in range(3):
        for column in range(2):
            i=start+row*3+column;faces.extend([(i,i+3,i+1),(i+1,i+3,i+4)])
me=bpy.data.meshes.new('SculptedFur');me.from_pydata(verts,[],faces);me.materials.append(fur);me.materials.append(tip)
locks=bpy.data.objects.new('Bruno_FurLocks',me);scene.collection.objects.link(locks);parts.append(locks)
for i,p in enumerate(me.polygons):p.material_index=0;p.use_smooth=True
# Generic rig, separate from every player/Humanoid asset.
arm=bpy.data.armatures.new('BrunoSkeleton'); rig=bpy.data.objects.new('BrunoRig',arm);scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
bones={}
def bone(name,head,tail,parent=None):
    b=arm.edit_bones.new(name);b.head=head;b.tail=tail
    if parent:b.parent=arm.edit_bones[parent]
    bones[name]=(Vector(head),Vector(tail));return b
bone('Root',(0,0,0),(0,0,.3))
bone('Pelvis',(0,-.95,1.12),(0,-.55,1.26),'Root')
bone('Lumbar',(0,-.55,1.26),(0,-.10,1.44),'Pelvis')
bone('Spine',(0,-.10,1.44),(0,.38,1.63),'Lumbar')
bone('Chest',(0,.38,1.63),(0,.68,1.62),'Spine')
bone('Neck',(0,.68,1.62),(0,1.15,1.64),'Chest')
bone('Head',(0,1.15,1.64),(0,1.92,1.55),'Neck')
bone('Jaw',(0,1.54,1.30),(0,2.04,1.25),'Head')
bone('Tail',(0,-1.34,1.29),(0,-1.65,1.29),'Pelvis')
for side,s in [('L',1),('R',-1)]:
    x=.51*s
    bone('Scapula.'+side,(x,.10,1.63),(x,.38,1.38),'Chest')
    bone('ForeUpper.'+side,(x,.38,1.38),(x,.20,.73),'Scapula.'+side)
    bone('ForeLower.'+side,(x,.20,.73),(x,.66,.23),'ForeUpper.'+side)
    bone('ForePaw.'+side,(x,.66,.23),(x,1.09,.16),'ForeLower.'+side)
    bone('HindUpper.'+side,(x,-.86,1.22),(x,-.64,.62),'Pelvis')
    bone('HindLower.'+side,(x,-.64,.62),(x,-1.03,.20),'HindUpper.'+side)
    bone('HindPaw.'+side,(x,-1.03,.20),(x,-.59,.16),'HindLower.'+side)
    bone('ForeToes.'+side,(x,.97,.17),(x,1.25,.13),'ForePaw.'+side)
    bone('HindToes.'+side,(x,-.73,.17),(x,-.43,.13),'HindPaw.'+side)
    bone('Lid.'+side,(s*.286,1.816,1.712),(s*.286,1.816,1.763),'Head')
    bone('Ear.'+side,(s*.365,1.16,1.78),(s*.365,1.16,1.99),'Head')
bpy.ops.object.mode_set(mode='OBJECT')

def segdist(p,a,b):
    v=b-a;t=max(0,min(1,(p-a).dot(v)/v.length_squared));return (p-a-v*t).length
for o in parts:
    o.parent=rig
    for name in bones:o.vertex_groups.new(name=name)
    for v in o.data.vertices:
        p=o.matrix_local@v.co
        if 'bone' in o:weights=[(o['bone'],1)]
        else:
            side='L' if p.x>=0 else 'R'
            limb='Fore' if abs(p.y-.44)<abs(p.y+.9) else 'Hind'
            def distribution(candidates):
                ds=[(n,segdist(p,*bones[n])) for n in candidates]
                ws=[math.exp(-d*8) for n,d in ds];total=sum(ws)
                return [(nd[0],w/total) for nd,w in zip(ds,ws)]
            def ease(value,low,high):
                t=max(0,min(1,(value-low)/(high-low)));return t*t*(3-2*t)
            if p.z<.34:weights=[(limb+'Paw.'+side,1)]
            else:
                trunk=distribution(['Pelvis','Lumbar','Spine','Chest','Neck','Head'])
                legweights=distribution([limb+'Upper.'+side,limb+'Lower.'+side,limb+'Paw.'+side])
                amount=ease(abs(p.x),.18,.43)*(1-ease(p.z,1.02,1.60))
                if limb=='Fore':
                    # Let the shoulder cap travel with the raised upper arm.
                    # A hard height cutoff pinched the coat during the swipe.
                    amount=ease(abs(p.x),.20,.55)*(1-ease(p.z,1.30,2.12))*(1-ease(p.y,.80,1.20))
                amount=max(amount,1-ease(p.z,.34,.53))
                weights=[(n,w*(1-amount)) for n,w in trunk]+[(n,w*amount) for n,w in legweights]
        for n,w in weights:o.vertex_groups[n].add([v.index],w,'REPLACE')
    if o==skin:
        # Bone heat follows the connected sculpt around the shoulder socket.
        # Height-based weights fold back on themselves in a high paw lift.
        o.vertex_groups.clear()
        for b in arm.bones:
            b.use_deform=not (b.name in ('Root','Jaw','Tail') or b.name.startswith(('Lid.','Ear.','ForeToes.','HindToes.')))
        bpy.ops.object.select_all(action='DESELECT');o.select_set(True);rig.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
        bpy.context.view_layer.objects.active=o
        bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
        bpy.ops.object.vertex_group_smooth(group_select_mode='ALL',factor=.5,repeat=4)
        bpy.ops.object.vertex_group_normalize_all(lock_active=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        for b in arm.bones:b.use_deform=True
    mod=next((m for m in o.modifiers if m.type=='ARMATURE'),None)
    if mod is None:mod=o.modifiers.new('Bruno skeleton','ARMATURE')
    mod.object=rig

exec(compile(Path(__file__).with_name('circus_bear_motion.py').read_text(), 'circus_bear_motion.py', 'exec'))

# One skinned renderer with shared material slots, rather than a draw hierarchy per toe.
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=skin
bpy.ops.object.join();skin=bpy.context.object;skin.name='Bruno_SkinAndFur';parts=[skin]
rig.animation_data.action=bpy.data.actions['Bruno_Idle'];scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(ART/'Models/CN_Bruno.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},
    axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,bake_anim_simplify_factor=.1,apply_scale_options='FBX_SCALE_UNITS')

# Source includes an independent studio; exported FBX contains only bear and skeleton.
scene.world=bpy.data.worlds.new('StudioNight');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.026,.038,.06,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
def area(name,pos,power,color,size):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
area('WarmKey',(3,4,6),1000,(1,.78,.53),4)
area('CoolRim',(-3,-2,4),1450,(.34,.57,1),3)
area('FaceFill',(-1,5,2.8),420,(1,.91,.77),3)
bpy.ops.mesh.primitive_plane_add(size=200);floor=bpy.context.object;floor.name='StudioGround';floor.data.materials.append(material('StudioSlate',(.035,.044,.059)))
camd=bpy.data.cameras.new('BrunoPortrait');cam=bpy.data.objects.new('BrunoPortrait',camd);scene.collection.objects.link(cam)
cam.location=(5,7,3.0);cam.rotation_euler=(Vector((0,.15,1.14))-cam.location).to_track_quat('-Z','Y').to_euler();camd.type='ORTHO';camd.ortho_scale=5.5;scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=1400;scene.render.resolution_y=1050;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.render.filepath=str(REVIEW/'Bruno_Blender.png')
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Bruno.blend'))
bpy.ops.render.render(write_still=True)
(REVIEW/'bear-metrics.json').write_text(json.dumps({'mesh_vertices':sum(len(o.data.vertices) for o in parts),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in parts),'bones':len(bones),'actions':[a.name for a in bpy.data.actions]},indent=2))
print('BRUNO_EXPORT_COMPLETE',flush=True)
