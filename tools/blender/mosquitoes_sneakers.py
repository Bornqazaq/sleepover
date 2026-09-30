"""A pair of canvas high-tops with genuinely open collars and layered stitched panels."""
for ob in list(scene.objects):
    if ob.name.startswith(('Shoe','Sneaker_')):bpy.data.objects.remove(ob,do_unlink=True)
for foot,cz in enumerate((.18,.63)):
    cx=-3.08;before=set(scene.objects)
    def outline(t,scale=1):
        return (cx+.235*math.cos(t)*scale,cz+.093*math.sin(t)*(1+.16*math.cos(t))*scale)
    rows=[]
    for y,scale in [(0,.94),(.006,1),(.027,1),(.035,.985),(.039,.95)]:
        rows.append([(outline(i*math.tau/80,scale)[0],y,outline(i*math.tau/80,scale)[1]) for i in range(80)])
    surface('VulcanisedSole',rows,'Ivory',True,subdivision=1)
    for y in (.011,.030):
        tube('RubberSidewallLine',[(outline(i*math.tau/96)[0],y,outline(i*math.tau/96)[1]) for i in range(97)],.0012,'Blue')
    # Toe/vamp is closed; the tall rear panel is a U-shaped open surface.
    rows=[]
    for x,w,h in [(.232,.002,.003),(.215,.047,.038),(.175,.079,.074),(.10,.089,.098),(.035,.084,.122),(-.035,.076,.145)]:
        rows.append([(cx+x,.036+h*math.sin(i*math.pi/32),cz+w*math.cos(i*math.pi/32)) for i in range(33)])
    surface('CanvasVamp',rows,'Blue',thickness=.005,subdivision=2)
    # Rear quarter wraps the heel, leaves both the foot hole and lace opening empty.
    rows=[]
    for j in range(17):
        t=j/16;row=[]
        for i in range(49):
            a=math.pi*.08+(math.tau-math.pi*.16)*i/48
            xx=-.121+.100*math.cos(a);zz=.074*math.sin(a)
            top=.215+.012*(1-math.cos(a))
            row.append((cx+xx,.039+(top-.039)*t,cz+zz*(1-.08*t)+.002*math.sin(a*5)*math.sin(math.pi*t)))
        rows.append(row)
    surface('CanvasQuarter',rows,'Blue',thickness=.005,subdivision=2)
    tube('PaddedCollar',rows[-1],.006,'Ivory')
    # Insole is deep inside the opening, rather than a cap across the ankle.
    box('Insole',(cx-.12,.044,cz),(.16,.007,.112),'Black',.02)
    tongue=[]
    for j in range(21):
        t=j/20;x=cx+.06-.095*t;y=.148+.088*t
        tongue.append([(x,y-.012*(i/8-.5)**2,cz+(i/8-.5)*.082) for i in range(9)])
    surface('PaddedTongue',tongue,'Blue',thickness=.007,subdivision=2)
    # Narrow reinforced eyestays follow the tongue; five crossed lace runs.
    for side in (-1,1):
        rows=[]
        for j in range(21):
            t=j/20;x=cx+.07-.108*t;y=.137+.088*t
            rows.append([(x,y-.006,cz+side*.062),(x,y+.003,cz+side*.047)])
        surface('Eyestay',rows,'Teal',thickness=.002,subdivision=1)
        tube('EyestayStitch',[r[0] for r in rows],.001,'Paper')
    for k in range(5):
        x=cx+.055-k*.021;y=.153+k*.017
        for side in (-1,1):
            tube('MetalEyelet',[(x+.0035*math.cos(i*math.tau/20),y,cz+side*.049+.0035*math.sin(i*math.tau/20)) for i in range(21)],.0013,'Chrome')
        for side in (-1,1):tube('CottonLace',[(x,y+.004,cz+side*.047),(x-.009,y+.013,cz),(x-.019,y+.022,cz-side*.047)],.0023,'Paper')
    for side in (-1,1):
        pts=[]
        for j in range(33):
            t=j/32;pts.append((cx-.202+.19*t,.077+.035*math.sin(math.pi*t),cz+side*(.053+.023*math.sin(math.pi*t))))
        tube('QuarterStitch',pts,.0013,'Paper')
        # Heel tab is a flat strip that folds over the collar.
    tube('HeelSeam',[(cx-.219,.05,cz),(cx-.222,.14,cz),(cx-.22,.226,cz)],.002,'Teal')
    for side in (-1,1):
        tube('LaceBow',[(cx-.045+.018*math.sin(i*math.tau/32),.225+.002*math.sin(i*math.tau/32),cz+side*.032*(1-math.cos(i*math.tau/32))) for i in range(33)],.0022,'Paper')
    shoe=unite('Sneaker_'+str(foot),list(set(scene.objects)-before))
    pivot=Vector(co((cx,0,cz)));shoe.matrix_world=Matrix.Translation(pivot)@Matrix.Rotation(.12 if foot==0 else -.1,4,'Z')@Matrix.Translation(-pivot)@shoe.matrix_world
    shoe.location.z+=.0355
