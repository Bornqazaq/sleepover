"""IGR-565 original finishing props. Live Blender MCP; metres, front -Y.
Set BCD_EXPORT_ALL after checking the first roulette import in Unity.
Original imagegen paintings live beside the procedural wood / cloth textures.
Accepted kit scenes, meshes and materials are never modified.
"""
import bpy
import math
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/BelieveClubDetails'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/BelieveClubDetails'
for p in (ART/'Models',ART/'Textures',SOURCE):p.mkdir(parents=True,exist_ok=True)
NAME='Believe_Club_Details'
if bpy.context.scene.name==NAME:bpy.context.window.scene=next(s for s in bpy.data.scenes if s.name!=NAME)
old=bpy.data.scenes.get(NAME)
if old:
    for o in list(old.objects):bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(old)
scene=bpy.data.scenes.new(NAME);bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'
mats={};parts=[];models={}
palette=[('Walnut',(.245,.135,.078),.38,0),('Brass',(.57,.36,.16),.65,.6),
 ('Leather',(.30,.145,.070),.30,0),('Felt',(.065,.17,.09),.06,0),
 ('Ivory',(.64,.55,.37),.25,0),('Red',(.36,.055,.042),.28,0),
 ('Black',(.028,.022,.018),.38,0),('Glass',(.19,.26,.24),.86,.20),
 ('GreenGlass',(.035,.32,.11),.75,.08),('Cream',(.52,.41,.27),.12,0),
 ('Paper',(.57,.47,.31),.04,0),('Book',(.19,.060,.045),.25,0),
 ('Blue',(.10,.19,.22),.3,0),('MapLand',(.34,.26,.13),.15,0),
 ('MapSea',(.11,.17,.17),.22,0),('Ink',(.018,.018,.019),.80,0)]
for name,col,smooth,metal in palette:
    m=bpy.data.materials.get('BCD_'+name) or bpy.data.materials.new('BCD_'+name)
    m.use_nodes=True;m.diffuse_color=(*col,1)
    n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value=(*col,1);n.inputs['Metallic'].default_value=metal;n.inputs['Roughness'].default_value=1-smooth
    mats[name]=m
for name in ('Portrait','Landscape','StillLife','Hunt'):
    m=bpy.data.materials.get('BCD_'+name) or bpy.data.materials.new('BCD_'+name);m.use_nodes=True
    nodes=m.node_tree.nodes
    for n in list(nodes):
        if n.type=='TEX_IMAGE':nodes.remove(n)
    node=nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(ART/'Textures'/('BCD_'+name+'.png')),check_existing=True)
    m.node_tree.links.new(node.outputs['Color'],next(n for n in nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'])
    mats[name]=m
# Reuse the accepted original timber grain without changing the accepted material.
wood=mats['Walnut'];nodes=wood.node_tree.nodes
for n in list(nodes):
    if n.type=='TEX_IMAGE':nodes.remove(n)
n=nodes.new('ShaderNodeTexImage');n.image=bpy.data.images.load(str(ROOT/'igruha/Assets/_Project/Art/BelieveClubDetails/Textures/BCD_Walnut.png'),check_existing=True)
wood.node_tree.links.new(n.outputs['Color'],next(n for n in nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'])


def finish(o, mat, smooth=True):
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    o.data.materials.clear(); o.data.materials.append(mats[mat])
    uv = o.data.uv_layers.get('UVMap') or o.data.uv_layers.new(name='UVMap')
    for p in o.data.polygons:
        p.use_smooth = smooth
        axis = max(range(3), key=lambda i:abs(p.normal[i]))
        for li in p.loop_indices:
            c = o.matrix_world @ o.data.vertices[o.data.loops[li].vertex_index].co
            uv.data[li].uv = (c.y,c.z) if axis==0 else ((c.x,c.z) if axis==1 else (c.x,c.y))
    o.select_set(False); parts.append(o); return o

def mesh(name, verts, faces, mat, smooth=True):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(name,data);scene.collection.objects.link(o)
    return finish(o,mat,smooth)

def box(pos,size,mat='Walnut',bevel=.015):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos)
    o=bpy.context.object; o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        m=o.modifiers.new('Soft joinery edges','BEVEL');m.width=min(bevel,min(size)*.45);m.segments=3
        bpy.ops.object.modifier_apply(modifier=m.name)
        m=o.modifiers.new('Corner normals','WEIGHTED_NORMAL');m.keep_sharp=True
        bpy.ops.object.modifier_apply(modifier=m.name)
    return finish(o,mat,False)

def ellipsoid(pos,size,mat='Leather',seg=32,rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=pos)
    o=bpy.context.object;o.scale=size
    return finish(o,mat)

def lathe(pos,profile,mat='Walnut',seg=48,flutes=0):
    v=[];f=[]
    for z,r in profile:
        for i in range(seg):
            a=math.tau*i/seg;rr=r*(1+.05*math.cos(a*flutes)) if flutes else r
            v.append((pos[0]+rr*math.cos(a),pos[1]+rr*math.sin(a),pos[2]+z))
    for j in range(len(profile)-1):
        for i in range(seg):
            a=j*seg+i;b=j*seg+(i+1)%seg;f.append((a,b,b+seg,a+seg))
    f += [tuple(reversed(range(seg))),tuple((len(profile)-1)*seg+i for i in range(seg))]
    return mesh('Turned profile',v,f,mat)

def tube(points,radius,mat='Brass',radii=None,closed=False,linear=False):
    data=bpy.data.curves.new('Carved moulding','CURVE');data.dimensions='3D'
    data.resolution_u=8;data.bevel_depth=radius;data.bevel_resolution=2
    s=data.splines.new('POLY' if linear else 'BEZIER')
    collection=s.points if linear else s.bezier_points
    collection.add(len(points)-1)
    for i,(p,co) in enumerate(zip(collection,points)):
        if linear:p.co=(*co,1)
        else:p.co=co;p.handle_left_type='AUTO';p.handle_right_type='AUTO'
        if radii:p.radius=radii[i]
    s.use_cyclic_u=closed
    o=bpy.data.objects.new('Carved moulding',data);scene.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH');return finish(bpy.context.object,mat)

def ring(pos,rx,ry,rad,mat='Brass'):
    return tube([(pos[0]+rx*math.cos(i*math.tau/32),pos[1]+ry*math.sin(i*math.tau/32),pos[2]) for i in range(32)],rad,mat,closed=True)

def rounded_rectangle(w,h,r):
    pts=[];r=min(r,w*.25,h*.25)
    for cx,cy,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
        for a in np.linspace(start,start+90,7):
            a=math.radians(a);pts.append((cx+r*math.cos(a),cy+r*math.sin(a)))
    return pts

def frame(x,y,z,w,h,mat='Brass',r=.012):
    tube([(x+u,y,z+v) for u,v in rounded_rectangle(w,h,.05)],r,mat,closed=True,linear=True)

def scroll(cx,y,z,scale=1):
    for sign in (-1,1):
        pts=[]
        for i in range(33):
            a=i/32*math.pi*2.25;r=(.32-.265*i/32)*scale
            pts.append((cx+sign*(.28*scale+r*math.cos(a)),y,z+r*.62*math.sin(a)))
        tube(pts,.012*scale,'Brass')
    for sign in (-1,1):
        for i in range(3):
            tube([(cx+sign*.12*i*scale,y,z-.03),(cx+sign*(.12*i+.08)*scale,y-.015,z+.10*scale),(cx+sign*(.12*i+.18)*scale,y,z+.13*scale)],.009*scale,'Brass')

def join(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join()
    o=bpy.context.object;o.name='BCD_'+name
    scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    models[name]=o;parts.clear();o.select_set(False);return o

def export(name):
    o=models[name];bpy.ops.object.select_all(action='DESELECT');o.select_set(True)
    bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('BCD_'+name+'.fbx')),use_selection=True,
        object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,
        bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True,path_mode='STRIP')
    o.select_set(False)

def label(text,pos,size,mat='Ivory',rotation=(0,0,0)):
    data=bpy.data.curves.new('Engraved '+text,'FONT');data.body=text;data.align_x='CENTER';data.align_y='CENTER'
    data.size=size;data.extrude=.0003;data.resolution_u=2
    o=bpy.data.objects.new('Engraved '+text,data);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=rotation
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH');return finish(bpy.context.object,mat,False)

def roulette():
    # Radial layout: the long side runs across the side-wall strip, never along Z.
    box((0,0,.80),(2.46,1.24,.17),'Walnut',.08)
    box((0,0,.892),(2.29,1.10,.018),'Felt',.08)
    tube([(u,v,.91) for u,v in rounded_rectangle(2.39,1.17,.18)],.043,'Leather',closed=True,linear=True)
    tube([(u,v,.838) for u,v in rounded_rectangle(2.47,1.25,.18)],.012,'Brass',closed=True,linear=True)
    for x0 in (-.85,.85):
        lathe((x0,0,.13),[(0,.14),(.05,.16),(.16,.10),(.30,.075),(.42,.13),(.58,.16)],seg=64,flutes=12)
        for sign in (-1,1):
            tube([(x0,0,.29),(x0,sign*.25,.12),(x0,sign*.44,.055)],.065,'Walnut')
    tube([(-.85,0,.24),(.85,0,.24)],.042,'Walnut')
    cx=-.63
    lathe((cx,0,.90),[(0,.50),(.03,.535),(.07,.53),(.10,.50),(.085,.45),(.045,.385),(.03,.28),(.025,.05)],'Walnut',128)
    for r,z in ((.51,1.001),(.395,.962),(.273,.943)):ring((cx,0,z),r,r,.008,'Brass')
    order=[0,32,15,19,4,21,2,25,17,34,6,27,13,36,11,30,8,23,10,5,24,16,33,1,20,14,31,9,22,18,29,7,28,12,35,3,26]
    red={1,3,5,7,9,12,14,16,18,19,21,23,25,27,30,32,34,36}
    for i,num in enumerate(order):
        a=i*math.tau/37;b=(i+1)*math.tau/37
        verts=[(cx+r*math.cos(t),r*math.sin(t),z) for r,z in ((.285,.947),(.382,.96)) for t in (a,b)]
        mesh('Number pocket',verts,[(0,2,3,1)],'Felt' if num==0 else ('Red' if num in red else 'Black'),False)
        tube([(cx+.28*math.cos(a),.28*math.sin(a),.95),(cx+.39*math.cos(a),.39*math.sin(a),.967)],.0028,'Brass',linear=True)
        mid=(a+b)/2
        label(str(num),(cx+.345*math.cos(mid),.345*math.sin(mid),.965),.023,rotation=(0,0,mid-math.pi/2))
    lathe((cx,0,.929),[(0,.255),(.025,.24),(.065,.14),(.075,.04),(.19,.022),(.205,.04)],'Brass',64)
    for i in range(4):
        a=i*math.pi/2
        tube([(cx,0,1.094),(cx+.12*math.cos(a),.12*math.sin(a),1.094),(cx+.18*math.cos(a),.18*math.sin(a),1.065)],.012,'Brass')
        ellipsoid((cx+.18*math.cos(a),.18*math.sin(a),1.065),(.022,.022,.022),'Brass',16,8)
    ellipsoid((cx+.44,0,.98),(.013,.013,.013),'Ivory',16,8)
    # Printed betting grid: 3 rows, twelve columns, a separate green zero field.
    for j in range(13):
        x0=.05+j*.078
        tube([(x0,-.315,.907),(x0,.315,.907)],.0015,'Ivory',linear=True)
    for y0 in (-.315,-.105,.105,.315):tube([(.05,y0,.907),(.986,y0,.907)],.0015,'Ivory',linear=True)
    for j in range(12):
        for row in range(3):
            num=j*3+row+1;x0=.089+j*.078;y0=-.21+row*.21
            label(str(num),(x0,y0,.909),.046,rotation=(0,0,math.pi/2))
    for row,t in enumerate(('1-12','13-24','25-36')):label(t,(.20+row*.32,.405,.908),.043)
    for k,(x0,y0,col,count) in enumerate(((.24,-.43,'Red',6),(.47,-.43,'Ivory',5),(.73,-.43,'Blue',8),(.95,.43,'Red',4))):
        for j in range(count):
            lathe((x0,y0,.913+j*.012),[(0,.037),(.009,.037)],col,32)
            for a in (0,math.pi/2,math.pi,3*math.pi/2):
                box((x0+.031*math.cos(a),y0+.031*math.sin(a),.917+j*.012),(.008,.008,.009),'Ivory',.001)
    join('RouletteTable')

def banker_lamp():
    lathe((0,0,0),[(0,.135),(.022,.145),(.04,.13),(.055,.105),(.065,.055)],'Brass',64)
    tube([(0,.035,.055),(0,.045,.20),(0,.015,.34),(0,-.03,.355)],.018,'Brass')
    for sign in (-1,1):tube([(0,.015,.29),(sign*.15,.015,.29),(sign*.17,-.035,.35)],.009,'Brass')
    # Thick open-bottom cylindrical green glass hood; no solid ellipsoid underside.
    verts=[];faces=[];nx=16;na=32
    for layer in range(2):
        for i in range(nx+1):
            x0=-.205+i/nx*.410
            for j in range(na+1):
                a=j/na*math.pi;r=.112-layer*.007
                taper=1-.045*(abs(x0)/.205)**8
                verts.append((x0,-.035+math.cos(a)*r*taper,.34+math.sin(a)*r*taper))
    n=(nx+1)*(na+1)
    for l in range(2):
        for i in range(nx):
            for j in range(na):
                a=l*n+i*(na+1)+j
                f=(a,a+1,a+na+2,a+na+1);faces.append(f if l==0 else tuple(reversed(f)))
    for j in range(na):
        faces.append((j,n+j,n+j+1,j+1));a=nx*(na+1)+j;faces.append((a,a+1,n+a+1,n+a))
    hood=mesh('Emerald glass shade',verts,faces,'GreenGlass')
    for face in hood.data.polygons:
        for li in face.loop_indices:
            vi=hood.data.loops[li].vertex_index % n
            hood.data.uv_layers.active.data[li].uv=(vi//(na+1)/nx,(vi%(na+1))/na)
    for sign in (-1,1):tube([(-.205,-.035+sign*.109,.34),(.205,-.035+sign*.109,.34)],.005,'Brass',linear=True)
    for x0 in (-.205,.205):tube([(x0,-.035+.11*math.cos(a),.34+.11*math.sin(a)) for a in np.linspace(0,math.pi,24)],.004,'Brass')
    ellipsoid((0,-.035,.342),(.080,.018,.018),'Cream',24,12)
    tube([(.11,-.005,.332),(.11,-.005,.18)],.002,'Brass',linear=True)
    ellipsoid((.11,-.005,.17),(.012,.012,.019),'Brass',16,8)
    join('BankerLamp')

def bureau():
    box((0,0,.752),(1.45,.77,.105),'Walnut',.035)
    box((0,-.38,.708),(1.41,.045,.06),'Brass',.008)
    box((0,-.03,.585),(1.32,.61,.23),'Walnut',.018)
    box((0,-.345,.59),(.57,.045,.17),'Walnut',.008)
    frame(0,-.373,.59,.50,.12,'Brass',.004)
    for x0 in (-.47,.47):
        box((x0,-.345,.59),(.31,.045,.17),'Walnut',.008)
        frame(x0,-.373,.59,.26,.12,'Brass',.004)
        ring0=lathe((0,0,0),[(0,.022),(.015,.022)],'Brass',24);ring0.rotation_euler[0]=math.pi/2;ring0.location=(x0,-.379,.59)
        tube([(x0-.045,-.394,.605),(x0,-.413,.575),(x0+.045,-.394,.605)],.005,'Brass')
    for x0 in (-.61,.61):
        for y0 in (-.25,.25):
            lathe((x0,y0,.035),[(0,.043),(.04,.052),(.10,.034),(.38,.029),(.47,.052),(.57,.055)],seg=40,flutes=8)
    box((0,.29,.842),(1.32,.20,.11),'Walnut',.022)
    for x0 in (-.40,0,.40):
        box((x0,.165,.85),(.36,.035,.08),'Walnut',.008)
        ellipsoid((x0,.14,.85),(.014,.008,.014),'Brass',16,8)
    join('Bureau')

def desk_chair():
    for x0 in (-.25,.25):
        for y0 in (-.23,.23):lathe((x0,y0,.02),[(0,.035),(.10,.025),(.38,.039),(.44,.045)],seg=32)
    box((0,0,.445),(.66,.61,.13),'Walnut',.065)
    box((0,-.025,.527),(.60,.53,.115),'Leather',.055)
    tube([(u,v-.025,.53) for u,v in rounded_rectangle(.592,.522,.085)],.004,'Brass',closed=True,linear=True)
    for x0 in (-.29,.29):tube([(x0,.24,.43),(x0,.27,.82),(x0,.32,1.15)],.035,'Walnut')
    box((0,.30,.93),(.56,.11,.45),'Walnut',.075)
    box((0,.234,.93),(.48,.08,.36),'Leather',.055)
    for x0 in (-.12,.12):
        for z in (.84,1.01):ellipsoid((x0,.188,z),(.011,.006,.011),'Brass',16,8)
    for sign in (-1,1):
        tube([(sign*.28,-.22,.51),(sign*.28,-.22,.72),(sign*.29,.26,.76)],.032,'Walnut')
        tube([(sign*.28,-.19,.73),(sign*.29,.17,.77)],.045,'Leather')
    join('BureauChair')

def globe():
    lathe((0,0,.02),[(0,.26),(.06,.28),(.11,.23),(.16,.15),(.22,.07),(.42,.045),(.53,.13),(.56,.19)],seg=64,flutes=12)
    for i in range(3):
        a=i*math.tau/3
        tube([(0,0,.23),(.20*math.cos(a),.20*math.sin(a),.11),(.29*math.cos(a),.29*math.sin(a),.045)],.039,'Walnut')
    r=.275;cz=.92
    ellipsoid((0,0,cz),(r,r,r),'MapSea',64,32)
    # Hand-drawn, original cartographic silhouettes on the spherical surface.
    continents=[ [(-160,58),(-128,68),(-98,53),(-80,28),(-92,8),(-115,23),(-128,43)],
      [(-78,10),(-45,-5),(-39,-21),(-66,-56),(-76,-29)],
      [(-15,37),(12,36),(39,13),(31,-24),(16,-36),(-3,-8)],
      [(-10,47),(25,69),(63,58),(93,69),(147,55),(125,17),(105,4),(71,19),(45,43)],
      [(113,-13),(138,-11),(153,-28),(129,-39),(115,-28)] ]
    def sphere(lon,lat,radius=r+.002):
        a=math.radians(lon);b=math.radians(lat)
        return (radius*math.cos(b)*math.sin(a),-radius*math.cos(b)*math.cos(a),cz+radius*math.sin(b))
    for outline in continents:
        center=(sum(a for a,b in outline)/len(outline),sum(b for a,b in outline)/len(outline))
        verts=[];faces=[]
        for i in range(len(outline)):
            a=center;b=outline[i];c=outline[(i+1)%len(outline)]
            n=16;lookup={}
            for u in range(n+1):
                for v in range(n+1-u):
                    lookup[(u,v)]=len(verts)
                    verts.append(sphere(a[0]+(b[0]-a[0])*u/n+(c[0]-a[0])*v/n,
                                        a[1]+(b[1]-a[1])*u/n+(c[1]-a[1])*v/n))
            for u in range(n):
                for v in range(n-u):
                    faces.append((lookup[(u,v)],lookup[(u+1,v)],lookup[(u,v+1)]))
                    if u+v<n-1:faces.append((lookup[(u+1,v)],lookup[(u+1,v+1)],lookup[(u,v+1)]))
        o=mesh('Cartographic land',verts,faces,'MapLand')
        # Orient all patches outward after spherical tessellation.
        for face in o.data.polygons:
            if face.normal.dot(face.center-Vector((0,0,cz)))<0:face.flip()
    for lat in (-60,-30,0,30,60):tube([sphere(a,lat,r+.004) for a in np.linspace(-180,180,96,endpoint=False)],.0008,'Brass',closed=True,linear=True)
    for lon in range(0,180,30):tube([sphere(lon,b,r+.004) for b in np.linspace(-90,90,49)]+[sphere(lon+180,b,r+.004) for b in np.linspace(90,-90,49)],.0008,'Brass',closed=True,linear=True)
    tube([(0,.310*math.sin(a),cz+.310*math.cos(a)) for a in np.linspace(0,math.tau,96,endpoint=False)],.010,'Brass',closed=True,linear=True)
    ring((0,0,cz),.332,.332,.018,'Walnut')
    ring((0,0,cz+.012),.333,.333,.004,'Brass')
    for sign in (-1,1):tube([(0,sign*.24,.57),(0,sign*.31,.72),(0,sign*.32,.92)],.018,'Brass')
    join('Globe')

def floor_lamp():
    lathe((0,0,0),[(0,.21),(.03,.24),(.06,.21),(.08,.11),(.13,.055),(.19,.04)],'Brass',64)
    lathe((0,0,.13),[(0,.027),(1.18,.019),(1.25,.042),(1.29,.035)],'Brass',48)
    # Pleated cream linen, closed thickness at the rim, no extra light or emission.
    verts=[];faces=[];n=96
    for z,r in ((1.37,.28),(1.77,.16),(1.765,.15),(1.375,.27)):
        for i in range(n):
            a=i*math.tau/n;rr=r+.004*(i%2)
            verts.append((rr*math.cos(a),rr*math.sin(a),z))
    for j in range(4):
        for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,((j+1)%4)*n+(i+1)%n,((j+1)%4)*n+i))
    mesh('Pleated linen shade',verts,faces,'Cream',False)
    ring((0,0,1.37),.281,.281,.006,'Brass');ring((0,0,1.77),.162,.162,.005,'Brass')
    ellipsoid((0,0,1.794),(.018,.018,.026),'Brass',20,12)
    join('FloorLamp')

def painting(name,w,h):
    box((0,0,0),(w,.052,h),'Walnut',.006)
    for inset,y,r in ((0,-.042,.026),(.037,-.068,.014),(.064,-.079,.008)):
        frame(0,y,0,w-2*inset,h-2*inset,'Brass',r)
    # Alternating hand-carved leaf strokes along all four sides.
    for sign in (-1,1):
        for i in range(9):
            z=-h*.38+i*h*.095
            tube([(sign*(w/2-.014),-.066,z-.018),(sign*(w/2+.012),-.071,z),(sign*(w/2-.014),-.066,z+.018)],.004,'Brass')
        for i in range(8):
            x0=-w*.36+i*w*.103
            tube([(x0-.018,-.066,sign*(h/2-.014)),(x0,-.071,sign*(h/2+.012)),(x0+.018,-.066,sign*(h/2-.014))],.004,'Brass')
    iw=w-.15;ih=h-.15
    o=mesh('Original canvas '+name,[(-iw/2,-.066,-ih/2),(iw/2,-.066,-ih/2),(iw/2,-.066,ih/2),(-iw/2,-.066,ih/2)],[(0,1,2,3)],name,False)
    for li,uv in zip(o.data.polygons[0].loop_indices,((0,0),(1,0),(1,1),(0,1))):o.data.uv_layers.active.data[li].uv=uv
    join('Painting_'+name)

def tumbler(x,y,z):
    lathe((x,y,z),[(0,.037),(.008,.041),(.10,.046),(.103,.043),(.013,.036)],'Glass',40)
    lathe((x,y,z+.015),[(0,.036),(.030,.038)],'Brass',32)
    for i in range(12):
        a=i*math.tau/12;tube([(x+.040*math.cos(a),y+.040*math.sin(a),z+.014),(x+.045*math.cos(a),y+.045*math.sin(a),z+.094)],.0014,'Glass',linear=True)

def bar_service():
    # A narrow silver tray with a hollow bucket, ice cubes, two glasses and soda siphon.
    box((0,0,.012),(.82,.30,.024),'Brass',.025)
    tube([(u,v,.030) for u,v in rounded_rectangle(.81,.29,.05)],.008,'Brass',closed=True,linear=True)
    lathe((-.22,0,.025),[(0,.087),(.025,.095),(.20,.114),(.21,.108),(.032,.084)],'Brass',48)
    for sign in (-1,1):tube([(-.22+sign*.107,-.036,.16),(-.22+sign*.139,0,.135),(-.22+sign*.107,.036,.16)],.006,'Brass')
    for i,(x0,y0) in enumerate(((-.26,-.03),(-.18,.026),(-.23,.05),(-.19,-.04))):
        o=box((x0,y0,.207),(.053,.043,.039),'Glass',.009);o.rotation_euler=(.2*i,.1*i,.7*i)
    for y0 in (-.078,.078):tumbler(.012,y0,.03)
    lathe((.245,.025,.03),[(0,.055),(.03,.065),(.18,.067),(.245,.036),(.27,.026)],'Blue',48)
    lathe((.245,.025,.30),[(0,.036),(.035,.039),(.065,.026)],'Brass',32)
    tube([(.245,.025,.345),(.19,.025,.345),(.16,.025,.32)],.014,'Brass')
    tube([(.25,.025,.36),(.28,.025,.395),(.22,.025,.395)],.006,'Brass')
    join('BarService')

def lounge_service():
    # Everything fits the existing oval coffee table; local Z zero is its surface.
    for j,(w,h,col) in enumerate(((.24,.17,'Book'),(.22,.16,'Blue'),(.23,.155,'Book'))):
        z=.014+j*.036
        box((-.14,.15,z),(w,h,.028),'Paper',.002)
        for dz in (-.017,.017):box((-.14,.15,z+dz),(w+.01,h+.01,.004),col,.002)
        box((-.14-w/2,.15,z),(.010,h+.01,.036),col,.003)
    for x0,y0 in ((.18,.13),(.18,-.15)):tumbler(x0,y0,.005)
    lathe((-.14,-.15,.003),[(0,.085),(.012,.095),(.031,.089),(.032,.065),(.012,.059)],'Brass',48)
    for i in range(3):
        a=i*math.tau/3;ellipsoid((-.14+.055*math.cos(a),-.15+.055*math.sin(a),.018),(.012,.015,.005),'Black',16,8)
    join('LoungeService')

def desk_stationery():
    box((0,0,.006),(.49,.29,.012),'Leather',.012)
    for i in range(3):
        o=box((-.06,0,.015+i*.0025),(.24,.21,.002),'Paper',.001);o.rotation_euler.z=i*.035
    for j in range(6):tube([(-.15,-.07+j*.023,.024),(.018,-.07+j*.023,.024)],.00065,'Ink',linear=True)
    lathe((.16,.068,.015),[(0,.033),(.016,.040),(.042,.035),(.055,.022)],'Ink',32)
    lathe((.16,.068,.069),[(0,.026),(.009,.026)],'Brass',32)
    tube([(.07,-.10,.024),(.20,.01,.027)],.004,'Black',linear=True)
    tube([(.065,-.106,.024),(.087,-.087,.025)],.004,'Brass',linear=True)
    join('DeskStationery')

roulette();export('RouletteTable')

def save_source():
    for i,(name,o) in enumerate(models.items()):o.location=((i%4)*3,(i//4)*3,0)
    bpy.data.libraries.write(str(SOURCE/'BelieveClubDetails.blend'),{scene},fake_user=True,compress=True,path_remap='RELATIVE')

if globals().get('BCD_EXPORT_ALL',False):
    banker_lamp();bureau();desk_chair();globe();floor_lamp()
    painting('Portrait',.92,1.115);painting('Landscape',1.13,.885)
    painting('StillLife',1.13,.885);painting('Hunt',1.13,.885)
    bar_service();lounge_service();desk_stationery()
    for name in models:
        if name!='RouletteTable':export(name)
save_source()
print('Original detail models:',list(models))
