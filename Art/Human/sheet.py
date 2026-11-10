"""Renders the design sheet from human.blend into design_sheet.png.

Run:  blender -b --python Art/Human/sheet.py

The two right-hand night panels use the game's real values: the Lamp prefab, the flashlight, the follow
camera, the ambient colours and the Volume grade from Corridor_01.
"""
import math
import os
import sys

sys.dont_write_bytecode = True

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import body as B  # noqa: E402
import garment_lib as G  # noqa: E402
import render_util as R  # noqa: E402

WIDTH, HEIGHT = 900, 1200
SAMPLES = 160

# Blender's light power that gives the same diffuse brightness as a URP light of intensity 1.
URP_TO_BLENDER = 1.0 / 0.0253
# From Assets/Prefabs/Lamp.prefab, Assets/Settings/SampleSceneProfile.asset and Corridor_01.
LAMP_INTENSITY = 25.0
LAMP_ANGLE, LAMP_INNER = 80.0, 65.0
FLASHLIGHT_INTENSITY = 4.0
FLASHLIGHT_ANGLE, FLASHLIGHT_INNER = 45.0, 25.0
FLASHLIGHT_COLOUR = (1.0, 0.93, 0.8)
CONTRAST, SATURATION, VIGNETTE, VIGNETTE_SMOOTH = 35.0, -45.0, 0.4, 0.4
AMBIENT = (0.08, 0.087, 0.09)


def pose_for_display(rig):
    prefix = G.PREFIX
    for side, sign in (("Left", 1), ("Right", -1)):
        B.aim(rig, f"{prefix}{side}Arm", (sign * 0.2, 0.02, -1.0))
        B.aim(rig, f"{prefix}{side}ForeArm", (sign * 0.16, -0.18, -1.0))
    bpy.context.view_layer.update()


def plane(name, size, material, location=(0, 0, 0), rotation=(0, 0, 0)):
    half = size / 2
    obj = bpy.data.objects.new(name, bpy.data.meshes.new(name))
    bpy.context.scene.collection.objects.link(obj)
    obj.data.from_pydata([(-half, -half, 0), (half, -half, 0), (half, half, 0), (-half, half, 0)], [], [(0, 1, 2, 3)])
    obj.data.materials.append(material)
    obj.location = location
    obj.rotation_euler = rotation
    return obj


def floor_material(colour, name):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    node = tree.nodes["Principled BSDF"]
    noise = tree.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 3.5
    noise.inputs["Detail"].default_value = 7.0
    ramp = tree.nodes.new("ShaderNodeMapRange")
    ramp.inputs["From Min"].default_value = 0.3
    ramp.inputs["From Max"].default_value = 0.7
    ramp.inputs["To Min"].default_value = 0.72
    ramp.inputs["To Max"].default_value = 1.2
    tree.links.new(noise.outputs["Fac"], ramp.inputs["Value"])
    mix = tree.nodes.new("ShaderNodeVectorMath")
    mix.operation = "SCALE"
    mix.inputs[0].default_value = colour
    tree.links.new(ramp.outputs[0], mix.inputs["Scale"])
    tree.links.new(mix.outputs[0], node.inputs["Base Color"])
    node.inputs["Roughness"].default_value = 0.85
    return material


def capsule_guide(radius, height, y):
    """A see-through copy of the player's CharacterController, to compare his size with."""
    profile = []
    for step in range(10):
        a = math.pi / 2 * step / 9
        profile.append((radius * math.sin(a), radius - radius * math.cos(a)))
    for step in range(10):
        a = math.pi / 2 * step / 9
        profile.append((radius * math.cos(a), height - radius + radius * math.sin(a)))
    verts, faces = G.lathe_disc((0, y, 0), (0, 0, 1), profile, segments=40)
    obj = G.mesh_object("Guide_Capsule", verts, faces)
    material = bpy.data.materials.new("M_Guide")
    material.use_nodes = True
    node = material.node_tree.nodes["Principled BSDF"]
    node.inputs["Base Color"].default_value = (0.25, 0.42, 0.75, 1)
    node.inputs["Alpha"].default_value = 0.26
    node.inputs["Roughness"].default_value = 0.4
    obj.data.materials.append(material)
    obj.visible_shadow = False
    return obj


def height_post(y, height, colour):
    bpy.ops.mesh.primitive_cylinder_add(radius=0.012, depth=height, location=(0.38, y, height / 2))
    post = bpy.context.object
    material = bpy.data.materials.new("M_Post")
    material.use_nodes = True
    material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*colour, 1)
    post.data.materials.append(material)
    post.visible_shadow = False
    return post


def spot(name, location, target, intensity, angle, inner, colour=(1, 1, 1)):
    light = bpy.data.objects.get(name)
    if light is None:
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SPOT"))
        bpy.context.scene.collection.objects.link(light)
    light.data.energy = intensity * URP_TO_BLENDER
    light.data.color = colour
    light.data.spot_size = math.radians(angle)
    light.data.spot_blend = 1.0 - inner / angle
    light.data.shadow_soft_size = 0.0
    light.location = Vector(location)
    light.rotation_mode = "QUATERNION"
    light.rotation_quaternion = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y")
    return light


def load_exr(path):
    image = bpy.data.images.load(path, check_existing=False)
    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape(height, width, 4)[::-1, :, :3].astype(np.float64)


def linear_to_srgb(values):
    values = np.clip(values, 0.0, 1.0)
    return np.where(values <= 0.0031308, values * 12.92, 1.055 * values ** (1 / 2.4) - 0.055)


def to_logc(x):
    cut, a, b, c, d, e, f = 0.011361, 5.555556, 0.047996, 0.244161, 0.386036, 5.301883, 0.092814
    return np.where(x > cut, c * np.log10(np.maximum(a * x + b, 1e-6)) + d, e * x + f)


def from_logc(t):
    cut, a, b, c, d, e, f = 0.011361, 5.555556, 0.047996, 0.244161, 0.386036, 5.301883, 0.092814
    return np.where(t > e * cut + f, (10 ** ((t - d) / c) - b) / a, (t - f) / e)


def neutral_curve(x):
    a, b, c, d, e, f = 0.2, 0.29, 0.24, 0.272, 0.02, 0.3
    return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f


def urp_grade(linear):
    """The same order of operations as URP's colour grading, using the game's Volume values."""
    grey = 0.4135884
    colour = from_logc((to_logc(linear) - grey) * (1.0 + CONTRAST / 100.0) + grey)
    luma = colour @ np.array([0.2126729, 0.7151522, 0.0721750])
    colour = luma[..., None] + (1.0 + SATURATION / 100.0) * (colour - luma[..., None])
    colour = np.maximum(colour, 0.0)

    white = 1.0 / neutral_curve(5.3)
    colour = neutral_curve(colour * white) * white

    height, width = colour.shape[:2]
    y, x = np.mgrid[0:height, 0:width]
    d = np.stack([np.abs(x / width - 0.5), np.abs(y / height - 0.5)], axis=-1) * VIGNETTE
    d[..., 0] *= width / height
    factor = np.clip(1.0 - (d * d).sum(axis=-1), 0.0, 1.0) ** (VIGNETTE_SMOOTH * 5.0)
    return colour * factor[..., None]


def render_panel(name, graded):
    scene = bpy.context.scene
    path = os.path.join(BUILD, f"{name}.exr")
    scene.render.image_settings.file_format = "OPEN_EXR"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "32"
    scene.view_settings.view_transform = "Standard"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    linear = load_exr(path)
    output = urp_grade(linear) if graded else linear
    R.remove_file_quietly(path)
    return linear_to_srgb(output)


def clear_lights():
    for obj in list(bpy.data.objects):
        if obj.type == "LIGHT":
            bpy.data.objects.remove(obj)


def studio():
    clear_lights()
    R.set_world((0.3, 0.3, 0.33), 0.75)
    centre = Vector((0, 0, 0.98))
    R.area_light("Key", R.orbit(centre, -40, 35, 5.5), centre, 300, 3, (1, 0.96, 0.9))
    R.area_light("Rim", R.orbit(centre, 155, 30, 5), centre, 180, 3, (0.9, 0.95, 1))


def set_visible(objects, visible):
    for obj in objects:
        obj.hide_render = not visible


def main():
    global BUILD
    bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "human.blend"))
    BUILD = os.path.join(bpy.app.tempdir or "/tmp")
    rig = bpy.data.objects["Rig"]
    pose_for_display(rig)

    R.setup_cycles(samples=SAMPLES, width=WIDTH, height=HEIGHT)
    scene = bpy.context.scene

    studio_floor = plane("StudioFloor", 60, floor_material((0.22, 0.22, 0.24), "M_StudioFloor"))
    night_floor = plane("NightFloor", 60, floor_material((0.15, 0.15, 0.155), "M_NightFloor"))
    night_wall = plane("NightWall", 60, floor_material((0.12, 0.12, 0.125), "M_NightWall"),
                       location=(0, 2.9, 0), rotation=(math.radians(90), 0, 0))
    capsule = capsule_guide(0.35, 1.8, 0.82)
    stand_post = height_post(0.82, 1.8, (0.9, 0.86, 0.25))
    crouch_post = height_post(0.82, 1.1, (0.85, 0.32, 0.2))
    guides = [capsule, stand_post, crouch_post]

    centre = Vector((0, 0, 0.98))
    panels = []

    set_visible([night_floor, night_wall] + guides, False)
    studio()
    for name, azimuth, elevation, distance, lens, target in (
        ("front", 0, 2, 5.6, 85, centre),
        ("back", 180, 3, 5.6, 85, centre),
    ):
        R.look_camera("SheetCamera", R.orbit(target, azimuth, elevation, distance), target, lens)
        panels.append(render_panel(name, False))

    set_visible(guides, True)
    side_target = Vector((0, 0.32, 0.98))
    R.look_camera("SheetCamera", R.orbit(side_target, 90, 2, 6.4), side_target, 85)
    panels.append(render_panel("side", False))
    set_visible(guides, False)

    portrait_target = Vector((0, -0.06, 1.685))
    R.look_camera("SheetCamera", R.orbit(portrait_target, 27, 1, 1.1), portrait_target, 85)
    panels.append(render_panel("portrait", False))

    set_visible([studio_floor], False)
    set_visible([night_floor, night_wall], True)
    clear_lights()
    R.set_world(AMBIENT, 1.0)

    lamp = spot("Lamp", (0.55, -1.75, 4.2), (0.55, -1.75, 0.0), LAMP_INTENSITY * 0.88, LAMP_ANGLE, LAMP_INNER)
    lit_target = Vector((0, -0.2, 0.98))
    R.look_camera("SheetCamera", R.orbit(lit_target, 24, 4, 5.6), lit_target, 70)
    panels.append(render_panel("lamp", True))

    # What the player sees: the real follow camera, with a lamp up ahead throwing his shadow back.
    set_visible([night_wall], False)
    lamp.location = Vector((0.5, -3.3, 4.2))
    lamp.rotation_quaternion = Vector((0, 0, -1)).to_track_quat("-Z", "Y")
    target_height = 1.55
    shoulder = 0.5
    arm = 0.3
    camera_distance = 3.2
    camera_at = Vector((-shoulder, camera_distance, target_height + arm))
    R.look_camera("SheetCamera", camera_at, camera_at + Vector((0, -1, 0)), 31.18)
    spot(
        "Flashlight",
        (-0.25, -0.4, target_height - 0.25),
        (-0.25, -5.0, target_height - 0.85),
        FLASHLIGHT_INTENSITY,
        FLASHLIGHT_ANGLE,
        FLASHLIGHT_INNER,
        FLASHLIGHT_COLOUR,
    )
    panels.append(render_panel("game", True))

    sheet = np.hstack(panels)
    rgba = np.ones((sheet.shape[0], sheet.shape[1], 4), dtype=np.float32)
    rgba[..., :3] = sheet[::-1]
    image = bpy.data.images.new("design_sheet", sheet.shape[1], sheet.shape[0], alpha=False)
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = os.path.join(HERE, "design_sheet.png")
    image.file_format = "PNG"
    image.save()

    tops = {}
    for obj in bpy.data.objects:
        if obj.type == "MESH" and obj.name in ("Hat", "Head"):
            depsgraph = bpy.context.evaluated_depsgraph_get()
            evaluated = obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh()
            tops[obj.name] = max((evaluated.matrix_world @ v.co).z for v in mesh.vertices)
            evaluated.to_mesh_clear()
    print("HEIGHTS", {k: round(v, 3) for k, v in tops.items()})


BUILD = "/tmp"

if __name__ == "__main__":
    main()
