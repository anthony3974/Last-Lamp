# Builds the realistic human design model on a MakeHuman (MPFB) body.
# Needs the MPFB extension in Blender and the CC0 "makehuman_system_assets" pack unzipped at HUMAN_ASSETS
# (default ~/.cache/last-lamp/mh_assets). PLAN.md, "Realistic human", has the install steps.
# Run: blender -b --factory-startup --python human.py -- [front three side noir face]
# HUMAN_OUT sets where renders go; human.blend is saved next to this script on a full build.
import math, os, sys
import bpy, bmesh, numpy as np
from mathutils import Vector, Matrix, geometry

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get('HUMAN_OUT', HERE)
SAMPLES = int(os.environ.get('HUMAN_SAMPLES', 96))
ASSETS = os.path.expanduser(os.environ.get('HUMAN_ASSETS', '~/.cache/last-lamp/mh_assets'))
want = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['front', 'three', 'side', 'noir', 'face']
FULL = set(want) >= {'front', 'three', 'side', 'noir', 'face'}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module='bl_ext.blender_org.mpfb')
from bl_ext.blender_org.mpfb.services.humanservice import HumanService
from bl_ext.blender_org.mpfb.services.targetservice import TargetService
from bl_ext.blender_org.mpfb.services.locationservice import LocationService
TARGETS = LocationService.get_mpfb_data('targets')
HEIGHT = 1.80   # top of the skull, matching the CharacterController

# ---------- small helpers ----------
def sstep(x, a, b):
    t = np.clip((np.asarray(x, float)-a)/(b-a), 0, 1)
    return t*t*(3-2*t)

def vnoise(x, y=0.0, seed=0):
    """Smooth value noise in -1..1, vectorised."""
    x = np.asarray(x, float); y = np.zeros_like(x)+y
    xi, yi = np.floor(x), np.floor(y)
    fx, fy = x-xi, y-yi
    def h(i, j):
        v = np.sin(i*127.1+j*311.7+seed*74.7)*43758.5453
        return (v-np.floor(v))*2-1
    sx, sy = fx*fx*(3-2*fx), fy*fy*(3-2*fy)
    return (h(xi, yi)*(1-sx)+h(xi+1, yi)*sx)*(1-sy)+(h(xi, yi+1)*(1-sx)+h(xi+1, yi+1)*sx)*sy

def smooth1(v, sig):
    k = np.exp(-(np.arange(-3*sig, 3*sig+1)/sig)**2/2); k /= k.sum()
    return np.convolve(np.pad(v, int(3*sig), mode='edge'), k, mode='valid')

def catmull(P, per):
    P = np.array(P, float)
    Q = np.vstack([2*P[0]-P[1], P, 2*P[-1]-P[-2]])
    out = []
    for i in range(1, len(Q)-2):
        for t in np.linspace(0, 1, per, endpoint=False):
            p0, p1, p2, p3 = Q[i-1], Q[i], Q[i+1], Q[i+2]
            out.append(0.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t**3))
    out.append(P[-1])
    return np.array(out)

def unit(v):
    v = np.asarray(v, float)
    return v/np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-12)

def run(ob, op, **kw):
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob], selected_editable_objects=[ob]):
        return op(**kw)

class Mesh:
    """Collects lofted grids and loose pieces into one object."""
    def __init__(self): self.V, self.F, self.n = [], [], 0
    def raw(self, V, F):
        V = np.asarray(V, float).reshape(-1, 3)
        self.V.append(V); self.F += [tuple(i+self.n for i in f) for f in F]; self.n += len(V)
    def loft(self, G, close_u=False, close_v=False, inside=None):
        """G is (rows, cols, 3). inside is a point the surface should face away from."""
        G = np.asarray(G, float); R, C = G.shape[:2]
        F = []
        for i in range(R if close_u else R-1):
            for j in range(C if close_v else C-1):
                a, b, c, d = i*C+j, i*C+(j+1) % C, ((i+1) % R)*C+(j+1) % C, ((i+1) % R)*C+j
                F.append((a, b, c, d))
        if inside is not None:
            i, j = (R-1)//2, (C-1)//2
            p0, p1, p2 = G[i, j], G[i, (j+1) % C], G[(i+1) % R, j]
            if np.dot(np.cross(p1-p0, p2-p0), p0-np.asarray(inside, float)) < 0:
                F = [f[::-1] for f in F]
        self.raw(G.reshape(-1, 3), F)
    def build(self, name, mat=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata(np.vstack(self.V).tolist(), [], self.F); me.update()
        me.polygons.foreach_set('use_smooth', [True]*len(me.polygons))
        ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
        if mat: me.materials.append(mat)
        return ob

def lathe(profile, oval=1.0, sides=128, per=8, brim=None):
    """Spins an (r, z) profile round the Z axis. brim=(r_from, r_to, lift, drop) curls the outer part up at the sides."""
    P = catmull(profile, per)
    a = np.linspace(0, 2*math.pi, sides, endpoint=False)
    V = []
    for r, z in P:
        r = max(r, .0002)
        ring = np.column_stack([r*np.cos(a), r*oval*np.sin(a), np.full(sides, z)])
        if brim:
            t = np.clip((r-brim[0])/(brim[1]-brim[0]), 0, 1)
            ring[:, 2] += brim[2]*t*t*np.cos(a)**2 - brim[3]*t*t*np.sin(a)**2
        V.append(ring)
    return np.array(V)

def store_rest(ob):
    """Keeps the undeformed position on each vertex, so procedural textures stay put when the rig moves."""
    at = ob.data.attributes.new('rest', 'FLOAT_VECTOR', 'POINT')
    co = np.empty(len(ob.data.vertices)*3); ob.data.vertices.foreach_get('co', co)
    at.data.foreach_set('vector', co)
    ob.data.update()

# ---------- 1. body, face, rig, skin, hair, suit ----------
MACRO = dict(gender=1.0, age=.64, muscle=.62, weight=.42, proportions=.62, cupsize=.5, firmness=.5, race=dict(asian=0, caucasian=1, african=0))
FACE = {'head/head-square': .3, 'head/head-rectangular': .2, 'chin/chin-prominent-incr': .35, 'chin/chin-width-incr': .3, 'chin/chin-bones-incr': .45, 'chin/chin-cleft-incr': .5,
        'cheek/l-cheek-bones-incr': .5, 'cheek/r-cheek-bones-incr': .5, 'cheek/l-cheek-volume-decr': .45, 'cheek/r-cheek-volume-decr': .45, 'cheek/l-cheek-inner-decr': .3, 'cheek/r-cheek-inner-decr': .3,
        'nose/nose-hump-incr': .4, 'nose/nose-scale-vert-incr': .2, 'nose/nose-width2-decr': .2, 'nose/nose-point-down': .2,
        'eyebrows/eyebrows-trans-down': .35, 'eyebrows/eyebrows-angle-down': .2, 'eyebrows/eyebrows-trans-forward': .35,
        'eyes/l-eye-bag-incr': .45, 'eyes/r-eye-bag-incr': .45, 'eyes/l-eye-eyefold-down': .3, 'eyes/r-eye-eyefold-down': .3, 'eyes/l-eye-height2-decr': .2, 'eyes/r-eye-height2-decr': .2,
        'mouth/mouth-angles-down': .25, 'mouth/mouth-upperlip-volume-decr': .45, 'mouth/mouth-lowerlip-volume-decr': .15, 'mouth/mouth-laugh-lines-in': .5, 'mouth/mouth-scale-horiz-incr': .2,
        'head/head-age-incr': .4, 'forehead/forehead-temple-decr': .25, 'neck/neck-scale-horiz-incr': .2}

def coords(ob):
    co = np.empty(len(ob.data.vertices)*3); ob.data.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)

def shown(ob):
    dg = bpy.context.evaluated_depsgraph_get(); me = ob.evaluated_get(dg).data
    co = np.empty(len(me.vertices)*3); me.vertices.foreach_get('co', co)
    return co.reshape(-1, 3)

def make_base(height):
    b = HumanService.create_human(macro_detail_dict=dict(MACRO, height=height), mask_helpers=True, detailed_helpers=True, extra_vertex_groups=True)
    for k, v in FACE.items():
        TargetService.load_target(b, f'{TARGETS}/{k}.target.gz', weight=v)
    return b

lo, hi = .2, .9
for _ in range(9):   # the height slider is not linear, so search for the value that gives 1.80 m
    mid = (lo+hi)/2
    b = make_base(mid); h = shown(b)[:, 2].max()
    bpy.data.objects.remove(b)
    lo, hi = (mid, hi) if h < HEIGHT else (lo, mid)
body = make_base((lo+hi)/2)
body.name = 'Body'
rig = HumanService.add_builtin_rig(body, 'mixamo_unity')
rig.name = 'Human_Rig'
HumanService.set_character_skin(f'{ASSETS}/skins/middleage_caucasian_male/middleage_caucasian_male.mhmat', body, skin_type='ENHANCED_SSS')
parts = {}
for key, typ, f in (('Eyes', 'Eyes', 'eyes/high-poly/high-poly'), ('Brows', 'Eyebrows', 'eyebrows/eyebrow009/eyebrow009'), ('Lashes', 'Eyelashes', 'eyelashes/eyelashes01/eyelashes01'),
                    ('Teeth', 'Teeth', 'teeth/teeth_base/teeth_base'), ('Tongue', 'Tongue', 'tongue/tongue01/tongue01'), ('Hair', 'Hair', 'hair/short04/short04'),
                    ('Suit', 'Clothes', 'clothes/male_elegantsuit01/male_elegantsuit01'), ('Shoes', 'Clothes', 'clothes/shoes04/shoes04')):
    before = set(bpy.data.objects)
    HumanService.add_mhclo_asset(f'{ASSETS}/{f}.mhclo', body, asset_type=typ, subdiv_levels=0, material_type='MAKESKIN')
    parts[key] = (set(bpy.data.objects)-before).pop()
    parts[key].name = key
TargetService.bake_targets(body)
skinned = [body]+list(parts.values())

def bone_w(name, tail=False):
    bn = rig.data.bones['mixamorig:'+name]
    return np.array(rig.matrix_world @ (bn.tail_local if tail else bn.head_local))

def turn(pb, q):
    """Rotates a pose bone by a world-space quaternion about its own head."""
    bpy.context.view_layer.update()
    M = rig.matrix_world @ pb.matrix
    T = Matrix.Translation(M.translation)
    pb.matrix = rig.matrix_world.inverted() @ T @ q.to_matrix().to_4x4() @ T.inverted() @ M
    bpy.context.view_layer.update()

def aim(name, direction):
    """Points a pose bone along a world direction. Returns the rotation it used."""
    pb = rig.pose.bones['mixamorig:'+name]
    bpy.context.view_layer.update()
    cur = ((rig.matrix_world @ pb.matrix).to_3x3() @ Vector((0, 1, 0))).normalized()
    q = cur.rotation_difference(Vector(direction).normalized())
    turn(pb, q)
    return q

# MakeHuman stands with the feet wide apart. Bring them under the hips and make that the rest pose.
for side, s in (('Left', 1), ('Right', -1)):
    d = bone_w(side+'UpLeg', True)-bone_w(side+'UpLeg')
    q = aim(side+'UpLeg', (s*.022, d[1], d[2]))
    turn(rig.pose.bones[f'mixamorig:{side}Foot'], q.inverted())
for ob in skinned:
    for m in [m for m in ob.modifiers if m.type == 'ARMATURE']:
        run(ob, bpy.ops.object.modifier_apply, modifier=m.name)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode='OBJECT')

def bind(ob):
    m = ob.modifiers.new('Armature', 'ARMATURE'); m.object = rig
    run(ob, bpy.ops.object.modifier_move_to_index, modifier=m.name, index=0)
    ob.parent = rig
for ob in skinned: bind(ob)
bpy.context.view_layer.update()
suit, shoes = parts['Suit'], parts['Shoes']

def dominant(ob):
    """Name of the strongest bone on each vertex, and the weights by bone name."""
    names = [g.name.replace('mixamorig:', '') if g.name.startswith('mixamorig:') else None for g in ob.vertex_groups]
    dom, W = [], []
    for v in ob.data.vertices:
        w = {names[g.group]: g.weight for g in v.groups if names[g.group]}
        W.append(w); dom.append(max(w, key=w.get) if w else '')
    return np.array(dom), W

body_dom, body_W = dominant(body)
body_co = coords(body)
in_body = np.zeros(len(body_co), bool)
gi = body.vertex_groups['body'].index
for v in body.data.vertices:
    in_body[v.index] = any(g.group == gi and g.weight > .5 for g in v.groups)
suit_dom, _ = dominant(suit)
suit_co = coords(suit)
# the suit is two loose pieces: jacket with shirt and tie, and trousers
bm = bmesh.new(); bm.from_mesh(suit.data); bm.verts.ensure_lookup_table()
piece = np.full(len(bm.verts), -1)
for v in bm.verts:
    if piece[v.index] >= 0: continue
    stack = [v]; piece[v.index] = v.index
    while stack:
        w = stack.pop()
        for e in w.link_edges:
            o = e.other_vert(w)
            if piece[o.index] < 0: piece[o.index] = v.index; stack.append(o)
bm.free()
is_trousers = np.array([suit_co[piece == piece[i]][:, 2].min() < .3 for i in range(len(suit_co))])
print('HEIGHT', shown(body)[:, 2].max(), flush=True)

# ---------- 2. measurements the coat is cut from ----------
is_arm = lambda dom: np.array([('Arm' in d or 'Hand' in d) for d in dom])
is_head = lambda dom: np.array([d in ('Head', 'Jaw', 'Neck') or 'Eye' in d or 'Orbic' in d for d in dom])
cloud = np.vstack([suit_co, body_co[in_body]])
cloud_dom = np.concatenate([suit_dom, body_dom[in_body]])
TOR = cloud[~is_arm(cloud_dom) & ~is_head(cloud_dom)]
# below the chest the jacket is hidden, so the coat is cut closer, round the body and trousers
low_co = np.vstack([suit_co[is_trousers], body_co[in_body]]); low_dom = np.concatenate([suit_dom[is_trousers], body_dom[in_body]])
LOW = low_co[~is_arm(low_dom) & ~is_head(low_dom)]
ARM = cloud[is_arm(cloud_dom)]
UPPER = cloud[~is_head(cloud_dom) & (cloud[:, 2] > 1.25)]

SH, EL, WR = bone_w('LeftArm'), bone_w('LeftForeArm'), bone_w('LeftHand')
ZHEM, ZBELT, ZB = .455, 1.085, 1.285
X_TIP = SH[0]+.014
X_NK = .074
# shoulder line: the highest cloth at each distance from the middle
xs = np.arange(X_NK-.01, X_TIP+.012, .004)
ztop = np.array([UPPER[np.abs(np.abs(UPPER[:, 0])-x) < .006][:, 2].max() for x in xs])
ztop = np.minimum.accumulate(smooth1(ztop, 2))+.011
# In the A-pose the shoulder muscle sits as high as the neck. Cut the coat to a natural slope instead; the sleeve head covers the muscle.
slope = float(np.interp(X_NK, xs, ztop))-.34*(xs-X_NK)
ztop = np.where(xs < SH[0]-.075, ztop, np.minimum(ztop, np.maximum(slope, ztop-.011+.002-.2*sstep(xs, SH[0]-.075, SH[0]-.04))))
ztop = np.minimum.accumulate(smooth1(ztop, 2))
Z_TIP = float(np.interp(X_TIP, xs, ztop)); Z_NK = float(np.interp(X_NK, xs, ztop))
ZTOP = Z_NK-.036   # where the lapel roll line meets the collar
zs = np.concatenate([np.arange(ZHEM, Z_TIP, .005), np.linspace(Z_TIP, Z_NK, 30)])

def slab(P, z, h=.012, xmax=None):
    s = P[np.abs(P[:, 2]-z) < h]
    if xmax is not None: s = s[np.abs(s[:, 0]) < xmax]
    return s
def measure(P_low, P_high):
    hw, yf, yb = [], [], []
    for z in zs:
        lim = None if z < Z_TIP-.16 else float(np.interp(z, ztop[::-1], xs[::-1]))+.012
        s = slab(P_low if z < Z_TIP-.16 else P_high, z, xmax=lim)
        hw.append(np.abs(s[:, 0]).max()); yf.append(s[:, 1].min()); yb.append(s[:, 1].max())
    return np.array([hw, yf, yb])
JACKET_Z = 1.20   # the jacket is masked off below this, under the coat
t_j = sstep(zs, JACKET_Z-.10, JACKET_Z-.01)
hw, yf, yb = [smooth1(v, 5) for v in measure(LOW, UPPER)*(1-t_j)+measure(TOR, UPPER)*t_j]

ease = .026+.006*(1-t_j)-.020*np.exp(-((zs-ZBELT)/.05)**2)             # the belt pulls the waist in
A = hw+ease
B = (yb-yf)/2+ease
YC = (yf+yb)/2
k_hip = int(np.argmin(np.abs(zs-.93)))
flare = np.clip((zs[k_hip]-zs)/(zs[k_hip]-ZHEM), 0, 1)
A = np.where(zs < zs[k_hip], np.maximum(A, A[k_hip]+.055*flare), A)   # skirt widens to the hem
B = np.where(zs < zs[k_hip], np.maximum(B, B[k_hip]+.050*flare), B)
t_sh = sstep(zs, Z_TIP-.09, Z_TIP-.01)
A = A*(1-t_sh)+np.maximum(A, X_TIP)*t_sh                               # square the shoulder out to its tip
top = zs >= Z_TIP
A[top] = np.interp(zs[top], ztop[::-1], xs[::-1])
A = smooth1(A, 2); B = smooth1(B, 3); YC = smooth1(YC, 3)
NEXP = 2.2+.4*sstep(zs, 1.0, 1.25)-.4*sstep(zs, Z_TIP-.05, Z_NK)

X_OV, X_IN, X_G = .088, .045, .056
def x_out(z):
    t = np.clip((z-ZB)/(ZTOP-ZB), 0, 1)
    return np.where(z > ZTOP, X_G, -X_OV+(X_G+X_OV)*t)
def x_in(z):
    t = np.clip((z-ZB)/(ZTOP-ZB), 0, 1)
    return np.where(z > ZTOP, -X_G, X_IN-(X_G+X_IN)*t)

def ring(z):
    return [np.interp(z, zs, v) for v in (A, B, YC, NEXP)]

def phi_of(x, z):
    a, b, yc, n = ring(z)
    r = np.clip(np.abs(x)/a, 0, .985)
    return np.sign(x)*np.arcsin(r**(n/2))

def folds(phi, z):
    """How far the cloth stands off its smooth shape. phi is the angle round the body, 0 at the front, + toward his left."""
    cx, sx = np.cos(phi), np.sin(phi)
    pw = np.arctan2(sx, cx)
    back = sstep(-cx, .0, .5)
    d = .0009*vnoise(cx*2.2+z*3, sx*2.2-z*2, 1)
    g, under = np.exp(-((z-ZBELT)/.08)**2), np.exp(-((z-ZBELT)/.024)**2)
    d += .0055*g*(1-.8*under)*np.sin(pw*19+2.5*vnoise(cx*3, sx*3, 2)+1.2*vnoise(z*14, pw*.5, 3))        # gathers at the belt
    t = np.clip((ZBELT-.07-z)/(ZBELT-.07-ZHEM), 0, 1)
    f = .6*vnoise(cx*2.5+.3*vnoise(z*3, 0, 5), sx*2.5, 4)+.4*vnoise(cx*5.2, sx*5.2+.4*vnoise(z*4, 0, 7), 6)
    d += .032*t**1.25*f                                                                                  # skirt drape
    d += .0028*np.sin(z*38+pw*4+3*vnoise(cx*2, sx*2+z*5, 8))*sstep(z, ZBELT+.05, ZBELT+.12)*(1-sstep(z, 1.30, 1.38))*sstep(np.abs(sx), .35, .8)
    zy = 1.335+.035*sx*sx
    d += .0042*back*sstep(z, zy-.003, zy)                                                                # back yoke
    gun = sstep(-pw, .50, .55)*(1-sstep(-pw, 1.5, 1.6))
    d += .0040*gun*sstep(z, 1.318, 1.321)                                                                # gun flap on the right chest
    d -= .007*np.exp(-(sx/.022)**2)*back*(1-sstep(z, .80, .84))                                          # back vent
    return d

TWO_PI = 2*math.pi
def coat_pt(theta, z, off=0.0):
    """Point on the coat. theta runs from the hidden right front edge, round his right side and back, to the left front edge on top."""
    theta = np.asarray(theta, float); z = np.zeros_like(theta)+z
    a, b, yc, n = ring(z)
    def base(p):
        s, c = np.sin(p), np.cos(p)
        return a*np.sign(s)*np.abs(s)**(2/n), yc-b*np.sign(c)*np.abs(c)**(2/n)
    phi = -theta
    x, y = base(phi); x1, y1 = base(phi+2e-3); x0, y0 = base(phi-2e-3)
    tx, ty = x1-x0, y1-y0; L = np.maximum(np.hypot(tx, ty), 1e-9)
    nx, ny = ty/L, -tx/L
    lap = 1-sstep(z, ZB+.03, ZB+.11)
    d = folds(phi, z)+off+.0075*sstep(theta, TWO_PI-1.25, TWO_PI-.6)*lap-.004*(1-sstep(theta, .1, .6))*lap
    return np.stack([x+nx*d, y+ny*d, z], -1), np.stack([nx, ny, np.zeros_like(nx)], -1)

def front_theta(x, z):
    """theta for a point on the visible front of the coat."""
    p = phi_of(x, z)
    return np.where(x >= x_out(z)-.002, TWO_PI-p, -p)

# ---------- 3. materials ----------
def pbsdf(name, **inp):
    m = bpy.data.materials.new(name); m.use_nodes = True
    n = m.node_tree.nodes.get('Principled BSDF')
    for k, v in inp.items(): n.inputs[k.replace('_', ' ')].default_value = v
    return m, n

class Nodes:
    """Short names for the node chains the materials repeat. Textures read the stored rest position."""
    def __init__(self, tree):
        self.t = tree; self.N = tree.nodes; self.L = tree.links
        self.tc = self.N.new('ShaderNodeAttribute'); self.tc.attribute_name = 'rest'
    def tex(self, scale, detail=6, stretch=None):
        n = self.N.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
        src = self.tc.outputs['Vector']
        if stretch:
            mp = self.N.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = stretch
            self.L.new(src, mp.inputs['Vector']); src = mp.outputs['Vector']
        self.L.new(src, n.inputs['Vector'])
        return n.outputs['Fac']
    def ramp(self, fac, a, b, pa=.3, pb=.7):
        n = self.N.new('ShaderNodeValToRGB'); r = n.color_ramp
        r.elements[0].position = pa; r.elements[0].color = a
        r.elements[1].position = pb; r.elements[1].color = b
        self.L.new(fac, n.inputs['Fac'])
        return n.outputs['Color']
    def mix(self, fac, a, b, blend='MIX'):
        n = self.N.new('ShaderNodeMix'); n.data_type = 'RGBA'; n.blend_type = blend
        for sock, v in ((n.inputs[0], fac), (n.inputs['A'], a), (n.inputs['B'], b)):
            if hasattr(v, 'links'): self.L.new(v, sock)
            else: sock.default_value = v
        return n.outputs[2]
    def math(self, op, a, b):
        n = self.N.new('ShaderNodeMath'); n.operation = op
        for sock, v in ((n.inputs[0], a), (n.inputs[1], b)):
            if hasattr(v, 'links'): self.L.new(v, sock)
            else: sock.default_value = v
        return n.outputs[0]
    def bump(self, height, strength, distance):
        n = self.N.new('ShaderNodeBump'); n.inputs['Strength'].default_value = strength; n.inputs['Distance'].default_value = distance
        self.L.new(height, n.inputs['Height'])
        return n.outputs['Normal']

def make_cloth(name, a, b, rough, weave, strength, sheen=.15):
    m, p = pbsdf(name, Roughness=rough, Sheen_Weight=sheen)
    n = Nodes(m.node_tree)
    col = n.ramp(n.tex(55, 8), a, b, .36, .64)
    col = n.mix(1, col, n.ramp(n.tex(6, 4), (.86, .85, .84, 1), (1, 1, 1, 1), .35, .7), 'MULTIPLY')   # slow change in tone
    n.L.new(col, p.inputs['Base Color'])
    w = n.N.new('ShaderNodeTexWave'); w.inputs['Scale'].default_value = weave; w.inputs['Distortion'].default_value = .6
    mp = n.N.new('ShaderNodeMapping'); mp.inputs['Rotation'].default_value = (0, math.radians(38), math.radians(20))
    n.L.new(n.tc.outputs['Vector'], mp.inputs['Vector']); n.L.new(mp.outputs['Vector'], w.inputs['Vector'])   # twill runs on the diagonal
    n.L.new(n.bump(n.math('ADD', w.outputs['Fac'], n.math('MULTIPLY', n.tex(weave*2.5, 2), .6)), strength, .0005), p.inputs['Normal'])
    return m

def make_leather(name, a, b, rough):
    m, p = pbsdf(name, Roughness=rough)
    n = Nodes(m.node_tree)
    n.L.new(n.ramp(n.tex(38, 7), a, b), p.inputs['Base Color'])
    vor = n.N.new('ShaderNodeTexVoronoi'); vor.feature = 'DISTANCE_TO_EDGE'; vor.inputs['Scale'].default_value = 900
    n.L.new(n.tc.outputs['Vector'], vor.inputs['Vector'])
    n.L.new(n.bump(n.math('ADD', n.tex(420, 4), n.math('MULTIPLY', vor.outputs['Distance'], 1.5)), .22, .0006), p.inputs['Normal'])
    return m

coat_mat = make_cloth('Coat', (.098, .078, .048, 1), (.118, .095, .06, 1), .62, 2600, .22)
belt_mat = make_cloth('Coat_Belt', (.09, .072, .044, 1), (.108, .087, .055, 1), .6, 2600, .22)
glove_mat = make_leather('Glove', (.014, .009, .007, 1), (.045, .028, .02, 1), .38)
button_mat, _ = pbsdf('Button', Base_Color=(.035, .02, .012, 1), Roughness=.22, Coat_Weight=.4)
buckle_mat, _ = pbsdf('Buckle', Base_Color=(.05, .028, .016, 1), Roughness=.3)

def make_hat():
    m, p = pbsdf('Hat', Roughness=.36)
    n = Nodes(m.node_tree)
    grain = n.tex(95, 5, (1, 1, .05))
    n.L.new(n.ramp(grain, (.009, .009, .011, 1), (.024, .024, .028, 1)), p.inputs['Base Color'])
    n.L.new(n.bump(n.tex(900, 3), .08, .0006), p.inputs['Normal'])
    return m
def make_band():
    m, p = pbsdf('Ribbon', Roughness=.5, Sheen_Weight=.25)
    n = Nodes(m.node_tree)
    rib = n.tex(70, 1, (.04, .04, 28))
    n.L.new(n.ramp(rib, (.04, .006, .008, 1), (.075, .012, .014, 1)), p.inputs['Base Color'])
    n.L.new(n.bump(rib, .18, .0006), p.inputs['Normal'])
    return m
hat_mat, band_mat = make_hat(), make_band()

# ---------- 4. trench coat ----------
coat = Mesh()
NCOL = 230
th_s = -phi_of(x_in(zs), zs); th_e = TWO_PI-phi_of(x_out(zs), zs)
u = np.linspace(0, 1, NCOL)
TH = th_s[:, None]+(th_e-th_s)[:, None]*u[None]
G, _ = coat_pt(TH, zs[:, None]+0*TH)
G[:, :, 2] += .006*np.clip(1-(zs[:, None]-ZHEM)/.25, 0, 1)**2*vnoise(TH*3.1, 0, 12)     # the hem is not dead level
coat.loft(G, inside=(0, 0, 1.0))

# sleeves: a tube along the arm bones, wide enough for the suit sleeve inside it
d_up, d_fo = unit(EL-SH), unit(WR-EL)
axis_pts = [SH-d_up*.055, SH+d_up*.03, (SH+EL)/2, EL-d_up*.03, EL+d_fo*.03, (EL+WR)/2, WR-d_fo*.012]
CL = catmull(axis_pts, 14)
seg = np.linalg.norm(np.diff(CL, axis=0), axis=1); S = np.concatenate([[0], np.cumsum(seg)]); SL = S[-1]
arm = cloud[np.array([d in ('LeftArm', 'LeftForeArm') for d in cloud_dom])]
rad = np.zeros(len(CL))
d2 = np.linalg.norm(arm[:, None, :]-CL[None], axis=2)
near = d2.argmin(1)
for i in range(len(CL)):
    sel = np.abs(near-i) <= 2
    rad[i] = d2[sel, near[sel]].max() if sel.any() else 0
s_el = S[np.linalg.norm(CL-EL, axis=1).argmin()]
floor = np.interp(S/SL, [0, .12, .45, .75, 1], [.070, .067, .060, .055, .053])
rad = smooth1(np.clip(rad+.011, floor, floor+.012), 4)
T = unit(np.gradient(CL, axis=0))
inner = unit(d_fo-d_up)                                    # the side the elbow folds toward
ref = unit(np.cross(T[0], inner)); frames = []
for t in T:
    ref = unit(ref-ref.dot(t)*t); frames.append((np.cross(t, ref), ref.copy()))
NS = 56
ang = np.linspace(0, TWO_PI, NS, endpoint=False)
rings = []
for i in range(len(CL)):
    e1, e2 = frames[i]
    dirs = np.cos(ang)[:, None]*e1+np.sin(ang)[:, None]*e2
    inn = np.clip(dirs @ inner, 0, 1)
    s = S[i]
    d = .0022*vnoise(s*16+np.cos(ang)*1.5, np.sin(ang)*1.5, 21)
    d += .0050*np.exp(-((s-s_el)/.075)**2)*(.35+.65*inn)*np.sin(s*210+2.2*vnoise(ang*1.3, s*9, 22))   # creases at the elbow
    d += .0028*np.sin(s*70+ang*2+2*vnoise(s*8, ang, 23))*sstep(s, s_el+.02, s_el+.1)*(1-sstep(s, SL-.09, SL-.05))
    d += .0018*np.sin(s*55-ang*2+2*vnoise(s*7, ang, 24))*sstep(s, .09, .16)*(1-sstep(s, s_el-.1, s_el-.03))
    cap = math.sqrt(max(1-(max(.07-s, 0)/.07)**2, .02))        # the sleeve head closes to a dome inside the shoulder
    rings.append(CL[i]+dirs*((rad[i]+d)*cap)[:, None])
rings = np.array(rings)
for sx in (1, -1):
    coat.loft(rings*np.array([sx, 1, 1]), close_v=True, inside=CL[len(CL)//2]*np.array([sx, 1, 1]))

def strip(m, P, Nrm, Side, width, thick, closed=False, taper=None):
    """Flat strap along P, lying on a surface whose normal is Nrm."""
    P, Nrm, Side = np.asarray(P, float), unit(Nrm), unit(Side)
    w = np.full(len(P), width/2) if taper is None else width/2*np.asarray(taper)
    bev = min(thick*.6, width*.12)
    prof = [(-1, 0, 0), (-1, 0, .55), (-1, bev, 1), (0, 0, 1), (1, -bev, 1), (1, 0, .55), (1, 0, 0)]
    G = np.array([[P[i]+Side[i]*(a*w[i]+b*(w[i] > bev))+Nrm[i]*c*thick for a, b, c in prof] for i in range(len(P))])
    m.loft(G, close_u=closed, inside=P.mean(0)-Nrm.mean(0)*.05 if not closed else P.mean(0))

def panel(outline, spacing=.006):
    """Triangulates a flat outline with points inside it, so it can bend over a surface."""
    O = np.array(outline, float); pts = []
    for i in range(len(O)):
        a, b = O[i], O[(i+1) % len(O)]
        n = max(1, int(np.linalg.norm(b-a)/spacing))
        pts += [a+(b-a)*k/n for k in range(n)]
    nb = len(pts)
    lo_, hi_ = O.min(0), O.max(0)
    gx, gz = np.meshgrid(np.arange(lo_[0], hi_[0], spacing), np.arange(lo_[1], hi_[1], spacing))
    Q = np.column_stack([gx.ravel(), gz.ravel()])
    Bd = np.array(pts)
    inside = np.zeros(len(Q), bool)
    for i in range(len(O)):
        a, b = O[i], O[(i+1) % len(O)]
        cond = ((a[1] > Q[:, 1]) != (b[1] > Q[:, 1])) & (Q[:, 0] < (b[0]-a[0])*(Q[:, 1]-a[1])/(b[1]-a[1]+1e-12)+a[0])
        inside ^= cond
    far = np.linalg.norm(Q[:, None]-Bd[None], axis=2).min(1) > spacing*.55
    allp = np.vstack([Bd, Q[inside & far]])
    res = geometry.delaunay_2d_cdt([Vector(p) for p in allp], [(i, (i+1) % nb) for i in range(nb)], [], 1, 1e-7)
    return np.array([tuple(v) for v in res[0]]), [tuple(f) for f in res[2]]

def seg_dist(P, a, b):
    a, b = np.array(a), np.array(b); ab = b-a
    t = np.clip(((P-a) @ ab)/(ab @ ab), 0, 1)
    return np.linalg.norm(P-(a+t[:, None]*ab), axis=1)

def clip(poly, a, b):
    """Keeps the part of a polygon on the left of the line a to b."""
    a, b = np.array(a), np.array(b); out = []
    side = lambda p: (b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])
    for i in range(len(poly)):
        p, q = np.array(poly[i]), np.array(poly[(i+1) % len(poly)])
        sp, sq = side(p), side(q)
        if sp >= 0: out.append(tuple(p))
        if (sp >= 0) != (sq >= 0): out.append(tuple(p+(q-p)*sp/(sp-sq)))
    return out

# lapels: his left one lies on the top panel, the right one comes out from under it
RB, RT = (-X_OV, ZB), (X_G, ZTOP)
dz = ZTOP-ZB
left_lapel = [RB, (-.030, ZB+.012), (.045, ZB+.20*dz), (.100, ZB+.47*dz), (.150, ZB+.76*dz), (.119, ZB+.835*dz), (.093, ZB+.945*dz), (X_G+.004, ZTOP+.004)]
lap = Mesh()
def add_lapel(outline, mirror):
    V2, F = panel(outline)
    x, z = V2[:, 0], V2[:, 1]
    roll = seg_dist(V2, (-RB[0], RB[1]) if mirror else RB, (-RT[0], RT[1]) if mirror else RT)
    th = -phi_of(x, z) if mirror else TWO_PI-phi_of(x, z)
    P, _ = coat_pt(th, z, .0035+.0055*sstep(roll, 0, .028))
    if (np.cross(P[F[0][1]]-P[F[0][0]], P[F[0][2]]-P[F[0][0]])[1] > 0): F = [f[::-1] for f in F]
    lap.raw(P, F)
add_lapel(left_lapel, False)
right_lapel = clip([(-x, z) for x, z in left_lapel][::-1], (RT[0]-.004, RT[1]), (RB[0]-.004, RB[1]))
add_lapel(right_lapel, True)

# collar: a stand round the neck that folds over and lies on the shoulders
top_th = np.linspace(th_s[-1]+.02, th_e[-1]-.02, 90)
CP, CN = coat_pt(top_th, Z_NK)
cphi = np.arctan2(np.sin(-top_th), np.cos(-top_th))
frontness = sstep(np.cos(cphi), -.1, .75)
rows = []
for i in range(len(top_th)):
    f = frontness[i]
    hs = .034-.012*f; L = .056+.022*f; al = math.radians(24+50*f)
    prof = [(0, -.004), (-.003, hs*.5), (-.004, hs), (.002, hs+.005), (.009, hs+.003)]
    for t in (.3, .6, .85, 1):
        prof.append((.009+L*t*math.cos(al*(.6+.4*t)), hs+.003-L*t*math.sin(al*(.6+.4*t))))
    rows.append([CP[i]+CN[i]*r+np.array([0, 0, h]) for r, h in prof])
collar = Mesh(); collar.loft(np.array(rows), inside=(0, 0, Z_NK+.5))

# belt, its loops and the loose end past the buckle
belt = Mesh()
bphi = np.linspace(-math.pi, math.pi, 260, endpoint=False)
def outer_theta(p, z):
    return np.where(p >= phi_of(x_out(np.array(z)), z), TWO_PI-p, -p)
BP, BN = coat_pt(outer_theta(bphi, ZBELT), ZBELT, .0035)
up = np.tile([0, 0, 1.0], (len(BP), 1))
strip(belt, BP, BN, up, .044, .0035, closed=True)
tphi = phi_of(np.linspace(-.035, -.165, 30), ZBELT)
TP, TN = coat_pt(outer_theta(tphi, ZBELT), ZBELT-np.linspace(0, .012, 30), .0085)
strip(belt, TP, TN, np.tile([0, 0, 1.0], (30, 1)), .042, .003, taper=np.concatenate([np.ones(24), np.linspace(1, .25, 6)]))
for p in (-2.45, -1.45, 1.45, 2.45):
    zz = np.linspace(ZBELT-.034, ZBELT+.034, 9)
    LP, LN = coat_pt(outer_theta(np.full(9, p), ZBELT), zz, .004+.006*np.sin(np.linspace(0, math.pi, 9)))
    strip(belt, LP, LN, np.cross(LN, [0, 0, 1.0]), .011, .002)

bits, metal = Mesh(), Mesh()
def frame_at(x, z, off):
    P, N = coat_pt(front_theta(np.array([x]), z), z, off)
    N = N[0]; U = unit(np.cross([0, 0, 1.0], N))
    return P[0], U, np.array([0, 0, 1.0]), N
def button(m, P, U, V, N, r=.0118):
    prof = [(.0002, .0024), (r*.45, .0022), (r*.74, .0036), (r*.93, .0036), (r, .0022), (r*.97, .0004), (r*.8, 0)]
    R = lathe(prof, sides=28, per=3)
    m.loft(P+R[:, :, 0:1]*U+R[:, :, 1:2]*V+R[:, :, 2:3]*N, close_v=True, inside=P-N*.01)
for bx in (-.058, .058):
    for bz in (ZB-.025, ZB-.115, ZBELT-.105, ZBELT-.215):
        button(bits, *frame_at(bx, bz, .0028))
def box(m, c, U, V, N, su, sv, sn):
    co = [c+U*a*su/2+V*b*sv/2+N*k*sn/2 for a in (-1, 1) for b in (-1, 1) for k in (-1, 1)]
    m.raw(co, [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)])
P, U, V, N = frame_at(-.035, ZBELT, .0075)
for du, dv, su, sv in ((0, .029, .052, .009), (0, -.029, .052, .009), (-.0225, 0, .009, .058), (.0225, 0, .009, .058), (0, 0, .006, .058)):
    box(metal, P+U*du+V*dv, U, V, N, su, sv, .006)
box(metal, P+U*-.011+N*.004, U, V, N, .026, .004, .003)

# shoulder straps and cuff straps
straps = Mesh()
for sx in (1, -1):
    zz = np.interp(np.linspace(.094, X_TIP-.022, 16), xs, ztop)
    EP, _ = coat_pt(np.full(16, TWO_PI-math.pi/2 if sx > 0 else math.pi/2), zz, .0035)
    tang = unit(np.gradient(EP, axis=0)); side = np.tile([0, 1.0, 0], (16, 1))
    EN = unit(np.cross(tang, side)); EN *= np.sign(EN[:, 2:3])
    strip(straps, EP+EN*.001, EN, side, .044, .003, taper=np.concatenate([[.5, .85], np.ones(14)]))
    button(bits, EP[2]+EN[2]*.004, side[2], np.cross(EN[2], side[2]), EN[2], r=.0085)
    i = int(np.searchsorted(S, SL-.085)); e1, e2 = frames[i]
    dirs = np.cos(ang)[:, None]*e1+np.sin(ang)[:, None]*e2
    CPn = (CL[i]+dirs*(rad[i]+.0045)[..., None])*np.array([sx, 1, 1])
    strip(straps, CPn, dirs*np.array([sx, 1, 1]), np.tile(T[i]*np.array([sx, 1, 1]), (NS, 1)), .028, .003, closed=True)
    k = int(np.argmax(dirs @ np.array([.6, -.8, 0])))
    c = CPn[k]+dirs[k]*np.array([sx, 1, 1])*.005
    box(metal, c, np.cross(T[i], dirs[k])*np.array([sx, 1, 1]), T[i]*np.array([sx, 1, 1]), dirs[k]*np.array([sx, 1, 1]), .022, .034, .005)

coat_ob = coat.build('Coat', coat_mat)
coat_parts = [lap.build('Lapels', coat_mat), collar.build('Collar', coat_mat), straps.build('Straps', coat_mat)]
belt_ob = belt.build('Belt', belt_mat)
bits_ob = bits.build('Buttons', button_mat)
metal_ob = metal.build('Buckles', buckle_mat)
metal_ob.data.polygons.foreach_set('use_smooth', [False]*len(metal_ob.data.polygons))
for ob in coat_parts[:2]+[coat_ob]:
    so = ob.modifiers.new('Thickness', 'SOLIDIFY'); so.thickness = .0026; so.offset = 1 if ob is coat_ob else 0
bv = metal_ob.modifiers.new('Bevel', 'BEVEL'); bv.width = .0012; bv.segments = 2

# ---------- 5. gloves from his own hands ----------
gloves = body.copy(); gloves.data = body.data.copy(); gloves.name = 'Gloves'
bpy.context.collection.objects.link(gloves)
for m in list(gloves.modifiers): gloves.modifiers.remove(m)
keep = np.zeros(len(body_co), bool)
for i, w in enumerate(body_W):
    if not in_body[i]: continue
    hand = sum(v for k, v in w.items() if 'Hand' in k)
    side = 1 if body_co[i, 0] > 0 else -1
    wr, el = WR*np.array([side, 1, 1]), EL*np.array([side, 1, 1])
    up_arm = np.dot(body_co[i]-wr, unit(el-wr))
    keep[i] = hand > .5 or (w.get('LeftForeArm', 0)+w.get('RightForeArm', 0)+hand > .6 and up_arm < .055)
bm = bmesh.new(); bm.from_mesh(gloves.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep[v.index]], context='VERTS')
for _ in range(2): bmesh.ops.smooth_vert(bm, verts=bm.verts, factor=.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
bm.normal_update()
for v in bm.verts:
    side = 1 if v.co.x > 0 else -1
    wr, el = WR*np.array([side, 1, 1]), EL*np.array([side, 1, 1])
    cuff = float(sstep(np.dot(np.array(v.co)-wr, unit(el-wr)), .012, .05))
    v.co += v.normal*(.0019+.0035*cuff)
bm.to_mesh(gloves.data); bm.free()
gloves.data.materials.clear(); gloves.data.materials.append(glove_mat)
gloves.data.polygons.foreach_set('use_smooth', [True]*len(gloves.data.polygons))
hide = body.vertex_groups.new(name='Delete.gloves'); hide.add([int(i) for i in np.where(keep)[0]], 1.0, 'REPLACE')
mk = body.modifiers.new('Delete.gloves', 'MASK'); mk.vertex_group = hide.name; mk.invert_vertex_group = True

# ---------- 6. top hat, fitted to the head at the hat line ----------
hair = parts['Hair']
head_pts = np.vstack([coords(hair), body_co[in_body & is_head(body_dom)]])
ZHAT = HEIGHT-.082
ring_pts = head_pts[np.abs(head_pts[:, 2]-ZHAT) < .012]
HX = np.abs(ring_pts[:, 0]).max()+.0025
HY0, HY1 = ring_pts[:, 1].min()-.0025, ring_pts[:, 1].max()+.0025
OVAL = (HY1-HY0)/2/HX; k = HX/.0945
ZB_ = 0.0
hat_profile = [(.002, .004), (.05, .002), (.094, -.001), (.125, -.0035), (.1455, -.002), (.1525, .0035), (.1495, .0075), (.128, .0045), (.101, .0065),
               (.0945, .018), (.0905, .052), (.0925, .098), (.0985, .143), (.1015, .1585), (.0985, .1655), (.08, .167), (.002, .167)]
hat_c = np.array([0, (HY0+HY1)/2, ZHAT])
def hat_piece(name, prof, mat, **kw):
    m = Mesh(); R = lathe([(r*k, z) for r, z in prof], OVAL, **kw)
    m.loft(R+hat_c, close_v=True, inside=hat_c+np.array([0, 0, .08]))
    return m.build(name, mat)
hat_ob = hat_piece('Top_Hat', hat_profile, hat_mat, brim=(.102*k, .1525*k, .02, .004))
band_ob = hat_piece('Hat_Band', [(.0925, .0115), (.0948, .013), (.0955, .024), (.0945, .036), (.0912, .0385)], band_mat, per=4)

# ---------- 7. skin the new pieces to the rig ----------
mine = [coat_ob]+coat_parts+[belt_ob, bits_ob, metal_ob, gloves, hat_ob, band_ob]
for ob in mine: store_rest(ob)
for ob in [coat_ob]+coat_parts+[belt_ob, bits_ob, metal_ob]:
    dt = ob.modifiers.new('wt', 'DATA_TRANSFER'); dt.object = suit; dt.use_vert_data = True
    dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
    run(ob, bpy.ops.object.modifier_move_to_index, modifier='wt', index=0)
    run(ob, bpy.ops.object.datalayout_transfer, modifier='wt')
    run(ob, bpy.ops.object.modifier_apply, modifier='wt')
# The skirt hangs from the hips. Nearest-cloth weights would split it down the middle with the legs.
for ob in (coat_ob, bits_ob):
    co = coords(ob)
    g = {n: ob.vertex_groups.get('mixamorig:'+n) or ob.vertex_groups.new(name='mixamorig:'+n) for n in ('Hips', 'LeftUpLeg', 'RightUpLeg')}
    low = np.where((co[:, 2] < .99) & (np.abs(co[:, 0]) < A[k_hip]+.07))[0]
    t = .65*sstep(.99-co[low, 2], 0, .32); sl = sstep(co[low, 0], -.06, .06)
    for vg in ob.vertex_groups: vg.remove([int(i) for i in low])
    for i, tt, s in zip(low, t, sl):
        g['Hips'].add([int(i)], float(1-tt), 'REPLACE'); g['LeftUpLeg'].add([int(i)], float(tt*s), 'REPLACE'); g['RightUpLeg'].add([int(i)], float(tt*(1-s)), 'REPLACE')
# The body of the coat stays with the chest. If it took arm weights from the jacket it would crumple at the armpit; the sleeves turn inside it instead.
arm_groups = [g.index for g in coat_ob.vertex_groups if 'Arm' in g.name or 'Hand' in g.name]
chest = coat_ob.vertex_groups['mixamorig:Spine2']
for v in coat_ob.data.vertices[:len(zs)*NCOL]:
    lost = sum(g.weight for g in v.groups if g.group in arm_groups)
    if lost > 0:
        for gi_ in arm_groups: coat_ob.vertex_groups[gi_].remove([v.index])
        chest.add([v.index], lost, 'ADD')
for ob in [coat_ob]+coat_parts+[belt_ob, bits_ob, metal_ob, gloves]: bind(ob)
for ob in (hat_ob, band_ob):
    ob.parent = rig; ob.parent_type = 'BONE'; ob.parent_bone = 'mixamorig:Head'
    ob.matrix_world = Matrix.Identity(4)
under = suit.vertex_groups.new(name='Delete.coat')
under.add([int(i) for i in np.where(~is_trousers & ~is_arm(suit_dom) & (suit_co[:, 2] < JACKET_Z))[0]], 1.0, 'REPLACE')
mk = suit.modifiers.new('Delete.coat', 'MASK'); mk.vertex_group = under.name; mk.invert_vertex_group = True
for ob in (body, suit, shoes, gloves):
    sub = ob.modifiers.new('Subdivision', 'SUBSURF'); sub.levels = 1; sub.render_levels = 2 if ob is body else 1

# ---------- 8. skin, eyes and suit colour ----------
lips = body.vertex_groups['lips'].index
is_lip = np.array([any(g.group == lips and g.weight > .5 for g in v.groups) for v in body.data.vertices])
MO = body_co[is_lip].mean(0)
front = body_co[in_body & (np.abs(body_co[:, 0]) < .015) & (body_co[:, 1] < MO[1]+.03) & (body_co[:, 2] < MO[2]) & (body_co[:, 2] > MO[2]-.09)]
CHIN = front[:, 2].min()
x, y, z = np.abs(body_co[:, 0]), body_co[:, 1]-MO[1], body_co[:, 2]
line = MO[2]-.004+.085*sstep(y, .045, .125)
stub = sstep(line-z, -.014, .012)*sstep(z, CHIN-.05, CHIN-.012)*(1-sstep(y, .125, .14))
stub = np.maximum(stub, sstep(z, MO[2]+.007, MO[2]+.011)*(1-sstep(z, MO[2]+.02, MO[2]+.026))*(1-sstep(x, .028, .038))*(y < .03))
stub *= (~is_lip) & in_body
ca = body.data.color_attributes.new('stubble', 'FLOAT_COLOR', 'POINT')
stub = stub*.7   # how dark the shadow gets
ca.data.foreach_set('color', np.column_stack([stub, stub, stub, np.ones(len(stub))]).ravel())
body.data.update()
for mat in body.data.materials:
    grp = next((n for n in mat.node_tree.nodes if n.type == 'GROUP'), None)
    if not grp: continue
    for k_, v_ in (('Pore strength', .35), ('Roughness', .5), ('colorMixInStrength', .03)):
        if k_ in grp.inputs: grp.inputs[k_].default_value = v_
    tree = grp.node_tree
    tex = tree.nodes.get('DiffuseTexture')
    if not tex or tree.nodes.get('Stubble'): continue
    n = Nodes(tree)
    at = tree.nodes.new('ShaderNodeAttribute'); at.attribute_name = 'stubble'; at.name = 'Stubble'
    targets = [l.to_socket for l in tex.outputs['Color'].links]
    out = n.mix(at.outputs['Color'], tex.outputs['Color'], (.07, .065, .07, 1))
    for sock in targets: n.L.new(out, sock)
store_rest(body)

eye_mat = parts['Eyes'].data.materials[0]
for nd in eye_mat.node_tree.nodes:
    if nd.type == 'TEX_IMAGE': nd.image = bpy.data.images.load(f'{ASSETS}/eyes/materials/deepblue_eye.png')
    if nd.type == 'BSDF_PRINCIPLED': nd.inputs['Roughness'].default_value = .06; nd.inputs['Specular IOR Level'].default_value = .8

# the suit's tie is blue in its texture; make it the dark red of the hat band
sm = suit.data.materials[0]; st = sm.node_tree
p = next(n for n in st.nodes if n.type == 'BSDF_PRINCIPLED')
src = p.inputs['Base Color'].links[0].from_socket
sep = st.nodes.new('ShaderNodeSeparateColor'); sep.mode = 'HSV'; st.links.new(src, sep.inputs[0])
mr = st.nodes.new('ShaderNodeMapRange'); mr.interpolation_type = 'SMOOTHSTEP'
mr.inputs['From Min'].default_value = .22; mr.inputs['From Max'].default_value = .4; st.links.new(sep.outputs[1], mr.inputs['Value'])
mul = st.nodes.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = .42; st.links.new(sep.outputs[2], mul.inputs[0])
comb = st.nodes.new('ShaderNodeCombineColor'); comb.mode = 'HSV'; comb.inputs[0].default_value = .985; comb.inputs[1].default_value = .88; st.links.new(mul.outputs[0], comb.inputs[2])
mx = st.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'
st.links.new(mr.outputs['Result'], mx.inputs[0]); st.links.new(src, mx.inputs['A']); st.links.new(comb.outputs[0], mx.inputs['B'])
st.links.new(mx.outputs[2], p.inputs['Base Color'])
p.inputs['Roughness'].default_value = .7

# ---------- 9. standing pose (the rest pose stays an A-pose for rigging) ----------
if 'rest' not in want:
    for side, s in (('Left', 1), ('Right', -1)):
        aim(side+'Arm', (s*.20, .03, -1))
        aim(side+'ForeArm', (s*.13, -.20, -1))
        aim(side+'Hand', (s*.06, -.16, -1))
        for f in ('Index', 'Middle', 'Ring', 'Pinky'):
            for j, deg in ((1, 16), (2, 20), (3, 12)):
                pb = rig.pose.bones[f'mixamorig:{side}Hand{f}{j}']
                pb.rotation_mode = 'XYZ'; pb.rotation_euler.x += math.radians(deg)
    bpy.context.view_layer.update()

# ---------- stage ----------
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
scene.cycles.device = 'CPU'
scene.render.resolution_x, scene.render.resolution_y = 760, 1080
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast'
world = bpy.data.worlds.new('w'); world.use_nodes = True; scene.world = world
bg = world.node_tree.nodes['Background']
bg.inputs[0].default_value = (.16, .16, .17, 1)

def area(name, loc, target, size, power):
    li = bpy.data.lights.new(name, 'AREA'); li.size = size; li.energy = power
    o = bpy.data.objects.new(name, li); bpy.context.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector(target)-Vector(loc)).to_track_quat('-Z', 'Y').to_euler(); return o
key = area('key', (-2.4, -3.6, 3.3), (0, -.2, 1.2), 2.6, 420)
rim = area('rim', (2.6, 3.0, 3.0), (0, 0, 1.3), 2.0, 520)
fill = area('fill', (2.8, -3.0, 1.2), (0, 0, 1.3), 3.0, 90)

cam = bpy.data.cameras.new('c'); cam.sensor_fit = 'VERTICAL'; cam.sensor_height = 24
co = bpy.data.objects.new('Camera', cam); bpy.context.collection.objects.link(co); scene.camera = co
def shoot(name, az, height, dist, target, lens):
    t = Vector(target); a = math.radians(az)
    co.location = t + Vector((math.sin(a)*dist, -math.cos(a)*dist, height-target[2]))
    co.rotation_euler = (t-co.location).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = lens
    scene.render.filepath = f'{OUT}/{name}.png'
    bpy.ops.render.render(write_still=True)
    print('rendered', name, flush=True)

def noir(on, side=False):
    key.hide_render = rim.hide_render = fill.hide_render = on
    lo_.hide_render = fo.hide_render = not on
    old_player.hide_render = visor.hide_render = not side
    bg.inputs[0].default_value = (.012, .012, .014, 1) if on else (.16, .16, .17, 1)
    bg.inputs[1].default_value = .35 if on else 1

fm, fn = pbsdf('Floor', Roughness=.75)
bpy.ops.mesh.primitive_plane_add(size=24); floor_ob = bpy.context.object; floor_ob.name = 'Floor'; floor_ob.data.materials.append(fm); store_rest(floor_ob)
n = Nodes(fm.node_tree)
n.L.new(n.ramp(n.tex(.7, 10), (.07, .07, .07, 1), (.24, .24, .23, 1)), fn.inputs['Base Color'])

# the old capsule player for scale, side view only
pm, _ = pbsdf('Old_Player', Base_Color=(.2, .25, .34, 1), Roughness=.6)
me_ = bpy.data.meshes.new('Old_Player'); bm_ = bmesh.new(); bmesh.ops.create_uvsphere(bm_, u_segments=96, v_segments=64, radius=.35)
for v_ in bm_.verts: v_.co += Vector((0, .75, 1.45 if v_.co.z > 0 else .35))
bm_.to_mesh(me_); bm_.free(); me_.polygons.foreach_set('use_smooth', [True]*len(me_.polygons)); me_.update(); me_.materials.append(pm)
old_player = bpy.data.objects.new('Old_Player', me_); bpy.context.collection.objects.link(old_player)
bpy.ops.mesh.primitive_cube_add(size=1, location=(.0, .45, 1.5), scale=(.35, .15, .12)); visor = bpy.context.object; visor.name = 'Old_Player_Visor'
vm, _ = pbsdf('Visor', Base_Color=(.02, .02, .022, 1), Roughness=.4); visor.data.materials.append(vm)

lamp = bpy.data.lights.new('lamp', 'SPOT'); lamp.energy = 900; lamp.spot_size = math.radians(80); lamp.spot_blend = .12; lamp.shadow_soft_size = .05; lamp.color = (1, .86, .62)
lo_ = bpy.data.objects.new('lamp', lamp); bpy.context.collection.objects.link(lo_); lo_.location = (-1.0, -.6, 3.6)
lo_.rotation_euler = (Vector((0, -.05, 1.0))-lo_.location).to_track_quat('-Z', 'Y').to_euler()
flash = bpy.data.lights.new('flash', 'SPOT'); flash.energy = 70; flash.spot_size = math.radians(30); flash.spot_blend = .5; flash.shadow_soft_size = .02
fo = bpy.data.objects.new('flash', flash); bpy.context.collection.objects.link(fo); fo.location = (.5, -4.2, 1.35)
fo.rotation_euler = (Vector((0, 0, 1.45))-fo.location).to_track_quat('-Z', 'Y').to_euler()

# Paint on the mesh (the stubble mask) only reaches the renderer after a save and reload, so render from the reloaded file.
tmp = os.path.join(bpy.app.tempdir, 'human_build.blend')
bpy.ops.wm.save_as_mainfile(filepath=tmp); bpy.ops.wm.open_mainfile(filepath=tmp)
scene = bpy.context.scene; D = bpy.data.objects
key, rim, fill, lo_, fo = D['key'], D['rim'], D['fill'], D['lamp'], D['flash']
old_player, visor, co = D['Old_Player'], D['Old_Player_Visor'], D['Camera']; cam = co.data
bg = scene.world.node_tree.nodes['Background']

BODY = (0, -.03, 1.0)
if 'front' in want: noir(False); shoot('p1_front', 0, 1.18, 8.6, BODY, 85)
if 'three' in want: noir(False); shoot('p2_three', 215, 1.18, 8.6, BODY, 85)
if 'side' in want: noir(False, side=True); shoot('p3_side', 90, 1.18, 10.5, (0, .15, 1.0), 85)
if 'noir' in want: noir(True); shoot('p4_noir', 22, 1.18, 8.6, BODY, 85)
if 'face' in want: noir(False); shoot('p5_face', 25, 1.72, 2.4, (0, -.04, 1.69), 85)
if 'close' in want:
    noir(False)
    shoot('c1_face', 12, 1.70, 1.1, (0, -.06, 1.70), 85)
    shoot('c2_chest', 8, 1.38, 2.6, (0, -.1, 1.32), 85)
    shoot('c3_back', 180, 1.45, 3.2, (0, 0, 1.30), 85)
    shoot('c4_hand', 60, .95, 1.6, (.28, -.05, .86), 85)
    shoot('c5_belt', -20, 1.1, 2.2, (0, -.1, 1.02), 85)
    shoot('c6_shoulder', 115, 1.5, 1.8, (.15, 0, 1.42), 85)
if FULL:
    names = ['p1_front', 'p2_three', 'p3_side', 'p4_noir', 'p5_face']
    imgs = [bpy.data.images.load(f'{OUT}/{n_}.png') for n_ in names]
    w, h = imgs[0].size
    px = [np.array(i.pixels[:]).reshape(h, w, 4) for i in imgs]
    sheet = np.hstack(px)
    out = bpy.data.images.new('sheet', sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels = sheet.ravel().tolist()
    out.filepath_raw = f'{HERE}/human_design_sheet.png'; out.file_format = 'PNG'; out.save()
    for i in imgs+[out]: bpy.data.images.remove(i)
    noir(False)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=f'{HERE}/human.blend', compress=True)
if 'wip' in want: bpy.ops.wm.save_as_mainfile(filepath=f'{OUT}/wip.blend')
dg = bpy.context.evaluated_depsgraph_get()
tris = 0
for o_ in bpy.data.objects:
    if o_.type == 'MESH' and o_.name not in ('Floor', 'Old_Player', 'Old_Player_Visor'):
        me = o_.evaluated_get(dg).data; tris += sum(len(p_.vertices)-2 for p_ in me.polygons)
print('TRIS', tris, flush=True)
print('DONE', flush=True)
