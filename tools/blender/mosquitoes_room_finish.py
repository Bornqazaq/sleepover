"""Finish the remaining room details after the sewn prop revision."""
# Matching thin wardrobe door and an upholstered chair distinct from the clothes.
remove(('WardrobeDoorClosed',))
box('WardrobeDoorClosed',(-2.43,1.36,2.10),(.94,2.43,.038),'NaturalOak',.008)
for x in (-2.837,-2.023):box('ClosedDoorStile',(x,1.36,2.074),(.042,2.36,.018),'NaturalOak',.004)
for y in (.205,2.515):box('ClosedDoorRail',(-2.43,y,2.074),(.80,.048,.018),'NaturalOak',.004)
for ob in scene.objects:
    if ob.name.startswith(('DeskChair','PaddedArmrest')):
        for i,m in enumerate(ob.data.materials):
            if m==mats['Blue']:ob.data.materials[i]=mats['Upholstery']
# Bed upholstery: a soft padded panel fitted into the wood frame, with real welting.
remove(('HeadboardInset',))
before=set(scene.objects)
rows=[]
for j in range(33):
    v=-1+2*j/32;row=[]
    for i in range(49):
        u=-1+2*i/48
        depth=.02+.055*(max(0,1-u*u)*max(0,1-v*v))**.38
        depth-=.008*math.exp(-((u-.45)/.11)**2-((v-.1)/.18)**2)
        row.append((-3.072+depth,.98+.383*v,-1.1+.873*u))
    rows.append(row)
surface('HeadboardCushion',rows,'Linen',thickness=.009,subdivision=1)
edge=rows[0]+[r[-1] for r in rows]+list(reversed(rows[-1]))+[r[0] for r in reversed(rows)]
line('HeadboardPiping',edge,.003,'Stitch')
unite('HeadboardInset',list(set(scene.objects)-before))

# Preserve the existing blanket/giant contact surface. Give it a quilted border,
# one turned-down corner and a hem that follows the mattress rather than a ruler.
before=set(scene.objects)
for x in (-1.88,.11):
    line('DuvetBorder',[bed_surface(x,-.87+1.74*i/72,.013) for i in range(73)],.002,'Denim')
for z in (-1.935,-.265):
    line('DuvetBorder',[bed_surface(-1.87+1.94*i/72,z+1.1,.013) for i in range(73)],.002,'Denim')
corner=[]
for j in range(19):
    t=j/18;row=[]
    for i in range(25):
        u=i/24;x=-1.89+.34*u*(1-t);v=.44+.39*t+.08*u
        p=bed_surface(x,v,.018+.037*math.sin(math.pi*t))
        row.append(p)
    corner.append(row)
surface('TurnedDownDuvetCorner',corner,'Linen',thickness=.006,subdivision=1)
unite('DuvetFinishing',list(set(scene.objects)-before))

# Full hollow clock case, bezel, three different hands and bell supports.
remove(('AlarmClock','ClockFace','ClockBell','ClockHands','ClockFoot','DialIndex','ClockBezel','ClockTopHandle'))
before=set(scene.objects);cx=-3.09;cy=1.073;cz=-2.86
case=[]
for z,r in ((-.072,.115),(-.061,.125),(-.039,.128),(.04,.128),(.054,.118),(.057,.09)):
    case.append([(cx+r*math.sin(i*math.tau/64),cy+r*math.cos(i*math.tau/64),cz+z) for i in range(64)])
surface('ClockHousing',case,'GarmentTeal',True,subdivision=1)
face=cylinder('ClockFace',(cx,cy,cz-.075),.112,.008,'Linen');face.rotation_euler.x=math.pi/2
line('ClockBezel',[(cx+.115*math.sin(i*math.tau/96),cy+.115*math.cos(i*math.tau/96),cz-.078) for i in range(97)],.0035,'BrushedMetal')
for i in range(12):
    a=i*math.tau/12;length=.017 if i%3==0 else .010
    line('ClockIndex',[(cx+(.097-length)*math.sin(a),cy+(.097-length)*math.cos(a),cz-.081),
                       (cx+.097*math.sin(a),cy+.097*math.cos(a),cz-.081)],.002 if i%3==0 else .0012,'GarmentCharcoal')
for a,length,r,mat in ((-.95,.055,.003,'GarmentCharcoal'),(1.08,.086,.002,'GarmentCharcoal'),(3.5,.087,.0009,'Canvas')):
    line('ClockHand',[(cx-.012*math.sin(a),cy-.012*math.cos(a),cz-.085),
                      (cx+length*math.sin(a),cy+length*math.cos(a),cz-.085)],r,mat)
ellipsoid('ClockHandPin',(cx,cy,cz-.085),(.005,.005,.002),'BrushedMetal')
for side in (-1,1):
    x=cx+side*.079
    line('BellSupport',[(x,cy+.092,cz),(x,cy+.135,cz)],.009,'BrushedMetal')
    lathe_prop('ClockBell',(x,cy+.132,cz),[(0,.044),(.028,.043),(.052,.033),(.066,.012),(.067,0),(.060,-.002),(.055,.012),(.023,.035),(0,.037)],'BrushedMetal')
    line('ClockFoot',[(cx+side*.084,cy-.085,cz),(cx+side*.103,.912,cz-.015)],.009,'BrushedMetal')
    ellipsoid('ClockRubberFoot',(cx+side*.103,.91,cz-.015),(.014,.006,.015),'GarmentCharcoal')
line('ClockCarryHandle',[(cx-.041,cy+.123,cz),(cx-.041,cy+.179,cz),(cx+.041,cy+.179,cz),(cx+.041,cy+.123,cz)],.005,'BrushedMetal')
line('BellHammer',[(cx,cy+.131,cz),(cx+.034,cy+.147,cz)],.004,'BrushedMetal')
unite('AlarmClock',list(set(scene.objects)-before))

# A ceramic plant pot with a contrasting rolled rim and saucer, soil below the rim.
for ob in scene.objects:
    if ob.name.startswith('PlantPot'):
        ob.data.materials.clear();ob.data.materials.append(mats['Ceramic'])
lathe_prop('PlantSaucer',(2.42,2.4875,2.94),[(0,0),(.165,0),(.178,.006),(.178,.017),(.163,.022),(.152,.007),(0,.007)],'Ceramic')
for i in range(19):
    a=i*2.399;r=.023+.083*((i*17)%19)/19
    ellipsoid('PotStone',(2.42+math.cos(a)*r,2.703,2.94+math.sin(a)*r),(.007,.004,.008),'Stone')

# Gravity folds in curtains, with tapered gathers and stitched hems. The rails
# and functional open casements retain their current positions.
remove(('CurtainRing','Curtain.','Curtain'))
# remove() also matches CurtainRail: explicitly recreate the same fixed rod.
line('CurtainRail',[(3.24,3.18,-.45),(3.24,3.18,2.32)],.027,'BrushedMetal')
for index,z in enumerate((-.17,2.04)):
    before=set(scene.objects);rows=[]
    for j in range(65):
        t=j/64;row=[]
        for i in range(65):
            u=i/64
            width=.60+.09*t
            depth=.039*math.sin(u*math.pi*12+.08*t)+.009*math.sin(u*math.pi*24+t*.6)
            depth+=.009*math.sin(t*8+u*4)*t
            row.append((3.195+depth,3.135-2.24*t+.009*math.cos(u*math.pi*12)*t**8,z+(u-.5)*width))
        rows.append(row)
    surface('CurtainFabric',rows,'Linen',thickness=.005,subdivision=1)
    line('CurtainHem',rows[-1],.0025,'Linen')
    for edge in (0,-1):line('CurtainHem',[r[edge] for r in rows],.002,'Linen')
    ob=unite('Curtain_'+str(index),list(set(scene.objects)-before))
    # Sway rotates around the rod instead of the world origin.
    pivot=Vector(co((3.20,3.14,z)));ob.data.transform(Matrix.Translation(-pivot));ob.location=pivot
    for i in range(6):
        zz=z-.25+i*.10
        line('RodRing',[(3.24+.041*math.cos(a),3.18+.041*math.sin(a),zz) for a in (k*math.tau/32 for k in range(33))],.0025,'BrushedMetal')
        line('CurtainHook',[(3.22,3.141,zz),(3.20,3.136,zz)],.002,'BrushedMetal')

# Rectangular folded cartons keep their function; stamped labels add paper detail.
for ob in [o for o in scene.objects if o.name.startswith('PackingLabel')]:
    x,y,z=-ob.location.x,ob.location.z,-ob.location.y
    for i in range(13):
        xx=x-.085+i*.008
        box('CartonBarcode',(xx,y,z-.006),(.002 if i%3 else .004,.043,.001),'GarmentCharcoal',0)
    for yy in (y+.035,y+.025):line('CartonLabelRule',[(x+.03,yy,z-.006),(x+.088,yy,z-.006)],.001,'Stitch')

# The crafted objects get metre-based UVs after joining, keeping weave consistent.
for ob in scene.objects:
    if ob.type!='MESH' or ob.get('msq_exterior') or ob.name.startswith(('WingPivot_', 'Membrane','Dragonfly','CompoundEye','AbdomenSegment','PredatorLeg')):continue
    if not ob.data.uv_layers:
        uv=ob.data.uv_layers.new(name='SurfaceMetres')
        for p in ob.data.polygons:
            axis=max(range(3),key=lambda a:abs(p.normal[a]));a,b=[k for k in range(3) if k!=axis]
            for loop in p.loop_indices:
                v=ob.data.vertices[ob.data.loops[loop].vertex_index].co;uv.data[loop].uv=(v[a],v[b])

# Fine details share a finished prop mesh rather than hundreds of separate draw
# objects. Keep the animated curtains and exact bedside glow renderer names apart.
def group(name,prefixes):
    parts=[o for o in scene.objects if o.type=='MESH' and not o.get('msq_exterior') and o.name.startswith(prefixes)]
    if parts:unite(name,parts)
group('WovenRug',('WovenRug','RugBorder','RugFringe'))
group('PlantArrangement',('PlantPot','PlantSaucer','PotSoil','PotStone','PointedLeaf','LeafStem','LeafVein'))
group('DeskChair',('DeskChair','ChairArmSupport','PaddedArmrest'))
group('WindowTextileHardware',('CurtainRail','RodRing','CurtainHook'))
group('DeskTaskLamp',('DeskLamp','LowerLampArm','UpperLampArm','LampHinge'))
group('WardrobeDoorOpen',('WardrobeDoorOpen','DoorStile','DoorRail','OpenDoorPull'))
group('WardrobeDoorClosed',('WardrobeDoorClosed','ClosedDoorStile','ClosedDoorRail','ClosedDoorPull'))
# Carton barcodes and label rules are spatially partitioned by the nearest box.
boxes=[o for o in scene.objects if o.name.startswith('PackingBox')]
extras=[o for o in scene.objects if o.name.startswith(('PackingTape','PackingLabel','CartonBarcode','CartonLabelRule','CartonTopSplit','CartonFold','WrappedTape','HandlingArrow'))]
def centre(o):return sum((o.matrix_world@Vector(c) for c in o.bound_box),Vector())/8
ownership={o:[] for o in boxes}
for detail in extras:
    nearest=min(boxes,key=lambda o:(centre(o)-centre(detail)).length)
    if (centre(nearest)-centre(detail)).length<.85:ownership[nearest].append(detail)
for index,ob in enumerate(boxes):unite('PackingBox_'+str(index),[ob]+ownership[ob])

# Curve conversion contributes an empty UVMap to joined cloth panels. Replace
# all room UVs after the final joins, so Unity UV0 has physical metre coordinates.
for ob in scene.objects:
    if ob.type!='MESH' or ob.get('msq_exterior') or ob.name.startswith(('WingPivot_', 'Membrane','Dragonfly','CompoundEye','AbdomenSegment','PredatorLeg')):continue
    for old_uv in list(ob.data.uv_layers):ob.data.uv_layers.remove(old_uv)
    uv=ob.data.uv_layers.new(name='SurfaceMetres');uv.active_render=True
    for p in ob.data.polygons:
        axis=max(range(3),key=lambda a:abs(p.normal[a]));a,b=[k for k in range(3) if k!=axis]
        for loop in p.loop_indices:
            v=ob.data.vertices[ob.data.loops[loop].vertex_index].co;uv.data[loop].uv=(v[a],v[b])
