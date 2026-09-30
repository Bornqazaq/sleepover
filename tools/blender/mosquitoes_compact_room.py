"""Compact reference bedroom geometry. Executed by mosquitoes_bedroom.py with its helpers."""
# Seven metres across: furniture occupies the room, with an open central flight lane.
box('RoomFloor',(0,-.12,0),(7.2,.24,6.4),'Walnut',0)
box('Ceiling',(0,3.52,0),(7.2,.24,6.4),'Wall',0)
box('SideWall',(-3.72,1.7,0),(.24,3.4,6.4),'Wall',0)
# Actual open aperture connected to the exterior spawning volume.
for y,h in [(.51,1.02),(3.22,.36)]:box('WindowWallLintel',(3.72,y,0),(.24,h,6.4),'Wall',0)
for z,w in [(-1.64,3.12),(2.58,1.24)]:box('WindowWallPier',(3.72,2.03,z),(.24,2.02,w),'Wall',0)
for z in (-3.32,3.32): box('EndWall',(0,1.7,z),(7.2,3.4,.24),'Wall',0)
for x in (-3.56,3.56): box('Skirting',(x,.09,0),(.08,.18,6.4),'Walnut',.012)
for z in (-3.16,3.16): box('Skirting',(0,.09,z),(7.2,.18,.08),'Walnut',.012)
for i in range(24):
    x=-3.45+i*.30
    box('FloorSeam',(x,.002,0),(.004,.004,6.38),'Black',0)
    for j in range(4): box('FloorJoint',(x+.15,.003,-2.6+j*1.7+(i%3)*.51),(.297,.004,.005),'Black',0)
box('WovenRug',(-.4,.018,-1.12),(5.65,.035,3.65),'Blue',.01)
for z in (-2.86,.62):box('RugBorder',(-.4,.037,z),(5.5,.006,.035),'Teal',.002)

# Bed extends left-to-right like the supplied bedside reference.
box('BedFrame',(-1.6,.28,-1.1),(3.12,.34,1.95),'Walnut',.055)
box('Mattress',(-1.6,.54,-1.1),(3,.25,1.85),'Ivory',.11)
box('Headboard',(-3.17,.87,-1.1),(.16,1.48,2.05),'Walnut',.07)
box('HeadboardInset',(-3.073,.98,-1.1),(.025,.78,1.76),'Cardboard',.02)
for x in (-2.94,-.26):
    for z in (-1.91,-.29):box('BedLeg',(x,.15,z),(.17,.30,.17),'Walnut')
# Bed textiles are tailored surfaces in mosquitoes_detail_models.py.

# Warm foreground lamp and recognisable bedside objects.
box('Nightstand',(-2.85,.41,-2.63),(.95,.82,.86),'Walnut',.045)
box('NightstandTop',(-2.85,.86,-2.63),(1.02,.09,.93),'Cardboard',.04)
for y in (.30,.61):
    box('Drawer',(-2.85,y,-3.077),(.84,.25,.035),'Walnut',.02)
    ellipsoid('DrawerKnob',(-2.85,y,-3.108),(.045,.033,.025),'Gold')
cylinder('LampBase',(-3.02,.936,-2.47),.21,.06,'Gold')
cylinder('LampStem',(-3.02,1.25,-2.47),.027,.64,'Gold')
cylinder('LampShade',(-3.02,1.59,-2.47),.33,.43,'Ivory',.20)
ellipsoid('WarmBulb',(-3.02,1.48,-2.47),(.075,.075,.075),'Amber')
for k in range(2):
    box('BookCover',(-2.65,.946+k*.075,-2.80),(.35,.066,.27),('Coral','Blue')[k],.01)
    box('BookPages',(-2.65,.946+k*.075,-2.79),(.33,.043,.25),'Paper',.006)
cylinder('WaterGlass',(-2.55,1.07,-2.41),.068,.25,'Glass')
cylinder('Water',(-2.55,1.04,-2.41),.063,.16,'Glass')
clock=ellipsoid('AlarmClock',(-3.09,1.085,-2.86),(.13,.15,.065),'Teal')
face=cylinder('ClockFace',(-3.09,1.085,-2.936),.111,.012,'Paper');face.rotation_euler.x=math.pi/2
for x in (-3.18,-3.00):ellipsoid('ClockBell',(x,1.23,-2.86),(.07,.05,.057),'Gold')
tube('ClockHands',[(-3.15,1.14,-2.948),(-3.09,1.085,-2.948),(-3.03,1.085,-2.948)],.006,'Black')

# Wardrobe against the back wall; a real recessed interior and open door.
box('WardrobeBack',(-1.93,1.35,3.08),(2.02,2.56,.09),'Black')
for x in (-2.91,-.95):box('WardrobeSide',(x,1.35,2.61),(.11,2.62,.96),'Walnut',.035)
for y in (.14,1.98,2.63):box('WardrobeShelf',(-1.93,y,2.61),(2.06,.11,.96),'Walnut',.025)
box('WardrobeDoorClosed',(-2.43,1.36,2.10),(.94,2.43,.11),'Walnut',.04)
door=box('WardrobeDoorOpen',(-.79,1.36,1.73),(.91,2.43,.11),'Walnut',.04);door.rotation_euler.z=math.radians(-62)
ellipsoid('WardrobeHandle',(-2.06,1.23,2.025),(.043,.043,.03),'Gold')
ellipsoid('OpenHandle',(-.61,1.23,1.44),(.043,.043,.03),'Gold')
tube('ClothesRail',[(-1.89,1.84,2.61),(-1.03,1.84,2.61)],.015,'Gold')
for i in range(5):
    x=-1.83+i*.16
    tube('Hanger',[(x,1.85,2.61),(x-.095,1.73,2.61),(x+.095,1.73,2.61),(x,1.85,2.61)],.007,'Gold')
for i in range(3):box('FoldedClothes',(-1.49,2.08+i*.10,2.59),(.74,.10,.63),('Blue','Paper','Teal')[i],.045)
box('WardrobeBox',(-2.18,2.95,2.61),(.84,.53,.72),'Cardboard',.02)
box('BoxTape',(-2.18,3.22,2.61),(.12,.012,.73),'Paper',.003)

# Desk, drawers, rounded swivel chair, draped hoodie, bookshelves.
box('DeskTop',(1.52,.84,2.62),(2.45,.12,1.00),'Walnut',.04)
for x in (.43,2.62):box('DeskLeg',(x,.41,2.62),(.13,.82,.84),'Walnut')
box('DeskCabinet',(2.18,.46,2.65),(.73,.75,.81),'Walnut')
for y in (.30,.57):
    box('DeskDrawer',(2.18,y,2.215),(.66,.23,.04),'Cardboard',.02)
    ellipsoid('DeskKnob',(2.18,y,2.175),(.037,.03,.029),'Gold')
for i in range(3):box('DeskBooks',(.77,.925+i*.056,2.54),(.46,.052,.33),('Coral','Ivory','Blue')[i],.009)
box('Notebook',(1.43,.915,2.40),(.48,.024,.31),'Paper',.007)
cylinder('DeskLampBase',(2.38,.929,2.83),.145,.045,'Blue')
tube('DeskLampArm',[(2.38,.95,2.83),(2.48,1.37,2.81),(2.18,1.56,2.70)],.023,'Blue')
cylinder('DeskLampShade',(2.18,1.50,2.70),.14,.18,'Blue',.065)
# Sculpted chair and draped hoodie are built by the detail pass.
for y in (1.85,2.45):
    box('Shelf',(1.45,y,2.95),(2.50,.075,.43),'Walnut')
    for x in (.43,2.45):box('ShelfBracket',(x,y-.15,3.04),(.065,.30,.10),'Walnut')
    for j in range(7):
        h=rng.uniform(.24,.38)
        b=box('BookSpine',(.38+j*.12,y+.05+h/2,2.95),(.085,h,.22),('Blue','Coral','Ivory','Teal')[j%4],.006)
        if j==6:b.rotation_euler.y=.24
    for j in range(2):box('StackedBook',(2.04,y+.07+j*.055,2.95),(.45,.055,.24),('Ivory','Blue')[j],.005)
cylinder('PlantPot',(2.42,2.63,2.94),.13,.25,'Coral',.16)
for j in range(7):
    leaf=ellipsoid('PlantLeaf',(2.42+math.sin(j)*.13,2.90+(j%3)*.05,2.94+math.cos(j)*.10),(.045,.21,.025),'Leaf');leaf.rotation_euler.y=j*.45

# Tall moonlit right-hand window; deep sill and flowing curtains.
# Open casement replaces the old opaque glowing glass.
for y in (1.02,3.04):box('WindowFrame',(3.49,y,.94),(.16,.11,2.14),'Ivory')
for z in (-.08,1.96):box('WindowFrame',(3.49,2.03,z),(.16,2.02,.11),'Ivory')
# Both casements open outward; frames and handles follow their hinges.
for side in (-1,1):
    before=set(scene.objects)
    center=.94+side*.49
    for y in (1.12,2.94):box('Casement',(3.52,y,center),(.07,.07,.94),'Ivory',.01)
    for z in (center-.435,center+.435):box('Casement',(3.52,2.03,z),(.07,1.82,.07),'Ivory',.01)
    tube('WindowHandle',[(3.45,1.99,center-side*.37),(3.42,1.99,center-side*.37),(3.42,2.13,center-side*.37)],.015,'Gold')
    hinge=Vector(co((3.52,0,.94+side*.96)))
    from mathutils import Matrix
    rotate=Matrix.Rotation(math.radians(-side*62),4,'Z')
    for ob in set(scene.objects)-before:ob.matrix_world=Matrix.Translation(hinge)@rotate@Matrix.Translation(-hinge)@ob.matrix_world
box('WindowSill',(3.36,1.00,.94),(.50,.12,2.28),'Ivory',.025)
tube('CurtainRail',[(3.24,3.18,-.45),(3.24,3.18,2.32)],.027,'Gold')
for z in (-.17,2.04):cloth('Curtain',(3.20,3.15,z),.72,2.28,'Ivory',True)
for z in (-.17,2.04):
    for i in range(6):ellipsoid('CurtainRing',(3.21,3.16,z-.32+i*.13),(.034,.048,.025),'Gold')

# Secondary scale cues and lived-in details.
for x,y,z,s in [(2.89,.34,-1.05,.68),(2.91,1.00,-1.03,.63),(2.94,.28,-1.74,.56),(-.25,.31,2.60,.62)]:
    box('PackingBox',(x,y,z),(s,s,s),'Cardboard',.016)
    box('PackingTape',(x,y+s/2+.006,z),(.12,.012,s),'Paper',.002)
    box('PackingLabel',(x,y,z-s/2-.008),(.22,.10,.009),'Paper',.002)
# Tailored backpack built in the detail pass.
box('Door',(-3.55,1.19,.73),(.10,2.38,1.12),'Blue',.018)
for z in (.13,1.33):box('DoorFrame',(-3.47,1.23,z),(.14,2.46,.10),'Ivory')
box('DoorTop',(-3.47,2.44,.73),(.14,.10,1.30),'Ivory')
ellipsoid('DoorKnob',(-3.39,1.10,1.10),(.05,.04,.04),'Gold')
for z in (.38,1.09):box('DoorInset',(-3.487,1.3,z),(.02,1.58,.52),'Teal',.01)
for z in (.30,.65):
    ellipsoid('Shoe',(-3.12,.11,z),(.27,.10,.13),'Ivory')
    ellipsoid('ShoeUpper',(-3.19,.17,z),(.16,.13,.12),'Blue')
cylinder('PendantCable',(.15,3.16,.10),.012,.48,'Black')
cylinder('PendantShade',(.15,2.82,.10),.38,.26,'Blue',.16)
ellipsoid('PendantBulb',(.15,2.72,.10),(.07,.07,.07),'Ivory')

detail_source=(ROOT/'tools/blender/mosquitoes_detail_models.py').read_text(encoding='utf-8')
if globals().get('room_revision_two',False):
    # Revision two builds its own clothes/luggage/shoes. Keep only the useful
    # textile/seat surfaces here, avoiding obsolete voxel remeshing on every rebuild.
    exec(compile(detail_source[:detail_source.index('# Sewn canvas pack')], 'mosquitoes_detail_models.py', 'exec'))
    exec(compile(detail_source[detail_source.index('# Upholstered task chair'):detail_source.index('# One connected sweatshirt')], 'mosquitoes_detail_models.py', 'exec'))
else:
    exec(compile(detail_source, 'mosquitoes_detail_models.py', 'exec'))
