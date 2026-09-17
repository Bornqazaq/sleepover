"""Original fractured architecture. Executed by memory_foundry.py in its mesh context."""
import random

ruin_rng = random.Random(170926)
RUST = mat('Rust', (.48, .19, .075), .91, .25, 'Rust')
PLASTER = mat('Plaster', (.59, .57, .49), .94, 0, 'Masonry')
SOOT = mat('Charred', (.075, .067, .062), .98, 0, 'Masonry')
PIPEBLUE = mat('PipeBlue', (.12, .33, .43), .63, .4, 'Rust')
OXIDE = mat('Oxide', (.19, .37, .29), .84, .3, 'Rust')
EMBER = mat('Ember', (1, .14, .012), .7, 0, emission=3.5)


def rock(m, pos, size, material=PLASTER):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1)
    bm.verts.ensure_lookup_table()
    for v in bm.verts:
        v.co *= ruin_rng.uniform(.72, 1.19)
    verts = [(pos[0]+v.co.x*size[0], pos[1]+v.co.y*size[1], pos[2]+v.co.z*size[2]) for v in bm.verts]
    faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    m.add(verts, faces, material)
    bm.free()


def slab(m, x, y0, y1, bottom, h0, h1, material, thickness=.65):
    verts = [(xx, yy, zz) for xx in (x-thickness/2, x+thickness/2)
             for yy, zz in ((y0,bottom),(y1,bottom),(y1,h1),(y0,h0))]
    m.add(verts, [(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], material)


def wall_height(side, y):
    points = [(-33,19),(-12,19),(2,18.5),(12,19),(22,19),(27,13.5),(30,14),(33,18)] if side > 0 else [(-33,19),(-12,19),(-3,18.5),(8,19),(33,19)]
    return float(np.interp(y, [p[0] for p in points], [p[1] for p in points]))


def breach(side, y):
    centre, radius, height, vertical = (7.5, 5.4, 10.6, 4.4) if side > 0 else (-1, 4.1, 11.2, 3.5)
    t = (y-centre)/radius
    if abs(t) >= 1: return None
    extent = vertical*math.sqrt(1-t*t)
    return height-extent, height+extent


for side in (-1,1):
    m = Mesh(); x = side*17.5
    steps = 66; pitch = 65.16/steps
    heights = [wall_height(side,-32.58+i*pitch)+ruin_rng.uniform(-.6,.6) for i in range(steps+1)]
    for i in range(steps):
        y0=-32.58+i*pitch; y1=y0+pitch; cy=(y0+y1)/2; h0=heights[i]; h1=heights[i+1]
        # Surviving window voids remain actual holes, with only a few glass panes.
        is_window = abs((cy+2.16)%4.32-2.16)<1.05
        hole = breach(side,cy)
        gaps = [(7.1,13.8)] if is_window else []
        if hole: gaps.append(hole)
        cursor = 0
        for low,high in sorted(gaps):
            if low > cursor: slab(m,x,y0,y1,cursor,min(low,h0),min(low,h1),PLASTER)
            cursor = max(cursor,high)
        if cursor < min(h0,h1): slab(m,x,y0,y1,cursor,h0,h1,BRICK)
        for z in np.arange(.4,max(h0,h1),.68):
            if z>min(h0,h1)-.45 or any(low-.25<z<high+.25 for low,high in gaps): continue
            if ruin_rng.random()<.22:continue
            box(m,(x-side*.37,cy+ruin_rng.uniform(-.07,.07),z),( .12,pitch*.92,.59),
                ruin_rng.choice([BRICK,BRICK,PLASTER,PLASTER,STONE]),.025)
        if hole:
            for edge in hole:
                rock(m,(x,cy,edge),(.44,.48,.35),ruin_rng.choice([PLASTER,STONE,SOOT]))
                box(m,(x-side*.40,cy,edge+(-.5 if edge==hole[0] else .5)),(.035,.85,.5),SOOT,.012)
            if i%2==0: pipe(m,[(x,cy,hole[0]-.4),(x-side*.2,cy,hole[0]+.5),(x-side*.65,cy+.35,hole[0]+.9)],.028,RUST)
        if min(h0,h1)<17:
            for j in range(3):
                rock(m,(x,cy+ruin_rng.uniform(-.4,.4),min(h0,h1)+ruin_rng.uniform(-.20,.25)),(.48,.38,.44),ruin_rng.choice([PLASTER,STONE,SOOT]))
            if i%2==0:
                pipe(m,[(x,cy,min(h0,h1)-.25),(x-side*.14,cy+.15,min(h0,h1)+.7),(x-side*.7,cy+.42,min(h0,h1)+1.0)],.028,RUST)
        # Blast soot belongs to broken masonry, not the memory plates.
        if min(h0,h1)<12 and i%3==0:
            box(m,(x-side*.39,cy,min(h0,h1)-1.0),(.025,.8,1.2),SOOT,.02)
    for y in np.arange(-30.24,32,4.32):
        h=wall_height(side,y)
        box(m,(x-side*.52,y,h/2),(.45,.32,h),IRON,.025)
        if h>14.8 and breach(side,y+2.1) is None:
            for z in (7.2,9.4,11.6,13.7): box(m,(x-side*.40,y+2.1,z),(.12,3.7,.085),IRON,.01)
            for yy in (y+.65,y+1.6,y+2.55,y+3.50):
                if yy>32:continue
                box(m,(x-side*.40,yy,10.45),(.10,.075,6.5),IRON,.008)
                if ruin_rng.random()<.45:
                    box(m,(x,yy+.30,11.1),(.022,.58,2.35),GLASS,.006)
        if h<13:
            pipe(m,[(x-side*.5,y,h),(x-side*.8,y+.2,h+.9),(x-side*1.2,y+1.1,h+.6)],.09,RUST)
    export(m,'RuinWallRight' if side>0 else 'RuinWallLeft')

# Missing far-right corner: a ragged low wall rather than a sealed rectangle.
m=Mesh()
for i in range(11):
    xx=7.2+i*.98; height=18.5-5.0*math.exp(-((xx-14.5)/2.3)**2)+ruin_rng.uniform(-.45,.4)
    box(m,(xx,32.3,height/2),(.99,.65,height),PLASTER,.035)
    rock(m,(xx,32.3,height),(.65,.52,.50),STONE)
    pipe(m,[(xx,32.3,height-.4),(xx+.15,32.1,height+.7),(xx+.4,31.8,height+.9)],.035,RUST)
export(m,'RuinCorner')

m=Mesh()
for ix in range(8):
    for iy in range(12):
        x=-15.75+ix*4.5; y=-29.7+iy*5.4
        keep = ((x-11)/4.8)**2+((y-8)/5.6)**2>1 and ((x+12)/3.8)**2+((y+1)/4.3)**2>1
        if x>10 and y>24:keep=False
        if not keep:continue
        angle=ruin_rng.uniform(-.07,.07)
        tr=Matrix.Translation(Vector((x,y,18.6+ruin_rng.uniform(-.3,.25)))) @ Matrix.Rotation(angle,4,'Y')
        box(m,(0,0,0),(4.42,5.30,.32),RUST,.045,tr)
        for xx in (-1.65,-.8,0,.8,1.65):box(m,(xx,0,.18),(.08,5.3,.055),IRON,.008,tr)
        box(m,(0,0,-.35),(4.5,.22,.45),IRON,.025,tr)
        if not (y<-22 and x<-8):
            for j in range(3):rock(m,(x+ruin_rng.uniform(-2.1,2.1),y+2.5,18.6),(.6,.5,.24),PLASTER)
for y,start,end in [(-26,-17,17),(-15,-17,17),(-5,-17,17),(9,-17,9),(22,-17,17),(30,-17,12)]:
    for z in (17.8,19.0):pipe(m,[(start,y,z),(end,y,z)],.13,IRON)
    for x in np.arange(start,end-2,2.6):pipe(m,[(x,y,17.8),(x+2.6,y,19)],.07,RUST)
    pipe(m,[(end,y,18.5),(end+1.1,y+.2,17.9),(end+2.3,y+.9,16.2)],.12,IRON)
    for j in range(3):
        x=end-2+j*.7
        pipe(m,[(x,y,18),(x+.3,y+.15,16.3-j*.3),(x+.8,y+.3,15.8-j*.3)],.025,RUST)
rock(m,(11.6,22.3,14.4),(1.1,.42,1.35),PLASTER)
for x in (10.9,12.1):
    pipe(m,[(x,22,18.2),(x+.15,22.1,16.8),(x,22.3,14.5)],.035,RUST)
export(m,'RuinRoof')

# Deep shaft modules: no bottom. Large irregular pipe runs sit well outside the route.
for variant in range(3):
    m=Mesh()
    box(m,(0,.35,-43),(6.48,.5,86),SOOT,.025)
    for z in (-4,-14,-26,-41,-60,-79):
        box(m,(0,-.05,z),(6.48,.42,.30),IRON,.025)
        for x in (-2.5,2.4):box(m,(x,-.18,z-4),(.36,.40,8),STONE,.02)
    for k,(px,rad,ma) in enumerate([(-2.5,.43,RUST),(-.65,.22,PIPEBLUE),(1.25,.62,OXIDE),(2.8,.15,RED)]):
        offset=variant*1.9+k*.6
        pts=[(px,-.40,-2),(px,-.40,-8-offset),(px+.55,-1.8,-10-offset),(px+.55,-1.8,-17-offset),
             (px,-.48,-20-offset),(px,-.48,-83)]
        pipe(m,pts,rad,ma)
        for z in (-5,-23,-37,-53,-70):
            flange(m,px,-.4,z,rad*1.45,RUST)
            box(m,(px,-.1,z-.10),(rad*2.8,.75,.20),IRON,.025)
    for j in range(4):
        z=-7-j*14-variant*2.1
        box(m,(0,-1.15,z),(5.2,2.1,.22),IRON,.035)
        rail(m,4.8,z+.14,-2.10)
        box(m,(1.3,-1.0,z+1.1),(1.4,1.15,1.8),PIPEBLUE,.08)
        for x in (-1.8,1.8):pipe(m,[(x,-.2,z-2.7),(x,-2,z-.14)],.12,RUST)
    export(m,'ShaftServices'+str(variant))

for variant in range(3):
    m=Mesh()
    for j in range(10):
        rock(m,(ruin_rng.uniform(-1.4,1.4),ruin_rng.uniform(-.6,.6),ruin_rng.uniform(0,.35)),
             (ruin_rng.uniform(.25,.7),.40,ruin_rng.uniform(.15,.50)),ruin_rng.choice([STONE,PLASTER,BRICK,SOOT]))
    for x in (-.6,.4):pipe(m,[(x,0,.2),(x+.1,.2,.7),(x+.6,.4,.82)],.033,RUST)
    export(m,'Rubble'+str(variant))

m=Mesh()
pipe(m,[(0,0,-3),(0,0,-.5),(0,-1.5,0),(0,-3.3,0)],.64,RUST)
for z in (-2.6,-.9):flange(m,0,0,z,.83,RUST)
lathe(m,(0,-3.32,0),[(0,.70),(.10,.70)],IRON,20,matrix=Matrix.Rotation(PI/2,4,'X'))
export(m,'BrokenOutfall')

# Asymmetric cantilevers, machinery and wrecks. Local -Y points into the shaft.
for variant in range(4):
    m=Mesh()
    depth=[3.4,2.5,4.6,3.8][variant]
    box(m,(0,-depth/2,0),(4.7,depth,.25),SOOT,.04)
    for x in (-1.9,1.9):
        pipe(m,[(x,.3,-2.8),(x,-depth+.25,-.18)],.16,RUST)
        box(m,(x,-depth/2,-.25),(.22,depth+.5,.48),IRON,.02)
    for j in range(6):
        rock(m,(ruin_rng.uniform(-2,2),ruin_rng.uniform(-depth,0),.12),(.35,.27,.22),STONE)
    if variant==0:
        for x in (-1.5,0,1.5):
            tr=Matrix.Translation(Vector((x,-depth*.6,.25))) @ Matrix.Rotation(ruin_rng.uniform(-.25,.25),4,'Y')
            box(m,(0,0,0),(.3,depth+1.8,.4),RUST,.02,tr)
        pipe(m,[(-2,-depth,.1),(-2,-depth,1.4),(-.7,-depth,1.2)],.055,YELLOW)
        rock(m,(-1,-1.2,.65),(1.2,.8,.6),PLASTER)
    elif variant==1:
        box(m,(-.9,-1.1,1.25),(1.7,1.8,2.3),PIPEBLUE,.10)
        for z in np.arange(.5,2.1,.22):box(m,(-.9,-2.04,z),(1.35,.09,.07),IRON,.015)
        for x in (-1.3,-.5):
            pipe(m,[(x,-.2,2.4),(x,-.2,3),(x,.7,3)],.16,RUST)
        for j in range(3):box(m,(.65+j*.32,-.6,.6),(.23,.65,.9),TEAL,.025)
    elif variant==2:
        tr=Matrix.Translation(Vector((-.6,-2.8,1.2))) @ Matrix.Rotation(.25,4,'Y')
        box(m,(0,0,0),(2.1,2.8,1.9),OXIDE,.10,tr)
        for x in (-.95,.95):box(m,(x,0,0),(.09,2.86,1.97),RUST,.015,tr)
        for x in (-1.8,1.8):pipe(m,[(x,0,4),(x,-1.2,2.3),(x,-3.5,.2)],.035,IRON)
        for y in (-2,-3,-4):box(m,(1.65,y,-1),(.08,.12,2.0),IRON,.01)
        for z in (-1.9,-1.4,-.9,-.4):box(m,(1.65,-3,z),(.1,2.1,.06),RUST,.01)
    else:
        tr=Matrix.Translation(Vector((-1,-1.8,.9))) @ Matrix.Rotation(.44,4,'Y')
        lathe(m,(0,0,0),[(-.8,.6),(-.6,.8),(1.2,.8),(1.4,.6)],RUST,16,matrix=tr)
        for z in (-.4,.9):lathe(m,(0,0,z),[(0,.86),(.14,.86)],IRON,16,matrix=tr)
        pipe(m,[(-1,-1.8,2.1),(-1,-1.8,3),(0,-2.4,3.2)],.2,PIPEBLUE)
        box(m,(.8,-2.3,.6),(1.05,.75,.9),BRICK,.04)
    export(m,'ShaftWreck'+str(variant))

m=Mesh()
for j in range(18):
    rock(m,(ruin_rng.uniform(-.6,.6),ruin_rng.uniform(-.45,.45),ruin_rng.uniform(0,.12)),
         (.13,.10,.09),EMBER if j%3==0 else SOOT)
for j in range(5):
    tr=Matrix.Translation(Vector((ruin_rng.uniform(-.4,.4),0,.13))) @ Matrix.Rotation(ruin_rng.uniform(-.9,.9),4,'Z')
    box(m,(0,0,0),(.08,.95,.08),SOOT,.012,tr)
export(m,'EmberDebris')

m=Mesh()
for row,n in ((0,4),(1,3)):
    for j in range(n):
        x=(j-(n-1)/2)*.29; z=.16+row*.25
        axis=Matrix.Translation(Vector((x,-.67,z))) @ Matrix.Rotation(-PI/2,4,'X')
        lathe(m,(0,0,0),[(0,.13),(1.27,.13),(1.34,.11)],RED,12,matrix=axis)
        pipe(m,[(x,.67,z),(x+.04,.78,z+.08),(x+.07,.84,z+.13)],.018,WOOD)
for y in (-.4,.4):
    box(m,(0,y,.53),(1.15,.10,.06),IRON,.01)
    for x in (-.56,.56):box(m,(x,y,.27),(.055,.10,.56),IRON,.01)
export(m,'DynamiteHeavy')

# Outdoor foundry district with streets, roof silhouettes and a lattice crane.
m=Mesh()
box(m,(0,0,-.4),(16,12,.8),STONE,.03)
box(m,(0,0,4),(14,10,8),PLASTER,.07)
for x in (-6.3,6.3):box(m,(x,0,4.3),(.3,10.3,8.6),BRICK,.035)
for y in (-5.06,5.06):
    for x in range(-5,6,2):
        for z in (2.3,5.4):
            box(m,(x,y,z),(1.25,.06,1.8),DARK,.015)
            box(m,(x,y*1.002,z),(1.05,.02,1.5),GLASS,.006)
    for x in (-3.5,3.5):box(m,(x,y,1),(.11,.10,2),IRON,.01)
for x in (-7.06,7.06):
    for y in (-3,0,3):
        for z in (2.3,5.4):
            box(m,(x,y,z),(.08,1.4,1.9),IRON,.015)
            box(m,(x*1.003,y,z),(.035,1.18,1.68),GLASS,.008)
    pipe(m,[(x,-4,8),(x,-4,.4),(x,-2,.4)],.16,RUST)
for z in (.7,3.9,7.5):
    box(m,(0,0,z),(14.25,10.25,.28),BRICK,.02)
for side in (-1,1):
    transform=Matrix.Translation(Vector((0,side*2.75,9.5))) @ Matrix.Rotation(-side*math.atan2(3,5.5),4,'X')
    box(m,(0,0,0),(14.8,6.35,.24),IRON,.035,transform)
    for x in np.arange(-7,7,.6):box(m,(x,0,.15),(.065,6.35,.075),RUST,.008,transform)
for x in (-5,0,5):
    pipe(m,[(x,-5.5,8),(x,0,11),(x,5.5,8)],.22,RUST)
export(m,'ExteriorWorkshop')

m=Mesh()
for x in (-2,2):
    for y in (-2,2):pipe(m,[(x*1.4,y*1.4,0),(x,y,23)],.17,RUST)
for z in range(0,24,3):
    for sign in (-1,1):
        pipe(m,[(-2,sign*2,z),(2,sign*2,z+3)],.09,IRON)
        pipe(m,[(sign*2,-2,z),(sign*2,2,z+3)],.09,IRON)
box(m,(0,0,23.3),(5.4,5.4,.55),RUST,.05)
box(m,(0,0,25),(3.2,3.5,2.8),TEAL,.09)
box(m,(0,-1.8,25.2),(2.7,.05,1.7),GLASS,.02)
for y in (-.7,.7):
    for z in (26,27.7):pipe(m,[(-8,y,z),(24,y,z)],.14,RUST)
    for x in range(-8,23,2):pipe(m,[(x,y,26),(x+2,y,27.7)],.065,IRON)
pipe(m,[(19,0,26),(19,0,9)],.05,IRON)
box(m,(19,0,8.7),(1.1,1,.5),YELLOW,.03)
export(m,'ExteriorCrane')

m=Mesh()
lathe(m,(0,0,0),[(0,2.8),(4,2.4),(30,1.5),(32,1.7)],BRICK,20)
for z in (3,9,15,21,27,31):lathe(m,(0,0,z),[(0,2.5-z*.028),(.25,2.5-z*.028)],IRON,20)
export(m,'ExteriorStack')
