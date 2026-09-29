"""Bedroom revision 2: sewn garments, built shoes, luggage and crafted furniture.

Unity metres, Y up. No voxel balloons: cloth panels retain open hems,
necklines and cuffs, and broad tension/compression folds.
"""
from mathutils import Matrix, Vector

def remove(prefixes):
    for ob in list(scene.objects):
        if not ob.get('msq_exterior') and ob.name.startswith(prefixes):
            bpy.data.objects.remove(ob, do_unlink=True)

def material(name, hexcolor, roughness=.75):
    rgb=tuple(int(hexcolor[i:i+2],16)/255 for i in (0,2,4))
    mat=bpy.data.materials.get('MSQ_'+name) or bpy.data.materials.new('MSQ_'+name)
    mat.use_nodes=True;mat.diffuse_color=(*rgb,1)
    bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*rgb,1);bs.inputs['Roughness'].default_value=roughness
    mats[name]=mat;palette[name]=hexcolor

for n,h,r in [('GarmentTeal','48756F',.9),('GarmentSand','C6B79D',.9),
              ('GarmentCharcoal','424A51',.9),('Denim','596F8B',.9),
              ('Canvas','B78262',.9),('ShoeLeather','CAD0C6',.65),
              ('Sole','D3CDBC',.85),('Stitch','B5AEA1',.9),
              ('BrushedMetal','909A9A',.35),('Ceramic','D0B498',.3),
              ('Upholstery','5A6972',.9),('NaturalOak','987957',.65),('Linen','D2C9B8',.9)]:
    material(n,h,r)

def ringmesh(name,rows,mat,thickness=.004,subd=1,cap_start=False,cap_end=False):
    """Loft rings WITHOUT end caps; garments have holes, not solid skin."""
    n=len(rows[0]);verts=[co(p) for row in rows for p in row]
    faces=[((j+1)*n+i,(j+1)*n+(i+1)%n,j*n+(i+1)%n,j*n+i)
           for j in range(len(rows)-1) for i in range(n)]
    if cap_start:faces.append(tuple(range(n)))
    if cap_end:faces.append(tuple((len(rows)-1)*n+i for i in range(n-1,-1,-1)))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);scene.collection.objects.link(ob);mesh.materials.append(mats[mat])
    for p in mesh.polygons:p.use_smooth=True
    if subd:m=ob.modifiers.new('Sewn panels','SUBSURF');m.levels=subd
    if thickness:m=ob.modifiers.new('Cloth edge thickness','SOLIDIFY');m.thickness=thickness
    return ob

def line(name,points,r,mat):
    # Fewer radial segments than the generic helper, with a smooth spline path.
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D'
    curve.bevel_depth=r;curve.bevel_resolution=2
    spl=curve.splines.new('POLY');spl.points.add(len(points)-1)
    for p,v in zip(spl.points,points):p.co=(*co(v),1)
    ob=bpy.data.objects.new(name,curve);scene.collection.objects.link(ob);curve.materials.append(mats[mat])
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.convert(target='MESH');return bpy.context.object

def ribbon(name,path,width,mat,axis=(1,0,0),thickness=.003):
    rows=[[(p[0]+axis[0]*width*s,p[1]+axis[1]*width*s,p[2]+axis[2]*width*s) for s in (-.5,0,.5)] for p in path]
    return surface(name,rows,mat,thickness=thickness,subdivision=1)

# ---- Garments. Local u is across shoulders, v is fabric depth, y is height. ----
remove(('DrapedHoodie','WardrobeGarment','Hanger','ClothesRail','FoldedClothes',
        'HoodiePocketSeam','HoodieRibbing'))

def sweatshirt(name,origin,mat,chair=False,kind='hoodie',phase=0):
    start=set(scene.objects)
    def world(u,y,v):
        if chair:return (origin[0]+u,y,origin[2]+v)
        return (origin[0]+v,y,origin[2]+u)
    bottom=.60 if chair else .90;top=1.43 if chair else 1.70
    height=top-bottom;width=.305 if chair else .252
    def body(u,t,front=True):
        shoulder_start=.86 if chair else .935
        shoulder=max(0,(t-shoulder_start)/(1-shoulder_start))
        shoulder=shoulder*shoulder*(3-2*shoulder)
        x=u*width*(1+.025*math.sin(t*8+phase)-.13*(1-t)**4)*(1-.63*shoulder)
        yy=bottom+height*t
        fold=(.009*math.sin(u*13+t*8+phase)+.004*math.sin(u*31-t*13))*math.sin(math.pi*t*.94)
        fold+=.010*math.sin(t*37+u*4)*math.exp(-((t-.21)/.15)**2)*(1-u*u)
        # Thin collapsed panels; the chair holds the top and the hem gathers below.
        if chair:
            yy+=.018*math.sin(u*8+1)*(1-t)**5
            # Keep the front cloth outside the chair's central metal back support.
            v=(-.155 if front else .11)+fold+(.030 if front else .105)*(1-t)**3
            v+=.016*math.cos(u*9+t*12)*(1-t)**2
        else:
            yy+=.014*math.sin(u*11+phase)*(1-t)**5
            v=(.025 if front else -.025)+fold*(1 if front else -.7)
            v+=.012*math.sin(u*8+phase)*(1-t)
        # Shoulder panels converge to a small neckline rather than a square open torso.
        v=v*(1-shoulder)+((.015 if front else -.015) if not chair else (-.025 if front else .07))*shoulder
        return world(x,yy,v)
    # Two cloth panels share their side seams but retain a neckline and open hem.
    count=49;rows=[]
    for j in range(37):
        t=j/36
        rows.append([body(-1+2*i/(count-1),t,True) for i in range(count)]
                    +[body(1-2*i/(count-1),t,False) for i in range(count)])
    ringmesh('SweatshirtPanels',rows,mat,.005,1)
    hem=rows[0];line('SewnHem',hem+[hem[0]],.0028,mat)
    # Deep ribbed hem, conforming to the gathers instead of floating parallel bars.
    def outside(p,front=True,amount=.007):
        p=list(p);p[2 if chair else 0]+=(-1 if chair else 1)*(1 if front else -1)*amount;return tuple(p)
    ribrows=[[outside(p,k<count) for k,p in enumerate(rows[j])] for j in (0,1,2,3)]
    ringmesh('RibbedWaist',ribrows,mat,.002,1)
    line('WaistBandSeam',ribrows[-1]+[ribrows[-1][0]],.0013,mat)
    for side in (-1,1):
        # An asymmetrically folded sleeve settles onto the right armrest.
        sleeve=[];centres=[]
        for j in range(31):
            t=j/30;uu=side*(width*.91+.12*math.sin(t*math.pi*.7))
            yy=top-(.095 if chair else .04)-(.72 if chair and side<0 else .54 if chair else .60)*t
            vv=(-.058+.035*math.sin(t*math.pi)) if chair else .004
            if chair and side>0:
                uu+=.085*t*t;yy+=.11*math.sin(math.pi*t);vv+=.08*t*t
            centres.append((uu,yy,vv));rad=.077-.037*t
            sleeve.append([world(uu+rad*math.cos(a),yy+.030*math.cos(a),
                                      vv+.030*math.sin(a)+.006*math.sin(t*30+a*3))
                           for a in (i*math.tau/40 for i in range(40))])
        ringmesh('SewnSleeve',sleeve,mat,.004,1,cap_start=True)
        # Avoid a second coplanar surface on the sleeve cuff.
        cuff=[]
        for j,row in enumerate(sleeve[-4:]):
            c=world(*centres[len(centres)-4+j])
            cuff.append([tuple(c[k]+(p[k]-c[k])*1.055 for k in range(3)) for p in row])
        ringmesh('RibbedCuff',cuff,mat,.002,1)
        line('OpenCuff',sleeve[-1]+[sleeve[-1][0]],.0025,mat)
        # The seam follows the sleeve and reads as a garment construction line.
        line('SleeveSeam',[world(u-.045*side,y,v+.031) for u,y,v in centres],.0014,mat)
    neck=rows[-1]
    line('NeckBinding',neck+[neck[0]],.0032,mat)
    if kind=='hoodie':
        # A hollow hood laid over the chair / collapsed behind the hanger neck.
        hood=[]
        for j in range(25):
            t=j/24;row=[]
            for i in range(41):
                a=math.pi*i/40;u=.155*math.cos(a)*(1-.48*t)
                if chair:
                    yy=top-.07+.065*math.sin(a)-.080*t+.025*math.sin(t*math.pi)
                    v=.040+.255*t+.018*math.cos(a*5)*math.sin(t*math.pi)
                else:
                    yy=top-.095+.135*math.sin(a)*(1-.35*t)-.085*t
                    v=-.020-.11*t
                row.append(world(u,yy,v))
            hood.append(row)
        surface('CollapsedHood',hood,mat,thickness=.006,subdivision=1)
        line('HoodOpening',hood[0],.004,mat)
        line('HoodCrownSeam',[r[len(r)//2] for r in hood],.0016,'Stitch')
        for side in (-1,1):
            path=[world(side*.065,top-.10,-.155 if chair else .036),
                  world(side*.073,top-.20,-.158 if chair else .039),
                  world(side*.058,top-.31,-.160 if chair else .034)]
            line('CottonDrawstring',path,.0022,'Linen')
            line('StringAglet',path[-2:][1:]+[tuple(path[-1][k]+(0,-.013,0)[k] for k in range(3))],.003,'BrushedMetal')
        # A shaped pocket with two diagonal hand openings, on the visible panel.
        pocket=[]
        for j in range(15):
            t=.12+.24*j/14;limit=.67 if j<7 else .67-.18*(j-7)/7
            row=[]
            for i in range(29):
                u=(-1+2*i/28)*limit;p=list(body(u,t,True))
                p[2 if chair else 0]+=(-1 if chair else 1)*(.009+.012*math.sin(math.pi*i/28))
                row.append(tuple(p))
            pocket.append(row)
        surface('KangarooPocket',pocket,mat,thickness=.003,subdivision=1)
        for edge in (0,-1):line('PocketOpening',[row[edge] for row in pocket],.0028,mat)
        line('PocketBottom',pocket[0],.0015,'Stitch')
    elif kind=='jacket':
        line('JacketZip',[body(0,t,True) for t in (i/32 for i in range(33))],.003,'BrushedMetal')
        for side in (-1,1):
            line('WeltPocket',[body(side*(.43+.07*t),.2+.15*t,True) for t in (i/12 for i in range(13))],.003,mat)
    else:
        line('ShirtPlacket',[body(0,t,True) for t in (i/24 for i in range(25))],.0028,mat)
        for t in (.17,.32,.47,.62,.77):
            p=body(0,t,True);ellipsoid('ShirtButton',p,(.004,.004,.004),'Paper')
        for side in (-1,1):
            surface('ShirtCollar',[[world(side*.016,top-.095,.039),world(side*.12,top-.035,.034)],
                                  [world(side*.06,top-.15,.051),world(side*.135,top-.10,.04)]],mat,thickness=.003,subdivision=1)
    return unite(name,list(set(scene.objects)-start))

sweatshirt('DrapedHoodie',(1.41,0,1.34),'GarmentTeal',True)
line('ClothesRail',[(-1.9,1.84,2.61),(-1.02,1.84,2.61)],.013,'BrushedMetal')
for i,(mat,kind) in enumerate([('Denim','hoodie'),('GarmentSand','shirt'),('GarmentTeal','hoodie'),('GarmentCharcoal','jacket')]):
    x=-1.77+i*.205
    # Correct axis: each hanger crosses the cabinet depth; hooks sit on the rail.
    line('HangerHook',[(x,1.72,2.61),(x,1.78,2.61),(x,1.81,2.57),(x,1.854,2.59),
                       (x,1.854,2.635),(x,1.823,2.65)],.004,'BrushedMetal')
    line('WoodenHanger',[(x,1.72,2.61),(x,1.66,2.36),(x,1.646,2.34),
                        (x,1.639,2.88),(x,1.66,2.86),(x,1.72,2.61)],.012,'NaturalOak')
    sweatshirt('WardrobeGarment_'+kind+'_'+str(i),(x,0,2.61),mat,False,kind,i*.9)

# Folded knitwear: an outer continuous fold, layered edges and folded sleeves.
for i,mat in enumerate(('Denim','Linen','GarmentTeal')):
    start=set(scene.objects);cy=2.083+i*.092
    rows=[]
    for j in range(25):
        v=j/24;row=[]
        for k in range(41):
            a=k*math.tau/40
            row.append((-1.49+.35*signed(math.cos(a),.38),cy+.036*math.sin(a)+.004*math.sin(v*13+a*3),2.31+.57*v))
        rows.append(row)
    surface('FoldedSweater',rows,mat,closed=True,subdivision=0)
    for yoff in (-.023,0,.023):
        line('FoldedLayer',[(-1.81,cy+yoff,2.31),(-1.49,cy+yoff-.003,2.303),(-1.17,cy+yoff,2.31)],.002,mat)
    unite('FoldedClothes_'+str(i),list(set(scene.objects)-start))

# ---- Shoes: integrated low court sneakers. ----
exec(compile((ROOT/'tools/blender/mosquitoes_court_sneakers.py').read_text(encoding='utf-8'), 'mosquitoes_court_sneakers.py', 'exec'))

# ---- Backpack: gusseted canvas, reinforced base, curved zipper and webbing. ----
remove(('Backpack',))
start=set(scene.objects);cx=.57;cz=-2.13
profiles=[(.04,.225,.12),(.049,.265,.15),(.09,.29,.17),(.25,.295,.165),(.5,.28,.145),
          (.66,.26,.13),(.75,.21,.105),(.806,.13,.073),(.824,.02,.02)]
rows=[]
for y,w,d in profiles:
    rows.append([(cx+w*signed(math.cos(a),.40),y,cz+d*signed(math.sin(a),.48)+.024*y+
                  .006*math.sin(a*8+y*12)*math.exp(-((y-.18)/.18)**2)) for a in (i*math.tau/64 for i in range(64))])
surface('CanvasBody',rows,'Canvas',True,subdivision=1)
surface('ReinforcedBase',rows[:3],'GarmentCharcoal',True,subdivision=1)
# Sewn gussets follow the dome, not a floating zipper loop.
for side in (-1,1):
    path=[(cx+side*w*.94,y,cz-d*.35+.024*y) for y,w,d in profiles[1:-1]]
    line('GussetSeam',path,.002,'Stitch')
zipline=[(cx-.27,.18,cz-.125),(cx-.272,.45,cz-.118),(cx-.23,.65,cz-.09),
         (cx-.16,.76,cz-.068),(cx,.808,cz-.047),(cx+.16,.76,cz-.068),
         (cx+.23,.65,cz-.09),(cx+.272,.45,cz-.118),(cx+.27,.18,cz-.125)]
for offset in (-.004,.004):line('ZipperTape',[(x,y,z+offset) for x,y,z in zipline],.0023,'GarmentCharcoal')
for p in zipline[3:5]:
    line('ZipSlider',[p,(p[0]+.014,p[1]-.015,p[2]-.006),(p[0]+.01,p[1]-.040,p[2]-.01)],.004,'BrushedMetal')
pocket=[]
for j in range(25):
    v=-1+2*j/24;row=[]
    for i in range(33):
        u=-1+2*i/32
        row.append((cx+.227*u*(1-.08*abs(v)**8),.29+.155*v,
                    cz-.164-.052*(max(0,1-u*u)*max(0,1-v*v))**.35))
    pocket.append(row)
surface('FrontPocket',pocket,'Canvas',thickness=.006,subdivision=1)
line('PocketZip',[(cx-.2+i*.4/32,.4+.006*math.cos(i*math.pi/32),cz-.195) for i in range(33)],.003,'GarmentCharcoal')
line('PocketSewnEdge',pocket[0]+[row[-1] for row in pocket]+list(reversed(pocket[-1]))+[row[0] for row in reversed(pocket)],.0018,'Stitch')
box('PackPatch',(cx,.28,cz-.224),(.067,.04,.002),'Linen',.003)
for side in (-1,1):
    path=[]
    for i in range(31):
        t=i/30;path.append((cx+side*(.155+.04*math.sin(math.pi*t)),.71-.57*t,cz+.145+.095*math.sin(math.pi*t)))
    ribbon('PaddedShoulderStrap',path,.055,'GarmentCharcoal',thickness=.01)
    line('StrapTopstitch',[(x+.023,y,z+.006) for x,y,z in path],.0014,'Stitch')
    box('StrapBuckle',(cx+side*.17,.185,cz+.174),(.059,.034,.018),'BrushedMetal',.006)
    sidep=[(cx+side*.293,.15,cz-.07),(cx+side*.302,.36,cz-.08),(cx+side*.279,.43,cz-.03)]
    ribbon('SideSlipPocket',sidep,.12,'Canvas',axis=(0,0,1),thickness=.004)
handle=[(cx+.09*math.cos(a),.774+.125*math.sin(a),cz+.015) for a in (i*math.pi/24 for i in range(25))]
ribbon('WebbingCarryHandle',handle,.032,'GarmentCharcoal',axis=(0,0,1),thickness=.006)
unite('Backpack',list(set(scene.objects)-start))

# ---- Furniture: legs/reveals/hardware replace featureless solid boxes. ----
remove(('WardrobeHandle','OpenHandle','WardrobeDoorOpen','CabinetEdge'))
hinge=Vector(co((-.95,0,2.10)))
doorstart=set(scene.objects)
box('WardrobeDoorOpen',(-1.42,1.36,2.10),(.94,2.43,.038),'NaturalOak',.008)
for x in (-1.83,-1.02):box('DoorStile',(x,1.36,2.074),(.042,2.36,.018),'NaturalOak',.004)
for y in (.205,2.515):box('DoorRail',(-1.425,y,2.074),(.80,.048,.018),'NaturalOak',.004)
line('OpenDoorPull',[(-1.78,1.16,2.04),(-1.78,1.34,2.04)],.008,'BrushedMetal')
# 110 degrees opens the leaf alongside the cabinet instead of masking the contents.
for ob in set(scene.objects)-doorstart:
    ob.matrix_world=Matrix.Translation(hinge)@Matrix.Rotation(math.radians(-110),4,'Z')@Matrix.Translation(-hinge)@ob.matrix_world
for y in (.44,1.4,2.25):
    cylinder('WardrobeHinge',(-.95,y,2.10),.011,.08,'BrushedMetal')
line('ClosedDoorPull',[(-2.065,1.16,2.02),(-2.065,1.34,2.02)],.008,'BrushedMetal')
for ob in scene.objects:
    if ob.name.startswith(('WardrobeDoorClosed','WardrobeSide','WardrobeShelf','DeskTop','NightstandTop','Shelf')):
        for i,m in enumerate(ob.data.materials):
            if m in (mats['Walnut'],mats['Cardboard']):ob.data.materials[i]=mats['NaturalOak']
remove(('Nightstand', 'Drawer', 'DeskKnob', 'DeskDrawer', 'DeskCabinet', 'DrawerBead', 'DeskLeg'))
# A floating carcass above four short legs, with proper drawer gaps.
box('NightstandCase',(-2.85,.455,-2.63),(.95,.70,.86),'Walnut',.016)
for x in (-3.23,-2.47):
    for z in (-2.97,-2.29):box('NightstandFoot',(x,.065,z),(.055,.13,.055),'Walnut',.006)
box('NightstandTop',(-2.85,.86,-2.63),(1.02,.09,.93),'NaturalOak',.015)
for y in (.29,.61):
    box('DrawerReveal',(-2.85,y,-3.068),(.85,.30,.016),'Black',.002)
    box('NightstandDrawer',(-2.85,y,-3.086),(.828,.277,.028),'NaturalOak',.006)
    line('DrawerPull',[(-2.94,y,-3.112),(-2.94,y,-3.127),(-2.76,y,-3.127),(-2.76,y,-3.112)],.006,'BrushedMetal')
box('DeskPedestal',(2.18,.46,2.65),(.73,.75,.81),'Walnut',.015)
for y in (.29,.57):
    box('DeskDrawerReveal',(2.18,y,2.237),(.68,.256,.016),'Black',.002)
    box('DeskDrawer',(2.18,y,2.216),(.656,.232,.032),'NaturalOak',.006)
    line('DeskPull',[(2.09,y,2.195),(2.09,y,2.178),(2.27,y,2.178),(2.27,y,2.195)],.006,'BrushedMetal')
for z in (2.27,2.97):box('DeskTaperedLeg',(.44,.41,z),(.068,.82,.068),'NaturalOak',.008)
box('DeskApron',(.44,.70,2.62),(.055,.12,.73),'NaturalOak',.006)
for x in (.43,2.45):
    for y in (1.85,2.45):
        line('ShelfSteelBracket',[(x,y-.24,3.10),(x,y-.02,3.10),(x,y-.02,2.79),(x,y-.24,3.10)],.01,'BrushedMetal')
remove(('ShelfBracket',))

# Real articulated desk lamp with hinge cylinders, socket and inset diffuser.
remove(('DeskLampArm','DeskLampBase'))
lathe_prop('DeskLampBase',(2.38,.90,2.83),[(0,0),(.14,0),(.147,.012),(.139,.038),(.04,.052),(0,.052)],'GarmentCharcoal')
line('LowerLampArm',[(2.38,.951,2.83),(2.48,1.37,2.81)],.014,'BrushedMetal')
line('UpperLampArm',[(2.48,1.37,2.81),(2.18,1.56,2.70)],.014,'BrushedMetal')
for p in ((2.38,.956,2.83),(2.48,1.37,2.81),(2.18,1.56,2.70)):
    ob=cylinder('LampHinge',p,.030,.037,'GarmentCharcoal');ob.rotation_euler.x=math.pi/2
line('DeskLampCord',[(2.38,.915,2.86),(2.49,.907,2.99),(2.51,.87,3.08),(2.51,.47,3.10)],.003,'Black')
cylinder('DeskLampDiffuser',(2.18,1.425,2.70),.124,.007,'Linen')

# An open notebook with page curl, ruled paper, elastic ribbon and a pencil.
remove(('Notebook',))
for side in (-1,1):
    cxn=1.43+side*.124
    box('NotebookCover',(cxn,.907,2.40),(.247,.008,.327),'GarmentCharcoal',.004)
    rows=[]
    for j in range(25):
        z=2.24+.32*j/24;row=[]
        for i in range(25):
            t=i/24;x=1.43+side*(.008+.235*t)
            row.append((x,.92+.014*math.exp(-t*12)+.004*t**10,z))
        rows.append(row)
    surface('OpenNotebookPages',rows,'Linen',thickness=.009,subdivision=1)
    for k in range(11):
        z=2.27+.025*k
        line('NotebookRule',[(1.43+side*.035,.921,z),(1.43+side*.216,.922,z)],.00045,'Stitch')
line('NotebookBinding',[(1.43,.933,2.24),(1.43,.933,2.56)],.0018,'Stitch')
line('RibbonBookmark',[(1.43,.936,2.26),(1.45,.932,2.23),(1.45,.906,2.18)],.003,'Canvas')
line('PencilBody',[(1.18,.912,2.69),(1.56,.912,2.67)],.006,'NaturalOak')
line('GraphiteTip',[(1.56,.912,2.67),(1.575,.912,2.669)],.002,'Black')

# Soft rug edges and short fringe; material grain uses physical metre UVs.
for z in (-2.90,.66):
    for i in range(87):
        x=-3.18+i*.064
        line('RugFringe',[(x,.015,z),(x+.007,.008,z+(.045 if z>0 else -.045))],.0018,'Linen')

# UVs on the new meshes, evaluated normals and repeatable export transforms.
for ob in scene.objects:
    if ob.type!='MESH' or ob.get('msq_exterior') or ob.name.startswith(('WingPivot_', 'Membrane', 'Dragonfly', 'CompoundEye', 'AbdomenSegment', 'PredatorLeg')):continue
    if not ob.data.uv_layers:
        uv=ob.data.uv_layers.new(name='SurfaceMetres')
        for p in ob.data.polygons:
            axis=max(range(3),key=lambda a:abs(p.normal[a]));a,b=[v for v in range(3) if v!=axis]
            for loop in p.loop_indices:
                v=ob.data.vertices[ob.data.loops[loop].vertex_index].co;uv.data[loop].uv=(v[a],v[b])
