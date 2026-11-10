"""Builds the human body and face with MPFB and saves them to body.blend.

Run:  blender -b --python Art/Human/body.py

Every shape decision lives in MACRO and DETAILS, so the face is tuned here and nowhere else.
"""
import os

import addon_utils
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
MPFB = "bl_ext.blender_org.mpfb"

# Overall build: an adult man, lean and a little imposing.
MACRO = {
    "gender": 1.0,
    "age": 0.62,
    "muscle": 0.5,
    "weight": 0.42,
    "proportions": 0.8,
    "height": 0.552,
    "cupsize": 0.5,
    "firmness": 0.5,
    "race": {"asian": 0.0, "caucasian": 1.0, "african": 0.0},
}

# Both sides get the same value; the "$" is replaced with l and r.
DETAILS = {
    # Long face with a square jaw.
    "head-rectangular": 0.6,
    "head-scale-horiz-decr": 0.22,
    "chin-bones-incr": 0.8,
    "chin-width-incr": 0.3,
    "chin-prominent-incr": 0.35,
    "chin-height-incr": 0.25,
    # High cheekbones with hollow cheeks.
    "$-cheek-bones-incr": 0.5,
    "$-cheek-volume-decr": 0.55,
    "$-cheek-inner-decr": 0.35,
    # Straight, narrow nose.
    "nose-width1-decr": 0.3,
    "nose-width2-decr": 0.3,
    "nose-point-width-decr": 0.3,
    "nose-scale-vert-incr": 0.2,
    "nose-point-down": 0.12,
    # Thin mouth with a slight frown.
    "mouth-angles-down": 0.28,
    "mouth-scale-horiz-incr": 0.2,
    "mouth-laugh-lines-in": 0.45,
    "mouth-upperlip-height-decr": 0.3,
    "mouth-lowerlip-height-decr": 0.2,
    "mouth-upperlip-volume-decr": 0.4,
    "mouth-lowerlip-volume-decr": 0.35,
    # Deep-set eyes under a low, straight brow.
    "$-eye-trans-in": 0.35,
    "$-eye-height2-decr": 0.24,
    "$-eye-eyefold-down": 0.22,
    "eyebrows-trans-down": 0.35,
    "eyebrows-trans-forward": 0.35,
    "forehead-temple-decr": 0.2,
}


def enable_mpfb():
    bpy.context.preferences.use_preferences_save = False
    addon_utils.enable(MPFB, default_set=True)


def build_body():
    from bl_ext.blender_org.mpfb.services.humanservice import HumanService
    from bl_ext.blender_org.mpfb.services.targetservice import TargetService

    body = HumanService.create_human(
        mask_helpers=False,
        detailed_helpers=True,
        extra_vertex_groups=True,
        feet_on_ground=True,
        scale=0.1,
        macro_detail_dict=dict(MACRO),
    )
    body.name = "Body"

    for name, value in DETAILS.items():
        for resolved in ([name.replace("$", side) for side in "lr"] if "$" in name else [name]):
            path = TargetService.target_full_path(resolved)
            if path is None:
                raise ValueError(f"MPFB has no target called {resolved}")
            TargetService.load_target(body, path, weight=value)

    TargetService.bake_targets(body)
    return body


def rotate_world(rig, bone_name, axis, angle):
    """Turns a pose bone about its own head around a world-space axis."""
    bone = rig.pose.bones[bone_name]
    head = bone.matrix.to_translation()
    turn = Matrix.Translation(head) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(-head)
    bone.matrix = turn @ bone.matrix
    bpy.context.view_layer.update()


def aim(rig, bone_name, direction):
    """Turns a pose bone so its length points along the given world direction."""
    bone = rig.pose.bones[bone_name]
    now = (bone.tail - bone.head).normalized()
    wanted = Vector(direction).normalized()
    axis = now.cross(wanted)
    if axis.length > 1e-6:
        rotate_world(rig, bone_name, axis.normalized(), now.angle(wanted))


def keep_world_rotation(rig, bone_name, rotation):
    bone = rig.pose.bones[bone_name]
    bone.matrix = Matrix.Translation(bone.matrix.to_translation()) @ rotation.to_matrix().to_4x4()
    bpy.context.view_layer.update()


def set_rest_stance(body, rig):
    """Brings the feet in and straightens the elbows, so clothes fit a natural standing shape."""
    from bl_ext.blender_org.mpfb.services.rigservice import RigService

    prefix = "mixamorig:"
    for side, sign in (("Left", 1), ("Right", -1)):
        foot = rig.pose.bones[f"{prefix}{side}Foot"].matrix.to_quaternion()
        aim(rig, f"{prefix}{side}UpLeg", (sign * 0.012, -0.06, -1.0))
        aim(rig, f"{prefix}{side}Leg", (sign * 0.02, 0.03, -1.0))
        keep_world_rotation(rig, f"{prefix}{side}Foot", foot)
        upper = rig.pose.bones[f"{prefix}{side}Arm"]
        arm_line = (upper.tail - upper.head).normalized()
        aim(rig, f"{prefix}{side}ForeArm", arm_line + Vector((0, -0.16, 0)))

    bpy.context.view_layer.objects.active = rig
    RigService.apply_pose_as_rest_pose(rig)


def main():
    enable_mpfb()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    enable_mpfb()
    body = build_body()

    from bl_ext.blender_org.mpfb.services.humanservice import HumanService

    rig = HumanService.add_builtin_rig(body, "mixamo_unity", import_weights=True)
    rig.name = "Rig"
    set_rest_stance(body, rig)

    verts = [body.matrix_world @ v.co for v in body.data.vertices]
    print("BODY verts", len(verts), "faces", len(body.data.polygons))
    print("BODY z", min(v.z for v in verts), max(v.z for v in verts))
    print("GROUPS", sorted(g.name for g in body.vertex_groups if not g.name.startswith("mixamorig"))[:80])
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "body.blend"))


if __name__ == "__main__":
    main()
