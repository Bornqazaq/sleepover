"""Contact placement and sewn high-top sneakers."""
if not globals().get('room_revision_two',False):
    exec(compile((ROOT/'tools/blender/mosquitoes_sneakers.py').read_text(encoding='utf-8'),'mosquitoes_sneakers.py','exec'))
# Place whole finished objects/groups by evaluated mesh surface, including modifier output.
def support(objects,height):
    if not objects:return
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();lowest=1e9
    for ob in objects:
        ev=ob.evaluated_get(deps);mesh=ev.to_mesh()
        lowest=min(lowest,min((ev.matrix_world@v.co).z for v in mesh.vertices));ev.to_mesh_clear()
    for ob in objects:ob.location.z+=height-lowest
for prefix,height in [('Backpack',.0355),('DeskChair',0),('Pillow',.665),('PlantPot',2.4875),('WaterGlass',.905)]:
    support([o for o in scene.objects if o.name.split('.')[0]==prefix],height)
# Objects assembled in several meshes move together.
for prefixes,height in [(('AlarmClock','ClockFace','ClockBell','ClockHands'),.905),(('LampBase','LampStem','LampShade','WarmBulb'),.905),(('BookCover','BookPages'),.905),(('DeskLampBase','DeskLampArm','DeskLampShade'),.90),(('DeskBooks',),.90),(('Notebook',),.90)]:
    support([o for o in scene.objects if o.name.startswith(prefixes)],height)
for ob in list(scene.objects):
    if ob.name.startswith(('BookSpine','StackedBook')):
        y=ob.location.z
        if ob.name.startswith('BookSpine'):support([ob],1.8875 if y<2.4 else 2.4875)
# Clock contact feet, plant stems and realistic joined box stacks.
for x in (-3.17,-3.01):tube('ClockFoot',[(x,.925,-2.86),(x,.905,-2.89)],.015,'Gold')
for j in range(7):tube('PlantStem',[(2.42,2.72,2.94),(2.42+math.sin(j)*.10,2.9+(j%3)*.05,2.94+math.cos(j)*.08)],.008,'Leaf')
