"""Author the Moonlit Gallery kit in Blender; source dimensions are in metres.
Run through tools/blender_client.py execute_code --file tools/blender/crying_angels.py.
Layout is the committed CryingAngels blockout snapshot, not a new random arena.
"""
import bpy, bmesh, math, json, random, time
from pathlib import Path
from mathutils import Vector, Matrix
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/CryingAngels'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/CryingAngels'
for p in [ART/'Models', ART/'Textures', SOURCE, ROOT/'docs/art/crying-angels']:
    p.mkdir(parents=True, exist_ok=True)
RNG = random.Random(914)
PI = math.pi
scene = bpy.data.scenes.new('CA_MoonlitGallery')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.world = bpy.data.worlds.new('CA_Night')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.04,.075,.16,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .06
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast'

# Periodic stone and marble textures: shared by all meshes in Blender and Unity.
N=1024
u,v=np.meshgrid(np.arange(N,dtype=np.float32)/N,np.arange(N,dtype=np.float32)/N)
noise=np.zeros((N,N),dtype=np.float32)
r=np.random.default_rng(23)
for freq,amp in [(2,.28),(5,.19),(11,.12),(23,.06),(59,.028),(131,.015)]:
    for j in range(3):
        fx=int(r.integers(1,freq+1)); fy=int(r.integers(1,freq+1))
        noise += amp/3 * np.sin(2*PI*(fx*u+fy*v)+r.random()*2*PI)
vein=np.exp(-np.abs(np.sin(2*PI*(3*u+2*v)+noise*15))*34)
stone=np.clip(.70 + noise*.5-vein*.15, .25,.92)
height=noise*.13-vein*.018

def write_image(name,rgb):
    rgba=np.ones((N,N,4),dtype=np.float32)
    rgba[:,:,:3]=rgb if rgb.ndim==3 else rgb[:,:,None]
    im=bpy.data.images.new(name,width=N,height=N,alpha=True)
    im.pixels.foreach_set(rgba.ravel()); im.filepath_raw=str(ART/'Textures'/f'{name}.png')
    im.file_format='PNG'; im.save(); return im
base=write_image('CA_WeatheredStone',stone)
gy,gx=np.gradient(height); normal=np.stack((-gx*180,-gy*180,np.ones_like(gx)),axis=-1)
normal/=np.linalg.norm(normal,axis=-1)[:,:,None]
norm=write_image('CA_StoneNormal',normal*.5+.5); norm.colorspace_settings.name='Non-Color'

materials=[]
def material(name,color,rough=.75,metal=0,emission=0,texture=True):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Roughness'].default_value=rough; bs.inputs['Metallic'].default_value=metal
    if texture:
        tex=m.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=base
        mult=m.node_tree.nodes.new('ShaderNodeMixRGB'); mult.blend_type='MULTIPLY'; mult.inputs[0].default_value=1; mult.inputs[2].default_value=(*color,1)
        m.node_tree.links.new(tex.outputs['Color'],mult.inputs[1]); m.node_tree.links.new(mult.outputs[0],bs.inputs['Base Color'])
        nt=m.node_tree.nodes.new('ShaderNodeTexImage'); nt.image=norm
        nm=m.node_tree.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.09
        m.node_tree.links.new(nt.outputs['Color'],nm.inputs['Color']); m.node_tree.links.new(nm.outputs['Normal'],bs.inputs['Normal'])
    if emission:
        bs.inputs['Emission Color'].default_value=(*color,1); bs.inputs['Emission Strength'].default_value=emission
    materials.append(m); return len(materials)-1
STONE=material('CA_PaleLimestone',(.66,.70,.73))
DARK=material('CA_BlueSlate',(.15,.22,.29))
TRIM=material('CA_CarvedStone',(.40,.48,.54))
FLOOR=material('CA_Marble',(.39,.46,.51),.4)
GOLD=material('CA_AgedBrass',(.34,.23,.105),.47,.72)
CRACK=material('CA_Crevices',(.045,.062,.071),1,texture=False)
GLASS=material('CA_MoonGlass',(.17,.44,.73),.3,emission=.6,texture=False)
IRON=material('CA_OxidizedIron',(.052,.075,.09),.52,.65)
WEB=material('CA_Cobweb',(.33,.43,.47),.95,texture=False)

# Mesh accumulation avoids thousands of Blender objects and keeps reusable props compact.
class Mesh:
    def __init__(self): self.v=[]; self.f=[]; self.m=[]
    def add(self,verts,faces,mat=STONE,matrix=None):
        start=len(self.v)
        self.v.extend([tuple(matrix@Vector(p)) for p in verts] if matrix else verts)
        self.f.extend([tuple(start+i for i in f) for f in faces]); self.m.extend([mat]*len(faces))
    def object(self,name):
        data=bpy.data.meshes.new(name); data.from_pydata(self.v,[],self.f); data.update()
        for m in materials: data.materials.append(m)
        uv=data.uv_layers.new(name='StoneUV')
        for poly,mi in zip(data.polygons,self.m):
            poly.material_index=mi
            n=poly.normal; axis=max(range(3),key=lambda i:abs(n[i])); pair=[i for i in range(3) if i!=axis]
            for li in poly.loop_indices:
                co=data.vertices[data.loops[li].vertex_index].co
                uv.data[li].uv=(co[pair[0]]*.55,co[pair[1]]*.55)
        obj=bpy.data.objects.new(name,data); scene.collection.objects.link(obj); return obj

def transform(pos=(0,0,0),rot=0):
    return Matrix.Translation(Vector(pos)) @ Matrix.Rotation(rot,4,'Z')

_box_cache={}
def box(m,pos,size,mat=STONE,bevel=.025,matrix=None):
    key=(*size,bevel)
    if key not in _box_cache:
        bm=bmesh.new(); bmesh.ops.create_cube(bm,size=1)
        for q in bm.verts: q.co.x*=size[0]; q.co.y*=size[1]; q.co.z*=size[2]
        if bevel: bmesh.ops.bevel(bm,geom=list(bm.edges),offset=min(bevel,min(size)*.22),segments=2,affect='EDGES')
        bm.verts.ensure_lookup_table(); bm.verts.index_update()
        _box_cache[key]=([tuple(q.co) for q in bm.verts],[tuple(q.index for q in f.verts) for f in bm.faces]); bm.free()
    verts,faces=_box_cache[key]
    matx=Matrix.Translation(Vector(pos)); matx=matrix@matx if matrix else matx
    m.add(verts,faces,mat,matx)

def lathe(m,pos,profile,mat=STONE,segments=32,flutes=0,matrix=None):
    verts=[]; faces=[]
    for z,rad in profile:
        for i in range(segments):
            a=2*PI*i/segments; radius=rad*(1-.045*(.5+.5*math.cos(a*flutes))) if flutes else rad
            verts.append((rad if False else radius*math.cos(a),radius*math.sin(a),z))
    for j in range(len(profile)-1):
        for i in range(segments):
            a=j*segments+i; b=j*segments+(i+1)%segments
            faces.append((a,b,b+segments,a+segments))
    faces.extend([tuple(reversed(range(segments))),tuple((len(profile)-1)*segments+i for i in range(segments))])
    x=Matrix.Translation(Vector(pos)); m.add(verts,faces,mat,matrix@x if matrix else x)

def ellipsoid(m,pos,scale,mat=STONE,segments=20,rings=12,matrix=None):
    verts=[]; faces=[]
    for j in range(rings+1):
        a=PI*j/rings
        for i in range(segments):
            b=2*PI*i/segments
            verts.append((math.sin(a)*math.cos(b)*scale[0],math.sin(a)*math.sin(b)*scale[1],math.cos(a)*scale[2]))
    for j in range(rings):
        for i in range(segments):
            a=j*segments+i; b=j*segments+(i+1)%segments; faces.append((a,b,b+segments,a+segments))
    x=Matrix.Translation(Vector(pos)); m.add(verts,faces,mat,matrix@x if matrix else x)

def tube(m,points,radius=.02,mat=STONE,sides=6):
    verts=[]; faces=[]
    for j,p in enumerate(points):
        tangent=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
        q=tangent.to_track_quat('Z','Y'); rr=radius[j] if isinstance(radius,list) else radius
        for i in range(sides): verts.append(tuple(Vector(p)+q@Vector((rr*math.cos(2*PI*i/sides),rr*math.sin(2*PI*i/sides),0))))
    for j in range(len(points)-1):
        for i in range(sides):
            a=j*sides+i; b=j*sides+(i+1)%sides; faces.append((a,b,b+sides,a+sides))
    faces.extend([tuple(reversed(range(sides))),tuple((len(points)-1)*sides+i for i in range(sides))]); m.add(verts,faces,mat)

def arc(m,center,radius,a0,a1,t=.04,mat=STONE,steps=32):
    tube(m,[(center[0]+radius*math.cos(a0+(a1-a0)*i/steps),center[1],center[2]+radius*math.sin(a0+(a1-a0)*i/steps)) for i in range(steps+1)],t,mat)

def ring(m,r,z,width=.04,mat=GOLD,steps=128):
    tube(m,[(r*math.cos(2*PI*i/steps),r*math.sin(2*PI*i/steps),z) for i in range(steps+1)],width,mat)

def plinth(m,height=.58,width=1,depth=.95):
    box(m,(0,0,.075),(width,depth,.15),TRIM,.035)
    box(m,(0,0,.18),(width*.94,depth*.94,.07),STONE,.02)
    box(m,(0,0,height*.53),(width*.83,depth*.83,height-.26),STONE,.025)
    box(m,(0,0,height-.10),(width*.94,depth*.94,.07),TRIM,.022)
    box(m,(0,0,height-.035),(width,depth,.09),STONE,.032)
    # Carved recessed plaque and two bead borders on each face.
    for sign in [-1,1]:
        box(m,(0,sign*depth*.424,height*.52),(width*.58,.02,height*.36),TRIM,.012)
        box(m,(0,sign*depth*.441,height*.52),(width*.49,.015,height*.24),DARK,.01)
        for zz in [-.08,.08]: box(m,(0,sign*depth*.455,height*.52+zz),(width*.34,.014,.009),GOLD,.001)
    for k in range(3):
        x=-.33+k*.28
        tube(m,[(x,-depth*.505,height-.014),(x+.045,-depth*.505,height-.12),(x+.007,-depth*.426,height-.20),(x+.061,-depth*.425,height-.31)],.004,CRACK,4)

def feather(m,start,end,width):
    start=Vector(start); end=Vector(end); axis=end-start
    side=Vector((axis.z,0,-axis.x)).normalized()*width
    verts=[]; faces=[]
    for j in range(9):
        t=j/8; cen=start+axis*t; bulge=math.sin(PI*t)**.65
        for i in range(6):
            a=2*PI*i/6; p=cen+side*(bulge*math.cos(a))+Vector((0,.027*bulge*math.sin(a),0))
            verts.append(tuple(p))
    for j in range(8):
        for i in range(6): faces.append((j*6+i,j*6+(i+1)%6,(j+1)*6+(i+1)%6,(j+1)*6+i))
    m.add(verts,faces,STONE)
    tube(m,[tuple(start+axis*t+Vector((0,-.029*math.sin(PI*t),0))) for t in [.05,.25,.5,.75,.95]],.0045,TRIM,4)

def angel(name,pose=0):
    m=Mesh(); plinth(m)
    # A softly tapering, deeply pleated garment, with an asymmetric hem.
    verts=[]; faces=[]; rings=22; seg=64
    for j in range(rings+1):
        t=j/rings; z=.59+t*1.07
        radius=.32*(1-t)+.15*t + .06*math.exp(-((t-.80)/.15)**2)
        for i in range(seg):
            a=2*PI*i/seg; folds=1+.12*math.cos(a*12+t*1.6)+.04*math.sin(a*23-t*3)
            verts.append((radius*folds*math.cos(a),radius*.70*folds*math.sin(a),z+.018*math.sin(a*6)*(1-t)))
    for j in range(rings):
        for i in range(seg):
            a=j*seg+i;b=j*seg+(i+1)%seg;faces.append((a,b,b+seg,a+seg))
    m.add(verts,faces,STONE)
    # Draped shoulder folds and sash.
    for i in range(5):
        tube(m,[(-.24+i*.07,-.15,1.53),(-.13+i*.055,-.215,1.4),(.1+i*.024,-.16,1.20)],.017,STONE,6)
    ellipsoid(m,(0,-.014,1.80),(.17,.14,.21),STONE,24,16)
    # Nose, eyelids and small downturned mouth; hands cover the eyes in pose 0.
    ellipsoid(m,(0,-.14,1.80),(.028,.045,.065),STONE,12,8)
    for sign in [-1,1]:
        tube(m,[(sign*.025,-.143,1.847),(sign*.058,-.153,1.846),(sign*.087,-.14,1.837)],.009,TRIM,5)
    tube(m,[(-.036,-.135,1.731),(0,-.148,1.733),(.036,-.135,1.731)],.006,TRIM,5)
    # Hood: cloth wraps the cheeks without placing a solid shell over the face.
    for row in range(4):
        rr=.19+row*.018
        arc(m,(0,.02+row*.024,1.79),rr,-.18,PI+.18,.023,STONE,26)
    ellipsoid(m,(0,.085,1.79),(.186,.124,.217),STONE,20,12)
    # Wing relief extends to the top of the cover envelope, layered flight feathers.
    for sign in [-1,1]:
        for layer in range(3):
            for k in range(8):
                t=k/7
                start=(sign*(.14+.02*layer),.11+layer*.027,1.45+.13*t)
                end=(sign*(.43-.13*t+.017*layer),.095+layer*.035,.96+1.13*t-.07*layer)
                feather(m,start,end,.055-.009*layer)
        tube(m,[(sign*.15,.13,1.20),(sign*.34,.14,1.56),(sign*.32,.14,2.07)],.028,STONE,7)
    for sign in [-1,1]:
        if pose==0:
            arm=[(sign*.19,-.01,1.56),(sign*.265,-.20,1.42),(sign*.17,-.25,1.61),(sign*.072,-.199,1.80)]
            palm=(sign*.073,-.191,1.807)
        elif pose==1:
            arm=[(sign*.19,-.01,1.56),(sign*.27,-.14,1.37),(sign*.15,-.26,1.39),(sign*.052,-.28,1.49)]
            palm=(sign*.046,-.282,1.50)
        else:
            arm=[(sign*.19,-.01,1.56),(sign*.28,-.13,1.42),(sign*.31,-.28,1.58),(sign*.29,-.30,1.65)]
            palm=(sign*.29,-.31,1.65)
        tube(m,arm,[.096,.078,.059,.047],STONE,10)
        ellipsoid(m,palm,(.052,.036,.072),STONE,12,8)
        for f in range(4):
            xx=palm[0]+(f-1.5)*.019
            tube(m,[(xx,palm[1]-.018,palm[2]),(xx,palm[1]-.02,palm[2]+.066),(xx-sign*.012,palm[1]+.005,palm[2]+.098-abs(f-1.5)*.009)],.011,STONE,6)
    # A broken stone seam across robe and shoulder.
    tube(m,[(.12,-.20,1.19),(.10,-.222,1.11),(.16,-.231,1.05),(.11,-.24,.99)],.004,CRACK,4)
    obj=m.object(name)
    return obj

def low_cover(name,variant):
    m=Mesh(); plinth(m,height=.54 if variant!=2 else .82)
    if variant==0:
        # Fallen wing and the broken face of an older statue on a display block.
        ellipsoid(m,(.1,-.03,.68),(.20,.19,.16),STONE)
        ellipsoid(m,(.10,-.192,.69),(.045,.035,.047),STONE,12,8)
        for i in range(7): feather(m,(-.30,.15,.59),(.30-i*.10,.12,.60+i*.022),.04)
        tube(m,[(.0,-.175,.73),(.065,-.18,.78),(.12,-.154,.82)],.007,CRACK,4)
    elif variant==1:
        # Draped stone reliquary, broad enough to read as crouching cover.
        box(m,(0,0,.66),(.82,.72,.20),STONE,.055)
        lathe(m,(0,0,.755),[(0,.12),(.03,.13),(.08,.05)],GOLD,16)
        for i in range(8):
            x=-.32+i*.092
            tube(m,[(x,-.40,.56),(x+.012,-.36,.75),(x+.03,0,.785),(x-.01,.34,.73)],.014,TRIM,5)
    else:
        for k in range(6):
            a=2*PI*k/6
            box(m,(math.cos(a)*.30,math.sin(a)*.25,.835),(.10,.12,.045),STONE,.013)
    return m.object(name)

assets={}
for i,n in enumerate(['CA_WeepingAngel','CA_PrayingAngel','CA_WarningAngel']): assets[n]=angel(n,i)
for i,n in enumerate(['CA_FallenVisage','CA_Reliquary','CA_BrokenPlinth']): assets[n]=low_cover(n,i)

# The colonnade is outside the existing wall collision radius.
m=Mesh()
box(m,(0,0,.16),(1.60,1.40,.32),DARK,.06)
lathe(m,(0,0,0),[(.30,.77),(.39,.77),(.44,.65),(.60,.60),(.70,.56),(.78,.51)],STONE,48)
lathe(m,(0,0,0),[(.78,.51),(1.0,.48),(7.9,.40),(8.1,.45)],TRIM,80,flutes=20)
lathe(m,(0,0,0),[(8.1,.46),(8.2,.55),(8.36,.58),(8.42,.55),(8.52,.73),(8.62,.73)],STONE,48)
for k in range(12):
    a=2*PI*k/12
    leaf=[(.44*math.cos(a),.44*math.sin(a),7.91),(.56*math.cos(a),.56*math.sin(a),8.17),(.70*math.cos(a),.70*math.sin(a),8.34)]
    tube(m,leaf,[.10,.095,.025],STONE,6)
box(m,(0,0,8.70),(1.57,1.37,.20),STONE,.05)
# Thin fractures on the column base, not floor obstacles.
for k in range(3):
    x=-.5+k*.36
    tube(m,[(x,-.711,.31),(x+.06,-.712,.23),(x+.02,-.712,.13),(x+.10,-.712,.05)],.008,CRACK,4)
assets['CA_FlutedColumn']=m.object('CA_FlutedColumn')

# A complete 22.5-degree facade module in local coordinates: interior faces -Y.
m=Mesh(); W=11.10
box(m,(0,.50,2.10),(W,1.05,4.4),DARK,.06)
for z,d,h in [(.16,1.18,.30),(.51,1.10,.12),(1.04,1.12,.12),(3.65,1.10,.14),(4.18,1.32,.23),(10.90,1.30,.30),(11.32,1.64,.27),(11.68,1.85,.23)]:
    box(m,(0,.47,z),(W,d,h),TRIM if z<10 else STONE,.04)
for x in [-4.15,-1.38,1.38,4.15]:
    box(m,(x,-.045,2.31),(2.28,.11,2.10),TRIM,.045)
    box(m,(x,-.114,2.31),(2.05,.08,1.88),DARK,.035)
    box(m,(x,-.166,2.31),(1.86,.028,1.70),TRIM,.03)
    for z in [1.53,3.09]: box(m,(x,-.187,z),(1.72,.026,.025),GOLD,.002)
# Two tall arched windows with real openings and recessed luminous glass.
for cx in [-2.76,2.76]:
    hw=1.64; spring=8.72; bottom=4.38
    for sign in [-1,1]:
        box(m,(cx+sign*(hw+.40),.5,7.17),(.76,1.08,5.64),DARK,.04)
        box(m,(cx+sign*(hw+.06),-.13,(bottom+spring)/2),(.16,.25,spring-bottom),STONE,.025)
    # Stone voussoirs and the wall spandrel above the arch.
    for j in range(18):
        a0=PI*j/18+.007; a1=PI*(j+1)/18-.007
        verts=[]
        for y in [-.05,.95]:
            for rr in [hw,hw+.34]:
                for a in [a0,a1]: verts.append((cx+rr*math.cos(a),y,spring+rr*math.sin(a)))
        m.add(verts,[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)],STONE)
    for j in range(24):
        x=-hw+(j+.5)*(2*hw/24); top=spring+math.sqrt(max(0,(hw+.35)**2-x*x))
        box(m,(cx+x,.55,(top+10.82)/2),(2*hw/24+.01,1.0,max(.02,10.82-top)),DARK,.0)
    box(m,(cx,.97,(bottom+spring)/2),(hw*2,.06,spring-bottom),GLASS,.0)
    verts=[(cx,.97,spring)]+[(cx+hw*math.cos(PI*j/24),.97,spring+hw*math.sin(PI*j/24)) for j in range(25)]
    m.add(verts,[(0,j+1,j+2) for j in range(24)],GLASS)
    for x in [-.82,0,.82]:
        top=spring+math.sqrt(hw*hw-x*x)
        box(m,(cx+x,.77,(bottom+top)/2),(.065,.14,top-bottom),IRON,.005)
    for z in [5.45,6.85,8.25]: box(m,(cx,.77,z),(hw*2,.14,.065),IRON,.005)
    arc(m,(cx,.73,8.53),.64,0,2*PI,.045,IRON,32)
    for a in [0,PI/2,PI,3*PI/2]: arc(m,(cx+.30*math.cos(a),.70,8.53+.30*math.sin(a)),.25,0,2*PI,.023,IRON,16)
    box(m,(cx,-.22,4.40),(3.72,.72,.17),STONE,.04)
# End piers + centre bay plaque.
for x in [-5.30,0,5.30]: box(m,(x,.53,7.0),(.42,1.08,5.8),DARK,.025)
box(m,(0,-.15,5.49),(.85,.20,1.12),TRIM,.035)
arc(m,(0,-.285,5.5),.29,0,2*PI,.026,GOLD,24)
assets['CA_ArchedWallBay']=m.object('CA_ArchedWallBay')

# Floor sectors are individually batched: imperfect slabs, contrasting concentric inlays.
R=28.08
for sector in range(16):
    m=Mesh(); a0=2*PI*sector/16; a1=2*PI*(sector+1)/16
    for band in range(20):
        r0=2.19+band*(R-2.19)/20; r1=2.19+(band+1)*(R-2.19)/20
        count=max(2,round((a1-a0)*(r0+r1)/2/1.45))
        for tile in range(count):
            aa=a0+(a1-a0)*tile/count+.0008; ab=a0+(a1-a0)*(tile+1)/count-.0008
            outer=r1-.025; inner=r0+.025; z=.008
            corners=[(inner*math.cos(aa),inner*math.sin(aa),z),(outer*math.cos(aa),outer*math.sin(aa),z),(outer*math.cos(ab),outer*math.sin(ab),z),(inner*math.cos(ab),inner*math.sin(ab),z)]
            mi=FLOOR if (band+tile)%7 else TRIM
            m.add(corners,[(0,1,2,3)],mi)
            if RNG.random()<.14:
                mid=(aa+ab)/2
                pts=[(rr*math.cos(mid+off),rr*math.sin(mid+off),z+.003) for rr,off in [(inner,0),((inner+outer)*.48,.004),((inner+outer)*.53,-.004),(outer,.005)]]
                tube(m,pts,.007,CRACK,3)
    # Fine radial brass spine and concentric triple-band border, embedded flush.
    for rad in [2.25,5.2,12.85,13.02,26.75,27.0,27.8]:
        tube(m,[(rad*math.cos(a0+(a1-a0)*j/20),rad*math.sin(a0+(a1-a0)*j/20),.014) for j in range(21)],.017 if rad<26 else .028,GOLD,4)
    tube(m,[(rr*math.cos(a0),rr*math.sin(a0),.013) for rr in [2.25,12.85,27.8]],.02,GOLD,4)
    assets[f'CA_FloorSector_{sector:02}']=m.object(f'CA_FloorSector_{sector:02}')

# Keeper dais: low and unobstructed, with an eight-point compass and engraved rim.
m=Mesh()
lathe(m,(0,0,0),[(0,2.15),(.08,2.15),(.12,2.09),(.26,2.09),(.31,2.16),(.36,2.16)],TRIM,128)
lathe(m,(0,0,0),[(.355,1.97),(.36,1.97)],FLOOR,128)
for rr in [1.02,1.76,1.90,2.10]: ring(m,rr,.365,.013,GOLD)
for k in range(16):
    a=k*PI/8; length=1.73 if k%2==0 else 1.12; w=.19 if k%2==0 else .12
    v=[(0,0,.366),(length*math.cos(a),length*math.sin(a),.366),(.50*math.cos(a)-w*math.sin(a),.50*math.sin(a)+w*math.cos(a),.366)]
    m.add(v,[(0,1,2)],GOLD)
    v=[(0,0,.367),(length*math.cos(a),length*math.sin(a),.367),(.50*math.cos(a)+w*math.sin(a),.50*math.sin(a)-w*math.cos(a),.367)]
    m.add(v,[(0,2,1)],DARK)
for k in range(64):
    a=k*2*PI/64
    tube(m,[(2.02*math.cos(a),2.02*math.sin(a),.369),(2.085*math.cos(a),2.085*math.sin(a),.369)],.012,GOLD,4)
assets['CA_KeeperDais']=m.object('CA_KeeperDais')

# Dome sector, lifted well clear of all gameplay rays.
m=Mesh()
for rad,z,w in [(27.85,11.87,.12),(25.6,13.5,.12),(19.7,16.20,.12),(9.0,18.3,.16)]:
    tube(m,[(rad*math.cos(PI*j/128),rad*math.sin(PI*j/128),z) for j in range(17)],w,TRIM,8)
for a in [0,PI/8]:
    tube(m,[(rr*math.cos(a),rr*math.sin(a),zz) for rr,zz in [(28,11.8),(26,13.3),(23,14.7),(19,16.4),(14,17.65),(9,18.3)]],.17,TRIM,8)
# Dark roof coffers, partially opened at the oculus.
rr=[28,26,23,19,14,9]; zz=[11.84,13.40,14.88,16.55,17.84,18.48]
for j in range(5):
    for k in range(6):
        a=PI/8*k/6; b=PI/8*(k+1)/6
        m.add([(rr[j]*math.cos(a),rr[j]*math.sin(a),zz[j]),(rr[j]*math.cos(b),rr[j]*math.sin(b),zz[j]),(rr[j+1]*math.cos(b),rr[j+1]*math.sin(b),zz[j+1]),(rr[j+1]*math.cos(a),rr[j+1]*math.sin(a),zz[j+1])],[(0,3,2,1)],DARK)
assets['CA_DomeSector']=m.object('CA_DomeSector')

# Delicate corner webs; no collision.
m=Mesh(); origin=Vector((0,0,0))
for i in range(9):
    a=PI/2*i/8; end=(math.cos(a)*.72,0,math.sin(a)*.72)
    tube(m,[(0,0,0),end],.0018,WEB,3)
for rad in [.13,.24,.37,.51,.67]:
    pts=[]
    for i in range(33):
        a=PI/2*i/32; sag=1-.08*math.sin((i%4)/4*PI)
        pts.append((rad*math.cos(a)*sag,-.006,rad*math.sin(a)*sag))
    tube(m,pts,.0014,WEB,3)
assets['CA_CornerWeb']=m.object('CA_CornerWeb')

# Export each reusable module. Geometry uses +Z up and -Y forward in Blender.
manifest=[]
for name,obj in assets.items():
    if name in ['CA_WeepingAngel','CA_PrayingAngel','CA_WarningAngel','CA_FallenVisage','CA_Reliquary','CA_BrokenPlinth','CA_FlutedColumn']:
        bm=bmesh.new(); bm.from_mesh(obj.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free();obj.data.update()
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
    if not name.startswith('CA_Floor'): bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_space_transform=True,path_mode='RELATIVE')
    tris=sum(len(p.vertices)-2 for p in obj.data.polygons)
    manifest.append({'name':name,'triangles':tris,'vertices':len(obj.data.vertices),'dimensions':list(obj.dimensions)})
(ART/'Models'/'CA_KitManifest.json').write_text(json.dumps(manifest,indent=2))

# Arrange the source scene with linked meshes from the existing gameplay layout.
kit=bpy.data.collections.new('00_ReusableKit'); scene.collection.children.link(kit)
for obj in assets.values():
    scene.collection.objects.unlink(obj); kit.objects.link(obj)
kit.hide_render=True; kit.hide_viewport=True

def instance(name,loc=(0,0,0),rot=0,scale=(1,1,1)):
    ob=bpy.data.objects.new(name,assets[name].data); scene.collection.objects.link(ob)
    ob.location=loc; ob.rotation_euler.z=rot; ob.scale=scale; return ob
layout=json.loads((ROOT/'tools/blender/crying_angels_layout.json').read_text())
for i,c in enumerate(sorted([x for x in layout if x['name'].startswith('Cover_')],key=lambda x:x['name'])):
    high=c['name'].endswith('High'); key=['CA_WeepingAngel','CA_PrayingAngel','CA_WarningAngel'][i%3] if high else ['CA_FallenVisage','CA_Reliquary','CA_BrokenPlinth'][i%3]
    p=c['position']; s=c['scale']; q=c['rotation']; yaw=2*math.atan2(q['y'],q['w'])
    ob=instance(key,(p['x'],p['z'],p['y']-s['y']/2),-yaw,(s['x'],s['z'],s['y']/(2.16 if high else .864)))
for i in range(16):
    a=2*PI*i/16
    instance('CA_ArchedWallBay',(28.5*math.cos(a),28.5*math.sin(a),-.10),a-PI/2)
    # columns between bays, outside the blocking wall plane
    ac=a+PI/16
    instance('CA_FlutedColumn',(29.2*math.cos(ac),29.2*math.sin(ac),-.10),ac)
    instance('CA_DomeSector',rot=a)
    instance(f'CA_FloorSector_{i:02}')
    if i%2==0:
        instance('CA_CornerWeb',(27.85*math.cos(a),27.85*math.sin(a),.13),a-PI/2,scale=(2,2,2))
instance('CA_KeeperDais')
# Black mortar substrate, just below the existing floor collider's surface (0).
m=Mesh(); lathe(m,(0,0,0),[(-.3,28.6),(-.005,28.6)],DARK,128); m.object('MortarUnderFloor')

def light(name,kind,loc,color,power,target=None,size=5):
    d=bpy.data.lights.new(name,kind); d.energy=power; d.color=color
    if kind=='AREA': d.shape='DISK'; d.size=size
    o=bpy.data.objects.new(name,d); scene.collection.objects.link(o); o.location=loc
    if target is not None: o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
    return o
for i in range(8):
    a=2*PI*i/8
    light('Moon_Window','AREA',(26*math.cos(a),26*math.sin(a),8.8),(.28,.53,1.0),1800 if math.cos(a)>.3 else 180,(11*math.cos(a),11*math.sin(a),0),5)
light('Oculus','AREA',(0,0,18),(.35,.52,1),700,(0,0,0),13)
lantern=light('KeeperLantern','SPOT',(0,-.25,1.7),(1,.67,.29),1900,(13,-22,.6),.15)
lantern.data.spot_size=.611; lantern.data.spot_blend=.18
# Atmospheric volume is presentation only; Unity has an inexpensive scene-local mist layer.
vol=bpy.data.materials.new('CA_PreviewAtmosphere'); vol.use_nodes=True
nt=vol.node_tree; nt.nodes.clear(); out=nt.nodes.new('ShaderNodeOutputMaterial'); pv=nt.nodes.new('ShaderNodeVolumePrincipled'); pv.inputs['Density'].default_value=.0035; pv.inputs['Color'].default_value=(.40,.52,.65,1); pv.inputs['Anisotropy'].default_value=.25; nt.links.new(pv.outputs['Volume'],out.inputs['Volume'])
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,0,8)); fog=bpy.context.object; fog.name='PreviewAtmosphere';fog.scale=(63,63,20);fog.data.materials.append(vol);fog.display_type='WIRE'
camd=bpy.data.cameras.new('CA_PresentationCamera'); cam=bpy.data.objects.new('CA_PresentationCamera',camd);scene.collection.objects.link(cam)
cam.location=(12,-20,2.25); target=Vector((-1,1,2.85));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();camd.lens=28;scene.camera=cam
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.clip_end=250
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'CA_MoonlitGallery.blend'))
print(json.dumps({'exported_modules':len(assets),'unique_triangles':sum(x['triangles'] for x in manifest),'source':str(SOURCE/'CA_MoonlitGallery.blend')}))
scene.render.filepath=str(ROOT/'docs/art/crying-angels/Blender_Gallery.png')
bpy.ops.render.render(write_still=True)
print('CA_BUILD_AND_RENDER_COMPLETE')
