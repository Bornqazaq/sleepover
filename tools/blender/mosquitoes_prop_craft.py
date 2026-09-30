"""Finished bedroom props: open lampshades, bound books, carton folds, botanical leaves."""
def remove_named(prefixes):
    for ob in list(scene.objects):
        if ob.name.startswith(prefixes):bpy.data.objects.remove(ob,do_unlink=True)

def lathe_prop(name,center,profile,material):
    rows=[[(center[0]+r*math.cos(i*math.tau/64),center[1]+y,center[2]+r*math.sin(i*math.tau/64)) for i in range(65)] for r,y in profile]
    return surface(name,rows,material,False,0,1)

# Open, thin-walled shades with rolled hems. Preserve exact lamp names for presentation.
remove_named(('LampShade','DeskLampShade','PendantShade'))
lathe_prop('LampShade',(-3.02,1.38,-2.47),[(.328,0),(.332,.005),(.325,.025),(.204,.405),(.20,.425),(.197,.427),(.195,.415),(.316,.020),(.319,.005),(.328,0)],'Ivory')
for y,r in ((1.387,.330),(1.799,.200)):
    tube('ShadeRolledHem',[(-3.02+r*math.cos(i*math.tau/96),y,-2.47+r*math.sin(i*math.tau/96)) for i in range(97)],.005,'Paper')
lathe_prop('DeskLampShade',(2.18,1.41,2.70),[(.141,0),(.143,.009),(.125,.05),(.075,.16),(.062,.175),(.055,.168),(.065,.155),(.117,.045),(.134,.005),(.141,0)],'Blue')
lathe_prop('PendantShade',(.15,2.69,.10),[(.38,0),(.383,.012),(.35,.07),(.285,.15),(.16,.255),(.14,.27),(.135,.26),(.155,.246),(.278,.14),(.343,.06),(.37,.009),(.38,0)],'Blue')

# Bound books: page blocks stop short of covers, rounded spines and embossed bands.
old=[o for o in scene.objects if o.name.startswith(('BookSpine','StackedBook','DeskBooks','BookCover'))]
remove_named(('BookPages',))
for i,ob in enumerate(old):
    world=ob.matrix_world.copy();dims=ob.dimensions.copy();name=ob.name
    # Existing cube pivots and their authored rotations are retained.
    loc=world.translation;cx,cy,cz=-loc.x,loc.z,-loc.y
    w,h,d=dims.x,dims.z,dims.y;mat=('Blue','Coral','Teal','Ivory')[i%4]
    before=set(scene.objects);vertical=name.startswith('BookSpine')
    if vertical:
        box('PaperBlock',(cx,cy,cz),(max(.012,w-.014),h-.022,d-.02),'Paper',.006)
        for side in (-1,1):box('BoardCover',(cx+side*(w/2-.003),cy,cz),( .006,h,d),mat,.004)
        box('BoundSpine',(cx,cy,cz-d/2),(w,h,.016),mat,.009)
        for yy in (cy-h*.33,cy+h*.33):box('SpineBand',(cx,yy,cz-d/2-.009),(w*.68,.008,.002),'Gold',.001)
        for yy in (cy+.035,cy+.016):box('TitleRule',(cx,yy,cz-d/2-.010),(w*.50,.004,.002),'Paper',0)
    else:
        box('PaperBlock',(cx,cy,cz+.004),(w-.02,max(.012,h-.013),d-.026),'Paper',.004)
        for side in (-1,1):box('BoardCover',(cx,cy+side*(h/2-.003),cz),(w,.006,d),mat,.004)
        box('BoundSpine',(cx,cy,cz-d/2),(w,h,.014),mat,.006)
        for xx in (cx-w*.32,cx+w*.32):box('SpineBand',(xx,cy,cz-d/2-.008),(.012,h*.7,.002),'Gold',0)
        for yy in (-.010,0,.010):
            if abs(yy)<h/2-.009:tube('PageEdge',[(cx-w/2+.012,cy+yy,cz+d/2-.009),(cx+w/2-.012,cy+yy,cz+d/2-.009)],.0007,'Cardboard')
    fresh=unite('BoundBook',list(set(scene.objects)-before))
    # Rotate about the original centre only, keeping the established support height.
    rotate=world.to_quaternion().to_matrix().to_4x4();center=Vector(co((cx,cy,cz)))
    fresh.matrix_world=Matrix.Translation(center)@rotate@Matrix.Translation(-center)@fresh.matrix_world
    if vertical:
        bpy.context.view_layer.update();ev=fresh.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
        lowest=min((ev.matrix_world@v.co).z for v in mesh.vertices);ev.to_mesh_clear()
        fresh.location.z+=(1.8875 if cy<2.4 else 2.4875)-lowest
    bpy.data.objects.remove(ob,do_unlink=True)

# Carton edge folds, top flap split, wrapped tape and printed handling marks.
for ob in [o for o in scene.objects if o.name.startswith(('PackingBox','WardrobeBox'))]:
    p=ob.location;cx,cy,cz=-p.x,p.z,-p.y;sx,sz,sy=ob.dimensions
    tube('CartonTopSplit',[(cx-sx*.48,cy+sy/2+.001,cz),(cx+sx*.48,cy+sy/2+.001,cz)],.0018,'Hair')
    for side in (-1,1):
        tube('CartonFold',[(cx+side*(sx/2-.018),cy-sy/2+.02,cz-sz/2-.001),(cx+side*(sx/2-.018),cy+sy/2-.018,cz-sz/2-.001)],.0015,'Hair')
    box('WrappedTape',(cx,cy+sy*.30,cz-sz/2-.004),(.12,sy*.38,.003),'Paper',.001)
    for dx in (-.07,.07):
        xx=cx+dx;yy=cy-sy*.20;zz=cz-sz/2-.006
        tube('HandlingArrow',[(xx,yy-.036,zz),(xx,yy+.036,zz)],.003,'Hair')
        tube('HandlingArrowHead',[(xx-.016,yy+.016,zz),(xx,yy+.036,zz),(xx+.016,yy+.016,zz)],.003,'Hair')

# A hollow ceramic pot and a plant with folded pointed leaves, each attached to its stem.
remove_named(('PlantPot','PlantLeaf','PlantStem'))
lathe_prop('PlantPot',(2.42,2.4875,2.94),[(.116,0),(.12,.006),(.128,.025),(.155,.228),(.16,.236),(.16,.25),(.148,.257),(.142,.245),(.140,.225),(.115,.035),(.0,.035)],'Coral')
cylinder('PotSoil',(2.42,2.69,2.94),.137,.012,'Soil')
for j in range(9):
    a=j*2.399;length=.28+(j%3)*.06;reach=.14+(j%2)*.035
    stem=[(2.42,2.70,2.94),(2.42+math.cos(a)*reach*.35,2.82,2.94+math.sin(a)*reach*.35)]
    tube('LeafStem',stem,.004,'Leaf');rows=[];vein=[]
    for k in range(25):
        t=k/24;cx=stem[-1][0]+math.cos(a)*reach*t;cz=stem[-1][2]+math.sin(a)*reach*t;yy=2.82+length*t-.13*t*t
        width=.048*math.sin(math.pi*t)**.72;row=[]
        for n in range(9):
            u=-1+n/4;row.append((cx-math.sin(a)*width*u,yy-.025*abs(u)*math.sin(math.pi*t),cz+math.cos(a)*width*u))
        rows.append(row);vein.append((cx,yy+.001,cz))
    surface('PointedLeaf',rows,'Leaf',thickness=.0015,subdivision=1);tube('LeafVein',vein,.001,'FoliageLight')

# Clock dial, bezel and winding hardware: readable as a clock at close flying distance.
for k in range(12):
    a=k*math.tau/12;x=-3.09+.090*math.sin(a);y=1.085+.090*math.cos(a)
    tube('DialIndex',[(x,y,-2.946),(x-.011*math.sin(a),y-.011*math.cos(a),-2.946)],.0022,'Black')
tube('ClockBezel',[(-3.09+.114*math.sin(i*math.tau/96),1.085+.114*math.cos(i*math.tau/96),-2.942) for i in range(97)],.006,'Gold')
tube('ClockTopHandle',[(-3.145,1.225,-2.86),(-3.145,1.269,-2.86),(-3.035,1.269,-2.86),(-3.035,1.225,-2.86)],.009,'Gold')

# Chair armrests are shaped ribbons with padded tops and structural supports.
for side in (-1,1):
    x=1.41+side*.40
    tube('ChairArmSupport',[(x,.47,1.72),(x,.68,1.73),(x,.76,1.63),(x,.76,1.39)],.024,'Black')
    box('PaddedArmrest',(x,.785,1.56),(.09,.045,.39),'Blue',.04)

# Furniture joinery and an actual lever on the entrance door.
remove_named(('DoorKnob',))
cylinder_ob=cylinder('DoorEscutcheon',(-3.395,1.10,1.10),.037,.012,'Gold')
cylinder_ob.rotation_euler.y=math.pi/2
tube('DoorLever',[(-3.38,1.10,1.10),(-3.32,1.10,1.10),(-3.32,1.10,.98)],.013,'Gold')
for z in (.20,):
    for y in (.32,2.06):
        hinge=cylinder('DoorHinge',(-3.40,y,z),.012,.12,'Gold')
for x in (-2.91,-.95):
    for y in (.13,2.66):box('CabinetEdge',(x,y,2.61),(.13,.045,1.00),'Cardboard',.015)
for y in (.30,.61):
    # Small recessed border follows each bedside drawer face.
    for x in (-3.235,-2.465):box('DrawerBead',(x,y,-3.101),(.016,.19,.012),'Cardboard',.005)
    for yy in (y-.092,y+.092):box('DrawerBead',(-2.85,yy,-3.101),(.78,.016,.012),'Cardboard',.005)

# A folded cuff, kangaroo pocket seam and ribbed hem give the draped garment structure.
for side in (-1,1):
    tube('HoodiePocketSeam',[(1.41+side*.03,.79,1.176),(1.41+side*.18,.80,1.176),(1.41+side*.21,.94,1.176)],.0025,'Blue')
for y in (.735,.747,.759):tube('HoodieRibbing',[(1.17,y,1.194),(1.29,y-.006,1.187),(1.52,y-.006,1.187),(1.66,y,1.194)],.0018,'Blue')

# Give soft assets useful UVs, avoiding a single stretched texel on procedural surfaces.
for ob in scene.objects:
    if ob.type!='MESH' or ob.get('msq_exterior'):continue
    if not ob.data.uv_layers:
        uv=ob.data.uv_layers.new(name='SurfaceUV')
        for p in ob.data.polygons:
            axis=max(range(3),key=lambda a:abs(p.normal[a]));a,b=[v for v in range(3) if v!=axis]
            for loop in p.loop_indices:
                v=ob.data.vertices[ob.data.loops[loop].vertex_index].co;uv.data[loop].uv=(v[a],v[b])
