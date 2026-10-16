"""Reference projection paint for the user's untextured Meshy horse.

The supplied three-view sheet is the color source. Coordinates are calibrated
to this model; no substitute geometry is generated. Later exported as an atlas.
"""
import bpy, math
import numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-source/HorseRingmaster'
ob=bpy.data.objects['Horse_Meshy_Skin']
im=bpy.data.images.load(str(OUT/'Reference.png'),check_existing=True)
w,h=im.size;pixels=np.array(im.pixels[:],dtype=np.float32).reshape(h,w,4)

def pixel(x,y):
    x=max(0,min(w-1,int(x)));y=max(0,min(h-1,int(y)))
    return pixels[h-1-y,x,:3].copy()
def background(c):
    return max(c)-min(c)<.030 and .43<float(np.mean(c))<.74
def sample(x,y,center):
    c=pixel(x,y)
    if background(c):
        # Fill projection borders from the closest colored silhouette texel;
        # do not paint the gray studio background onto the back of a limb.
        for offset in range(1,19):
            q=pixel(x+(offset if x<center else -offset),y)
            if not background(q):return q
    return c
def linear(c):
    return np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4)

# Cache normals before writing attributes: reading v.normal after each color
# assignment can force a full mesh normal evaluation thousands of times.
count=len(ob.data.vertices)
coords=np.empty(count*3,dtype=np.float32);ob.data.vertices.foreach_get('co',coords);coords=coords.reshape(-1,3)
normals=np.empty(count*3,dtype=np.float32);ob.data.vertices.foreach_get('normal',normals);normals=normals.reshape(-1,3)
weights=np.zeros((count,4),dtype=np.float32);weights[:,3]=1
uvs=[np.empty((count,2),dtype=np.float32) for _ in range(3)]
for index,((x,y,z),n) in enumerate(zip(coords,normals)):
    py=691-162*z
    points=[(395+160*x,py,395),(1773-160*x,py,1773),(1098+162*y,py,1098)]
    for j,(px,pz,center) in enumerate(points):
        if background(pixel(px,pz)):
            for offset in range(1,19):
                candidate=px+(offset if px<center else -offset)
                if not background(pixel(candidate,pz)):
                    px=candidate;break
        uvs[j][index]=(px/w,1-pz/h)
    fw=max(.015,-n[1])**4;bw=max(.015,n[1])**4
    sw=abs(n[0])**4*(.35 if z>2.85 and abs(x)<.4 else .015)
    if y<-.55 and z>2.80:fw+=1
    if y>.40 and z<1.8:bw+=1
    weights[index,:3]=(fw,bw,sw)
    weights[index,:3]/=fw+bw+sw
attr=ob.data.color_attributes.get('ProjectionWeights') or ob.data.color_attributes.new(name='ProjectionWeights',type='FLOAT_COLOR',domain='POINT')
attr.data.foreach_set('color',weights.ravel())
indices=np.empty(len(ob.data.loops),dtype=np.int32);ob.data.loops.foreach_get('vertex_index',indices)
for name,uv in zip(['FrontProjection','BackProjection','SideProjection'],uvs):
    layer=ob.data.uv_layers.get(name) or ob.data.uv_layers.new(name=name)
    layer.data.foreach_set('uv',uv[indices].ravel())
mat=bpy.data.materials.get('Ringmaster • reference paint') or bpy.data.materials.new('Ringmaster • reference paint')
mat.use_nodes=True;ns=mat.node_tree.nodes;ls=mat.node_tree.links;ns.clear()
out=ns.new('ShaderNodeOutputMaterial');p=ns.new('ShaderNodeBsdfPrincipled');ls.new(p.outputs['BSDF'],out.inputs['Surface']);p.inputs['Roughness'].default_value=.76
weight=ns.new('ShaderNodeVertexColor');weight.layer_name='ProjectionWeights';split=ns.new('ShaderNodeSeparateXYZ');ls.new(weight.outputs['Color'],split.inputs[0])
colors=[]
for j,name in enumerate(['FrontProjection','BackProjection','SideProjection']):
    uv=ns.new('ShaderNodeUVMap');uv.uv_map=name
    tex=ns.new('ShaderNodeTexImage');tex.image=im;ls.new(uv.outputs['UV'],tex.inputs['Vector'])
    mul=ns.new('ShaderNodeVectorMath');mul.operation='SCALE';ls.new(tex.outputs['Color'],mul.inputs[0]);ls.new(split.outputs[j],mul.inputs['Scale']);colors.append(mul.outputs[0])
add=ns.new('ShaderNodeVectorMath');add.operation='ADD';ls.new(colors[0],add.inputs[0]);ls.new(colors[1],add.inputs[1])
add2=ns.new('ShaderNodeVectorMath');add2.operation='ADD';ls.new(add.outputs[0],add2.inputs[0]);ls.new(colors[2],add2.inputs[1]);ls.new(add2.outputs[0],p.inputs['Base Color'])
ob.data.materials.clear();ob.data.materials.append(mat)
for face in ob.data.polygons:face.material_index=0
im.pack()
S=bpy.context.scene
if S.world is None:S.world=bpy.data.worlds.new('Studio')
S.world.color=(.20,.20,.20)
for old in list(S.objects):
    if old.type in ['CAMERA','LIGHT']:bpy.data.objects.remove(old,do_unlink=True)
from mathutils import Vector
def area(name,pos,energy,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
    light=bpy.data.objects.new(name,data);S.collection.objects.link(light);light.location=pos
    light.rotation_euler=(Vector((0,0,2.1))-light.location).to_track_quat('-Z','Y').to_euler()
area('Key',(-3,-4,6),500,5);area('Fill',(3,-4,4),350,4);area('Rim',(1,3,5),600,4)
data=bpy.data.cameras.new('Horse review');camera=bpy.data.objects.new('Horse review',data);S.collection.objects.link(camera)
camera.location=(0,-9,2.10);camera.rotation_euler=(Vector((0,0,2.10))-camera.location).to_track_quat('-Z','Y').to_euler()
data.type='ORTHO';data.ortho_scale=4.6;S.camera=camera
S.render.engine='CYCLES';S.cycles.samples=24;S.render.resolution_x=1000;S.render.resolution_y=1100;S.render.resolution_percentage=100
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HorseRingmaster.blend'))
print('Painted',len(ob.data.vertices),'vertices from supplied front/side/back reference')
