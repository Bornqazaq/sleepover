"""Refine the retained UV atlases; unify jaw, eye rims and horn with the body."""
import bpy,math,json
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/BrunoMeshy/polish-v6'
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def linear(c):return np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4)
def sampler(image,color=True):
 a=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(a);a=a.reshape(image.size[1],image.size[0],4)
 if color:a[:,:,:3]=linear(a[:,:,:3])
 def sample(uv):
  x=max(0,min(image.size[0]-1,round(uv[0]*(image.size[0]-1))));y=max(0,min(image.size[1]-1,round(uv[1]*(image.size[1]-1))))
  return a[y,x,:3].copy()
 return sample
body=bpy.data.objects['Bruno_Body'];body.data.calc_loop_triangles();parts=json.loads((OUT/'Bruno_Body-islands.json').read_text());skin=set(parts[0]['ids'])
tris=[(tuple(t.vertices),tuple(t.loops)) for t in body.data.loop_triangles if t.vertices[0] in skin];vs=[v.co.copy() for v in body.data.vertices]
tree=BVHTree.FromPolygons(vs,[t[0] for t in tris],all_triangles=True)
bs=next(n for n in body.data.materials[0].node_tree.nodes if n.type=='BSDF_PRINCIPLED')
sample_body=sampler(bs.inputs['Base Color'].links[0].from_node.image)
def surface_color(point):
 hit,n,idx,d=tree.find_nearest(Vector(point));t=tris[idx]
 bary=barycentric_transform(hit,*(vs[j] for j in t[0]),Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
 uv=sum((Vector(body.data.uv_layers.active.data[j].uv)*w for j,w in zip(t[1],bary)),Vector((0,0)))
 return sample_body(uv)

s=bpy.context.scene;s.cycles.samples=8;s.render.bake.use_pass_direct=False;s.render.bake.use_pass_indirect=False;s.render.bake.use_pass_color=True;s.render.bake.margin=8
for name,label,size in [('Bruno_Body','Body',2048),('Bruno_Details','Details',1024)]:
 ob=bpy.data.objects[name];me=ob.data;islands=json.loads((OUT/(name+'-islands.json')).read_text())
 part_by_vertex={vi:idx for idx,part in enumerate(islands) for vi in part['ids']}
 old=me.materials[0];bs=next(n for n in old.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 source=sampler(bs.inputs['Base Color'].links[0].from_node.image)
 rough_source=sampler(bs.inputs['Roughness'].links[0].from_node.image,False)
 colors=np.ones((len(me.loops),4),dtype=np.float32);rough=np.ones((len(me.loops),4),dtype=np.float32)
 for loop in me.loops:
  vi=loop.vertex_index;idx=part_by_vertex[vi];x,y,z=me.vertices[vi].co;uv=me.uv_layers.active.data[loop.index].uv
  c=source(uv);r=float(rough_source(uv)[0])
  if label=='Body':
   if 17<=idx<=36:
    p=islands[idx];t=smooth(p['min'][1],p['max'][1],y)
    c=np.array((.024,.015,.008))*(1-t)+np.array((.23,.165,.085))*t
    c*=.94+.055*math.sin(idx*2.71);r=.63-.17*t
   elif idx in (12,13,14,15):c=np.array((.048,.020,.008));r=.76
   elif idx in (10,11):
    bowl=math.exp(-((abs(x)-.321)/.057)**2-((z-1.47)/.07)**2)
    c=np.array((.072,.031,.012))*(1-.65*bowl)+np.array((.026,.010,.005))*(.65*bowl);r=.87
  else:
   if idx==0:c=surface_color((x,y,z))*.96;r=.86
   elif idx in (3,4):c=np.array((.017,.007,.0035));r=.73
   elif idx==5:c=np.array((.085,.029,.020));r=.69
   elif idx>=30:
    t=smooth(.93,1.03,z);c=np.array((.44,.34,.20))*(1-t)+np.array((.29,.205,.105))*t;r=.45
  colors[loop.index,:3]=c;rough[loop.index,:3]=r
 mat=old.copy();mat.name='Bruno_V6_'+label;me.materials[0]=mat;nt=mat.node_tree;bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
 for layer,values,socket in [('V6Color',colors,'Base Color'),('V6Roughness',rough,'Roughness')]:
  attr=me.color_attributes.get(layer) or me.color_attributes.new(name=layer,type='FLOAT_COLOR',domain='CORNER');attr.data.foreach_set('color',values.ravel())
  node=nt.nodes.new('ShaderNodeVertexColor');node.layer_name=layer;nt.links.new(node.outputs['Color'],bs.inputs[socket])
 bs.inputs['Specular IOR Level'].default_value=.26
 for node in nt.nodes:
  if node.type=='NORMAL_MAP':node.inputs['Strength'].default_value=.24
 bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
 maps={}
 for channel,bake,dimension in [('BaseColor','DIFFUSE',size),('Roughness','ROUGHNESS',1024)]:
  im=bpy.data.images.new('Bruno_V6_'+label+'_'+channel,width=dimension,height=dimension,alpha=False)
  if channel=='Roughness':im.colorspace_settings.name='Non-Color'
  node=nt.nodes.new('ShaderNodeTexImage');node.image=im;nt.nodes.active=node
  bpy.ops.object.bake(type=bake)
  im.filepath_raw=str(OUT/('Bruno_'+label+'_'+channel+'.png'));im.file_format='PNG';im.save();im.pack();maps[channel]=node
 for channel,socket in [('BaseColor','Base Color'),('Roughness','Roughness')]:nt.links.new(maps[channel].outputs['Color'],bs.inputs[socket])
 for layer in ['V6Color','V6Roughness']:me.color_attributes.remove(me.color_attributes[layer])
print('V6_UV_MATERIALS_BAKED')
(OUT/'materials.done').write_text('done')
