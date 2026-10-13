"""Fit the card widths to the sculpt and use continuous surface normals/colours."""
import bpy,math,numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from pathlib import Path
OUT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover/art-source/BrunoMeshy/polish-v5')
body=bpy.data.objects['Bruno_Body'];coat=bpy.data.objects['Bruno_Coat']
body.data.calc_loop_triangles()
# The continuous skin is the only component with more than 1k vertices.
adj=[[] for _ in body.data.vertices]
for e in body.data.edges:
 a,b=e.vertices;adj[a].append(b);adj[b].append(a)
seen=set();allparts=[]
for v in body.data.vertices:
 if v.index in seen:continue
 stack=[v.index];seen.add(v.index);ids=[]
 while stack:
  i=stack.pop();ids.append(i)
  for j in adj[i]:
   if j not in seen:seen.add(j);stack.append(j)
 allparts.append(ids)
skin=set(max(allparts,key=len))
tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],[tuple(t.vertices) for t in body.data.loop_triangles if t.vertices[0] in skin],all_triangles=True)
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def shade(p):
 x,y,z=p;c=Vector((.075,.032,.012))
 mantle=smooth(1.05,1.60,z)*math.exp(-((y-.34)/1.12)**2)*.62
 c=c.lerp(Vector((.125,.063,.026)),mantle)
 muzzle=smooth(1.62,1.93,y-abs(x)*.26)*(1-smooth(1.26,1.44,z))
 c=c.lerp(Vector((.145,.073,.030)),muzzle)
 c=c.lerp(Vector((.018,.010,.006)),(1-smooth(.16,.52,z))*.79)
 return c
normals=[];ca=coat.data.color_attributes['Color']
for i in range(0,len(coat.data.vertices),7):
 root=(coat.data.vertices[i].co+coat.data.vertices[i+1].co)*.5
 color=shade(root)*(1+.035*math.sin(i*6.741))
 for j in range(7):
  v=coat.data.vertices[i+j];hit,n,_,_=tree.find_nearest(v.co)
  t=[0,0,.37,.37,.76,.76,1][j]
  if hit is not None:
   lift=.002+t*.010*(1+.45*smooth(.85,1.5,root.z))
   v.co=hit+n*lift;normals.append(n)
  else:normals.append(v.normal.copy())
  ca.data[i+j].color=(*color,1)
coat.data.update()
coat.data.normals_split_custom_set([normals[l.vertex_index] for l in coat.data.loops])
mat=coat.data.materials[0];nt=mat.node_tree
tex=next(n for n in nt.nodes if n.type=='TEX_IMAGE');tex.image.reload();tex.image.pack()
bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
bs.inputs['Roughness'].default_value=.94
# Finish the lower lip in the mandible at the mouth corners.
mouth=bpy.data.objects['Bruno_Mouth'];mi=next(i for i,m in enumerate(mouth.data.materials) if m.name=='V5_Lips')
vids=set(v for p in mouth.data.polygons if p.material_index==mi for v in p.vertices)
jawgroup=mouth.vertex_groups['Jaw'].index
for i in vids:
 v=mouth.data.vertices[i]
 if any(g.group==jawgroup for g in v.groups):
  factor=smooth(.06,.205,abs(v.co.x))
  v.co.y+=.11*factor*(1-smooth(1.75,1.95,v.co.y))
  v.co.z+=.038*factor
mouth.data.update()
print('GROOM normals and surface fitted; lip corners seated')
