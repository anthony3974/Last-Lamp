"""Renders the game version of the Wick under the same views as wick_design_sheet.png.

Run:  blender -b --python Art/Wick/sheet.py                    (game_sheet.png from game.blend)
      blender -b --python Art/Wick/sheet.py -- --poses         (pose_sheet.png: the rig bent into a grab and a stride)
      blender -b --python Art/Wick/sheet.py -- --checker       (checker_sheet.png: a checker in the UVs, to see stretching)
      blender -b --python Art/Wick/sheet.py -- --blend some/game.blend --out some/folder
"""
import argparse
import math
import os
import sys

sys.dont_write_bytecode = True

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Euler, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = 64
PANEL = (760, 1080)
SHEET_HEIGHT = 739
PREFIX = "mixamorig:"


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend", default=os.path.join(HERE, "game.blend"))
    parser.add_argument("--out", default=HERE)
    parser.add_argument("--poses", action="store_true")
    parser.add_argument("--checker", action="store_true")
    parser.add_argument("--samples", type=int, default=SAMPLES)
    return parser.parse_args(argv)


def principled(material):
    return next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")


def new_material(name, colour, roughness):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = principled(material)
    bsdf.inputs["Base Color"].default_value = colour
    bsdf.inputs["Roughness"].default_value = roughness
    return material


def spot(name, energy, size, blend, soft, location, colour=(1, 1, 1)):
    light = bpy.data.lights.new(name, "SPOT")
    light.energy, light.spot_size, light.spot_blend, light.shadow_soft_size, light.color = energy, math.radians(size), blend, soft, colour
    obj = bpy.data.objects.new(name, light)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    return obj


def area(name, location, target, size, power):
    light = bpy.data.lights.new(name, "AREA")
    light.size, light.energy = size, power
    obj = bpy.data.objects.new(name, light)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
    return obj


class Stage:
    """The lights, floor and camera from wick.py, so the two sheets compare directly."""

    def __init__(self, samples):
        scene = self.scene = bpy.context.scene
        scene.render.engine = "CYCLES"
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
        scene.cycles.device = "CPU"
        scene.render.resolution_x, scene.render.resolution_y = PANEL
        scene.render.resolution_percentage = 100
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Medium High Contrast"

        world = bpy.data.worlds.new("Sheet")
        world.use_nodes = True
        scene.world = world
        self.background = world.node_tree.nodes["Background"]

        bpy.ops.mesh.primitive_plane_add(size=24)
        floor = bpy.context.object
        floor.data.materials.append(new_material("Floor", (.17, .17, .17, 1), .75))

        bpy.ops.mesh.primitive_cylinder_add(radius=.32, depth=1.8, location=(0, .58, .9), vertices=48)
        self.player = bpy.context.object
        self.player.data.materials.append(new_material("Player", (.2, .25, .34, 1), .6))
        bpy.ops.object.shade_smooth()

        self.studio = [area("key", (-2.4, -3.6, 3.3), (0, -.2, 1.2), 2.6, 420), area("rim", (2.6, 3.0, 3.0), (0, 0, 1.3), 2.0, 520)]
        self.lamp = spot("lamp", 900, 80, .12, .05, (-1.0, -.1, 3.6), (1, .86, .62))
        self.flash = spot("flash", 70, 30, .5, .02, (.5, -4.2, 1.35))
        self.flash.rotation_euler = (Vector((0, -.4, 1.45)) - self.flash.location).to_track_quat("-Z", "Y").to_euler()

        self.camera_data = bpy.data.cameras.new("Sheet")
        self.camera_data.sensor_fit = "VERTICAL"
        self.camera_data.sensor_height = 24
        self.camera = bpy.data.objects.new("Sheet", self.camera_data)
        scene.collection.objects.link(self.camera)
        scene.camera = self.camera

        alert = bpy.data.materials.get("Monster_Alert")
        self.glow = principled(alert) if alert else None

    def mode(self, noir, side=False, red=False):
        for light in self.studio:
            light.hide_render = noir
        self.lamp.hide_render = self.flash.hide_render = not noir
        self.player.hide_render = not side
        self.background.inputs[0].default_value = (.012, .012, .014, 1) if noir else (.16, .16, .17, 1)
        self.background.inputs[1].default_value = .35 if noir else 1
        if self.glow:
            self.glow.inputs["Emission Color"].default_value = (1, .015, .01, 1) if red else (1, .48, .05, 1)
            self.glow.inputs["Emission Strength"].default_value = 34 if red else 2.5

    def shoot(self, path, azimuth, height=1.18, distance=8.6, target=(0, -.12, 1.0), lens=85):
        aim = Vector(target)
        angle = math.radians(azimuth)
        self.camera.location = aim + Vector((math.sin(angle) * distance, -math.cos(angle) * distance, height - target[2]))
        self.camera.rotation_euler = (aim - self.camera.location).to_track_quat("-Z", "Y").to_euler()
        self.camera_data.lens = lens
        self.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("[sheet] rendered", os.path.basename(path), flush=True)
        return path


def pose(rig, rotations):
    """Rotations in degrees, applied in each bone's own axes on top of the rest pose."""
    for bone in rig.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
    for name, degrees in rotations.items():
        rig.pose.bones[PREFIX + name].rotation_euler = Euler([math.radians(value) for value in degrees], "XYZ")
    bpy.context.view_layer.update()


# Bones point along their own Y. With zero roll, X is roughly the Wick's side and Z its front or back.
GRAB = {
    "LeftArm": (-40, 0, 0), "RightArm": (-40, 0, 0),
    "LeftForeArm": (-20, 0, 0), "RightForeArm": (-20, 0, 0),
    "LeftHandIndex1": (-30, 0, 0), "LeftHandMiddle1": (-30, 0, 0), "LeftHandRing1": (-30, 0, 0), "LeftHandPinky1": (-30, 0, 0),
    "Spine1": (-10, 0, 0), "Neck": (15, 0, 0), "Head": (10, 0, 0),
}
STRIDE = {
    "LeftUpLeg": (35, 0, 0), "LeftLeg": (-30, 0, 0), "RightUpLeg": (-30, 0, 0), "RightLeg": (-50, 0, 0),
    "LeftArm": (25, 0, 0), "RightArm": (-25, 0, 0), "Spine": (0, 12, 0), "Spine2": (0, -12, 0),
}


def show_checker():
    """Swaps the baked colour for an even checker, so stretched or squeezed UVs show as uneven squares."""
    material = bpy.data.materials["Wick_Skin"]
    tree = material.node_tree
    checker = tree.nodes.new("ShaderNodeTexChecker")
    checker.inputs["Scale"].default_value = 60
    checker.inputs["Color1"].default_value = (.8, .8, .8, 1)
    checker.inputs["Color2"].default_value = (.05, .05, .05, 1)
    tree.links.new(checker.outputs["Color"], principled(material).inputs["Base Color"])


def stitch(paths, out):
    panels = []
    for path in paths:
        image = bpy.data.images.load(path)
        width, height = image.size
        pixels = np.array(image.pixels[:], dtype=np.float32).reshape(height, width, 4)
        panels.append(pixels)
        bpy.data.images.remove(image)
    sheet = np.concatenate(panels, axis=1)
    full = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    full.pixels.foreach_set(sheet.ravel())
    full.scale(int(sheet.shape[1] * SHEET_HEIGHT / sheet.shape[0]), SHEET_HEIGHT)
    full.filepath_raw = out
    full.file_format = "PNG"
    full.save()
    for path in paths:
        os.remove(path)
    print("[sheet] wrote", out, flush=True)


def main():
    options = args()
    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(options.blend))
    stage = Stage(options.samples)
    rig = bpy.data.objects["Rig"]
    out = os.path.abspath(options.out)
    shots = []

    if options.poses:
        for name, rotations in (("grab", GRAB), ("stride", STRIDE)):
            pose(rig, rotations)
            stage.mode(False)
            shots.append(stage.shoot(os.path.join(out, f"_{name}_front.png"), 20))
            stage.mode(False)
            shots.append(stage.shoot(os.path.join(out, f"_{name}_side.png"), 90, distance=10.5))
        stitch(shots, os.path.join(out, "pose_sheet.png"))
        return

    if options.checker:
        show_checker()
        for name, azimuth in (("front", 0), ("back", 180), ("side", 90)):
            stage.mode(False)
            shots.append(stage.shoot(os.path.join(out, f"_{name}.png"), azimuth))
        stage.mode(False)
        shots.append(stage.shoot(os.path.join(out, "_face.png"), 25, height=1.72, distance=3.0, target=(0, -.5, 1.68)))
        stitch(shots, os.path.join(out, "checker_sheet.png"))
        return

    stage.mode(False)
    shots.append(stage.shoot(os.path.join(out, "_front.png"), 0))
    stage.mode(False)
    shots.append(stage.shoot(os.path.join(out, "_three.png"), 215))
    stage.mode(False, side=True)
    shots.append(stage.shoot(os.path.join(out, "_side.png"), 90, distance=10.5))
    stage.mode(True, red=True)
    shots.append(stage.shoot(os.path.join(out, "_noir.png"), 22))
    # Not on the design sheet: the glands down the spine are the warning cue from behind.
    stage.mode(True, red=True)
    shots.append(stage.shoot(os.path.join(out, "_noir_back.png"), 200))
    stage.mode(False)
    shots.append(stage.shoot(os.path.join(out, "_face.png"), 25, height=1.72, distance=3.0, target=(0, -.5, 1.68)))
    stitch(shots, os.path.join(out, "game_sheet.png"))


if __name__ == "__main__":
    main()
