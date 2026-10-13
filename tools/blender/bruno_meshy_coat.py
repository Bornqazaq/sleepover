"""Short directional, skinned opaque fur and PBR studio for finished Bruno."""
import bpy, math, random, bisect
from pathlib import Path
from mathutils import Vector, Quaternion
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-source/BrunoMeshy/finished'
scene=bpy.data.scenes['Bruno_Finished'];bpy.context.window.scene=scene
body=bpy.data.objects['Bruno_Body'];rig=bpy.data.objects['BrunoRig']
rig.data.pose_position='REST'
anatomy={};ap=ROOT/'tools/blender/bruno_anatomy.py'
exec(compile(ap.read_text(),str(ap),'exec'),anatomy)
unmap=anatomy['unmap']
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def shade(p):
    p=unmap(p)
    dark=1-smooth(.17,.50,p.z)
    muzzle=smooth(1.64,2.02,p.y-abs(p.x)*.34)*(1-smooth(1.60,1.77,p.z))
    base=Vector((.073,.030,.010)).lerp(Vector((.016,.008,.003)),dark*.88)
    base=base.lerp(Vector((.135,.066,.024)),muzzle)
    saddle=smooth(1.5,2.10,p.z)*(.5+.5*math.sin(p.y*2+1))
    base=base.lerp(Vector((.175,.09,.037)),saddle*.26)
    return base
attr=body.data.color_attributes['Color']
for p in body.data.polygons:
    if p.material_index==0:
        for vi in p.vertices:attr.data[vi].color=(*shade(body.data.vertices[vi].co),1)
for mat in body.data.materials:
    bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    if any(s in mat.name for s in ('Body','Muzzle','Paws')):
        bs.inputs['Roughness'].default_value=.88
        bs.inputs['Sheen Weight'].default_value=.035
    if 'Body_umber' in mat.name:bs.inputs['Base Color'].default_value=(.073,.030,.010,1)
    if 'Muzzle_warm' in mat.name:bs.inputs['Base Color'].default_value=(.135,.066,.024,1)
    if 'Claws' in mat.name:
        bs.inputs['Base Color'].default_value=(.067,.055,.039,1)
        bs.inputs['Roughness'].default_value=.49
    if 'Teeth' in mat.name:bs.inputs['Base Color'].default_value=(.62,.54,.36,1)

# Submillimetre irregular grain survives in a tangent-space bake, unlike
# viewport-only procedural nodes. The muzzle has much shorter fibres.
for mat in body.data.materials:
    nt=mat.node_tree;bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
    tc=nt.nodes.new('ShaderNodeTexCoord');mapping=nt.nodes.new('ShaderNodeVectorMath');mapping.operation='MULTIPLY'
    mapping.inputs[1].default_value=(90,210,70)
    nt.links.new(tc.outputs['Generated'],mapping.inputs[0])
    noise=nt.nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=1;noise.inputs['Detail'].default_value=2.5
    nt.links.new(mapping.outputs['Vector'],noise.inputs['Vector'])
    bump=nt.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.26
    bump.inputs['Distance'].default_value=.012 if 'Body' in mat.name else .0015
    nt.links.new(noise.outputs['Fac'],bump.inputs['Height']);nt.links.new(bump.outputs['Normal'],bs.inputs['Normal'])

body.data.calc_loop_triangles()
eligible=[];areas=[];total=0
for tr in body.data.loop_triangles:
    poly=body.data.polygons[tr.polygon_index]
    mat=body.data.materials[poly.material_index].name
    if not any(n in mat for n in ('Body','Muzzle')):continue
    ids=tuple(tr.vertices);a,b,c=[body.data.vertices[i].co for i in ids]
    n=(b-a).cross(c-a);area=n.length*.5
    if area<1e-10:continue
    total+=area;areas.append(total);eligible.append((ids,poly.material_index))
alltris=[tuple(t.vertices) for t in body.data.loop_triangles]
bvh=BVHTree.FromPolygons([v.co for v in body.data.vertices],alltris,all_triangles=True)
rng=random.Random(756)
verts=[];faces=[];colors=[];weights=[];uvs=[]
for k in range(40000):
    ids,mi=eligible[bisect.bisect_left(areas,rng.random()*total)]
    r=math.sqrt(rng.random());t=rng.random();bary=(1-r,r*(1-t),r*t)
    p=sum((body.data.vertices[i].co*w for i,w in zip(ids,bary)),Vector())
    n=sum((body.data.vertices[i].normal*w for i,w in zip(ids,bary)),Vector()).normalized()
    if p.z<.070 or (p.z<.14 and n.z<-.2):continue
    # Keep eye apertures clean rather than hiding the iris with random fibres.
    ref=unmap(p)
    if 1.54<ref.y<1.72 and abs(abs(ref.x)-.271)<.041 and abs(ref.z-1.72)<.034:continue
    ref=unmap(p)
    ear=ref.z>1.83 and ref.y>1.1
    head=ref.y>1.11 and ref.z>1.02
    muzzle=head and ref.y>1.70+abs(ref.x)*.35 and ref.z<1.67
    if ear:
        flow=Vector((p.x*.2,-.10,1));length=rng.uniform(.016,.029);width=rng.uniform(.0014,.0028)
    elif head:
        flow=Vector((p.x*.8,-1,-.32))
        length=rng.uniform(.014,.026) if muzzle else rng.uniform(.040,.080)
        width=rng.uniform(.0012,.0023) if muzzle else rng.uniform(.0018,.0034)
    elif ref.z<1.08 and abs(ref.x)>.28:
        flow=Vector((n.x*.1,-.08,-1));length=rng.uniform(.047,.094);width=rng.uniform(.002,.004)
    else:
        flow=Vector((0,-.85,-.78*abs(n.x)));length=rng.uniform(.078,.148);width=rng.uniform(.0025,.005)
    flow-=n*flow.dot(n)
    if flow.length<.01:flow=n.cross(Vector((1,0,0)))
    flow=Quaternion(n,rng.uniform(-.24,.24))@flow.normalized()
    cross=n.cross(flow).normalized();lift=length*rng.uniform(.09,.19)
    w={}
    for vi,part in zip(ids,bary):
        for g in body.data.vertices[vi].groups:
            name=body.vertex_groups[g.group].name;w[name]=w.get(name,0)+g.weight*part
    w=sorted(w.items(),key=lambda pair:-pair[1])[:4];sumw=sum(v for n,v in w);w=[(n,v/sumw) for n,v in w]
    base=shade(p)*rng.uniform(.80,1.12)
    start=len(verts)
    for along,spread,height in ((0,.70,.0008),(.37,1,lift*.36),(.76,.53,lift*.8),(1,0,lift)):
        for sign in ((-1,1) if along<1 else (0,)):
            pt=p+flow*(length*along)+cross*(width*spread*sign)
            surface,sn,_,_=bvh.find_nearest(pt)
            if surface is not None and (surface-pt).length<length*.45:pt=surface+sn*height
            else:pt+=n*height
            rootpoint=p+n*.0008
            pt=rootpoint+(pt-rootpoint)*(.35+.65*smooth(.32,.70,ref.z))
            verts.append(tuple(pt));weights.append(w)
            col=base*(.76+.34*along)
            colors.append((*col,1));uvs.append(((sign+1)/2,along))
    faces.extend([(start,start+2,start+1),(start+1,start+2,start+3),
        (start+2,start+4,start+3),(start+3,start+4,start+5),(start+4,start+6,start+5)])
if 'Bruno_Coat' in bpy.data.objects:bpy.data.objects.remove(bpy.data.objects['Bruno_Coat'],do_unlink=True)
me=bpy.data.meshes.new('Bruno_Coat');me.from_pydata(verts,[],faces);me.update()
coat=bpy.data.objects.new('Bruno_Coat',me);scene.collection.objects.link(coat)
mat=bpy.data.materials.new('Bruno_ShortFur');mat.use_nodes=True
bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
bs.inputs['Roughness'].default_value=.88;bs.inputs['Sheen Weight'].default_value=.045
bs.inputs['Specular IOR Level'].default_value=.25
vc=mat.node_tree.nodes.new('ShaderNodeVertexColor');vc.layer_name='Color'
mat.node_tree.links.new(vc.outputs['Color'],bs.inputs['Base Color'])
me.materials.append(mat)
ca=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
ca.data.foreach_set('color',[c for co in colors for c in co])
uv=me.uv_layers.new(name='StrandFlow')
for poly in me.polygons:
    poly.use_smooth=True
    for li in poly.loop_indices:uv.data[li].uv=uvs[me.loops[li].vertex_index]
groups={n:coat.vertex_groups.new(name=n) for n in body.vertex_groups.keys()}
for i,ws in enumerate(weights):
    for n,w in ws:groups[n].add([i],w,'REPLACE')
mod=coat.modifiers.new('Skin','ARMATURE');mod.object=rig;coat.parent=rig
coat['fur_type']='Opaque short curved locks; barycentric skin weights, max 4 influences'
print('COAT',len(verts),'verts',len(faces),'triangles')

# Copy the established studio only; the downloaded meshes stay in their scene.
for o in list(scene.objects):
    if o.name.startswith('BF_'):bpy.data.objects.remove(o,do_unlink=True)
source_scene=bpy.data.scenes['Bruno_Meshy_2026_10']
scene.world=source_scene.world.copy()
for src in source_scene.objects:
    if src==bpy.data.objects['Bruno_Meshy_BodyOnly']:continue
    o=src.copy();o.data=src.data.copy();o.name='BF_'+src.name;scene.collection.objects.link(o)
    if o.type=='CAMERA':scene.camera=o
cam=scene.camera;cam.location=(5.5,7.6,2.7)
cam.rotation_euler=(Vector((0,.28,.90))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=4.95
scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
rig.data.pose_position='POSE';rig.animation_data.action=bpy.data.actions['Bruno_Walk']
scene.frame_set(5)
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(OUT/'Bruno_beauty.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Finished.blend'),copy=True,compress=True)
bpy.ops.render.render(write_still=True)
