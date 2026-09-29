"""Original bedroom and mosquito models. Run with tools/blender_client.py execute_code --file.
Coordinates below are Unity metres (Y up). The source scene is separate from the user's scene.
"""
import bpy, math, json, random
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/Mosquitoes'
SOURCE = ROOT / 'art-source/Mosquitoes'
for p in (ART/'Models', SOURCE): p.mkdir(parents=True, exist_ok=True)
old_scenes=[s for s in bpy.data.scenes if s.name.startswith('Mosquitoes_OriginalBedroom')]
scene = bpy.data.scenes.new('Mosquitoes_OriginalBedroom_Rebuild')
bpy.context.window.scene = scene
for old in old_scenes:
    for o in list(old.objects):
        if len(o.users_scene)==1:bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(old)
scene.name='Mosquitoes_OriginalBedroom'
scene.unit_settings.system = 'METRIC'
rng = random.Random(613)
palette = {'Walnut':'765943','Wall':'414B68','Ivory':'D8CFC0','Amber':'F0B860','Teal':'285861',
    'Blue':'53687F','Coral':'BB7159','Gold':'A68A49','Paper':'C9BDA2','Black':'202634',
    'Skin':'D59B71','Hair':'694639','Wing':'92ABB9','Glass':'6B8799','Leaf':'426A53','Cardboard':'876D53','Concrete':'626E89','Asphalt':'202A3D','Soil':'293B3E','WindowWarm':'FFD99C','WindowCool':'86BCD9','WindowDark':'182739','ClearGlass':'C5E1ED','WaterTint':'6495AA'}
palette.update({'Facade':'647185','Stone':'9C9C96','Paving':'6D6B69','PavingJoint':'52555A','Brick':'856757','Sage':'627675','Oak':'94704B','Metal':'253039','Bark':'645343','Foliage':'46684D','FoliageLight':'66835C','FoliageDark':'2D5143','Grass':'3B5444','Lantern':'FFE1A7','CarBlue':'526F89','CarRed':'92534B','CarCream':'BEB6A4','CarGreen':'546F66','CarGlass':'243B4C','Chrome':'9DA9AF','Rubber':'1D252A','Headlamp':'B9CDCF','TailLamp':'A5342E','RoadPaint':'AAAFAC'})
mats = {}
for name, color in palette.items():
    rgb=tuple(int(color[i:i+2],16)/255 for i in (0,2,4))
    m=bpy.data.materials.get('MSQ_'+name) or bpy.data.materials.new('MSQ_'+name); m.use_nodes=True; m.diffuse_color=(*rgb,1)
    bs=next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); bs.inputs['Base Color'].default_value=(*rgb,1); bs.inputs['Roughness'].default_value=.78
    mats[name]=m
def co(p): return (-p[0],-p[2],p[1])
def attach(o,name,mat):
    o.name=name; o.data.materials.append(mats[mat]); return o
def box(name,p,s,mat='Walnut',bevel=.025):
    bpy.ops.mesh.primitive_cube_add(size=1,location=co(p)); o=bpy.context.object
    o.scale=(s[0],s[2],s[1]); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Soft crafted edges','BEVEL'); mod.width=min(bevel,min(s)*.25); mod.segments=3
        bpy.ops.object.modifier_apply(modifier=mod.name)
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return attach(o,name,mat)
def ellipsoid(name,p,s,mat='Ivory'):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,radius=1,location=co(p)); o=bpy.context.object
    o.scale=(s[0],s[2],s[1]); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for poly in o.data.polygons: poly.use_smooth=True
    return attach(o,name,mat)
def cylinder(name,p,r,h,mat='Walnut',top=None):
    bpy.ops.mesh.primitive_cone_add(vertices=32,radius1=r,radius2=r if top is None else top,depth=h,location=co(p)); o=bpy.context.object
    mod=o.modifiers.new('Soft rim','BEVEL');mod.width=min(.018,h*.1);mod.segments=3
    bpy.ops.object.modifier_apply(modifier=mod.name)
    for poly in o.data.polygons: poly.use_smooth=True
    return attach(o,name,mat)
def tube(name,points,r,mat):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.bevel_depth=r;curve.bevel_resolution=3
    spline=curve.splines.new('POLY');spline.points.add(len(points)-1)
    for point,p in zip(spline.points,points):point.co=(*co(p),1)
    o=bpy.data.objects.new(name,curve);scene.collection.objects.link(o);o.data.materials.append(mats[mat]);
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH');return bpy.context.object
def cloth(name,origin,width,length,mat,curtain=False):
    verts=[];faces=[]; nx=36; ny=40
    for j in range(ny+1):
        v=j/ny
        for i in range(nx+1):
            u=i/nx
            if curtain:
                p=(origin[0]+.07*math.sin(u*math.pi*12)+.07*math.sin(v*math.pi),origin[1]-v*length,origin[2]+(u-.5)*width)
            else:
                x=(u-.5)*width; z=(v-.5)*length
                edge=max(0,(abs(x)-.72))
                p=(origin[0]+x,origin[1]+.05*math.sin(u*18+v*6)+.035*math.cos(v*20-u*7)-edge*1.25,origin[2]+z)
            verts.append(co(p))
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i;faces.append((a,a+1,a+nx+2,a+nx+1))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);mesh.materials.append(mats[mat])
    for poly in mesh.polygons:poly.use_smooth=True
    solid=o.modifiers.new('Fabric thickness','SOLIDIFY');solid.thickness=.012
    return o

room_revision_two = True
exec(compile((ROOT/'tools/blender/mosquitoes_compact_room.py').read_text(encoding='utf-8'), 'mosquitoes_compact_room.py', 'exec'))

def export(name,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_space_transform=True,path_mode='RELATIVE')
exec(compile((ROOT/'tools/blender/mosquitoes_contact_details.py').read_text(encoding='utf-8'), 'mosquitoes_contact_details.py', 'exec'))
exec(compile((ROOT/'tools/blender/mosquitoes_finish_props.py').read_text(encoding='utf-8'), 'mosquitoes_finish_props.py', 'exec'))
exec(compile((ROOT/'tools/blender/mosquitoes_prop_craft.py').read_text(encoding='utf-8'), 'mosquitoes_prop_craft.py', 'exec'))
exec(compile((ROOT/'tools/blender/mosquitoes_prop_lod.py').read_text(encoding='utf-8'), 'mosquitoes_prop_lod.py', 'exec'))
exec(compile((ROOT/'tools/blender/mosquitoes_room_models.py').read_text(encoding='utf-8'), 'mosquitoes_room_models.py', 'exec'))
exec(compile((ROOT/'tools/blender/mosquitoes_room_finish.py').read_text(encoding='utf-8'), 'mosquitoes_room_finish.py', 'exec'))
bedroom=list(scene.objects);export('Bedroom',bedroom)
exec(compile((ROOT/'tools/blender/mosquitoes_exterior.py').read_text(encoding='utf-8'), 'mosquitoes_exterior.py', 'exec'))
# Wings only: existing roster characters provide the bodies in Unity.
start=set(scene.objects)
for side in (-1,1):
    # Two tapered, curved membranes share a root on the shoulder blades.
    for pair in range(2):
        vs=[co((side*.008,0,0))]; fs=[]
        outline=[]
        for i in range(40):
            t=i/19 if i<20 else (39-i)/19
            spread=.019 if pair==0 else .013
            x=side*(.006+(.145 if pair==0 else .116)*t)
            y=(.10 if pair==0 else .026)*t + math.sin(math.pi*t)*spread*(1 if i<20 else -1)
            z=-.037*t-pair*.008
            outline.append((x,y,z));vs.append(co((x,y,z)))
        for i in range(39):fs.append((0,i+1,i+2))
        mesh=bpy.data.meshes.new('WingMembrane');mesh.from_pydata(vs,[],fs);mesh.update()
        o=bpy.data.objects.new('WingPivot_'+str(side)+'_'+str(pair),mesh);scene.collection.objects.link(o);mesh.materials.append(mats['Wing'])
        for f in mesh.polygons:f.use_smooth=True
        rim=tube('MembraneRim',outline+[outline[0]],.00055,'Wing');rim.parent=o
        for end in (8,14,20,27):
            tip=outline[end];vein=tube('MembraneVein',[(side*.008,0,0),(tip[0]*.65,tip[1]*.50,tip[2]*.60),tip],.00040,'Wing');vein.parent=o
wings=[o for o in scene.objects if o not in start];export('Wings',wings)
for o in wings:
    if o.parent is None:o.location.x-=5
(ART/'Models/Palette.json').write_text(json.dumps(palette,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Mosquitoes.blend'))
print(json.dumps({'bedroom_meshes':len(bedroom),'wing_meshes':len(wings),'source':str(SOURCE/'Mosquitoes.blend')}))
