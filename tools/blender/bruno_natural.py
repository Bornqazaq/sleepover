"""IGR-757: soften the exaggerated v3 masses without changing gait or soles.

An export-stage rest-space sculpt, shared by skin and coat. The point attribute
keeps an editable basis, so reapplying this pass is deterministic and reversible.
UVs, weights, skeleton, contact geometry and all six actions are preserved.
"""
import bpy
import numpy as np
from mathutils import Vector, Quaternion
from mathutils.bvhtree import BVHTree
import math
import json
from mathutils.geometry import barycentric_transform

REVISION = '2026-10-12-natural-1'
BASIS = 'BrunoNaturalBasis'


def smooth(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)


def sculpt(points):
    p = points.astype(np.float64)
    x, y, z = p.T.copy()
    side = np.abs(x)
    above_paw = smooth(.28, .64, z)
    torso = 1-smooth(.88, 1.40, y)
    shoulder = np.exp(-((y-.36)/.63)**2-((z-1.15)/1.10)**2)
    haunch = np.exp(-((y+.91)/.56)**2-((z-.91)/1.10)**2)
    # Bring the lateral shoulder/thigh lobes into the barrel silhouette.
    p[:, 0] -= np.sign(x)*(.235*shoulder+.25*haunch+.025*torso)*smooth(.28, .95, side)*above_paw*torso
    limb = smooth(.40, .82, side)*above_paw*(1-smooth(1.05, 1.65, z))
    p[:, 1] -= limb*(.27*(y-.34)*np.exp(-((y-.34)/.62)**4)
                       + .24*(y+.90)*np.exp(-((y+.90)/.57)**4))
    # A low, continuous shoulder mantle replaces the high domed hump.
    p[:, 2] -= .14*np.exp(-((y-.37)/.80)**2)*smooth(1.10, 1.73, z)*torso
    return p.astype(np.float32)


def groom():
    """Blend the previous hard chest/leg grooming boundary into a soft coat."""
    skin = bpy.data.objects['Bruno_Body'].data
    coat = bpy.data.objects['Bruno_Coat'].data
    skin.calc_loop_triangles()
    surface = BVHTree.FromPolygons([v.co for v in skin.vertices],
                                  [tuple(t.vertices) for t in skin.loop_triangles], all_triangles=True)
    for i in range(0, len(coat.vertices), 7):
        verts = [coat.vertices[i+j] for j in range(7)]
        root = (verts[0].co+verts[1].co)*.5
        amount = float(smooth(.32, .52, root.z)*(1-smooth(.93, 1.23, root.y)))
        if amount < .001:
            continue
        hit, normal, _, _ = surface.find_nearest(root)
        if hit is None:
            continue
        leg = float((1-smooth(.58, 1.14, root.z))*smooth(.26, .53, abs(root.x)))
        desired = Vector((normal.x*.08*leg, -.85*(1-leg)-.08*leg,
                          -.78*abs(normal.x)*(1-leg)-leg))
        desired -= normal*desired.dot(normal)
        old = verts[-1].co-root
        old -= normal*old.dot(normal)
        if desired.length < .001 or old.length < .001:
            continue
        desired.normalize(); old.normalize()
        angle = math.atan2(normal.dot(old.cross(desired)), old.dot(desired))*amount
        turn = Quaternion(normal, angle)
        # Shorter, quieter locks on the torso, retaining the existing variation.
        length = 1-.18*amount*(1-leg)
        for v in verts:
            v.co = root+(turn@(v.co-root))*length
    coat.update()


def soften_skin():
    """Spread the elbow/knee skin transition; preserve contact vertices exactly."""
    body = bpy.data.objects['Bruno_Body']
    coat = bpy.data.objects['Bruno_Coat']
    key = 'natural_weights_basis'
    if key not in body.data:
        body.data[key] = json.dumps([[(g.group, g.weight) for g in v.groups] for v in body.data.vertices])
    original = json.loads(body.data[key])
    names = {g.index:g.name for g in body.vertex_groups}
    groups = {g.name:g for g in body.vertex_groups}
    basis = body.data.attributes[BASIS]
    allweights = []
    for v, saved in zip(body.data.vertices, original):
        w = {names[g]:a for g,a in saved}
        p = basis.data[v.index].vector
        blend = float(smooth(.28, .43, p.z)*(1-smooth(.98, 1.25, p.z)))
        if blend > 0 and p.y < 1.10:
            limb = 'Fore' if p.y > -.22 else 'Hind'
            side = 'L' if p.x > 0 else 'R'
            legnames = [limb+part+'.'+side for part in ('Upper','Lower','Paw','Toes')]
            total = sum(w.get(n,0) for n in legnames)
            if total > .05:
                upper = float(smooth(.42 if limb=='Fore' else .38, 1.06 if limb=='Fore' else 1.0, p.z))
                lower = float(smooth(.23,.41,p.z))*(1-upper)
                paw = 1-upper-lower
                oldfoot = w.get(legnames[2],0)+w.get(legnames[3],0)
                toe = w.get(legnames[3],0)/oldfoot if oldfoot > 1e-8 else 0
                for n,fraction in zip(legnames,(upper,lower,paw*(1-toe),paw*toe)):
                    w[n] = w.get(n,0)*(1-blend)+total*fraction*blend
        ranked = sorted(((n,a) for n,a in w.items() if a>1e-7),key=lambda item:-item[1])[:4]
        total = sum(a for _,a in ranked)
        w = {n:a/total for n,a in ranked}
        allweights.append(w)
        for group_index in [g.group for g in v.groups]:body.vertex_groups[group_index].remove([v.index])
        for n,a in w.items():groups[n].add([v.index],a,'REPLACE')
    body.data.calc_loop_triangles()
    triangles = [tuple(t.vertices) for t in body.data.loop_triangles]
    surface = BVHTree.FromPolygons([v.co for v in body.data.vertices],triangles,all_triangles=True)
    for i in range(0,len(coat.data.vertices),7):
        root = (coat.data.vertices[i].co+coat.data.vertices[i+1].co)*.5
        if root.z <= .28:
            continue
        hit, _, index, _ = surface.find_nearest(root)
        if hit is None:continue
        ids = triangles[index]
        bary = barycentric_transform(hit,*(body.data.vertices[j].co for j in ids),
                                     Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
        w = {}
        for j,part in zip(ids,bary):
            for n,a in allweights[j].items():w[n]=w.get(n,0)+a*max(0,part)
        ranked = sorted(w.items(),key=lambda item:-item[1])[:4]
        total = sum(a for _,a in ranked)
        for j in range(i,i+7):
            for group_index in [g.group for g in coat.data.vertices[j].groups]:coat.vertex_groups[group_index].remove([j])
        for n,a in ranked:
            coat.vertex_groups[n].add(list(range(i,i+7)),a/total,'REPLACE')


def apply():
    report = {}
    for name in ('Bruno_Body', 'Bruno_Coat'):
        obj = bpy.data.objects[name]
        mesh = obj.data
        basis = mesh.attributes.get(BASIS)
        if basis is None:
            points = np.empty(len(mesh.vertices)*3, dtype=np.float32)
            mesh.vertices.foreach_get('co', points)
            basis = mesh.attributes.new(BASIS, 'FLOAT_VECTOR', 'POINT')
            basis.data.foreach_set('vector', points)
        points = np.empty(len(mesh.vertices)*3, dtype=np.float32)
        basis.data.foreach_get('vector', points)
        points = points.reshape(-1, 3)
        result = sculpt(points)
        mesh.vertices.foreach_set('co', result.ravel())
        mesh.update()
        obj['natural_revision'] = REVISION
        report[name] = {'before': np.ptp(points, axis=0).tolist(),
                        'after': np.ptp(result, axis=0).tolist(),
                        'unchanged_sole': bool(np.array_equal(points[points[:,2] <= .28], result[points[:,2] <= .28]))}
    groom()
    soften_skin()
    for name in ('Bruno_Body','Bruno_Coat'):
        mesh = bpy.data.objects[name].data
        assert max(len(v.groups) for v in mesh.vertices) <= 4
        error = max(abs(1-sum(g.weight for g in v.groups)) for v in mesh.vertices)
        assert error < 1e-5, (name, error)
        report[name]['max_weight_error'] = error
    print('NATURAL_SCULPT', report)
    return report


if __name__ == '__main__':
    apply()
