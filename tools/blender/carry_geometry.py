"""Carry-only edge finish. Executed after the shared primitive definitions.

Keep authored dimensions/pivots, use three-segment fillets and weighted normals.
No change to the shared Crying Angels kit or to material/face ordering.
"""
_carry_mesh_object = Mesh.object


def _carry_object(self, name):
    obj = _carry_mesh_object(self, name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for face in bm.faces:
        face.smooth = True
    for edge in bm.edges:
        edge.smooth = edge.is_manifold and edge.calc_face_angle() < math.radians(55)
    bm.to_mesh(obj.data)
    bm.free()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    modifier = obj.modifiers.new('Carry weighted corner normals', 'WEIGHTED_NORMAL')
    modifier.keep_sharp = True
    modifier.weight = 50
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


Mesh.object = _carry_object
_box_cache = {}


def box(m, pos, size, mat=STONE, bevel=.025, matrix=None):
    # A broad concrete chamfer, a small rolled metal edge; thin labels stay thin.
    radius = min(bevel * 1.8, min(size) * .30) if bevel else 0
    key = (*size, radius)
    if key not in _box_cache:
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1)
        for vertex in bm.verts:
            vertex.co.x *= size[0]
            vertex.co.y *= size[1]
            vertex.co.z *= size[2]
        if radius:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=radius,
                           segments=3, affect='EDGES')
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        _box_cache[key] = ([tuple(v.co) for v in bm.verts],
                           [tuple(v.index for v in f.verts) for f in bm.faces])
        bm.free()
    verts, faces = _box_cache[key]
    placement = Matrix.Translation(Vector(pos))
    m.add(verts, faces, mat, matrix @ placement if matrix else placement)
