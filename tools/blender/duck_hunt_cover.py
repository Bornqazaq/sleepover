"""One original, human-sized cover shape. Lightweight export only; no rendering.
Run in a separate background Blender process with --threads 1.
"""
import bpy
from pathlib import Path

root = Path(__file__).resolve().parents[2]
output = root / 'igruha/Assets/_Project/Art/DuckHuntOriginal/Models'
source = root / 'tools/blender/source/DuckHuntOriginal'
output.mkdir(parents=True, exist_ok=True)
source.mkdir(parents=True, exist_ok=True)
scene = bpy.data.scenes.new('DuckHunt_SingleCover')
bpy.context.window.scene = scene
parts = []

def mat(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1)
    return material

body = mat('DH_Cover_Enamel', (.16, .44, .48))
rim = mat('DH_Cover_Rim', (.85, .74, .48))
base = mat('DH_Cover_Base', (.18, .23, .27))

# X horizontal, Y depth (hunter at -Y), Z height. Narrow shoulders + rounded head.
profile = [(-.34,0),(.34,0),(.34,.80),(.43,1.20),(.39,1.36),
           (.23,1.43),(.23,1.65),(.16,1.79),(.08,1.83),
           (-.08,1.83),(-.16,1.79),(-.23,1.65),(-.23,1.43),
           (-.39,1.36),(-.43,1.20),(-.34,.80)]

def plate(name, points, thickness, material, depth=0):
    count=len(points)
    vertices=[(x,depth+y,z+.08) for y in (-thickness/2,thickness/2) for x,z in points]
    faces=[tuple(reversed(range(count))),tuple(range(count,count*2))]
    faces += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
    obj.data.materials.append(material)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    bevel=obj.modifiers.new('Rounded edges','BEVEL');bevel.width=.018;bevel.segments=2
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    parts.append(obj)

plate('Cover_Rim',profile,.18,rim)
plate('Cover_Face',[(x*.91,.04+z*.955) for x,z in profile],.025,body,-.102)

def box(name,pos,size,material):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos)
    obj=bpy.context.object;obj.name=name;obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    obj.data.materials.append(material)
    bevel=obj.modifiers.new('Soft edge','BEVEL');bevel.width=.018;bevel.segments=2
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    parts.append(obj)

box('Foot',(0,0,.04),(.90,.40,.08),base)
box('ChestBand',(0,-.13,1.06),(.55,.016,.045),rim)
box('ChestBandSmall',(0,-.13,.94),(.36,.016,.024),rim)
for name,loc in [('ForwardMarker',(0,1,0)),('UpMarker',(0,0,1))]:
    obj=bpy.data.objects.new(name,None);obj.location=loc;scene.collection.objects.link(obj);parts.append(obj)
bpy.ops.object.select_all(action='DESELECT')
for obj in parts:obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
bpy.ops.export_scene.fbx(filepath=str(output/'DH_SingleCover.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
bpy.ops.wm.save_as_mainfile(filepath=str(source/'DH_SingleCover.blend'))
print('DH single cover export complete: 0.90 m wide, 1.91 m tall, one shape.')
