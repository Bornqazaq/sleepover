"""Repair the production foreleg's skin bindings, without changing mesh or rig.

Run through Blender MCP with the production Bruno.blend open. The source
weights are retained, so repeating the pass does not accumulate corrections.
"""
import bpy
import json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform


def smooth(a, b, value):
    t = max(0, min(1, (value - a) / (b - a)))
    return t * t * (3 - 2 * t)


def apply():
    body = bpy.data.objects['Bruno_Body']
    coat = bpy.data.objects['Bruno_Coat']
    key = 'BeforeStrikeSkinFix'
    if key not in body.data:
        body.data[key] = json.dumps([
            [(body.vertex_groups[g.group].name, g.weight) for g in v.groups]
            for v in body.data.vertices])
    original = json.loads(body.data[key])
    weights = []

    def assign(obj, indices, values):
        ranked = sorted(((n, w) for n, w in values.items() if w > 1e-7),
                        key=lambda item: -item[1])[:4]
        total = sum(w for _, w in ranked)
        assert total > 0
        for i in indices:
            old_groups = [g.group for g in obj.data.vertices[i].groups]
            for group in old_groups:
                obj.vertex_groups[group].remove([i])
        for name, weight in ranked:
            group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
            group.add(indices, weight / total, 'REPLACE')
        return {n: w / total for n, w in ranked}

    for vertex, saved in zip(body.data.vertices, original):
        w = dict(saved)
        x, y, z = vertex.co
        for side in ('L', 'R'):
            names = ['Fore' + part + '.' + side for part in ('Upper', 'Lower', 'Paw', 'Toes')]
            total = sum(w.get(n, 0) for n in names)
            if total < .001:
                continue
            # The old transfer let the wrist move the upper arm and let the
            # humerus lift the ribs/back. Blend continuously through the elbow
            # and shoulder; the scapula, chest and spine retain the flank.
            upper = smooth(.53, .80, z)
            paw = (1 - smooth(.29, .43, z)) * (1 - upper)
            blend = smooth(.32, .49, z)
            fractions = (upper, 1 - upper - paw, paw, 0)
            shoulder = smooth(.94, 1.48, z)
            for name, fraction in zip(names, fractions):
                w[name] = (w.get(name, 0) * (1 - blend) + total * fraction * blend) * (1 - shoulder)
            moved = total * shoulder
            chest = smooth(-.30, .36, y)
            w['Chest'] = w.get('Chest', 0) + moved * chest * .75
            w['Spine'] = w.get('Spine', 0) + moved * (1 - chest) * .75
            scapula = 'Scapula.' + side
            w[scapula] = w.get(scapula, 0) + moved * .25
            if y > -.24 and w.get('Lumbar', 0) > 0:
                moved = w['Lumbar'] * smooth(-.24, .05, y)
                w['Lumbar'] -= moved
                w['Chest'] = w.get('Chest', 0) + moved * .78
                w['Spine'] = w.get('Spine', 0) + moved * .22
        # Sole/pad weights were fitted to the planted skin in the production
        # gait. Preserve them, feathering the repair into the forearm above.
        blend = smooth(.45, .62, z)
        old = dict(saved)
        w = {name: old.get(name, 0) * (1 - blend) + w.get(name, 0) * blend
             for name in old.keys() | w.keys()}
        weights.append(assign(body, [vertex.index], w))

    # Transfer at the root of each connected fur lock (seven vertices in the
    # production groom). A lock has several triangles sharing vertices.
    # All its vertices need identical weights, including the tapered tip.
    body.data.calc_loop_triangles()
    triangles = [tuple(t.vertices) for t in body.data.loop_triangles]
    coords = [v.co.copy() for v in body.data.vertices]
    tree = BVHTree.FromPolygons(coords, triangles, all_triangles=True)
    parents = list(range(len(coat.data.vertices)))
    def root(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i
    for edge in coat.data.edges:
        a, b = (root(i) for i in edge.vertices)
        parents[b] = a
    locks = {}
    for i in range(len(parents)):
        locks.setdefault(root(i), []).append(i)
    for indices in locks.values():
        assert len(indices) == 7, 'Unexpected production fur topology'
        point = (coat.data.vertices[indices[0]].co + coat.data.vertices[indices[1]].co) * .5
        hit, _, triangle, _ = tree.find_nearest(point)
        ids = triangles[triangle]
        bary = barycentric_transform(hit, *(coords[i] for i in ids),
                                     Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
        values = {}
        for i, part in zip(ids, bary):
            for name, weight in weights[i].items():
                values[name] = values.get(name, 0) + weight * max(0, part)
        assign(coat, indices, values)
    print('Foreleg skin repaired; unchanged topology, UV, skeleton and animation.')


if __name__ == '__main__':
    apply()
