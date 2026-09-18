"""IGR-565 hero props, original deterministic Blender models. Metres, FBX -Z/Y.
Run through Blender MCP; BTP_EXPORT_ALL=True after checking first Unity import.
Casket body + hinged lid share one atlas/material and are instanced identically.
"""
import bpy
import bmesh
import math
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/BelieveTableProps'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/BelieveTableProps'
for p in (ART/'Models',ART/'Textures',SOURCE):p.mkdir(parents=True,exist_ok=True)
NAME='Believe_Table_Props'
previous=bpy.context.window.scene
if previous.name==NAME:
    previous=next(s for s in bpy.data.scenes if s.name!=NAME)
    bpy.context.window.scene=previous
old=bpy.data.scenes.get(NAME)
if old:
    for o in list(old.objects):bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(old)
scene=bpy.data.scenes.new(NAME);bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'
materials={}
for name,col,smooth,metal in [('Walnut',(.20,.105,.059),.64,0),('Felt',(.10,.22,.085),.04,0),('Casket',(.52,.25,.12),.5,0),('Leather',(.39,.19,.080),.32,0),('Seam',(.16,.065,.025),.25,0),('Brass',(.57,.35,.13),.7,.7)]:
    m=bpy.data.materials.get('BTP_'+name) or bpy.data.materials.new('BTP_'+name)
    m.diffuse_color=(*col,1);m.use_nodes=True
    n=m.node_tree.nodes.get('Principled BSDF');n.inputs['Base Color'].default_value=(*col,1)
    n.inputs['Roughness'].default_value=1-smooth;n.inputs['Metallic'].default_value=metal
    materials[name]=m

def texture(name,pixels):
    h,w,_=pixels.shape
    old=bpy.data.images.get(name)
    if old:bpy.data.images.remove(old)
    im=bpy.data.images.new(name,width=w,height=h,alpha=True)
    im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(np.clip(pixels,0,1).astype(np.float32).ravel())
    im.filepath_raw=str(ART/'Textures'/(name+'.png'));im.file_format='PNG';im.save()
    return im

def rgba(rgb):
    result=np.ones((*rgb.shape[:2],4));result[:,:,:3]=rgb;return result

# No random generators: identical local UVs produce exactly identical grain and hardware.
n=1024;y,x=np.mgrid[0:n,0:n]/n
fine=np.sin(x*math.tau*377+y*math.tau*137)*np.sin(y*math.tau*293-x*19)
grain=.014*np.sin(y*math.tau*29+1.7*np.sin(x*math.tau*2))+.006*np.sin(y*math.tau*93+np.sin(x*math.tau*3))+.004*fine
wood=rgba(np.stack([.28+grain,.16+grain*.55,.10+grain*.33],axis=-1))
wood[:,int(n*.75):int(n*.80),:3]=(.16,.065,.038)
wood[:,int(n*.80):,:3]=(.66,.43,.18)
texture('BTP_Casket',wood)
mask=np.zeros((n,n,4));mask[:,:,3]=.34+.025*fine
mask[:,int(n*.75):int(n*.80),3]=.15
mask[:,int(n*.80):,0]=.72;mask[:,int(n*.80):,3]=.72
texture('BTP_CasketMetalSmooth',mask)
# Felt: gently darkened edge, no hard printed ring; cross fibres visible at close range.
r=np.sqrt((x*2-1)**2+(y*2-1)**2)
edge=1-.45*np.clip((r-.60)/.40,0,1)**1.8
nap=.018*fine+.006*np.sin(x*math.tau*503)
felt=rgba(np.stack([( .18+nap)*edge,(.32+nap)*edge,(.13+nap*.7)*edge],axis=-1))
texture('BTP_Felt',felt)
# Fine original grain normal maps, weak amplitude so the table does not glitter.
def normal(name,h,amount):
    gy,gx=np.gradient(h);v=np.stack([-gx*amount,-gy*amount,np.ones_like(h)],axis=-1)
    v/=np.linalg.norm(v,axis=-1)[:,:,None];texture(name,rgba(v*.5+.5))
normal('BTP_FeltNormal',fine, .12)
walnut=rgba(np.stack([.17+grain*.22,.105+grain*.14,.072+grain*.10],axis=-1));texture('BTP_Walnut',walnut)
normal('BTP_WoodNormal',grain,.25)
pores=np.sin(x*math.tau*181+2*np.sin(y*math.tau*47))*np.sin(y*math.tau*157+2*np.sin(x*math.tau*61))
leather=rgba(np.stack([.36+pores*.015,.20+pores*.012,.115+pores*.008],axis=-1));texture('BTP_Leather',leather)
normal('BTP_LeatherNormal',pores,.16)
for mat,tex in [('Walnut','BTP_Walnut'),('Felt','BTP_Felt'),('Casket','BTP_Casket'),('Leather','BTP_Leather')]:
    m=materials[mat];nodes=m.node_tree.nodes
    for a in list(nodes):
        if a.type=='TEX_IMAGE':nodes.remove(a)
    a=nodes.new('ShaderNodeTexImage');a.image=bpy.data.images[tex]
    m.node_tree.links.new(a.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])

parts=[];modules=[]
def finish(o,mat,region=None):
    o.data.materials.clear();o.data.materials.append(materials[mat])
    # Each piece receives deterministic UV projection before joining. Casket is one atlas.
    uv=o.data.uv_layers.get('UVMap') or o.data.uv_layers.new(name='UVMap')
    for p in o.data.polygons:
        axis=max(range(3),key=lambda i:abs(p.normal[i]))
        for li in p.loop_indices:
            co=o.matrix_world@o.data.vertices[o.data.loops[li].vertex_index].co
            u,v=((co.y,co.z) if axis==0 else ((co.x,co.z) if axis==1 else (co.x,co.y)))
            if mat=='Felt':u,v=co.x/2.60+.5,co.y/2.60+.5
            elif mat=='Casket':
                if region=='brass':u,v=.9,.5
                elif region=='lining':u,v=.775,.5
                else:u,v=.06+(u+.36)*.9,.12+(v+.36)*.85
            uv.data[li].uv=(u,v)
    parts.append(o);return o

def mesh(name,verts,faces,mat,smooth=True,region=None):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    o=bpy.data.objects.new(name,data);scene.collection.objects.link(o)
    for p in data.polygons:p.use_smooth=smooth
    return finish(o,mat,region)

def box(pos,size,mat,bevel=.01,region=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos);o=bpy.context.object;o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        m=o.modifiers.new('Rounded crafted edge','BEVEL');m.width=min(bevel,min(size)*.45);m.segments=4
        bpy.ops.object.modifier_apply(modifier=m.name)
        for p in o.data.polygons:p.use_smooth=True
        m=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL');m.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=m.name)
    return finish(o,mat,region)

def lathe(profile,mat,segments=192,flutes=0):
    verts=[]
    for r,z in profile:
        for i in range(segments):
            a=i*math.tau/segments
            rr=r+(math.cos(a*16)*.006 if flutes and .18<z<.53 else 0)
            verts.append((rr*math.cos(a),rr*math.sin(a),z))
    faces=[]
    for j in range(len(profile)-1):
        for i in range(segments):
            a=j*segments+i;b=j*segments+(i+1)%segments
            faces.append((a,b,b+segments,a+segments))
    return mesh('Turned joinery',verts,faces,mat)

def tube(points,radius,mat,region=None):
    c=bpy.data.curves.new('Piping','CURVE');c.dimensions='3D';c.bevel_depth=radius;c.bevel_resolution=3;c.resolution_u=1
    s=c.splines.new('POLY');s.points.add(len(points)-1)
    for p,co in zip(s.points,points):p.co=(*co,1)
    o=bpy.data.objects.new('Piping',c);scene.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH');return finish(bpy.context.object,mat,region)

def sphere(pos,scale,mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=pos)
    o=bpy.context.object;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for p in o.data.polygons:p.use_smooth=True
    return finish(o,mat)

def module(name,pivot=(0,0,0)):
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=bpy.context.object;o.name='BTP_'+name
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    scene.cursor.location=pivot;bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    # Weld duplicate material slots to one submesh per material, including all casket hardware.
    slots=list(o.data.materials);unique=[]
    for m in slots:
        if m not in unique:unique.append(m)
    indices=[unique.index(slots[p.material_index]) for p in o.data.polygons]
    o.data.materials.clear()
    for m in unique:o.data.materials.append(m)
    for p,i in zip(o.data.polygons,indices):p.material_index=i
    if name.startswith('Casket'):
        # Unity's FBX handedness conversion reflects X. Keep the hinge on Unity +X.
        for v in o.data.vertices:v.co.x=-v.co.x
        o.location.x=-o.location.x
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    parts.clear();modules.append(o);return o

# Thick rolled lacquer rim, recessed green nap, fine inner lip and a turned apron.
lathe([(1.29,.596),(1.385,.596),(1.423,.613),(1.439,.639),(1.440,.695),(1.430,.720),(1.410,.738),(1.368,.746),(1.332,.738),(1.307,.727),(1.298,.719),(1.29,.709),(1.29,.596)],'Walnut')
lathe([(1.297,.716),(1.293,.720),(1.280,.721),(0,.721)],'Felt')
lathe(list(reversed([(1.28,.603),(1.305,.59),(1.302,.57),(1.282,.558),(1.245,.551),(1.23,.540),(1.228,.531),(1.21,.523),(1.195,.532),(1.195,.604)])),'Walnut')
# Carved baluster and shaped cross feet; broad enough to support a 2.88 m top.
lathe([(0,.08),(.24,.08),(.255,.095),(.253,.125),(.218,.15),(.182,.172),(.165,.21),(.146,.26),(.127,.305),(.126,.34),(.145,.36),(.181,.382),(.192,.411),(.187,.449),(.16,.471),(.133,.48),(.123,.496),(.129,.512),(.175,.526),(.218,.546),(.254,.59),(0,.60)],'Walnut',128,True)
for k in range(4):
    a=k*math.pi/2+math.pi/4;verts=[]
    profile=[(.13,.16,.20),(.28,.15,.165),(.46,.135,.12),(.68,.10,.067),(.88,.115,.050)]
    for r,w,z in profile:
        for yv,zv in [(-w,z-.035),(w,z-.035),(w,z+.045),(-w,z+.045)]:
            verts.append((r*math.cos(a)-yv*math.sin(a),r*math.sin(a)+yv*math.cos(a),zv))
    faces=[(0,3,2,1),(16,17,18,19)]
    for j in range(len(profile)-1):
        for i in range(4):faces.append((j*4+i,j*4+(i+1)%4,(j+1)*4+(i+1)%4,(j+1)*4+i))
    o=mesh('Carved cross foot',verts,faces,'Walnut',False)
    bpy.context.view_layer.objects.active=o;m=o.modifiers.new('Soft carved corners','BEVEL');m.width=.025;m.segments=4;bpy.ops.object.modifier_apply(modifier=m.name)
    m=o.modifiers.new('Foot normals','WEIGHTED_NORMAL');bpy.ops.object.modifier_apply(modifier=m.name)
module('RoundTable')

# Mahogany casket: hollow body, single material atlas; hinge line runs along Blender Y / Unity Z.
W=.432;D=.500;H=.135;T=.020
box((0,0,.011),(W,D,.022),'Casket',.007)
for s in (-1,1):
    box((s*(W-T)/2,0,H/2),(T,D,H),'Casket',.004)
    box((0,s*(D-T)/2,H/2),(W-2*T,T,H),'Casket',.004)
box((0,0,.027),(W-2*T-.002,D-2*T-.002,.006),'Casket',.002,'lining')
# Subtle lower bead; no individual marks or unique scuffs.
for sy in (-1,1):box((0,sy*(D/2+.001),.025),(W-.016,.004,.009),'Casket',.001)
for sx in (-1,1):box((sx*(W/2+.001),0,.025),(.004,D-.008,.009),'Casket',.001)
# Brass clasp opposite the hinge, two hinge leaves and barrel knuckles.
box((-W/2-.004,0,.109),(.011,.057,.050),'Casket',.006,'brass')
box((-W/2-.011,0,.109),(.01,.022,.029),'Casket',.004,'brass')
for yy in (-.133,.133):
    box((W/2+.001,yy,.120),(.007,.064,.037),'Casket',.002,'brass')
    tube([(W/2-.003,yy-.034,.137),(W/2-.003,yy+.034,.137)],.009,'Casket','brass')
module('CasketBody')
# Low lid bottom .139, top .192; geometry extends left from right-hand hinge pivot.
box((0,0,.1625),(W+.012,D+.012,.047),'Casket',.010)
box((0,0,.187),(W-.022,D-.022,.010),'Casket',.004)
box((-W/2-.004,0,.152),(.011,.054,.027),'Casket',.004,'brass')
for yy in (-.133,.133):box((W/2-.010,yy,.156),(.040,.059,.007),'Casket',.002,'brass')
module('CasketLid',(W/2,0,.137))

# Cognac barrel chair: rounded continuous back and arms, a padded seat and dark tapered feet.
# Front points toward Blender -Y, or Unity +Z after import.
for xx in (-.295,.295):
    for yy in (-.245,.255):
        o=box((xx,yy,.165),(.085,.085,.31),'Walnut',.015)
        o.rotation_euler[0]=-.10 if yy<0 else .10;o.rotation_euler[1]=.10 if xx>0 else -.10
sphere((0,.025,.397),(.414,.394,.085),'Leather')
sphere((0,-.018,.468),(.358,.337,.074),'Leather')
# Cross section has a rolled top and rounded upholstered base; smooth in both axes.
verts=[];faces=[];steps=80
section=[(.328,0),(.325,.08),(.322,.24),(.325,.50),(.332,.80),(.347,.97),(.375,1.025),(.414,1.012),(.445,.965),(.452,.81),(.449,.51),(.441,.23),(.422,.03),(.384,-.035),(.351,-.02)]
for i in range(steps+1):
    a=math.radians(-14+208*i/steps);height=.255+.235*max(0,math.sin(a))**.8
    for radius,z in section:verts.append((radius*math.cos(a),.040+radius*math.sin(a),.423+z*height))
cs=len(section)
for j in range(steps):
    for i in range(cs):faces.append((j*cs+i,j*cs+(i+1)%cs,(j+1)*cs+(i+1)%cs,(j+1)*cs+i))
faces.append(tuple(range(cs-1,-1,-1)));faces.append(tuple(steps*cs+i for i in range(cs)))
mesh('Padded curved barrel back',verts,faces,'Leather')
# Welt on top and seat cushion. Fine vertical stitched panels in the back upholstery.
tube([(.408*math.cos(math.radians(-14+208*i/100)),.04+.408*math.sin(math.radians(-14+208*i/100)),.423+(.255+.235*max(0,math.sin(math.radians(-14+208*i/100)))**.8)*1.024) for i in range(101)],.0045,'Seam')
tube([(.348*math.cos(i*math.tau/128),-.018+.323*math.sin(i*math.tau/128),.482) for i in range(129)],.004,'Seam')
for deg in (20,55,90,125,160):
    a=math.radians(deg);height=.255+.235*math.sin(a)**.8
    tube([((.450-.010*t)*math.cos(a),.04+(.450-.010*t)*math.sin(a),.46+t*(height-.07)) for t in np.linspace(0,1,18)],.0023,'Seam')
module('BarrelChair')

# Store only this kit scene; never save unrelated Blender scenes into the asset.
def export(o):
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    # Location is pivot metadata; zero object position on export while retaining local hinged geometry.
    loc=o.location.copy();o.location=(0,0,0)
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/(o.name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True)
    o.location=loc
export(modules[0])
if globals().get('BTP_EXPORT_ALL',False):
    for o in modules[1:]:export(o)
bpy.data.libraries.write(str(SOURCE/'BelieveTableProps.blend'),{scene},fake_user=True,compress=True,path_remap='RELATIVE')
print('Hero props:',[(o.name,len(o.data.polygons),len(o.data.materials)) for o in modules])
bpy.context.window.scene=previous
