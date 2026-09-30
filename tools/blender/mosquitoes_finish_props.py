"""Hollow drinking glass with a water surface; no opaque cylinder masquerading as glass."""
for ob in list(scene.objects):
    if ob.name.split('.')[0] in ('WaterGlass','Water'):bpy.data.objects.remove(ob,do_unlink=True)
x,z=-2.55,-2.41
profiles=[(.906,.049),(.915,.062),(.94,.063),(1.143,.071),(1.152,.070),(1.152,.065),(1.142,.065),(.927,.056),(.923,.02)]
rows=[[(x+r*math.cos(i*math.tau/64),y,z+r*math.sin(i*math.tau/64)) for i in range(64)] for y,r in profiles]
surface('WaterGlass',rows,'ClearGlass',closed=True,subdivision=1)
cylinder('Water',(x,.994,z),.059,.142,'WaterTint',.064)
# Fine rim catches the bedside light.
tube('GlassRim',[(x+.068*math.cos(i*math.tau/96),1.151,z+.068*math.sin(i*math.tau/96)) for i in range(97)],.002,'ClearGlass')
