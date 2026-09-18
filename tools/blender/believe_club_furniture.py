"""IGR-565: original perimeter furniture. Run in live Blender through MCP.
Metres, front -Y, floor pivot. First export BackBar; BCF_EXPORT_ALL after Unity check.
Only the named furniture scene is replaced. No purchased meshes or random state.
"""
import bpy
import math
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/BelieveClubFurniture'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/BelieveClubFurniture'
for p in (ART/'Models', ART/'Textures', SOURCE): p.mkdir(parents=True, exist_ok=True)
NAME = 'Believe_Club_Furniture'
if bpy.context.scene.name == NAME:
    bpy.context.window.scene = next(s for s in bpy.data.scenes if s.name != NAME)
old = bpy.data.scenes.get(NAME)
if old:
    for o in list(old.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(old)
scene = bpy.data.scenes.new(NAME)
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
mats = {}
palette = [
    ('Walnut', (.245,.135,.078), .38,0), ('Brass',(.57,.36,.16),.68,.65),
    ('Leather',(.36,.20,.115),.30,0), ('Seam',(.16,.068,.032),.22,0),
    ('Bottle',(.045,.105,.07),.75,.15), ('Glass',(.29,.33,.32),.88,.3),
    ('Ivory',(.55,.45,.30),.25,0), ('Fur',(.25,.135,.072),.08,0),
    ('Antler',(.43,.34,.22),.18,0), ('Black',(.028,.020,.016),.42,0),
    ('Glazing',(.22,.29,.27),.94,0)]
for name, color, smooth, metal in palette:
    mat = bpy.data.materials.get('BCF_'+name) or bpy.data.materials.new('BCF_'+name)
    mat.use_nodes = True
    mat.diffuse_color = (*color,1)
    node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = (*color,1)
    node.inputs['Roughness'].default_value = 1-smooth
    node.inputs['Metallic'].default_value = metal
    mats[name] = mat
next(n for n in mats['Glazing'].node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Alpha'].default_value=.08

def image(name, rgb):
    h,w,_ = rgb.shape
    data = np.ones((h,w,4), dtype=np.float32); data[:,:,:3] = rgb
    old = bpy.data.images.get(name)
    if old: bpy.data.images.remove(old)
    im = bpy.data.images.new(name,width=w,height=h,alpha=True)
    im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(np.clip(data,0,1).ravel())
    im.file_format = 'PNG'; im.filepath_raw = str(ART/'Textures'/(name+'.png')); im.save()
    return im

y,x = np.mgrid[0:512,0:512]/512
grain = .017*np.sin(y*math.tau*23+1.9*np.sin(x*math.tau*2)) + .006*np.sin(y*math.tau*97+np.sin(x*math.tau*3))
pores = np.sin(x*math.tau*137+2*np.sin(y*math.tau*41))*np.sin(y*math.tau*127+2*np.sin(x*math.tau*37))
for name, tex in [('Walnut',np.stack([.245+grain,.135+grain*.6,.078+grain*.35],-1)),
                  ('Leather',np.stack([.36+pores*.018,.20+pores*.012,.115+pores*.008],-1))]:
    im = image('BCF_'+name,tex)
    nodes = mats[name].node_tree.nodes
    for n in list(nodes):
        if n.type == 'TEX_IMAGE': nodes.remove(n)
    node = nodes.new('ShaderNodeTexImage'); node.image = im
    mats[name].node_tree.links.new(node.outputs['Color'],next(n for n in nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'])
gy,gx = np.gradient(pores)
norm = np.stack([-gx*.13,-gy*.13,np.ones_like(x)],-1)
norm /= np.linalg.norm(norm,axis=-1)[:,:,None]
image('BCF_LeatherNormal',norm*.5+.5)
parts = []; models = {}

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
    o=bpy.context.object;o.name='BCF_'+name
    scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    models[name]=o;parts.clear();o.select_set(False);return o

def export(name):
    o=models[name];bpy.ops.object.select_all(action='DESELECT');o.select_set(True)
    bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('BCF_'+name+'.fbx')),use_selection=True,
        object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,
        bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True,path_mode='STRIP')
    o.select_set(False)

def backbar():
    box((0,.22,1.72),(5.36,.20,3.32))
    box((0,0,.14),(5.52,.76,.28));box((0,-.025,.91),(5.50,.78,.13))
    for x0 in (-2.16,-1.08,0,1.08,2.16):
        box((x0,-.14,.54),(.98,.42,.62))
        frame(x0,-.36,.54,.84,.47,'Walnut',.023)
        frame(x0,-.38,.54,.78,.41,'Brass',.007)
    for z in (1.18,1.76,2.34):
        box((0,-.01,z),(5.10,.57,.065))
        box((0,-.30,z+.005),(5.12,.025,.035),'Brass',.007)
    for x0 in (-2.56,0,2.56):
        lathe((x0,-.22,.96),[(0,.15),(.06,.18),(.13,.13),(.22,.115),(1.85,.11),(1.93,.17),(2.03,.18)],flutes=16)
        for z in (1.03,1.12,2.90,2.97):lathe((x0,-.22,z),[(0,.157),(.045,.157)],'Brass')
    # The scalloped arches remain open; the back is dark timber, never emissive.
    for x0 in (-1.28,1.28):
        tube([(x0+1.1*math.cos(a),-.15,2.48+.40*math.sin(a)) for a in np.linspace(0,math.pi,25)],.065,'Walnut')
        tube([(x0+1.08*math.cos(a),-.225,2.48+.40*math.sin(a)) for a in np.linspace(0,math.pi,25)],.012,'Brass')
    box((0,-.015,3.15),(5.60,.72,.32))
    box((0,-.025,3.36),(5.74,.80,.12))
    box((0,-.43,3.03),(5.65,.045,.055),'Brass')
    for x0 in (-2.05,-.80,.80,2.05):scroll(x0,-.388,3.16,.92)
    ellipsoid((0,-.41,3.20),(.19,.05,.22),'Walnut')
    frame(0,-.464,3.20,.25,.28,'Brass',.008)
    # Bottles and stemware belong to the backbar, repeated deterministic shapes.
    for shelf,z in enumerate((1.215,1.795,2.375)):
        for j in range(16):
            x0=-2.25+j*.30
            if abs(x0)<.18:continue
            if (j+shelf)%3:
                h=.31+.045*((j+shelf)%3)
                lathe((x0,-.075,z),[(0,.047),(.025,.056),(h*.64,.056),(h*.77,.025),(h,.025)],'Bottle',24)
                lathe((x0,-.075,z+h-.04),[(0,.027),(.045,.027)],'Brass',20)
                lathe((x0,-.075,z+.09),[(0,.057),(.10,.057)],'Ivory',24)
            else:
                lathe((x0,-.075,z),[(0,.056),(.016,.056),(.024,.012),(.14,.011),(.16,.044),(.22,.064),(.285,.062),(.29,.055),(.235,.053),(.185,.025)],'Glass',24)
    join('BackBar')

def counter():
    box((0,0,.52),(5.32,.70,1.00))
    box((0,0,.10),(5.49,.80,.20))
    box((0,-.04,1.06),(5.70,.94,.14),bevel=.045)
    box((0,-.52,1.02),(5.58,.03,.055),'Brass')
    for x0 in (-2.10,-1.05,0,1.05,2.10):
        box((x0,-.37,.56),(.96,.07,.70))
        frame(x0,-.413,.54,.78,.50,'Walnut',.027)
        frame(x0,-.445,.54,.72,.44,'Brass',.009)
        scroll(x0,-.425,.89,.61)
    for x0 in (-2.58,-1.57,-.525,.525,1.57,2.58):
        box((x0,-.43,.54),(.07,.08,.69))
    tube([(-2.75,-.61,.21),(2.75,-.61,.21)],.034,'Brass')
    for x0 in (-2.4,-1.2,0,1.2,2.4):
        tube([(x0,-.28,.08),(x0,-.60,.08),(x0,-.61,.21)],.022,'Brass')
    join('BarCounter')

def stool():
    lathe((0,0,.77),[(0,.26),(.025,.29),(.085,.29),(.125,.26)],'Leather',64)
    ring((0,0,.80),.285,.285,.008,'Seam')
    ring((0,0,.30),.22,.22,.018,'Brass')
    for i in range(4):
        a=math.pi/4+i*math.pi/2;x0=math.cos(a);y0=math.sin(a)
        tube([(x0*.23,y0*.23,.045),(x0*.18,y0*.18,.42),(x0*.17,y0*.17,.78)],.032,'Walnut')
        lathe((x0*.23,y0*.23,.015),[(0,.035),(.075,.035)],'Brass',24)
    join('BarStool')

def tufted_back(w):
    # Diamond channels and deep button wells are actual surface geometry.
    nx=round(w*70);nz=42;v=[];f=[]
    for j in range(nz+1):
        z=.58+j/nz*.51
        for i in range(nx+1):
            x0=-w/2+i/nx*w
            a=(x0+w/2)/.27+(z-.58)/.23
            b=(x0+w/2)/.27-(z-.58)/.23
            puff=abs(math.sin(a*math.pi)*math.sin(b*math.pi))**.6
            v.append((x0,.245-.080*puff,z))
    for j in range(nz):
        for i in range(nx):
            a=j*(nx+1)+i;f.append((a,a+1,a+nx+2,a+nx+1))
    mesh('Diamond tufted leather',v,f,'Leather')
    for row in range(3):
        z=.58+row*.23
        for k in range(int(w/.27)+1):
            x0=-w/2+(k+(row%2)*.5)*.27
            if x0>w/2-.045 or x0<-w/2+.045:continue
            ellipsoid((x0,.238,z),(.018,.014,.018),'Seam',16,8)

def upholstery(name,w,seats):
    for x0 in (-w/2+.18,w/2-.18):
        for y0 in (-.32,.33):
            lathe((x0,y0,.025),[(0,.063),(.035,.077),(.10,.067),(.18,.050),(.21,.071)],'Walnut',32)
    box((0,.01,.31),(w-.13,.89,.23),'Leather',.065)
    box((0,.355,.80),(w-.26,.26,.64),'Leather',.12)
    tufted_back(w-.43)
    ellipsoid((0,.35,1.085),(w/2-.12,.17,.115),'Leather',48,16)
    for sign in (-1,1):
        x0=sign*(w/2-.16)
        box((x0,0,.57),(.27,.91,.49),'Leather',.115)
        roll=lathe((0,0,0),[(0,.15),(.025,.183),(.06,.20),(.94,.20),(.975,.183),(1.0,.15)],'Leather',64)
        roll.rotation_euler[0]=math.pi/2;roll.location=(x0,.45,.79)
        # Scroll on the front of each rolled arm; small brass upholstery nails.
        tube([(x0+.133*math.cos(a),-.553,.79+.133*math.sin(a)) for a in np.linspace(0,math.tau,33)],.006,'Seam',closed=True)
        for j in range(9):
            z=.455+j*.026
            ellipsoid((x0,-.458,z),(.009,.006,.009),'Brass',12,6)
    cushion_w=(w-.66)/seats
    for i in range(seats):
        x0=-(w-.66)/2+(i+.5)*cushion_w
        box((x0,-.105,.50),(cushion_w-.015,.64,.19),'Leather',.085)
        tube([(x0+u,-.105+v,.53) for u,v in rounded_rectangle(cushion_w-.035,.63,.08)],.004,'Seam',closed=True,linear=True)
    join(name)

def coffee_table():
    lathe((0,0,.13),[(0,.17),(.06,.13),(.15,.095),(.24,.15),(.28,.22)],'Walnut',64,12)
    top=lathe((0,0,.42),[(0,.60),(.045,.62),(.08,.60)],'Walnut',96)
    top.scale.y=.73
    ring((0,0,.473),.609,.444,.008,'Brass')
    for i in range(4):
        a=math.pi/4+i*math.pi/2
        tube([(math.cos(a)*.08,math.sin(a)*.08,.23),(math.cos(a)*.29,math.sin(a)*.29,.16),(math.cos(a)*.42,math.sin(a)*.42,.055)],.047,'Walnut')
    join('CoffeeTable')

def clock():
    box((0,0,.18),(.80,.56,.36))
    box((0,.08,1.20),(.61,.37,1.78))
    box((0,-.14,1.14),(.46,.035,1.18),'Black')
    frame(0,-.17,1.14,.45,1.15,'Walnut',.038)
    frame(0,-.215,1.14,.36,1.06,'Brass',.009)
    # Pendulum visible through the open, glazed door; restrained edge reflections.
    tube([(0,-.19,1.66),(0,-.19,.86)],.009,'Brass')
    ellipsoid((0,-.205,.83),(.115,.018,.115),'Brass')
    for x0 in (-.105,.105):
        tube([(x0,-.17,1.70),(x0,-.17,1.13)],.0035,'Brass')
        lathe((x0,-.17,1.12),[(0,.024),(.20,.024)],'Brass',20)
    tube([(-.125,-.236,.66),(-.145,-.236,1.52)],.003,'Glass')
    tube([(.125,-.236,.67),(.105,-.236,1.51)],.002,'Glass')
    mesh('Clock door glazing',[(-.175,-.242,.63),(.175,-.242,.63),(.175,-.242,1.65),(-.175,-.242,1.65)],[(0,1,2,3)],'Glazing',False)
    box((0,0,2.05),(.76,.56,.63))
    face=lathe((0,0,0),[(0,.254),(.014,.254)],'Ivory',96)
    face.rotation_euler[0]=math.pi/2;face.location=(0,-.294,2.07)
    tube([(.278*math.cos(a),-.311,2.07+.278*math.sin(a)) for a in np.linspace(0,math.tau,64)],.017,'Brass',closed=True)
    for i in range(12):
        a=i*math.tau/12
        tube([(.214*math.sin(a),-.314,2.07+.214*math.cos(a)),(.24*math.sin(a),-.314,2.07+.24*math.cos(a))],.005,'Black')
    tube([(0,-.328,2.07),(-.135,-.328,2.19)],.009,'Black')
    tube([(0,-.331,2.07),(.11,-.331,2.10)],.006,'Black')
    ellipsoid((0,-.34,2.07),(.018,.008,.018),'Brass',16,8)
    for x0 in (-.35,.35):
        lathe((x0,-.22,1.78),[(0,.055),(.045,.07),(.53,.049),(.58,.08)],'Walnut',32,8)
    box((0,0,2.42),(.89,.64,.12))
    tube([(-.40,-.03,2.46),(-.23,-.03,2.53),(0,-.03,2.61),(.23,-.03,2.53),(.40,-.03,2.46)],.052,'Walnut')
    scroll(0,-.34,2.43,.55)
    join('GrandfatherClock')

def deer():
    # Closed shield with a tapered lower point; muzzle, ears and branching antlers.
    outline=[(-.28,.35),(-.30,.18),(-.25,-.30),(0,-.54),(.25,-.30),(.30,.18),(.28,.35),(0,.43)]
    verts=[(x,y,z) for y in (.05,-.06) for x,z in outline]
    faces=[tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    mesh('Stag oak shield',verts,faces,'Walnut',False)
    tube([(x,-.07,z) for x,z in outline],.017,'Walnut',closed=True)
    ellipsoid((0,-.19,-.06),(.20,.21,.40),'Fur')
    ellipsoid((0,-.36,.24),(.18,.21,.27),'Fur')
    ellipsoid((0,-.53,.14),(.115,.24,.12),'Fur')
    ellipsoid((0,-.723,.12),(.085,.048,.065),'Black')
    for sign in (-1,1):
        ear=ellipsoid((sign*.23,-.28,.42),(.20,.06,.087),'Fur');ear.rotation_euler[1]=sign*-.55
        ear=ellipsoid((sign*.235,-.335,.423),(.13,.015,.049),'Antler');ear.rotation_euler[1]=sign*-.55
        ellipsoid((sign*.147,-.487,.30),(.030,.014,.033),'Antler',20,10)
        ellipsoid((sign*.151,-.499,.30),(.017,.008,.020),'Black',20,10)
        tube([(sign*.12,-.25,.44),(sign*.22,-.17,.65),(sign*.35,-.12,.86),(sign*.45,-.07,1.10),(sign*.52,-.03,1.36)],.039,'Antler',[1,.9,.73,.48,.035])
        for pts in [((.21,-.17,.64),(.10,-.25,.82),(.09,-.26,.99)),((.32,-.12,.83),(.49,-.18,.91),(.64,-.13,1.09)),((.43,-.08,1.06),(.32,-.14,1.23),(.33,-.12,1.41))]:
            tube([(sign*x,y,z) for x,y,z in pts],.025,'Antler',[1,.55,.025])
    join('StagTrophy')

backbar()
export('BackBar')
if globals().get('BCF_EXPORT_ALL',False):
    counter();stool();upholstery('Chesterfield',2.76,3);upholstery('ClubArmchair',1.22,1)
    coffee_table();clock();deer()
    for name in models:
        if name!='BackBar':export(name)

def save_source():
    # An editable modelling layout in the .blend; exported pivots remain at floor origin.
    layout={'BackBar':(0,3,0),'BarCounter':(0,0,0),'BarStool':(4,0,0),
            'Chesterfield':(-4,-3,0),'ClubArmchair':(0,-3,0),'CoffeeTable':(2,-3,0),
            'GrandfatherClock':(4,3,0),'StagTrophy':(6,3,1)}
    for name,o in models.items():o.location=layout[name];o.hide_set(False)
    bpy.data.libraries.write(str(SOURCE/'BelieveClubFurniture.blend'),{scene},fake_user=True,compress=True,path_remap='RELATIVE')

save_source()
print('BCF BackBar ready: dimensions',tuple(models['BackBar'].dimensions),'triangles',sum(len(p.vertices)-2 for p in models['BackBar'].data.polygons))
