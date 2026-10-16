"""Standalone reference study. Run in Blender through MCP; never writes Unity Assets.

The supplied front/three-quarter cartoon is interpreted as a fully volumetric
caveman sponge. All geometry/materials are authored here, without paid services.
"""
import bpy
import bmesh
import math
import random
from pathlib import Path
from mathutils import Vector
from mathutils.noise import noise_vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art-source/CharacterSandbox/CavemanSponge'
OUT.mkdir(parents=True, exist_ok=True)
SCENE = bpy.context.scene
assert 'CharacterSandbox' in bpy.data.filepath, 'Open a separate CharacterSandbox .blend before rebuilding.'
SCENE.name = 'CavemanSponge_Studio'
for ob in list(SCENE.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
for collection in list(bpy.data.collections):
    if collection.name.startswith(('01_CHARACTER','02_STUDIO','03_RIG_SHAPES')):bpy.data.collections.remove(collection)
for action in list(bpy.data.actions):bpy.data.actions.remove(action)
bpy.data.orphans_purge(do_recursive=True)
COL = bpy.data.collections.new('01_CHARACTER')
SCENE.collection.children.link(COL)
STUDIO = bpy.data.collections.new('02_STUDIO')
SCENE.collection.children.link(STUDIO)
PARTS = []

def move_to(ob, collection=COL):
    for c in list(ob.users_collection): c.objects.unlink(ob)
    collection.objects.link(ob)
    return ob

def material(name, color, roughness=.48, subsurface=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    p = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = roughness
    p.inputs['Subsurface Weight'].default_value = subsurface
    return mat

YELLOW = material('Sponge • warm golden yellow', (.89,.77,.018), .53, .045)
BODY = YELLOW.copy(); BODY.name = 'Sponge • sculpted pores + color'
PINK = material('Lips • warm dusty salmon', (.53,.245,.155), .48, .065)
INNER = material('Mouth • deep terracotta', (.075,.013,.008), .68)
TONGUE = material('Tongue • muted coral', (.31,.065,.045), .5, .04)
WHITE = material('Eyes • ivory', (.92,.97,1.0), .24)
BLUE = material('Iris • ocean blue', (.035,.37,.62), .3)
IRIS_EDGE = material('Iris • charcoal teal rim', (.008,.06,.075), .3)
BLACK = material('Pupils • ink', (.002,.006,.008), .2)
TOOTH = material('Tusks • warm enamel', (.92,.87,.67), .3)
BROWN = material('Loincloth • warm leather', (.22,.075,.026), .79)
STITCH = material('Leather seam', (.092,.026,.012), .84)
FRECKLE = material('Cheek freckles', (.49,.085,.035), .66)
CREASE = material('Sponge creases', (.29,.28,.006), .62)

def bump_noise(mat, scale, strength, distance):
    ns=mat.node_tree.nodes; ls=mat.node_tree.links
    p=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
    tex=ns.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value=scale
    tex.inputs['Detail'].default_value=3.0
    bump=ns.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=strength
    bump.inputs['Distance'].default_value=distance
    ls.new(tex.outputs['Fac'],bump.inputs['Height']);ls.new(bump.outputs['Normal'],p.inputs['Normal'])

for mat in [YELLOW,BODY]: bump_noise(mat, 95, .20, .012)
bump_noise(PINK,130,.12,.007)
bump_noise(BROWN,65,.28,.02)
ns=BODY.node_tree.nodes; ls=BODY.node_tree.links
attr=ns.new('ShaderNodeVertexColor');attr.layer_name='SpongeColor'
ls.new(attr.outputs['Color'],next(n for n in ns if n.type=='BSDF_PRINCIPLED').inputs['Base Color'])

def mesh(name, verts, faces, mat, tag='Head', collection=COL):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new(name,me);collection.objects.link(ob)
    ob.data.materials.append(mat)
    for p in me.polygons:p.use_smooth=True
    if collection==COL:
        ob['bind_region']=tag;PARTS.append(ob)
    return ob

def apply(ob, mod):
    bpy.context.view_layer.objects.active=ob
    bpy.ops.object.modifier_apply(modifier=mod.name)

def ellipsoid(name,center,scale,mat,tag='Head',segments=48,rings=32):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=rings,location=center)
    ob=bpy.context.object;ob.name=name;move_to(ob)
    ob.scale=scale
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    ob.data.materials.append(mat)
    for p in ob.data.polygons:p.use_smooth=True
    ob['bind_region']=tag;PARTS.append(ob)
    return ob

def catmull(points, count=8):
    points=[Vector(p) for p in points];res=[]
    for i in range(len(points)-1):
        p0=points[max(0,i-1)];p1=points[i];p2=points[i+1];p3=points[min(len(points)-1,i+2)]
        for j in range(count):
            t=j/count
            res.append(.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t))
    res.append(points[-1]);return res

def tube(name,points,radii,mat,tag='Head',sides=16,steps=8,flatten=1):
    path=catmull(points,steps);verts=[];faces=[];previous_a=None
    for i,p in enumerate(path):
        t=i/(len(path)-1);u=t*(len(radii)-1);k=min(int(u),len(radii)-2);v=u-k
        rad=radii[k]*(1-v)+radii[k+1]*v
        tangent=(path[min(i+1,len(path)-1)]-path[max(0,i-1)]).normalized()
        if previous_a is None:
            axis=min([Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1))],key=lambda ax:abs(tangent.dot(ax)))
            a=tangent.cross(axis).normalized()
        else:a=(previous_a-tangent*previous_a.dot(tangent)).normalized()
        b=tangent.cross(a).normalized();previous_a=a
        for j in range(sides):
            angle=2*math.pi*j/sides
            verts.append(p+rad*(math.cos(angle)*a+math.sin(angle)*b*flatten))
        if i:
            for j in range(sides):
                q=(j+1)%sides;faces.append(((i-1)*sides+j,(i-1)*sides+q,i*sides+q,i*sides+j))
    verts.extend([path[0],path[-1]])
    for j in range(sides):
        q=(j+1)%sides;faces.append((len(verts)-2,q,j));faces.append((len(verts)-1,(len(path)-1)*sides+j,(len(path)-1)*sides+q))
    return mesh(name,verts,faces,mat,tag)

def smoothstep(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)

# A welded, rounded cuboid with hand-placed large pores and smaller distributed ones.
HALF=Vector((1.10,.49,1.22));CENTER=Vector((0,0,2.12));BEVEL=.14
def base_box(p):
    p=Vector(p);q=Vector([max(-HALF[i]+BEVEL,min(HALF[i]-BEVEL,p[i])) for i in range(3)])
    n=(p-q).normalized();pos=q+n*BEVEL+CENTER
    wav=.022*math.sin(p.x*14+p.z*5)+.016*math.sin(p.z*17-p.y*7)
    wav+=noise_vector(pos*3.3)[0]*.023
    return pos+n*wav,n

pores=[]
# Front composition deliberately leaves room for facial features.
front=[(-.87,3.12,.115),(-.52,3.27,.075),(.12,3.23,.13),(.62,3.12,.11),(.87,2.92,.155),
       (-.94,2.61,.105),(.88,2.45,.09),(-.98,2.12,.085),(.91,1.86,.12),(.77,1.30,.16),
       (.30,1.10,.08),(-.38,1.14,.10),(-.88,1.29,.15),(-.98,1.04,.063),(.96,1.03,.075)]
for x,z,r in front:pores.append((Vector((x,-.49,z)),Vector((0,-1,0)),r,.068))
rng=random.Random(771)
for side in [-1,1]:
    for i in range(14):
        y=rng.uniform(-.31,.34);z=rng.uniform(1.02,3.22);r=rng.uniform(.06,.15)
        if side==1 and i==0:y=-.04;z=2.87;r=.24
        pores.append((Vector((side*1.1,y,z)),Vector((side,0,0)),r,.065))
for i in range(35):
    pores.append((Vector((rng.uniform(-.96,.96),.49,rng.uniform(1.02,3.20))),Vector((0,1,0)),rng.uniform(.05,.17),.06))
for i in range(10):
    pores.append((Vector((rng.uniform(-.96,.96),rng.uniform(-.36,.36),3.34)),Vector((0,0,1)),rng.uniform(.055,.12),.05))

verts=[];faces=[];colors=[];keymap={}
for axis,sign in [(0,-1),(0,1),(1,-1),(1,1),(2,-1),(2,1)]:
    others=[i for i in range(3) if i!=axis];nu=[92,42,102][others[0]];nv=[92,42,102][others[1]]
    grid={}
    for j in range(nv+1):
        for i in range(nu+1):
            p=[0,0,0];p[axis]=sign*HALF[axis];p[others[0]]=HALF[others[0]]*(2*i/nu-1);p[others[1]]=HALF[others[1]]*(2*j/nv-1)
            key=tuple(round(x,6) for x in p)
            if key in keymap:grid[i,j]=keymap[key];continue
            pos,n=base_box(p);depth=0;spot=0
            for c,pn,r,d in pores:
                if n.dot(pn)<.35:continue
                dist=(pos-c).length/r
                if dist<1.15:
                    depth-=d*max(0,1-dist*dist)**1.4
                    depth+=.007*math.exp(-((dist-.91)/.13)**2)
                    spot=max(spot,1-smoothstep(.60,1.04,dist))
            pos+=n*depth
            sidegreen=smoothstep(-.52,.12,pos.y)*.48+abs(n.x)*.15
            base=Vector((.88,.77,.018)).lerp(Vector((.32,.48,.045)),sidegreen)
            base=base.lerp(Vector((.18,.24,.012)),spot*.91)
            base*=1+noise_vector(pos*7)[0]*.055
            grid[i,j]=len(verts);keymap[key]=len(verts);verts.append(pos);colors.append((*base,1))
    for j in range(nv):
        for i in range(nu):faces.append((grid[i,j],grid[i+1,j],grid[i+1,j+1],grid[i,j+1]))
body=mesh('SpongeBody • rounded porous skin',verts,faces,BODY,'Body')
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(body.data);bm.free()
attribute=body.data.color_attributes.new(name='SpongeColor',type='FLOAT_COLOR',domain='POINT')
for i,c in enumerate(colors):attribute.data[i].color=c

# Eye whites, independent aimable iris assemblies, and sculpted drooping lids.
for side,x in [('L',.40),('R',-.40)]:
    z=2.72 if side=='R' else 2.68
    ellipsoid('EyeWhite.'+side,(x,-.70,z),(.405,.31,.43),WHITE,'Eye.'+side)
    # Colored surfaces sit on the front of the eyeball, not as flat floating discs.
    ellipsoid('IrisRim.'+side,(x-.025,-.996,z+.012),(.153,.027,.163),IRIS_EDGE,'Eye.'+side)
    ellipsoid('IrisBlue.'+side,(x-.025,-1.017,z+.012),(.132,.018,.142),BLUE,'Eye.'+side)
    ellipsoid('Pupil.'+side,(x-.035,-1.035,z+.017),(.062,.013,.078),BLACK,'Eye.'+side)
    ellipsoid('EyeGlint.'+side,(x-.071,-1.047,z+.071),(.020,.007,.027),WHITE,'Eye.'+side,24,16)
    # Cap sampled as latitude rings; Blink shape key extends the same cap downwards.
    vs=[];fs=[];rows=22;cols=64;theta_end=1.23
    for a in range(rows+1):
        theta=.018+(theta_end-.018)*a/rows
        for j in range(cols):
            phi=2*math.pi*j/cols
            vs.append((x+.423*math.sin(theta)*math.cos(phi),-.70+.373*math.sin(theta)*math.sin(phi),z+.45*math.cos(theta)))
            if a:
                k=a*cols+j;q=a*cols+(j+1)%cols;fs.append((k-cols,q-cols,q,k))
    lid=mesh('UpperLid.'+side,vs,fs,YELLOW,'Head')
    lid.shape_key_add(name='Basis');blink=lid.shape_key_add(name='Blink')
    for a in range(rows+1):
        theta=.018+(2.93-.018)*a/rows
        for j in range(cols):
            phi=2*math.pi*j/cols
            blink.data[a*cols+j].co=(x+.423*math.sin(theta)*math.cos(phi),-.70+.373*math.sin(theta)*math.sin(phi),z+.45*math.cos(theta))
    sol=lid.modifiers.new('Lid thickness','SOLIDIFY');sol.thickness=.012
    tube('Lower eye fold.'+side,[(x-.35,-.72,z-.20),(x-.25,-.85,z-.35),(x,-.89,z-.42),(x+.25,-.81,z-.35),(x+.36,-.67,z-.22)],[.009,.017,.014,.013,.005],CREASE,sides=10)

# Long rounded nose, thick brow and two characteristic horizontal forehead folds.
nose=tube('Long hooked nose',[(0,-.57,2.76),(0,-.98,2.71),(-.045,-1.32,2.75),(-.14,-1.67,2.86),(-.19,-1.76,2.93)],[.145,.125,.107,.085,.06],YELLOW,sides=24,steps=12)
tip=ellipsoid('Rounded nose tip',(-.19,-1.76,2.93),(.060,.060,.060),YELLOW,segments=32,rings=20)
bpy.ops.object.select_all(action='DESELECT');nose.select_set(True);tip.select_set(True);bpy.context.view_layer.objects.active=nose;bpy.ops.object.join()
rem=nose.modifiers.new('Continuous rounded nose','REMESH');rem.mode='VOXEL';rem.voxel_size=.009;rem.use_smooth_shade=True;apply(nose,rem)
smo=nose.modifiers.new('Soft nose tip','SMOOTH');smo.factor=.65;smo.iterations=4;apply(nose,smo)
tube('Heavy brow', [(-.84,-.65,2.97),(-.52,-.96,3.00),(-.12,-1.02,2.96),(.35,-.99,2.94),(.82,-.67,2.93)],[.045,.065,.065,.065,.04],YELLOW,sides=20,steps=12,flatten=.72)
for i in range(2):
    tube('Forehead expression crease '+str(i+1),[(-.40,-.515,3.18-i*.075),(-.22,-.535,3.15-i*.075),(.04,-.54,3.135-i*.075),(.36,-.53,3.14-i*.075)],[.005,.009,.009,.004],CREASE,sides=10)

# A large pouch-like lower lip. The mouth remains a genuine dark, recessed bowl.
ellipsoid('Lower lip and chin',(0,-.78,1.87),(.72,.44,.49),PINK,'Jaw',64,44)
mouthverts=[(0,-.89,2.24)];mouthfaces=[]
for ring in range(1,17):
    t=ring/16
    for j in range(96):
        angle=2*math.pi*j/96
        mouthverts.append((.79*t*math.cos(angle),-1.08+.21*(1-t*t),2.24+.237*t*math.sin(angle)))
        if ring==1:mouthfaces.append((0,1+j,1+(j+1)%96))
        else:
            a=1+(ring-2)*96+j;b=1+(ring-2)*96+(j+1)%96
            mouthfaces.append((a,b,b+96,a+96))
mesh('Mouth cavity',mouthverts,mouthfaces,INNER,'Mouth')
lippts=[]
for j in range(33):
    t=2*math.pi*j/32
    lippts.append((.80*math.cos(t),-1.082-.015*math.sin(t),2.24+.247*math.sin(t)))
tube('Mouth lip rim',lippts,[.055,.082,.095,.08,.055,.075,.085,.075,.055],PINK,'Lip',sides=16,steps=3)
ellipsoid('Tongue inside mouth',(0,-1.027,2.062),(.39,.072,.052),TONGUE,'Jaw')
for x in [-.43,.34]:
    tube('Upper tusk '+str(x),[(x,-1.084,2.43),(x-.025,-1.12,2.33),(x-.018,-1.135,2.22),(x+.015,-1.15,2.16)],[.068,.052,.030,.0015],TOOTH,'Head',sides=24,steps=8)
for s in [-1,1]:
    for i,(dx,dz,r) in enumerate([(0,0,.026),(.08,.04,.021),(.095,-.09,.024)]):
        x=s*(.80+dx);z=2.45+dz
        ellipsoid('Freckle %s %s'%(s,i),(x,-.508,z),(r,.012,r*1.06),FRECKLE,segments=24,rings=16)

# Long rubbery limbs; fuse the palm webs so fingers belong to a continuous hand.
JOINTS={};CHAINS={}
for side,s in [('L',1),('R',-1)]:
    mirror=lambda p:(s*p[0],p[1],p[2])
    shoulder=mirror((1.03,0,2.10));elbow=mirror((1.70,-.015,1.77));wrist=mirror((2.38,-.04,1.40))
    JOINTS[side]={'shoulder':shoulder,'elbow':elbow,'wrist':wrist}
    parts=[tube('ArmSkin.'+side,[shoulder,mirror((1.38,-.01,1.93)),elbow,mirror((2.04,-.027,1.59)),wrist],[.095,.075,.068,.070,.085],YELLOW,'Arm.'+side,sides=20,steps=8)]
    parts.append(ellipsoid('Palm.'+side,mirror((2.53,-.04,1.35)),(.21,.105,.155),YELLOW,'Arm.'+side))
    fingers={
        'Thumb':[(2.43,-.04,1.38),(2.26,-.07,1.28),(2.22,-.10,1.12)],
        'Index':[(2.59,-.04,1.42),(2.84,-.05,1.36),(3.00,-.09,1.20)],
        'Middle':[(2.63,-.04,1.34),(2.86,-.07,1.16),(2.88,-.12,1.00)],
        'Ring':[(2.58,-.04,1.27),(2.70,-.10,1.06),(2.67,-.15,.90)]}
    for finger,ps in fingers.items():
        ps=[mirror(p) for p in ps];JOINTS[side][finger]=ps
        parts.append(tube(finger+'Skin.'+side,ps,[.074,.064,.042],YELLOW,'Arm.'+side,sides=16,steps=10))
        parts.append(ellipsoid(finger+'Tip.'+side,ps[-1],(.043,.043,.043),YELLOW,'Arm.'+side,24,16))
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts:ob.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();arm=parts[0]
    rem=arm.modifiers.new('Continuous palm webs','REMESH');rem.mode='VOXEL';rem.voxel_size=.019;rem.use_smooth_shade=True;apply(arm,rem)
    smo=arm.modifiers.new('Gentle skin relaxation','SMOOTH');smo.factor=.65;smo.iterations=4;apply(arm,smo)
    # Short bowed legs and spread bare toes.
    hip=mirror((.73,.04,1.03));knee=mirror((1.02,-.025,.59));ankle=mirror((1.24,-.04,.22));toebase=mirror((1.47,-.22,.13))
    JOINTS[side].update(hip=hip,knee=knee,ankle=ankle,toebase=toebase)
    legparts=[tube('LegSkin.'+side,[hip,mirror((.88,.01,.81)),knee,mirror((1.13,-.03,.39)),ankle,mirror((1.40,-.12,.14))],[.10,.078,.073,.072,.075,.075],YELLOW,'Leg.'+side,sides=20,steps=8),ellipsoid('FootPad.'+side,mirror((1.43,-.16,.14)),(.27,.18,.095),YELLOW,'Leg.'+side)]
    for i in range(3):
        ps=[mirror((1.48,-.29+i*.135,.14)),mirror((1.72,-.32+i*.135,.115)),mirror((1.86-.035*i,-.33+i*.135,.102))]
        JOINTS[side]['Toe'+str(i+1)]=ps
        legparts.append(tube('ToeSkin%s.%s'%(i+1,side),ps,[.07,.052,.037],YELLOW,'Leg.'+side,sides=16,steps=7))
        legparts.append(ellipsoid('ToeTip%s.%s'%(i+1,side),ps[-1],(.038,.038,.038),YELLOW,'Leg.'+side,24,16))
    bpy.ops.object.select_all(action='DESELECT')
    for ob in legparts:ob.select_set(True)
    bpy.context.view_layer.objects.active=legparts[0];bpy.ops.object.join();leg=legparts[0]
    rem=leg.modifiers.new('Joined bare foot','REMESH');rem.mode='VOXEL';rem.voxel_size=.017;rem.use_smooth_shade=True;apply(leg,rem)
    smo=leg.modifiers.new('Soft sole','SMOOTH');smo.factor=.65;smo.iterations=4;apply(leg,smo)

# Simple primitive leather wrap matching the reference, with curved hem and seam.
vs=[];fs=[];nx=24;nz=30
for j in range(nz+1):
    t=j/nz
    for i in range(nx+1):
        u=i/nx;w=.30-.025*t;x=(u*2-1)*w
        z=1.00-.64*t+.025*math.cos(u*math.pi)*t
        y=-.525-.08*t+.022*math.sin(u*math.pi*2)*t
        vs.append((x,y,z))
        if i and j:
            k=j*(nx+1)+i;fs.append((k-nx-2,k-nx-1,k,k-1))
cloth=mesh('Leather loincloth • front',vs,fs,BROWN,'Cloth')
sol=cloth.modifiers.new('Leather thickness','SOLIDIFY');sol.thickness=.028
bev=cloth.modifiers.new('Soft leather edges','BEVEL');bev.width=.016;bev.segments=3
tube('Leather waist cord',[(-1.07,-.29,1.03),(-.90,-.50,1.02),(0,-.54,1.02),(.90,-.50,1.02),(1.07,-.29,1.03)],[.014]*5,STITCH,'Hips',sides=10)
for x in [-.27,.27]:
    tube('Loincloth edge '+str(x),[(x,-.544,.96),(x*.96,-.58,.68),(x*.90,-.607,.38)],[.006,.008,.006],STITCH,'Cloth',sides=8)

# Neutral studio, soft contact shadows and warm rim light.
floor=material('Studio • desaturated slate',(.055,.085,.095),.85)
bpy.ops.mesh.primitive_plane_add(size=200)
ob=bpy.context.object;ob.name='Studio Ground';move_to(ob,STUDIO);ob.location.z=.045;ob.data.materials.append(floor)
world=bpy.data.worlds.new('Studio World');world.use_nodes=True;SCENE.world=world
bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.14,.20,.25,1);bg.inputs['Strength'].default_value=.42
def point_at(ob,point):ob.rotation_euler=(Vector(point)-ob.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power,size,col in [('Key softbox',(-3.8,-4.7,6.7),650,4.5,(1,.95,.88)),('Cool fill',(4,-2.0,4),450,3.5,(.78,.89,1)),('Gold rim',(1.2,3,5.5),800,3,(1,.88,.62))]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=col
    ob=bpy.data.objects.new(name,d);STUDIO.objects.link(ob);ob.location=loc;point_at(ob,(0,0,1.8))
camdata=bpy.data.cameras.new('Portrait Camera');cam=bpy.data.objects.new('Portrait Camera',camdata);STUDIO.objects.link(cam)
cam.location=(5.0,-9.0,4.2);point_at(cam,(0,-.14,1.74));camdata.type='ORTHO';camdata.ortho_scale=7.2;SCENE.camera=cam
try:SCENE.render.engine='CYCLES'
except TypeError:pass
SCENE.cycles.samples=40;SCENE.cycles.use_denoising=True
SCENE.render.resolution_x=1500;SCENE.render.resolution_y=1300;SCENE.render.resolution_percentage=100
SCENE.render.image_settings.file_format='PNG'
try:SCENE.view_settings.view_transform='Khronos PBR Neutral'
except TypeError:SCENE.view_settings.view_transform='AgX'
SCENE.view_settings.exposure=-.4
SCENE.render.film_transparent=False
SCENE.render.fps=30;SCENE.frame_start=1;SCENE.frame_end=120
SCENE['Reference']='User supplied caveman sponge cartoon, 2026-10-14. Back inferred; sandbox only.'
SCENE['NoUnityIntegration']=True
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CavemanSponge.blend'))
print('Built sandbox geometry:',len(COL.objects),'objects;',sum(len(o.data.vertices) for o in COL.objects if o.type=='MESH'),'vertices')
