"""Mesh tools for tailoring clothes onto a body: grids, panels, ribbons, tubes and surface projection."""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

PREFIX = "mixamorig:"
FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")


def smoothstep(edge0, edge1, x):
    t = np.clip((np.asarray(x, dtype=float) - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def get_co(mesh):
    values = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
    mesh.vertices.foreach_get("co", values)
    return values.reshape(-1, 3).astype(np.float64)


def set_co(mesh, co):
    mesh.vertices.foreach_set("co", np.asarray(co, dtype=np.float32).ravel())
    mesh.update()


def get_normals(mesh):
    values = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
    mesh.vertex_normals.foreach_get("vector", values)
    return values.reshape(-1, 3).astype(np.float64)


def get_edges(mesh):
    values = np.empty(len(mesh.edges) * 2, dtype=np.int32)
    mesh.edges.foreach_get("vertices", values)
    return values.reshape(-1, 2)


def link(obj):
    collection = bpy.data.collections.get("Human")
    if collection is None:
        collection = bpy.data.collections.new("Human")
        bpy.context.scene.collection.children.link(collection)
    if obj.name not in collection.objects:
        collection.objects.link(obj)
    return obj


def shade_smooth(obj):
    obj.data.polygons.foreach_set("use_smooth", np.ones(len(obj.data.polygons), dtype=bool))
    obj.data.update()


def mesh_object(name, verts, faces):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    link(obj)
    shade_smooth(obj)
    return obj


def grid_faces(rows, cols, close_columns=False):
    spans = cols if close_columns else cols - 1
    faces = []
    for r in range(rows - 1):
        for c in range(spans):
            next_c = (c + 1) % cols
            faces.append((r * cols + c, r * cols + next_c, (r + 1) * cols + next_c, (r + 1) * cols + c))
    return faces


def grid_object(name, points, close_columns=False):
    """Makes a quad mesh from an array of shape (rows, columns, 3)."""
    rows, cols = points.shape[:2]
    return mesh_object(name, points.reshape(-1, 3), grid_faces(rows, cols, close_columns))


def join(name, objects):
    """Merges several mesh objects into one and returns it."""
    objects = [o for o in objects if o is not None]
    bm = bmesh.new()
    for obj in objects:
        bm.from_mesh(obj.data)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    result = bpy.data.objects.new(name, mesh)
    link(result)
    shade_smooth(result)
    for obj in objects:
        bpy.data.objects.remove(obj)
    return result


def apply_subsurf(obj, levels):
    bpy.context.view_layer.objects.active = obj
    modifier = obj.modifiers.new("Subdivide", "SUBSURF")
    modifier.levels = levels
    modifier.render_levels = levels
    modifier.boundary_smooth = "PRESERVE_CORNERS"
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def taubin(co, edges, locked, iterations, lam=0.5, mu=-0.53):
    """Smooths away muscle and bone detail without shrinking the shape."""
    count = len(co)
    degree = np.bincount(edges.ravel(), minlength=count).astype(float)
    degree[degree == 0] = 1.0

    def step(points, factor):
        total = np.zeros_like(points)
        for axis in range(3):
            total[:, axis] = (
                np.bincount(edges[:, 0], weights=points[edges[:, 1], axis], minlength=count)
                + np.bincount(edges[:, 1], weights=points[edges[:, 0], axis], minlength=count)
            )
        delta = total / degree[:, None] - points
        delta[locked] = 0.0
        return points + factor * delta

    for _ in range(iterations):
        co = step(step(co, lam), mu)
    return co


def copy_body(source, name):
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = name
    obj.data.name = name
    obj.modifiers.clear()
    link(obj)
    return obj


def group_weights(obj, names):
    """Summed weight of the named vertex groups, one value per vertex."""
    wanted = {obj.vertex_groups[n].index for n in names if n in obj.vertex_groups}
    result = np.zeros(len(obj.data.vertices))
    for vertex in obj.data.vertices:
        total = 0.0
        for element in vertex.groups:
            if element.group in wanted:
                total += element.weight
        result[vertex.index] = total
    return result


def strip_helpers(obj):
    """Keeps only the visible body surface, dropping MPFB's helper geometry."""
    keep = group_weights(obj, ["body"]) > 0.5
    delete_vertices(obj, ~keep)


def delete_vertices(obj, mask):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if mask[v.index]], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def cut(bm, point, normal, keep_filter):
    """Removes geometry on the normal side of a plane, only among vertices accepted by keep_filter."""
    verts = [v for v in bm.verts if keep_filter(v.co)]
    chosen = set(verts)
    edges = [e for e in bm.edges if e.verts[0] in chosen or e.verts[1] in chosen]
    faces = [f for f in bm.faces if any(v in chosen for v in f.verts)]
    bmesh.ops.bisect_plane(
        bm, geom=verts + edges + faces, plane_co=point, plane_no=normal, clear_outer=True, clear_inner=False
    )


def boundary_mask(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    mask = np.array([v.is_boundary for v in bm.verts])
    bm.free()
    return mask


class Surface:
    """Finds points on the outside of a set of meshes, for fitting panels and trims."""

    def __init__(self, objects, axis_y=-0.01):
        verts, polys = [], []
        offset = 0
        for obj in objects:
            matrix = obj.matrix_world
            verts.extend(matrix @ v.co for v in obj.data.vertices)
            polys.extend([i + offset for i in p.vertices] for p in obj.data.polygons)
            offset += len(obj.data.vertices)
        self.bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False)
        self.axis_y = axis_y

    def _ray(self, origin, direction, fallback_normal):
        location, normal, _, _ = self.bvh.ray_cast(origin, direction)
        if location is None:
            location, normal, _, _ = self.bvh.find_nearest(origin + direction * 1.5)
            normal = Vector(fallback_normal)
        return np.array(location), np.array(normal)

    def front(self, x, z):
        return self._ray(Vector((x, -1.5, z)), Vector((0, 1, 0)), (0, -1, 0))

    def back(self, x, z):
        return self._ray(Vector((x, 1.5, z)), Vector((0, -1, 0)), (0, 1, 0))

    def around(self, theta, z):
        """Theta is measured from the front (-Y) toward the character's left (+X)."""
        outward = Vector((math.sin(theta), -math.cos(theta), 0))
        origin = Vector((0, self.axis_y, z)) + outward * 0.36
        return self._ray(origin, -outward, outward)

    def ray(self, origin, direction):
        origin, direction = Vector(origin), Vector(direction).normalized()
        return self._ray(origin, direction, -direction)

    def nearest(self, point):
        location, normal, _, _ = self.bvh.find_nearest(Vector(point))
        return np.array(location), np.array(normal)


def blur_grid(values, passes=2):
    """Softens a (rows, columns, ...) grid, leaving its size alone."""
    for _ in range(passes):
        padded = np.pad(values, [(1, 1), (1, 1)] + [(0, 0)] * (values.ndim - 2), mode="edge")
        values = (
            padded[1:-1, 1:-1] * 4
            + padded[:-2, 1:-1]
            + padded[2:, 1:-1]
            + padded[1:-1, :-2]
            + padded[1:-1, 2:]
        ) / 8.0
    return values


def edge_spacing(width, edge):
    """Offsets from one side of a span, tightened near the edge so the rim can be crisp."""
    scale = min(1.0, width / (2.0 * edge[-1] * 1.15))
    return np.array(edge) * scale


def panel(name, left, right, project, height, rows=36, cols=16, edge=(0.0, 0.0015, 0.0035, 0.0065, 0.0095, 0.013)):
    """Builds a layer of cloth bounded by two chains running up in b.

    left and right are lists of (a, b) with b increasing. project(a, b) returns a surface point and
    normal. height(distance_from_edge, a, b) says how far above the surface each point sits.
    """
    left = np.array(left, dtype=float)
    right = np.array(right, dtype=float)
    low = max(left[0, 1], right[0, 1])
    high = min(left[-1, 1], right[-1, 1])

    rim = np.array(edge)
    inner = np.linspace(low + rim[-1], high - rim[-1], rows)
    b_values = np.concatenate([low + rim[:-1], inner, high - rim[:-1][::-1]])

    edge_count = len(edge) - 1
    grid = np.empty((len(b_values), edge_count * 2 + cols, 3))
    normals = np.empty_like(grid)
    distances = np.empty(grid.shape[:2])
    for r, b in enumerate(b_values):
        a_left = np.interp(b, left[:, 1], left[:, 0])
        a_right = np.interp(b, right[:, 1], right[:, 0])
        width = max(a_right - a_left, 1e-4)
        spacing = edge_spacing(width, edge)
        middle = np.linspace(a_left + spacing[-1], a_right - spacing[-1], cols)
        a_values = np.concatenate([a_left + spacing[:-1], middle, a_right - spacing[:-1][::-1]])
        for c, a in enumerate(a_values):
            point, normal = project(a, b)
            grid[r, c] = point
            normals[r, c] = normal
            distances[r, c] = min(a - a_left, a_right - a, b - low, high - b)

    normals = blur_grid(normals, 3)
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    for r, b in enumerate(b_values):
        for c in range(grid.shape[1]):
            grid[r, c] += normals[r, c] * height(distances[r, c], grid[r, c][0], b)
    return grid_object(name, grid)


def rim_height(top, rim=0.0035, stitch=None, groove=0.0006, width=0.0022):
    """Height profile: rises quickly at the edge to `top`, with an optional sewn groove."""

    def height(distance, _a, _b):
        value = top * float(smoothstep(0.0, rim, distance))
        if stitch is not None:
            value -= groove * math.exp(-(((distance - stitch) / width) ** 2))
        return value

    return height


def tube(name, path, radius, closed=False, segments=12):
    """Sweeps a circle along a path of 3D points."""
    path = np.array(path, dtype=float)
    count = len(path)
    if closed:
        tangents = path[(np.arange(count) + 1) % count] - path[(np.arange(count) - 1) % count]
    else:
        tangents = np.gradient(path, axis=0)
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)
    reference = np.array([0.0, 0.0, 1.0]) if abs(tangents[0, 2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    side = np.cross(tangents, reference)
    lengths = np.linalg.norm(side, axis=1, keepdims=True)
    if (lengths < 1e-6).any():
        # The path runs along the reference somewhere, so carry the first ring's direction along the path instead.
        side[0] /= lengths[0]
        for i in range(1, count):
            side[i] = side[i - 1] - tangents[i] * np.dot(side[i - 1], tangents[i])
            side[i] /= np.linalg.norm(side[i])
    else:
        side /= lengths
    up = np.cross(side, tangents)
    angles = np.linspace(0, 2 * math.pi, segments, endpoint=False)
    points = np.empty((count, segments, 3))
    radii = np.broadcast_to(np.asarray(radius, dtype=float), (count,))
    for i in range(count):
        for j, angle in enumerate(angles):
            points[i, j] = path[i] + (side[i] * math.cos(angle) + up[i] * math.sin(angle)) * radii[i]
    rows, cols = points.shape[:2]
    faces = grid_faces(rows, cols, close_columns=True)
    if closed:
        for c in range(cols):
            next_c = (c + 1) % cols
            faces.append(((rows - 1) * cols + c, (rows - 1) * cols + next_c, next_c, c))
    return mesh_object(name, points.reshape(-1, 3), faces)


def ribbon(name, path, normals, width, thickness, round_end=True, close=False):
    """A strap following a path. normals give the outward direction at each point of the path."""
    path = np.array(path, dtype=float)
    normals = np.array(normals, dtype=float)
    normals /= np.linalg.norm(normals, axis=1, keepdims=True)
    count = len(path)
    if close:
        tangents = path[(np.arange(count) + 1) % count] - path[(np.arange(count) - 1) % count]
    else:
        tangents = np.gradient(path, axis=0)
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)
    lateral = np.cross(normals, tangents)
    lateral /= np.linalg.norm(lateral, axis=1, keepdims=True)

    bevel = min(0.0025, width * 0.15)
    across = np.array([
        -0.5, -0.5 + bevel / width, -0.5 + 2.2 * bevel / width, -0.25, 0.0, 0.25,
        0.5 - 2.2 * bevel / width, 0.5 - bevel / width, 0.5,
    ])
    lift = np.array([0.0, 0.75, 1.0, 1.0, 1.0, 1.0, 1.0, 0.75, 0.0]) * thickness

    widths = np.full(count, width)
    if round_end and not close:
        distance = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))])
        remaining = distance[-1] - distance
        cap = width * 0.5
        taper = np.clip(remaining / cap, 0.0, 1.0)
        widths = width * np.sqrt(np.maximum(1.0 - (1.0 - taper) ** 2, 0.0))
        widths = np.maximum(widths, width * 0.02)

    points = np.empty((count, len(across), 3))
    for i in range(count):
        for j, (a, l) in enumerate(zip(across, lift)):
            points[i, j] = path[i] + lateral[i] * a * widths[i] + normals[i] * l
    obj = grid_object(name, points)
    if close:
        rows, cols = points.shape[:2]
        mesh = obj.data
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        for c in range(cols - 1):
            a = bm.verts[(rows - 1) * cols + c]
            b = bm.verts[(rows - 1) * cols + c + 1]
            d = bm.verts[c + 1]
            e = bm.verts[c]
            bm.faces.new((a, b, d, e))
        bm.to_mesh(mesh)
        bm.free()
        shade_smooth(obj)
    return obj


def lathe_disc(center, normal, profile, segments=24):
    """Returns vertices and faces for a round shape: profile is (radius, height) from rim to middle."""
    normal = Vector(normal).normalized()
    side = normal.cross(Vector((0, 0, 1)))
    if side.length < 1e-4:
        side = normal.cross(Vector((1, 0, 0)))
    side.normalize()
    up = normal.cross(side)
    verts = []
    for radius, height in profile:
        for j in range(segments):
            angle = 2 * math.pi * j / segments
            verts.append(Vector(center) + (side * math.cos(angle) + up * math.sin(angle)) * radius + normal * height)
    faces = grid_faces(len(profile), segments, close_columns=True)
    return verts, faces

