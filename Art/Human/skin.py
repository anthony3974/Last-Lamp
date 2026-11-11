"""Paints the head's colour, roughness and normal textures from its 3D position on the head."""
import os

import bpy
import numpy as np

import garment_lib as G
import head as H
from garment_lib import smoothstep

SIZE = 2048
MPFB_TEXTURES = "/home/anthony/.config/blender/5.2/extensions/blender_org/mpfb/data/textures"


def rasterize(obj, size, crop):
    """For every texel of the cropped UV tile, finds the 3D position, normal and original UV."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    tris = mesh.loop_triangles
    tri_loops = np.empty(len(tris) * 3, dtype=np.int32)
    tri_verts = np.empty(len(tris) * 3, dtype=np.int32)
    tris.foreach_get("loops", tri_loops)
    tris.foreach_get("vertices", tri_verts)
    tri_loops = tri_loops.reshape(-1, 3)
    tri_verts = tri_verts.reshape(-1, 3)

    uv = np.empty(len(mesh.loops) * 2, dtype=np.float32)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2).astype(np.float64)
    co = G.get_co(mesh)
    normals = G.get_normals(mesh)

    (u0, v0), (u1, v1) = crop
    pixel = (uv - np.array([u0, v0])) / np.array([u1 - u0, v1 - v0]) * size

    position = np.zeros((size, size, 3))
    normal = np.zeros((size, size, 3))
    original = np.zeros((size, size, 2))
    mask = np.zeros((size, size), dtype=bool)
    for loops, verts in zip(tri_loops, tri_verts):
        a, b, c = pixel[loops]
        low = np.floor(np.minimum(np.minimum(a, b), c)).astype(int)
        high = np.ceil(np.maximum(np.maximum(a, b), c)).astype(int)
        low = np.clip(low, 0, size - 1)
        high = np.clip(high, 0, size - 1)
        if (high < low).any():
            continue
        xs, ys = np.meshgrid(np.arange(low[0], high[0] + 1) + 0.5, np.arange(low[1], high[1] + 1) + 0.5)
        denominator = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(denominator) < 1e-9:
            continue
        w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / denominator
        w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / denominator
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
        if not inside.any():
            continue
        rows = (ys[inside]).astype(int)
        cols = (xs[inside]).astype(int)
        weights = np.stack([w0[inside], w1[inside], w2[inside]], axis=1)
        position[rows, cols] = weights @ co[verts]
        normal[rows, cols] = weights @ normals[verts]
        original[rows, cols] = weights @ uv[loops]
        mask[rows, cols] = True
    return position, normal, original, mask


def dilate(values, mask, passes=8):
    """Grows painted colour outward past UV island edges, so filtering can't pull in black."""
    values = values.copy()
    mask = mask.copy()
    for _ in range(passes):
        total = np.zeros(values.shape)
        count = np.zeros(mask.shape)
        for shift_row, shift_col in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            total += np.roll(np.roll(values * (mask[..., None] if values.ndim == 3 else mask), shift_row, 0), shift_col, 1)
            count += np.roll(np.roll(mask, shift_row, 0), shift_col, 1)
        grow = (~mask) & (count > 0)
        divided = total[grow] / (count[grow][:, None] if values.ndim == 3 else count[grow])
        values[grow] = divided
        mask |= grow
    return values


def noise(size, beta, seed):
    """Fractal noise, 0 mean and unit spread. Larger beta gives broader blotches."""
    rng = np.random.default_rng(seed)
    white = rng.normal(size=(size, size))
    fx = np.fft.fftfreq(size)
    fy = np.fft.rfftfreq(size)
    radius = np.hypot(*np.meshgrid(fx, fy, indexing="ij"))
    radius[0, 0] = 1.0
    field = np.fft.irfft2(np.fft.rfft2(white) / radius ** (beta / 2.0), s=(size, size))
    return (field - field.mean()) / field.std()


def blur(field, sigma):
    """Gaussian blur, in texels. Padded first, so one edge of the tile can't bleed into the opposite edge."""
    pad = int(sigma * 4) + 1
    padded = np.pad(field, pad, mode="constant")
    fx = np.fft.fftfreq(padded.shape[0])
    fy = np.fft.rfftfreq(padded.shape[1])
    radius2 = fx[:, None] ** 2 + fy[None, :] ** 2
    kernel = np.exp(-2.0 * (np.pi * sigma) ** 2 * radius2)
    result = np.fft.irfft2(np.fft.rfft2(padded) * kernel, s=padded.shape)
    return result[pad:-pad, pad:-pad]


def position_noise(position, mask, seed, wavelengths=(0.09, 0.05, 0.028), waves=16):
    """Broad blotches worked out from 3D position, so they carry straight across UV seams."""
    rng = np.random.default_rng(seed)
    total = np.zeros(position.shape[:2])
    for wavelength in wavelengths:
        directions = rng.normal(size=(waves, 3))
        directions /= np.linalg.norm(directions, axis=1, keepdims=True)
        for direction in directions:
            total += wavelength * np.sin((position @ direction) * 2.0 * np.pi / wavelength + rng.uniform(0, 6.28))
    return (total - total[mask].mean()) / total[mask].std()


def load_mask(name):
    path = os.path.join(MPFB_TEXTURES, f"mpfb_{name}.jpg")
    image = bpy.data.images.load(path, check_existing=False)
    w, h = image.size
    pixels = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape(h, w, 4)[:, :, 0].astype(np.float64)


def sample(mask, uv):
    h, w = mask.shape
    x = np.clip(uv[..., 0] * (w - 1), 0, w - 1).astype(int)
    y = np.clip(uv[..., 1] * (h - 1), 0, h - 1).astype(int)
    return mask[y, x]


def gaussian(position, centre, radii):
    offset = (position - np.array(centre)) / np.array(radii)
    return np.exp(-(offset ** 2).sum(axis=-1))


def to_srgb(linear):
    linear = np.clip(linear, 0.0, 1.0)
    return np.where(linear <= 0.0031308, linear * 12.92, 1.055 * linear ** (1 / 2.4) - 0.055)


def save(name, pixels, folder):
    height, width = pixels.shape[:2]
    rgba = np.ones((height, width, 4), dtype=np.float32)
    rgba[..., :3] = pixels if pixels.ndim == 3 else pixels[..., None]
    image = bpy.data.images.new(name, width, height, alpha=False, float_buffer=False)
    image.pixels.foreach_set(rgba.ravel())
    path = os.path.join(folder, f"{name}.png")
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    return path


def brow_mask(fit, position, seed):
    """Short dark hairs running along each brow."""
    eye = np.array(fit.joint["LeftEye"])
    relative_x = np.abs(position[..., 0])
    z = position[..., 2]
    front = position[..., 1] < eye[1] + 0.03

    inner, peak, outer = (0.0135, eye[2] + 0.0175), (0.045, eye[2] + 0.0235), (0.0615, eye[2] + 0.017)
    centre_z = np.interp(relative_x, [inner[0], peak[0], outer[0]], [inner[1], peak[1], outer[1]])
    thickness = np.interp(relative_x, [inner[0], inner[0] + 0.012, peak[0], outer[0]], [0.0042, 0.0052, 0.0042, 0.0026])
    across = (z - centre_z) / thickness

    shape = np.exp(-np.abs(across) ** 3) * smoothstep(inner[0] - 0.002, inner[0] + 0.004, relative_x)
    shape *= smoothstep(outer[0] + 0.001, outer[0] - 0.012, relative_x)

    rng = np.random.default_rng(seed)
    strokes = np.zeros(z.shape)
    for frequency, amount, phase in ((5200, 0.42, rng.uniform(0, 6)), (8300, 0.34, rng.uniform(0, 6)), (12100, 0.24, rng.uniform(0, 6))):
        slope = 1.2 - 1.6 * (relative_x - inner[0]) / (outer[0] - inner[0])
        strokes += amount * np.sin(frequency * (relative_x * np.sign(position[..., 0] + 1e-9) * 1.0 + slope * (z - centre_z)) + phase)
    return np.clip(shape * (0.86 + 0.42 * strokes), 0.0, 1.0) * front


def paint_head(obj, fit, folder):
    mesh = obj.data
    uv = np.empty(len(mesh.loops) * 2, dtype=np.float32)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    margin = 0.004
    crop = ((uv[:, 0].min() - margin, uv[:, 1].min() - margin), (uv[:, 0].max() + margin, uv[:, 1].max() + margin))

    position, normal, original, mask = rasterize(obj, SIZE, crop)

    # Region masks that come with MPFB, in its own UV layout.
    # MPFB's masks carry faint stray marks elsewhere on the head, so only the solid parts count.
    lips = smoothstep(0.2, 0.6, sample(load_mask("lips"), original))
    eyelids = smoothstep(0.2, 0.6, sample(load_mask("eyelids"), original))
    ears = smoothstep(0.2, 0.6, sample(load_mask("ears"), original))

    eye = np.array(fit.joint["LeftEye"])
    eye_mirror = np.array([-eye[0], eye[1], eye[2]])
    nose_tip = np.array([0.0, eye[1] - 0.07, eye[2] - 0.043])
    mouth = np.array([0.0, eye[1] - 0.028, eye[2] - 0.08])

    blotch = position_noise(position, mask, 1)
    fine = noise(SIZE, 1.1, 2)
    pores = noise(SIZE, 0.15, 3)

    # Base colour, in linear light: a warm, slightly weathered complexion.
    colour = np.empty((SIZE, SIZE, 3))
    colour[...] = (0.585, 0.385, 0.295)
    colour *= (1.0 + 0.055 * blotch[..., None] + 0.02 * fine[..., None])

    # Blood: warmer over cheeks, nose and ears.
    red = (
        0.52 * gaussian(position, (eye[0] + 0.015, eye[1] - 0.012, eye[2] - 0.042), (0.031, 0.04, 0.026))
        + 0.52 * gaussian(position, (eye_mirror[0] - 0.015, eye[1] - 0.012, eye[2] - 0.042), (0.031, 0.04, 0.026))
        + 0.62 * gaussian(position, nose_tip, (0.019, 0.02, 0.022))
        + 0.62 * np.clip(ears, 0, 1)
    )
    red = np.clip(red * (1.0 + 0.2 * blotch), 0.0, 1.0)
    colour = colour * (1 - red[..., None] * 0.28) + np.array([0.56, 0.23, 0.18]) * red[..., None] * 0.28

    # Shadow around the eyes.
    socket = gaussian(position, eye, (0.024, 0.03, 0.02)) + gaussian(position, eye_mirror, (0.024, 0.03, 0.02))
    colour *= (1.0 - 0.2 * np.clip(socket, 0, 1)[..., None])
    colour = colour * (1 - 0.3 * eyelids[..., None]) + np.array([0.42, 0.24, 0.22]) * 0.3 * eyelids[..., None]

    # A shaved beard: a cool shadow with fine stubble over the jaw and upper lip.
    z = position[..., 2]
    y = position[..., 1]
    x = position[..., 0]
    jawline = eye[2] - 0.052 + 0.045 * np.clip((np.abs(x) - 0.012) / 0.07, 0, 1)
    beard = smoothstep(jawline + 0.012, jawline - 0.015, z) * smoothstep(fit.chin - 0.022, fit.chin + 0.005, z)
    beard *= smoothstep(eye[1] + 0.09, eye[1] + 0.045, y)
    beard = np.clip(beard + 0.55 * gaussian(position, mouth + np.array([0.0, 0.0, 0.02]), (0.03, 0.03, 0.011)), 0.0, 1.0)
    beard *= 1.0 - np.clip(lips * 3.0, 0, 1)
    stubble = np.clip(0.5 + 0.5 * pores, 0.0, 1.0) ** 3
    colour = colour * (1.0 - beard[..., None] * (0.08 + 0.13 * stubble[..., None]))
    colour = colour * (1.0 - beard[..., None] * np.array([0.0, 0.02, 0.0]))

    # Lips.
    lip_mask = np.clip(lips * 1.25, 0, 1)
    colour = colour * (1 - lip_mask[..., None] * 0.75) + np.array([0.3, 0.115, 0.1]) * lip_mask[..., None] * 0.75

    # Brows.
    brows = brow_mask(fit, position, 9)
    colour = colour * (1 - brows[..., None] * 0.92) + np.array([0.026, 0.018, 0.014]) * brows[..., None] * 0.92

    # Hair shadow on the scalp, so the hairline melts into the hair.
    region = H.hair_region(fit, position.reshape(-1, 3), keep_off_ears=False).reshape(SIZE, SIZE) & (ears < 0.5)
    # Only well inside the hair, so nothing shows below where the hair stops.
    hairline = smoothstep(0.55, 0.97, blur((region & mask).astype(np.float64), 14.0))
    colour = colour * (1 - 0.5 * hairline[..., None]) + np.array([0.05, 0.038, 0.03]) * 0.5 * hairline[..., None]

    # Roughness: oilier down the nose, satin lips.
    roughness = 0.56 + 0.03 * fine - 0.13 * np.clip(gaussian(position, nose_tip, (0.03, 0.05, 0.05)), 0, 1)
    roughness = roughness * (1 - 0.4 * lip_mask) + 0.44 * 0.4 * lip_mask + 0.08 * beard
    roughness = np.clip(roughness, 0.2, 0.9)

    # Height: pores, then normal map from height.
    height = 0.00007 * np.clip(0.8 * pores + 0.3 * fine, -3, 3)
    height += 0.0004 * brows
    # Texels per metre of the crop, from how far apart neighbouring texels are on the head.
    step = max(float(np.median(np.linalg.norm(np.diff(position[mask][:2000], axis=0), axis=1))), 2e-4)
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / (2 * step)
    gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) / (2 * step)
    nx, ny, nz = -gx, -gy, np.ones_like(gx)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    normals = np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5

    colour = dilate(colour, mask, 12)
    roughness = dilate(roughness, mask, 12)
    normals = dilate(normals, mask, 12)

    os.makedirs(folder, exist_ok=True)
    albedo = save("Head_Albedo", to_srgb(colour), folder)
    rough = save("Head_Roughness", roughness, folder)
    bump = save("Head_Normal", normals, folder)

    # Move the head's UVs to fill its own texture tile.
    (u0, v0), (u1, v1) = crop
    fitted = (uv - np.array([u0, v0])) / np.array([u1 - u0, v1 - v0])
    mesh.uv_layers.active.data.foreach_set("uv", fitted.astype(np.float32).ravel())
    return albedo, rough, bump
