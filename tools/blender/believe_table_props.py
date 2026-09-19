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
# Зерно шкатулки — своё и мелкое. Общее grain кладёт 29 полос по высоте,
# и на крышке они читались гофрокартоном, а не деревом.
cask=.005*np.sin(y*math.tau*71+1.3*np.sin(x*math.tau*3))+.0035*np.sin(y*math.tau*157+np.sin(x*math.tau*5))+.0035*fine
wood=rgba(np.stack([.225+cask,.122+cask*.55,.074+cask*.33],axis=-1))
wood[:,int(n*.375):int(n*.75),:3]=np.stack([.46+cask[:,int(n*.375):int(n*.75)],.255+cask[:,int(n*.375):int(n*.75)]*.65,.145+cask[:,int(n*.375):int(n*.75)]*.4],axis=-1)
wood[:,int(n*.75):int(n*.80),:3]=(.16,.065,.038)
wood[:,int(n*.80):,:3]=(.74,.51,.22)
texture('BTP_Casket',wood)
mask=np.zeros((n,n,4));mask[:,:,3]=.15
mask[:,int(n*.75):int(n*.80),3]=.15
mask[:,int(n*.80):,0]=.85;mask[:,int(n*.80):,3]=.80
texture('BTP_CasketMetalSmooth',mask)
# Felt: gently darkened edge, no hard printed ring; cross fibres visible at close range.
r=np.sqrt((x*2-1)**2+(y*2-1)**2)
edge=1-.45*np.clip((r-.60)/.40,0,1)**1.8
nap=.018*fine+.006*np.sin(x*math.tau*503)
felt=rgba(np.stack([( .112+nap)*edge,(.285+nap)*edge,(.158+nap*.7)*edge],axis=-1))
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
                elif region=='wall':u,v=.40+(u+.36)*.43,.12+(v+.36)*.85
                else:u,v=.025+(u+.36)*.43,.12+(v+.36)*.85
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
    if name in ('TableTop','TablePedestal'):
        uv=o.data.uv_layers.active
        for poly in o.data.polygons:
            if o.data.materials[poly.material_index]!=materials['Walnut']:continue
            for li in poly.loop_indices:
                u,v=uv.data[li].uv
                uv.data[li].uv=(.03+(u+1.45)/2.90*.94,.03+(v+1.45)/2.90*.94)
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
tabletop=module('TableTop')
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
pedestal=module('TablePedestal')
assert len(pedestal.data.materials)==1 and pedestal.data.materials[0]==materials['Walnut']
modules.remove(tabletop);modules.remove(pedestal)
table=bpy.data.objects.new('BTP_RoundTable',None);scene.collection.objects.link(table)
tabletop.parent=table;pedestal.parent=table;modules.append(table)

# Mahogany casket: hollow body, single material atlas; hinge line runs along Blender Y / Unity Z.
W=.432;D=.500;H=.135;T=.020
box((0,0,.011),(W,D,.022),'Casket',.007)
for s in (-1,1):
    box((s*(W-T)/2,0,H/2),(T,D,H),'Casket',.009,'wall')
    box((0,s*(D-T)/2,H/2),(W-2*T,T,H),'Casket',.009,'wall')
box((0,0,.027),(W-2*T-.002,D-2*T-.002,.006),'Casket',.002,'lining')
# Subtle lower bead; no individual marks or unique scuffs.
for sy in (-1,1):box((0,sy*(D/2+.001),.025),(W-.016,.004,.009),'Casket',.001)
for sx in (-1,1):box((sx*(W/2+.001),0,.025),(.004,D-.008,.009),'Casket',.001)
# A narrow rounded brass foot rail catches grazing light on all four sides.
for sy in (-1,1):box((0,sy*(D/2+.003),.024),(W+.012,.014,.014),'Casket',.006,'brass')
for sx in (-1,1):box((sx*(W/2+.003),0,.024),(.014,D+.012,.014),'Casket',.006,'brass')
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

# Cognac barrel chair. Origin = gameplay seat anchor on the floor; front faces Blender -Y (Unity +Z).
# The eight sitting poses put the seat contact 0.27-0.48 m above the floor, so no rigid chair fits
# everyone: Unity lifts BarrelChair to each sitter and stretches BarrelChairLegs down to the floor
# (BelieveChairFit). SEAT and LEG_TOP are mirrored in BelieveTablePropsBuilder.
# Clearances come from the skinned sitting poses of all eight, relative to the seat: calves hang in
# front of y=.09, heels reach back to y=.10 on the floor, hips stay inside |x|=.42 and elbows only
# widen .45 above the seat, the broadest back stays in front of y=.40.
SEAT,LEG_TOP=.38,.23
INNER_X,INNER_BACK,BACK_CENTRE,ROUNDNESS=.43,.42,.20,3.0
# BACK_RISE поднят с .56 до .70: верх спинки уходит с .94 на 1.08 м от пола,
# то есть выше плеч сидящего. На прежней высоте спинка кончалась ровно под
# лопатками, и в геройском кадре соперник читался сидящим на табурете,
# а бриф 14.3 просит «дуэль», а не «двое стоят у тумбы».
SHELL,ARM_FRONT,ARM_RISE,BACK_RISE=.10,.10,.22,.70
CUSHION_FRONT,DECK_FRONT,CUSHION=.085,.16,.09

def back_curve(phi,inset=0):
    # Superellipse from the right side (phi=0) around the back (pi/2) to the left side (pi).
    a=INNER_X-inset;b=INNER_BACK-BACK_CENTRE-inset;c,s=math.cos(phi),max(0,math.sin(phi))
    return a*math.copysign(abs(c)**(2/ROUNDNESS),c),BACK_CENTRE+b*s**(2/ROUNDNESS)

def seat_plan(front,inset,corner):
    # Convex CCW outline: the shell's back curve, straight sides and rounded front corners.
    a=INNER_X-inset;side=front+corner
    pts=[back_curve(math.pi*i/48,inset) for i in range(49)]
    pts+=[(-a,BACK_CENTRE-(BACK_CENTRE-side)*k/4) for k in range(1,5)]
    pts+=[(-a+corner+corner*math.cos(t),side+corner*math.sin(t)) for t in np.linspace(math.pi,1.5*math.pi,7)[1:]]
    pts+=[(a-corner+corner*math.cos(t),side+corner*math.sin(t)) for t in np.linspace(1.5*math.pi,2*math.pi,7)]
    pts+=[(a,side+(BACK_CENTRE-side)*k/4) for k in range(1,4)]
    return pts

def offset(poly,d):
    # Move every vertex of a convex CCW polygon inward by d along the averaged edge normals.
    out=[]
    for i in range(len(poly)):
        (x0,y0),(x1,y1),(x2,y2)=poly[i-1],poly[i],poly[(i+1)%len(poly)]
        l1=math.hypot(x1-x0,y1-y0) or 1;l2=math.hypot(x2-x1,y2-y1) or 1
        nx=(y1-y0)/l1+(y2-y1)/l2;ny=-(x1-x0)/l1-(x2-x1)/l2;l=math.hypot(nx,ny) or 1
        out.append((x1-d*nx/l,y1-d*ny/l))
    return out

def pillow(poly,z0,z1,r,crown,mat,name):
    # Upholstered slab: rounded lower and upper edges, softly crowned top.
    rings=[(r*(1-math.sin(t)),z0+r*(1-math.cos(t))) for t in np.linspace(0,math.pi/2,6)]
    rings+=[(r*(1-math.cos(t)),z1-r+r*math.sin(t)) for t in np.linspace(0,math.pi/2,6)]
    verts=[];faces=[];n=len(poly)
    for d,z in rings:verts+=[(x,y,z) for x,y in offset(poly,d)]
    top=offset(poly,r);cx=sum(p[0] for p in top)/n;cy=sum(p[1] for p in top)/n
    for f in (.7,.4):verts+=[(cx+(x-cx)*f,cy+(y-cy)*f,z1+crown*(1-f*f)) for x,y in top]
    count=len(verts)//n
    for j in range(count-1):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    verts.append((cx,cy,z1+crown))
    for i in range(n):faces.append(((count-1)*n+i,(count-1)*n+(i+1)%n,len(verts)-1))
    verts.append((cx,cy,z0))
    for i in range(n):faces.append(((i+1)%n,i,len(verts)-1))
    return mesh(name,verts,faces,mat)

def shell_top(w):return SEAT+ARM_RISE+(BACK_RISE-ARM_RISE)*w**1.6

def shell_section(h,inset=0):
    # Clockwise (offset, z) profile across the shell: inner face, rolled top, outer face, underside.
    r=SHELL/2
    prof=[(0,LEG_TOP+.012),(0,h-r)]+[(r-r*math.cos(t),h-r+.8*r*math.sin(t)) for t in np.linspace(0,math.pi,11)[1:-1]]
    prof+=[(SHELL,h-r),(SHELL,LEG_TOP+.03),(SHELL-.012,LEG_TOP+.004),(SHELL-.03,LEG_TOP),(.02,LEG_TOP)]
    return offset(prof[::-1],inset)[::-1] if inset else prof

# Barrel shell: inner face path along the right arm, around the back and along the left arm.
path=[(INNER_X,ARM_FRONT+(BACK_CENTRE-ARM_FRONT)*k/6,1,0,0) for k in range(6)]
for i in range(73):
    phi=math.pi*i/72;x,y=back_curve(phi);b=INNER_BACK-BACK_CENTRE
    gx=math.copysign(abs(x/INNER_X)**(ROUNDNESS-1),x)/INNER_X;gy=((y-BACK_CENTRE)/b)**(ROUNDNESS-1)/b
    l=math.hypot(gx,gy);path.append((x,y,gx/l,gy/l,math.sin(phi)))
path+=[(-INNER_X,BACK_CENTRE-(BACK_CENTRE-ARM_FRONT)*k/6,-1,0,0) for k in range(1,7)]
ARM_ROUND=.03
def shell_ring(p,inset=0,forward=0):
    x,y,nx,ny,w=p
    return [(x+nx*o,y+ny*o-forward,z) for o,z in shell_section(shell_top(w),inset)]
ends=np.linspace(math.pi/2,0,7)[:-1]
rings=[shell_ring(path[0],ARM_ROUND*(1-math.cos(t)),ARM_ROUND*math.sin(t)) for t in ends]
rings+=[shell_ring(p) for p in path]
rings+=[shell_ring(path[-1],ARM_ROUND*(1-math.cos(t)),ARM_ROUND*math.sin(t)) for t in ends[::-1]]
cs=len(rings[0]);verts=[v for ring in rings for v in ring];faces=[]
for j in range(len(rings)-1):
    for i in range(cs):faces.append((j*cs+i,j*cs+(i+1)%cs,(j+1)*cs+(i+1)%cs,(j+1)*cs+i))
# Closed arm fronts keep the shell manifold, so normals recalculate outward; flat so the n-gon does not smear.
faces+=[tuple(range(cs)),tuple(len(verts)-1-i for i in range(cs))]
o=mesh('Padded barrel shell',verts,faces,'Leather')
for poly in o.data.polygons[-2:]:poly.use_smooth=False
# Deck under the cushion; its front stays behind the calves of every sitter.
pillow(seat_plan(DECK_FRONT,-.02,.05),LEG_TOP,SEAT-CUSHION,.015,0,'Leather','Seat deck')
cushion=offset(seat_plan(CUSHION_FRONT,.004,.06),0)
pillow(cushion,SEAT-CUSHION,SEAT-.008,.03,.008,'Leather','Seat cushion')
# Piping: cushion top welt, both edges of the rolled top, stitched channels inside the back.
welt=offset(cushion,.03);tube([(x,y,SEAT-.008) for x,y in welt+welt[:1]],.0045,'Seam')
for o,drop in ((0,SHELL/2),(SHELL,SHELL/2)):
    tube([(x+nx*o,y+ny*o,shell_top(w)-drop) for x,y,nx,ny,w in path],.0045,'Seam')
for deg in (25,47,68,90,112,133,155):
    phi=math.radians(deg);x,y=back_curve(phi,.002);top=shell_top(math.sin(phi))
    tube([(x,y,z) for z in np.linspace(SEAT+.03,top-.075,12)],.0025,'Seam')
module('BarrelChair')

# Legs are a separate module: Unity scales it vertically so the tub always stands on the floor.
# Front legs stay behind every heel; all four splay slightly outward.
for x0,y0,dy in ((.42,.21,-.012),(.36,.44,.012)):
    for sx in (-1,1):
        verts=[];faces=[(0,1,2,3),(7,6,5,4)]
        for z,half,dx,ddy in ((0,.019,.018,dy),(LEG_TOP+.01,.029,0,0)):
            cx,cy=sx*(x0+dx),y0+ddy
            verts+=[(cx-half,cy-half,z),(cx+half,cy-half,z),(cx+half,cy+half,z),(cx-half,cy+half,z)]
        for i in range(4):faces.append((i,(i+1)%4,4+(i+1)%4,4+i))
        o=mesh('Tapered walnut leg',verts,faces,'Walnut',False)
        bpy.context.view_layer.objects.active=o;m=o.modifiers.new('Soft turned corners','BEVEL');m.width=.008;m.segments=3;bpy.ops.object.modifier_apply(modifier=m.name)
        m=o.modifiers.new('Leg normals','WEIGHTED_NORMAL');bpy.ops.object.modifier_apply(modifier=m.name)
module('BarrelChairLegs')

# Store only this kit scene; never save unrelated Blender scenes into the asset.
def export(o):
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    for child in o.children_recursive:child.select_set(True)
    # Location is pivot metadata; zero object position on export while retaining local hinged geometry.
    loc=o.location.copy();o.location=(0,0,0)
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/(o.name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True)
    o.location=loc
# Default: the table only. BTP_EXPORT_ALL exports every module; BTP_EXPORT_MODELS narrows either
# choice to the named modules, so re-exporting one prop does not rewrite the others.
selected=[m.name for m in modules] if globals().get('BTP_EXPORT_ALL',False) else [modules[0].name]
for o in modules:
    if o.name in selected and o.name in globals().get('BTP_EXPORT_MODELS',selected):export(o)
bpy.data.libraries.write(str(SOURCE/'BelieveTableProps.blend'),{scene},fake_user=True,compress=True,path_remap='RELATIVE')
print('Hero props:',[(o.name,sum(len(c.data.polygons) for c in [o]+list(o.children_recursive) if c.type=='MESH')) for o in modules])
bpy.context.window.scene=previous
