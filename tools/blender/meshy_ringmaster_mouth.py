"""Open a real lip seam in the otherwise closed Meshy sculpt before skinning."""
import bpy,bmesh
from mathutils import Vector
body=bpy.data.objects['Horse_Meshy_Skin']
# Clean only generated rig components so the downloaded surface/paint survives.
body.shape_key_clear()
body.vertex_groups.clear()
for m in list(body.modifiers):
    if m.type=='ARMATURE':body.modifiers.remove(m)
body.parent=None
for name in ['Ringmaster_Rig','UpperLid.L','UpperLid.R','MouthInterior']:
    ob=bpy.data.objects.get(name)
    if ob:bpy.data.objects.remove(ob,do_unlink=True)
bm=bmesh.new();bm.from_mesh(body.data)
# The lip seam rises toward the rear corners, underneath the upper teeth.
plane=Vector((0,-.18,1));offset=3.122
faces=[f for f in bm.faces if all(2.78<v.co.z<3.30 and v.co.y<-.27 for v in f.verts)]
geom=set(faces)
for f in faces:geom.update(f.edges);geom.update(f.verts)
res=bmesh.ops.bisect_plane(bm,geom=list(geom),dist=.00001,plane_co=(0,0,offset),plane_no=plane)
edges=[e for e in res['geom_cut'] if isinstance(e,bmesh.types.BMEdge)]
bmesh.ops.split_edges(bm,edges=edges)
# Store a stable lower-lip membership across the split (coincident seam verts).
attr=bm.verts.layers.float.new('LowerLip')
for v in bm.verts:
    x,y,z=v.co
    lower=all((plane.dot(f.calc_center_median())-offset)<.00001 for f in v.link_faces)
    if y<-.29 and z>2.80 and lower:
        t=max(0,min(1,(-.29-y)/.20));v[attr]=t*t*(3-2*t)
bm.to_mesh(body.data);bm.free();body.data.update()
print('Opened lip seam',len(edges),'edges')
