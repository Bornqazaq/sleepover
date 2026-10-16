"""Paint the horse's face once, on the visible front surface of the sculpt.

The three-view sheet is not a seamless texture. Blending all its projections
printed another eye on the temple and another mouth on the neck. Keep the
front facial detail only where a front ray actually reaches that surface;
paint the cheeks/neck with continuous coat colour instead. Existing UVs,
geometry, skin weights, shape keys and animation are left intact.
"""
import struct
import zlib
from pathlib import Path

import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]


def smooth(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def repair(body, atlas_path, reference_surface_vertices):
    """Repaint only original head surface texels, plus their UV gutter."""
    atlas_image = bpy.data.images.load(str(atlas_path), check_existing=False)
    width, height = atlas_image.size
    pixels = np.empty(width * height * 4, np.float32)
    atlas_image.pixels.foreach_get(pixels)
    atlas = pixels.reshape(height, width, 4)[:, :, :3].copy()
    reference = bpy.data.images.load(str(ROOT / 'art-source/HorseRingmaster/Reference.png'), check_existing=True)
    rw, rh = reference.size
    pixels = np.empty(rw * rh * 4, np.float32)
    reference.pixels.foreach_get(pixels)
    photo = pixels.reshape(rh, rw, 4)[:, :, :3]
    mesh = body.data
    mesh.calc_loop_triangles()
    coords = np.array([v.co[:] for v in mesh.vertices])
    tree = BVHTree.FromPolygons(coords.tolist(), [p.vertices[:] for p in mesh.polygons])
    visibility = np.zeros(len(coords))
    for i, (x, y, z) in enumerate(coords[:reference_surface_vertices]):
        if 2.75 < z < 3.85 and y < .13:
            hit, _, _, _ = tree.ray_cast((x, -3, z), (0, 1, 0), 4)
            if hit is not None:
                visibility[i] = smooth((.065 - max(0, y - hit.y)) / .055)
    uv = np.empty(len(mesh.loops) * 2, np.float32)
    mesh.uv_layers['GameAtlas'].data.foreach_get('uv', uv)
    uv = uv.reshape(-1, 2) * (width, height)
    occupied = np.zeros((height, width), bool)
    changed = np.zeros_like(occupied)

    def front_colour(points):
        x = np.clip(395 + 160 * points[:, 0], 0, rw - 1.001)
        y = np.clip(rh - 1 - (691 - 162 * points[:, 2]), 0, rh - 1.001)
        ix, iy = x.astype(int), y.astype(int)
        fx, fy = (x - ix)[:, None], (y - iy)[:, None]
        return ((photo[iy, ix] * (1-fx) + photo[iy, ix+1] * fx) * (1-fy) +
                (photo[iy+1, ix] * (1-fx) + photo[iy+1, ix+1] * fx) * fy)

    def head_colour(points, front_visible, previous):
        x, y, z = points.T
        ax = np.abs(x)
        grain = (.004 * np.sin(x * 161 + z * 83) * np.sin(y * 149 - z * 57) +
                 .008 * np.sin(x * 13 + z * 9) * np.cos(y * 17 - z * 7))
        skin = np.tile((.56, .395, .285), (len(points), 1)) + grain[:, None]
        muzzle = smooth((-.53-y)/.16) * (1-smooth((z-3.27)/.11))
        blaze_width = .07 + .14 * (1-smooth((z-3.18)/.48))
        blaze = (1-smooth((ax-blaze_width)/.065)) * smooth((-.30-y)/.25)
        pale = np.maximum(muzzle, blaze)
        cream = np.tile((.74, .675, .57), (len(points), 1)) + grain[:, None]
        coat = skin * (1-pale[:, None]) + cream * pale[:, None]
        # Photo features have anatomical bounds as well as visibility. A front
        # ray can also reach the neck beside the snout; that is not another lip.
        eye = (1-smooth((np.abs(ax-.153)-.075)/.055)) * (1-smooth((np.abs(z-3.55)-.07)/.065))
        brow = (1-smooth((np.abs(ax-.15)-.08)/.065)) * (1-smooth((np.abs(z-3.67)-.025)/.055))
        feature = np.maximum(eye, brow) * smooth((-.42-y)/.065)
        front = front_visible * feature
        colour = coat * (1-front[:, None]) + front_colour(points) * front[:, None]
        # The existing snout paint already follows the actual nose and teeth.
        # Keep its pores and dark nostril interiors, including occluded surfaces.
        snout = smooth((-.57-y)/.14) * (1-smooth((z-3.38)/.13))
        colour = colour * (1-snout[:, None]) + previous * snout[:, None]
        hair_boundary = -.04 - .12*smooth((ax-.26)/.08)
        mane = smooth((y-hair_boundary)/.07) * smooth((z-2.85)/.12)
        hair = np.tile((.285, .185, .125), (len(points), 1)) + grain[:, None]
        colour = colour * (1-mane[:, None]) + hair * mane[:, None]
        mask = smooth((z-2.80)/.10) * (1-smooth((z-3.72)/.09))
        forelock = (z > 3.68) & (y < -.40)
        mask[forelock] = 0
        return previous * (1-mask[:, None]) + colour * mask[:, None], mask

    for tri in mesh.loop_triangles:
        ids = list(tri.loops)
        vertex_ids = list(tri.vertices)
        a, b, c = uv[ids]
        det = (b[1]-c[1])*(a[0]-c[0]) + (c[0]-b[0])*(a[1]-c[1])
        if abs(det) < 1e-6:
            continue
        lo = np.maximum(0, np.floor(uv[ids].min(axis=0)).astype(int))
        hi = np.minimum((width-1, height-1), np.ceil(uv[ids].max(axis=0)).astype(int))
        xx, yy = np.meshgrid(np.arange(lo[0], hi[0]+1), np.arange(lo[1], hi[1]+1))
        px, py = xx.ravel(), yy.ravel()
        x, y = px+.5, py+.5
        wa = ((b[1]-c[1])*(x-c[0]) + (c[0]-b[0])*(y-c[1]))/det
        wb = ((c[1]-a[1])*(x-c[0]) + (a[0]-c[0])*(y-c[1]))/det
        wc = 1-wa-wb
        keep = (wa >= -.0001) & (wb >= -.0001) & (wc >= -.0001)
        px, py = px[keep], py[keep]
        occupied[py, px] = True
        points = coords[vertex_ids]
        if (max(vertex_ids) >= reference_surface_vertices or
                points[:, 2].max() < 2.80 or points[:, 2].min() > 3.81):
            continue
        bary = np.column_stack((wa[keep], wb[keep], wc[keep]))
        colour, mask = head_colour(bary @ points, bary @ visibility[vertex_ids], atlas[py, px])
        atlas[py, px] = colour
        changed[py[mask > 0], px[mask > 0]] = True
    surface_texels = int(changed.sum())
    # Extend corrected edge colours into unoccupied UV gutters, not neighbouring islands.
    for _ in range(10):
        previous = changed.copy()
        for dy, dx in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
            valid = np.roll(previous, (dy, dx), (0, 1)) & ~changed & ~occupied
            if dy == 1: valid[0] = False
            if dy == -1: valid[-1] = False
            if dx == 1: valid[:, 0] = False
            if dx == -1: valid[:, -1] = False
            atlas[valid] = np.roll(atlas, (dy, dx), (0, 1))[valid]
            changed[valid] = True
    def chunk(tag, data):
        return struct.pack('!I', len(data)) + tag + data + struct.pack('!I', zlib.crc32(tag+data) & 0xffffffff)
    rgb = np.clip(atlas*255+.5, 0, 255).astype(np.uint8)
    raw = b''.join(b'\0' + row.tobytes() for row in rgb[::-1])
    Path(atlas_path).write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', width, height, 8, 2, 0, 0, 0)) +
                                chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))
    bpy.data.images.remove(atlas_image)
    print('Face paint repaired:', surface_texels, 'surface texels; original surface vertices:', reference_surface_vertices)
    return {'surface_texels': surface_texels, 'reference_surface_vertices': reference_surface_vertices}
