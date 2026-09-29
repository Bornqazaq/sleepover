"""Apartment garden: shaped vehicles, branched foliage, luminaires and layered facades.
All positions use the same Unity-space metres as the bedroom. No gameplay geometry.
"""
import bmesh
from mathutils import Matrix
from mathutils.noise import noise_vector
yard_start=set(scene.objects)
ground=-32.4
light_points=[]

def finish_mesh(name,vs,fs,material,smooth=False):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([co(v) for v in vs],[],fs);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    ob=bpy.data.objects.new(name,mesh);scene.collection.objects.link(ob);mesh.materials.append(mats[material])
    for p in mesh.polygons:p.use_smooth=smooth
    if material=='Clinker':
        uv=mesh.uv_layers.new(name='FacadeMetres')
        for poly in mesh.polygons:
            side=abs(poly.normal.x)>abs(poly.normal.y)
            for li in poly.loop_indices:
                v=mesh.vertices[mesh.loops[li].vertex_index].co
                uv.data[li].uv=((v.y if side else v.x)*.5,v.z)
    return ob

# Facades are batched by building, not the whole site: lights remain local.
chunks={};chunk='Host'
def cb(name,p,s,mat='Stone'):
    vs,fs=chunks.setdefault((chunk,mat),([],[]));base=len(vs)
    vs.extend([(p[0]+x*s[0]/2,p[1]+y*s[1]/2,p[2]+z*s[2]/2) for x,y,z in [(-1,-1,-1),(-1,-1,1),(-1,1,-1),(-1,1,1),(1,-1,-1),(1,-1,1),(1,1,-1),(1,1,1)]])
    fs.extend([tuple(base+i for i in f) for f in [(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)]])

def branch(name,points,radii,mat='Bark',sides=9):
    vs=[];fs=[]
    for j,p in enumerate(points):
        tangent=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
        tangent.normalize();u=tangent.cross(Vector((0,0,1))).normalized();v=tangent.cross(u).normalized()
        for i in range(sides):
            t=i*math.tau/sides;q=Vector(p)+radii[j]*(u*math.cos(t)+v*math.sin(t));vs.append(tuple(q))
        if j:
            for i in range(sides):a=(j-1)*sides+i;b=(j-1)*sides+(i+1)%sides;fs.append((a,b,b+sides,a+sides))
    fs.extend([tuple(range(sides-1,-1,-1)),tuple((len(points)-1)*sides+i for i in range(sides))])
    return finish_mesh(name,vs,fs,mat,True)

def shape_loft(name,rows,mat):
    n=len(rows[0]);vs=[p for row in rows for p in row];fs=[]
    for j in range(len(rows)-1):
        for i in range(n):k=(i+1)%n;fs.append((j*n+i,j*n+k,(j+1)*n+k,(j+1)*n+i))
    fs.extend([tuple(range(n-1,-1,-1)),tuple((len(rows)-1)*n+i for i in range(n))])
    return finish_mesh(name,vs,fs,mat,True)

def tree(x,z,height,seed):
    local=random.Random(seed);before=set(scene.objects)
    branch('TaperedTrunk',[(x,ground+.16,z),(x+.10,ground+height*.36,z-.1),(x-.18,ground+height*.66,z+.07),(x-.05,ground+height*.9,z)], [.19,.14,.085,.025])
    for b in range(7):
        a=b*2.399+local.uniform(-.2,.2);base=height*(.39+b*.052);spread=1.6*(1-b*.075)
        tip=(x+math.cos(a)*spread,ground+base+height*.26,z+math.sin(a)*spread)
        branch('TreeBranch',[(x,ground+base,z),(x+math.cos(a)*spread*.48,ground+base+.35,z+math.sin(a)*spread*.48),tip],[.085,.052,.012])
        for j in range(4):
            center=Vector(tip)+Vector((local.uniform(-.55,.55),local.uniform(-.1,.65),local.uniform(-.55,.55)))
            # Dense sprays of pointed leaves create the silhouette; no sphere crown.
            vs=[];fs=[]
            for leaf in range(46):
                direction=Vector((local.uniform(-1,1),local.uniform(-.7,1),local.uniform(-1,1))).normalized()
                pos=center+direction*local.uniform(.05,.72)
                tangent=Vector((local.uniform(-1,1),local.uniform(.15,.65),local.uniform(-1,1))).normalized()
                cross=tangent.cross(Vector((0,1,0))).normalized();ln=local.uniform(.24,.42);width=ln*.45
                base=len(vs)
                for q in (pos-tangent*ln,pos-tangent*ln*.25+cross*width,pos+Vector((0,.055,0)),pos+tangent*ln*.30+cross*width*.82,pos+tangent*ln,pos+tangent*ln*.30-cross*width*.82,pos-tangent*ln*.25-cross*width):vs.append(tuple(q))
                fs.extend([tuple(base+i for i in face) for face in [(0,1,2),(1,3,2),(3,4,2),(4,5,2),(5,6,2),(6,0,2)]])
            finish_mesh('LeafSpray',vs,fs,['Foliage','FoliageLight','FoliageDark'][(b+j)%3],False)
    return unite('GardenTree',list(set(scene.objects)-before))

def car(x,z,paint,angle=0):
    before=set(scene.objects);rows=[]
    # Rear deck -> curved cabin -> raked windscreen -> bonnet -> nose.
    stations=[(-2.08,.70,.76,.77),(-1.94,.91,.93,.95),(-1.42,.96,1.0,1.03),(-.90,.96,1.01,1.53),(-.65,.95,1.01,1.62),(.28,.95,1.00,1.61),(.92,.94,.98,1.05),(1.70,.91,.91,.95),(2.06,.78,.79,.82)]
    dense=[]
    for a,b in zip(stations[:-1],stations[1:]):
        steps=max(2,int((b[0]-a[0])/.075))
        for k in range(steps):dense.append(tuple(a[i]+(b[i]-a[i])*k/steps for i in range(4)))
    dense.append(stations[-1])
    for zz,w,belt,roof in dense:
        wheel_raise=max([.38]+[.40+math.sqrt(max(0,.43**2-(zz-c)**2)) for c in (-1.32,1.32) if abs(zz-c)<.43])
        rw=w*(.88-.11*min(1,max(0,(roof-1.05)/.45)))
        rows.append([(x-w,ground+wheel_raise,z+zz),(x-w,ground+belt-.12,z+zz),(x-w*.96,ground+belt,z+zz),(x-rw,ground+roof-.04,z+zz),(x-rw*.82,ground+roof,z+zz),(x+rw*.82,ground+roof,z+zz),(x+rw,ground+roof-.04,z+zz),(x+w*.96,ground+belt,z+zz),(x+w,ground+belt-.12,z+zz),(x+w,ground+wheel_raise,z+zz)])
    body=shape_loft('SculptedCoachwork',rows,paint)
    bevel=body.modifiers.new('Pressed panel radii','BEVEL');bevel.width=.055;bevel.segments=3
    def profile(zz):
        for a,b in zip(stations[:-1],stations[1:]):
            if a[0]<=zz<=b[0]:
                t=(zz-a[0])/(b[0]-a[0]);return tuple(a[i]+(b[i]-a[i])*t for i in range(1,4))
    # Glass vertices follow the exact coachwork loft, offset only seven millimetres.
    for side in (-1,1):
        for name,z0,z1 in [('RearDoorGlass',-.83,-.09),('FrontDoorGlass',-.015,.79)]:
            points=[];faces=[]
            for k in range(25):
                zz=z0+(z1-z0)*k/24;w,belt,roof=profile(zz);rw=w*(.88-.11*min(1,max(0,(roof-1.05)/.45)))
                for t in (.10,.87):
                    yy=belt+(roof-.04-belt)*t;xx=w*.96+(rw-w*.96)*t+.008
                    points.append((x+side*xx,ground+yy,z+zz))
                if k:faces.append(((k-1)*2,(k-1)*2+1,k*2+1,k*2))
            finish_mesh(name,points,faces,'CarGlass')
        for zz in (-.68,.53):
            tube('DoorHandle',[(x+side*.963,ground+.94,z+zz-.08),(x+side*.972,ground+.94,z+zz+.08)],.021,'Chrome')
        tube('DoorShutLine',[(x+side*.958,ground+.41,z+.005),(x+side*.969,ground+.9,z+.005),(x+side*.94,ground+1.02,z+.005)],.008,'Rubber')
        tube('SillTrim',[(x+side*.94,ground+.43,z-.8),(x+side*.94,ground+.43,z+.8)],.022,'Rubber')
        mirror=box('WingMirror',(x+side*1.035,ground+1.04,z+.64),(.22,.15,.27),paint,.055)
    for z0,z1 in [(.33,.86),(-1.30,-.82)]:
        points=[]
        for zz,side in [(z0,-1),(z0,1),(z1,1),(z1,-1)]:
            w,belt,roof=profile(zz);rw=w*(.88-.11*min(1,max(0,(roof-1.05)/.45)))
            points.append((x+side*rw*.77,ground+roof+.009,z+zz))
        finish_mesh('RakedGlass',points,[(0,1,2,3)],'CarGlass')
    for side in (-1,1):
        for axle in (-1.32,1.32):
            rings=[]
            for xx,rad in [(-.12,.27),(-.105,.36),(-.065,.385),(.065,.385),(.105,.36),(.12,.27)]:
                rings.append([(x+side*(.90+xx),ground+.385+rad*math.sin(i*math.tau/40),z+axle+rad*math.cos(i*math.tau/40)) for i in range(40)])
            shape_loft('Tyre',rings,'Rubber')
            rings=[[(x+side*1.022,ground+.385+r*math.sin(i*math.tau/40),z+axle+r*math.cos(i*math.tau/40)) for i in range(40)] for r in (.25,.20)]
            shape_loft('AlloyRim',rings,'Chrome')
            for k in range(5):
                a=k*math.tau/5;branch('AlloySpoke',[(x+side*1.024,ground+.385,z+axle),(x+side*1.024,ground+.385+.22*math.sin(a),z+axle+.22*math.cos(a))],[.036,.026],'Chrome',6)
        box('Headlight',(x+side*.57,ground+.72,z+2.055),(.42,.14,.055),'Headlamp',.04)
        box('TailLamp',(x+side*.59,ground+.73,z-2.065),(.35,.115,.045),'TailLamp',.03)
    box('FrontGrille',(x,ground+.50,z+2.055),(.87,.18,.045),'Rubber',.05)
    box('RegistrationPlate',(x,ground+.55,z-2.1),(.43,.11,.026),'Paper',.015)
    ob=unite('ParkedSedan',list(set(scene.objects)-before));pivot=Vector(co((x,ground,z)))
    ob.matrix_world=Matrix.Translation(pivot)@Matrix.Rotation(angle,4,'Z')@Matrix.Translation(-pivot)@ob.matrix_world

def lantern(x,z,h=4.5):
    before=set(scene.objects);y=ground
    branch('LampStandard',[(x,y,z),(x,y+h,z)],[.09,.045],'Metal')
    cylinder('LampFoot',(x,y+.12,z),.16,.24,'Metal')
    cylinder('LanternBase',(x,y+h,z),.24,.075,'Metal')
    cylinder('FrostedLantern',(x,y+h+.22,z),.17,.38,'Lantern',.21)
    cylinder('LanternCap',(x,y+h+.45,z),.28,.13,'Metal',.05)
    for a in range(4):
        t=a*math.pi/2;branch('LanternFrame',[(x+.17*math.cos(t),y+h,z+.17*math.sin(t)),(x+.21*math.cos(t),y+h+.41,z+.21*math.sin(t))],[.014,.014],'Metal',6)
    light_points.append(dict(position=[x,y+h+.1,z],intensity=32,range=10,color=[1,.69,.38]))
    unite('CourtyardLantern',list(set(scene.objects)-before))

exec(compile((ROOT/'tools/blender/mosquitoes_architecture.py').read_text(encoding='utf-8'), 'mosquitoes_architecture.py', 'exec'))

# Continuous ground under the entire quarter, including the rear of the host.
chunk='DistrictGround'
cb('DistrictFoundation',(12,ground-.43,0),(310,.74,310),'Asphalt')
# A paved garden promenade between the building and the access road.
for x in range(2,137,9):
    for z in range(-65,66,9):
        chunk='Ground_%d_%d'%(x,z);cb('Ground',(x,ground-.10,z),(9,.18,9),'Asphalt' if x<16 or x>62 else 'Paving')
chunk='Paths'
for z in range(-54,55,3):
    for x in range(17,63,3):cb('PaverJoint',(x,ground+.003,z),(.018,.008,3),'PavingJoint')
for z in range(-54,55,3):cb('PaverCourse',(39,ground+.004,z),(46,.008,.016),'PavingJoint')
for x in (15.5,62.5):cb('Kerb',(x,ground+.10,0),(.22,.22,117),'Stone')
for z in range(-48,49,6):
    cb('ParkingStripe',(9,ground+.015,z),(5,.015,.075),'RoadPaint')
    cb('RoadDash',(68,ground+.015,z),(.10,.015,2),'RoadPaint')

for index,z in enumerate((-23,0,23)):
    before=set(scene.objects)
    # Rounded low masonry gardens with timber bench edges, asymmetrical planting.
    box('GardenKerb',(29,ground+.22,z),(15,.44,13),'Stone',.65)
    box('GardenEarth',(29,ground+.44,z),(14.45,.06,12.45),'Soil',.5)
    box('GardenLawn',(29,ground+.482,z),(14,.035,12),'Grass',.5)
    for x,zz,h in [(25,z-3,6.6),(32,z+2.8,7.8),(34,z-3.4,5.8)]:tree(x,zz,h,613+index*15+int(x))
    # Low shrubs have smaller clusters and leave trunks visible.
    for j in range(9):
        xx=23+j*1.45;zz=z+5
        ob=ellipsoid('BorderShrub',(xx,ground+.82,zz),(.74,.38,.48),'FoliageDark')
        for v in ob.data.vertices:v.co*=1+.09*math.sin(v.co.x*17+v.co.y*22)
    for side in (-1,1):
        bx=29+side*8.4
        for k in range(12):box('BenchSeat',(bx,ground+.49,z-1.94+k*.125),(.58,.075,.09),'Oak',.025)
        for k in range(4):box('BenchBack',(bx+side*.30,ground+.75+k*.12,z-1.25),(.065,.085,1.48),'Oak',.025)
        for zz in (z-1.83,z-.67):
            branch('BenchBackUpright',[(bx+side*.30,ground+.06,zz),(bx+side*.30,ground+1.13,zz)],[.026,.026],'Metal')
        for zz in (z-1.83,z-.67):branch('BenchLeg',[(bx-.20,ground+.02,zz),(bx-.20,ground+.44,zz),(bx+.20,ground+.44,zz),(bx+.20,ground+.02,zz)],[.035]*4,'Metal')
    # Keep each island as its own light-receiving mesh collection.
    island=list(set(scene.objects)-before)
    groups=[(mat,[o for o in island if len(o.data.materials)==1 and o.data.materials[0]==mat]) for mat in list(mats.values())]
    for mat,group in groups:
        if group:unite('GardenIsland_'+str(index)+'_'+mat.name,group)
    for x in (19,39):
        for zz in (z-7.6,z+7.6):lantern(x,zz)
    # Two catenaries across each sitting pocket, warm globes suspended on cables.
    festoon_start=set(scene.objects)
    for zz in (z-7.6,z+7.6):
        pts=[(19+20*t/40,ground+4.7-.75*math.sin(math.pi*t/40),zz) for t in range(41)]
        tube('FestoonCable',pts,.012,'Metal')
        for i in range(1,12):
            t=i/12;xx=19+20*t;yy=ground+4.7-.75*math.sin(math.pi*t)
            branch('BulbCord',[(xx,yy,zz),(xx,yy-.16,zz)],[.012,.012],'Metal',6)
            ellipsoid('FestoonBulb',(xx,yy-.22,zz),(.07,.095,.07),'Lantern')
    unite('FestoonAssembly',list(set(scene.objects)-festoon_start))
    for zz in (z-5,z+5):
        cylinder('Bollard',(44,ground+.49,zz),.115,.98,'Metal')
        cylinder('BollardLens',(44,ground+.83,zz),.12,.16,'Lantern')
        light_points.append(dict(position=[44,ground+.94,zz],intensity=3,range=4,color=[1,.68,.36]))

for j,z in enumerate((-39,-26,-12,8,26,40)):
    car(9,z,['CarBlue','CarRed','CarCream','CarGreen'][j%4],(-.025,.025)[j%2])
for z in (-45,-12,19,46):lantern(57,z)

# Rear street, long sidewalks and planted strips continue beyond the host.
chunk='RearStreet'
for x in (-17,-43):
    cb('RearSidewalk',(x,ground+.025,0),(6.8,.10,236),'Paving')
    for zz in range(-116,117,4):
        cb('RearPavingJoint',(x,ground+.081,zz),(6.8,.012,.018),'PavingJoint')
for x in (-20.5,-39.5):
    cb('RearKerb',(x,ground+.10,0),(.24,.22,238),'Stone')
for zz in range(-115,116,6):
    cb('RearRoadDash',(-30,ground-.049,zz),(.12,.015,2.3),'RoadPaint')
    cb('RearParkingStripe',(-36,ground-.048,zz),(5,.015,.08),'RoadPaint')
for index,zz in enumerate((-74,-50,-25,0,50,74,98)):
    lantern(-18,zz,4.5)
    lantern(-42,zz+8,4.5)
    car(-36,zz+3,['CarBlue','CarCream','CarGreen'][index%3],0)
for side,zz in enumerate((-62,-27,53,83)):
    chunk='RearGarden_'+str(side)
    cb('RearGardenKerb',(-54,ground+.13,zz),(13,.26,22),'Stone')
    cb('RearGardenSoil',(-54,ground+.275,zz),(12.6,.08,21.6),'Soil')
    cb('RearGardenLawn',(-54,ground+.32,zz),(12.3,.025,21.3),'Grass')
    for offset in (-6,6):
        tree(-54,zz+offset,6.2,1400+side*10+offset)
# Ground on both ends of the block has lawns and cross streets, not a cut edge.
chunk='DistrictEnds'
for zz in (-122,126):
    cb('CrossWalk',(30,ground+.025,zz),(190,.10,6),'Paving')
    cb('DistrictLawn',(36,ground+.07,zz+(-1 if zz<0 else 1)*10),(170,.16,12),'Grass')
    for xx in range(-48,120,24):
        lantern(xx,zz,4.5)
        tree(xx,zz+(-1 if zz<0 else 1)*10,6.2,1700+xx+zz)

# Background garden strips and a lit route to the third entrance.
chunk='FarGarden'
for z in (-15,15):
    cb('FarLawn',(88,ground+.10,z),(30,.22,16),'Grass')
    for x in (77,91,101):
        tree(x,z,6,900+x+int(z));lantern(x,z-9,3.6)
cb('FarWalk',(91,ground+.03,0),(42,.065,4),'Paving')
for x in range(72,112,2):cb('WalkJoint',(x,ground+.07,0),(.02,.012,4),'PavingJoint')
for (group,material),(vs,fs) in chunks.items():finish_mesh(group+'_'+material,vs,fs,material)
exterior=list(set(scene.objects)-yard_start)
for ob in exterior:ob['msq_exterior']=True
export('ResidentialExterior',exterior)
(ART/'Models/ExteriorLights.json').write_text(json.dumps({'lights':light_points},indent=2))
print('Courtyard: %d meshes, %d practical lights'%(len(exterior),len(light_points)))
