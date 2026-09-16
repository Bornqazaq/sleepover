"""Geometry supplement executed in exam_hall.py's material/mesh context."""
# Room shell is authored to config metres: 19.44 x 20.16, height 6.84.
W=19.44;D=20.16;H=6.84;PZ=2.88
# Floor quadrants: real individually jointed parquet blocks, no mesh over the pit.
for sector,(x0,x1,y0,y1) in enumerate([(-9.52,9.52,-9.88,.72),(-9.52,9.52,5.04,9.88),(-9.52,-6.48,.72,5.04),(6.48,9.52,.72,5.04)]):
    m=Mesh();box(m,((x0+x1)/2,(y0+y1)/2,-.012),(x1-x0,y1-y0,.025),WOOD,0)
    # Basket weave parquet with alternating grain direction and a narrow, dark joint.
    tile=.72
    nx=math.ceil((x1-x0)/tile);ny=math.ceil((y1-y0)/tile)
    for ix in range(nx):
        for iy in range(ny):
            xx=x0+ix*tile;yy=y0+iy*tile;sw=min(tile,x1-xx);sd=min(tile,y1-yy)
            horizontal=(ix+iy)%2==0
            for strip in range(3):
                sx=sw if horizontal else sw/3;sy=sd/3 if horizontal else sd
                x=xx+sw/2 if horizontal else xx+(strip+.5)*sw/3
                y=yy+(strip+.5)*sd/3 if horizontal else yy+sd/2
                box(m,(x,y,.007),(sx-.006,sy-.006,.018),[OAK,OAK,OAK2,OAK][rng.randrange(4)],0)
    export(m,'Parquet_'+str(sector))
# Wainscot walls, pilasters, layered cornices and window recesses.
for side in (-1,1):
    m=Mesh();x=side*9.48
    for y in [-9.4+i*1.175 for i in range(17)]:
        box(m,(x,y,.80),(.15,1.13,1.6),WOOD,.014)
        box(m,(x-side*.09,y,.87),(.06,.94,1.17),OAK,.012)
        box(m,(x-side*.13,y,.87),(.02,.80,1.02),WOOD,.008)
    for z,depth,height,ma in [(.12,.30,.20,WOOD),(1.62,.26,.12,OAK),(1.71,.30,.055,GOLD),(5.99,.20,.18,STONE),(6.18,.32,.14,WOOD),(6.35,.46,.16,OAK),(6.50,.53,.10,GOLD)]:
        box(m,(x-side*depth*.2,0,z),(depth,D-.25,height),ma,.015)
    for y in (-8.0,-4,0,4,8):
        box(m,(x-side*.10,y,3.78),(.31,.45,4.1),STONE,.025)
        for z,sy,sz in [(1.8,.68,.18),(2.0,.53,.13),(5.67,.56,.17),(5.82,.75,.17)]:box(m,(x-side*.17,y,z),(.44,sy,sz),STONE,.02)
        for yy in (-.12,0,.12):box(m,(x-side*.27,y+yy,3.8),(.03,.026,3.3),OAK,.006)
    export(m,'SideArchitecture_'+str(side))
# Front and rear panelled wall; front has a broad classical pediment above the board.
for y in (-9.78,9.78):
    m=Mesh();inward=-1 if y>0 else 1
    for x in [-8.8+i*1.1 for i in range(17)]:
        box(m,(x,y,.80),(1.06,.17,1.6),WOOD,.018)
        box(m,(x,y+inward*.1,.84),(.86,.05,1.21),OAK,.015)
        box(m,(x,y+inward*.14,.84),(.72,.025,1.05),WOOD,.008)
    for z,d,h,ma in [(.12,.3,.20,WOOD),(1.62,.3,.12,OAK),(1.71,.34,.055,GOLD),(6.15,.36,.18,STONE),(6.36,.50,.15,OAK),(6.50,.56,.1,GOLD)]:box(m,(0,y,z),(19.1,d,h),ma,.02)
    export(m,'EndArchitecture_'+('Front' if y>0 else 'Rear'))
# Repeated arched window, pivot at sill. Front faces -Y.
m=Mesh();ww=2.5;spring=2.43;r=ww/2
# Luminous pane is an actual arched polygon, opaque; camera wall remains solid behind.
v=[(-r,0,0),(r,0,0),(r,0,spring)]+[(r*math.cos(i*PI/24),0,spring+r*math.sin(i*PI/24)) for i in range(1,25)]
m.add(v,[tuple(range(len(v)-1,-1,-1))],GLASS)
for x in (-r-.08,r+.08):box(m,(x,-.055,spring/2),(.17,.23,spring+.1),STONE,.02)
for z in (0,1.13,2.25):box(m,(0,-.10,z),(ww,.10,.075),WOOD,.008)
for x in (-.625,0,.625):box(m,(x,-.10,1.48),(.055,.10,2.98 if x==0 else 2.85),WOOD,.005)
arc(m,(0,-.05,spring),r+.08,0,PI,.095,STONE,40)
arc(m,(0,-.16,spring),r-.035,0,PI,.033,GOLD,40)
for a in [i*PI/6 for i in range(1,6)]:tube(m,[(0,-.105,spring),(r*.95*math.cos(a),-.105,spring+r*.95*math.sin(a))],.028,WOOD)
box(m,(0,-.04,-.08),(2.95,.47,.15),STONE,.023)
export(m,'ArchedWindow')
# Coffered ceiling: dark timber ribs with plaster insets (clear above camera sightlines).
for idx,y in enumerate((-8,-4,0,4,8)):
    m=Mesh();box(m,(0,y,6.65),(19.05,.22,.35),WOOD,.025)
    box(m,(0,y,6.43),(19.07,.28,.07),OAK,.015)
    for x in (-6,-3,0,3,6):box(m,(x,y,6.38),(.30,.35,.08),GOLD,.012)
    export(m,'CeilingRib_'+str(idx))
m=Mesh()
for x in (-6,-3,0,3,6):box(m,(x,0,6.7),(.16,19.7,.27),WOOD,.02)
export(m,'CeilingLongitudinals')
# Board frame is sculpted joinery, broad enough for the live 10.4 x 2.9 text panel.
m=Mesh();bw=10.45;bh=2.95
for extra,thick,dep,ma in [(.27,.17,.20,WOOD),(.14,.06,.25,GOLD),(.065,.07,.17,OAK)]:
    for z in (-bh/2-extra,bh/2+extra):box(m,(0,-dep*.5,z),(bw+extra*2+thick,dep,thick),ma,.014)
    for x in (-bw/2-extra,bw/2+extra):box(m,(x,-dep*.5,0),(thick,dep,bh+extra*2),ma,.014)
box(m,(0,-.30,-bh/2-.19),(bw+.50,.66,.08),WOOD,.018)
for x in (-3.9,-3.65,-3.4):box(m,(x,-.39,-bh/2-.125),(.13,.03,.035),IVORY,.005)
box(m,(3.6,-.38,-bh/2-.12),(.32,.14,.08),IRON,.012)
# Quiet pediment and oval crest above; no ornament intrudes on text.
for s in (-1,1):tube(m,[(0,0,bh/2+.85),(s*3.4,0,bh/2+.37)],.10,WOOD,8)
box(m,(0,0,bh/2+.4),(7.1,.22,.15),OAK,.02)
export(m,'BoardFrame')
# Platform leaf exactly 2.88 x 4.32; center-pivot shell, underside ribs and visible edge thickness.
for side in ('A','B'):
    m=Mesh();paint=BLUE if side=='A' else AMBER
    box(m,(0,0,-.105),(2.87,4.31,.19),WOOD,.012)
    for j in range(16):box(m,(0,-2.025+j*.27,.006),(2.81,.259,.018),OAK if j%3 else OAK2,0)
    for x in (-1.31,1.31):box(m,(x,0,.019),(.18,4.26,.032),paint,.008)
    for y in (-2.05,2.05):box(m,(0,y,.019),(2.65,.18,.032),paint,.008)
    for y in (-1.55,0,1.55):
        box(m,(0,y,-.235),(2.79,.13,.15),IRON,.012)
        for x in (-1.12,1.12):ellipsoid(m,(x,y,.04),(.041,.041,.016),GOLD,8,4)
    # Brass corners and seam edge; material is neutral before the verdict.
    for x in (-1.33,1.33):
        for y in (-1.94,1.94):box(m,(x,y,.043),(.18,.26,.012),GOLD,.004)
    # Recessed seam stitching, mechanical corner plates and underside X bracing.
    for xx in (-1.16,1.16):
        for yy in [-1.8+i*.30 for i in range(13)]:
            ellipsoid(m,(xx,yy,.042),(.023,.023,.012),GOLD,8,4)
    for yy in (-1.72,1.72):
        box(m,(0,yy,.028),(1.90,.12,.02),IRON,.008)
        for xx in (-.80,.80):ellipsoid(m,(xx,yy,.046),(.035,.035,.017),GOLD,8,4)
    for sign in (-1,1):tube(m,[(-1.29,-1.90*sign,-.24),(1.29,1.90*sign,-.24)],.065,IRON,6)
    for yy in (-1.6,0,1.6):
        box(m,(0,yy,-.32),(.28,.28,.11),GOLD,.02)
    export(m,'DoorLeaf'+side)
# Hinge barrels centered on the real pivot, along Y (Unity Z).
m=Mesh()
for y in (-1.58,0,1.58):
    tube(m,[(0,y-.24,0),(0,y+.24,0)],.075,IRON,12)
    for yy in (-.20,.20):tube(m,[(0,y+yy-.016,0),(0,y+yy+.016,0)],.086,GOLD,12)
export(m,'Hinge')
# Neutral stone walkway, visibly safe between the two trap floors.
m=Mesh()
for y in range(6):box(m,(0,-1.8+y*.72,.008),(1.40,.70,.025),STONE,.005)
for x in (-.66,.66):box(m,(x,0,.024),(.025,4.28,.012),GOLD,.003)
export(m,'GapBridge')
# Wide raised dais with inset front panels, parquet top and brass nosing.
m=Mesh();pw=8.64;pd=2.88
box(m,(0,0,.35),(pw,pd,.70),WOOD,.03)
for x in [-3.75+i*.94 for i in range(9)]:
    box(m,(x,-1.457,.35),(.79,.06,.47),OAK,.015)
    box(m,(x,-1.49,.35),(.64,.035,.34),WOOD,.008)
for y in range(8):box(m,(0,-1.25+y*.355,.724),(8.60,.343,.025),OAK if y%3 else OAK2,0)
box(m,(0,-1.45,.72),(8.77,.15,.06),GOLD,.012)
export(m,'Dais')
# Double school desk and bench: credible adult scale, sloped top, trestles, book.
m=Mesh()
for x in (-.60,.60):
    for y in (-.05,.37):box(m,(x,y,.47),(.075,.085,.94),IRON,.012)
    box(m,(x,.11,.12),(.095,.68,.07),IRON,.01)
box(m,(0,.12,.94),(1.50,.68,.10),OAK,.035,Matrix.Rotation(-.055,4,'X'))
box(m,(0,.1,.57),(1.40,.085,.10),IRON,.01)
for x in (-.55,.55):
    for y in (-.60,-.90):box(m,(x,y,.25),(.07,.075,.50),IRON,.009)
box(m,(0,-.75,.52),(1.50,.39,.075),WOOD,.025)
for x in (-.63,.63):box(m,(x,-.94,.71),(.055,.07,.88),IRON,.008)
box(m,(0,-.94,.92),(1.48,.085,.26),OAK,.02)
book(m,-.40,.1,1.0,ma=GREEN)
export(m,'SchoolDesk')
# Academic cupboard with real shelves and varied book spines.
m=Mesh();box(m,(0,.27,1.25),(1.8,.12,2.5),WOOD,.025)
for x in (-.86,.86):box(m,(x,0,1.25),(.13,.65,2.5),WOOD,.02)
for z in (.12,.65,1.18,1.71,2.24,2.53):box(m,(0,0,z),(1.9,.69,.09),OAK,.02)
for row in range(4):
    for j in range(11):
        x=-.72+j*.14;h=rng.uniform(.28,.41)
        box(m,(x,-.10,.70+row*.53+h/2),(.105,.37,h),(RED,GREEN,OAK,BLUE)[j%4],.009)
        for dz in (.065,h-.05):box(m,(x,-.292,.70+row*.53+dz),(.096,.012,.014),GOLD,.003)
export(m,'Bookcase')
# Opal chandelier: rods, spun brass canopy, five globe lamps. Local pivot at top.
m=Mesh();lathe(m,(0,0,-.12),[(0,.26),(.06,.31),(.12,.19)],GOLD,24)
tube(m,[(0,0,-.1),(0,0,-1.10)],.026,IRON)
lathe(m,(0,0,-1.17),[(0,.19),(.08,.19),(.13,.10)],GOLD,24)
for i in range(5):
    a=i*math.tau/5;x=.63*math.cos(a);y=.63*math.sin(a)
    tube(m,[(0,0,-1.08),(x*.7,y*.7,-1.15),(x,y,-.96)],.029,GOLD)
    lathe(m,(x,y,-.97),[(0,.13),(.07,.13)],GOLD,20)
    ellipsoid(m,(x,y,-.78),(.17,.17,.21),BULB,16,10)
export(m,'Chandelier')
# Wall sconce.
m=Mesh();box(m,(0,.035,0),(.15,.12,.40),GOLD,.04)
tube(m,[(0,0,-.1),(0,-.35,-.15),(0,-.41,.05)],.029,GOLD)
ellipsoid(m,(0,-.41,.22),(.12,.12,.20),BULB,16,10)
export(m,'Sconce')
# Large clock face with separate raised ticks and fixed hands; decorative, never a timer.
m=Mesh()
for r,y,ma in [(.65,0,WOOD),(.59,-.08,GOLD),(.55,-.11,IVORY)]:
    tube(m,[(0,y+.018,0),(0,y-.018,0)],r,ma,64)
for i in range(60):
    a=i*math.tau/60
    tube(m,[(.49*math.sin(a),-.15,.49*math.cos(a)),((.42 if i%5==0 else .465)*math.sin(a),-.15,(.42 if i%5==0 else .465)*math.cos(a))],.012 if i%5==0 else .004,IRON,4)
tube(m,[(0,-.17,0),(-.24,-.17,.16)],.025,IRON)
tube(m,[(0,-.18,0),(.16,-.18,.35)],.016,IRON)
ellipsoid(m,(0,-.20,0),(.045,.025,.045),GOLD,12,8)
export(m,'Clock')
# Ornate enamel signs, text added as crisp Cyrillic TMP in Unity.
for side in ('A','B'):
    m=Mesh();paint=BLUE if side=='A' else AMBER
    box(m,(0,0,0),(1.55,.13,1.07),WOOD,.06)
    box(m,(0,-.085,0),(1.44,.06,.96),GOLD,.035)
    box(m,(0,-.124,0),(1.34,.025,.86),paint,.025)
    for x in (-.55,.55):tube(m,[(x,0,.52),(x,0,1.8)],.013,GOLD)
    export(m,'Sign'+side)
# Pit dressed as old school boiler basement: horizontal masonry courses and recessed ribs.
m=Mesh();width=16.16
for y in (-1.53,7.29):
    for row in range(11):
        for j in range(18):box(m,(-7.7+j*.90+(row%2)*.12,y,-.4-row*.60),(.87,.10,.57),PIT,.012)
    for x in (-6.9,0,6.9):box(m,(x,y,-3.6),(.22,.17,6.9),IRON,.02)
for x in (-7.82,7.82):box(m,(x,2.88,-3.6),(.12,9.3,6.9),PIT,.02)
# Low brass service pipes, fitted to walls clear of the swinging leaves.
for y in (-1.36,7.12):
    for z in (-2.3,-4.8):
        tube(m,[(-7.8,y,z),(7.8,y,z)],.06,IRON,12)
        for x in (-6,-3,0,3,6):tube(m,[(x-.04,y,z),(x+.04,y,z)],.084,GOLD,12)
    for x in (-3.6,3.6):
        box(m,(x,y,-4.0),(.34,.18,.62),IRON,.025)
        ellipsoid(m,(x,y+(.14 if y<0 else -.14),-4.0),(.12,.1,.22),BULB,16,10)
export(m,'PitMasonry')
# Grilles and warm maintenance lamps sit below the swinging leaves, never across the hole.
m=Mesh()
for x in (-3.6,3.6):
    for j in range(8):box(m,(x-1.3+j*.37,2.88,-7.0),(.045,2.8,.045),IRON,.006)
    for y in (1.48,4.28):box(m,(x,y,-7.0),(2.85,.055,.065),GOLD,.006)
export(m,'PitFloorDetail')
# Reusable portrait frame, with a separate original portrait canvas in Unity.
m=Mesh()
for extra,w,d,ma in [(0,.13,.16,WOOD),(-.085,.04,.20,GOLD)]:
    for x in (-.82-extra,.82+extra):box(m,(x,-d*.5,0),(w,d,2.1+extra*2),ma,.015)
    for z in (-1.05-extra,1.05+extra):box(m,(0,-d*.5,z),(1.75+extra*2,d,w),ma,.015)
export(m,'PortraitFrame')
# Front pilaster with inset fluting, stepped capital and a restrained brass collar.
m=Mesh()
box(m,(0,.04,3.92),(.42,.29,4.35),STONE,.022)
for z,w,h in [(1.83,.68,.16),(2,.55,.15),(5.98,.56,.17),(6.14,.78,.14)]:box(m,(0,0,z),(w,.39,h),STONE,.018)
for x in (-.13,0,.13):box(m,(x,-.12,3.98),(.032,.018,3.45),OAK,.007)
box(m,(0,-.16,5.8),(.48,.06,.04),GOLD,.006)
export(m,'FrontPilaster')
# Rear double door and transom; solid wall collider is the room boundary.
m=Mesh()
box(m,(0,0,1.70),(3.4,.25,3.4),WOOD,.03)
for x in (-.77,.77):
    for z in (.65,1.67,2.70):
        box(m,(x,-.145,z),(1.25,.07,.79),OAK,.02)
        box(m,(x,-.19,z),(1.08,.03,.61),WOOD,.012)
    tube(m,[(x*.16,-.27,1.45),(x*.16,-.27,1.81)],.027,GOLD)
for x in (-1.82,1.82):box(m,(x,-.04,1.84),(.22,.39,3.68),STONE,.02)
box(m,(0,-.05,3.6),(3.95,.43,.24),STONE,.02)
export(m,'EntranceDoor')
