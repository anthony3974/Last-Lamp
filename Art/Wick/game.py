"""Builds the game version of the Wick from wick.blend.

Run:  blender -b --python Art/Wick/game.py
      blender -b --python Art/Wick/game.py -- --scale 0.25 --out /some/folder     (quick test outside Assets)

Each part is reduced to its share of the triangle budget in PARTS, unwrapped into one skin atlas, and baked from its own
full-detail part. The rig uses Mixamo's bone names, so Unity maps it as Humanoid without help. The seam and spine glands
stay a separate mesh with the Monster_Alert material, because Enemy colours that whole renderer. Saves game.blend, and
writes Wick.fbx and the textures to Assets/Art/Wick.
"""
import argparse
import heapq
import os
import sys
import time

sys.dont_write_bytecode = True

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector, kdtree  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
HUMAN = os.path.normpath(os.path.join(HERE, "..", "Human"))
if HUMAN not in sys.path:
    sys.path.insert(0, HUMAN)

import render_util as R  # noqa: E402

OUTPUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Art", "Wick"))
ATLAS_NAME, ATLAS_SIZE = "Skin", 4096

# Part, triangles, texture density relative to the body, ray start distance in metres.
PARTS = [
    ("Wick", 21500, 1.0, 0.012),
    ("Claws", 1100, 1.0, 0.004),
    ("Teeth", 1100, 1.6, 0.003),
    ("Eyes", 300, 2.0, 0.002),
]
ALERT_PART, ALERT_TRIANGLES = "Alert_Organ", 900
# wick.py places the spine glands before it raises the spine knuckles, which bury them about 1 cm under the skin.
# The game version lifts each gland back out so its centre is this many radii above the skin, and scales it up so it
# reads from behind at a distance. Pieces wider than GLAND_MAX_RADIUS (the chest seam) are left as they are.
GLAND_SCALE = 1.5
GLAND_RAISE = 0.3
GLAND_MAX_RADIUS = 0.03
# Unity's material for the seam already exists in Assets/Materials, so the FBX only carries its name.
ALERT_MATERIAL = "Monster_Alert"
# These sit inside the head and follow it rigidly. There is no jaw bone, since no Mixamo clip would move it.
HEAD_ONLY = ("Teeth", "Eyes")

MARGIN_PIXELS = 8
AO_DISTANCE = 0.25
AO_IN_ALBEDO = 0.6
SAMPLES = 16
AO_SAMPLES = 64
HOLE_FILL_PASSES = 48
STAGE_OBJECTS = ("Plane", "Cylinder", "c", "key", "rim", "lamp", "flash")
PREFIX = "mixamorig:"
HEAT_SCALE = 10.0

# Body UVs: a part smaller than this joins the part around it, a tip nearer its opening than this (in weighted metres)
# isn't cut open, and each foot is cut out to its three toes.
MIN_ISLAND_FACES = 40
TIP_MIN_DISTANCE = 0.05
SEAM_TOES = 3


def log(*values):
    print("[wick]", *values, flush=True)


def activate(*objects):
    for other in bpy.context.selected_objects:
        other.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[-1]


def triangle_count(mesh):
    return sum(len(polygon.vertices) - 2 for polygon in mesh.polygons)


# ---------- skeleton ----------
# Joint positions follow the tube centres in wick.py (faces -Y, feet at z=0, the Wick's left side is +X).

def finger_joints(sign):
    """Index to pinky, each four points from knuckle to tip, as wick.py lays them out."""
    wrist = np.array([sign * .435, -.32, .68])
    fingers = []
    for i, length in enumerate([.78, .9, .85, .7]):
        dx = (i - 1.5) * .027 * sign
        root = wrist + np.array([dx, -.055, -.135])
        fingers.append([
            root,
            root + np.array([dx * .5, -.03, -.13 * length]),
            root + np.array([dx * .8, -.03, -.27 * length]),
            root + np.array([dx * .9, .03, -.38 * length]),
        ])
    return wrist, fingers


def thumb_joints(sign, wrist):
    knuckle = wrist + np.array([-sign * .038, -.02, -.05])
    return [
        wrist + np.array([-sign * .012, -.01, -.02]),
        knuckle,
        knuckle + np.array([-sign * .03, -.045, -.07]),
        knuckle + np.array([-sign * .045, -.075, -.13]),
    ]


def skeleton():
    """(name, head, tail, parent) for every bone, parents before children."""
    bones = [
        ("Hips", (0, .035, .99), (0, .05, 1.08), None),
        ("Spine", (0, .05, 1.08), (0, .055, 1.27), "Hips"),
        ("Spine1", (0, .055, 1.27), (0, .0, 1.50), "Spine"),
        ("Spine2", (0, .0, 1.50), (0, -.15, 1.81), "Spine1"),
        ("Neck", (0, -.15, 1.81), (0, -.36, 1.85), "Spine2"),
        # Along the skull toward the face, so the whole bone is inside the head for the weights.
        ("Head", (0, -.36, 1.85), (0, -.58, 1.70), "Neck"),
    ]
    for side, sign in (("Left", 1), ("Right", -1)):
        def x(point):
            return (sign * point[0], point[1], point[2])

        bones += [
            (f"{side}Shoulder", x((.05, -.16, 1.74)), x((.27, -.13, 1.72)), "Spine2"),
            (f"{side}Arm", x((.27, -.13, 1.72)), x((.40, -.05, 1.21)), f"{side}Shoulder"),
            (f"{side}ForeArm", x((.40, -.05, 1.21)), x((.435, -.32, .68)), f"{side}Arm"),
        ]
        wrist, fingers = finger_joints(sign)
        bones.append((f"{side}Hand", tuple(wrist), tuple(wrist + np.array([0, -.05, -.125])), f"{side}ForeArm"))
        for finger, points in zip(("Index", "Middle", "Ring", "Pinky"), fingers):
            parent = f"{side}Hand"
            for k in range(3):
                name = f"{side}Hand{finger}{k + 1}"
                bones.append((name, tuple(points[k]), tuple(points[k + 1]), parent))
                parent = name
        thumb = thumb_joints(sign, wrist)
        parent = f"{side}Hand"
        for k in range(3):
            name = f"{side}HandThumb{k + 1}"
            bones.append((name, tuple(thumb[k]), tuple(thumb[k + 1]), parent))
            parent = name

        bones += [
            (f"{side}UpLeg", x((.10, .02, .97)), x((.165, -.14, .50)), "Hips"),
            (f"{side}Leg", x((.165, -.14, .50)), x((.165, .045, .10)), f"{side}UpLeg"),
            (f"{side}Foot", x((.165, .045, .10)), x((.17, -.15, .028)), f"{side}Leg"),
            (f"{side}ToeBase", x((.17, -.15, .028)), x((.17, -.32, .02)), f"{side}Foot"),
        ]
    return bones


def build_rig():
    data = bpy.data.armatures.new("Rig")
    rig = bpy.data.objects.new("Rig", data)
    bpy.context.scene.collection.objects.link(rig)
    activate(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = data.edit_bones
    for name, head, tail, parent in skeleton():
        bone = edit.new(PREFIX + name)
        bone.head, bone.tail = Vector(head), Vector(tail)
        bone.roll = 0.0
        if parent:
            bone.parent = edit[PREFIX + parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def bone_segments(rig):
    names = [bone.name for bone in rig.data.bones]
    starts = np.array([list(bone.head_local) for bone in rig.data.bones])
    ends = np.array([list(bone.tail_local) for bone in rig.data.bones])
    return names, starts, ends


def nearest_bone(points, starts, ends):
    along = ends - starts
    t = np.clip(((points[:, None] - starts) * along).sum(-1) / np.maximum((along * along).sum(-1), 1e-9), 0.0, 1.0)
    offsets = points[:, None] - (starts + t[..., None] * along)
    return (offsets * offsets).sum(-1).argmin(axis=1)


def vertex_positions(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def tidy_weights(obj):
    activate(obj)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)


def skin_body(body, rig):
    """Bone heat weights, with any vertex the heat solve missed given to its nearest bone.

    Blender's heat solve fails outright on a model this small, so it runs at HEAT_SCALE times the size.
    """
    for obj in (body, rig):
        obj.scale = (HEAT_SCALE,) * 3
    bpy.context.view_layer.update()
    activate(body, rig)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    activate(body)
    bpy.ops.object.parent_clear(type="CLEAR")
    body.modifiers.clear()
    for obj in (body, rig):
        obj.scale = (1.0,) * 3
    bpy.context.view_layer.update()
    names, starts, ends = bone_segments(rig)
    groups = body.vertex_groups
    missed = [vertex.index for vertex in body.data.vertices if sum(entry.weight for entry in vertex.groups) < 1e-4]
    if missed:
        co = vertex_positions(body)[missed]
        for index, bone in zip(missed, nearest_bone(co, starts, ends)):
            group = groups.get(names[bone]) or groups.new(name=names[bone])
            group.add([index], 1.0, "REPLACE")
    log("vertices without heat weights", len(missed), "of", len(body.data.vertices))
    tidy_weights(body)


def copy_weights(target, body):
    """Each vertex takes the weights of the nearest body vertex, so claws follow their finger or toe."""
    body_co = vertex_positions(body)
    tree = kdtree.KDTree(len(body_co))
    for index, point in enumerate(body_co):
        tree.insert(point, index)
    tree.balance()

    names = {group.index: group.name for group in body.vertex_groups}
    for name in names.values():
        if name not in target.vertex_groups:
            target.vertex_groups.new(name=name)
    for vertex in target.data.vertices:
        _, nearest, _ = tree.find(vertex.co)
        for entry in body.data.vertices[nearest].groups:
            target.vertex_groups[names[entry.group]].add([vertex.index], entry.weight, "REPLACE")
    tidy_weights(target)


def weight_to_head(target):
    group = target.vertex_groups.new(name=PREFIX + "Head")
    group.add(list(range(len(target.data.vertices))), 1.0, "REPLACE")


def raise_glands(organ, body):
    """Moves each spine gland out of the knuckle it sits in, onto the skin."""
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    mesh = bmesh.new()
    mesh.from_mesh(organ.data)
    seen, raised = set(), 0
    for first in mesh.verts:
        if first.index in seen:
            continue
        piece, stack = [], [first]
        seen.add(first.index)
        while stack:
            vertex = stack.pop()
            piece.append(vertex)
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other.index not in seen:
                    seen.add(other.index)
                    stack.append(other)

        centre = sum((vertex.co for vertex in piece), Vector()) / len(piece)
        radius = max((vertex.co - centre).length for vertex in piece)
        if radius > GLAND_MAX_RADIUS:
            continue
        skin, normal, _, _ = tree.find_nearest(centre)
        target = skin + normal * radius * GLAND_SCALE * GLAND_RAISE
        for vertex in piece:
            vertex.co = target + (vertex.co - centre) * GLAND_SCALE
        raised += 1
    mesh.to_mesh(organ.data)
    mesh.free()
    log("glands raised onto the skin", raised)


# ---------- reduce, unwrap, bake ----------

def make_low(high, target):
    low = high.copy()
    low.data = high.data.copy()
    low.name = high.name + "_low"
    bpy.context.scene.collection.objects.link(low)
    low.modifiers.clear()

    welded = bmesh.new()
    welded.from_mesh(low.data)
    bmesh.ops.remove_doubles(welded, verts=welded.verts, dist=0.00001)
    welded.to_mesh(low.data)
    welded.free()

    activate(low)
    modifier = low.modifiers.new("Reduce", "DECIMATE")
    modifier.ratio = min(1.0, target / triangle_count(low.data))
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    for name in list(low.data.color_attributes.keys()):
        low.data.color_attributes.remove(low.data.color_attributes[name])
    low.data.polygons.foreach_set("use_smooth", [True] * len(low.data.polygons))
    low.data.update()
    return low


def body_part(bone_name):
    """The UV island a bone's skin belongs to, how many tips to cut open, and the side its seams hide on."""
    name = bone_name[len(PREFIX):]
    if name in ("Hips", "Spine", "Spine1", "Spine2", "LeftShoulder", "RightShoulder"):
        return "Torso"
    for finger in ("Index", "Middle", "Ring", "Pinky", "Thumb"):
        if finger in name:
            return name.rstrip("0123456789")
    if name.endswith("ToeBase"):
        return name.replace("ToeBase", "Foot")
    return name


def part_rules(part):
    """(tips to cut open, direction the seams should face, so they stay out of sight)."""
    back, under = Vector((0, 1, 0)), Vector((0, 0, -1))
    if part == "Head":
        return 1, under
    if part == "Neck":
        return 0, under
    if part.endswith("Foot"):
        return SEAM_TOES, under
    if any(finger in part for finger in ("Index", "Middle", "Ring", "Pinky", "Thumb")):
        return 1, back
    return 0, back


def dominant_bones(low):
    names = {group.index: group.name for group in low.vertex_groups}
    result = []
    for vertex in low.data.vertices:
        best = max(vertex.groups, key=lambda entry: entry.weight, default=None)
        result.append(names[best.group] if best else PREFIX + "Hips")
    return result


def mark_body_seams(low):
    """Seams between body parts, between each part's openings, and out to its tips, so every part unrolls flat."""
    mesh = bmesh.new()
    mesh.from_mesh(low.data)
    mesh.faces.ensure_lookup_table()
    mesh.verts.ensure_lookup_table()
    bones = dominant_bones(low)

    part = {}
    for face in mesh.faces:
        votes = [body_part(bones[vertex.index]) for vertex in face.verts]
        part[face.index] = max(set(votes), key=votes.count)

    # Stray patches of one part inside another would each become a tiny island, so they join their surroundings.
    for _ in range(10):
        changed = False
        seen = set()
        for first in mesh.faces:
            if first.index in seen:
                continue
            label, piece, stack = part[first.index], [], [first]
            seen.add(first.index)
            while stack:
                face = stack.pop()
                piece.append(face)
                for edge in face.edges:
                    for other in edge.link_faces:
                        if other.index not in seen and part[other.index] == label:
                            seen.add(other.index)
                            stack.append(other)
            if len(piece) >= MIN_ISLAND_FACES:
                continue
            around = [part[other.index] for face in piece for edge in face.edges for other in edge.link_faces if part[other.index] != label]
            if around:
                new_label = max(set(around), key=around.count)
                for face in piece:
                    part[face.index] = new_label
                changed = True
        if not changed:
            break

    for edge in mesh.edges:
        labels = {part[face.index] for face in edge.link_faces}
        edge.seam = len(labels) > 1

    islands = 0
    for label in set(part.values()):
        faces = [face for face in mesh.faces if part[face.index] == label]
        tips, hide = part_rules(label)
        cut_open(faces, {face.index for face in faces}, tips, hide)
        islands += 1

    mesh.to_mesh(low.data)
    mesh.free()
    log("body parts", islands)


def cut_open(faces, inside, tips, hide):
    """Joins the openings of one part with seams, then cuts out to its farthest tips, preferring the hidden side."""

    def region_edges(vertex):
        for edge in vertex.link_edges:
            if any(face.index in inside for face in edge.link_faces):
                yield edge

    def cost(edge):
        normal = sum((face.normal for face in edge.link_faces if face.index in inside), Vector())
        if normal.length > 0:
            normal.normalize()
        return edge.calc_length() * (2.0 - normal.dot(hide))

    def search(sources):
        dist = {vertex.index: 0.0 for vertex in sources}
        back = {}
        queue = [(0.0, vertex.index, vertex) for vertex in sources]
        heapq.heapify(queue)
        while queue:
            d, index, vertex = heapq.heappop(queue)
            if d > dist.get(index, float("inf")):
                continue
            for edge in region_edges(vertex):
                other = edge.other_vert(vertex)
                nd = d + cost(edge)
                if nd < dist.get(other.index, float("inf")):
                    dist[other.index] = nd
                    back[other.index] = (vertex, edge)
                    heapq.heappush(queue, (nd, other.index, other))
        return dist, back

    def cut_back(vertex, back, sources):
        path = []
        while vertex.index not in sources:
            vertex, edge = back[vertex.index]
            edge.seam = True
            path.append(vertex)
        return path

    def is_open(edge):
        return edge.seam or sum(1 for face in edge.link_faces if face.index in inside) == 1

    opening = {edge.index: edge for face in faces for edge in face.edges if is_open(edge)}
    loops, seen = [], set()
    for start in opening.values():
        if start.index in seen:
            continue
        loop, stack = {}, [start]
        seen.add(start.index)
        while stack:
            edge = stack.pop()
            for vertex in edge.verts:
                loop[vertex.index] = vertex
                for other in vertex.link_edges:
                    if other.index in opening and other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
        loops.append(loop)

    if not loops:
        lowest = min((vertex for face in faces for vertex in face.verts), key=lambda vertex: vertex.co.z)
        loops = [{lowest.index: lowest}]

    joined = dict(loops[0])
    for loop in loops[1:]:
        dist, back = search(joined.values())
        reachable = [vertex for index, vertex in loop.items() if index in dist]
        if reachable:
            nearest = min(reachable, key=lambda vertex: dist[vertex.index])
            for vertex in cut_back(nearest, back, joined):
                joined[vertex.index] = vertex
        joined.update(loop)

    for _ in range(tips):
        dist, back = search(joined.values())
        far_index = max(dist, key=dist.get)
        if dist[far_index] < TIP_MIN_DISTANCE:
            break
        far = next(vertex for face in faces for vertex in face.verts if vertex.index == far_index)
        for vertex in cut_back(far, back, joined):
            joined[vertex.index] = vertex
        joined[far_index] = far


def unwrap(lows, density, body=None):
    for low in lows:
        while low.data.uv_layers:
            low.data.uv_layers.remove(low.data.uv_layers[0])
        low.data.uv_layers.new(name="UVMap")

    if body is not None:
        mark_body_seams(body)
        activate(body)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.unwrap(method="MINIMUM_STRETCH", fill_holes=True, margin=0.0)
        bpy.ops.object.mode_set(mode="OBJECT")

    small = [low for low in lows if low is not body]
    if small:
        activate(*small)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=1.13, island_margin=0.0, area_weight=0.0, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")

    activate(*lows)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")

    for low in lows:
        data = low.data.uv_layers.active.data
        uv = np.empty(len(data) * 2, dtype=np.float32)
        data.foreach_get("uv", uv)
        data.foreach_set("uv", uv * density[low.name])

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, scale=True, margin_method="SCALED", margin=0.006, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")


class Atlas:
    """The images for the skin atlas and the material the bake writes through."""

    PASSES = {"albedo": "sRGB", "normal": "Non-Color", "ao": "Non-Color", "roughness": "Non-Color", "metallic": "Non-Color"}
    START = {
        "albedo": (0, 0, 0, 0), "normal": (0.5, 0.5, 1, 1), "ao": (1, 1, 1, 1), "roughness": (0.5, 0.5, 0.5, 1), "metallic": (0, 0, 0, 1),
    }

    def __init__(self, name, size):
        self.name = name
        self.size = size
        self.images = {}
        for kind, space in self.PASSES.items():
            image = bpy.data.images.new(f"Wick_{name}_{kind}", size, size, alpha=kind == "albedo")
            image.colorspace_settings.name = space
            image.generated_color = self.START[kind]
            image.pixels[0]  # Forces the generated colour into the buffer before the first bake.
            self.images[kind] = image

        self.material = bpy.data.materials.new(f"Wick_{name}")
        self.material.use_nodes = True
        self.tree = self.material.node_tree
        self.node = self.tree.nodes.new("ShaderNodeTexImage")

    def target(self, kind):
        self.node.image = self.images[kind]
        self.tree.nodes.active = self.node

    def read(self, kind):
        pixels = np.empty(self.size * self.size * 4, dtype=np.float32)
        self.images[kind].pixels.foreach_get(pixels)
        return pixels.reshape(self.size, self.size, 4)


def bake(kind, low, high, reach, samples):
    activate(high, low)
    bpy.context.scene.cycles.samples = samples
    bpy.ops.object.bake(
        type=kind,
        use_selected_to_active=True,
        cage_extrusion=reach,
        max_ray_distance=reach * 2.5,
        margin=MARGIN_PIXELS,
        margin_type="EXTEND",
        use_clear=False,
        target="IMAGE_TEXTURES",
        normal_space="TANGENT",
        normal_r="POS_X",
        normal_g="POS_Y",
        normal_b="POS_Z",
    )


def principled(material):
    return next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")


def output_node(material):
    return next(node for node in material.node_tree.nodes if node.type == "OUTPUT_MATERIAL")


def show_input_as_emission(material, socket_name):
    """Sends one Principled input straight to the output, so an emission bake reads the exact value."""
    tree = material.node_tree
    socket = principled(material).inputs[socket_name]
    emission = tree.nodes.new("ShaderNodeEmission")
    if socket.is_linked:
        tree.links.new(socket.links[0].from_socket, emission.inputs["Color"])
    else:
        value = socket.default_value
        emission.inputs["Color"].default_value = tuple(value) if hasattr(value, "__len__") else (value, value, value, 1)
    tree.links.new(emission.outputs["Emission"], output_node(material).inputs["Surface"])


def save_png(name, pixels, colour):
    image = bpy.data.images.new(name, pixels.shape[1], pixels.shape[0], alpha=True)
    image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = os.path.join(OUTPUT, name + ".png")
    image.file_format = "PNG"
    image.alpha_mode = "STRAIGHT"
    image.save()
    bpy.data.images.remove(image)


def fill_holes(pixels):
    """Spreads each island's colour past its edge, so mip levels don't pull the black background into the seams."""
    colour = pixels[..., :3]
    known = pixels[..., 3] > 0.5
    log("albedo texels baked", round(float(known.mean()), 3))
    for _ in range(HOLE_FILL_PASSES):
        total = np.zeros_like(colour)
        count = np.zeros(known.shape, dtype=np.float32)
        for axis, shift in ((0, 1), (0, -1), (1, 1), (1, -1)):
            total += np.roll(colour * known[..., None], shift, axis)
            count += np.roll(known, shift, axis)
        grow = ~known & (count > 0)
        if not grow.any():
            break
        colour[grow] = total[grow] / count[grow][..., None]
        known |= grow
    return pixels


def finish_textures(atlas):
    albedo = fill_holes(atlas.read("albedo"))
    ao = atlas.read("ao")[..., 0]
    factor = 1.0 - AO_IN_ALBEDO * (1.0 - np.clip(ao, 0.0, 1.0))
    # The albedo buffer holds sRGB values, and the occlusion is a linear amount of light.
    albedo[..., :3] *= (factor ** (1.0 / 2.2))[..., None]
    albedo[..., 3] = 1.0
    save_png(f"Wick_{atlas.name}_Albedo", albedo, True)

    normal = atlas.read("normal")
    normal[..., 3] = 1.0
    save_png(f"Wick_{atlas.name}_Normal", normal, False)

    # URP's Metallic map: metallic in red, smoothness in alpha.
    metal = atlas.read("metallic")
    metal[..., 1] = metal[..., 2] = 0.0
    metal[..., 3] = 1.0 - atlas.read("roughness")[..., 0]
    save_png(f"Wick_{atlas.name}_MetallicSmoothness", metal, False)


def preview_material(atlas):
    """A plain Principled material from the finished textures, so game.blend can be rendered next to the design."""
    material = atlas.material
    tree = material.node_tree
    tree.nodes.remove(atlas.node)
    bsdf = principled(material)
    folder = os.path.relpath(OUTPUT, HERE)

    def image(suffix, space):
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(os.path.join(OUTPUT, f"Wick_{atlas.name}_{suffix}.png"))
        node.image.colorspace_settings.name = space
        node.image.filepath = "//" + os.path.join(folder, f"Wick_{atlas.name}_{suffix}.png")
        return node

    tree.links.new(image("Albedo", "sRGB").outputs["Color"], bsdf.inputs["Base Color"])
    metal = image("MetallicSmoothness", "Non-Color")
    metal.image.alpha_mode = "CHANNEL_PACKED"
    tree.links.new(metal.outputs["Color"], bsdf.inputs["Metallic"])
    invert = tree.nodes.new("ShaderNodeMath")
    invert.operation = "SUBTRACT"
    invert.inputs[0].default_value = 1.0
    tree.links.new(metal.outputs["Alpha"], invert.inputs[1])
    tree.links.new(invert.outputs[0], bsdf.inputs["Roughness"])
    normal_map = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(image("Normal", "Non-Color").outputs["Color"], normal_map.inputs["Color"])
    tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])


def alert_material():
    """Glows in Blender like the design, and carries Unity's material name into the FBX."""
    material = bpy.data.materials.new(ALERT_MATERIAL)
    material.use_nodes = True
    bsdf = principled(material)
    bsdf.inputs["Base Color"].default_value = (0, 0, 0, 1)
    bsdf.inputs["Emission Color"].default_value = (1, .48, .05, 1)
    bsdf.inputs["Emission Strength"].default_value = 2.5
    return material


def bind(obj, rig):
    obj.parent = rig
    obj.matrix_parent_inverse.identity()
    if not any(modifier.type == "ARMATURE" for modifier in obj.modifiers):
        modifier = obj.modifiers.new("Rig", "ARMATURE")
        modifier.object = rig


def export(body, alert, rig):
    for obj in (body, alert):
        bind(obj, rig)
    activate(body, alert, rig)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUTPUT, "Wick.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        use_mesh_modifiers=False,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        armature_nodetype="NULL",
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
    )


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--scale", type=float, default=1.0)
    parser.add_argument("--out", default=None)
    return parser.parse_args(argv)


def main():
    global OUTPUT
    args = parse_args()
    scale = args.scale
    if args.out:
        OUTPUT = os.path.abspath(args.out)
    started = time.time()
    os.makedirs(OUTPUT, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "wick.blend"))
    for name in STAGE_OBJECTS:
        if name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name])

    R.setup_cycles(samples=SAMPLES, width=64, height=64)
    scene = bpy.context.scene
    R.set_world((0, 0, 0), 0.0)
    scene.world.light_settings.distance = AO_DISTANCE
    scene.cycles.use_denoising = False

    rig = build_rig()

    highs, lows, density, reaches = {}, [], {}, {}
    for name, target, part_density, reach in PARTS:
        high = bpy.data.objects[name]
        low = make_low(high, target)
        highs[low.name] = high
        lows.append(low)
        density[low.name] = part_density
        reaches[low.name] = reach
    alert_high = bpy.data.objects[ALERT_PART]
    raise_glands(alert_high, bpy.data.objects[PARTS[0][0]])
    alert = make_low(alert_high, ALERT_TRIANGLES)
    log({low.name: triangle_count(low.data) for low in lows + [alert]})

    body = lows[0]
    skin_body(body, rig)
    for low in lows[1:] + [alert]:
        if any(low.name.startswith(name) for name in HEAD_ONLY):
            weight_to_head(low)
        else:
            copy_weights(low, body)

    unwrap(lows, density, body=body)
    unwrap([alert], {alert.name: 1.0})

    atlas = Atlas(ATLAS_NAME, max(64, int(ATLAS_SIZE * scale)))
    for low in lows:
        low.data.materials.clear()
        low.data.materials.append(atlas.material)
        # Copies must not block the occlusion rays that belong to the full-detail model.
        low.visible_shadow = low.visible_diffuse = low.visible_glossy = low.visible_transmission = low.visible_volume_scatter = False
    alert.data.materials.clear()
    alert.data.materials.append(alert_material())
    alert.visible_shadow = alert.visible_diffuse = alert.visible_glossy = alert.visible_transmission = alert.visible_volume_scatter = False

    high_materials = {slot.material for high in highs.values() for slot in high.material_slots if slot.material}

    def bake_all(kind, target, samples):
        begun = time.time()
        atlas.target(target)
        for low in lows:
            bake(kind, low, highs[low.name], reaches[low.name], samples)
        log("baked", target, round(time.time() - begun), "s")

    bake_all("NORMAL", "normal", SAMPLES)
    bake_all("AO", "ao", AO_SAMPLES)
    for socket_name, target in (("Base Color", "albedo"), ("Roughness", "roughness"), ("Metallic", "metallic")):
        for material in high_materials:
            show_input_as_emission(material, socket_name)
        bake_all("EMIT", target, SAMPLES)

    finish_textures(atlas)

    for high in list(highs.values()) + [alert_high]:
        bpy.data.objects.remove(high)
    for image in atlas.images.values():
        bpy.data.images.remove(image)
    preview_material(atlas)

    for low in lows + [alert]:
        low.visible_shadow = low.visible_diffuse = low.visible_glossy = low.visible_transmission = low.visible_volume_scatter = True

    activate(*lows)
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = body.data.name = "Wick"
    alert.name = alert.data.name = "Wick_Alert"
    export(body, alert, rig)
    log("exported triangles", triangle_count(body.data) + triangle_count(alert.data), "vertices", len(body.data.vertices) + len(alert.data.vertices))

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.outliner.orphans_purge(do_recursive=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUTPUT if args.out else HERE, "game.blend"))
    log("done in", round(time.time() - started), "s")


if __name__ == "__main__":
    main()
