"""Reshape the existing 18k opaque fur triangles into soft, low-contrast locks."""
import bpy,math,random,json
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/BrunoMeshy/polish-v6'
env={'__name__':'library','__file__':str(ROOT/'tools/blender/bruno_polish_v6.py')};exec(compile(Path(env['__file__']).read_text(),env['__file__'],'exec'),env)
smooth=env['smooth'];g=env['g'];body=bpy.data.objects['Bruno_Body'];coat=bpy.data.objects['Bruno_Coat'];me=coat.data
basis=env['basis'](me);body.data.calc_loop_triangles();skin=set(env['parts'](body.name)[0]['ids'])
triangles=[t for t in body.data.loop_triangles if t.vertices[0] in skin]
verts=[v.co.copy() for v in body.data.vertices];tree=BVHTree.FromPolygons(verts,[tuple(t.vertices) for t in triangles],all_triangles=True)
mat=body.data.materials[0];bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');im=bs.inputs['Base Color'].links[0].from_node.image
pixels=np.empty(len(im.pixels),dtype=np.float32);im.pixels.foreach_get(pixels);pixels=pixels.reshape(im.size[1],im.size[0],4)
uv=body.data.uv_layers.active
rng=random.Random(61210);points=[];normals=[];colors=[];tiles=[]
for k in range(len(basis)//3):
 q=basis[k*3:k*3+3];root=Vector((q[0]+q[1])*.5);hit,n,ti,_=tree.find_nearest(root)
 direction=Vector(q[2])-root;flow=direction-n*direction.dot(n)
 if flow.length<.0001:flow=Vector((0,-1,-.2));flow-=n*flow.dot(n)
 flow.normalize();x,y,z=hit;a=abs(x)
 mane=smooth(.95,1.30,z)*g(y,.38,.9)
 cheek=smooth(1.05,1.40,y)*(1-smooth(1.57,1.74,y))*smooth(.19,.33,a)
 limb=(1-smooth(.48,.91,z))*smooth(.30,.62,a)
 desired=Vector((n.x*.12,-1,-abs(n.x)*.45))
 if limb>.1:desired=desired.lerp(Vector((n.x*.1,-.05,-1)),limb)
 desired-=n*desired.dot(n)
 if desired.length>.001:flow=flow.lerp(desired.normalized(),.35).normalized()
 length=rng.uniform(.029,.049)+mane*rng.uniform(.009,.025)+cheek*.007-limb*.012
 width=rng.uniform(.0038,.0068)*(1+.12*mane-.24*limb)
 cross=n.cross(flow).normalized();height=(.005+.012*mane+.006*cheek)*(rng.uniform(.8,1.25))
 root=hit+n*.0006;guess=root+flow*length
 tip,tn,_,_=tree.find_nearest(guess)
 if (tip-guess).length>.06:tip=guess;tn=n
 tip+=tn*height
 points.extend([root-cross*width,root+cross*width,tip])
 normals.extend([n,n,(tn+flow*.10).normalized()])
 tri=triangles[ti];bary=barycentric_transform(hit,*(verts[j] for j in tri.vertices),Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
 tex=sum((Vector(uv.data[j].uv)*w for j,w in zip(tri.loops,bary)),Vector((0,0)))
 tx=max(0,min(im.size[0]-1,round(tex.x*(im.size[0]-1))));ty=max(0,min(im.size[1]-1,round(tex.y*(im.size[1]-1))))
 colors.append(pixels[ty,tx,:3])
 luv=me.uv_layers.active.data[me.polygons[k].loop_start].uv
 tiles.append((min(7,int(luv.y*8)),min(7,int(luv.x*8))))
 # Rebind a moved root barycentrically; every lock moves as one rigid tiny tuft.
 weights={}
 for vi,w in zip(tri.vertices,bary):
  for vg in body.data.vertices[vi].groups:
   name=body.vertex_groups[vg.group].name;weights[name]=weights.get(name,0)+vg.weight*max(0,w)
 weights=sorted(weights.items(),key=lambda x:-x[1])[:4];total=sum(w for _,w in weights)
 ids=list(range(k*3,k*3+3))
 for vi in ids:
  for gi in [group.group for group in me.vertices[vi].groups]:coat.vertex_groups[gi].remove([vi])
 for name,w in weights:coat.vertex_groups[name].add(ids,w/total,'REPLACE')
me.vertices.foreach_set('co',np.asarray(points,dtype=np.float32).ravel());me.update()
me.normals_split_custom_set([normals[loop.vertex_index] for loop in me.loops])
# Keep the exact palette UVs. A restrained root-to-tip gradient replaces black lines.
new=bpy.data.images.new('Bruno_V6_Fur_BaseColor',width=1024,height=1024,alpha=False)
paint=np.ones((1024,1024,4),dtype=np.float32);colors=np.asarray(colors);tiles=np.asarray(tiles)
for row in range(8):
 for col in range(8):
  subset=colors[(tiles[:,0]==row)&(tiles[:,1]==col)];color=subset.mean(axis=0) if len(subset) else np.array((.075,.032,.012))
  gradient=np.linspace(.985,1.065,128)[:,None,None]
  paint[row*128:(row+1)*128,col*128:(col+1)*128,:3]=color[None,None,:]*gradient
new.pixels.foreach_set(paint.ravel());new.filepath_raw=str(OUT/'Bruno_Fur_BaseColor.png');new.file_format='PNG';new.save();new.pack()
mat=me.materials[0].copy();mat.name='Bruno_V6_SoftFur';me.materials[0]=mat;bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
node=bs.inputs['Base Color'].links[0].from_node;node.image=new
bs.inputs['Roughness'].default_value=.90;bs.inputs['Specular IOR Level'].default_value=.22
if 'Sheen Weight' in bs.inputs:bs.inputs['Sheen Weight'].default_value=.012
coat['v6_groom']='Same 18000 opaque triangles and same UVs; refitted roots, broader shorter locks, low-contrast root-to-tip colour'
print('V6_GROOM',len(me.polygons),'opaque triangles',len(me.vertices),'vertices')
