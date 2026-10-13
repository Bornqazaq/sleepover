"""Remove inherited lower-leg influence above elbows; retain original weights as data."""
import bpy,json,math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
body=bpy.data.objects['Bruno_Body'];coat=bpy.data.objects['Bruno_Coat']
def sm(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
key='V5PreFlexionWeights'
if key not in body.data:body.data[key]=json.dumps([[(g.group,g.weight) for g in v.groups] for v in body.data.vertices])
saved=json.loads(body.data[key]);names={g.index:g.name for g in body.vertex_groups};groups={g.name:g for g in body.vertex_groups}
allweights=[]
for v,old in zip(body.data.vertices,saved):
 w={names[g]:a for g,a in old};x,y,z=v.co;side='L' if x>0 else 'R'
 for prefix,center in [('Fore',.35),('Hind',-.90)]:
  ns=[prefix+n+'.'+side for n in ['Upper','Lower','Paw','Toes']];total=sum(w.get(n,0) for n in ns)
  if total<.03 or z<.32:continue
  blend=sm(.32,.49,z)
  upper=sm(.53 if prefix=='Fore' else .45,.78 if prefix=='Fore' else .70,z)
  paw=(1-sm(.29,.43,z))*(1-upper)
  fractions=[upper,1-upper-paw,paw,0]
  for n,f in zip(ns,fractions):w[n]=w.get(n,0)*(1-blend)+total*f*blend
  if prefix=='Fore' and y>-.24 and w.get('Lumbar',0)>0:
   # The axilla belongs to the chest, not the lumbar chain two joints away.
   change=w['Lumbar']*sm(-.24,.05,y)
   w['Lumbar']-=change;w['Chest']=w.get('Chest',0)+change*.78;w['Spine']=w.get('Spine',0)+change*.22
 ranked=sorted([(n,a) for n,a in w.items() if a>1e-7],key=lambda x:-x[1])[:4];total=sum(a for _,a in ranked);w={n:a/total for n,a in ranked}
 for g in [g.group for g in v.groups]:body.vertex_groups[g].remove([v.index])
 for n,a in w.items():groups[n].add([v.index],a,'REPLACE')
 allweights.append(w)
body.data.calc_loop_triangles();tris=[tuple(t.vertices) for t in body.data.loop_triangles]
tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],tris,all_triangles=True)
def transfer(ob,stride):
 gs={g.name:g for g in ob.vertex_groups}
 for start in range(0,len(ob.data.vertices),stride):
  if stride==3:p=(ob.data.vertices[start].co+ob.data.vertices[start+1].co)*.5
  else:p=ob.data.vertices[start].co
  hit,n,idx,dist=tree.find_nearest(p);ids=tris[idx]
  bary=barycentric_transform(hit,*(body.data.vertices[i].co for i in ids),Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
  w={}
  for i,part in zip(ids,bary):
   for name,amount in allweights[i].items():w[name]=w.get(name,0)+amount*max(0,part)
  ranked=sorted(w.items(),key=lambda x:-x[1])[:4];total=sum(a for _,a in ranked)
  for i in range(start,start+stride):
   for g in [g.group for g in ob.data.vertices[i].groups]:ob.vertex_groups[g].remove([i])
  for name,amount in ranked:
   if name not in gs:gs[name]=ob.vertex_groups.new(name=name)
   gs[name].add(list(range(start,start+stride)),amount/total,'REPLACE')
transfer(coat,3)
for name,stride in [('Bruno_Body_LOD1',1),('Bruno_Coat_LOD1',3)]:transfer(bpy.data.objects[name],stride)
print('elbow/hock weights refined; coat and LOD1 weights transferred')
