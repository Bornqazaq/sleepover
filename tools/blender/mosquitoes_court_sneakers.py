"""Low court sneakers. Continuous lasted uppers with a real lined ankle opening.

Executed with the bedroom helpers, using Unity metres and Y up.
"""
import bmesh

remove(('Sneaker', 'Shoe'))
for index, (cx, cz, angle, floor) in enumerate(((-3.01, .26, -.20, .0355),
                                              (-3.01, .90, .18, .002))):
    start = set(scene.objects)
    def sp(x, y, z): return (cx+x, floor+y, cz+z)
    def outline(a, scale=1):
        return (.01+.18*math.cos(a)*scale,
                .067*math.sin(a)*(1+.13*math.cos(a)-.09*math.cos(2*a))*scale)
    angles = [i*math.tau/96 for i in range(96)]
    # A thin rubber bottom and a restrained cup sole, with no overlapping tread tubes.
    for name, mat, profiles in (
        ('RubberOutsole', 'GarmentCharcoal', ((.001,.965),(.004,1),(.008,1),(.010,.985))),
        ('Cupsole', 'Sole', ((.009,.985),(.012,1.015),(.030,1.015),(.037,.98)) )):
        rows = [[sp(x,h,z) for x,z in (outline(a,s) for a in angles)] for h,s in profiles]
        ringmesh(name, rows, mat, 0, 1, True, True)
    # A single continuous shell: toe box, vamp and quarters share the same surface.
    def lasted(a, t, offset=0):
        x,z = outline(a, .96)
        blend = t*t*(3-2*t)
        opening_x = -.085+.069*math.cos(a)
        opening_z = .043*math.sin(a)
        xx = x*(1-blend)+opening_x*blend
        zz = z*(1-blend)+opening_z*blend
        # Rounded, low toe; the heel rises into the ankle opening.
        yy = .035+.099*math.sin(t*math.pi*.5)+.020*math.cos(a)*t
        yy -= .018*max(0,math.cos(a))*math.sin(t*math.pi)
        return sp(xx+offset*math.cos(a), yy, zz+offset*math.sin(a))
    rows = [[lasted(a,t) for a in angles] for t in (0,.025,.08,.16,.27,.40,.54,.67,.78,.87,.94,1)]
    upper = ringmesh('LastedLeatherUpper', rows, 'ShoeLeather', .0025, 2)
    bpy.context.view_layer.objects.active = upper
    for modifier in list(upper.modifiers): bpy.ops.object.modifier_apply(modifier=modifier.name)
    # Ray fitting keeps the tongue, eye stays and laces seated on the curved vamp.
    def top(x,z, lift=0):
        hit,p,n,f = upper.ray_cast(Vector(co(sp(x,.5,z))), Vector((0,0,-1)))
        y = p.z-floor if hit else .148
        return sp(x,y+lift,z)
    # An inward rolled padded collar and dark lining terminate at an inset insole.
    lining = []
    for inward,drop in ((0,0),(.002,-.002),(.007,-.001),(.011,.005),(.012,.013),(.012,.052)):
        row=[]
        for a in angles:
            p=lasted(a,1)
            row.append((p[0]-inward*math.cos(a),p[1]-drop,p[2]-inward*math.sin(a)))
        lining.append(row)
    ringmesh('PaddedAnkleLining', lining, 'GarmentCharcoal', .0015, 1)
    ringmesh('InsetFootbed', [[sp(-.085+.053*math.cos(a),.078,.029*math.sin(a)) for a in angles],
                             [sp(-.085+.002*math.cos(a),.077,.002*math.sin(a)) for a in angles]],
             'GarmentCharcoal', 0, 0, False, True)
    # Panels follow the same last. Their hems do not stick out as detached ribbons.
    for side in (1,):
        heel_rows=[]
        for t in (.14,.20,.37,.59,.77,.83):
            heel_rows.append([lasted(math.pi+side*(-1.15+2.3*k/32),t,.0012) for k in range(33)])
        surface('LeatherHeelCounter',heel_rows,'ShoeLeather',thickness=.001,subdivision=1)
        line('HeelCounterSeam',heel_rows[-1],.0007,'Stitch')
    # Two small sewn side accents, following the curvature of the upper.
    for side in (-1,1):
        for centre in (side*1.37,side*1.70):
            panel=[[lasted(centre+(-.075+.15*k/4),t,.0019) for k in range(5)]
                   for t in (.28,.31,.47,.62,.65)]
            surface('SideLeatherAccent',panel,'GarmentTeal',thickness=.001,subdivision=1)
    toe_seam=[]
    for k in range(49):
        p=lasted(-1.13+2.26*k/48,.40)
        toe_seam.append(top(p[0]-cx,p[2]-cz,.0014))
    line('ToeBoxStitch',toe_seam,.00065,'Stitch')
    # The lightly padded tongue nestles underneath six rows of crossed cotton laces.
    tongue=[]
    for j in range(25):
        t=j/24;x=.123-.157*t
        tongue.append([top(x, (.025-.004*t)*u, .0025+.002*(1-u*u))
                       for u in (-1,-.5,0,.5,1)])
    surface('LeatherTongue',tongue,'ShoeLeather',thickness=.003,subdivision=1)
    for side in (-1,1):
        stay=[]
        for j in range(21):
            t=j/20;x=.112-.132*t;z=side*(.030-.005*t)
            stay.append([top(x,z+side*.008*u,.003) for u in (-.5,0,.5)])
        surface('LeatherEyeStay',stay,'ShoeLeather',thickness=.0015,subdivision=1)
    lacepoints=[]
    for k in range(6):
        t=k/5;x=.110-.129*t;w=.030-.005*t
        lacepoints.append((x,w))
        for side in (-1,1):
            centre=top(x,side*w,.005)
            line('ReinforcedEyelet',[(centre[0]+.002*math.cos(a),centre[1]+.0002,
                                     centre[2]+.002*math.sin(a)) for a in angles[::6]+[angles[0]]],
                 .00065,'GarmentCharcoal')
    for k in range(5):
        x,w=lacepoints[k];nx,nw=lacepoints[k+1]
        for side in (-1,1):
            lace=[]
            for q in range(17):
                t=q/16;xx=x+(nx-x)*t;zz=side*(w*(1-t)-nw*t)
                dx=nx-x;dz=-side*(w+nw);length=math.hypot(dx,dz)
                lace.append([top(xx-dz/length*.003*u,zz+dx/length*.003*u,
                                 .008+.002*math.sin(math.pi*t)+(side+1)*.0008)
                             for u in (-1,0,1)])
            surface('CottonFlatLace',lace,'Linen',thickness=.0013,subdivision=1)
    tie=top(-.019,0,.009)
    for side in (-1,1):
        line('LaceBow',[(tie[0]+.018*math.sin(a),tie[1]+.003*math.sin(a)**2,
                         tie[2]+side*.020*(1-math.cos(a))*.5) for a in [q*math.tau/32 for q in range(33)]],
             .0013,'Linen')
        line('LaceTail',[tie,top(-.040,side*.027,.009),top(-.055,side*.035,.008)],.0013,'Linen')
    # Recalculate each closed surface before joining; sharp soles retain cap normals.
    for ob in set(scene.objects)-start:
        if ob.type!='MESH':continue
        bm=bmesh.new();bm.from_mesh(ob.data)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    shoe=unite('Sneaker_'+str(index),list(set(scene.objects)-start))
    pivot=Vector(co((cx,0,cz)))
    shoe.matrix_world=Matrix.Translation(pivot)@Matrix.Rotation(angle,4,'Z')@Matrix.Translation(-pivot)@shoe.matrix_world
    shoe['msq_room_revision']=2
    shoe['msq_shoe_revision']=3
