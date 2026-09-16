"""Second-pass original school curiosities and hatch engineering. Z up, metres."""
# Oversized astronomical globe on a turned tripod pedestal.
m=Mesh()
lathe(m,(0,0,0),[(0,.60),(.10,.60),(.16,.46),(.23,.28),(.40,.20),(1.05,.12),(1.13,.28),(1.23,.28)],WOOD,24)
for a in (0,2*PI/3,4*PI/3):
    tube(m,[(0,0,.52),(.55*math.cos(a),.55*math.sin(a),.16)],.065,GOLD,10)
ellipsoid(m,(0,0,2.03),(.73,.73,.73),BLUE,40,24)
# Raised simplified land masses, hand-authored silhouettes, projected onto globe.
for lon,lat,sx,sy in [(-1.9,.65,.48,.42),(-1.7,-.30,.22,.61),(-.05,.65,.61,.28),(.05,.02,.30,.53),(1.3,-.42,.27,.18)]:
    points=[(0,0)]+[(math.cos(i*PI/8)*(1+.2*math.sin(i*2.7)),math.sin(i*PI/8)*(1+.18*math.cos(i*3.2))) for i in range(16)]
    verts=[]
    for px,py in points:
        lo=lon+px*sx;la=lat+py*sy;rr=.738
        verts.append((rr*math.cos(la)*math.sin(lo),-rr*math.cos(la)*math.cos(lo),2.03+rr*math.sin(la)))
    def patch(a,b,c,depth=3):
        def midpoint(p,q):
            vv=(Vector(p)+Vector(q))*.5-Vector((0,0,2.03));return tuple(vv.normalized()*.743+Vector((0,0,2.03)))
        if depth:
            ab=midpoint(a,b);bc=midpoint(b,c);ca=midpoint(c,a)
            for tri in ((a,ab,ca),(ab,b,bc),(ca,bc,c),(ab,bc,ca)):patch(*tri,depth-1)
        else:m.add([a,b,c],[(0,1,2),(2,1,0)],OAK2)
    for i in range(16):patch(verts[0],verts[i+1],verts[(i+1)%16+1])
for lat in (-.65,0,.65):
    rr=.745*math.cos(lat);zz=2.03+.745*math.sin(lat)
    tube(m,[(rr*math.cos(i*PI/32),rr*math.sin(i*PI/32),zz) for i in range(65)],.005,GOLD,4)
arc(m,(0,0,2.03),.85,0,2*PI,.032,GOLD,64)
for x in (-.93,.93):tube(m,[(x,0,1.8),(x,0,2.12)],.045,GOLD)
export(m,'Globe')
# Freestanding roll-down astronomy diagram: cream canvas and graphic orbit lines.
m=Mesh()
for x in (-1.02,1.02):
    tube(m,[(x,0,.08),(x,0,2.75)],.035,IRON)
    box(m,(x,0,.08),(.16,.82,.12),WOOD,.03)
box(m,(0,.02,1.94),(1.95,.035,1.44),PAPER,.009)
for z in (1.19,2.70):tube(m,[(-1.12,-.01,z),(1.12,-.01,z)],.045,WOOD)
ellipsoid(m,(-.35,-.022,1.96),(.15,.025,.15),AMBER,20,12)
for r in (.3,.48,.65):
    arc(m,(-.35,-.022,1.96),r,0,PI*2,.008,OAK,48)
for x,z,r in [(-.07,2.06,.045),(-.66,1.60,.06),(.28,2.13,.07)]:ellipsoid(m,(x,-.04,z),(r,.03,r),BLUE,12,8)
for z in (1.45,1.60,1.75,1.90,2.05,2.20,2.35):box(m,(.66,-.012,z),(.27,.012,.012),OAK,.002)
export(m,'AstronomyMap')
# Book mountain: deliberately varied trim and lean, recognisable at room scale.
m=Mesh()
for i in range(9):
    book(m,.04*math.sin(i*1.4),.03*math.cos(i*2),i*.12,.71-i*.022,.57,.10,[RED,GREEN,BLUE,WOOD][i%4])
export(m,'BookTower')
# Cabinet of school curiosities, drawers below, open displays above.
m=Mesh()
box(m,(0,.27,1.55),(2.25,.12,3.1),WOOD,.02)
for x in (-1.1,1.1):box(m,(x,0,1.57),(.15,.7,3.14),WOOD,.02)
for z in (.12,1.12,2.12,3.17):box(m,(0,0,z),(2.4,.8,.12),OAK,.02)
for x in (-.56,.56):
    for z in (.35,.80):
        box(m,(x,-.37,z),(1.03,.1,.38),WOOD,.025)
        tube(m,[(x-.11,-.45,z),(x+.11,-.45,z)],.025,GOLD)
# bottles, prism and oversized pencil specimen
for j in range(4):
    lathe(m,(-.80+j*.48,0,1.18),[(0,.12),(.30,.12),(.39,.045),(.48,.045)],(BLUE,GREEN,AMBER,IVORY)[j],16)
for x in (-.70,.65):
    lathe(m,(x,0,2.18),[(0,.25),(.06,.25),(.11,.16),(.49,.13),(.68,.0)],GOLD,6)
box(m,(0,0,3.26),(2.65,.95,.13),WOOD,.03)
export(m,'CuriosityCabinet')
# Bespectacled owl prize on stacked books. Round body / oversized eyes / mortarboard.
m=Mesh();book(m,0,0,0,.68,.55,.14,RED);book(m,.035,0,.17,.57,.62,.12,GREEN)
ellipsoid(m,(0,0,.66),(.30,.23,.36),GOLD,24,16)
for s in (-1,1):
    ellipsoid(m,(s*.25,.005,.65),(.10,.21,.29),WOOD,16,10)
    ellipsoid(m,(s*.145,-.205,.83),(.147,.065,.16),IVORY,20,12)
    ellipsoid(m,(s*.145,-.266,.83),(.055,.025,.07),IRON,16,10)
    arc(m,(s*.145,-.288,.83),.157,0,2*PI,.012,IRON,24)
    tube(m,[(s*.12,-.05,.35),(s*.12,-.19,.32)],.04,GOLD)
tube(m,[(-.02,-.29,.84),(.02,-.29,.84)],.012,IRON)
lathe(m,(0,-.25,.61),[(0,0),(.11,.064)],AMBER,4)
box(m,(0,0,1.10),(.69,.61,.055),IRON,.012,Matrix.Rotation(.13,4,'Z'))
tube(m,[(.05,0,1.14),(.37,-.08,1.14),(.40,-.09,.92)],.014,GOLD)
export(m,'OwlPrize')
# Desk apparatus: abacus, inkpot, tall pencils, scattered exercise sheets.
m=Mesh()
for x in (-.45,.45):box(m,(x,0,.36),(.06,.09,.72),WOOD,.012)
for z in (.06,.70):box(m,(0,0,z),(.95,.1,.06),OAK,.012)
for row in range(5):
    z=.17+row*.10;tube(m,[(-.42,0,z),(.42,0,z)],.009,GOLD,6)
    for j in range(8):ellipsoid(m,(-.33+j*.067+(0 if j<row else .14),0,z),(.027,.041,.041),RED if j<4 else IVORY,10,6)
export(m,'Abacus')
m=Mesh()
for i in range(5):box(m,(-.25+i*.035,.05+i*.018,.008+i*.004),(.40,.48,.006),PAPER,0,Matrix.Rotation(i*.08,4,'Z'))
lathe(m,(.28,.05,0),[(0,.10),(.19,.10),(.21,.115)],IRON,16)
for j in range(7):
    x=.28+.06*math.cos(j*2.4);y=.05+.06*math.sin(j*2.4);h=.35+(j%3)*.04
    tube(m,[(x,y,.07),(x,y,h)],.012,AMBER,6);lathe(m,(x,y,h),[(0,.012),(.04,0)],PAPER,6)
export(m,'Stationery')
# Rear coat stand and satchels.
m=Mesh();lathe(m,(0,0,0),[(0,.38),(.08,.38),(.14,.12),(2.12,.055),(2.2,.10)],WOOD,20)
for i in range(6):
    a=i*PI/3;tube(m,[(0,0,1.77),(.34*math.cos(a),.34*math.sin(a),2.08),(.33*math.cos(a),.33*math.sin(a),2.21)],.027,GOLD)
for x in (-.43,.43):
    box(m,(x,-.04,.32),(.59,.29,.55),RED,.07)
    box(m,(x,-.205,.46),(.56,.055,.25),WOOD,.03)
    for xx in (-.17,.17):box(m,(x+xx,-.244,.32),(.065,.026,.12),GOLD,.008)
export(m,'CoatStand')
# Floor compass inlay, all flush, no colliders.
m=Mesh()
for r in (1.72,1.79,2.05):
    tube(m,[(r*math.cos(i*PI/48),r*math.sin(i*PI/48),.035) for i in range(97)],.013,GOLD,4)
for i in range(8):
    a=i*PI/4;v=[(0,0,.036),(1.65*math.cos(a),1.65*math.sin(a),.036),(.39*math.cos(a+.4),.39*math.sin(a+.4),.036)]
    m.add(v,[(0,1,2)],GOLD if i%2 else DARK)
export(m,'FloorCompass')
# Hatch mechanisms, axis Z-up in Blender / Y-up Unity.
m=Mesh()
lathe(m,(0,0,0),[(0,.115),(.08,.14),(.13,.115),(.72,.115),(.78,.15),(.83,.15)],IRON,20)
for z in (.10,.70):lathe(m,(0,0,z),[(0,.14),(.045,.14)],GOLD,20)
export(m,'PistonBarrel')
m=Mesh();lathe(m,(0,0,0),[(0,.058),(1,.058)],GOLD,16);export(m,'PistonRod')
# Gear plane XZ, axle Y -> Unity Z.
m=Mesh();tube(m,[(0,-.045,0),(0,.045,0)],.26,IRON,32)
arc(m,(0,-.054,0),.22,0,2*PI,.026,GOLD,32)
for i in range(16):
    a=i*PI/8;box(m,(.275*math.cos(a),0,.275*math.sin(a)),(.10,.115,.075),GOLD,.006,Matrix.Rotation(-a,4,'Y'))
tube(m,[(0,-.10,0),(0,.10,0)],.08,GOLD,12)
export(m,'Gear')
# Fixed end beam, rivets and recessed vent slots below floor.
m=Mesh();box(m,(0,0,-.19),(5.82,.19,.38),IRON,.025)
for z in (-.035,-.345):box(m,(0,-.11,z),(5.85,.055,.04),GOLD,.006)
for x in [-2.7+i*.3 for i in range(19)]:
    box(m,(x,-.10,-.18),(.11,.03,.18),DARK,.018)
    ellipsoid(m,(x,-.132,-.06),(.027,.014,.027),GOLD,8,6)
export(m,'HatchBeam')
# Bent paper particle mesh, double sided for tumbling.
m=Mesh();verts=[(-.12,-.17,0),(.12,-.17,.02),(.12,0,.045),(-.12,0,.025),(-.12,.17,-.005),(.12,.17,.015)]
faces=[(0,1,2,3),(3,2,5,4)];m.add(verts,faces+[tuple(reversed(f)) for f in faces],PAPER);export(m,'LooseSheet')
