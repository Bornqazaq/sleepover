"""Original fairground props, executed by circus_night.py in its mesh context.
All props face Blender -Y (Unity -Z); no bought geometry or bitmap sources.
"""

def star(m,p,r,ma=GOLD,depth=.025):
    verts=[]
    for y in (-depth,depth):
        for i in range(10):
            a=PI/2+i*PI/5;rr=r if i%2==0 else r*.43
            verts.append((p[0]+rr*math.cos(a),p[1]+y,p[2]+rr*math.sin(a)))
    # Explicit triangle fans avoid concave n-gon triangulation differences in FBX.
    verts.extend([(p[0],p[1]-depth,p[2]),(p[0],p[1]+depth,p[2])])
    faces=[(20,i,(i+1)%10) for i in range(10)]+[(21,(i+1)%10+10,i+10) for i in range(10)]
    faces += [(i,i+10,(i+1)%10+10,(i+1)%10) for i in range(10)]
    m.add(verts,faces,ma)

def wheel(m,p,r,ma=GOLD):
    # Wheel is in the XZ plane, with a real hub and eight spokes.
    arc(m,p,r,0,math.tau,.055,IRON,48)
    arc(m,(p[0],p[1]-.035,p[2]),r*.85,0,math.tau,.028,ma,40)
    ellipsoid(m,p,(.10,.09,.10),ma,12,8)
    for i in range(8):
        a=i*PI/4
        tube(m,[p,(p[0]+r*.89*math.cos(a),p[1],p[2]+r*.89*math.sin(a))],.025,ma)

def teddy(m,p,scale=1):
    def e(off,size,ma):
        ellipsoid(m,tuple(p[i]+off[i]*scale for i in range(3)),tuple(v*scale for v in size),ma,14,10)
    e((0,0,.32),(.23,.16,.30),WOOD2);e((0,-.04,.70),(.26,.18,.23),WOOD2)
    for s in (-1,1):
        e((s*.20,-.03,.87),(.085,.055,.085),WOOD2)
        e((s*.13,-.08,.07),(.105,.16,.09),WOOD)
        e((s*.26,-.01,.35),(.085,.10,.20),WOOD2)
        e((s*.09,-.207,.73),(.025,.020,.029),IRON)
    e((0,-.20,.64),(.12,.055,.075),CREAM);e((0,-.255,.68),(.042,.030,.027),IRON)
    star(m,(p[0],p[1]-.17*scale,p[2]+.32*scale),.085*scale,RED,.009*scale)

# Tiered wooden seating with warm brass end caps and structural cross braces.
m=Mesh()
for row in range(3):
    y=-.72+row*.72;z=.38+row*.39
    for j in range(3):box(m,(0,y+j*.17,z),(4.15,.16,.075),WOOD2,.015)
    for x in (-1.78,1.78):
        box(m,(x,y+.17,z*.5),(.10,.12,z),IRON,.012)
        box(m,(x,y+.12,z+.045),(.17,.58,.022),GOLD,.009)
    box(m,(0,y+.10,z-.14),(3.6,.12,.13),WOOD,.015)
for s in (-1,1):
    tube(m,[(s*2.03,-1.05,.22),(s*2.03,-1.05,.95),(s*2.03,1.15,2.03)],.05,IRON)
    for y in (-1.05,1.15):ellipsoid(m,(s*2.03,y,.95 if y<0 else 2.03),(.075,.075,.075),GOLD,10,6)
    tube(m,[(s*1.78,-.75,.15),(s*1.78,.75,1.1)],.045,IRON)
export(m,'CN_Bleachers')

# Booths have open counters, inset panels, visible joinery and canvas awnings.
for variant in range(2):
    m=Mesh();paint=RED if variant==0 else TEAL
    box(m,(0,.24,.53),(2.66,1.36,1.06),paint,.045)
    for x in (-.88,0,.88):
        box(m,(x,-.46,.56),(.75,.045,.73),WOOD,.025)
        for zz in (.21,.9):box(m,(x,-.50,zz),(.78,.035,.028),GOLD,.006)
        star(m,(x,-.50,.57),.17,GOLD)
    box(m,(0,-.06,1.08),(2.96,1.79,.15),WOOD2,.045)
    for x in (-1.33,1.33):
        box(m,(x,.76,1.91),(.14,.14,2.3),paint,.02)
        box(m,(x,-.55,1.91),(.11,.11,2.3),GOLD,.014)
    for z in (1.35,1.81,2.27):box(m,(0,.75,z),(2.48,.45,.075),WOOD2,.015)
    # Real bent strips, with a scalloped front edge.
    for i in range(12):
        x0=-1.54+i*3.08/12;x1=x0+3.08/12
        yz=[(1.08,2.92),(.58,3.24),(-.3,3.14),(-1.05,2.76)]
        vv=[(x,y,z) for y,z in yz for x in (x0,x1)]
        m.add(vv,[(j*2,j*2+2,j*2+3,j*2+1) for j in range(3)],STRIPE if (i+variant)%2==0 else IVORY)
        m.add([(x0,-1.05,2.76),(x1,-1.05,2.76),(x1,-1.06,2.57),((x0+x1)/2,-1.07,2.47),(x0,-1.06,2.57)],[(0,4,3,2,1)],STRIPE if (i+variant)%2==0 else IVORY)
    box(m,(0,-.77,2.33),(2.65,.12,.35),paint,.045)
    for x in (-1.14,1.14):star(m,(x,-.85,2.34),.105,CREAM)
    for i in range(13):ellipsoid(m,(-1.45+i*.242,-1.08,2.71),(.033,.035,.043),BULB,8,6)
    export(m,'CN_Stall_'+str(variant))

# Popcorn cart: glazed-frame kettle, striped tubs, spoked wheels, folding handles.
m=Mesh()
box(m,(0,0,.72),(1.55,.85,.92),RED,.045)
for x in (-.58,-.29,0,.29,.58):box(m,(x,-.438,.75),(.12,.035,.72),IVORY,.01)
for x in (-.85,.85):
    # Wheels parallel to the side panels.
    temp=Mesh();wheel(temp,(0,0,0),.40)
    m.add(temp.v,temp.f,IRON,transform((x,.04,.4),PI/2))
box(m,(0,0,1.21),(1.78,1.00,.12),GOLD,.025)
for x in (-.75,.75):
    for y in (-.36,.36):box(m,(x,y,1.73),(.048,.048,1.02),GOLD,.008)
lathe(m,(0,0,1.28),[(0,.38),(.17,.39),(.30,.28)],GOLD,28)
for i in range(75):
    a=rng.random()*math.tau;r=.3*math.sqrt(rng.random())
    ellipsoid(m,(r*math.cos(a),r*math.sin(a),1.49+rng.random()*.13),(.035,.03,.03),IVORY,7,5)
box(m,(0,0,2.22),(1.87,1.05,.13),RED,.05)
star(m,(0,-.55,2.20),.17,GOLD)
for s in (-1,1):tube(m,[(s*.56,.32,1.04),(s*.56,1.22,1.12)],.035,IRON)
export(m,'CN_PopcornCart')

# Prize shelf loaded with handmade teddy bears and juggling balls.
m=Mesh()
for z in (.18,.87,1.58):box(m,(0,.12,z),(2.2,.72,.095),WOOD2,.025)
for x in (-1.01,1.01):box(m,(x,.36,.83),(.10,.10,1.73),TEAL,.015)
for x in (-.72,0,.72):teddy(m,(x,-.10,.93),.60)
for x in (-.76,-.4,0,.4,.76):ellipsoid(m,(x,-.03,.38),(.16,.16,.16),(STRIPE,TEAL,GOLD)[int(abs(x)*10)%3],16,10)
export(m,'CN_PrizeShelf')

# Wheel with twelve enamel wedges, brass pegs and a sprung pointer.
m=Mesh();center=(0,0,1.65);rad=1.03
for i in range(12):
    a=i*math.tau/12;b=(i+1)*math.tau/12
    vv=[center]+[(rad*math.cos(a+(b-a)*j/8),-.035,1.65+rad*math.sin(a+(b-a)*j/8)) for j in range(9)]
    m.add(vv,[(0,j+1,j+2) for j in range(8)],(STRIPE,IVORY,TEAL,GOLD)[i%4])
    ellipsoid(m,(rad*math.cos(a),-.09,1.65+rad*math.sin(a)),(.043,.07,.043),GOLD,10,6)
    star(m,(.7*math.cos((a+b)/2),-.075,1.65+.7*math.sin((a+b)/2)),.09,CREAM,.01)
arc(m,(0,-.045,1.65),rad,0,math.tau,.047,GOLD,64)
ellipsoid(m,(0,-.10,1.65),(.18,.085,.18),GOLD,20,10)
star(m,(0,-.20,1.65),.12,TEAL)
box(m,(0,.15,.85),(.18,.20,1.7),IRON,.02)
for x in (-.65,.65):tube(m,[(0,.15,.5),(x,-.35,.08)],.075,IRON)
m.add([(-.10,-.11,2.9),(.10,-.11,2.9),(0,-.11,2.63)],[(0,2,1)],RED)
export(m,'CN_PrizeWheel')

# A circus balance pedestal, hoops and juggling clubs are recognisable silhouettes.
m=Mesh()
lathe(m,(0,0,0),[(0,.8),(.08,.86),(.60,.65),(.71,.71)],RED,48)
for z,r in [(.06,.85),(.62,.67),(.71,.72)]:ring(m,r,z,.035,GOLD)
for i in range(12):
    a=i*PI/6
    vv=[(.81*math.cos(a-.08),.81*math.sin(a-.08),.1),(.81*math.cos(a+.08),.81*math.sin(a+.08),.1),(.68*math.cos(a),.68*math.sin(a),.58)]
    m.add(vv,[(0,1,2)],IVORY)
export(m,'CN_BalancePedestal')
m=Mesh()
for i in range(3):
    a=(-.27+i*.27)
    matx=Matrix.Translation(Vector((-.30+i*.30,0,.08)))@Matrix.Rotation(a,4,'Y')
    lathe(m,(0,0,0),[(0,.065),(.10,.035),(.32,.038),(.45,.11),(.69,.13),(.80,.04)],(STRIPE,TEAL,GOLD)[i],20,matrix=matx)
arc(m,(0,.16,.76),.72,0,math.tau,.045,GOLD,48)
box(m,(0,.2,.045),(1.8,.7,.09),WOOD,.035)
export(m,'CN_JugglingSet')

# Touring trunks, crates and coils are small layout accents, not piles of noise.
m=Mesh();box(m,(0,0,.39),(1.3,.74,.78),TEAL,.07)
for x in (-.49,.49):
    for y in (-.385,.385):box(m,(x,y,.39),(.08,.045,.72),GOLD,.009)
    box(m,(x,0,.795),(.08,.72,.025),GOLD,.008)
for y in (-.389,.389):box(m,(0,y,.60),(1.28,.025,.03),GOLD,.006)
for x in (-.35,.35):box(m,(x,-.417,.57),(.1,.04,.16),GOLD,.012)
tube(m,[(-.18,-.425,.33),(-.15,-.49,.4),(.15,-.49,.4),(.18,-.425,.33)],.025,IRON)
export(m,'CN_TouringTrunk')
m=Mesh()
for z in (.07,.22,.37,.52):
    for y in (-.37,.37):box(m,(0,y,z),(.85,.07,.13),WOOD2,.008)
    for x in (-.41,.41):box(m,(x,0,z),(.07,.70,.13),WOOD,.008)
for x in (-.35,.35):
    for y in (-.31,.31):box(m,(x,y,.3),(.065,.065,.6),WOOD,.008)
for i in range(5):box(m,(-.33+i*.165,0,.018),(.15,.69,.035),WOOD2,.005)
export(m,'CN_Crate')
m=Mesh()
for i in range(4):ring(m,.22+i*.047,.025,.024,ROPE,64)
tube(m,[(.39,0,.03),(.48,.14,.027),(.58,.11,.027),(.64,.02,.027)],.024,ROPE)
export(m,'CN_RopeCoil')

# Balloon bouquet (original mesh, thin ties) and tapered candy tubs.
m=Mesh()
for i,p in enumerate([(-.26,0,1.8),(.14,.1,2.08),(.38,-.06,1.65),(-.2,.15,2.35),(0,-.21,2.18)]):
    ellipsoid(m,p,(.20,.20,.26),(STRIPE,IVORY,TEAL,GOLD,RED)[i],20,14)
    tube(m,[(0,0,.04),(p[0]*.3,p[1]*.3,.85),(p[0],p[1],p[2]-.26)],.008,ROPE,4)
lathe(m,(0,0,0),[(0,.18),(.14,.12)],GOLD,16)
export(m,'CN_Balloons')
m=Mesh()
for i in range(4):
    x=-.42+i*.28
    lathe(m,(x,0,0),[(0,.085),(.22,.125)],STRIPE if i%2==0 else IVORY,20)
    for j in range(14):
        a=rng.random()*math.tau;r=.10*math.sqrt(rng.random())
        ellipsoid(m,(x+r*math.cos(a),r*math.sin(a),.22+rng.random()*.05),(.028,.026,.025),IVORY,7,5)
export(m,'CN_PopcornTubs')

# Poster relief with our bear emblem; lettering is real Cyrillic TMP in Unity.
m=Mesh();box(m,(0,0,1.05),(1.32,.12,2.1),GOLD,.025)
box(m,(0,-.075,1.05),(1.18,.045,1.96),CREAM,.012)
box(m,(0,-.105,1.76),(1.12,.03,.42),RED,.008)
box(m,(0,-.105,.24),(1.12,.03,.25),TEAL,.008)
for i in range(24):
    a=i*math.tau/24;b=a+PI/35
    m.add([(0,-.113,.95),(.53*math.cos(a),-.115,.95+.58*math.sin(a)),(.53*math.cos(b),-.115,.95+.58*math.sin(b))],[(0,2,1)],STRIPE if i%2==0 else IVORY)
ellipsoid(m,(0,-.16,.99),(.29,.05,.29),WOOD2,24,16)
for x in (-.21,.21):ellipsoid(m,(x,-.17,1.23),(.105,.06,.10),WOOD2,16,10)
ellipsoid(m,(0,-.22,.90),(.16,.04,.10),IVORY,16,10)
ellipsoid(m,(0,-.26,.95),(.075,.028,.046),IRON,16,10)
for x in (-.10,.10):ellipsoid(m,(x,-.22,1.07),(.03,.02,.027),IRON,10,6)
export(m,'CN_BearPoster')

# Cage front: wide viewing opening, low safety rail, an open upper frame.
# This skin is authored to the measured cage dimensions; collision stays in Unity.
m=Mesh();half=1.48;H=2.88
for x in (-half,half):
    box(m,(x,half,H/2),(.14,.15,H),IRON,.02)
    for z in (.18,.68,H-.08):box(m,(x,half,z),(.20,.20,.06),GOLD,.01)
box(m,(0,half,.57),(3.02,.15,.10),IRON,.018)
box(m,(0,half,.61),(3.04,.17,.025),GOLD,.006)
for x in (-1.06,-.53,0,.53,1.06):
    tube(m,[(x,half,.07),(x,half,.53)],.027,IRON)
# Only perimeter roof rails; the centre and the front upper sightline are open.
for x in (-half,half):box(m,(x,0,H),(.16,3.06,.12),IRON,.02)
box(m,(0,-half,H),(3.08,.16,.12),IRON,.02)
for x in (-1.3,1.3):box(m,(x,half,H),(.45,.16,.12),IRON,.02)
# Two rear shoulder mounts keep both chains outside the central sightline.
for x in (-1.42,1.42):
    box(m,(x,-.78,H+.015),(.20,.23,.08),GOLD,.015)
    tube(m,[(x,-.78,H+.045),(x,-.78,H+.18)],.045,IRON)
for x in (-1.25,1.25):star(m,(x,half+.095,.31),.10,GOLD)
export(m,'CN_CageWindow')

# A segment of real interleaved chain; top origin, extends down 0.96 metres.
m=Mesh()
for i in range(8):
    points=[]
    for j in range(13):
        a=j*math.tau/12
        x=.048*math.cos(a);y=0;z=-.072-i*.117+.073*math.sin(a)
        points.append((x,y,z) if i%2==0 else (y,x,z))
    tube(m,points,.012,IRON,5)
export(m,'CN_ChainSegment')

# Stage projector body + hinged yoke, pointing +Z in Blender (Unity +Y).
m=Mesh()
lathe(m,(0,0,0),[(0,.16),(.06,.20),(.35,.20),(.44,.26),(.51,.26)],IRON,24)
lathe(m,(0,0,.495),[(0,.225),(.02,.225)],GOLD,24)
lathe(m,(0,0,.516),[(0,.19),(.005,.19)],BULB,24)
for x in (-.29,.29):
    box(m,(x,0,.15),(.065,.085,.55),IRON,.012)
    ellipsoid(m,(x,0,.22),(.065,.065,.065),GOLD,12,8)
box(m,(0,0,-.14),(.65,.08,.07),IRON,.012)
for i in range(4):
    a=i*PI/2
    box(m,(0,.26,.57),(.40,.025,.22),IRON,.01,Matrix.Rotation(a,4,'Z'))
export(m,'CN_StageProjector')

# Marquee lamps for all four existing score faces. Text and layout remain live.
m=Mesh()
for i in range(23):
    for z in (-1.17,1.17):
        x=-2.8+i*5.6/22
        ellipsoid(m,(x,-.04,z),(.040,.035,.040),BULB,10,6)
for x in (-2.92,2.92):
    for i in range(9):ellipsoid(m,(x,-.04,-.98+i*.245),(.04,.035,.04),BULB,10,6)
export(m,'CN_ScoreBulbs')

# Own shelf, bell and board fascia preserve existing gameplay measurements.
m=Mesh()
lathe(m,(0,0,0),[(0,.11),(.018,.12),(.035,.105),(.085,.084),(.105,.025)],GOLD,32)
lathe(m,(0,0,.10),[(0,.023),(.025,.023)],IRON,16)
ellipsoid(m,(0,0,.135),(.04,.04,.02),GOLD,16,10)
export(m,'CN_DeskBell')

m=Mesh()
box(m,(.126,1.20,.918),(2.412,.42,.065),WOOD2,.018)
for x in (-1.0,1.15):
    box(m,(x,1.30,.45),(.085,.085,.89),IRON,.015)
    box(m,(x,1.27,.81),(.13,.12,.13),GOLD,.012)
    tube(m,[(x,1.29,.58),(x,.99,.87)],.025,IRON)
box(m,(.126,1.40,.83),(2.36,.045,.09),WOOD,.012)
for x in (-1.025,1.27):ellipsoid(m,(x,1.408,.83),(.016,.012,.016),GOLD,8,6)
export(m,'CN_GameShelf')

# Subtle scraps of own paper and wood instead of bought litter meshes.
m=Mesh()
for i in range(90):
    a=rng.random()*math.tau;r=rng.uniform(10.2,16.4)
    x=r*math.cos(a);y=r*math.sin(a)
    box(m,(x,y,1.115),(rng.uniform(.05,.10),.055,.004),IVORY if i%3 else STRIPE,0)
export(m,'CN_PaperScatter')

# Touring-show details: stitched seat pads, a real tension drum and tickets.
m=Mesh()
for i in range(3):
    x=-1.18+i*1.18
    box(m,(x,0,.065),(.92,.48,.13),RED if i%2==0 else TEAL,.06)
    for y in (-.20,.20):tube(m,[(x-.39,y,.115),(x+.39,y,.115)],.009,GOLD,5)
    for dx in (-.22,.22):ellipsoid(m,(x+dx,0,.137),(.025,.025,.008),GOLD,8,6)
export(m,'CN_SeatPads')
m=Mesh()
lathe(m,(0,0,0),[(0,.43),(.08,.47),(.64,.47),(.69,.44)],RED,40)
lathe(m,(0,0,.68),[(0,.45),(.018,.45)],IVORY,40)
for z in (.08,.62):ring(m,.48,z,.035,GOLD,48)
for i in range(12):
    a=i*math.tau/12;b=a+math.tau/24
    tube(m,[(.49*math.cos(a),.49*math.sin(a),.59),(.49*math.cos(b),.49*math.sin(b),.13)],.014,ROPE,5)
for x,a in ((-.16,.4),(.16,-.4)):
    tube(m,[(x-.22,a,.72),(x+.22,-a,.76)],.021,WOOD2,7)
    ellipsoid(m,(x+.22,-a,.76),(.062,.068,.06),IVORY,12,8)
export(m,'CN_TouringDrum')
m=Mesh()
for x in (-.13,.13):
    lathe(m,(x,0,0),[(0,.115),(.14,.115)],IVORY,24)
    ringpart=Mesh();ring(ringpart,.112,.145,.013,RED,24);m.add(ringpart.v,ringpart.f,RED,transform((x,0,0),0))
for i in range(5):
    x=-.33+i*.14
    box(m,(x,-.24,.009),(.13,.20,.006),CREAM,.003)
    for y in (-.30,-.18):box(m,(x,y,.014),(.1,.018,.005),RED)
export(m,'CN_TicketRolls')
m=Mesh()
lathe(m,(0,0,0),[(0,.24),(.055,.28),(.12,.15)],GOLD,24)
tube(m,[(0,0,.1),(0,0,2.3)],.028,GOLD,8)
ellipsoid(m,(0,0,2.35),(.07,.07,.10),GOLD,12,8)
for i in range(6):
    x0=i*.12;x1=x0+.12
    z0=2.18-i*.015;z1=2.18-(i+1)*.015
    m.add([(x0,math.sin(i*.7)*.08,z0),(x1,math.sin((i+1)*.7)*.08,z1),(x1,math.sin((i+1)*.7)*.08,1.64+i*.08),(x0,math.sin(i*.7)*.08,1.64+(i-1)*.08)],[(0,3,2,1)],STRIPE if i%2==0 else IVORY)
export(m,'CN_ShowPennant')
# Shallow sawdust impressions at the perimeter. No raised obstacle in the run path.
m=Mesh()
for step in range(4):
    x=(-.25 if step%2 else .25);y=step*.40
    ellipsoid(m,(x,y,.006),(.125,.15,.004),WOOD,16,8)
    for toe in range(5):ellipsoid(m,(x+(toe-2)*.062,y+.17-abs(toe-2)*.015,.007),(.026,.037,.004),WOOD,10,6)
export(m,'CN_PawTrail')
