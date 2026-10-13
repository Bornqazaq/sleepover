"""Portable PBR coat atlas, six game actions and production FBX/GLB export."""
import bpy, bmesh, json, shutil, struct, numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/BrunoMeshy/finished'
ART=ROOT/'igruha/Assets/_Project/Art/CircusNight'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CircusNight'
scene=bpy.data.scenes['Bruno_Finished'];bpy.context.window.scene=scene
rig=bpy.data.objects['BrunoRig'];body=bpy.data.objects['Bruno_Body'];coat=bpy.data.objects['Bruno_Coat']
old=bpy.data.materials.get('CN_BrunoBody')
if old and old!=body.data.materials[0]:old.name='Previous_CN_BrunoBody'
body.data.materials[0].name='CN_BrunoBody'

# Quantize only the tuft base colour; every tuft keeps its root-to-tip gradient.
# Each tuft occupies one padded palette tile, so interpolation never crosses
# unrelated colours. URP Lit therefore needs no vertex-colour custom shader.
count=len(coat.data.vertices)
rgba=np.empty(count*4,dtype=np.float32);coat.data.color_attributes['Color'].data.foreach_get('color',rgba)
roots=rgba.reshape(-1,4)[::7,:3]/.76
order=np.argsort(roots@np.array([.2126,.7152,.0722]));centres=roots[order[np.linspace(0,len(order)-1,128,dtype=int)]].copy()
for _ in range(9):
    distance=((roots[:,None,:]-centres[None,:,:])**2).sum(axis=2)
    index=distance.argmin(axis=1)
    for i in range(128):
        group=roots[index==i]
        if len(group):centres[i]=group.mean(axis=0)
W,H=256,128;pixels=np.ones((H,W,4),dtype=np.float32)
for i,c in enumerate(centres):
    tx,ty=i%16,i//16
    for y in range(16):
        along=np.clip((y-2)/11,0,1)
        for x in range(16):pixels[ty*16+y,tx*16+x,:3]=c*(.76+.34*along)
im=bpy.data.images.new('CN_BrunoCoat_BaseColor',width=W,height=H,alpha=False)
pixels[:,:,:3]=np.where(pixels[:,:,:3]<=.0031308,pixels[:,:,:3]*12.92,
    1.055*np.power(pixels[:,:,:3],1/2.4)-.055)
im.pixels.foreach_set(pixels.ravel());im.filepath_raw=str(OUT/'CN_BrunoCoat_BaseColor.png');im.file_format='PNG';im.save();im.pack()
uv=coat.data.uv_layers.active
for loop in coat.data.loops:
    vi=loop.vertex_index;tile=int(index[vi//7]);tuftvertex=vi%7
    along=(0,0,.37,.37,.76,.76,1)[tuftvertex]
    across=(0,1,0,1,0,1,.5)[tuftvertex]
    uv.data[loop.index].uv=((tile%16*16+2.5+11*across)/W,(tile//16*16+2.5+11*along)/H)
old=bpy.data.materials.get('CN_BrunoCoat')
if old:old.name='Previous_CN_BrunoCoat'
mat=bpy.data.materials.new('CN_BrunoCoat');mat.use_nodes=True
bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
bs.inputs['Roughness'].default_value=.88;bs.inputs['Specular IOR Level'].default_value=.25
n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=im;mat.node_tree.links.new(n.outputs['Color'],bs.inputs['Base Color'])
coat.data.materials.clear();coat.data.materials.append(mat)
for a in list(coat.data.color_attributes):coat.data.color_attributes.remove(a)

# Unity's metallic workflow stores smoothness in alpha.
rough=next(n.image for n in body.data.materials[0].node_tree.nodes if n.type=='TEX_IMAGE' and 'Roughness' in n.image.name)
pixels=np.empty(len(rough.pixels),dtype=np.float32);rough.pixels.foreach_get(pixels)
pixels=pixels.reshape(-1,4);pixels[:,3]=1-pixels[:,0];pixels[:,:3]=0
mask=bpy.data.images.new('CN_BrunoBody_MetallicSmoothness',width=rough.size[0],height=rough.size[1],alpha=True)
mask.colorspace_settings.name='Non-Color';mask.pixels.foreach_set(pixels.ravel())
mask.filepath_raw=str(OUT/'CN_BrunoBody_MetallicSmoothness.png');mask.file_format='PNG';mask.save();mask.pack()
for old,new in [('Bruno_BaseColor.png','CN_BrunoBody_BaseColor.png'),('Bruno_Normal.png','CN_BrunoBody_Normal.png'),
    ('CN_BrunoCoat_BaseColor.png','CN_BrunoCoat_BaseColor.png'),('CN_BrunoBody_MetallicSmoothness.png','CN_BrunoBody_MetallicSmoothness.png')]:
    shutil.copy2(OUT/old,ART/'Textures'/new)

# The final, editable silhouette pass also runs when rebuilding from source.
natural_path=ROOT/'tools/blender/bruno_natural.py'
natural={ '__file__':str(natural_path), '__name__':'bruno_natural' }
exec(compile(natural_path.read_text(),str(natural_path),'exec'),natural)
(OUT/'natural-shape-check.json').write_text(json.dumps(natural['apply'](),indent=2))

# Keep the repaired, relaxed forelimbs when rebuilding the production model.
rear_path=ROOT/'tools/blender/bruno_rear_motion.py'
exec(compile(rear_path.read_text(),str(rear_path),'exec'),{'__name__':'__main__','__file__':str(rear_path)})

backup=ROOT/'art-source/BrunoMeshy/production-before-IGR757';backup.mkdir(exist_ok=True)
for original in [ART/'Models/CN_Bruno.fbx',SOURCE/'Bruno.blend']:
    if original.exists() and not (backup/original.name).exists():shutil.copy2(original,backup/original.name)
rig.animation_data.action=None
for t in list(rig.animation_data.nla_tracks):rig.animation_data.nla_tracks.remove(t)
for title in ('Idle','Walk','Run','Alert','Strike','Roar'):
    t=rig.animation_data.nla_tracks.new();t.name='Bruno_'+title
    t.strips.new('Bruno_'+title,1,bpy.data.actions['Bruno_'+title])
bpy.ops.object.select_all(action='DESELECT')
for o in [body,coat,rig]:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(ART/'Models/CN_Bruno.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},
    axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=True,bake_anim_simplify_factor=0,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True)
for t in rig.animation_data.nla_tracks:t.mute=True
rig.animation_data.action=bpy.data.actions['Bruno_Walk'];scene.frame_set(5)

# GLB excludes studio, raw downloads and unused actions. Triangulation is only
# for export tangents; the editable source retains quads.
parents={o:o.parent for o in (body,coat)};meshes={o:o.data for o in (body,coat)}
for o in (body,coat):
    o.parent=None;o.data=o.data.copy();o.data.name=o.name+'_Export'
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
try:
    bpy.ops.export_scene.gltf(filepath=str(OUT/'Bruno_Heavy.glb'),export_format='GLB',use_selection=True,use_active_scene=True,
        export_animations=True,export_animation_mode='NLA_TRACKS',export_anim_single_armature=False,
        export_frame_range=False,export_force_sampling=True,export_skins=True,export_def_bones=False,
        export_all_influences=False,export_influence_nb=4,export_tangents=True,export_cameras=False,export_lights=False)
    # A nearly collinear tuft can produce a zero tangent in Blender. The coat
    # has no normal map, but glTF still requires every exported tangent to be
    # unit length. Supply the orthogonal frame for those rare vertices.
    glb=OUT/'Bruno_Heavy.glb';data=bytearray(glb.read_bytes())
    size=struct.unpack_from('<I',data,12)[0];document=json.loads(data[20:20+size]);start=28+size
    for mesh in document['meshes']:
        for primitive in mesh['primitives']:
            if document['materials'][primitive['material']]['name']!='CN_BrunoCoat':continue
            ta=document['accessors'][primitive['attributes']['TANGENT']]
            na=document['accessors'][primitive['attributes']['NORMAL']]
            tv=document['bufferViews'][ta['bufferView']];nv=document['bufferViews'][na['bufferView']]
            for i in range(ta['count']):
                offset=start+tv.get('byteOffset',0)+ta.get('byteOffset',0)+i*tv.get('byteStride',16)
                x,y,z,w=struct.unpack_from('<4f',data,offset)
                if x*x+y*y+z*z>.5:continue
                normal_offset=start+nv.get('byteOffset',0)+na.get('byteOffset',0)+i*nv.get('byteStride',12)
                nx,ny,nz=struct.unpack_from('<3f',data,normal_offset)
                v=np.array((-ny,nx,0) if abs(nz)<.9 else (nz,0,-nx));v/=np.linalg.norm(v)
                struct.pack_into('<4f',data,offset,*v,1)
    glb.write_bytes(data)
finally:
    for o in (body,coat):
        tmp=o.data;o.data=meshes[o];o.parent=parents[o];bpy.data.meshes.remove(tmp)
scene.render.engine='CYCLES';scene.cycles.samples=48
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.frame_set(5);scene.render.filepath=str(OUT/'Bruno_Heavy.png')
bpy.ops.render.render(write_still=True)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Bruno.blend'),copy=True,compress=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Heavy.blend'),copy=True,compress=True)
desk=Path.home()/'Desktop/Bruno_Heavy';desk.mkdir(exist_ok=True)
for name in ['Bruno_Heavy.glb','Bruno_Heavy.blend','Bruno_Heavy.png']:shutil.copy2(OUT/name,desk/name)
(OUT/'heavy-metrics.json').write_text(json.dumps({'vertices':len(body.data.vertices)+len(coat.data.vertices),
    'triangles':sum(len(p.vertices)-2 for o in [body,coat] for p in o.data.polygons),
    'bones':len(rig.data.bones),'actions':[t.name for t in rig.animation_data.nla_tracks],
    'palette_mean_linear_error':float(np.sqrt(distance.min(axis=1)).mean())},indent=2))
print('PRODUCTION_BRUNO_EXPORTED')
