"""Original midway kit for the user's three carnival references. Run via Blender MCP.
Uses the project's authored mesh/export helpers, not external asset libraries.
"""
from pathlib import Path
helper = Path(__file__).with_name('duck_hunt_barn.py').read_text(encoding='utf-8').split('# Hero single-person cover:')[0]
helper = helper.replace('DH_Copperwood_Kit', 'DH_Carnival_Kit').replace('DuckHuntBarn', 'DuckHuntCarnival').replace("startswith('DHB_')", "startswith('DHC_')")
exec(compile(helper, __file__, 'exec'))
for key,col in {'Rose':(.68,.19,.27),'Blue':(.12,.40,.54),'Purple':(.46,.24,.59),'Gold':(.94,.61,.13)}.items():
    m=bpy.data.materials.get('DHB_'+key) or bpy.data.materials.new('DHB_'+key)
    m.diffuse_color=(*col,1)
    next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=(*col,1)
    mats[key]=m

def orb(name,p,s,mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=1,location=p)
    o=bpy.context.object;o.scale=s
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat)

def toy(kind,offset=(0,0,0),color='Rose',scale=1):
    start=len(parts)
    orb('Soft body',(0,0,.43),(.30,.22,.37),color)
    orb('Head',(0,-.015,.91),(.29,.23,.27),color)
    orb('Belly',(0,-.198,.44),(.21,.035,.25),'Cream')
    for s in (-1,1):
        orb('Paw',(s*.23,-.09,.12),(.16,.20,.12),color)
        orb('Arm',(s*.32,-.035,.52),(.13,.14,.22),color)
        if kind=='Rabbit':
            orb('Long ear',(s*.145,.015,1.29),(.092,.085,.31),color)
            orb('Ear lining',(s*.145,-.06,1.30),(.051,.015,.22),'Cream')
        else: orb('Round ear',(s*.235,0,1.10),(.105,.09,.105),color)
        orb('Button eye',(s*.095,-.235,.95),(.027,.014,.033),'Ink')
        orb('Eye glint',(s*.095-.009,-.247,.96),(.008,.005,.009),'Cream')
    orb('Muzzle',(0,-.235,.835),(.135,.055,.081),'Cream')
    orb('Nose',(0,-.285,.88),(.046,.02,.028),'Ink')
    box('Ribbon',(0,-.223,.68),(.10,.035,.048),'Gold')
    for s in (-1,1):
        bow=box('Bow',(s*.082,-.225,.68),(.11,.038,.09),'Red',.02);bow.rotation_euler.y=s*.3
    for o in parts[start:]:o.location=Vector(offset)+o.location*scale;o.scale*=scale

toy('Bear');export('DHC_Bear')
toy('Rabbit',color='Blue');export('DHC_Rabbit')
orb('Duck body',(0,0,.28),(.30,.40,.26),'Gold')
orb('Duck head',(0,-.25,.62),(.215,.205,.225),'Gold')
orb('Beak',(0,-.46,.59),(.18,.14,.065),'Red')
for s in (-1,1):
    orb('Wing',(s*.26,.03,.34),(.09,.24,.14),'Brass')
    orb('Eye',(s*.145,-.398,.69),(.028,.016,.035),'Ink')
export('DHC_Duck')

# One-person obstruction: reinforced fairground shipping crates, exact .89 x 1.92 footprint.
for z in (.48,1.40):
    box('Crate shadow',(0,0,z),(.81,.51,.87),'OakDark',.02)
    for i in range(5):box('Slat',(-.32+i*.16,-.275,z),(.15,.05,.84),'OakLight' if i%2 else 'Oak')
    for side in (-1,1):
        box('Side',(.417*side,0,z),(.025,.56,.86),'Oak')
        box('Corner rail',(.388*side,-.315,z),(.10,.06,.89),'OakLight')
    for h in (-.39,.39):box('Cross rail',(0,-.316,z+h),(.88,.06,.11),'OakLight')
    beam('Diagonal',(-.31,-.35,z-.31),(.31,-.35,z+.31),.075,'OakLight',.035)
    for x in (-.385,.385):
        for h in (-.39,.39):cyl('Bolt',(x,-.353,z+h),.015,.01,'Iron',(0,1,0),8,0)
box('Top lip',(0,0,1.89),(.89,.58,.06),'Red')
export('DHC_CrateCover')

# Exposed floor-mounted linkage. Pivot is exactly z=.78, not floating above a plinth.
box('Mounting skid',(0,0,.045),(.62,.59,.09),'OakDark',.025)
for x in (-.23,.23):
    beam('Splayed timber leg',(x,-.20,.09),(x*.5,0,.76),.105,'Oak')
    beam('Splayed timber leg',(x,.20,.09),(x*.5,0,.76),.105,'Oak')
    cyl('Pivot bearing',(x*.5,0,.78),.115,.07,'Iron',(1,0,0),16)
    for y in (-.2,.2):cyl('Anchor bolt',(x,y,.099),.025,.035,'Brass',verts=8)
cyl('Cross axle',(0,0,.78),.055,.36,'Brass',(1,0,0),16)
box('Cable drum',(0,.14,.40),(.26,.19,.25),'Iron',.025)
beam('Linkage',(0,.13,.42),(0,0,.75),.038,'Brass')
box('Instruction plate',(0,-.13,.52),(.28,.03,.18),'Red')
text('Action','E',(0,-.151,.46),.14)
export('DHC_LeverBase')
cyl('Pivot hub',(0,0,0),.087,.17,'Iron',(1,0,0),16)
beam('Steel handle',(0,0,0),(0,0,.52),.049,'Brass')
cyl('Red grip',(0,0,.54),.067,.34,'Red',(1,0,0),16)
for x in (-.175,.175):cyl('Grip end',(x,0,.54),.077,.025,'Brass',(1,0,0),16)
export('DHC_LeverArm')

# Six-metre string: real cable sag, bulbs and triangular cloth pennants.
for i in range(24):
    x=-3+i*.25;nx=x+.25
    z=-.42*(1-(x/3)**2);nz=-.42*(1-(nx/3)**2)
    beam('Cable',(x,0,z),(nx,0,nz),.013,'Iron')
    if i%2==0:
        cyl('Socket',(x,0,z-.06),.028,.09,'Iron',verts=8,bevel=0)
        orb('Bulb',(x,0,z-.145),(.062,.062,.085),'Glow')
    else:
        me=bpy.data.meshes.new('Pennant');me.from_pydata([(x-.11,0,z),(x+.11,0,z),(x,0,z-.34)],[],[(0,1,2),(2,1,0)])
        ob=bpy.data.objects.new('Pennant',me);scene.collection.objects.link(ob);finish(ob,'Pennant',('Red','Cream','Blue')[i%3])
export('DHC_Garland')

# Target wheel with separately coloured radial wedges and a proper rim/hub.
cyl('Target back',(0,.055,0),1,.13,'OakDark',(0,1,0),48)
for i in range(16):
    a=i*math.tau/16;b=(i+1)*math.tau/16
    me=bpy.data.meshes.new('Painted sector');me.from_pydata([(0,-.02,0),(.94*math.cos(a),-.02,.94*math.sin(a)),(.94*math.cos(b),-.02,.94*math.sin(b))],[],[(0,1,2),(2,1,0)])
    ob=bpy.data.objects.new('Painted sector',me);scene.collection.objects.link(ob);finish(ob,'Painted sector',('Red','Cream','Blue','Gold')[i%4])
for i in range(32):
    a=i*math.tau/32;orb('Rim stud',(.975*math.cos(a),-.045,.975*math.sin(a)),(.025,.015,.025),'Brass')
cyl('Centre',(0,-.045,0),.21,.09,'Brass',(0,1,0),24)
cyl('Bullseye',(0,-.10,0),.13,.03,'Red',(0,1,0),24)
export('DHC_Wheel')

for row in range(4):
    for i in range(4-row):
        x=(i-(3-row)/2)*.22;z=.13+row*.27
        cyl('Tin can',(x,0,z),.105,.25,'Cream',verts=12)
        cyl('Red paper band',(x,0,z),.107,.14,'Red',verts=12,bevel=0)
        for h in (-.122,.122):cyl('Tin rim',(x,0,z+h),.109,.012,'Iron',verts=12,bevel=0)
export('DHC_Cans')

for i,(x,y,z) in enumerate([(-.26,0,1.45),(.2,.1,1.60),(0,-.18,1.85),(.36,-.03,1.16),(-.40,.05,1.05)]):
    orb('Balloon',(x,y,z),(.22,.18,.29),('Red','Blue','Gold','Purple','Rose')[i])
    beam('String',(x,y,z-.25),(0,0,.05),.006,'Cream')
export('DHC_Balloons')

box('Popcorn cart body',(0,0,.71),(1.22,.65,.64),'Cream',.035)
for i in range(7):box('Red stripe',(-.51+i*.17,-.335,.71),(.085,.02,.60),'Red',.003)
for s in (-1,1):
    cyl('Wheel',(s*.66,0,.30),.29,.08,'OakDark',(1,0,0),24)
    cyl('Hub',(s*.71,0,.30),.065,.045,'Brass',(1,0,0),12)
    for i in range(8):
        a=i*math.tau/8;beam('Spoke',(s*.713,0,.30),(s*.713,.25*math.cos(a),.30+.25*math.sin(a)),.022,'Brass')
    for y in (-.25,.25):beam('Awning post',(s*.52,y,1),(s*.52,y,1.93),.034,'Brass')
for i in range(8):
    x=-.63+i*.18
    for s in (-1,1):
        beam('Striped canopy',(x,0,2.16),(x,s*.49,1.93),.175,'Red' if i%2 else 'Cream',.03)
    orb('Scalloped valance',(x,-.48,1.89),(.089,.028,.11),'Red' if i%2 else 'Cream')
box('Tray',(0,0,1.06),(1.30,.73,.10),'OakLight')
rng=random.Random(8)
for i in range(35):orb('Popcorn',(rng.uniform(-.48,.48),rng.uniform(-.21,.21),1.14+rng.uniform(0,.16)),(.07,.055,.06),'Gold' if i%6==0 else 'Cream')
export('DHC_PopcornCart')

# Flexible unit marquee; text stays separate and readable in Unity.
box('Painted sign',(0,.065,0),(1,.12,1),'Red',.075)
box('Inset',(0,-.01,0),(.88,.06,.88),'OakDark',.06)
export('DHC_Marquee')
orb('Frosted bulb',(0,0,0),(.10,.085,.10),'Glow')
cyl('Brass socket',(0,.07,0),.065,.07,'Brass',(0,1,0),12,0)
export('DHC_Bulb')

# Booth is back-of-house decoration, never a gameplay obstruction.
box('Booth',(0,0,1.05),(1.9,1.5,2.1),'OakDark')
for i in range(10):
    x=-.855+i*.19
    box('Front striped panel',(x,-.76,.48),(.185,.055,.92),'Red' if i%2 else 'Cream')
    box('Header striped panel',(x,-.76,1.93),(.185,.055,.36),'Red' if i%2 else 'Cream')
box('Ticket window',(0,-.80,1.36),(1.5,.04,.70),'Ink')
box('Counter',(0,-.92,.98),(2.03,.45,.10),'OakLight')
for s in (-1,1):box('Window jamb',(s*.82,-.83,1.36),(.12,.15,.78),'OakLight')
box('Roof',(0,0,2.20),(2.2,1.85,.18),'Red')
text('Tickets','TICKETS',(0,-.807,1.88),.19)
export('DHC_Booth')

# Small working stalls double as cover: no anonymous chest-high boxes.
box('Counter body',(0,0,.57),(1.13,.57,1.10),'OakDark',.025)
for i in range(7):box('Painted counter front',(-.48+i*.16,-.305,.60),(.152,.05,1.0),'Cream' if i%2 else 'Blue')
for x in (-.56,.56):box('Rounded corner',(x,-.30,.60),(.09,.08,1.06),'OakLight',.022)
box('Solid oak bar top',(0,0,1.15),(1.34,.79,.10),'OakLight',.035)
beam('Brass foot rail',(-.56,-.49,.23),(.56,-.49,.23),.035,'Brass')
for x in (-.43,.43):beam('Foot rail bracket',(x,-.49,.23),(x,-.29,.12),.025,'Brass')
cyl('Drink dispenser',(-.35,.08,1.36),.105,.29,'Teal',verts=16)
cyl('Dispenser lid',(-.35,.08,1.52),.12,.035,'Brass',verts=16)
beam('Tap',(-.35,-.02,1.29),(-.35,-.16,1.29),.025,'Brass')
for x in (.12,.33):
    cyl('Paper cup',(x,-.15,1.26),.07,.16,'Cream',verts=12)
    cyl('Cup band',(x,-.15,1.27),.071,.06,'Red',verts=12,bevel=0)
cyl('Serving tray',(.33,.18,1.21),.17,.02,'Brass',verts=20)
export('DHC_SnackBar')

box('Prize cabinet back',(0,.22,.98),(1.02,.08,1.96),'Blue',.025)
box('Storage plinth',(0,0,.17),(1.02,.57,.34),'OakDark',.025)
for side in (-1,1):box('Carved upright',(side*.49,-.02,1.05),(.075,.49,1.78),'OakLight',.018)
for z in (.35,.91,1.47):box('Prize shelf',(0,-.045,z),(1.08,.60,.075),'OakLight',.015)
for row in range(3):
    for i in range(2):toy('Bear' if (i+row)%2==0 else 'Rabbit',(-.255+i*.51,-.08,.39+row*.56),('Rose','Gold','TealLight')[row],.42)
box('Prize header',(0,-.05,1.92),(1.10,.62,.21),'Red',.03)
text('Prize label','PRIZES',(0,-.374,1.88),.14)
export('DHC_PrizeRack')

box('Ticket counter',(0,0,.59),(1.08,.67,1.12),'OakDark',.025)
for i in range(7):box('Ticket stripe',(-.465+i*.155,-.36,.60),(.145,.055,1.08),'Red' if i%2 else 'Cream')
box('Ticket sill',(0,-.07,1.17),(1.31,.84,.10),'OakLight',.03)
for s in (-1,1):box('Booth post',(s*.56,.15,1.6),(.07,.07,1.1),'Brass')
for i in range(8):
    x=-.61+i*.175
    roof=box('Canopy stripe',(x,-.02,2.13),(.172,1.0,.045),'Red' if i%2 else 'Cream');roof.rotation_euler.x=-.13
    orb('Valance',(x,-.5,2.03),(.087,.028,.10),'Red' if i%2 else 'Cream')
box('Till',(-.24,.02,1.33),(.37,.31,.23),'Teal',.035)
for x in (-.33,-.24,-.15):box('Till key',(x,-.15,1.37),(.045,.025,.04),'Cream',.008)
cyl('Ticket roll',(.30,.07,1.30),.09,.20,'Red',(1,0,0),16)
box('Tickets',(.30,-.12,1.235),(.16,.19,.008),'Cream',.001)
export('DHC_TicketStand')

for x in (-.43,.43):
    beam('Front easel',(x,-.25,.03),(x,0,1.90),.065,'OakDark')
    beam('Rear easel',(x,.40,.03),(x,0,1.90),.065,'OakDark')
box('Chalkboard backing',(0,-.10,1.00),(.97,.10,1.76),'OakLight',.024)
box('Chalk face',(0,-.158,1.0),(.85,.022,1.62),'Teal',.012)
text('Menu title','SNACKS',(0,-.176,1.54),.17)
for words,z,size in [('POPCORN',1.24,.11),('LEMONADE',.98,.10),('50c',.65,.19)]:text('Menu chalk',words,(0,-.176,z),size)
beam('Easel chain',(-.4,-.22,.48),(-.4,.29,.48),.018,'Brass')
export('DHC_MenuBoard')

cyl('Padded stool seat',(0,0,.62),.20,.10,'Red',verts=20,bevel=.014)
for i in range(4):
    a=i*math.tau/4+math.pi/4
    beam('Stool leg',(.17*math.cos(a),.17*math.sin(a),.03),(.13*math.cos(a),.13*math.sin(a),.58),.033,'OakDark')
export('DHC_Stool')

scene.render.engine='BLENDER_EEVEE'
scene.render.image_settings.file_format='PNG'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'DH_Carnival_Kit.blend'))
print('Carnival kit exported:',len(exported),'models')
