"""Final low-overdraw groom: retain original strand roots, one triangle per lock."""
import bpy,math,random,numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover');OUT=ROOT/'art-source/BrunoMeshy/polish-v5'
env={'__name__':'library'}
exec(compile((ROOT/'tools/blender/bruno_polish_v5.py').read_text(),'v5','exec'),env)
body=bpy.data.objects['Bruno_Body'];rig=bpy.data.objects['BrunoRig'];src=bpy.data.objects['Bruno_Coat_SOURCE_v4']
body.data.calc_loop_triangles()
tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],[tuple(t.vertices) for t in body.data.loop_triangles],all_triangles=True)
p=env['sculpt'](env['basis'](src.data));rng=random.Random(75712)
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def shade(p):
 x,y,z=p;c=Vector((.075,.032,.012));mantle=smooth(1.05,1.60,z)*math.exp(-((y-.34)/1.12)**2)*.62
 c=c.lerp(Vector((.125,.063,.026)),mantle)
 muzzle=smooth(1.62,1.93,y-abs(x)*.26)*(1-smooth(1.26,1.44,z))
 c=c.lerp(Vector((.145,.073,.030)),muzzle)
 return c.lerp(Vector((.018,.010,.006)),(1-smooth(.16,.52,z))*.79)
ranked=[]
for k in range(len(p)//7):
 root=(p[k*7]+p[k*7+1])*.5;x,y,z=root
 if z<.11 or y>1.78:continue
 if y>1.49 and abs(abs(x)-.29)<.070 and 1.24<z<1.40:continue
 importance=1.3 if z>1.10 and -.4<y<1.15 else 1
 ranked.append((rng.random()**(1/importance),k))
chosen=sorted(k for _,k in sorted(ranked,reverse=True)[:18000])
verts=[];faces=[];colors=[];weights=[];normals=[]
for k in chosen:
 q=p[k*7:k*7+7];root=Vector((q[0]+q[1])*.5);hit,n,_,_=tree.find_nearest(root)
 if hit is None:continue
 direction=Vector(q[6])-root;length=direction.length
 flow=direction-n*direction.dot(n)
 if flow.length<.0001:continue
 flow.normalize();cross=n.cross(flow).normalized()
 width=(Vector(q[1])-Vector(q[0])).length*.72
 if root.y>1.10:length*=.78;width*=.8
 length*=rng.uniform(.87,1.20)
 root=hit+n*.001
 tip_guess=root+flow*length
 tip,sn,_,_=tree.find_nearest(tip_guess)
 if tip is None or (tip-tip_guess).length>.06:tip=tip_guess;sn=n
 tip+=sn*(.004+length*.13)
 start=len(verts);verts.extend([root-cross*width,root+cross*width,tip]);faces.append((start,start+2,start+1))
 color=shade(root)*rng.uniform(.86,1.13)
 colors.extend([(*color*.76,1),(*color*.76,1),(*color*1.09,1)])
 ws=[(src.vertex_groups[g.group].name,g.weight) for g in src.data.vertices[k*7].groups]
 weights.extend([ws]*3);normals.extend([n,n,sn])
old=bpy.data.objects.get('Bruno_Coat')
if old:bpy.data.objects.remove(old,do_unlink=True)
me=bpy.data.meshes.new('Bruno_Groom_18000');me.from_pydata(verts,[],faces);me.update()
ob=bpy.data.objects.new('Bruno_Coat',me);bpy.context.scene.collection.objects.link(ob)
mat=bpy.data.materials.new('Bruno_V5_ShortFur');mat.use_nodes=True;nt=mat.node_tree
bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(1,1,1,1)
bs.inputs['Roughness'].default_value=.86;bs.inputs['Specular IOR Level'].default_value=.22
vc=nt.nodes.new('ShaderNodeVertexColor');vc.layer_name='Color';nt.links.new(vc.outputs['Color'],bs.inputs['Base Color'])
me.materials.append(mat);ca=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
ca.data.foreach_set('color',[x for c in colors for x in c])
uv=me.uv_layers.new(name='StrandFlow')
for f in me.polygons:
 f.use_smooth=True
 for li in f.loop_indices:uv.data[li].uv=[(0,0),(.5,1),(1,0)][li%3]
me.normals_split_custom_set([normals[l.vertex_index] for l in me.loops])
groups={g.name:ob.vertex_groups.new(name=g.name) for g in src.vertex_groups}
for i,ws in enumerate(weights):
 for name,w in ws:groups[name].add([i],w,'REPLACE')
ob.parent=rig;mod=ob.modifiers.new('Skin','ARMATURE');mod.object=rig
ob['fur_type']='18000 opaque single-triangle tapered locks, v4 root/flow/skin data; zero alpha overdraw'
print('FINAL GROOM',len(verts),len(faces))
