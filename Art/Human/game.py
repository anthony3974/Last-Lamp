"""Builds the game version of the character from human.blend.

Run:  blender -b --python Art/Human/game.py
      blender -b --python Art/Human/game.py -- --scale 0.25     (quick low-resolution test)

Each part is reduced to its share of the triangle budget in PARTS, unwrapped into one of four atlases, and baked from
its own high-resolution part. That keeps a lapel from picking up the chest under it. Saves game.blend, and writes
Human.fbx and the textures to Assets/Art/Human.
"""
import argparse
import os
import sys
import time

sys.dont_write_bytecode = True

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import render_util as R  # noqa: E402

OUTPUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Art", "Human"))
ATLASES = {"Coat": 4096, "Clothes": 2048, "Head": 2048, "Hair": 1024}

# Part, atlas, triangles, texture density relative to the rest of its atlas, ray start distance in metres.
PARTS = [
    ("Coat_Skirt", "Coat", 5200, 1.0, 0.03),
    ("Coat_Upper", "Coat", 4200, 1.0, 0.03),
    ("Coat_Yoke", "Coat", 500, 1.0, 0.012),
    ("Coat_Belt", "Coat", 300, 1.0, 0.012),
    ("Coat_BeltTail", "Coat", 150, 1.0, 0.012),
    ("Coat_BeltLoops", "Coat", 120, 1.0, 0.006),
    ("Coat_LapelL", "Coat", 450, 1.0, 0.012),
    ("Coat_LapelR", "Coat", 450, 1.0, 0.012),
    ("Coat_Collar", "Coat", 450, 1.0, 0.012),
    ("Coat_StormFlap", "Coat", 300, 1.0, 0.012),
    ("Coat_Over", "Coat", 350, 1.0, 0.012),
    ("Coat_Buttons", "Coat", 640, 1.0, 0.004),
    ("Coat_Buckle", "Coat", 300, 1.0, 0.004),
    ("Coat_CuffStrap-1", "Coat", 130, 1.0, 0.006),
    ("Coat_CuffStrap1", "Coat", 130, 1.0, 0.006),
    ("Coat_Epaulet-1", "Coat", 90, 1.0, 0.006),
    ("Coat_Epaulet1", "Coat", 90, 1.0, 0.006),
    # Most of the trousers are under the coat, so they get less of the atlas than what shows.
    ("Trousers", "Clothes", 1500, 0.55, 0.02),
    ("Gloves", "Clothes", 2600, 1.3, 0.006),
    ("Shirt_Front", "Clothes", 300, 1.4, 0.012),
    ("Shirt_Collar", "Clothes", 350, 1.4, 0.008),
    ("Tie_Blade", "Clothes", 150, 1.8, 0.006),
    ("Tie_Knot", "Clothes", 120, 1.8, 0.006),
    ("Shoe_Upper-1", "Clothes", 900, 1.0, 0.008),
    ("Shoe_Upper1", "Clothes", 900, 1.0, 0.008),
    ("Shoe_Sole-1", "Clothes", 180, 0.6, 0.006),
    ("Shoe_Sole1", "Clothes", 180, 0.6, 0.006),
    ("Hat", "Clothes", 800, 1.0, 0.01),
    ("Hat_Band", "Clothes", 200, 1.0, 0.005),
    ("Head", "Head", 5200, 1.0, 0.008),
    ("Hair", "Hair", 1900, 1.0, 0.012),
    ("Eye_L", "Hair", 180, 3.0, 0.003),
    ("Eye_R", "Hair", 180, 3.0, 0.003),
]

# These weaves are finer than a texel of the atlas. Baked in they would only be noise, so Unity tiles a detail map instead.
FINE_WEAVE_MATERIALS = {"M_Coat", "M_Shirt", "M_Trousers", "M_HatBand"}

MARGIN_PIXELS = 8
AO_DISTANCE = 0.25
AO_IN_ALBEDO = 0.6
SAMPLES = 16
AO_SAMPLES = 64
AMBIGUOUS_FACING = 0.1
HOLE_FILL_PASSES = 48
SKELETON_SKIPS = ("Eye", "Orbicularis", "Jaw", "Breast", "Buttock", "Root")


def log(*values):
    print("[game]", *values, flush=True)


def activate(*objects):
    for other in bpy.context.selected_objects:
        other.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[-1]


def triangle_count(mesh):
    return sum(len(polygon.vertices) - 2 for polygon in mesh.polygons)


def nearest_bone_offset(points, starts, ends):
    """The vector from the closest point on any bone to each point."""
    along = ends - starts
    t = np.clip(((points[:, None] - starts) * along).sum(-1) / np.maximum((along * along).sum(-1), 1e-9), 0.0, 1.0)
    offsets = points[:, None] - (starts + t[..., None] * along)
    closest = (offsets * offsets).sum(-1).argmin(axis=1)
    return offsets[np.arange(len(points)), closest]


def face_outward(high, starts, ends):
    """Turns every connected piece to face away from the skeleton.

    Several panels were built facing inward. Cycles draws both sides so the design never showed it, but Unity culls them.
    """
    mesh = bmesh.new()
    mesh.from_mesh(high.data)
    mesh.faces.ensure_lookup_table()
    seen = set()
    for first in mesh.faces:
        if first.index in seen:
            continue
        seen.add(first.index)
        stack, piece = [first], []
        while stack:
            face = stack.pop()
            piece.append(face)
            for edge in face.edges:
                for other in edge.link_faces:
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)

        bmesh.ops.recalc_face_normals(mesh, faces=piece)
        centres = np.array([list(face.calc_center_median()) for face in piece])
        normals = np.array([list(face.normal) for face in piece])
        areas = np.array([face.calc_area() for face in piece])
        away = nearest_bone_offset(centres, starts, ends)
        away /= np.maximum(np.linalg.norm(away, axis=1, keepdims=True), 1e-9)
        facing = ((away * normals).sum(axis=1) * areas).sum() / areas.sum()
        closed = not any(edge.is_boundary for face in piece for edge in face.edges)
        # Below this the bones can't say which side is out (a tube or a button), so the normals from above stand.
        if not closed and facing < -AMBIGUOUS_FACING:
            bmesh.ops.reverse_faces(mesh, faces=piece)
    mesh.to_mesh(high.data)
    mesh.free()


def make_low(high, target, bones):
    low = high.copy()
    low.data = high.data.copy()
    low.name = high.name + "_low"
    bpy.context.scene.collection.objects.link(low)
    low.modifiers.clear()
    world = high.matrix_world.copy()
    low.parent = None
    low.matrix_world = world

    for group in list(low.vertex_groups):
        if group.name not in bones:
            low.vertex_groups.remove(group)

    # The buttons are built from rings that only touch, which stops them from being reduced.
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
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)

    low.data.polygons.foreach_set("use_smooth", [True] * len(low.data.polygons))
    low.data.update()
    return low


def unwrap_atlas(lows, density):
    """One packed atlas for these parts, with texels the same size everywhere apart from the density in PARTS."""
    for low in lows:
        while low.data.uv_layers:
            low.data.uv_layers.remove(low.data.uv_layers[0])
        low.data.uv_layers.new(name="UVMap")

    activate(*lows)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.13, island_margin=0.0, area_weight=0.0, scale_to_bounds=False)
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
    """The images for one atlas and the material the bake writes through."""

    PASSES = {"albedo": "sRGB", "normal": "Non-Color", "ao": "Non-Color", "roughness": "Non-Color", "metallic": "Non-Color"}
    START = {
        "albedo": (0, 0, 0, 0), "normal": (0.5, 0.5, 1, 1), "ao": (1, 1, 1, 1), "roughness": (0.7, 0.7, 0.7, 1), "metallic": (0, 0, 0, 1),
    }

    def __init__(self, name, size):
        self.name = name
        self.size = size
        self.images = {}
        for kind, space in self.PASSES.items():
            image = bpy.data.images.new(f"Human_{name}_{kind}", size, size, alpha=kind == "albedo")
            image.colorspace_settings.name = space
            image.generated_color = self.START[kind]
            image.pixels[0]  # Forces the generated colour into the buffer before the first bake.
            self.images[kind] = image

        self.material = bpy.data.materials.new(f"Human_{name}")
        self.material.use_nodes = True
        self.tree = self.material.node_tree
        self.node = self.tree.nodes.new("ShaderNodeTexImage")

    def target(self, kind):
        self.node.image = self.images[kind]
        self.tree.nodes.active = self.node

    def read(self, kind):
        image = self.images[kind]
        pixels = np.empty(self.size * self.size * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        return pixels.reshape(self.size, self.size, 4)


def bake(kind, low, high, reach, samples):
    activate(high, low)
    scene = bpy.context.scene
    scene.cycles.samples = samples
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


def drop_fine_weave(materials):
    for material in materials:
        if material.name in FINE_WEAVE_MATERIALS:
            socket = principled(material).inputs["Normal"]
            for link in list(socket.links):
                material.node_tree.links.remove(link)


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
    save_png(f"Human_{atlas.name}_Albedo", albedo, True)

    normal = atlas.read("normal")
    normal[..., 3] = 1.0
    save_png(f"Human_{atlas.name}_Normal", normal, False)

    # URP's Metallic map: metallic in red, smoothness in alpha.
    metal = atlas.read("metallic")
    metal[..., 1] = metal[..., 2] = 0.0
    metal[..., 3] = 1.0 - atlas.read("roughness")[..., 0]
    save_png(f"Human_{atlas.name}_MetallicSmoothness", metal, False)


def weave_normal(size=512, threads=48):
    """A tiling plain-weave normal map for the detail slot in URP."""
    t = (np.arange(size) + 0.5) / size * 2.0 * np.pi * threads
    u, v = np.meshgrid(t, t)
    over = np.sign(np.sin((u + v) / 2.0) * np.sin((u - v) / 2.0))
    height = 0.5 * (np.cos(u) ** 2 * (over > 0) + np.cos(v) ** 2 * (over <= 0))
    rng = np.random.default_rng(7)
    fuzz = np.fft.irfft2(np.fft.rfft2(rng.standard_normal((size, size))) * np.exp(-np.fft.rfftfreq(size)[None, :] ** 2 * 300), s=(size, size))
    height = height + fuzz * 0.15
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * 2.0
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * 2.0
    normal = np.stack([-dx, -dy, np.ones_like(dx)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    pixels = np.ones((size, size, 4), dtype=np.float32)
    pixels[..., :3] = normal * 0.5 + 0.5
    save_png("Human_Weave_Normal", pixels, False)


def uv_metres_per_unit(low):
    """How many metres one full width of the atlas covers on this part, for tiling the detail map."""
    mesh = low.data
    uv = np.empty(len(mesh.uv_layers.active.data) * 2, dtype=np.float32)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    uv_area = world_area = 0.0
    for polygon in mesh.polygons:
        a, b, c = polygon.loop_indices[:3]
        e1, e2 = uv[b] - uv[a], uv[c] - uv[a]
        uv_area += 0.5 * abs(e1[0] * e2[1] - e1[1] * e2[0])
        world_area += polygon.area
    return (world_area / uv_area) ** 0.5


def export(lows, rig):
    activate(*lows)
    bpy.ops.object.join()
    human = bpy.context.view_layer.objects.active
    human.name = "Human"
    human.data.name = "Human"
    human.parent = rig
    human.matrix_parent_inverse.identity()
    armature = human.modifiers.new("Rig", "ARMATURE")
    armature.object = rig

    activate(human, rig)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUTPUT, "Human.fbx"),
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
    return human


def preview_material(atlas):
    """A plain Principled material from the finished textures, so game.blend can be rendered next to the design."""
    material = atlas.material
    tree = material.node_tree
    tree.nodes.remove(atlas.node)
    bsdf = principled(material)
    folder = os.path.relpath(OUTPUT, HERE)

    def image(suffix, space):
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(os.path.join(OUTPUT, f"Human_{atlas.name}_{suffix}.png"))
        node.image.colorspace_settings.name = space
        node.image.filepath = "//" + os.path.join(folder, f"Human_{atlas.name}_{suffix}.png")
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


def parse_scale():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--scale", type=float, default=1.0)
    return parser.parse_args(argv).scale


def main():
    scale = parse_scale()
    started = time.time()
    os.makedirs(OUTPUT, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "human.blend"))
    rig = bpy.data.objects["Rig"]
    bones = {bone.name for bone in rig.data.bones}

    R.setup_cycles(samples=SAMPLES, width=64, height=64)
    scene = bpy.context.scene
    R.set_world((0, 0, 0), 0.0)
    scene.world.light_settings.distance = AO_DISTANCE
    scene.cycles.use_denoising = False

    skeleton = [bone for bone in rig.data.bones if not any(skip in bone.name for skip in SKELETON_SKIPS)]
    starts = np.array([list(rig.matrix_world @ bone.head_local) for bone in skeleton])
    ends = np.array([list(rig.matrix_world @ bone.tail_local) for bone in skeleton])

    lows, atlas_lows, density, reaches, highs = {}, {name: [] for name in ATLASES}, {}, {}, {}
    for name, atlas_name, target, part_density, reach in PARTS:
        high = bpy.data.objects[name]
        face_outward(high, starts, ends)
        low = make_low(high, target, bones)
        highs[low.name] = high
        lows[name] = low
        atlas_lows[atlas_name].append(low)
        density[low.name] = part_density
        reaches[low.name] = reach
    log("triangles", sum(triangle_count(low.data) for low in lows.values()))
    log({name: triangle_count(low.data) for name, low in lows.items()})

    # The head keeps the UVs its skin was painted on.
    for atlas_name, group in atlas_lows.items():
        if atlas_name != "Head":
            unwrap_atlas(group, density)

    atlases = {name: Atlas(name, max(64, int(size * scale))) for name, size in ATLASES.items()}
    for atlas_name, group in atlas_lows.items():
        for low in group:
            low.data.materials.clear()
            low.data.materials.append(atlases[atlas_name].material)
            # Copies must not block the occlusion rays that belong to the full-detail model.
            low.visible_shadow = low.visible_diffuse = low.visible_glossy = low.visible_transmission = low.visible_volume_scatter = False

    high_materials = {slot.material for high in highs.values() for slot in high.material_slots if slot.material}
    drop_fine_weave(high_materials)

    def bake_all(kind, target, samples):
        begun = time.time()
        for atlas_name, group in atlas_lows.items():
            atlases[atlas_name].target(target)
            for low in group:
                bake(kind, low, highs[low.name], reaches[low.name], samples)
        log("baked", target, round(time.time() - begun), "s")

    bake_all("NORMAL", "normal", SAMPLES)
    bake_all("AO", "ao", AO_SAMPLES)

    for socket_name, target in (("Base Color", "albedo"), ("Roughness", "roughness"), ("Metallic", "metallic")):
        for material in high_materials:
            show_input_as_emission(material, socket_name)
        bake_all("EMIT", target, SAMPLES)

    for atlas in atlases.values():
        finish_textures(atlas)
    weave_normal()

    coat_metres = np.mean([uv_metres_per_unit(low) for low in atlas_lows["Coat"][:2]])
    log("coat atlas width in metres", round(float(coat_metres), 3))

    for high in list(highs.values()):
        bpy.data.objects.remove(high)
    for atlas in atlases.values():
        for image in atlas.images.values():
            bpy.data.images.remove(image)
        preview_material(atlas)

    all_lows = [low for group in atlas_lows.values() for low in group]
    for low in all_lows:
        low.visible_shadow = low.visible_diffuse = low.visible_glossy = low.visible_transmission = low.visible_volume_scatter = True
    human = export(all_lows, rig)
    log("exported triangles", triangle_count(human.data), "vertices", len(human.data.vertices))

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.outliner.orphans_purge(do_recursive=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "game.blend"))
    log("done in", round(time.time() - started), "s")


if __name__ == "__main__":
    main()
