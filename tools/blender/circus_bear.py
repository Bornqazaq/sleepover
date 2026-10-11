"""Original circus brown bear. Blender 5.2, no external models or animation clips.
Run: blender --background --python tools/blender/circus_bear.py
Metres, +Y forward, Z up; source rig and six editable actions are preserved.
"""
import bpy, math, random, json, bisect
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
    p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'); p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=rough
    return m
fur=material('CN_UmberFur',(.185,.102,.057))
tip=material('CN_FurTips',(.245,.151,.086))
muzzle=material('CN_Muzzle',(.22,.14,.085),.9)
black=material('CN_Nose',(.018,.011,.008),.48)
darkfur=material('CN_DarkFur',(.054,.028,.012),.93)
eye=material('CN_AmberEyes',(.025,.010,.004),.34)
claw=material('CN_Claws',(.23,.185,.133),.55)
mouth=material('CN_Mouth',(.083,.018,.014),.62)
import numpy as np
N=1024
u,v=np.meshgrid(np.arange(N)/N,np.arange(N)/N)
noise=np.random.default_rng(88)
# Several scales of aligned fibres. These stay neutral so the existing URP
# materials set coat colour; the same map supplies the Blender and Unity grain.
hairs=np.full((N,N),.80)
for frequency,amplitude in ((13,.035),(37,.030),(79,.030),(131,.025),(239,.020),(431,.012)):
    phase=noise.uniform(0,math.tau)
    strand=np.sin(u*math.tau*frequency+np.sin(v*math.tau*3+phase)*2.8+phase)
    hairs+=amplitude*strand*(.72+.28*np.cos(v*math.tau*11+phase))
hairs+=noise.random((N,N))*.035
hairs=np.clip(hairs,.53,.98)
rgba=np.ones((N,N,4),dtype=np.float32);rgba[:,:,:3]=hairs[:,:,None]
im=bpy.data.images.new('CN_FurGrain',width=N,height=N);im.pixels.foreach_set(rgba.ravel())
(ART/'Textures').mkdir(exist_ok=True);im.filepath_raw=str(ART/'Textures/CN_FurGrain.png');im.file_format='PNG';im.save()
dx=(np.roll(hairs,1,1)-np.roll(hairs,-1,1))*2.2;dy=(np.roll(hairs,1,0)-np.roll(hairs,-1,0))*2.2
norm=np.stack((dx,dy,np.ones_like(dx)),axis=-1);norm/=np.linalg.norm(norm,axis=-1)[:,:,None]
rgba[:,:,:3]=norm*.5+.5
normalmap=bpy.data.images.new('CN_FurNormal',width=N,height=N);normalmap.colorspace_settings.name='Non-Color';normalmap.pixels.foreach_set(rgba.ravel())
normalmap.filepath_raw=str(ART/'Textures/CN_FurNormal.png');normalmap.file_format='PNG';normalmap.save()
for m in (fur,tip,muzzle,darkfur):
    tree=m.node_tree;tex=tree.nodes.new('ShaderNodeTexImage');tex.image=im
    mix=tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[1].default_value=m.diffuse_color
    tree.links.new(tex.outputs['Color'],mix.inputs[2]);tree.links.new(mix.outputs[0],next(n for n in tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'])
    bump=tree.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.30;bump.inputs['Distance'].default_value=.009
    tree.links.new(tex.outputs['Color'],bump.inputs['Height']);tree.links.new(bump.outputs['Normal'],next(n for n in tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Normal'])
parts=[]

def oval(name, pos, scale, mat=fur, bone=None, segments=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=rings,location=pos)
    o=bpy.context.object; o.name=name; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(mat)
    for p in o.data.polygons: p.use_smooth=True
    if bone: o['bone']=bone
    parts.append(o); return o

# Brown-bear proportions: high shoulder girdle, lower rump, concave facial
# profile, weight carried through long forearms and broad plantigrade feet.
# Shape reference: https://www.nps.gov/articles/bear-identification.htm
oval('Ribcage',(0,-.16,1.24),(.60,1.04,.60))
oval('Haunches',(0,-.95,1.10),(.59,.60,.58))
oval('ShoulderHump',(0,.24,1.54),(.62,.70,.60))
oval('Withers',(0,.42,1.72),(.40,.46,.37))
oval('Neck',(0,.96,1.44),(.43,.62,.385))
oval('Throat',(0,1.12,1.29),(.34,.41,.29))
oval('Skull',(0,1.40,1.53),(.345,.45,.285))
oval('Brow',(0,1.545,1.680),(.30,.29,.125))
oval('NasalBridge',(0,1.76,1.48),(.215,.31,.16))
oval('SnoutBase',(0,1.91,1.405),(.225,.255,.165))
for sign in (-1,1):
    oval('Cheek',(sign*.218,1.41,1.445),(.12,.27,.195))
    oval('EarRoot',(sign*.266,1.24,1.722),(.105,.145,.10))
for side,x in [('L',.51),('R',-.51)]:
    oval('ForeShoulder'+side,(x,.32,1.27),(.27,.36,.62))
    oval('ForeElbow'+side,(x,.24,.88),(.205,.25,.35))
    oval('ForeShin'+side,(x,.38,.63),(.192,.25,.44))
    oval('ForeWrist'+side,(x,.60,.295),(.205,.26,.235))
    oval('ForePaw'+side,(x,.80,.185),(.275,.36,.18))
    oval('HindThigh'+side,(x,-.88,.97),(.30,.38,.54))
    oval('HindShin'+side,(x,-.92,.46),(.205,.29,.36))
    oval('HindPaw'+side,(x,-.89,.175),(.265,.37,.17))
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join(); skin=bpy.context.object; skin.name='Bruno_ContinuousSkin'
remesh=skin.modifiers.new('Anatomy union','REMESH'); remesh.mode='VOXEL'; remesh.voxel_size=.028; remesh.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=remesh.name)
smooth=skin.modifiers.new('Sculpt smoothing','SMOOTH'); smooth.factor=.8; smooth.iterations=4; bpy.ops.object.modifier_apply(modifier=smooth.name)
# Inset sockets belong to the continuous face, rather than stacked eyelid beads.
for sign in (-1,1):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=(sign*.245,1.688,1.644))
    cutter=bpy.context.object;cutter.scale=(.035,.034,.024)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bpy.context.view_layer.objects.active=skin
    socket=skin.modifiers.new('Inset eye socket','BOOLEAN');socket.operation='DIFFERENCE';socket.object=cutter
    bpy.ops.object.modifier_apply(modifier=socket.name);bpy.data.objects.remove(cutter,do_unlink=True)
    bpy.context.view_layer.objects.active=skin
dec=skin.modifiers.new('Game topology','DECIMATE'); dec.ratio=.43; bpy.ops.object.modifier_apply(modifier=dec.name)
bpy.ops.object.select_all(action='DESELECT');skin.select_set(True);bpy.context.view_layer.objects.active=skin
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
parts=[skin]

def tube(name, points, radii, mat, bone_name, sides=8):
    """A tapered curved solid, used for lips, eyelids and the hooked claws."""
    vertices=[];polygons=[]
    for i,point in enumerate(points):
        point=Vector(point)
        tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        tangent.normalize()
        normal=tangent.cross(Vector((1,0,0)))
        if normal.length<.01:normal=tangent.cross(Vector((0,0,1)))
        normal.normalize();across=tangent.cross(normal).normalized()
        for j in range(sides):
            a=j/sides*math.tau
            vertices.append(point+(normal*math.cos(a)+across*math.sin(a))*radii[i])
        if i:
            for j in range(sides):
                a=(i-1)*sides+j;b=(i-1)*sides+(j+1)%sides;c=i*sides+(j+1)%sides;d=i*sides+j
                polygons.append((a,b,c,d))
    polygons.append(tuple(reversed(range(sides))))
    polygons.append(tuple((len(points)-1)*sides+j for j in range(sides)))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],polygons);mesh.materials.append(mat)
    ob=bpy.data.objects.new(name,mesh);scene.collection.objects.link(ob);ob['bone']=bone_name;parts.append(ob)
    for polygon in mesh.polygons:polygon.use_smooth=True
    return ob

# Small asymmetrical cups sit inside the mane. Each ear has a real bowl and
# thickness, with its opening tilted outwards; no flat circular insert.
coat_sources=[skin]
for side,sign in [('L',1),('R',-1)]:
    center=Vector((sign*.278,1.235,1.804))
    rotation=Quaternion(Vector((0,0,1)),-sign*math.radians(32))@Quaternion(Vector((1,0,0)),math.radians(24))
    vertices=[];polygons=[];indices=[];segments=28
    # The first rings form the rear wall, then turn over the thick furred lip
    # and descend into the recessed front. The small centre closes the cup.
    for radius,depth in ((.04,-.040),(.74,-.038),(1,-.002),(.85,.020),(.54,-.004),(.025,-.027)):
        for i in range(segments):
            a=i/segments*math.tau
            taper=1-.13*max(0,math.sin(a))
            x=math.cos(a)*.090*radius*taper
            z=math.sin(a)*.103*radius
            point=Vector((x,depth+.007*math.sin(a*2),z))
            vertices.append(center+rotation@point)
        ring=len(vertices)//segments-1
        if ring:
            for i in range(segments):
                a=(ring-1)*segments+i;b=(ring-1)*segments+(i+1)%segments
                c=ring*segments+(i+1)%segments;d=ring*segments+i
                polygons.append((a,b,c,d));indices.append(1 if ring>=4 else 0)
    polygons.append(tuple(reversed(range(segments))));indices.append(0)
    polygons.append(tuple(5*segments+i for i in range(segments)));indices.append(1)
    mesh=bpy.data.meshes.new('EarCup'+side);mesh.from_pydata(vertices,[],[tuple(reversed(face)) for face in polygons])
    mesh.materials.append(fur);mesh.materials.append(darkfur)
    ear=bpy.data.objects.new('Ear'+side,mesh);scene.collection.objects.link(ear);ear['bone']='Ear.'+side
    parts.append(ear);coat_sources.append(ear)
    for polygon,index in zip(mesh.polygons,indices):polygon.material_index=index;polygon.use_smooth=True
    # Eye opening lives in the continuous brow mass, 10 mm behind its surface.
    # Almost black, wet eyes read as a gaze, not exposed gold beads on stalks.
    ex=sign*.245;ey=1.675;ez=1.644
    oval('Eye'+side,(ex,ey,ez),(.022,.010,.014),eye,'Head',24,16)
    oval('Pupil'+side,(ex,ey+.008,ez),(.015,.003,.0125),black,'Head',20,12)
    oval('EyeGlint'+side,(ex-sign*.005,ey+.011,ez+.004),(.0006,.0005,.0006),black,'Head',12,8)
    oval('UpperLid'+side,(ex,1.679,1.662),(.028,.012,.007),fur,'Head',20,12)
    oval('LowerLid'+side,(ex,1.677,1.630),(.024,.007,.003),darkfur,'Head',20,12)
    # The established blink moves down 33 mm and triples the strip height.
    # Place the lid pivot above this smaller opening so it closes exactly on it.
    oval('BlinkLid'+side,(ex,1.686,1.677),(.026,.008,.0045),fur,'Lid.'+side,20,12)

# Muzzle fur shares the continuous skin. An irregular transition and short coat
# replace the former hard polygonal colour mask on the nose bridge.
skin.data.materials.clear()
for m in (fur,muzzle,darkfur):skin.data.materials.append(m)
for poly in skin.data.polygons:
    c=poly.center
    blend=max(0,min(1,(c.y-1.82-abs(c.x)*.55)/.27))
    if 1.26<c.z<1.61 and rng.random()<blend*blend:poly.material_index=1
    elif c.z<.38 and abs(c.x)>.3:poly.material_index=2
nose=oval('Nose',(0,2.119,1.467),(.146,.080,.072),black,'Head',32,20)
# Flatten the leathery front of the nose; nostrils are holes rather than beads.
for vertex in nose.data.vertices:
    if vertex.co.y>.039:vertex.co.y=.039+(vertex.co.y-.039)*.45
for side,sign in [('L',1),('R',-1)]:
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=12,location=(sign*.094,2.174,1.466))
    cutter=bpy.context.object;cutter.scale=(.025,.030,.015);cutter.rotation_euler.y=sign*.15
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bpy.context.view_layer.objects.active=nose
    hole=nose.modifiers.new('Nostril recess','BOOLEAN');hole.operation='DIFFERENCE';hole.object=cutter
    bpy.ops.object.modifier_apply(modifier=hole.name);bpy.data.objects.remove(cutter,do_unlink=True)
    oval('Nostril'+side,(sign*.094,2.153,1.466),(.018,.008,.012),darkfur,'Head',16,10)
tube('Philtrum',[(0,2.161,1.445),(0,2.155,1.408),(0,2.134,1.366)],[.010,.008,.005],black,'Head')
lip=[]
for i in range(21):
    a=i/20*math.pi
    lip.append((.202*math.cos(a),1.876+.232*math.sin(a),1.306+.015*abs(math.cos(a))))
tube('MouthLine',lip,[.0085]*len(lip),black,'Head')
oval('MouthInterior',(0,1.78,1.283),(.17,.24,.06),mouth,'Head')
jaw=oval('LowerJaw',(0,1.827,1.266),(.205,.30,.066),muzzle,'Jaw');coat_sources.append(jaw)
oval('Tongue',(0,1.945,1.305),(.10,.135,.012),mouth,'Jaw')
for side,sign in [('L',1),('R',-1)]:
    tube('Canine'+side,[(sign*.143,2.0,1.34),(sign*.141,2.018,1.307),(sign*.130,2.016,1.26)],
         [.019,.014,.0015],claw,'Head')
tail=oval('Tail',(0,-1.49,1.24),(.11,.175,.11),fur,'Tail');coat_sources.append(tail)
for side,sign in [('L',1),('R',-1)]:
    for limb,y in [('Fore',.79),('Hind',-.89)]:
        for i in range(5):
            x=sign*.51+(i-2)*.092
            edge=abs(i-2)/2
            toe=oval(limb+'Toe'+side+str(i),(x,y+.253-edge*.026,.145),(.075,.145,.095),darkfur,limb+'Toes.'+side,16,10)
            coat_sources.append(toe)
            length=.158 if limb=='Fore' else .108
            start=y+.345-edge*.026
            points=[(x,start,.142),(x,start+length*.34,.145),(x,start+length*.72,.112),(x,start+length,.061)]
            tube(limb+'Claw'+side+str(i),points,[.025,.021,.013,.0015],claw,limb+'Toes.'+side)

# Dense, narrow, curved fur tufts cover the entire anatomy. Triangle-area sampling
# gives equal coverage to shins, throat and face; cylindrical rays missed them.
# The coat uses opaque geometry and existing URP Lit materials, no alpha sorting.
from mathutils.bvhtree import BVHTree
skin.data.calc_loop_triangles()
skin_triangles=[tuple(triangle.vertices) for triangle in skin.data.loop_triangles]
skin_bvh=BVHTree.FromPolygons([vertex.co for vertex in skin.data.vertices],skin_triangles,all_triangles=True)
triangles=[];areas=[];total_area=0
for source in coat_sources:
    source.data.calc_loop_triangles()
    for triangle in source.data.loop_triangles:
        a,b,c=[source.matrix_world@source.data.vertices[i].co for i in triangle.vertices]
        n=(b-a).cross(c-a)
        area=n.length*.5
        if area<1e-8:continue
        n.normalize();total_area+=area;areas.append(total_area)
        triangles.append((a,b,c,n,source.get('bone'),source.name))
verts=[];faces=[];uvs=[];fur_bones={};fur_materials=[]
for k in range(19000):
    a,b,c,n,rigid,source_name=triangles[bisect.bisect_left(areas,rng.random()*total_area)]
    r=math.sqrt(rng.random());t=rng.random();p=a*(1-r)+b*(r*(1-t))+c*(r*t)
    if p.z<.065 or (p.z<.12 and n.z<-.2):continue
    if rigid:
        nearest,normal,_,_=skin_bvh.find_nearest(p)
        if nearest is not None and (p-nearest).dot(normal)<-.002:continue
    head=p.y>1.10 and p.z>1.03
    muzzle_area=head and p.y>1.76+abs(p.x)*.56 and p.z<1.61
    # Cheek and muzzle fibres radiate backwards from the nose, legs flow down;
    # long mane/shoulder hair lies diagonally down the flank, not up like spines.
    if 'Ear' in source_name:
        flow=Vector((p.x*.25,-.10,.85));length=rng.uniform(.017,.038);width=rng.uniform(.0018,.004)
    elif head:
        flow=Vector((p.x*.7,-1,-.36))
        length=rng.uniform(.010,.023) if muzzle_area else rng.uniform(.030,.066)
        width=rng.uniform(.0018,.004) if muzzle_area else rng.uniform(.003,.007)
    elif p.z<1.03 and abs(p.x)>.28:
        flow=Vector((n.x*.15,-.12,-1));length=rng.uniform(.033,.083);width=rng.uniform(.003,.007)
    else:
        flow=Vector((n.x*.25,-.62,-.64));length=rng.uniform(.064,.14);width=rng.uniform(.004,.009)
    flow-=n*flow.dot(n)
    if flow.length<.01:flow=n.cross(Vector((1,0,0)))
    flow.normalize();flow=Quaternion(n,rng.uniform(-.20,.20))@flow
    across=n.cross(flow).normalized()
    # Slight variation of lengths prevents rows; low lift makes overlapping
    # satin-matte locks instead of the old stiff triangular nails.
    lift=length*rng.uniform(.10,.20)
    start=len(verts)
    origin_u=rng.random()*.88;origin_v=rng.random()*.70
    for along,spread,height in ((0,.72,-.002),(.38,1,.35),(.76,.54,.8)):
        for sign in (-1,1):
            point=p+flow*(length*along)+across*(width*spread*sign)+n*(lift*height if height>0 else height)
            if rigid is None:
                surface,surface_normal,_,_=skin_bvh.find_nearest(point)
                # Stay on the same skin patch; never jump a tuft across a leg gap.
                if surface is not None and (surface-point).length<length*.5:
                    point=surface+surface_normal*(max(.001,lift*height))
            verts.append(tuple(point));uvs.append((origin_u+(sign+1)*.018,origin_v+along*.22))
            if rigid:fur_bones[len(verts)-1]=rigid
    point=p+flow*length+n*lift
    if rigid is None:
        surface,surface_normal,_,_=skin_bvh.find_nearest(point)
        if surface is not None and (surface-point).length<length*.5:point=surface+surface_normal*lift
    verts.append(tuple(point));uvs.append((origin_u+.018,origin_v+.22))
    if rigid:fur_bones[len(verts)-1]=rigid
    faces.extend([(start,start+2,start+1),(start+1,start+2,start+3),
                  (start+2,start+4,start+3),(start+3,start+4,start+5),(start+4,start+6,start+5)])
    if muzzle_area:mat_index=2 if rng.random()<max(.10,min(.92,(p.y-1.66)*2.2)) else 0
    elif p.z<.5:mat_index=3 if rng.random()<.6 else 0
    elif head:mat_index=1 if rng.random()<.10 else 0
    else:mat_index=1 if rng.random()<(.27 if p.z>1.5 else .13) else 0
    fur_materials.extend([mat_index]*5)
me=bpy.data.meshes.new('SculptedFur');me.from_pydata(verts,[],faces)
for coat_material in (fur,tip,muzzle,darkfur):me.materials.append(coat_material)
uv_layer=me.uv_layers.new(name='CoatFlow')
for polygon in me.polygons:
    polygon.material_index=fur_materials[polygon.index];polygon.use_smooth=True
    for loop_index in polygon.loop_indices:uv_layer.data[loop_index].uv=uvs[me.loops[loop_index].vertex_index]
locks=bpy.data.objects.new('Bruno_FurLocks',me);scene.collection.objects.link(locks);parts.append(locks)
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
    bone('Lid.'+side,(s*.245,1.686,1.677),(s*.245,1.686,1.728),'Head')
    bone('Ear.'+side,(s*.278,1.235,1.724),(s*.278,1.235,1.93),'Head')
bpy.ops.object.mode_set(mode='OBJECT')

def segdist(p,a,b):
    v=b-a;t=max(0,min(1,(p-a).dot(v)/v.length_squared));return (p-a-v*t).length
for o in parts:
    o.parent=rig
    for name in bones:o.vertex_groups.new(name=name)
    for v in o.data.vertices:
        p=o.matrix_local@v.co
        if 'bone' in o:weights=[(o['bone'],1)]
        elif o==locks:
            if v.index in fur_bones:weights=[(fur_bones[v.index],1)]
            else:
                near,normal,triangle_index,distance=skin_bvh.find_nearest(p)
                from mathutils.geometry import barycentric_transform
                ids=skin_triangles[triangle_index]
                a,b,c=[skin.data.vertices[i].co for i in ids]
                bary=barycentric_transform(near,a,b,c,Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
                weights_by_bone={}
                for index,amount in zip(ids,bary):
                    for group in skin.data.vertices[index].groups:
                        name=skin.vertex_groups[group.group].name
                        weights_by_bone[name]=weights_by_bone.get(name,0)+group.weight*max(0,amount)
                total=sum(weights_by_bone.values())
                weights=[(name,weight/total) for name,weight in weights_by_bone.items()] if total>0 else [('Chest',1)]
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
        for limb in ('Fore','Hind'):
            for side in ('L','R'):
                group_name=limb+'Toes.'+side
                if o.vertex_groups.get(group_name) is None:o.vertex_groups.new(name=group_name)
        # Heat weights exclude the tiny toe bones, then a smooth anatomical
        # gradient adds their share. The joined paw pad now bends with the toes
        # at push-off instead of leaving only the detached nails to articulate.
        for vertex in o.data.vertices:
            p=vertex.co
            if p.z>.36 or abs(p.x)<.22:continue
            limb='Fore' if p.y>.1 else 'Hind';side='L' if p.x>0 else 'R'
            start=.88 if limb=='Fore' else -.81
            amount=max(0,min(1,(p.y-start)/.19));amount=amount*amount*(3-2*amount)*.94
            if amount<=0:continue
            for group in list(vertex.groups):
                o.vertex_groups[group.group].add([vertex.index],group.weight*(1-amount),'REPLACE')
            o.vertex_groups[limb+'Toes.'+side].add([vertex.index],amount,'REPLACE')
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
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.026,.038,.06,1)
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[1].default_value=.4
def area(name,pos,power,color,size):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
area('WarmKey',(3,4,6),1000,(1,.97,.92),4)
area('CoolRim',(-3,-2,4),950,(.78,.86,1),3)
area('FaceFill',(-1,5,2.8),320,(1,1,1),3)
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
