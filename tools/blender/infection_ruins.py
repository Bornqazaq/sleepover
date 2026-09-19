"""Second-pass ruin modules. Executed by infection_quarantine.py in its isolated kit scene."""
# A broken neighbourhood has interiors, missing floors and irregular roof lines, not four identical walls.
for variant in range(2):
 m=Mesh(); floors=5 if variant else 4; width=14
 m.box((0,.16,2.5),(14.6,.32,6),'Concrete')
 for f in range(floors):
  y=f*3.05
  for col in range(5):
   x=-5.6+col*2.8
   ruined=(f==floors-1 and col>=3) or (variant==1 and f>=floors-2 and col==4)
   if ruined:
    if col==3:
     m.face([(x-1.4,y,0),(x+.3,y+.04,.35),(x+.6,y,3),(x-1.4,y,5)],'Concrete')
     for j in range(5):m.rod((x-.8+j*.2,y,.2),(x-.6+j*.2,y+.6,.4),.018,'Iron',4)
    continue
   # Floor/roof slabs and the room behind the perforated facade.
   m.box((x,y,2.5),(2.8,.22,5.3),'Concrete')
   m.box((x,y+1.45,5),(2.8,2.9,.18),'Soot')
   m.box((x-1.33,y+1.5,2.5),(.14,3,5),'Brick')
   m.box((x,y+.49,0),(2.8,.78,.28),'Plaster' if variant==0 else 'Concrete')
   m.box((x,y+2.67,0),(2.8,.75,.28),'Plaster' if variant==0 else 'Concrete')
   for side in(-1,1):m.box((x+side*1.08,y+1.6,0),(.65,1.5,.28),'Plaster' if variant==0 else 'Concrete')
   # Raised reveal, remaining glass shards and occasional boards.
   for side in(-1,1):m.box((x+side*.78,y+1.6,-.18),(.075,1.6,.16),'Ivory')
   m.box((x,y+.84,-.28),(1.85,.10,.60),'Concrete')
   if (col+f+variant)%3:
    m.box((x,y+1.6,.08),(1.44,1.42,.055),'Glass')
    m.box((x,y+1.6,-.23),(.06,1.5,.08),'Steel')
   else:
    m.face([(x-.7,y+.9,.08),(x-.25,y+1.2,.08),(x-.7,y+2.2,.08)],'Glass')
    m.face([(x+.7,y+2.3,.08),(x-.35,y+2.3,.08),(x+.5,y+1.8,.08)],'Glass')
    for j in range(2):
     m.face([(x-.8,y+1.1+j*.6,-.27),(x+.8,y+1.25+j*.6,-.27),(x+.8,y+1.38+j*.6,-.27),(x-.8,y+1.23+j*.6,-.27)],'Wood')
   if (col+f)%3==1:
    m.box((x,y+.74,-.9),(2.3,.16,1.7),'Concrete')
    for j in range(9):m.rod((x-1+j*.25,y+.8,-1.65),(x-1+j*.25,y+1.58,-1.65),.021,'Rust',5)
    m.rod((x-1.14,y+1.6,-1.65),(x+1.14,y+1.6,-1.65),.036,'Rust')
    for side in(-1,1):m.rod((x+side*1.14,y+1.6,-1.65),(x+side*1.14,y+1.6,-.1),.035,'Rust')
    if (col+f)%2:m.box((x-.4,y+1.15,-1.68),(.95,.62,.05),'Blue')
   # Water streaks and exposed masonry do not repeat in every bay.
   for j in range(rng.randrange(1,4)):
    q=x+rng.uniform(-1.2,1.2);h=rng.uniform(.25,.65)
    m.face([(q,y+.75,-.155),(q+.10,y+.7,-.16),(q+.06,y+.75-h,-.155),(q-.03,y+.3-h,-.155)],'Soot')
   if rng.random()<.4:
    for j in range(5):m.box((x-.7+(j%3)*.27,y+.3+(j//3)*.13,-.17),(.24,.10,.055),'Brick')
  remaining=8.4 if f==floors-1 else 14
  m.box((-(14-remaining)/2,y+3,2.5),(remaining,.22,5.4),'Concrete')
 # Shattered parapet and jagged exposed wall remnants on the broken roof edge.
 y=floors*3.05
 for j in range(13):
  x=-7+j*.64;h=rng.uniform(.15,1.15)
  m.face([(x,y,-.1),(x+.64,y,-.1),(x+.62,y+h*.6,-.1),(x+.24,y+h,-.1),(x,y+h*.8,-.1)],'Brick')
  if j%3==0:m.rod((x+.2,y,.03),(x+.32,y+h+.5,.1),.025,'Iron',5)
 for j in range(5):
  z=j*.95;h=rng.uniform(.3,1.9)
  m.face([(1.5,y-3,z),(1.5,y-3,z+.85),(1.5,y-3+h*.4,z+.85),(1.5,y-3+h,z+.2)],'Brick')
 # Side structure, a ragged top, drains and television aerials.
 for x in(-7.05,7.05):
  hh=(floors if x<0 else floors-2)*3.05
  m.box((x,hh/2,2.5),(.22,hh,5.3),'Brick')
  m.rod((x,.2,-.25),(x,hh,-.25),.055,'Rust')
 for i in range(15):
  x=rng.uniform(-7,.8);m.box((x,floors*3.05+rng.uniform(.03,.2),rng.uniform(.5,4.5)),(rng.uniform(.25,.65),.22,.30),'Brick',rng.uniform(-1,1))
 for x in(-5,-1):
  y=floors*3.05;m.rod((x,y,3),(x,y+2.7,3),.028,'Iron')
  for h in(.7,1.1,1.5):m.rod((x-.8,y+h,3),(x+.8,y+h,3),.014,'Iron',4)
 m.export('BrokenBlock'+str(variant+1))

# Jagged chunks with thickness, exposed rods and different sizes.
def rock(m,c,r,mat):
 x,y,z=c;sx,sy,sz=r
 p=[(x-sx,y,z-sz*.5),(x+sx*.7,y,z-sz),(x+sx,y,z+sz*.6),(x-sx*.5,y,z+sz),(x-sx*.55,y+sy,z-sz*.5),(x+sx*.5,y+sy*.85,z-sz*.65),(x+sx*.65,y+sy*.7,z+sz*.5),(x-sx*.45,y+sy*.75,z+sz*.55)]
 for face in [(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6),(4,6,7)]:m.face([p[i] for i in face],mat)
m=Mesh()
for i in range(50):
 x=rng.uniform(-2.5,2.5);z=rng.uniform(-1.4,1.4);h=max(0,1-abs(x)/2.5-abs(z)/2.4)
 rock(m,(x,h*.4,z),(rng.uniform(.13,.6),rng.uniform(.18,.7)*(h+.4),rng.uniform(.14,.45)),'Concrete' if i%3 else 'Brick')
for i in range(9):
 x=rng.uniform(-1.6,1.6);m.rod((x,.2,0),(x+rng.uniform(-.4,.4),rng.uniform(.8,1.4),rng.uniform(-.5,.5)),.022,'Iron',5)
m.export('RubblePile')
m=Mesh()
for row in range(9):
 for col in range(12):
  if col+row>15 or (row>4 and col<row-3):continue
  m.box((-2.7+col*.47+(row%2)*.23,.14+row*.27,0),(.44,.245,.5),'Brick')
for x in(-2.9,2.9):m.box((x,1.15,0),(.38,2.3,.7),'Concrete')
m.export('BrokenWall')
# Evacuation canopy: open silhouette, sagging, patched canvas.
m=Mesh()
for x in(-2.5,2.5):
 for z in(-1.6,1.6):m.rod((x,0,z),(x,2.65,z),.055,'Rust')
for ix in range(20):
 for iz in range(12):
  if iz==0 and ix in(3,4,14):continue
  def pt(i,j):
   x=-2.6+i*.26;z=-1.7+j*.285;y=2.7+.45*(1-abs(x)/2.6)-.18*math.sin(j*.55)
   return(x,y,z)
  m.face([pt(ix,iz),pt(ix+1,iz),pt(ix+1,iz+1),pt(ix,iz+1)],'Sand' if (ix//4+iz//3)%5 else 'Blue')
m.export('Canopy')
m=Mesh()
m.box((0,.75,0),(2.2,1.35,1.25),'Blue')
for x in(-1,1):
 for z in(-.5,.5):m.ring((x,.16,z),.16,.07,'Rubber',axis='x',n=12)
for z in(-.65,.65):
 for x in(-.85,-.4,0,.4,.85):m.box((x,.76,z),(.04,1.14,.045),'Steel')
for z in(-.33,.33):m.box((0,1.45,z),(2.35,.10,.64),'Iron')
m.box((0,.84,-.657),(.62,.4,.025),'Ivory');m.export('Dumpster')
m=Mesh()
m.rod((0,0,0),(0,5.8,0),.085,'Iron',10,.055)
m.rod((0,5.8,0),(1,6.1,0),.055,'Iron');m.box((1.2,6.08,0),(.7,.12,.38),'Steel')
m.box((1.2,5.99,0),(.55,.035,.30),'Ivory');m.export('StreetLamp')
m=Mesh()
for i in range(24):
 a=i/24;b=(i+1)/24
 m.rod((-4+8*a,-math.sin(a*math.pi)*.7,0),(-4+8*b,-math.sin(b*math.pi)*.7,0),.022,'Iron',5)
 if i%3==0:
  x=-4+8*a;y=-math.sin(a*math.pi)*.7
  m.face([(x,y,0),(x+.35,y-.03,0),(x+.3,y-.48,0)],'Yellow' if i%2 else 'Red')
m.export('CableFlags')
m=Mesh()
for x in(-2.7,2.7):
 m.box((x,1.9,0),(.3,3.8,.3),'Rust')
 for z in(-1,1):m.rod((x,0,z),(x,2.8,0),.07,'Steel')
m.box((0,3.5,0),(5.8,.68,.20),'Ivory')
for i in range(14):
 x=-2.7+i*.4;m.face([(x,3.18,-.11),(x+.16,3.18,-.11),(x+.5,3.82,-.11),(x+.34,3.82,-.11)],'Rust')
m.export('CheckpointGate')
# Loose rubble underfoot stays below the 0.42 m step threshold.
m=Mesh()
for i in range(36):rock(m,(rng.uniform(-2,2),0,rng.uniform(-1,1)),(rng.uniform(.05,.23),rng.uniform(.035,.12),rng.uniform(.05,.17)),'Brick' if i%2 else 'Concrete')
for i in range(10):
 x=rng.uniform(-2,2);z=rng.uniform(-1,1);m.box((x,.015,z),(.28,.009,.39),'Paper',rng.random()*6)
 for j in range(3):m.box((x,.022,z-.1+j*.07),(.18,.002,.013),'Soot')
m.export('Litter')
