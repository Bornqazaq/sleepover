"""Replace only Bruno's floating lip/jaw inserts with coherent skinned mouth parts."""
import bpy,bmesh,json,math
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
OUT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover/art-source/BrunoMeshy/polish-v5')
body=bpy.data.objects['Bruno_Body'];rig=bpy.data.objects['BrunoRig'];me=body.data
parts=json.loads((OUT/'body-islands-before.json').read_text())
skinids=set(parts[0]['ids']);me.calc_loop_triangles()
tree=BVHTree.FromPolygons([v.co for v in me.vertices],[tuple(t.vertices) for t in me.loop_triangles if t.vertices[0] in skinids],all_triangles=True)
if not body.get('v5_mouth_rebuilt'):
    remove=set(v for i in [2,3,6,7,8,*range(41,56)] for v in parts[i]['ids'])
    bm=bmesh.new();bm.from_mesh(me);bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[bm.verts[i] for i in remove],context='VERTS');bm.to_mesh(me);bm.free();me.update()
    body['v5_mouth_rebuilt']=True
if bpy.data.objects.get('Bruno_Mouth'):bpy.data.objects.remove(bpy.data.objects['Bruno_Mouth'],do_unlink=True)
def material(name,color,roughness):
    mat=bpy.data.materials.get(name) or bpy.data.materials.new(name);mat.use_nodes=True
    bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*color,1);bs.inputs['Roughness'].default_value=roughness
    return mat
mats=[material('V5_Mandible',(.12,.055,.022),.84),material('V5_Lips',(.021,.009,.005),.67),
      material('V5_Oral',(.009,.002,.0015),.8),material('V5_Ivory',(.52,.43,.27),.42),
      material('V5_Tongue',(.11,.033,.026),.6)]
objects=[]
def mesh(name,verts,faces,mat,bone):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(ob)
    me.materials.append(mats[mat]);ob.vertex_groups.new(name=bone).add(list(range(len(verts))),1,'REPLACE')
    for p in me.polygons:p.use_smooth=True
    objects.append(ob);return ob
def tube(name,points,radii,mat,bone,sides=10):
    v=[];f=[]
    for j,pt in enumerate(points):
        p=Vector(pt);t=(Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])).normalized()
        ref=Vector((1,0,0)) if abs(t.x)<.85 else Vector((0,1,0))
        u=t.cross(ref).normalized();w=t.cross(u).normalized()
        for k in range(sides):v.append(p+radii[j]*(math.cos(k*math.tau/sides)*u+math.sin(k*math.tau/sides)*w))
        if j:
            for k in range(sides):f.append(((j-1)*sides+k,(j-1)*sides+(k+1)%sides,j*sides+(k+1)%sides,j*sides+k))
    f+=[tuple(reversed(range(sides))),tuple((len(points)-1)*sides+k for k in range(sides))]
    return mesh(name,v,f,mat,bone)
def oval(name,loc,scale,mat,bone,seg=24,rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=loc)
    ob=bpy.context.object;ob.name=name;ob.scale=scale;bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    ob.data.materials.append(mats[mat]);ob.vertex_groups.new(name=bone).add(list(range(len(ob.data.vertices))),1,'REPLACE')
    for p in ob.data.polygons:p.use_smooth=True
    objects.append(ob);return ob
# A long mandibular ramus intersects the existing cheek at the hinge.
jaw=oval('V5_LowerJaw',(0,1.69,.865),(.226,.303,.122),0,'Jaw',32,16)
for v in jaw.data.vertices:
    if v.co.z>.933:v.co.z=.933+(v.co.z-.933)*.24
    # Flat broad underside, curved side walls and a blunt chin.
    if v.co.z<.776:v.co.z=.776+(v.co.z-.776)*.38
oval('V5_MouthInterior',(0,1.785,.952),(.187,.173,.051),2,'Head')
oval('V5_LowerGum',(0,1.785,.931),(.166,.155,.020),2,'Jaw')
oval('V5_Tongue',(0,1.78,.943),(.099,.118,.009),4,'Jaw',20,8)
upper=[];lower=[]
for j in range(29):
    t=j/28*math.pi;x=.22*math.cos(t);y=1.73+.247*math.sin(t)
    hit,_,_,_=tree.ray_cast(Vector((x,y,.78)),Vector((0,0,1)))
    z=(hit.z if hit else 1.01)+.003
    upper.append((x,y,z))
    lower.append((.207*math.cos(t),1.69+.268*math.sin(t),.936-.013*math.sin(t)))
tube('V5_UpperLip',upper,[.0095]*len(upper),1,'Head',8)
tube('V5_LowerLip',lower,[.009]*len(lower),1,'Jaw',8)
for sign in (-1,1):
    tube('V5_UpperCanine',[(sign*.179,1.881,1.020),(sign*.181,1.89,.997),
        (sign*.18,1.91,.957),(sign*.174,1.927,.908),(sign*.163,1.927,.870),(sign*.153,1.917,.850)],
        [.025,.024,.019,.013,.006,.0007],3,'Head',10)
    tube('V5_LowerCanine',[(sign*.164,1.803,.927),(sign*.162,1.822,.948),(sign*.157,1.831,.976),
        (sign*.151,1.831,.994)],[.017,.013,.007,.0006],3,'Jaw',8)
    for j in range(2):
        tube('V5_Premolar',[(sign*.17,1.755+j*.041,.983),(sign*.17,1.76+j*.041,.968),
            (sign*.167,1.765+j*.041,.964)],[.011,.010,.003],3,'Head',8)
# Short, individually seated front incisors show only a narrow edge.
for j in range(6):
    x=(j-2.5)*.028
    tube('V5_Incisor',[(x,1.958,1.009),(x,1.962,.989),(x,1.961,.985)],
        [.011,.010,.006],3,'Head',8)
bpy.ops.object.select_all(action='DESELECT')
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=jaw;bpy.ops.object.join();ob=bpy.context.object;ob.name='Bruno_Mouth'
ob.parent=rig;mod=ob.modifiers.new('Skin','ARMATURE');mod.object=rig
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Polished.blend'))
print('MOUTH REBUILT',len(ob.data.vertices),len(ob.data.polygons))
