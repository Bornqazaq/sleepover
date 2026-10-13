"""Portable textures, three-material LOD0 and skinned LOD1; no production overwrite."""
import bpy,bmesh,numpy as np,json,math,struct,zlib,hashlib
from pathlib import Path
OUT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover/art-source/BrunoMeshy/polish-v5')
scene=bpy.context.scene;rig=bpy.data.objects['BrunoRig'];rig.data.pose_position='REST'

def write_png(path,array):
    h,w,_=array.shape
    def chunk(t,data):return struct.pack('!I',len(data))+t+data+struct.pack('!I',zlib.crc32(t+data)&0xffffffff)
    raw=b''.join(b'\x00'+row.tobytes() for row in array)
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))

coat=bpy.data.objects['Bruno_Coat'];me=coat.data
cols=np.array([c.color[:3] for c in me.color_attributes['Color'].data])[::3]
order=np.argsort(cols.sum(axis=1));centers=cols[order[np.linspace(0,len(cols)-1,64).astype(int)]].copy()
for _ in range(9):
    labels=((cols[:,None,:]-centers[None,:,:])**2).sum(axis=2).argmin(axis=1)
    for j in range(64):
        if np.any(labels==j):centers[j]=cols[labels==j].mean(axis=0)
pixels=np.ones((1024,1024,4),dtype=np.float32)
for j,c in enumerate(centers):
    row,col=divmod(j,8)
    t=np.linspace(1,0,128)[:,None,None]
    rgb=c[None,None,:]*(1+t*(1.09/.76-1))
    pixels[row*128:(row+1)*128,col*128:(col+1)*128,:3]=rgb
rgb=pixels[:,:,:3];pixels[:,:,:3]=np.where(rgb<=.0031308,rgb*12.92,1.055*rgb**(1/2.4)-.055)
write_png(OUT/'Bruno_Fur_BaseColor.png',(np.clip(pixels,0,1)*255+.5).astype(np.uint8))
uv=me.uv_layers.active
for face in me.polygons:
    label=int(labels[face.index]);row,col=divmod(label,8)
    for li in face.loop_indices:
        vi=me.loops[li].vertex_index%3
        u,v=[(.04,.04),(.96,.04),(.5,.96)][vi]
        uv.data[li].uv=((col+u)/8,(7-row+v)/8)
mat=me.materials[0];nt=mat.node_tree;bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
tex=nt.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(OUT/'Bruno_Fur_BaseColor.png'));tex.image.pack()
nt.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
coat['texture_palette_error']=float(np.sqrt(((cols-centers[labels])**2).mean()))

# Bake the separate jaw parts and sole pads into one detail atlas/material.
mouth=bpy.data.objects['Bruno_Mouth'];pads=bpy.data.objects['Bruno_PawPads']
bpy.ops.object.select_all(action='DESELECT');mouth.select_set(True);pads.select_set(True)
bpy.context.view_layer.objects.active=mouth;bpy.ops.object.join();mouth.name='Bruno_Details'
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.018);bpy.ops.object.mode_set(mode='OBJECT')
scene.cycles.samples=8;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True;scene.render.bake.margin=8
maps={}
for label,bake in [('BaseColor','DIFFUSE'),('Roughness','ROUGHNESS')]:
    im=bpy.data.images.new('Bruno_Details_'+label,width=1024,height=1024,alpha=False)
    if label=='Roughness':im.colorspace_settings.name='Non-Color'
    for m in mouth.data.materials:
        n=m.node_tree.nodes.new('ShaderNodeTexImage');n.image=im;m.node_tree.nodes.active=n
    bpy.ops.object.bake(type=bake)
    im.filepath_raw=str(OUT/('Bruno_Details_'+label+'.png'));im.file_format='PNG';im.save();im.pack();maps[label]=im
mat=bpy.data.materials.new('Bruno_V5_Details');mat.use_nodes=True;nt=mat.node_tree;bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
for label,socket in [('BaseColor','Base Color'),('Roughness','Roughness')]:
    n=nt.nodes.new('ShaderNodeTexImage');n.image=maps[label];nt.links.new(n.outputs['Color'],bs.inputs[socket])
mouth.data.materials.clear();mouth.data.materials.append(mat)
for f in mouth.data.polygons:f.material_index=0

# Preserve non-destructive high fidelity source and separately reduce one LOD.
objects=[bpy.data.objects[n] for n in ('Bruno_Body','Bruno_Coat','Bruno_Details')]
collection=bpy.data.collections.new('Bruno_LOD1');scene.collection.children.link(collection)
lod=[]
for src in objects:
    ob=src.copy();ob.data=src.data.copy();ob.name=src.name+'_LOD1';collection.objects.link(ob);lod.append(ob)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    if src.name=='Bruno_Coat':
        bm=bmesh.new();bm.from_mesh(ob.data);bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.index%2],context='FACES')
        bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
        bm.to_mesh(ob.data);bm.free()
    else:
        m=ob.modifiers.new('LOD1 simplify','DECIMATE');m.ratio=.5 if src.name=='Bruno_Body' else .6
        bpy.ops.object.modifier_move_up(modifier=m.name)
        bpy.ops.object.modifier_apply(modifier=m.name)
    ob.data.calc_loop_triangles()
collection.hide_render=True
collection.hide_viewport=True
for ob in objects:ob.data.calc_loop_triangles()
stats={ob.name:{'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)} for ob in objects+lod}
(OUT/'geometry-stats.json').write_text(json.dumps(stats,indent=2))
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Polished.blend'))
print('PACKAGED',stats)
