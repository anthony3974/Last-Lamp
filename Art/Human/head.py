"""Eyes, eyelashes and hair for the head."""
import bmesh
import bpy
import numpy as np

import garment_lib as G
from garment_lib import smoothstep


def fit_sphere(points):
    points = np.asarray(points, dtype=float)
    a = np.column_stack([2.0 * points, np.ones(len(points))])
    b = (points ** 2).sum(axis=1)
    solution = np.linalg.lstsq(a, b, rcond=None)[0]
    centre = solution[:3]
    return centre, float(np.sqrt(solution[3] + centre @ centre))


def build_eye(source, group, name):
    co = G.get_co(source.data)
    chosen = G.group_weights(source, [group]) > 0.5
    centre, radius = fit_sphere(co[chosen])
    bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=48, radius=radius, location=tuple(centre))
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name
    # The material reads this to place the iris and pupil.
    obj["eye_radius"] = radius
    G.link(obj)
    for collection in list(obj.users_collection):
        if collection.name != "Human":
            collection.objects.unlink(obj)
    G.shade_smooth(obj)
    return obj


# How far above the top of the coat collar the hair stops at the back of the neck, in metres.
NAPE_ABOVE_COLLAR = 0.014


def hair_region(fit, co, keep_off_ears=True):
    """True for vertices of the head that carry hair."""
    ears = fit.ears
    ear_front = ears[:, 1].min()
    ear_back = ears[:, 1].max()
    ear_top = ears[:, 2].max()
    ear_bottom = ears[:, 2].min()
    ear_mid = (ear_top + ear_bottom) / 2

    x, y, z = co[:, 0], co[:, 1], co[:, 2]
    head_top = z.max()

    front_limit = np.interp(
        z,
        [ear_mid - 0.012, ear_mid, ear_mid + 0.012, ear_top, ear_top + 0.03, head_top],
        [0.2, ear_front + 0.014, ear_front - 0.004, ear_front - 0.012, ear_front - 0.03, ear_front - 0.045],
    )
    # At the back the hair runs down to just above the collar, lowest in the middle and rising to the ears.
    behind_ear = y > ear_back - 0.004
    nape = fit.chin + 0.052 + NAPE_ABOVE_COLLAR
    beside_ear = ear_bottom + 0.008
    outward = np.clip(np.abs(x) / 0.072, 0.0, 1.0) ** 2
    back_edge = nape + (beside_ear - nape) * outward
    bottom = np.where(behind_ear, back_edge, ear_mid - 0.012)
    # The front limit shapes the sideburn; it must not cut the hair running down behind the ears.
    region = ((y >= front_limit) | behind_ear) & (z >= bottom)

    if keep_off_ears:
        for sign in (1, -1):
            ear = ears[ears[:, 0] * sign > 0]
            middle = (ear.max(axis=0) + ear.min(axis=0)) / 2
            reach = (ear.max(axis=0) - ear.min(axis=0)) / 2 + 0.01
            nearby = np.nonzero(np.all(np.abs(co - middle) < reach, axis=1))[0]
            if len(nearby):
                closest = np.min(np.linalg.norm(co[nearby, None, :] - ear[None, :, :], axis=2), axis=1)
                region[nearby[closest <= 0.0055]] = False
    return region


def drop_small_islands(obj, keep_fraction=0.05):
    """Removes stray scraps of a mesh that are not joined to its main part."""
    edges = G.get_edges(obj.data)
    labels = np.arange(len(obj.data.vertices))
    while True:
        lowest = np.minimum(labels[edges[:, 0]], labels[edges[:, 1]])
        updated = labels.copy()
        np.minimum.at(updated, edges[:, 0], lowest)
        np.minimum.at(updated, edges[:, 1], lowest)
        if (updated == labels).all():
            break
        labels = updated
    sizes = np.bincount(labels, minlength=len(labels))
    G.delete_vertices(obj, sizes[labels] < keep_fraction * len(labels))


def build_hair(fit, skin):
    obj = G.copy_body(skin, "Hair")
    obj.modifiers.clear()
    G.apply_subsurf(obj, 2)
    mesh = obj.data
    region = hair_region(fit, G.get_co(mesh))

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    doomed = [f for f in bm.faces if not all(region[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    drop_small_islands(obj)

    edges = G.get_edges(mesh)
    count = len(mesh.vertices)
    boundary = G.boundary_mask(mesh)
    co = G.get_co(mesh)

    # The cut follows mesh edges and comes out stepped, so run a smoothing pass along the hairline.
    rim = edges[boundary[edges[:, 0]] & boundary[edges[:, 1]]]
    neighbours = np.bincount(rim.ravel(), minlength=count)
    on_rim = neighbours > 0
    for _ in range(18):
        total = np.stack([
            np.bincount(rim[:, 0], weights=co[rim[:, 1], axis], minlength=count)
            + np.bincount(rim[:, 1], weights=co[rim[:, 0], axis], minlength=count)
            for axis in range(3)
        ], axis=1)
        co[on_rim] = co[on_rim] * 0.5 + total[on_rim] / neighbours[on_rim][:, None] * 0.5
    G.set_co(mesh, co)

    depth = np.full(count, np.inf)
    depth[boundary] = 0.0
    lengths = np.linalg.norm(co[edges[:, 0]] - co[edges[:, 1]], axis=1)
    for _ in range(120):
        np.minimum.at(depth, edges[:, 0], depth[edges[:, 1]] + lengths)
        np.minimum.at(depth, edges[:, 1], depth[edges[:, 0]] + lengths)

    G.set_co(mesh, co + G.get_normals(mesh) * (0.0055 * smoothstep(0.0, 0.016, depth) + 0.0016)[:, None])

    co = G.get_co(mesh)
    normals = G.get_normals(mesh)
    rng = np.random.default_rng(3)
    clumps = np.zeros(len(co))
    for scale, amp in ((70.0, 0.0011), (140.0, 0.0006)):
        direction = rng.normal(size=(5, 3))
        direction /= np.linalg.norm(direction, axis=1, keepdims=True)
        for d in direction:
            clumps += amp / 2.0 * np.sin((co @ d) * scale + rng.uniform(0, 6.28))
    G.set_co(mesh, co + normals * clumps[:, None])
    return obj
