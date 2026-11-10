"""Tailors the trench coat, shirt, tie, trousers, gloves, shoes and top hat onto the MPFB body."""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

import garment_lib as G
from garment_lib import PREFIX, smoothstep


class Fit:
    """Measurements taken from the rig, so every garment follows the body."""

    def __init__(self, body, rig):
        self.body = body
        self.rig = rig
        self.joint = {
            b.name.replace(PREFIX, ""): rig.matrix_world @ b.head_local for b in rig.data.bones
        }
        self.tail = {
            b.name.replace(PREFIX, ""): rig.matrix_world @ b.tail_local for b in rig.data.bones
        }
        self.neck = self.joint["Neck"].z
        self.belt = self.joint["Spine1"].z - 0.022
        self.hem = self.joint["LeftLeg"].z - 0.012

        source = G.copy_body(body, "_measure")
        G.strip_helpers(source)
        self.points = G.get_co(source.data)
        self.ears = self.points[G.group_weights(source, ["ears"]) > 0.5]
        bpy.data.objects.remove(source)

        p = self.points
        head_y = self.joint["Head"].y
        chin_zone = p[(np.abs(p[:, 0]) < 0.012) & (p[:, 2] > self.neck - 0.02) & (p[:, 1] < head_y - 0.05)]
        self.chin = chin_zone[:, 2].min()
        # Measured above the shoulder slope and clear of the jaw, so this is the neck alone.
        ring = p[(np.abs(p[:, 2] - (self.chin - 0.012)) < 0.004) & (np.abs(p[:, 0]) < 0.082)]
        ring = ring[np.abs(ring[:, 1] - self.joint["Neck"].y) < 0.085]
        self.neck_x = ring[:, 0].max()
        self.neck_y_min, self.neck_y_max = ring[:, 1].min(), ring[:, 1].max()
        self.neck_centre_y = (self.neck_y_min + self.neck_y_max) / 2
        self.neck_y = (self.neck_y_max - self.neck_y_min) / 2

        # The V where the coat opens, shared by the cut and the lapels.
        self.v_top_z = self.chin - 0.022
        self.v_top_x = 0.098
        self.v_bottom_z = self.neck - 0.285

    def v_x(self, z):
        return self.v_top_x * (z - self.v_bottom_z) / (self.v_top_z - self.v_bottom_z)


# -- Coat, upper half ----------------------------------------------------------------------------

def arm_weight(obj):
    names = [PREFIX + side + part for side in ("Left", "Right") for part in ("Arm", "ForeArm")]
    return np.clip(G.group_weights(obj, names), 0.0, 1.0)


# Sleeve radius before ease, in metres: below the shoulder, at the elbow and at the cuff.
SLEEVE_RADII = (0.05, 0.0465, 0.038)


def shape_sleeve(fit, side, sign, co, arm):
    """Makes the sleeve an even tube narrowing gently to the cuff, whatever the arm under it looks like."""
    shoulder = np.array(fit.joint[side + "Arm"])
    elbow = np.array(fit.joint[side + "ForeArm"])
    wrist = np.array(fit.joint[side + "Hand"])
    upper_length = np.linalg.norm(elbow - shoulder)
    fore_length = np.linalg.norm(wrist - elbow)
    upper_line = (elbow - shoulder) / upper_length
    fore_line = (wrist - elbow) / fore_length

    on_upper = np.clip((co - shoulder) @ upper_line, 0.0, upper_length)
    on_fore = np.clip((co - elbow) @ fore_line, 0.0, fore_length)
    upper_point = shoulder + on_upper[:, None] * upper_line
    fore_point = elbow + on_fore[:, None] * fore_line
    use_fore = np.linalg.norm(co - fore_point, axis=1) < np.linalg.norm(co - upper_point, axis=1)
    along = np.where(use_fore, upper_length + on_fore, on_upper)
    centre = np.where(use_fore[:, None], fore_point, upper_point)
    offset = co - centre
    radius = np.linalg.norm(offset, axis=1)

    this_side = (co[:, 0] * sign > 0) & (arm > 0.5)
    total = upper_length + fore_length
    edges = np.linspace(0.0, total, 19)
    middles = (edges[:-1] + edges[1:]) / 2
    measured = np.array([
        radius[this_side & (along >= low) & (along < high)].mean() if (this_side & (along >= low) & (along < high)).any() else np.nan
        for low, high in zip(edges[:-1], edges[1:])
    ])
    known = ~np.isnan(measured)
    measured = np.interp(middles, middles[known], measured[known])
    measured = np.convolve(np.pad(measured, 2, mode="edge"), np.ones(5) / 5, mode="valid")

    near_shoulder, at_elbow, at_cuff = SLEEVE_RADII
    target = np.interp(along, [0.0, 0.09, upper_length, total], [near_shoulder, near_shoulder, at_elbow, at_cuff])
    scale = target / np.interp(along, middles, measured)
    amount = np.clip(arm, 0.0, 1.0) * smoothstep(0.0, 0.07, along) * (co[:, 0] * sign > 0)
    return centre + offset * (1.0 + (scale - 1.0) * amount)[:, None]


def build_coat_upper(fit):
    obj = G.copy_body(fit.body, "Coat_Upper")
    G.strip_helpers(obj)

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    neck_y = fit.joint["Neck"].y
    G.cut(bm, Vector((0, neck_y, fit.chin - 0.03)), Vector((0, -0.38, 0.925)),
          lambda p: p.z > fit.neck - 0.12 and abs(p.x) < 0.17)
    G.cut(bm, Vector((0, 0, fit.belt - 0.012)), Vector((0, 0, -1)),
          lambda p: abs(p.x) < 0.3)
    for side, sign in (("Left", 1), ("Right", -1)):
        wrist = fit.joint[side + "Hand"]
        elbow = fit.joint[side + "ForeArm"]
        direction = (wrist - elbow).normalized()
        G.cut(bm, wrist - direction * 0.012, direction, lambda p: p.x * sign > 0.3)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

    mesh = obj.data
    locked = G.boundary_mask(mesh)
    edges = G.get_edges(mesh)
    co = G.taubin(G.get_co(mesh), edges, locked, 30)
    arm = arm_weight(obj)
    # Arms need far more smoothing: a sleeve is a soft tube and shows no muscle.
    co = G.taubin(co, edges, locked | (arm < 0.4), 120)

    for side, sign in (("Left", 1), ("Right", -1)):
        co = shape_sleeve(fit, side, sign, co, arm)
    G.set_co(mesh, co)

    normals = G.get_normals(mesh)
    x, y, z = co[:, 0], co[:, 1], co[:, 2]

    torso = 0.017 + 0.006 * smoothstep(fit.belt + 0.08, fit.belt + 0.26, z)
    torso += 0.012 * smoothstep(fit.belt + 0.14, fit.belt + 0.03, z)
    torso *= 0.62 + 0.38 * smoothstep(fit.belt - 0.015, fit.belt + 0.05, z)

    shoulder_point = np.array([fit.joint["LeftArm"].x, fit.joint["LeftArm"].y, fit.joint["LeftArm"].z])
    mirrored = np.stack([np.abs(x), y, z], axis=1)
    near_shoulder = np.exp(-(np.linalg.norm(mirrored - shoulder_point, axis=1) / 0.075) ** 2)
    torso += 0.002 * near_shoulder

    elbow = np.array([fit.joint["LeftForeArm"].x, fit.joint["LeftForeArm"].y, fit.joint["LeftForeArm"].z])
    wrist = np.array([fit.joint["LeftHand"].x, fit.joint["LeftHand"].y, fit.joint["LeftHand"].z])
    arm_line = (wrist - elbow) / np.linalg.norm(wrist - elbow)
    along_forearm = np.clip((mirrored - elbow) @ arm_line / np.linalg.norm(wrist - elbow), -1.0, 1.0)
    sleeve = 0.0125 + 0.0065 * np.maximum(along_forearm, 0.0)

    ease = torso * (1.0 - arm) + sleeve * arm
    placed = co + normals * ease[:, None]
    # Bring the shoulder line in, so the coat falls from the shoulder instead of flaring out.
    placed[:, 0] *= 1.0 - 0.055 * near_shoulder
    G.set_co(mesh, placed)

    G.apply_subsurf(obj, 2)
    add_upper_folds(fit, obj)
    return obj


def add_upper_folds(fit, obj):
    mesh = obj.data
    co = G.get_co(mesh)
    normals = G.get_normals(mesh)
    x, y, z = co[:, 0], co[:, 1], co[:, 2]
    arm = arm_weight(obj)
    theta = np.arctan2(x, -(y + 0.01))

    rng = np.random.default_rng(11)
    gather = np.zeros(len(co))
    for k, amp in ((23, 0.0042), (37, 0.0030), (53, 0.0018)):
        drift = rng.uniform(0, 6.28) + 3.0 * (z - fit.belt)
        gather += amp * np.sin(k * theta + drift)
    gather *= np.exp(-np.maximum(z - fit.belt, 0.0) / 0.075) * (1.0 - arm)

    drag = np.zeros(len(co))
    for k, amp, slope in ((6, 0.0042, 2.2), (10, 0.0030, -3.1), (15, 0.0018, 1.4)):
        drag += amp * np.sin(k * theta + slope * z * 6.0 + rng.uniform(0, 6.28))
    drag *= smoothstep(fit.belt + 0.04, fit.belt + 0.2, z) * smoothstep(fit.neck, fit.neck - 0.16, z) * (1.0 - arm)

    sleeve = np.zeros(len(co))
    mirrored = np.stack([np.abs(x), y, z], axis=1)
    shoulder = np.array(fit.joint["LeftArm"])
    elbow = np.array(fit.joint["LeftForeArm"])
    wrist = np.array(fit.joint["LeftHand"])
    upper_line = (elbow - shoulder) / np.linalg.norm(elbow - shoulder)
    along = (mirrored - shoulder) @ upper_line
    length = np.linalg.norm(elbow - shoulder) + np.linalg.norm(wrist - elbow)
    near_elbow = np.exp(-((along - np.linalg.norm(elbow - shoulder)) / 0.09) ** 2)
    roll = np.arctan2(mirrored[:, 1] - shoulder[1], mirrored[:, 2] - shoulder[2])
    for k, amp, twist in ((23, 0.0048, 2.0), (41, 0.0032, -3.0), (67, 0.0016, 3.0)):
        sleeve += amp * np.sin(k * along + twist * roll + rng.uniform(0, 6.28))
    sleeve *= (0.25 + 0.75 * near_elbow) * arm * smoothstep(0.03, 0.14, along) * smoothstep(length, length - 0.1, along)

    G.set_co(mesh, co + normals * (gather + drag + sleeve)[:, None])


# -- Coat, skirt ---------------------------------------------------------------------------------

def body_profile(fit, zs, count=180, axis_y=-0.01):
    """Outer radius of the hips and legs at each height and angle."""
    source = G.copy_body(fit.body, "_profile")
    G.strip_helpers(source)
    names = [PREFIX + n for n in ("Hips", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "Spine", "Spine1")]
    keep = G.group_weights(source, names) > 0.5
    co = G.get_co(source.data)[keep]
    bpy.data.objects.remove(source)

    theta = np.arctan2(co[:, 0], -(co[:, 1] - axis_y))
    radius = np.hypot(co[:, 0], co[:, 1] - axis_y)
    bins = ((theta + math.pi) / (2 * math.pi) * count).astype(int) % count
    result = np.zeros((len(zs), count))
    for k, z in enumerate(zs):
        chosen = np.abs(co[:, 2] - z) < 0.025
        for b in range(count):
            here = radius[chosen & (bins == b)]
            result[k, b] = here.max() if len(here) else 0.0
        row = result[k]
        known = np.nonzero(row)[0]
        if len(known):
            row[:] = np.interp(np.arange(count), known, row[known], period=count)
    return result


def smooth_ring(values, sigma):
    count = values.shape[-1]
    frequencies = np.fft.rfftfreq(count, 1.0 / count)
    kernel = np.exp(-0.5 * (frequencies * 2 * math.pi * sigma / count) ** 2)
    return np.fft.irfft(np.fft.rfft(values, axis=-1) * kernel, n=count, axis=-1)


# Angles of the front overlap. The left front lies over the right, past the middle of the body.
OVER_RIGHT_OF_CENTER = math.radians(27.0)
UNDER_PAST_CENTER = math.radians(32.0)


def build_skirt(fit):
    top = fit.belt + 0.02
    rows = np.linspace(top, fit.hem, 120)
    raw = body_profile(fit, rows)
    spread = np.max([np.roll(raw, shift, axis=1) for shift in range(-9, 10)], axis=0)
    profile = smooth_ring(spread, 4.0) + 0.018
    hang = profile.copy()
    for k in range(1, len(rows)):
        hang[k] = np.maximum(hang[k], hang[k - 1])
    hang = np.stack([np.convolve(np.pad(hang[:, b], 6, mode="edge"), np.ones(13) / 13, mode="valid")
                     for b in range(hang.shape[1])], axis=1)

    hip = fit.joint["Hips"].z - 0.03
    start = UNDER_PAST_CENTER
    end = -(2 * math.pi + OVER_RIGHT_OF_CENTER)
    last_edge = np.linspace(0.0, 0.05, 14) / 0.2
    middle_end = end + last_edge[-1]
    thetas = np.concatenate([np.linspace(start, middle_end, 400, endpoint=False),
                             middle_end - last_edge])
    bins = (thetas + math.pi) / (2 * math.pi) * hang.shape[1]

    rng = np.random.default_rng(5)
    gather_set = [(count, amp, rng.uniform(0, 6.28)) for count, amp in ((24, 0.0034), (38, 0.0026), (57, 0.0015))]
    fold_set = [(count, amp, rng.uniform(0, 6.28), rng.uniform(-1.2, 1.2))
                for count, amp in ((4, 0.013), (7, 0.0105), (11, 0.0072), (17, 0.0038), (26, 0.0018))]
    points = np.empty((len(rows) + 2, len(thetas), 3))
    for k, z in enumerate(rows):
        radius = np.interp(bins % hang.shape[1], np.arange(hang.shape[1]), hang[k], period=hang.shape[1])
        ease = 0.013 + 0.016 * float(smoothstep(top, hip, z))
        drop = float(smoothstep(hip, fit.hem, z))
        flare = (0.024 + 0.022 * np.abs(np.sin(thetas))) * drop ** 1.4

        gather = 0.0
        for count, amp, phase in gather_set:
            gather = gather + amp * np.sin(count * thetas + phase + 2.0 * (z - top))
        gather = gather * math.exp(-(top - z) / 0.10)

        hang_folds = 0.0
        for count, amp, phase, wander in fold_set:
            hang_folds = hang_folds + amp * np.sin(count * thetas + phase + wander * (top - z))
        hang_folds = hang_folds * (0.2 + 0.8 * float(smoothstep(top - 0.08, top - 0.5, z)))

        layer = 0.0065 * smoothstep(end + math.radians(62), end + math.radians(32), thetas)
        layer = layer - 0.007 * smoothstep(start - math.radians(62), start - math.radians(32), thetas)
        edge = -0.0035 * smoothstep(end + 0.016, end, thetas)

        final = radius + ease + flare + gather + hang_folds + layer + edge
        points[k, :, 0] = np.sin(thetas) * final
        points[k, :, 1] = -0.01 - np.cos(thetas) * final
        points[k, :, 2] = z

    inner_radius = np.hypot(points[len(rows) - 1, :, 0], points[len(rows) - 1, :, 1] + 0.01) - 0.008
    for offset, row in ((0.0, len(rows)), (0.02, len(rows) + 1)):
        points[row, :, 0] = np.sin(thetas) * inner_radius
        points[row, :, 1] = -0.01 - np.cos(thetas) * inner_radius
        points[row, :, 2] = fit.hem + offset

    return G.grid_object("Coat_Skirt", points)


# -- Trims on the coat ---------------------------------------------------------------------------

def cut_neckline(fit, obj):
    """Opens the V at the front of the coat, where the shirt and tie show."""
    slope = fit.v_top_x / (fit.v_top_z - fit.v_bottom_z)
    apex = Vector((0.0, 0.0, fit.v_bottom_z))
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    G.cut(bm, apex, Vector((-1.0, 0.0, slope)), lambda p: p.y < 0.03 and p.x >= -0.0005 and p.z > fit.v_bottom_z - 0.01)
    G.cut(bm, apex, Vector((1.0, 0.0, slope)), lambda p: p.y < 0.03 and p.x <= 0.0005 and p.z > fit.v_bottom_z - 0.01)
    under_collar = [f for f in bm.faces if (lambda c: c.y < 0.0 and abs(c.x) < 0.128 and c.z > fit.chin - 0.078)(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=under_collar, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    G.shade_smooth(obj)


def lapel(fit, surface, sign, name):
    n = fit.neck
    low = fit.v_bottom_z - 0.018
    high = fit.chin - 0.086
    inner = [(0.0, low), (max(fit.v_x(high) - 0.016, 0.0), high)]
    outer = [(0.0, low), (0.075, n - 0.235), (0.158, n - 0.135),
             (0.176, n - 0.102), (0.134, high - 0.022), (max(fit.v_x(high) - 0.016, 0.0) + 0.012, high)]

    def project(a, b):
        return surface.front(a * sign, b)

    def height(distance, _x, b):
        roll = 0.004 + 0.007 * float(smoothstep(low, n - 0.17, b)) * float(smoothstep(high, high - 0.05, b))
        return G.rim_height(roll, rim=0.0045, stitch=0.0085)(distance, 0, 0)

    return G.panel(name, inner, outer, project, height, rows=46, cols=18)


def over_panel(fit, surface):
    """A lip along the crossing edge of the double-breasted front, from the belt to the lapel."""
    edge_x = -0.086

    def project(a, b):
        return surface.front(a, b)

    def height(_distance, x, b):
        from_edge = x - np.interp(b, [low, high], [edge_x, tip_x])
        rise = float(smoothstep(0.0, 0.0028, from_edge))
        fall = float(smoothstep(0.075, 0.028, from_edge))
        groove = 0.0006 * math.exp(-(((from_edge - 0.0095) / 0.0022) ** 2))
        ends = float(smoothstep(high, high - 0.02, b))
        return (0.0034 * rise * fall - groove) * ends

    low = fit.belt - 0.012
    high = fit.v_bottom_z + 0.03
    tip_x = -0.006
    left = [(edge_x, low), (tip_x, high)]
    right = [(edge_x + 0.085, low), (tip_x + 0.085, high)]
    return G.panel("Coat_Over", left, right, project, height, rows=30, cols=28, edge=(0.0, 0.001, 0.002, 0.003))


def storm_flap(fit, surface):
    """The gun flap on the wearer's right chest."""
    n = fit.neck

    def project(a, b):
        return surface.front(a, b)

    left = [(-0.215, n - 0.29), (-0.228, n - 0.21), (-0.218, n - 0.13), (-0.2, n - 0.05)]
    right = [(-0.150, n - 0.325), (-0.11, n - 0.30), (-0.098, n - 0.21), (-0.105, n - 0.125), (-0.122, n - 0.05)]
    profile = G.rim_height(0.0045, rim=0.004, stitch=0.0095)

    def height(distance, _x, b):
        return profile(max(distance, 0.012 if b > n - 0.09 else distance), 0, 0) * float(smoothstep(n - 0.05, n - 0.09, b))

    return G.panel("Coat_StormFlap", left, right, project, height, rows=34, cols=20)


def back_yoke(fit, surface):
    n = fit.neck

    def project(a, b):
        return surface.back(a, b)

    half = 0.235
    left = [(-0.05, n - 0.335), (-half, n - 0.255), (-half, n - 0.05)]
    right = [(0.05, n - 0.335), (half, n - 0.255), (half, n - 0.05)]
    return G.panel("Coat_Yoke", left, right, project, G.rim_height(0.0045, rim=0.004, stitch=0.0095), rows=44, cols=36)


BELT_THICKNESS = 0.0055


def belt_ring(fit, surface):
    """Angles around the waist, and how far out from the body's axis the belt sits at each."""
    thetas = np.linspace(0, 2 * math.pi, 200, endpoint=False)
    radii = np.array([np.linalg.norm(surface.around(t, fit.belt)[0][:2] - np.array([0, -0.01])) for t in thetas])
    return thetas, smooth_ring(radii, 3.0) + 0.002


def belt(fit, surface):
    z = fit.belt
    width = 0.054
    thetas, radii = belt_ring(fit, surface)
    out = np.stack([np.sin(thetas), -np.cos(thetas), np.zeros_like(thetas)], axis=1)
    centre = np.array([0.0, -0.01, z])
    path = centre + out * radii[:, None]
    return G.ribbon("Coat_Belt", path, out, width, BELT_THICKNESS, close=True)


def belt_details(fit, surface):
    z = fit.belt
    parts = []
    buckle_y = surface.front(0.0, z)[0][1] - 0.01
    w, h = 0.036, 0.042
    corners = []
    for angle in np.linspace(0, 2 * math.pi, 64, endpoint=False):
        c, s = math.cos(angle), math.sin(angle)
        px = (abs(c) ** 0.45) * (1 if c >= 0 else -1) * w
        pz = (abs(s) ** 0.45) * (1 if s >= 0 else -1) * h
        corners.append((px, buckle_y, z + pz))
    parts.append(G.tube("_buckle", corners, 0.0048, closed=True, segments=10))
    parts.append(G.tube("_prong", [(-w, buckle_y - 0.0005, z), (0.0, buckle_y - 0.006, z), (w, buckle_y - 0.0005, z)], 0.0028, segments=8))
    buckle = G.join("Coat_Buckle", parts)

    # The loose end lies flat along the belt, toward the wearer's right, stopping short of the first loop.
    thetas, radii = belt_ring(fit, surface)
    along = np.linspace(0.0, -0.92, 70)
    radius = np.interp(along % (2 * math.pi), thetas, radii, period=2 * math.pi) + BELT_THICKNESS + 0.0008
    out = np.stack([np.sin(along), -np.cos(along), np.zeros_like(along)], axis=1)
    path = np.array([0.0, -0.01, z]) + out * radius[:, None]
    tail = G.ribbon("Coat_BeltTail", path, out, 0.046, 0.0042)
    return buckle, tail


def belt_loops(fit, surface):
    parts = []
    for theta in (math.radians(62), math.radians(-62), math.radians(118), math.radians(-118)):
        path, normals = [], []
        for dz in np.linspace(-0.034, 0.034, 12):
            point, normal = surface.around(theta, fit.belt + dz)
            path.append(point + normal * (0.008 + 0.004 * (1.0 - (dz / 0.034) ** 2)))
            normals.append(normal)
        parts.append(G.ribbon("_loop", path, normals, 0.014, 0.0028, round_end=False))
    return G.join("Coat_BeltLoops", parts)


def buttons(fit, surface):
    n = fit.neck
    profile = [(0.0, 0.0), (0.0118, 0.0), (0.0132, 0.0016), (0.0122, 0.0038), (0.0098, 0.0036), (0.0084, 0.0024), (0.0, 0.0022)]
    verts, faces = [], []

    def add(point, normal):
        new_verts, new_faces = G.lathe_disc(point, normal, profile, segments=24)
        start = len(verts)
        verts.extend(new_verts)
        faces.extend(tuple(i + start for i in face) for face in new_faces)

    columns = (-0.066, 0.066)
    rows = [fit.belt + 0.058, fit.belt + 0.128, fit.belt - 0.085, fit.belt - 0.18]
    for z in rows:
        for x in columns:
            point, normal = surface.front(x, z)
            add(point + normal * 0.0038, normal)

    obj = G.mesh_object("Coat_Buttons", verts, faces)
    return obj


def soften_line(points, passes=4):
    for _ in range(passes):
        padded = np.pad(points, ((2, 2), (0, 0)), mode="edge")
        kernel = np.array([0.1, 0.2, 0.4, 0.2, 0.1])
        points = np.stack([np.convolve(padded[:, axis], kernel, mode="valid") for axis in range(3)], axis=1)
    return points


def epaulet(fit, surface, sign):
    outer = np.array(fit.joint["LeftArm"]) * [sign, 1, 1] + np.array([-sign * 0.008, 0, 0.09])
    inner = np.array([sign * 0.108, fit.joint["Neck"].y + 0.02, fit.neck + 0.075])
    path, normals = [], []
    for t in np.linspace(0, 1, 34):
        guess = outer + (inner - outer) * t
        guess[2] += 0.05
        point, normal = surface.nearest(guess)
        path.append(point)
        normals.append(normal)
    path, normals = soften_line(np.array(path)), soften_line(np.array(normals))
    path = path + normals * 0.004
    return G.ribbon(f"Coat_Epaulet{sign}", path, normals, 0.05, 0.0048)


def cuff_strap(fit, surface, side, sign):
    elbow = np.array(fit.joint[side + "ForeArm"])
    wrist = np.array(fit.joint[side + "Hand"])
    axis = (wrist - elbow) / np.linalg.norm(wrist - elbow)
    centre = wrist - axis * 0.085
    reference = np.array([0.0, 1.0, 0.0])
    u = np.cross(axis, reference)
    u /= np.linalg.norm(u)
    v = np.cross(axis, u)
    path, normals = [], []
    for angle in np.linspace(0, 2 * math.pi, 64, endpoint=False):
        out = u * math.cos(angle) + v * math.sin(angle)
        point, normal = surface.ray(centre + out * 0.3, -out)
        path.append(point + out * 0.002)
        normals.append(out)
    return G.ribbon(f"Coat_CuffStrap{sign}", path, normals, 0.03, 0.0042, close=True)


def collar(fit, surface, name="Coat_Collar"):
    centre_y = fit.neck_centre_y
    columns = np.linspace(-1.0, 1.0, 141)
    across = np.array([0.0, 0.12, 0.24, 0.36, 0.48, 0.6, 0.72, 0.84, 0.93, 0.975, 1.0])
    grid = np.empty((len(across), len(columns), 3))
    limit = math.radians(146)

    edges = np.empty((len(columns), 3))
    for c, t in enumerate(columns):
        phi = t * limit
        toward_front = abs(t) ** 1.4
        a, b = fit.neck_x + 0.017 + 0.012 * toward_front, fit.neck_y + 0.017
        direction = np.array([math.sin(phi), math.cos(phi), 0.0])
        roll = np.array([a * direction[0], centre_y + b * direction[1], fit.chin + 0.052 - 0.125 * toward_front])
        spread = 0.066 + 0.042 * abs(t) ** 3.0
        guess = roll + direction * spread + np.array([0.0, 0.0, -0.075 - 0.02 * toward_front])
        edge, normal = surface.nearest(guess)
        edges[c] = edge + normal * 0.0145
    for _ in range(3):
        kernel = np.array([0.05, 0.1, 0.2, 0.3, 0.2, 0.1, 0.05])
        pad = np.pad(edges, ((3, 3), (0, 0)), mode="edge")
        edges = np.stack([np.convolve(pad[:, axis], kernel, mode="valid") for axis in range(3)], axis=1)

    for c, t in enumerate(columns):
        phi = t * limit
        toward_front = abs(t) ** 1.4
        a, b = fit.neck_x + 0.017 + 0.012 * toward_front, fit.neck_y + 0.017
        direction = np.array([math.sin(phi), math.cos(phi), 0.0])
        roll = np.array([a * direction[0], centre_y + b * direction[1], fit.chin + 0.052 - 0.125 * toward_front])
        base = roll + np.array([0.0, 0.0, -0.055]) - direction * 0.012

        edge = edges[c]
        lift = 0.024 - 0.008 * toward_front

        for r, s in enumerate(across):
            if s <= 0.36:
                p = base + (roll - base) * (s / 0.36)
            else:
                f = (s - 0.36) / 0.64
                control = roll + (edge - roll) * 0.45 + np.array([0.0, 0.0, lift]) + direction * 0.01
                p = (1 - f) ** 2 * roll + 2 * (1 - f) * f * control + f ** 2 * edge
            grid[r, c] = p

    # Lift any part of the fold that dips into the coat beneath it.
    fold_start = int(np.searchsorted(across, 0.36)) + 1
    clearance = 0.0115
    for r in range(fold_start, len(across)):
        for c in range(len(columns)):
            location, normal = surface.nearest(grid[r, c])
            height_above = (grid[r, c] - location) @ normal
            if height_above < clearance:
                grid[r, c] = location + normal * clearance
    grid[fold_start:] = G.blur_grid(grid[fold_start:], 2)
    return G.grid_object(name, grid)


# -- Under the coat ------------------------------------------------------------------------------

def shirt_front(fit, chest):
    n = fit.neck
    top = fit.chin - 0.04

    def project(a, b):
        return chest.front(a, b)

    left = [(-0.135, n - 0.34), (-0.135, top)]
    right = [(0.135, n - 0.34), (0.135, top)]
    return G.panel("Shirt_Front", left, right, project, lambda d, a, b: 0.0075, rows=40, cols=44)


def shirt_collar(fit):
    centre_y = fit.neck_centre_y
    columns = np.linspace(-1.0, 1.0, 121)
    across = np.array([0.0, 0.12, 0.3, 0.42, 0.5, 0.62, 0.76, 0.9, 0.97, 1.0])
    grid = np.empty((len(across), len(columns), 3))
    limit = math.radians(162)
    for c, t in enumerate(columns):
        phi = t * limit
        direction = np.array([math.sin(phi), math.cos(phi), 0.0])
        toward_front = abs(t) ** 2.2
        ring = np.array([(fit.neck_x + 0.006) * direction[0], centre_y + (fit.neck_y + 0.006) * direction[1],
                         fit.chin + 0.015 - 0.042 * toward_front])
        base = ring + np.array([0.0, 0.0, -0.05]) - direction * 0.003
        tip_drop = 0.043 + 0.021 * abs(t) ** 7
        tip_out = 0.008 + 0.012 * abs(t) ** 5
        edge = ring + direction * tip_out + np.array([0.0, 0.0, -tip_drop])
        if abs(t) > 0.82:
            edge[1] -= 0.022 * (abs(t) - 0.82) / 0.18
        for r, s in enumerate(across):
            if s <= 0.42:
                p = base + (ring - base) * (s / 0.42) + direction * 0.002
            else:
                f = (s - 0.42) / 0.58
                control = ring + direction * 0.009 + np.array([0.0, 0.0, 0.004])
                p = (1 - f) ** 2 * ring + 2 * (1 - f) * f * control + f ** 2 * edge
            grid[r, c] = p
    return G.grid_object("Shirt_Collar", grid)


def tie(fit, chest):
    n = fit.neck

    def project(a, b):
        return chest.front(a, b)

    knot_z = fit.chin - 0.058
    left = [(-0.039, n - 0.36), (-0.030, n - 0.20), (-0.019, knot_z - 0.05), (-0.015, knot_z)]
    right = [(0.039, n - 0.36), (0.030, n - 0.20), (0.019, knot_z - 0.05), (0.015, knot_z)]
    blade = G.panel("Tie_Blade", left, right, project, G.rim_height(0.0115, rim=0.002), rows=36, cols=16)

    centre, normal = chest.front(0.0, knot_z)
    centre = centre + np.array([0.0, -0.017, 0.0])
    rows = 18
    cols = 28
    grid = np.empty((rows, cols, 3))
    for r in range(rows):
        v = r / (rows - 1)
        half_height = 0.022
        z = centre[2] + (v - 0.5) * 2 * half_height
        half_width = 0.0185 + 0.0115 * v
        depth = 0.012 * math.sin(math.pi * min(v * 1.05, 1.0)) ** 0.6 + 0.001
        for c in range(cols):
            angle = 2 * math.pi * c / cols
            grid[r, c] = (centre[0] + math.cos(angle) * half_width, centre[1] + math.sin(angle) * depth, z)
    knot = G.grid_object("_knot", grid)
    bm = bmesh.new()
    bm.from_mesh(knot.data)
    bm.verts.ensure_lookup_table()
    bm.faces.new([bm.verts[i] for i in range(cols)])
    bm.faces.new([bm.verts[(rows - 1) * cols + i] for i in range(cols)])
    bm.to_mesh(knot.data)
    bm.free()
    G.shade_smooth(knot)
    knot.name = "Tie_Knot"
    return blade, knot


# -- Legs and feet -------------------------------------------------------------------------------

def build_trousers(fit):
    obj = G.copy_body(fit.body, "Trousers")
    G.strip_helpers(obj)
    arms = G.group_weights(obj, [PREFIX + s + p for s in ("Left", "Right")
                                 for p in ("Arm", "ForeArm", "Hand", "Shoulder")])
    hands = np.array([v.co.x for v in obj.data.vertices])
    G.delete_vertices(obj, (arms > 0.35) | (np.abs(hands) > 0.3))

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    G.cut(bm, Vector((0, 0, fit.belt - 0.02)), Vector((0, 0, 1)), lambda p: True)
    ankle = fit.joint["LeftFoot"].z + 0.012
    G.cut(bm, Vector((0, 0, ankle)), Vector((0, 0, -1)), lambda p: True)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

    mesh = obj.data
    locked = G.boundary_mask(mesh)
    co = G.taubin(G.get_co(mesh), G.get_edges(mesh), locked, 40)
    G.set_co(mesh, co)
    normals = G.get_normals(mesh)
    z = co[:, 2]
    knee = fit.joint["LeftLeg"].z
    ease = 0.012 + 0.005 * smoothstep(knee + 0.12, knee - 0.2, z) + 0.003 * smoothstep(0.16, ankle, z)
    G.set_co(mesh, co + normals * ease[:, None])
    G.apply_subsurf(obj, 2)

    mesh = obj.data
    co = G.get_co(mesh)
    normals = G.get_normals(mesh)
    x, y, z = co[:, 0], co[:, 1], co[:, 2]
    centres = np.where(x > 0, 1.0, -1.0) * np.interp(z, [ankle, knee], [fit.joint["LeftFoot"].x, fit.joint["LeftLeg"].x])
    angle = np.arctan2(x - centres, -(y + 0.01))

    crease = 0.0035 * np.exp(-(angle / 0.1) ** 2) * smoothstep(fit.hem + 0.2, fit.hem - 0.05, z)
    breaks = 0.0
    for k, amp in ((55, 0.0022), (85, 0.0013)):
        breaks = breaks + amp * np.sin(k * z + 2.0 * np.sin(angle * 2.0) + k * 0.2)
    breaks = breaks * smoothstep(0.22, ankle + 0.015, z)
    G.set_co(mesh, co + normals * (crease + breaks)[:, None])
    return obj


def build_gloves(fit):
    obj = G.copy_body(fit.body, "Gloves")
    G.strip_helpers(obj)
    names = [PREFIX + s + "Hand" + f + str(i) for s in ("Left", "Right") for f in G.FINGERS for i in (1, 2, 3)]
    names += [PREFIX + s + "Hand" for s in ("Left", "Right")]
    weight = G.group_weights(obj, names)
    co = G.get_co(obj.data)
    up_the_wrist = np.zeros(len(co), dtype=bool)
    for side, sign in (("Left", 1), ("Right", -1)):
        wrist = np.array(fit.joint[side + "Hand"])
        elbow = np.array(fit.joint[side + "ForeArm"])
        line = (wrist - elbow) / np.linalg.norm(wrist - elbow)
        before_wrist = (wrist - co) @ line
        near_axis = np.linalg.norm((co - wrist) - ((co - wrist) @ line)[:, None] * line, axis=1) < 0.075
        up_the_wrist |= (co[:, 0] * sign > 0.2) & (before_wrist >= -0.02) & (before_wrist < 0.085) & near_axis
    G.delete_vertices(obj, (weight < 0.4) & ~up_the_wrist)
    mesh = obj.data
    normals = G.get_normals(mesh)
    co = G.get_co(mesh)
    G.set_co(mesh, co + normals * 0.0014)
    G.apply_subsurf(obj, 1)
    return obj


def build_shoe(fit, side, sign):
    foot = fit.joint[side + "Foot"]
    toe = fit.tail[side + "ToeBase"]
    heel_y = foot.y + 0.062
    toe_y = toe.y - 0.018
    length = heel_y - toe_y
    x0 = foot.x + 0.003 * sign

    sections = np.concatenate([np.linspace(0.0, 0.08, 7, endpoint=False), np.linspace(0.08, 0.86, 30, endpoint=False),
                               np.linspace(0.86, 1.0, 16)])
    around = 40
    points = np.empty((len(sections), around, 3))
    for i, s in enumerate(sections):
        back = math.sqrt(max(min(s / 0.08, 1.0), 0.0) * (2 - min(s / 0.08, 1.0)))
        front = math.sqrt(max(1.0 - ((s - 0.72) / 0.28) ** 2, 0.0)) if s > 0.72 else 1.0
        width_profile = 0.039 + 0.015 * float(smoothstep(0.1, 0.62, s)) - 0.006 * float(smoothstep(0.72, 1.0, s))
        half = width_profile * back * front + 0.0015
        offset_x = -sign * 0.006 * float(smoothstep(0.5, 1.0, s))

        top = float(np.interp(s, [0.0, 0.05, 0.2, 0.38, 0.62, 0.84, 1.0], [0.082, 0.098, 0.112, 0.098, 0.064, 0.05, 0.03]))
        top = 0.013 + (top - 0.013) * math.sqrt(back) * front ** 0.45 + 0.0005
        y = heel_y - length * s
        bottom = 0.012 + 0.013 * float(smoothstep(0.32, 0.26, s))

        for j in range(around):
            angle = 2 * math.pi * j / around
            c, sn = math.cos(angle), math.sin(angle)
            if sn >= 0:
                px = (abs(c) ** 0.72) * (1 if c >= 0 else -1) * half
                pz = bottom + (sn ** 0.82) * (top + bottom * 0.2 - bottom)
            else:
                px = (abs(c) ** 0.4) * (1 if c >= 0 else -1) * half
                pz = bottom + sn * 0.0015
            points[i, j] = (x0 + offset_x + px, y, pz)
    points[0] = points[0].mean(axis=0)
    points[-1] = points[-1].mean(axis=0)
    upper = G.grid_object(f"Shoe_Upper{sign}", points, close_columns=True)

    outline = 44
    sole = np.empty((6, outline * 2 - 2, 3))
    positions = np.linspace(0.0, 1.0, outline)
    half_widths = []
    for s in positions:
        back = math.sqrt(max(min(s / 0.08, 1.0), 0.0) * (2 - min(s / 0.08, 1.0)))
        front = math.sqrt(max(1.0 - ((s - 0.72) / 0.28) ** 2, 0.0)) if s > 0.72 else 1.0
        width_profile = 0.039 + 0.015 * float(smoothstep(0.1, 0.62, s)) - 0.006 * float(smoothstep(0.72, 1.0, s))
        half_widths.append(width_profile * back * front + 0.0045)
    half_widths = np.array(half_widths)
    ring_x = np.concatenate([half_widths, -half_widths[-2:0:-1]])
    ring_s = np.concatenate([positions, positions[-2:0:-1]])
    offsets = -sign * 0.006 * smoothstep(0.5, 1.0, ring_s)
    y_values = heel_y - length * ring_s
    z_top = 0.0135 + 0.012 * smoothstep(0.32, 0.26, ring_s)
    layers = [
        (0.0, 0.0), (-0.0012, 0.0), (0.0, -0.0014), (0.0, z_top - 0.0014),
        (-0.0012, z_top), (-0.003, z_top),
    ]
    for r, (inset, z) in enumerate(layers):
        factor = 1.0 + inset / np.maximum(np.abs(ring_x), 0.02)
        sole[r, :, 0] = x0 + offsets + ring_x * factor
        sole[r, :, 1] = y_values
        sole[r, :, 2] = z if r != 0 else 0.0
    sole[0, :, 2] = 0.0
    sole[1, :, 2] = 0.0
    sole_object = G.grid_object(f"Shoe_Sole{sign}", sole, close_columns=True)
    bm = bmesh.new()
    bm.from_mesh(sole_object.data)
    bm.verts.ensure_lookup_table()
    bm.faces.new([bm.verts[i] for i in range(sole.shape[1])])
    bm.to_mesh(sole_object.data)
    bm.free()
    G.shade_smooth(sole_object)
    return upper, sole_object


# -- Hat -----------------------------------------------------------------------------------------

def head_ellipse(fit, z):
    points = G.get_co(head_mesh(fit))
    slice_ = points[np.abs(points[:, 2] - z) < 0.006]
    cx = (slice_[:, 0].max() + slice_[:, 0].min()) / 2
    cy = (slice_[:, 1].max() + slice_[:, 1].min()) / 2
    a = (slice_[:, 0].max() - slice_[:, 0].min()) / 2
    b = (slice_[:, 1].max() - slice_[:, 1].min()) / 2
    return cx, cy, a, b


_head_mesh = {}


def head_mesh(fit):
    if "mesh" not in _head_mesh:
        obj = G.copy_body(fit.body, "_head")
        G.strip_helpers(obj)
        _head_mesh["mesh"] = obj.data.copy()
        bpy.data.objects.remove(obj)
    return _head_mesh["mesh"]


def build_hat(fit):
    head_top = G.get_co(head_mesh(fit))[:, 2].max()
    seat = head_top - 0.074
    cx, cy, a, b = head_ellipse(fit, seat)
    a += 0.008
    b += 0.008

    profile = [
        (0.0, 0.002, -0.0015),
        (0.015, 0.002, -0.0025),
        (0.04, 0.002, -0.003),
        (0.056, 0.002, -0.0005),
        (0.0635, 0.002, 0.0035),
        (0.0665, 0.002, 0.009),
        (0.066, 0.002, 0.014),
        (0.0615, 0.002, 0.0165),
        (0.054, 0.002, 0.0135),
        (0.038, 0.002, 0.0085),
        (0.018, 0.002, 0.007),
        (0.0055, 0.002, 0.009),
        (0.0028, 0.002, 0.0135),
        (0.0018, 0.002, 0.03),
        (0.0024, 0.002, 0.052),
        (0.0058, 0.002, 0.08),
        (0.0108, 0.002, 0.108),
        (0.0168, 0.002, 0.135),
        (0.0222, 0.002, 0.1565),
        (0.0245, 0.002, 0.1625),
        (0.0225, 0.002, 0.1672),
        (0.016, 0.002, 0.1695),
    ]
    cols = 96
    rows = len(profile) + 5
    grid = np.empty((rows, cols, 3))
    phis = np.linspace(0, 2 * math.pi, cols, endpoint=False)
    for j, phi in enumerate(phis):
        sn, cs = math.sin(phi), math.cos(phi)
        for r, (extra, gap, z) in enumerate(profile):
            brim_part = float(smoothstep(0.015, 0.06, extra)) if z < 0.02 else 0.0
            curl = 0.024 * sn * sn * brim_part * (1.0 if extra > 0.02 else 0.4)
            dip = -0.006 * cs * cs * brim_part
            grid[r, j] = (cx + (a + extra + gap) * sn, cy - (b + extra + gap) * cs, seat + z + curl + dip)
        top_extra = profile[-1][0] + 0.002
        top_z = seat + profile[-1][2]
        for r, fraction in enumerate((0.8, 0.6, 0.4, 0.18, 0.0)):
            grid[len(profile) + r, j] = (
                cx + (a + top_extra) * sn * fraction,
                cy - (b + top_extra) * cs * fraction,
                top_z + 0.0015 * (1.0 - fraction),
            )
    hat = G.grid_object("Hat", grid, close_columns=True)

    band_rows = np.array([
        (0.0, 0.009), (0.0028, 0.012), (0.0036, 0.016), (0.0038, 0.03), (0.0044, 0.04),
        (0.0056, 0.0465), (0.0058, 0.049), (0.0, 0.05),
    ])
    grid = np.empty((len(band_rows), cols, 3))
    for j, phi in enumerate(phis):
        sn, cs = math.sin(phi), math.cos(phi)
        for r, (extra, z) in enumerate(band_rows):
            crown = float(np.interp(z, [0.0135, 0.03, 0.052], [0.0028, 0.0018, 0.0024])) + 0.002
            grid[r, j] = (cx + (a + crown + extra) * sn, cy - (b + crown + extra) * cs, seat + z)
    band = G.grid_object("Hat_Band", grid, close_columns=True)
    return hat, band, (cx, cy, a, b, seat)


def build_coat(fit, chest):
    upper = build_coat_upper(fit)
    skirt = build_skirt(fit)
    surface = G.Surface([upper, skirt])
    parts = [
        upper,
        skirt,
        over_panel(fit, surface),
        storm_flap(fit, surface),
        back_yoke(fit, surface),
        lapel(fit, surface, 1, "Coat_LapelL"),
        lapel(fit, surface, -1, "Coat_LapelR"),
        collar(fit, surface),
        belt(fit, surface),
        belt_loops(fit, surface),
        epaulet(fit, surface, 1),
        epaulet(fit, surface, -1),
        cuff_strap(fit, surface, "Left", 1),
        cuff_strap(fit, surface, "Right", -1),
    ]
    cut_neckline(fit, upper)
    buckle, tail = belt_details(fit, surface)
    trims = [buckle, tail, buttons(fit, surface)]
    return parts, trims
