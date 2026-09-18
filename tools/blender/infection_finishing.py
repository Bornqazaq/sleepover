"""City closure and crafted playground details. Runs in the shared isolated Blender kit."""
# Opaque buildings with windows on every side: economical distant modules, no open backs.
for variant,h in enumerate((17.8,23.6,14.8)):
 m=Mesh();w=18;d=9;body=('Plaster','Concrete','Brick')[variant]
 m.box((0,h/2,d/2),(w,h,d),body)
 m.box((0,.45,d/2),(w+.15,.9,d+.1),'Concrete')
 m.box((0,h+.12,d/2),(w+.45,.24,d+.45),'Concrete')
 for floor in range(int(h/3)):
  y=1.65+floor*3
  for side in(-1,1):
   z=0 if side<0 else d
   for col in range(6):
    x=-7.5+col*3
    m.box((x,y,z+side*.055),(1.32,1.6,.10),'Glass')
    m.box((x,y-.83,z+side*.18),(1.60,.1,.42),'Concrete')
    for q in(-1,1):m.box((x+q*.72,y,z+side*.12),(.06,1.7,.08),'Ivory')
    if (floor+col+variant)%7==0:m.box((x,y-.33,z+side*.24),(1.5,.14,.06),'Wood')
   for col in range(3):
    z=1.5+col*3;x=side*(w/2+.06)
    m.box((x,y,z),(.1,1.6,1.28),'Glass')
    m.box((x+side*.09,y-.83,z),(.30,.1,1.6),'Concrete')
  m.box((0,y-1.45,-.08),(w,.09,.10),'Sand')
 for x in(-6,0,6):m.box((x,h/2,-.09),(.14,h,.13),'Rust')
 for x in(-5,4):m.box((x,h+.65,4),(1.3,1.3,1.2),'Brick')
 for side in(-1,1):m.rod((side*8.7,.5,-.15),(side*8.7,h,-.15),.05,'Iron',6)
 # Rooftop utility box and parapet are part of the silhouette.
 m.box((1,h+.8,5),(3,1.6,2.5),'Concrete')
 m.box((0,h+.3,-.05),(w,.6,.14),'Concrete')
 m.export('BackdropBlock'+str(variant+1))

# Low garages bridge streets between the taller buildings.
m=Mesh()
for bay in range(3):
 x=-3.6+bay*3.6
 m.box((x,1.5,2),(3.6,3,4),'Brick')
 m.box((x,1.32,-.065),(3.13,2.56,.13),'Blue' if bay!=1 else 'Steel')
 for i in range(13):m.box((x,.15+i*.19,-.145),(3.05,.038,.025),'Iron')
 m.box((x,2.76,-.25),(3.2,.17,.55),'Concrete')
 m.box((x,3.07,1.8),(3.75,.18,4.8),'Iron')
 m.box((x+.6,1.15,-.17),(.10,.2,.05),'Rust')
 m.box((x,2.40,-.17),(.32,.20,.02),'Ivory')
for x in(-5.4,-1.8,1.8,5.4):m.box((x,1.5,-.13),(.20,3,.3),'Concrete')
m.export('GarageRow')

# A small abandoned booth, entirely outside the playable boundary.
m=Mesh();m.box((0,1.25,0),(3,2.5,2.7),'Blue');m.box((0,2.58,0),(3.45,.18,3.15),'Rust')
m.box((0,1.55,-1.37),(2.2,1.0,.08),'Glass');m.box((0,.98,-1.6),(2.65,.12,.55),'Steel')
for x in(-.75,0,.75):m.box((x,1.55,-1.43),(.045,1.0,.055),'Steel')
m.box((0,2.33,-1.44),(2.7,.32,.06),'Yellow')
for j in range(10):m.box((-1.2+j*.25,2.85,-1.2),(.15,.12,.02),'Rust')
m.export('Kiosk')

# Separate metre-space overlays fit the existing collider-safe surfaces.
m=Mesh()
for x in(-4.5,4.5):
 for z in(-3,3):
  m.box((x,.414,z),(.38,.035,.38),'Steel')
  m.rod((x,.433,z),(x,.450,z),.053,'Rust',8)
for z in(-3,3):
 for i in range(22):
  x=-4.3+i*.4
  m.face([(x,.407,z-.09),(x+.29,.407,z-.09),(x+.34,.407,z+.06),(x,.407,z+.08)],'Sand' if i%3 else 'Wood')
# Soft sandy footprints, a disturbed patch and fallen twigs are lower than the step height.
for j in range(14):
 x=-3.6+j*.44;z=.48*math.sin(j*.55)+(j%2)*.22
 m.face([(x-.1,.111,z-.17),(x+.1,.111,z-.17),(x+.09,.111,z+.14),(x-.08,.111,z+.2)],'Concrete')
for j in range(18):
 a=j*math.tau/18;x=1.8+math.cos(a)*.9;z=.5+math.sin(a)*.5
 m.rod((x,.12,z),(x+.18,.15,z+.15),.016,'Sand',5)
for i in range(6):
 x=-3+i*.4;m.rod((x,.13,2.2),(x+.3,.15,2.45),.014,'Wood',5)
m.export('SandboxDetail')

m=Mesh()
for j in range(6):
 a=j*math.tau/6
 for rr in(.55,2.9):
  x=math.cos(a)*rr;z=math.sin(a)*rr
  m.rod((x,.412,z),(x,.44,z),.15,'Steel',10)
  for q in(-1,1):m.rod((x+q*.1,.441,z),(x+q*.1,.46,z),.033,'Iron',6)
 for k in range(9):
  rr=rng.uniform(1,3.75);t=a+rng.uniform(-.25,.25);x=rr*math.cos(t);z=rr*math.sin(t)
  m.face([(x,.413,z),(x+.17,.413,z+.03),(x+.22,.413,z+.16),(x-.07,.413,z+.07)],'Rust')
m.ring((0,1.58,0),.25,.09,'Steel',n=24);m.export('CarouselDetail')

# Faded chalk/paint games with deliberately interrupted lines, all below foot collision.
m=Mesh()
def paint_line(a,b,width=.045,mat='Ivory'):
 ax,az=a;bx,bz=b;dx=bx-ax;dz=bz-az;l=math.hypot(dx,dz);px=-dz/l*width/2;pz=dx/l*width/2
 for i in range(max(2,int(l/.12))):
  if rng.random()<.16:continue
  n=max(2,int(l/.12));u=i/n;v=(i+.87)/n
  m.face([(ax+dx*u+px,.014,az+dz*u+pz),(ax+dx*v+px,.014,az+dz*v+pz),(ax+dx*v-px,.014,az+dz*v-pz),(ax+dx*u-px,.014,az+dz*u-pz)],mat)
for row in range(7):
 for col in((-1,0) if row in(2,4) else (0,)):
  x=col*.85-(.0 if row in(2,4) else .425);z=row*.85
  for a,b in [((x,z),(x+.8,z)),((x+.8,z),(x+.8,z+.8)),((x+.8,z+.8),(x,z+.8)),((x,z+.8),(x,z))]:paint_line(a,b)
  number=([1,2,3,5,6,8,9][row])+(1 if row in(2,4) and col==0 else 0)
  segments={'a':((.25,.63),(.55,.63)),'b':((.55,.63),(.55,.42)),'c':((.55,.42),(.55,.20)),'d':((.25,.20),(.55,.20)),'e':((.25,.20),(.25,.42)),'f':((.25,.42),(.25,.63)),'g':((.25,.42),(.55,.42))}
  for key in {1:'bc',2:'abged',3:'abgcd',4:'fgbc',5:'afgcd',6:'afegcd',7:'abc',8:'abcdefg',9:'abfgcd'}[number]:
   a,b=segments[key];paint_line((x+a[0],z+a[1]),(x+b[0],z+b[1]),.055,'Ivory')
m.export('Hopscotch')

m=Mesh()
for j in range(72):
 if j%9 in(0,1):continue
 a=j*math.tau/72;b=(j+.85)*math.tau/72
 m.face([(4.6*math.cos(a),.014,4.6*math.sin(a)),(4.6*math.cos(b),.014,4.6*math.sin(b)),(4.75*math.cos(b),.014,4.75*math.sin(b)),(4.75*math.cos(a),.014,4.75*math.sin(a))],'Yellow' if j%3 else 'Ivory')
m.export('CarouselMarking')

# Patchwork sunshade and playful reliefs on the existing solid wall panels.
m=Mesh()
for ix in range(10):
 for iz in range(10):
  if (ix==0 and iz in(2,3)) or (iz==9 and ix in(6,7)):continue
  x=-2.5+ix*.5;z=-2.5+iz*.5
  y=2.86+.025*math.sin(ix*.65+iz*.8)
  m.face([(x,y,z),(x+.5,y+.012,z),(x+.5,y+.015,z+.5),(x,y,z+.5)],'Blue' if (ix//3+iz//3)%3==0 else 'Sand' if (ix//3+iz//3)%3==1 else 'Red')
for side in(-1,1):
 for q in(-1,1):
  # Raised childlike sun medallion faces east/west, above the low existing triangle.
  x=side*2.622;z=q*1.65;y=2.05
  m.rod((x,y,z),(x+side*.04,y,z),.23,'Yellow',20)
  for j in range(10):
   a=j*math.tau/10
   m.rod((x+side*.025,y+math.sin(a)*.29,z+math.cos(a)*.29),(x+side*.025,y+math.sin(a)*.4,z+math.cos(a)*.4),.023,'Yellow',5)
  for eye in(-1,1):m.rod((x+side*.047,y+.07,z+eye*.08),(x+side*.057,y+.07,z+eye*.08),.025,'Iron',8)
 for x in(-2.5,-.83,.83,2.5):
  for y in(.27,2.5):m.rod((x,y,side*2.61),(x,y,side*2.64),.045,'Steel',8)
for x in(-2.5,2.5):
 for z in(-2.5,2.5):m.rod((x,2.75,z),(x,2.91,z),.14,'Yellow',12)
m.export('ClimberDetail')

m=Mesh()
for side in(-1,1):
 x=side*1.32
 # A faded decorative cloud below the top platform railing, above head clearance.
 for z,r in((.65,.29),(1.0,.42),(1.4,.32)):
  m.rod((x,2.6,z),(x+side*.035,2.6,z),r,'Blue',20)
 for j in range(6):
  z=.3+j*.32;m.rod((x+side*.045,2.40,z),(x+side*.055,2.40,z),.035,'Ivory',8)
m.export('SlideDetail')
