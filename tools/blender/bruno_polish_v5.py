"""Refine the live v4 Bruno mesh in rest space; keep the original skin, UV and rig.

Source backup: art-source/BrunoMeshy/polish-v5/Bruno_Before.blend.
Run through Blender MCP in the separate Bruno_Polished.blend file.
"""
import bpy
import json
import math
import random
from pathlib import Path
import numpy as np
from mathutils import Vector

ROOT = Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover')
OUT = ROOT / 'art-source/BrunoMeshy/polish-v5'
BODY = bpy.data.objects['Bruno_Body']
RIG = bpy.data.objects['BrunoRig']


def smooth(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)


def basis(mesh):
    a = mesh.attributes.get('BrunoPolishV5Basis')
    if a is None:
        points = np.empty(len(mesh.vertices)*3, dtype=np.float32)
        mesh.vertices.foreach_get('co', points)
        a = mesh.attributes.new('BrunoPolishV5Basis', 'FLOAT_VECTOR', 'POINT')
        a.data.foreach_set('vector', points)
    points = np.empty(len(mesh.vertices)*3, dtype=np.float32)
    a.data.foreach_get('vector', points)
    return points.reshape(-1, 3).astype(np.float64)


def sculpt(points):
    p = points.copy()
    x, y, z = points.T
    side = np.abs(x)
    # Taper the lower limb around the existing bone axes. The shoulder mantle
    # and the planted sole stay in place; elbows and hocks gain direction.
    lateral = smooth(.32, .54, side)
    front = np.exp(-((y-.38)/.51)**6) * lateral
    rear = np.exp(-((y+.88)/.48)**6) * lateral
    fore_taper = .24*np.exp(-((z-.51)/.26)**2) + .08*np.exp(-((z-.30)/.12)**2)
    hind_taper = .20*np.exp(-((z-.43)/.24)**2)
    protect_sole = smooth(.13, .27, z)
    p[:, 0] -= (x-np.sign(x)*.671)*(front*fore_taper+rear*hind_taper)*protect_sole
    p[:, 1] -= (y-.38)*front*fore_taper*protect_sole
    p[:, 1] -= (y+.88)*rear*hind_taper*protect_sole
    p[:, 1] -= .050*front*np.exp(-((z-.67)/.15)**2)
    p[:, 1] += .040*rear*np.exp(-((z-.64)/.16)**2)
    p[:, 1] -= .048*rear*np.exp(-((z-.32)/.13)**2)
    # Clear knuckle scallops and the four grooves between each five-toe fan.
    for center_y, toe_y in ((.45, .85), (-.95, -.57)):
        paw = np.exp(-((y-toe_y)/.15)**2)*(1-smooth(.22,.32,z))*smooth(.015,.07,z)
        for s in (-1, 1):
            for k in range(5):
                tx=s*.671+(k-2)*.090
                knuckle=np.exp(-((x-tx)/.035)**2)*paw
                p[:, 1] += .022*knuckle
                p[:, 2] += .020*knuckle*smooth(.07,.14,z)
            for k in range(4):
                gx=s*.671+(k-1.5)*.090
                groove=np.exp(-((x-gx)/.015)**2)*paw
                p[:, 1] -= .044*groove
                p[:, 2] -= .013*groove*smooth(.085,.16,z)
    # Blunt bear muzzle and broad nasal bridge, without changing the skull mass.
    face=smooth(1.56,1.90,y)
    p[:, 1] -= .050*face
    p[:, 0] *= 1+.075*face*(1-smooth(1.30,1.47,z))
    # Brow and zygomatic planes are sculpted into the continuous skin.
    brow=np.exp(-((side-.26)/.12)**4-((y-1.625)/.16)**4-((z-1.365)/.055)**2)
    p[:, 1] += .032*brow
    p[:, 2] -= .013*brow*(1-smooth(.16,.31,side))
    cheek=np.exp(-((side-.31)/.11)**2-((y-1.39)/.25)**2-((z-1.16)/.15)**2)
    p[:, 0] += np.sign(x)*.018*cheek
    # The upper muzzle covers the incisor roots; corners do not turn upwards.
    lip=np.exp(-((z-.986)/.065)**2)*smooth(1.72,1.94,y)
    p[:, 2] -= .014*lip*smooth(.09,.22,side)
    return p


def skin():
    me=BODY.data
    points=basis(me)
    p=sculpt(points)
    islands=json.loads((OUT/'body-islands-before.json').read_text())
    # Cavity and mandible remain separate, closed, weighted jaw components.
    ids=islands[2]['ids']
    p[ids,2]=.915+(points[ids,2]-.915)*1.65-.008
    ids=islands[3]['ids']
    p[ids,2]=.912+(points[ids,2]-.912)*1.50-.034
    p[ids,0]*=1.055
    p[ids,1]-=.030
    # The mandibular rami reach back into the cheek rather than floating as a plate.
    p[ids,1]-=.080*(1-smooth(1.60,1.86,points[ids,1]))
    # Flatten the lip line and suppress the row of smiling incisors.
    ids=islands[6]['ids']
    p[ids,2]+=.016-.032*smooth(.06,.22,np.abs(points[ids,0]))
    for idx in (7,8):
        p[islands[idx]['ids'],2]-=.032
    for idx in range(50,56):
        ids=islands[idx]['ids'];p[ids,1]-=.019;p[ids,2]+=.014
    # Each existing canine is a curved multi-ring tooth. Broaden its root,
    # taper its crown and extend the hook instead of adding a white cone.
    for idx in (41,42):
        ids=islands[idx]['ids']; q=points[ids];t=np.clip((1.0-q[:,2])/.11,0,1)
        p[ids,2]-=.043*t
        p[ids,1]+=.015*np.sin(t*math.pi)-.014*t*t
        p[ids,0]-=np.sign(q[:,0])*.008*t*t
    for idx in (44,45):
        ids=islands[idx]['ids'];p[ids,2]-=.024
    # Small recessed eyes: a lower, more horizontal upper lid.
    for idx in (17,19):
        ids=islands[idx]['ids'];p[ids,2]-=.004
        p[ids,1]+=.002
    # Keep the eyes small but give the corneal/iris assembly enough screen area.
    for idx in (4,5,9,10,11,12):
        ids=islands[idx]['ids'];center=points[islands[4 if points[ids,0].mean()<0 else 5]['ids']].mean(axis=0)
        p[ids]=sculpt(center.reshape(1,3))[0]+(p[ids]-sculpt(center.reshape(1,3))[0])*1.10
    # Vary existing claws by toe. Roots stay at the toes; tips hook and splay.
    for idx in range(21,41):
        ids=islands[idx]['ids'];q=points[ids];cx=float(q[:,0].mean())
        root_y=float(q[:,1].min());length=float(np.ptp(q[:,1]));t=np.clip((q[:,1]-root_y)/length,0,1)
        rank=int(round((abs(cx)-.491)/.09));factor=[.84,1.03,1.13,1.01,.82][rank]
        p[ids,1]=root_y+(q[:,1]-root_y)*factor+.016
        p[ids,0]=q[:,0]+np.sign(cx)*(rank-2)*.011*t*t
        p[ids,2]=q[:,2]+.011*np.sin(t*math.pi)-.008*t*t
    me.vertices.foreach_set('co',p.astype(np.float32).ravel());me.update()
    BODY['polish_revision']='v5: existing skin sculpt, paw grooves, brow, mandible, hooked varied claws'
    for n in BODY.data.materials[0].node_tree.nodes:
        if n.type=='NORMAL_MAP':n.inputs['Strength'].default_value=.48
    return points,p


def fur():
    old=bpy.data.objects.get('Bruno_Coat_SOURCE_v4')
    if old is None:
        old=bpy.data.objects['Bruno_Coat'];old.name='Bruno_Coat_SOURCE_v4'
        archive=bpy.data.collections.new('SOURCE_ARCHIVE_DO_NOT_EXPORT')
        bpy.context.scene.collection.children.link(archive)
        for c in list(old.users_collection):c.objects.unlink(old)
        archive.objects.link(old);archive.hide_render=True;archive.hide_viewport=True
    if bpy.data.objects.get('Bruno_Coat'):
        bpy.data.objects.remove(bpy.data.objects['Bruno_Coat'],do_unlink=True)
    src=old.data;points=basis(src);transformed=sculpt(points)
    rng=random.Random(20261012)
    ranked=[]
    for k in range(len(points)//7):
        root=(points[k*7]+points[k*7+1])*.5
        x,y,z=root
        if z<.24 or y>1.71:continue
        if 1.50<y<1.71 and abs(abs(x)-.29)<.08 and 1.24<z<1.40:continue
        # Concentrate locks where they change the silhouette, not on the soles.
        importance=1.0
        if z>1.18 and -.45<y<1.13:importance=2.3
        if y>1.05:importance=.55
        ranked.append((rng.random()**(1/importance),k))
    chosen=sorted(k for _,k in sorted(ranked,reverse=True)[:3400])
    verts=[];faces=[];uv=[];colors=[];weights=[]
    olduv=src.uv_layers.active
    # Capture a representative atlas coordinate for every original vertex.
    srcuv={}
    for loop in src.loops:srcuv.setdefault(loop.vertex_index,tuple(olduv.data[loop.index].uv))
    for k in chosen:
        q=transformed[k*7:k*7+7].copy();root=(q[0]+q[1])*.5
        length_factor=rng.uniform(.85,1.18)
        if root[1]>1.10:length_factor*=.78
        width_factor=rng.uniform(10.0,15.0)
        for j in (0,2,4):
            mid=(q[j]+q[j+1])*.5
            cross=(q[j+1]-q[j])*.5
            q[j]=mid-cross*width_factor;q[j+1]=mid+cross*width_factor
        q=root+(q-root)*length_factor
        start=len(verts)
        for j in range(7):
            verts.append(q[j]);uv.append(((j%2 if j<6 else .5)/8+(k%8)/8,[0,0,.37,.37,.76,.76,1][j]))
            weights.append([(old.vertex_groups[g.group].name,g.weight) for g in src.vertices[k*7+j].groups])
        faces.extend([(start,start+2,start+1),(start+1,start+2,start+3),
                      (start+2,start+4,start+3),(start+3,start+4,start+5),(start+4,start+6,start+5)])
        # Gentle lock-to-lock variation, with a warm mantle and dark extremities.
        tone=rng.uniform(.87,1.09)
        mantle=.10*float(smooth(.9,1.6,root[2]))
        for j in range(7):colors.append((tone*(1+mantle),tone,tone*(1-mantle*.6),1))
    me=bpy.data.meshes.new('Bruno_Groom_LOD0');me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new('Bruno_Coat',me);bpy.context.scene.collection.objects.link(ob)
    mat=bpy.data.materials.new('Bruno_V5_Fur');mat.use_nodes=True
    nt=mat.node_tree;bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
    tex=nt.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(OUT/'Bruno_FurCards_RGBA.png'),check_existing=True)
    attr=nt.nodes.new('ShaderNodeVertexColor');attr.layer_name='Color'
    mul=nt.nodes.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1
    nt.links.new(tex.outputs['Color'],mul.inputs[1]);nt.links.new(attr.outputs['Color'],mul.inputs[2])
    nt.links.new(mul.outputs[0],bs.inputs['Base Color']);bs.inputs['Roughness'].default_value=.86
    nt.links.new(tex.outputs['Alpha'],bs.inputs['Alpha'])
    mat.surface_render_method=next(i.identifier for i in bpy.types.Material.bl_rna.properties['surface_render_method'].enum_items if i.identifier=='DITHERED')
    bs.inputs['Specular IOR Level'].default_value=.22
    me.materials.append(mat)
    layer=me.uv_layers.new(name='StrandFlow')
    for poly in me.polygons:
        poly.use_smooth=True
        for li in poly.loop_indices:layer.data[li].uv=uv[me.loops[li].vertex_index]
    ca=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
    ca.data.foreach_set('color',[c for col in colors for c in col])
    groups={g.name:ob.vertex_groups.new(name=g.name) for g in old.vertex_groups}
    for i,ws in enumerate(weights):
        for name,w in ws:groups[name].add([i],w,'REPLACE')
    mod=ob.modifiers.new('Skin','ARMATURE');mod.object=RIG;ob.parent=RIG
    ob['fur_type']='3400 curved alpha hair cards; 8 procedural cluster atlas variants; retained v4 roots/flow/skin weights'
    return ob


def pads():
    previous=bpy.data.objects.get('Bruno_PawPads')
    if previous:bpy.data.objects.remove(previous,do_unlink=True)
    mat=bpy.data.materials.get('Bruno_V5_Pads') or bpy.data.materials.new('Bruno_V5_Pads')
    mat.use_nodes=True
    bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(.022,.013,.009,1);bs.inputs['Roughness'].default_value=.78
    created=[]
    for s,side in ((-1,'R'),(1,'L')):
        for prefix,py in (('Fore',.57),('Hind',-.84)):
            specs=[(s*.671,py,.024,.142,.156,.030,prefix+'Paw.'+side)]
            for k in range(5):
                specs.append((s*.671+(k-2)*.09,py+.226-abs(k-2)*.017,.025,.033,.055,.023,prefix+'Toes.'+side))
            for x,y,z,sx,sy,sz,bone in specs:
                bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,location=(x,y,z))
                ob=bpy.context.object;ob.scale=(sx,sy,sz)
                bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
                ob.data.materials.append(mat)
                for f in ob.data.polygons:f.use_smooth=True
                ob.vertex_groups.new(name=bone).add(list(range(len(ob.data.vertices))),1,'REPLACE')
                created.append(ob)
    bpy.ops.object.select_all(action='DESELECT')
    for o in created:o.select_set(True)
    bpy.context.view_layer.objects.active=created[0];bpy.ops.object.join()
    ob=bpy.context.object;ob.name='Bruno_PawPads'
    # Apply the remaining origin offset before binding to a rig at the world origin.
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    ob.parent=RIG;mod=ob.modifiers.new('Skin','ARMATURE');mod.object=RIG
    return ob


def main():
    RIG.data.pose_position='REST'
    skin();coat=fur();pads()
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    BODY.select_set(True);bpy.context.view_layer.objects.active=BODY
    stats={}
    for name in ('Bruno_Body','Bruno_Coat','Bruno_PawPads'):
        o=bpy.data.objects[name];o.data.calc_loop_triangles()
        stats[name]={'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles)}
    (OUT/'geometry-stats.json').write_text(json.dumps(stats,indent=2))
    print('V5 GEOMETRY',stats)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Polished.blend'))


if __name__=='__main__':main()
