"""Replace fused mitten fingertips with articulated glove topology for pointing.
The supplied torso, clothing, head, mane, legs and feet remain the Meshy surface.
"""
import bpy,bmesh,math
from mathutils import Vector
body=bpy.data.objects['Horse_Meshy_Skin'];rig=bpy.data.objects['Ringmaster_Rig'];rig.data.pose_position='REST'
body.shape_key_clear()
bm=bmesh.new();bm.from_mesh(body.data)
remove=[]
for f in bm.faces:
    p=f.calc_center_median();sign=1 if p.x>0 else -1
    origin=Vector((sign*1.27,-.03,1.88));direction=Vector((sign*.18,0,-.23)).normalized()
    if abs(p.x)>1.1 and (p-origin).dot(direction)>.055:remove.append(f)
bmesh.ops.delete(bm,geom=remove,context='FACES');bm.to_mesh(body.data);bm.free();body.data.update()
# Any retained glove border follows the wrist as a whole.
for v in body.data.vertices:
    if abs(v.co.x)>1.15 and v.co.z<1.94:
        for g in list(v.groups):body.vertex_groups[g.group].remove([v.index])
        body.vertex_groups['Hand.'+('L' if v.co.x>0 else 'R')].add([v.index],1,'REPLACE')
body.shape_key_add(name='Basis');smile=body.shape_key_add(name='LaughSmile');smile.value=0
for v,k in zip(body.data.vertices,smile.data):
    x,y,z=v.co
    if y<-.30 and 2.98<z<3.43:
        a=math.exp(-((abs(x)-.20)/.10)**2-((z-3.035)/.13)**2)*max(0,min(1,(-.30-y)/.20))
        k.co.x+=math.copysign(.035*a,x);k.co.z+=.045*a
mat=bpy.data.materials.new('Soft charcoal gloves');mat.use_nodes=True
p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(.030,.034,.031,1);p.inputs['Roughness'].default_value=.64
parts=[]
def ellipsoid(name,center,scale,direction,bone):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,location=center);o=bpy.context.object;o.name=name;o.scale=scale;o.rotation_euler=Vector(direction).to_track_quat('Z','Y').to_euler()
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    o.data.materials.append(mat)
    for f in o.data.polygons:f.use_smooth=True
    o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))),1,'REPLACE');m=o.modifiers.new('Ringmaster skin','ARMATURE');m.object=rig;o.parent=rig;parts.append(o)
# Reposition finger joints to the separated glove fingers.
bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
positions={}
for side,sign in [('L',1),('R',-1)]:
    wrist=Vector((sign*1.27,-.03,1.88));d=Vector((sign*.18,0,-.23)).normalized();across=Vector((0,1,0));normal=d.cross(across)
    for name,offset,length in [('Index',-.082,.22),('Middle',0,.235),('Ring',.082,.195)]:
        a=wrist+d*.25+across*offset;b=a+d*(length*.54);c=a+d*length
        rig.data.edit_bones[name+'1.'+side].head=a;rig.data.edit_bones[name+'1.'+side].tail=b
        rig.data.edit_bones[name+'2.'+side].head=b;rig.data.edit_bones[name+'2.'+side].tail=c
        positions[name+'.'+side]=(a,b,c,.045)
    a=wrist+d*.12-across*.14;b=a+d*.085-across*.04;c=b+d*.08
    rig.data.edit_bones['Thumb1.'+side].head=a;rig.data.edit_bones['Thumb1.'+side].tail=b
    rig.data.edit_bones['Thumb2.'+side].head=b;rig.data.edit_bones['Thumb2.'+side].tail=c
    positions['Thumb.'+side]=(a,b,c,.050)
bpy.ops.object.mode_set(mode='OBJECT')
for side,sign in [('L',1),('R',-1)]:
    wrist=Vector((sign*1.27,-.03,1.88));d=Vector((sign*.18,0,-.23)).normalized()
    ellipsoid('Glove palm.'+side,wrist+d*.15,(.125,.10,.175),d,'Hand.'+side)
    for name in ['Index','Middle','Ring','Thumb']:
        a,b,c,r=positions[name+'.'+side]
        for j,(u,v) in enumerate([(a,b),(b,c)]):
            ellipsoid(name+' segment '+str(j)+'.'+side,(u+v)*.5,(r,r*.91,(v-u).length*.5+r*.45),v-u,name+str(j+1)+'.'+side)
        ellipsoid(name+' knuckle.'+side,a,(r*1.07,r*.98,r),d,'Hand.'+side)
rig.data.pose_position='POSE'
print('Articulated gloves',len(parts),'parts; preserved Meshy cuffs')
