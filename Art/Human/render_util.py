"""Shared helpers for rendering previews and the design sheet."""
import math
import os

import bpy
from mathutils import Vector


def setup_cycles(samples=48, width=800, height=1000, use_gpu=True):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.02
    scene.cycles.use_denoising = True
    scene.cycles.denoiser = "OPENIMAGEDENOISE"
    scene.cycles.max_bounces = 6
    scene.cycles.diffuse_bounces = 3
    scene.cycles.glossy_bounces = 3
    scene.cycles.transmission_bounces = 4
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.view_settings.view_transform = "Filmic"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    if use_gpu:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "HIP"
        prefs.get_devices()
        for device in prefs.devices:
            device.use = device.type == "HIP"
        scene.cycles.device = "GPU"


def look_camera(name, location, target, lens=85.0):
    camera = bpy.data.objects.get(name)
    if camera is None:
        camera = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        bpy.context.scene.collection.objects.link(camera)
    camera.data.lens = lens
    camera.data.sensor_width = 36.0
    camera.location = Vector(location)
    camera.rotation_mode = "QUATERNION"
    camera.rotation_quaternion = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y")
    bpy.context.scene.camera = camera
    return camera


def orbit(target, azimuth_degrees, elevation_degrees, distance):
    """Point on a sphere around target. Azimuth 0 is in front of the character (-Y); positive moves toward +X."""
    azimuth = math.radians(azimuth_degrees)
    elevation = math.radians(elevation_degrees)
    offset = Vector((
        math.sin(azimuth) * math.cos(elevation),
        -math.cos(azimuth) * math.cos(elevation),
        math.sin(elevation),
    ))
    return Vector(target) + offset * distance


def area_light(name, location, target, power, size, color=(1, 1, 1)):
    light = bpy.data.objects.get(name)
    if light is None:
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "AREA"))
        bpy.context.scene.collection.objects.link(light)
    light.data.energy = power
    light.data.size = size
    light.data.color = color
    light.location = Vector(location)
    light.rotation_mode = "QUATERNION"
    light.rotation_quaternion = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y")
    return light


def set_world(color, strength=1.0):
    world = bpy.data.worlds.get("World") or bpy.data.worlds.new("World")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs[0].default_value = (*color, 1)
    background.inputs[1].default_value = strength
    bpy.context.scene.world = world


def remove_file_quietly(path):
    try:
        os.remove(path)
    except OSError:
        pass
