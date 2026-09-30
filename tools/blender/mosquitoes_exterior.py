"""Courtyard and predator export; presentation-only assets."""
exec(compile((ROOT/'tools/blender/mosquitoes_courtyard.py').read_text(encoding='utf-8'), 'mosquitoes_courtyard.py', 'exec'))
# Predator mesh, origin at thorax; four pivoted membranes animate independently.
before=set(scene.objects)
ellipsoid('DragonflyThorax',(0,0,0),(.055,.05,.11),'Teal')
ellipsoid('DragonflyHead',(0,0,.14),(.07,.05,.06),'Gold')
for side in (-1,1):ellipsoid('CompoundEye',(side*.042,.025,.16),(.043,.045,.035),'Leaf')
for i in range(9):ellipsoid('AbdomenSegment',(0,-i*.006,-.11-i*.04),(.028-i*.0019,.026-i*.0018,.033),'Teal' if i%2 else 'Gold')
for side in (-1,1):
    for pair in range(2):
        rows=[]
        for j in range(25):
            t=j/24;x=side*(.03+.36*t);w=.055*math.sin(math.pi*t)**.65
            rows.append([(x,.015+.018*math.sin(math.pi*t),(.02 if pair==0 else -.07)+.09*t+(i/8-.5)*2*w) for i in range(9)])
        wing=surface('DragonflyWing'+str(side)+'_'+str(pair),rows,'Wing',thickness=.0006,subdivision=1)
        # Set pivot without moving the membrane.
        pivot=Vector(co((side*.03,.015,.02 if pair==0 else -.07)))
        wing.data.transform(Matrix.Translation(-pivot));wing.location=pivot
    for k in range(3):tube('PredatorLeg',[(side*.03,-.025,.08-k*.055),(side*.085,-.07,.08-k*.04),(side*.06,-.12,.13-k*.035)],.004,'Black')
predator=list(set(scene.objects)-before);export('Dragonfly',predator)
for ob in predator:ob.hide_set(True)
