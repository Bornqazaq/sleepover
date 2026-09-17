"""Original Memory Run foundry kit, metres. Run in a separate background Blender.
Walking plates are identical; environment damage has an independent fixed seed.
"""
import bpy, bmesh, math, ast, json, sys
from pathlib import Path
from mathutils import Vector, Matrix
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/MemoryFoundry'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/MemoryFoundry'
for p in (ART/'Models', ART/'Textures', SOURCE): p.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.name = 'MemoryFoundry_OriginalKit'
PI = math.pi
materials, palette, exports = [], [], []

def mat(name, color, rough=.65, metal=0, texture='', emission=0):
    m=bpy.data.materials.new('MF_'+name); m.diffuse_color=(*color,1); m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Roughness'].default_value=rough; bs.inputs['Metallic'].default_value=metal
    if emission:
        bs.inputs['Emission Color'].default_value=(*color,1); bs.inputs['Emission Strength'].default_value=emission
    materials.append(m)
    palette.append(dict(name=m.name,color=[*color,1],roughness=rough,metallic=metal,texture=texture,emission=emission))
    return len(materials)-1

STEEL=mat('Steel',(.52,.56,.56),.43,.55,'Steel')
IRON=mat('Iron',(.13,.17,.18),.47,.65,'Steel')
DARK=mat('Recess',(.032,.044,.046),.85)
BRICK=mat('Brick',(.48,.32,.245),.94,0,'Masonry')
STONE=mat('Concrete',(.44,.46,.43),.88,0,'Masonry')
YELLOW=mat('SafetyOchre',(.90,.59,.14),.52,.3,'Steel')
RED=mat('Vermilion',(.60,.095,.06),.58,.2,'Steel')
TEAL=mat('Enamel',(.13,.32,.29),.48,.35,'Steel')
BRASS=mat('Brass',(.60,.40,.19),.34,.7)
WOOD=mat('CrateWood',(.38,.255,.13),.86,0,'Wood')
IVORY=mat('Lettering',(.87,.86,.72),.7)
GLASS=mat('Window',(.40,.64,.82),.3,.1,emission=1.0)
BULB=mat('Opal',(.99,.68,.28),.3,emission=4)
GREEN=mat('Exit',(.15,.9,.36),.3,emission=3)
SIGNAL=mat('Signal',(.38,.92,1),.25,emission=2.8)
GOLD=BRASS

# Reuse our own bevel/lathe/UV mesh helpers, without running their scene generator.
for node in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
    if isinstance(node,(ast.ClassDef,ast.FunctionDef)) and node.name in {'Mesh','transform','box','lathe','tube','arc','ring'}:
        exec(compile(ast.Module(body=[node],type_ignores=[]),'own_geometry','exec'))
_box_cache={}

def export(m,name):
    obj=m.object('MF_'+name)
    used=sorted(set(m.m)); remap={old:i for i,old in enumerate(used)}
    obj.data.materials.clear()
    for idx in used: obj.data.materials.append(materials[idx])
    for poly,idx in zip(obj.data.polygons,m.m): poly.material_index=remap[idx]
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'MF_{name}.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
    exports.append(dict(name=name,vertices=len(m.v),faces=len(m.f)))
    return obj

def bolt(m,x,y,z,r=.045,ma=STEEL):
    lathe(m,(x,y,z),[(0,r),(.025,r)],ma,6)

def ibeam(m,x,y,z,height,width=.35):
    box(m,(x,y,z+height/2),(.10,width,height),IRON,.012)
    for xx in (-width/2,width/2): box(m,(x+xx,y,z+height/2),(.07,width+.13,height),IRON,.012)
    for zz in (z+.08,z+height-.08):
        box(m,(x,y,zz),(width+.35,width+.35,.13),STEEL,.012)

def pipe(m,points,r=.18,ma=IRON): tube(m,points,r,ma,16)

def flange(m,x,y,z,r=.28,ma=IRON):
    lathe(m,(x,y,z),[(0,r),(.10,r)],ma,20)
    for j in range(8):
        a=j*PI/4;bolt(m,x+math.cos(a)*r*.77,y+math.sin(a)*r*.77,z+.1,.035)

def rail(m,length,z=0,y=0):
    for x in (-length/2,0,length/2):
        box(m,(x,y,z+.55),(.07,.07,1.1),YELLOW,.01)
        box(m,(x,y,z+.04),(.19,.18,.08),IRON,.01)
    for h in (.43,1.08):pipe(m,[(-length/2,y,z+h),(length/2,y,z+h)],.032,YELLOW)
    box(m,(0,y,z+.10),(length,.055,.14),YELLOW,.01)

def lamp(m,x,y,z):
    pipe(m,[(x,y,z),(x,y,z-.65)],.035,IRON)
    lathe(m,(x,y,z-1.1),[(0,.44),(.07,.44),(.40,.15),(.50,.12)],TEAL,24)
    lathe(m,(x,y,z-1.16),[(0,.15),(.08,.21),(.18,.18)],BULB,20)

# Prototype: a single precise, identical walking surface, all details below y=0.
m=Mesh()
box(m,(0,0,-.19),(2.88,2.88,.34),IRON,.065)
box(m,(0,0,-.071),(2.62,2.62,.11),STEEL,.027)
for s in (-1,1):
    box(m,(s*1.375,0,-.055),(.13,2.85,.09),STEEL,.018)
    box(m,(0,s*1.375,-.055),(2.62,.13,.09),STEEL,.018)
    for a in (-1.20,0,1.20):bolt(m,s*1.375,a,-.033,.043)
    for a in (-1.20,0,1.20):bolt(m,a,s*1.375,-.033,.043)
for ix in range(-7,8):
    for iy in range(-7,8):
        x=ix*.165;y=iy*.165
        box(m,(0,0,-.007),(.095,.022,.014),STEEL,.003,
            Matrix.Translation(Vector((x,y,0))) @ Matrix.Rotation((1 if (ix+iy)%2 else -1)*PI/4,4,'Z'))
export(m,'Plate')

if '--prototype' not in sys.argv:
    # Wall module: brick piers, recessed blue windows, steel buttresses, deep shaft.
    m=Mesh()
    box(m,(0,.26,-6),(4.32,.50,50),STONE,.015)
    for z in range(0,19):
        for j in range(5):
            x=-2.16+(j+.5)*.864
            if 6<z<15 and abs(x)<1.32:continue
            box(m,(x,-.025,z+.47),(.835,.10,.92),BRICK,.015)
    box(m,(0,-.09,10.55),(2.62,.12,8.15),IRON,.035)
    for ix in (-1,0,1):
        for iz in range(5):box(m,(ix*.79,-.165,7.0+iz*1.46),(.70,.025,1.33),GLASS,.008)
    for x in (-1.36,1.36):box(m,(x,-.19,10.5),(.17,.25,8.5),STONE,.025)
    for z in (6.23,14.78):box(m,(0,-.19,z),(2.87,.28,.18),STONE,.025)
    ibeam(m,2.0,-.31,-30,49,.34)
    for z in (-27,-20,-13,-6,1,5,16.5):
        box(m,(0,-.10,z),(4.32,.24,.27),IRON,.02)
        for x in (-1.2,0,1.2):box(m,(x,-.045,z-1.2),(1.1,.025,1.8),DARK,.02)
    # Gallery sits far outside the jump corridor; upper and deep lower levels.
    for z in (-21,-12,-3,3.2):
        box(m,(0,-1.05,z),(4.32,1.65,.23),IRON,.025)
        rail(m,4.32,z+.13,-1.8)
        for x in (-1.7,1.7):pipe(m,[(x,-.3,z-1.9),(x,-1.65,z-.15)],.085,IRON)
    pipe(m,[(-2.16,-.7,2),(2.16,-.7,2)],.27,IRON)
    pipe(m,[(-2.16,-.5,-8),(2.16,-.5,-8)],.42,TEAL)
    pipe(m,[(1.2,-.50,-30),(1.2,-.50,18)],.18,IRON)
    for z in (-25,-17,-9,1,9,16):flange(m,1.2,-.5,z,.30)
    lamp(m,0,-2.3,5.5)
    export(m,'WallBay')

    m=Mesh()
    for y in (-1.9,1.9):
        for z in (0,1.5):box(m,(0,y,z),(36,.20,.22),IRON,.015)
        for i in range(12):
            x=-18+i*3;pipe(m,[(x,y,0),(x+3,y,1.5)],.065,STEEL)
            pipe(m,[(x,y,1.5),(x+3,y,0)],.065,IRON)
    for x in (-15,-9,0,9,15):box(m,(x,0,1.35),(.22,4.32,.24),IRON,.015)
    box(m,(0,0,1.8),(36,4.32,.25),IRON,.02)
    for x in (-8,8):lamp(m,x,0,0)
    export(m,'RoofBay')

    m=Mesh()
    # Keep the structural top below the tread sheet, not coplanar with it.
    box(m,(0,0,-.125),(3,3,.15),IRON,.025)
    box(m,(0,0,-.025),(2.92,2.92,.05),STEEL,.01)
    for x in (-1.40,1.40):
        for y in (-1.40,1.40):bolt(m,x,y,-.016)
    # The repeated chequer texture is geometric in close-up, no purchased atlas.
    for i in range(-6,7):
        for j in range(-6,7):box(m,(i*.21,j*.21,.002),(.11,.018,.008),IRON,0,
            Matrix.Rotation(0,4,'Z'))
    export(m,'Deck')

    m=Mesh();rail(m,3);export(m,'Railing')
    m=Mesh()
    box(m,(0,0,0),(3,.55,.08),YELLOW,.01)
    for i in range(6):
        x=-1.36+i*.50
        m.add([(x,-.27,.045),(x+.22,-.27,.045),(min(1.49,x+.46),.27,.045),(x+.24,.27,.045)],[(0,1,2,3)],DARK)
    export(m,'HazardEdge')

    m=Mesh()
    lathe(m,(0,0,.05),[(0,.43),(.12,.47),(.25,.49),(1.12,.49),(1.30,.45),(1.35,.43)],RED,24)
    for z in (.18,.48,1.06,1.33):lathe(m,(0,0,z),[(0,.50),(.06,.50)],IRON,24)
    flange(m,.19,0,1.41,.075,BRASS)
    box(m,(0,-.486,.79),(.57,.016,.38),IVORY,.008)
    export(m,'Drum')

    m=Mesh()
    for j in range(5):
        z=.12+j*.22
        for y in (-.5,.5):box(m,(0,y,z),(1.45,.10,.195),WOOD,.02)
        for x in (-.70,.70):box(m,(x,0,z),(.10,.94,.195),WOOD,.02)
    box(m,(0,0,.06),(1.5,1.10,.12),WOOD,.02)
    for x in (-.59,.59):
        for y in (-.53,.53):box(m,(x,y,.55),(.11,.08,1.18),IRON,.013)
    for j in range(7):
        x=-.52+(j%4)*.33;y=-.28+(j//4)*.44
        lathe(m,(x,y,.2),[(0,.115),(.87,.115),(.92,.10)],RED,12)
        pipe(m,[(x,y,1.12),(x+.02,y,1.23),(x+.13,y,1.25)],.014,DARK)
    export(m,'DynamiteCrate')

    m=Mesh()
    lathe(m,(0,0,.12),[(0,1.15),(.18,1.15),(.35,.91),(4.5,.91),(4.8,.72),(5.0,.24)],TEAL,32)
    for z in (.30,1.3,3.8,4.55):flange(m,0,0,z,1.03)
    pipe(m,[(0,0,5.05),(0,0,6.1),(.65,0,6.75),(2,0,6.75)],.24,IRON)
    for z in (.7,1.1,1.5,1.9,2.3,2.7,3.1,3.5,3.9):
        pipe(m,[(-.34,-1.1,z),(.34,-1.1,z)],.035,STEEL)
    for x in (-.37,.37):pipe(m,[(x,-1.1,.5),(x,-1.1,4.1)],.04,IRON)
    for x in (-.65,.65):box(m,(x,0,.08),(.28,1.5,.16),IRON,.02)
    box(m,(0,-.94,2.4),(.35,.10,.55),BRASS,.02)
    export(m,'PressureVessel')

    m=Mesh()
    box(m,(0,0,1.05),(2.2,.60,2.1),TEAL,.075)
    for x in (-.69,0,.69):
        box(m,(x,-.325,1.25),(.59,.07,1.54),IRON,.025)
        box(m,(x,-.37,1.65),(.40,.018,.31),IVORY,.01)
        for z in (.70,.90,1.10):box(m,(x,-.38,z),(.36,.035,.045),BRASS,.01)
        pipe(m,[(x,0,2.1),(x,0,2.6),(x+.2,0,2.8)],.045,IRON)
    export(m,'Switchboard')

    m=Mesh()
    for y in (-.48,.48):
        lathe(m,(0,0,0),[(0,.78),(.12,.78)],WOOD,24,
              matrix=Matrix.Translation(Vector((0,y,.8))) @ Matrix.Rotation(PI/2,4,'X'))
    lathe(m,(0,0,0),[(0,.48),(.85,.48)],DARK,24,
          matrix=Matrix.Translation(Vector((0,.43,.8))) @ Matrix.Rotation(PI/2,4,'X'))
    for i in range(14):
        y=-.4+i*.06
        pipe(m,[(.49*math.cos(a*PI/16),y,.8+.49*math.sin(a*PI/16)) for a in range(33)],.025,IRON)
    export(m,'CableReel')

    m=Mesh()
    for x in (-3.2,3.2):ibeam(m,x,0,0,6.8,.55)
    box(m,(0,0,6.7),(7.1,.8,.9),YELLOW,.07)
    for x in range(-3,4):box(m,(x,-.416,6.7),(.32,.025,.7),DARK,.01)
    box(m,(1.8,0,6.1),(1.1,1.1,.35),IRON,.04)
    for x in (1.55,2.05):pipe(m,[(x,0,6),(x,0,3.1)],.027,IRON)
    lathe(m,(1.8,0,2.8),[(0,.27),(.5,.27)],YELLOW,20)
    pipe(m,[(1.8,0,2.8),(1.8,0,2.45),(1.98,0,2.25),(2.2,0,2.40)],.10,IRON)
    export(m,'Gantry')

    m=Mesh()
    box(m,(0,.12,2.1),(5.4,.5,4.2),IRON,.075)
    for x in (-1.16,1.16):
        box(m,(x,-.18,1.96),(2.2,.14,3.78),TEAL,.06)
        for z in (.7,3.2):box(m,(x,-.275,z),(1.87,.045,.12),BRASS,.01)
        pipe(m,[(x-.83,-.29,.55),(x+.83,-.29,3.37)],.065,IRON)
    for x in (-2.63,2.63):
        for z in (.3,1,1.7,2.4,3.1,3.8):box(m,(x,-.17,z),(.20,.06,.25),YELLOW,.018)
    box(m,(0,-.16,4.5),(2.9,.30,.55),IRON,.04)
    box(m,(0,-.33,4.50),(2.66,.04,.38),GREEN,.02)
    export(m,'ExitPortal')

    m=Mesh()
    box(m,(0,0,0),(4.8,.14,3.2),IRON,.035)
    box(m,(0,-.08,0),(4.58,.025,2.98),RED,.018)
    for x in (-2.18,2.18):
        for z in (-1.35,1.35):box(m,(x,-.11,z),(.08,.035,.08),BRASS,.01)
    export(m,'PosterFrame')

    # Side production line: fixed chassis, independent tread links and cargo.
    m=Mesh()
    for x in (-.89,.89):
        box(m,(x,0,-.23),(.18,4.32,.48),TEAL,.035)
        box(m,(x,0,.04),(.10,4.32,.10),YELLOW,.015)
        for y in (-1.7,0,1.7):
            box(m,(x,y,-.75),(.16,.18,1.0),IRON,.015)
            box(m,(x,y,-1.27),(.45,.44,.13),STEEL,.025)
            box(m,(x*1.105,y,-.22),(.045,.20,.20),BRASS,.015)
        for y in (-1.3,.85):box(m,(x*1.11,y,-.22),(.025,.8,.28),YELLOW,.01)
    for y in (-1.8,0,1.8):box(m,(0,y,-.55),(1.9,.13,.14),IRON,.02)
    for x in (-1.05,1.05):pipe(m,[(x,-2.16,-.60),(x,2.16,-.60)],.06,IRON)
    export(m,'ConveyorBed')

    m=Mesh()
    box(m,(0,0,-.035),(1.54,.43,.07),STEEL,.018)
    box(m,(0,0,.01),(1.42,.35,.025),STEEL,.009)
    for x in (-.72,.72):box(m,(x,0,.028),(.09,.40,.07),STEEL,.009)
    for x in (-.47,.47):box(m,(x,0,.027),(.045,.29,.016),STEEL,.005)
    export(m,'BeltLink')

    m=Mesh()
    # Roller axle runs across the belt; axis conversion is baked during import.
    roll=Matrix.Rotation(PI/2,4,'Y')
    lathe(m,(0,0,0),[(-.91,.30),(-.78,.32),(.78,.32),(.91,.30)],IRON,20,matrix=roll)
    for x in (-.86,.86):
        lathe(m,(0,0,0),[(x-.045,.35),(x+.045,.35)],YELLOW,20,matrix=roll)
    export(m,'ConveyorRoller')

    m=Mesh()
    for x,z in [(-.26,.15),(0,.15),(.26,.15),(-.13,.38),(.13,.38)]:
        axis=Matrix.Translation(Vector((x,-.48,z))) @ Matrix.Rotation(-PI/2,4,'X')
        lathe(m,(0,0,0),[(0,.115),(.90,.115),(.96,.10)],RED,12,matrix=axis)
        pipe(m,[(x,.48,z),(x+.04,.55,z+.07),(x+.07,.66,z+.09)],.017,WOOD)
    for y in (-.25,.25):
        box(m,(0,y,.49),(.59,.095,.05),IRON,.01)
        box(m,(0,y,.025),(.75,.095,.05),IRON,.01)
        for x in (-.37,.37):box(m,(x,y,.24),(.055,.095,.44),IRON,.01)
    box(m,(0,-.20,.532),(.36,.16,.012),IVORY,.005)
    export(m,'DynamiteBundle')

    m=Mesh()
    box(m,(0,0,1.0),(2.3,2.1,.20),TEAL,.06)
    for x in (-1.07,1.07):
        box(m,(x,0,.36),(.19,2.1,1.12),TEAL,.04)
        box(m,(x,-1.08,.36),(.20,.08,1.12),YELLOW,.015)
    for y in (-1.02,1.02):box(m,(0,y,.96),(2.1,.10,.16),YELLOW,.02)
    box(m,(0,0,-.56),(2.2,2.1,.17),IRON,.035)
    for x in (-.6,0,.6):box(m,(x,-1.10,1.0),(.13,.035,.13),DARK,.01)
    box(m,(.0,0,1.18),(.55,.58,.18),IRON,.03)
    lathe(m,(0,0,1.27),[(0,.16),(.23,.16),(.29,.10)],BULB,16)
    box(m,(1.26,0,.2),(.3,.7,.6),IRON,.025)
    for y in (-.24,0,.24):box(m,(1.43,y,.2),(.045,.05,.43),STEEL,.008)
    export(m,'LoadingHood')

    # Convex industrial badge with a separately modelled downward arrow.
    def emblem(points,depth,ma,y=0):
        n=len(points);verts=[(x,y+d,z) for d in (-depth/2,depth/2) for x,z in points]
        faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
        faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        m.add(verts,faces,ma)
    m=Mesh()
    outline=[(-.53,.52),(.53,.52),(.63,.36),(.52,-.20),(0,-.66),(-.52,-.20),(-.63,.36)]
    emblem(outline,.16,IRON)
    emblem([(x*.90,z*.90) for x,z in outline],.035,YELLOW,-.10)
    emblem([(x*.77,z*.77) for x,z in outline],.025,TEAL,-.13)
    # Stem and triangle overlap in volume, never coplanar faces.
    box(m,(0,-.17,.17),(.17,.04,.38),SIGNAL,.018)
    emblem([(-.30,.01),(.30,.01),(0,-.36)],.045,SIGNAL,-.185)
    for x in (-.42,.42):box(m,(x,-.145,.36),(.085,.025,.085),STEEL,.012)
    for x in (-.24,0,.24):box(m,(x,-.16,.44),(.105,.018,.035),BULB,.006)
    export(m,'TurnSignal')

if '--prototype' not in sys.argv:
    exec(compile((ROOT/'tools/blender/memory_foundry_ruins.py').read_text(), 'memory_foundry_ruins.py', 'exec'))

# Periodic original textures and alpha particles, saved and packed in the source.
N=512;u,v=np.meshgrid(np.arange(N)/N,np.arange(N)/N)
noise=np.random.default_rng(160926).random((N,N))
maps={'Steel':.88+.07*noise+.018*np.sin(u*PI*180),
      'Masonry':.83+.12*noise+.025*np.sin(u*PI*16)*np.cos(v*PI*12),
      'Wood':.78+.08*noise+.09*np.sin(u*PI*32+np.sin(v*PI*4)),
      'Rust':.62+.20*noise+.12*np.sin(u*PI*14+np.sin(v*PI*8))*np.cos(v*PI*10)}
for name,data in maps.items():
    rgba=np.ones((N,N,4),dtype=np.float32);rgba[:,:,:3]=data[:,:,None]
    im=bpy.data.images.new('MF_'+name,width=N,height=N);im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw=str(ART/'Textures'/f'MF_{name}.png');im.file_format='PNG';im.save()
    for index,e in enumerate(palette):
        if e['texture']!=name:continue
        nodes=materials[index].node_tree;tex=nodes.nodes.new('ShaderNodeTexImage');tex.image=im
        mult=nodes.nodes.new('ShaderNodeMixRGB');mult.blend_type='MULTIPLY';mult.inputs[0].default_value=1;mult.inputs[2].default_value=materials[index].diffuse_color
        nodes.links.new(tex.outputs['Color'],mult.inputs[1]);nodes.links.new(mult.outputs[0],next(n for n in nodes.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'])
r=np.sqrt((u-.5)**2+(v-.5)**2)
# Clear centre, subtle diagonal reflections, denser laminated edges.
edge=np.maximum(np.exp(-np.minimum(u,1-u)*65),np.exp(-np.minimum(v,1-v)*45))
reflection=np.exp(-((u*.7+v-.86)/.055)**2)+.5*np.exp(-((u*.7+v-1.04)/.02)**2)
rgba=np.ones((N,N,4),dtype=np.float32)
rgba[:,:,3]=np.clip(.12+.35*edge+.075*reflection,.12,.65)
im=bpy.data.images.new('MF_Glass',width=N,height=N,alpha=True);im.pixels.foreach_set(rgba.ravel())
im.filepath_raw=str(ART/'Textures/MF_Glass.png');im.file_format='PNG';im.save()
for name,alpha in {'Puff':np.clip(1-r/.49,0,1)**2,'Ring':np.exp(-((r-.36)/.028)**2)*np.clip((.5-r)*15,0,1)}.items():
    rgba=np.ones((N,N,4),dtype=np.float32);rgba[:,:,3]=alpha
    im=bpy.data.images.new('MF_'+name,width=N,height=N,alpha=True);im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(ART/'Textures'/f'MF_{name}.png');im.file_format='PNG';im.save()
# A lit, scalloped smoke sprite for blasts; the shaft keeps the soft mist map.
rgba=np.zeros((N,N,4),dtype=np.float32)
for cx,cy,rad in [(.33,.35,.23),(.66,.39,.23),(.50,.68,.24),(.34,.61,.23),(.64,.61,.23),(.50,.48,.30)]:
    dx=(u-cx)/rad;dy=(v-cy)/rad;rr=dx*dx+dy*dy
    alpha=np.clip((1-rr)*22,0,1)
    shade=np.clip(.52+.35*np.sqrt(np.clip(1-rr,0,1))-.17*dx+.20*dy,.30,1)
    for channel in range(3):rgba[:,:,channel]=np.where(alpha>0,shade*alpha+rgba[:,:,channel]*(1-alpha),rgba[:,:,channel])
    rgba[:,:,3]=np.maximum(rgba[:,:,3],alpha)
im=bpy.data.images.new('MF_Smoke',width=N,height=N,alpha=True);im.pixels.foreach_set(rgba.ravel())
im.filepath_raw=str(ART/'Textures/MF_Smoke.png');im.file_format='PNG';im.save()
# Soft tapered tongues, authored procedurally for small environmental fires.
width=.32*(1-v)**.65+.015
centre=.5+.055*np.sin(v*17)*(v+.2)
alpha=np.clip(1-np.abs(u-centre)/width,0,1)**1.5*np.sin(np.clip(v*1.07,0,1)*PI)**.65
rgba=np.ones((N,N,4),dtype=np.float32);rgba[:,:,3]=alpha
im=bpy.data.images.new('MF_Flame',width=N,height=N,alpha=True);im.pixels.foreach_set(rgba.ravel())
im.filepath_raw=str(ART/'Textures/MF_Flame.png');im.file_format='PNG';im.save()
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'MemoryFoundryKit.blend'))
(ART/'palette.json').write_text(json.dumps({'materials':palette},indent=2))
(ART/'geometry.json').write_text(json.dumps(exports,indent=2))
print('MEMORY_FOUNDRY_EXPORT_COMPLETE',flush=True)
