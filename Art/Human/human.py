"""Builds the full dressed character from body.blend and saves it to human.blend.

Run:  blender -b --python Art/Human/human.py
"""
import os
import sys

sys.dont_write_bytecode = True

import bpy  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import body as body_step  # noqa: E402
import clothes as C  # noqa: E402
import garment_lib as G  # noqa: E402
import head as H  # noqa: E402
import materials as M  # noqa: E402
import skin as S  # noqa: E402

PREFIX = G.PREFIX

def attach_to_rig(obj, source, rig):
    """Copies the body's bone weights onto a garment and binds it to the skeleton."""
    for other in bpy.context.selected_objects:
        other.select_set(False)
    obj.select_set(True)
    source.select_set(True)
    bpy.context.view_layer.objects.active = source
    bpy.ops.object.data_transfer(
        use_reverse_transfer=False,
        data_type="VGROUP_WEIGHTS",
        use_create=True,
        vert_mapping="POLYINTERP_NEAREST",
        layers_select_src="ALL",
        layers_select_dst="NAME",
        mix_mode="REPLACE",
    )
    obj.select_set(False)
    source.select_set(False)
    bind(obj, rig)


def bind(obj, rig):
    obj.parent = rig
    if not any(m.type == "ARMATURE" for m in obj.modifiers):
        modifier = obj.modifiers.new("Rig", "ARMATURE")
        modifier.object = rig


def bind_all_to(obj, bone, rig):
    group = obj.vertex_groups.new(name=PREFIX + bone)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    bind(obj, rig)


def main():
    body_step.enable_mpfb()
    bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "body.blend"))
    body_step.enable_mpfb()

    rig = bpy.data.objects["Rig"]
    body = bpy.data.objects["Body"]
    fit = C.Fit(body, rig)
    print("FIT neck", round(fit.neck, 3), "belt", round(fit.belt, 3), "hem", round(fit.hem, 3))

    # A reference copy of the whole body supplies weights, then is removed.
    reference = G.copy_body(body, "_Reference")
    G.strip_helpers(reference)
    chest = G.Surface([reference])

    parts, trims = C.build_coat(fit, chest)
    shirt = [C.shirt_front(fit, chest), C.shirt_collar(fit)]
    blade, knot = C.tie(fit, chest)
    trousers = C.build_trousers(fit)
    gloves = C.build_gloves(fit)
    shoes = []
    for side, sign in (("Left", 1), ("Right", -1)):
        shoes.extend(C.build_shoe(fit, side, sign))
    hat, band, hat_info = C.build_hat(fit)
    print("HAT", [round(v, 3) for v in hat_info])

    coat_material = M.gabardine()
    wearing = {
        "Metal": M.metal(), "Button": M.horn(),
    }
    for obj in parts + trims:
        if obj.name == "Coat_Buckle":
            obj.data.materials.append(wearing["Metal"])
        elif obj.name == "Coat_Buttons":
            obj.data.materials.append(wearing["Button"])
        else:
            obj.data.materials.append(coat_material)
    shirt_material = M.shirt()
    for obj in shirt:
        obj.data.materials.append(shirt_material)
    tie_material = M.tie()
    blade.data.materials.append(tie_material)
    knot.data.materials.append(tie_material)
    trousers.data.materials.append(M.wool("M_Trousers", (0.03, 0.03, 0.035)))
    gloves.data.materials.append(M.leather("M_Gloves", (0.03, 0.024, 0.021), 0.56))
    shoe_material = M.leather("M_Shoes", (0.017, 0.013, 0.011), 0.46, coat=0.1)
    sole_material = M.leather("M_Sole", (0.02, 0.015, 0.01), 0.6)
    for obj in shoes:
        obj.data.materials.append(sole_material if "Sole" in obj.name else shoe_material)
    hat.data.materials.append(M.silk_hat())
    band.data.materials.append(M.band())

    for obj in parts + trims + shirt + [blade, knot] + shoes:
        attach_to_rig(obj, reference, rig)
    bind(trousers, rig)
    bind(gloves, rig)
    bind_all_to(hat, "Head", rig)
    bind_all_to(band, "Head", rig)

    # Only the head and neck of the bare body show; everything else is under clothes.
    skin = G.copy_body(body, "Head")
    G.strip_helpers(skin)
    # Weights alone leave gaps at the sides of the neck, so keep everything down into the collar.
    skin_co = G.get_co(skin.data)
    into_collar = (skin_co[:, 2] > fit.chin - 0.075) & (abs(skin_co[:, 0]) < 0.13)
    visible = (G.group_weights(skin, [PREFIX + "Head", PREFIX + "Neck"]) > 0.2) | into_collar
    G.delete_vertices(skin, ~visible)
    G.apply_subsurf(skin, 1)
    bind(skin, rig)

    hair = H.build_hair(fit, skin)
    hair.data.materials.clear()
    hair.data.materials.append(M.hair())
    bind(hair, rig)

    for group, name, bone in (("helper-l-eye", "Eye_L", "LeftEye"), ("helper-r-eye", "Eye_R", "RightEye")):
        eye = H.build_eye(body, group, name)
        eye.data.materials.append(M.eye(eye))
        bind_all_to(eye, bone, rig)

    texture_folder = os.path.join(HERE, "tex")
    albedo, rough, normal = S.paint_head(skin, fit, texture_folder)
    skin.data.materials.clear()
    skin.data.materials.append(M.head(albedo, rough, normal))

    bpy.data.objects.remove(reference)
    bpy.data.objects.remove(body)

    total = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == "MESH")
    print("TOTAL faces", total)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "human.blend"))
    # Saved relative, so the textures are still found if this folder moves.
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_mainfile()


if __name__ == "__main__":
    main()
