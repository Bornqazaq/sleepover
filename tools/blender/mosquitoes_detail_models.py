"""Tailored cloth and furniture. Unity Y-up coordinates, executed by bedroom generator."""
def surface(name, rows, material, closed=False, thickness=0, subdivision=1):
    n=len(rows[0]); vs=[co(p) for row in rows for p in row]; fs=[]
    for j in range(len(rows)-1):
        for i in range(n if closed else n-1):
            k=(i+1)%n; a=j*n;fs.append((a+i,a+k,a+n+k,a+n+i))
    if closed:fs.extend([tuple(range(n-1,-1,-1)),tuple((len(rows)-1)*n+i for i in range(n))])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vs,[],fs);mesh.update()
    ob=bpy.data.objects.new(name,mesh);scene.collection.objects.link(ob);mesh.materials.append(mats[material])
    for p in mesh.polygons:p.use_smooth=True
    if subdivision:
        m=ob.modifiers.new('Tailored surface','SUBSURF');m.levels=subdivision
    if thickness:
        m=ob.modifiers.new('Fabric thickness','SOLIDIFY');m.thickness=thickness
    return ob

def unite(name,objects):
    objects=sorted(objects,key=lambda o:o.name)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True);bpy.context.view_layer.objects.active=o
        for m in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active=objects[0]
    if len(objects)>1:bpy.ops.object.join()
    ob=bpy.context.object;ob.name=name
    # Bake the chosen join object's transform. Stable pivots avoid stale model
    # transform overrides when a repeated export chooses a different first part.
    ob.data.transform(ob.matrix_world);ob.matrix_world.identity()
    return ob

def signed(v,p):return math.copysign(abs(v)**p,v)

# A continuous quad-grid cushion: broad inflated face, pinched rectangular seam.
for zz in (-1.1,):
    before=set(scene.objects)
    for face in (-1,1):
        rows=[]
        for j in range(33):
            v=-1+2*j/32;row=[]
            for i in range(33):
                u=-1+2*i/32
                x=.40*u*(1-.07*abs(v)**6);z=.57*v*(1-.07*abs(u)**6)
                height=.112*(max(0,1-u*u)*max(0,1-v*v))**.4
                row.append((-2.61+x,.712+height if face>0 else .674+.038*(1-height/.112),zz+z))
            rows.append(row)
        surface('PillowPanel',rows,'Ivory',thickness=.003,subdivision=1)
    seam=[]
    for u,v in [(i/16-1,-1) for i in range(33)]+[(1,i/16-1) for i in range(33)]+[(1-i/16,1) for i in range(33)]+[(-1,1-i/16) for i in range(33)]:
        seam.append((-2.61+.40*u*(1-.07*abs(v)**6),.712,zz+.57*v*(1-.07*abs(u)**6)))
    tube('PillowSeam',seam,.0035,'Paper');unite('Pillow',list(set(scene.objects)-before))

# Cloth is in contact with the mattress; excess length turns vertically over its edge.
def bed_surface(x,v,extra=0):
    half=.915; av=abs(v); outside=max(0,av-half)
    z=-1.1+math.copysign(min(av,half)+.025*(1-math.exp(-outside*25)),v)
    # Broad shallow gathers, never the unsupported crest of the previous throw.
    fold=.008+.025*(.5+.5*math.sin(x*9+v*2))**4+.015*(.5+.5*math.cos(v*12-x*4))**6
    contact=.683+fold*math.exp(-outside*20)+extra
    y=contact-outside
    xedge=max(0,x+.12)
    if xedge>0:x=.12+.02*(1-math.exp(-xedge*25));y-=xedge
    return (x,y,z)

for name,x0,x1,extent,extra,mat in [('RumpledDuvet',-1.93,.40,1.35,0,'Blue'),('FoldedThrow',-1.43,-.73,1.36,.023,'Coral')]:
    rows=[]
    for j in range(81):
        v=-extent+2*extent*j/80;row=[]
        for i in range(57):
            u=i/56;x=x0+(x1-x0)*u+.025*math.sin(v*3)*(math.sin(math.pi*u)**2)
            row.append(bed_surface(x,v,extra))
        rows.append(row)
    ob=surface(name,rows,mat,thickness=.012,subdivision=1)
    # Visible fine stitched hem follows the same gravity-conforming surface.
    parts=[ob]
    for u in (0,56):parts.append(tube('SewnHem',[r[u] for r in rows],.0025,'Paper' if mat=='Blue' else 'Coral'))
    unite(name,parts)

# Sewn canvas pack: shaped cross sections, flat base, fuller lower body and domed crown.
before=set(scene.objects);cx=.57;cz=-2.12
profiles=[(.055,.22,.12),(.065,.28,.17),(.10,.315,.195),(.25,.325,.205),(.47,.31,.19),(.65,.28,.165),(.77,.22,.125),(.825,.13,.075),(.842,.012,.012)]
rows=[]
for y,w,d in profiles:
    row=[]
    for i in range(64):
        a=math.tau*i/64;xx=w*signed(math.cos(a),.62);zz=d*signed(math.sin(a),.65)
        # Slight compression and material creases near the sewn bottom.
        zz+=.006*math.sin(a*7+y*12)*math.exp(-((y-.15)/.16)**2)
        row.append((cx+xx,y,cz+zz+.035*y))
    rows.append(row)
surface('PackShell',rows,'Coral',True,subdivision=2)
# Pocket has a tailored rectangular face with rounded corners, sunk into the shell.
pocket=[]
for z,w,h in [(-.168,.20,.15),(-.205,.252,.184),(-.255,.244,.178),(-.279,.20,.137)]:
    pocket.append([(cx+w*signed(math.cos(i*math.tau/64),.4),.295+h*signed(math.sin(i*math.tau/64),.4),cz+z) for i in range(64)])
surface('PackPocket',pocket,'Cardboard',True,subdivision=2)
# Perimeter seam and paired zip rails trace the crown and straight sides.
path=[(cx-.286,.17,cz-.118),(cx-.29,.48,cz-.103),(cx-.245,.68,cz-.089),(cx-.16,.78,cz-.060),(cx,.809,cz-.035),(cx+.16,.78,cz-.060),(cx+.245,.68,cz-.089),(cx+.29,.48,cz-.103),(cx+.286,.17,cz-.118)]
for off in (-.005,.005):tube('MainZip',[(x,y,z+off) for x,y,z in path],.003,'Gold')
tube('PocketSeam',[(cx+.242*signed(math.cos(i*math.tau/96),.4),.295+.174*signed(math.sin(i*math.tau/96),.4),cz-.26) for i in range(97)],.003,'Coral')
for side in (-1,1):
    pts=[]
    for i in range(33):
        t=i/32;y=.71-.59*t;z=cz+.16+.12*math.sin(math.pi*t)
        x=cx+side*(.17+.045*math.sin(math.pi*t))
        pts.append([(x-.032,y,z),(x+.032,y,z)])
    surface('PaddedStrap',pts,'Coral',thickness=.015,subdivision=2)
handle=[]
for i in range(25):
    a=math.pi*i/24;x=cx+.105*math.cos(a);y=.795+.125*math.sin(a)
    handle.append([(x,y,cz+.015),(x,y,cz+.055)])
surface('CarryLoop',handle,'Cardboard',thickness=.012,subdivision=2)
for x,y,z in [path[1],(cx+.19,.459,cz-.276)]:
    tube('ZipPull',[(x,y,z),(x-.017,y-.038,z-.008),(x+.007,y-.048,z-.008),(x+.02,y-.012,z)],.006,'Gold')
unite('Backpack',list(set(scene.objects)-before))

# Upholstered task chair: continuous shaped seat/back shells with piping and real casters.
before=set(scene.objects)
def pad(name,center,w,h,depth,vertical):
    rows=[]
    for j in range(17):
        a=math.pi*j/16;k=max(.015,math.sin(a));row=[]
        for i in range(64):
            t=math.tau*i/64;x=w*signed(math.cos(t),.48)*k;y=h*signed(math.sin(t),.48)*k
            d=depth*math.cos(a)
            row.append((center[0]+x,center[1]+(y if vertical else d),center[2]+(d if vertical else y)))
        rows.append(row)
    return surface(name,rows,'Blue',True,subdivision=1)
pad('SeatUpholstery',(1.41,.54,1.67),.40,.37,.070,False)
pad('BackUpholstery',(1.41,1.03,1.34),.36,.39,.063,True)
tube('BackSupport',[(1.41,.40,1.61),(1.41,.44,1.37),(1.41,.80,1.26),(1.41,1.12,1.28)],.037,'Black')
cylinder('GasLift',(1.41,.28,1.67),.045,.44,'Gold')
cylinder('LiftShroud',(1.41,.24,1.67),.07,.25,'Black')
for i in range(5):
    a=i*math.tau/5;x=1.41+math.cos(a)*.40;z=1.67+math.sin(a)*.40
    tube('CastBase',[(1.41,.19,1.67),(1.41+math.cos(a)*.21,.15,1.67+math.sin(a)*.21),(x,.095,z)],.035,'Black')
    for off in (-.024,.024):
        o=cylinder('Caster',(x+off,.065,z),.057,.033,'Black');o.rotation_euler.y=math.pi/2
unite('DeskChair',list(set(scene.objects)-before))

# One connected sweatshirt outline, front/back lofted panels and hollow folded hood.
before=set(scene.objects)
rows=[]
for y,w,d in [(.72,.265,.062),(.74,.272,.069),(.90,.285,.075),(1.12,.305,.09),(1.29,.32,.085),(1.35,.255,.072)]:
    row=[]
    for i in range(48):
        a=i*math.tau/48
        row.append((1.41+w*signed(math.cos(a),.52),y,1.27+d*signed(math.sin(a),.45)+.009*math.sin(a*8+y*17)))
    rows.append(row)
surface('SweatshirtTorso',rows,'Teal',True,subdivision=2)
for side in (-1,1):
    rows=[]
    for j in range(17):
        t=j/16; x=1.41+side*(.275+.145*math.sin(t*math.pi*.65));y=1.30-.64*t
        r=.09-.032*t
        rows.append([(x+r*math.cos(i*math.tau/32),y,1.26+r*.77*math.sin(i*math.tau/32)+.008*math.sin(t*27)) for i in range(32)])
    surface('SweatshirtSleeve',rows,'Teal',True,subdivision=2)
# Sewn sleeve intersections are fused into the same surface; no box body or floating tubes.
body=unite('Sweatshirt',list(set(scene.objects)-before))
m=body.modifiers.new('Continuous garment','REMESH');m.mode='VOXEL';m.voxel_size=.009
bpy.context.view_layer.objects.active=body;bpy.ops.object.modifier_apply(modifier=m.name)
m=body.modifiers.new('Soften cloth junctions','SMOOTH');m.factor=.75;m.iterations=3;bpy.ops.object.modifier_apply(modifier=m.name)
for poly in body.data.polygons:poly.use_smooth=True
hood=[]
for j in range(17):
    v=j/16;row=[]
    for i in range(25):
        a=math.pi*i/24
        row.append((1.41+.18*math.cos(a)*(1-.55*v),1.27+.10*math.sin(a)+.018*v,1.27-.20*v+.04*math.sin(a)))
    hood.append(row)
surface('SoftHood',hood,'Teal',thickness=.012,subdivision=2)
tube('HoodRim',hood[-1],.007,'Blue')
for x in (-.11,.11):tube('Drawstring',[(1.41+x,1.35,1.11),(1.41+x*.8,1.24,1.10),(1.41+x,1.16,1.095)],.003,'Paper')
garment=unite('DrapedHoodie',list(set(scene.objects)-before))
# Real garments on the rail, replacing the old ellipsoidal placeholders.
from mathutils import Matrix
for i in range(5):
    ob=garment.copy();ob.data=garment.data.copy();scene.collection.objects.link(ob);ob.name='WardrobeGarment'
    center=Vector(co((1.41,1.06,1.27)));target=Vector(co((-1.83+i*.16,1.41,2.60)))
    ob.matrix_world=Matrix.Translation(target)@Matrix.Rotation(math.pi/2,4,'Z')@Matrix.Diagonal(Vector((.8,.6,.8,1)))@Matrix.Translation(-center)@garment.matrix_world
    for k in range(len(ob.data.materials)):
        if ob.data.materials[k]==mats['Teal']:ob.data.materials[k]=mats[('Blue','Ivory','Teal','Coral','Black')[i]]
