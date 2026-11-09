# Builds the realistic human design model (same method as Art/Wick/wick.py).
# Run: blender -b --factory-startup --python human.py -- [face] [front three side noir]
# HUMAN_OUT sets where renders go; human.blend is saved next to this script on a full build.
import math, os, sys
import bpy, bmesh, numpy as np
from mathutils import noise, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get('HUMAN_OUT', HERE)
SAMPLES = int(os.environ.get('HUMAN_SAMPLES', 64))
want = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['front', 'three', 'side', 'noir', 'face']
HEAD_ONLY = want == ['face']

bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------- tube builder ----------
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

def norm(p):
    return [tuple(q) if len(q) == 6 else tuple(q)+(1.0, 1.0) for q in p]

def tube(pts, sides=18, per=7, cap=(1, 1)):
    S = catmull(norm(pts), per)
    C, R, W, D = S[:, :3], np.maximum(S[:, 3], 0.0004), S[:, 4], S[:, 5]
    T = np.gradient(C, axis=0)
    T /= np.maximum(np.linalg.norm(T, axis=1, keepdims=True), 1e-9)
    s = np.array([1.0, 0, 0]) if abs(T[0][0]) < 0.85 else np.array([0, 1.0, 0])
    Sd = []
    for t in T:
        s = s - s.dot(t)*t
        s /= np.linalg.norm(s)
        Sd.append(s)
    Sd = np.array(Sd)
    U = np.cross(T, Sd)
    a = np.linspace(0, 2*math.pi, sides, endpoint=False)
    ring = lambda c, r, w, d, s, u: c + r*w*np.cos(a)[:, None]*s + r*d*np.sin(a)[:, None]*u
    rings = []
    for k in [4, 3, 2, 1]:
        an = k/5*math.pi/2
        rings.append(ring(C[0]-T[0]*R[0]*math.sin(an)*cap[0], R[0]*math.cos(an), W[0], D[0], Sd[0], U[0]))
    for i in range(len(C)):
        rings.append(ring(C[i], R[i], W[i], D[i], Sd[i], U[i]))
    for k in [1, 2, 3, 4]:
        an = k/5*math.pi/2
        rings.append(ring(C[-1]+T[-1]*R[-1]*math.sin(an)*cap[1], R[-1]*math.cos(an), W[-1], D[-1], Sd[-1], U[-1]))
    V = np.vstack(rings)
    F = []
    n = len(rings)
    for i in range(n-1):
        for j in range(sides):
            j2 = (j+1) % sides
            F.append((i*sides+j, i*sides+j2, (i+1)*sides+j2, (i+1)*sides+j))
    F.append(tuple(reversed(range(sides))))
    F.append(tuple((n-1)*sides+j for j in range(sides)))
    return V, F

def mirror(pts):
    return [(-p[0],)+tuple(p[1:]) for p in pts]

class Group:
    def __init__(self): self.V, self.F, self.n = [], [], 0
    def raw(self, V, F):
        self.V.append(np.array(V, float))
        self.F += [tuple(i+self.n for i in f) for f in F]
        self.n += len(V)
    def add(self, pts, **kw):
        self.raw(*tube(pts, **kw))
    def both(self, pts, **kw):
        self.add(pts, **kw)
        self.add(mirror(pts), **kw)
    def blob(self, c, r, w=1, d=1, h=.2):
        self.add([(c[0], c[1], c[2]-r*h, r, w, d), (c[0], c[1], c[2]+r*h, r, w, d)], sides=18, per=2)
    def build(self, name, mat=None, smooth=True):
        me = bpy.data.meshes.new(name)
        me.from_pydata(np.vstack(self.V).tolist(), [], self.F)
        me.update()
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        if smooth:
            me.polygons.foreach_set('use_smooth', [True]*len(me.polygons))
            me.update()
        if mat: me.materials.append(mat)
        return ob

def lathe(profile, oval=1.0, sides=128, per=8, brim=None):
    """Spins an (r, z) profile round the Z axis. brim=(r_from, r_to, lift) curls the outer part up at the sides."""
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
    V = np.vstack(V)
    F = []
    for i in range(len(P)-1):
        for j in range(sides):
            j2 = (j+1) % sides
            F.append((i*sides+j, i*sides+j2, (i+1)*sides+j2, (i+1)*sides+j))
    return V, F

def sstep(x, a, b):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)

def select_only(ob):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)

def remesh(ob, voxel, iters, lam=.9):
    select_only(ob)
    m = ob.modifiers.new('r', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = voxel; m.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier='r')
    if iters:
        m = ob.modifiers.new('s', 'LAPLACIANSMOOTH'); m.iterations = iters; m.lambda_factor = lam; m.use_volume_preserve = True
        bpy.ops.object.modifier_apply(modifier='s')
    return ob

def surface(ob):
    me = ob.data
    n = len(me.vertices)
    V = np.empty(n*3); me.vertices.foreach_get('co', V)
    N = np.empty(n*3); me.vertex_normals.foreach_get('vector', N)
    return V.reshape(-1, 3), N.reshape(-1, 3)

def push(ob, V, N, disp):
    V2 = V + N*disp[:, None]
    ob.data.vertices.foreach_set('co', V2.ravel())
    ob.data.update()
    return V2

def gauss(V, c, sig):
    return np.exp(-(((V-np.array(c))/np.array(sig))**2).sum(1)/2)

def lump(V, scale, offset=(0, 0, 0)):
    o = Vector(offset)
    return np.array([noise.noise(Vector(p)*scale+o) for p in V])

def lumpv(V, scale):
    sx, sy, sz = scale
    return np.array([noise.noise(Vector((p[0]*sx, p[1]*sy, p[2]*sz))) for p in V])

# ---------- materials ----------
def pbsdf(name, **inp):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    n = m.node_tree.nodes.get('Principled BSDF')
    for k, v in inp.items():
        n.inputs[k.replace('_', ' ')].default_value = v
    return m, n

class Nodes:
    """Short names for the node chains the materials repeat."""
    def __init__(self, mat):
        self.t = mat.node_tree; self.N = self.t.nodes; self.L = self.t.links
        self.tc = self.N.new('ShaderNodeTexCoord')
    def tex(self, scale, detail=6, stretch=None):
        n = self.N.new('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
        src = self.tc.outputs['Object']
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

def make_skin():
    m, p = pbsdf('Skin', Roughness=.5, Specular_IOR_Level=.38)
    p.inputs['Subsurface Weight'].default_value = .55
    p.inputs['Subsurface Radius'].default_value = (.55, .3, .2)
    p.inputs['Subsurface Scale'].default_value = .0042
    n = Nodes(m)
    attr = n.N.new('ShaderNodeAttribute'); attr.attribute_name = 'tint'
    sep = n.N.new('ShaderNodeSeparateColor'); n.L.new(attr.outputs['Color'], sep.inputs[0])
    base = n.ramp(n.tex(14, 8), (.40, .245, .185, 1), (.56, .365, .285, 1))
    freck = n.ramp(n.tex(90, 5), (.9, .84, .8, 1), (1, 1, 1, 1), .3, .7)
    col = n.mix(1, base, freck, 'MULTIPLY')
    col = n.mix(sep.outputs[0], col, (.5, .2, .16, 1))            # flush: cheeks, nose, ears
    col = n.mix(n.math('MULTIPLY', sep.outputs[2], .3), col, (.11, .095, .09, 1))  # shadow of stubble
    col = n.mix(n.math('MULTIPLY', sep.outputs[1], .62), col, (.2, .075, .075, 1))           # lips
    n.L.new(col, p.inputs['Base Color'])
    rough = n.N.new('ShaderNodeMapRange')
    rough.inputs['To Min'].default_value = .42; rough.inputs['To Max'].default_value = .62
    n.L.new(n.tex(40, 4), rough.inputs['Value'])
    n.L.new(rough.outputs['Result'], p.inputs['Roughness'])
    vor = n.N.new('ShaderNodeTexVoronoi'); vor.feature = 'DISTANCE_TO_EDGE'; vor.inputs['Scale'].default_value = 1100
    n.L.new(n.tc.outputs['Object'], vor.inputs['Vector'])
    height = n.math('ADD', n.tex(170, 6), n.math('MULTIPLY', vor.outputs['Distance'], 1.2))
    n.L.new(n.bump(height, .12, .0008), p.inputs['Normal'])
    return m

skin = make_skin()

def make_hair():
    m, p = pbsdf('Hair', Roughness=.46)
    n = Nodes(m)
    streak = n.tex(180, 6, (1, 1, .06))
    n.L.new(n.ramp(streak, (.006, .005, .005, 1), (.05, .036, .028, 1)), p.inputs['Base Color'])
    n.L.new(n.bump(streak, .9, .003), p.inputs['Normal'])
    return m
hair_mat = make_hair()

def make_hat():
    m, p = pbsdf('Hat', Roughness=.36)
    n = Nodes(m)
    grain = n.tex(95, 5, (1, 1, .05))
    n.L.new(n.ramp(grain, (.009, .009, .011, 1), (.024, .024, .028, 1)), p.inputs['Base Color'])
    n.L.new(n.bump(n.tex(900, 3), .08, .0006), p.inputs['Normal'])
    return m
hat_mat = make_hat()

def make_band():
    m, p = pbsdf('Ribbon', Roughness=.42, Sheen_Weight=.6)
    n = Nodes(m)
    rib = n.tex(70, 1, (.04, .04, 28))
    n.L.new(n.ramp(rib, (.04, .006, .008, 1), (.075, .012, .014, 1)), p.inputs['Base Color'])
    n.L.new(n.bump(rib, .18, .0006), p.inputs['Normal'])
    return m
band_mat = make_band()

# ---------- head (faces -Y, feet at z=0, metres) ----------
# One smooth base, with every feature pushed into it as a soft brush stroke. Added tubes read as sausages on a face.
class Sculpt:
    def __init__(self, ob):
        self.ob = ob
        self.V, self.N = surface(ob)
        self.D = np.zeros_like(self.V)
        self.front = sstep(-self.V[:, 1], .01, .05)
    def _apply(self, val, axis):
        d = self.N if axis is None else np.tile(np.array(axis, float), (len(self.V), 1))
        self.D += d*val[:, None]
    def spot(self, c, sig, a, axis=None):
        """Soft dab at (x, z) on the front of the head."""
        q = ((self.V[:, 0]-c[0])/sig[0])**2 + ((self.V[:, 2]-c[1])/sig[1])**2
        self._apply(a*np.exp(-q/2)*self.front, axis)
    def stroke(self, pts, axis=None, per=8):
        """Brush stroke through (x, z, amount, width) points on the front of the head."""
        S = catmull(norm_stroke(pts), per) if len(pts) > 1 else np.array(pts, float)
        lo, hi = S[:, :2].min(0)-S[:, 3].max()*4, S[:, :2].max(0)+S[:, 3].max()*4
        idx = np.where((self.V[:, 0] > lo[0]) & (self.V[:, 0] < hi[0]) & (self.V[:, 2] > lo[1]) & (self.V[:, 2] < hi[1]) & (self.front > 0))[0]
        P = self.V[idx][:, [0, 2]]
        d2 = ((P[:, None, :]-S[None, :, :2])**2).sum(2)
        v = S[:, 2][None]*np.exp(-d2/(2*S[:, 3][None]**2))
        best = v[np.arange(len(idx)), np.abs(v).argmax(1)]
        val = np.zeros(len(self.V)); val[idx] = best*self.front[idx]
        self._apply(val, axis)
    def finish(self):
        V2 = self.V+self.D
        self.ob.data.vertices.foreach_set('co', V2.ravel())
        self.ob.data.update()
        return V2

def norm_stroke(p):
    return [tuple(q) for q in p]

base = Group()
base.add([
    (0, -.045, 1.576, .024, 1.25, 1.0),
    (0, -.040, 1.588, .035, 1.2, 1.05),
    (0, -.034, 1.603, .0445, 1.2, 1.1),
    (0, -.026, 1.622, .056, 1.14, 1.2),
    (0, -.016, 1.645, .064, 1.1, 1.3),
    (0, -.009, 1.668, .0705, 1.02, 1.36),
    (0, .000, 1.700, .0745, 1.0, 1.38),
    (0, .006, 1.740, .0745, 1.0, 1.32),
    (0, .012, 1.775, .060, 1.0, 1.3),
    (0, .014, 1.798, .028, 1.0, 1.2),
], sides=220, per=24, cap=(.4, 1))
base.add([(0, .014, 1.505, .053), (0, .018, 1.545, .0525), (0, .012, 1.612, .054)], sides=120, per=14)
for s in (1, -1):
    x = s*.0705
    base.add([(x, .012, 1.700, .018, .8, 1.2), (x, .014, 1.722, .016, .8, 1.1)], sides=14)  # ear root
    base.add([(x+s*.004, .014, 1.668, .0065, .6, 1), (x+s*.010, .018, 1.690, .012, .36, 1.2), (x+s*.0135, .022, 1.716, .0165, .3, 1.15), (x+s*.0115, .022, 1.736, .010, .35, 1.1)], sides=16)
    base.add([(x+s*.0125, .006, 1.712, .0028), (x+s*.015, .012, 1.733, .0032), (x+s*.017, .027, 1.737, .0036), (x+s*.0175, .038, 1.718, .0036), (x+s*.015, .036, 1.690, .003), (x+s*.009, .024, 1.668, .0028)], sides=10)

head = remesh(base.build('Head', skin), .0013, 30)
sc = Sculpt(head)
FORWARD = (0, -1, 0)

# nose: bridge to tip, then wings and nostrils
sc.stroke([(0, 1.712, .0015, .0062), (0, 1.692, .0085, .0062), (0, 1.668, .021, .0072), (0, 1.651, .0345, .0092), (0, 1.646, .0345, .0094)], FORWARD, per=10)
sc.spot((0, 1.6395), (.0066, .0056), -.012, FORWARD)                                    # tuck the base of the nose in
for s in (1, -1):
    sc.spot((s*.0150, 1.6465), (.0056, .0062), .0125, FORWARD)                          # wing
    sc.spot((s*.0190, 1.6545), (.0026, .0078), -.0034, FORWARD)                         # wing groove
    sc.spot((s*.0088, 1.6385), (.0034, .0022), -.0105, FORWARD)                         # nostril

# brow, sockets and cheek shape
for s in (1, -1):
    sc.stroke([(s*.008, 1.7215, .0034, .0065), (s*.030, 1.7265, .0066, .0062), (s*.056, 1.7255, .0052, .0058), (s*.072, 1.716, .0008, .006)])
    sc.spot((s*.0335, 1.7000), (.0165, .0085), -.0085, FORWARD)                         # socket
    sc.spot((s*.0125, 1.7020), (.0052, .0088), -.0028, FORWARD)                         # inner corner beside the bridge
    sc.stroke([(s*.074, 1.678, .0005, .009), (s*.060, 1.668, .0052, .0105), (s*.043, 1.661, .0036, .0098), (s*.026, 1.660, .0005, .009)])  # cheekbone
    sc.spot((s*.0595, 1.6275), (.0125, .0175), -.0048)                                  # hollow under the cheekbone
    sc.spot((s*.0715, 1.7145), (.0095, .018), -.0034)                                   # temple
    sc.stroke([(s*.0225, 1.6490, -.0006, .0024), (s*.0315, 1.6300, -.0022, .0026), (s*.0345, 1.6090, -.0014, .0028), (s*.0325, 1.5940, -.0002, .003)], FORWARD)  # nose-to-mouth crease
    sc.stroke([(s*.070, 1.640, .0006, .010), (s*.066, 1.612, .0032, .0115), (s*.047, 1.590, .0026, .0105), (s*.022, 1.578, .001, .009)])  # jaw line
    sc.stroke([(s*.021, 1.6835, -.0004, .0026), (s*.0335, 1.6815, -.0016, .0029), (s*.047, 1.6845, -.0006, .0026)])  # under-eye fold
sc.spot((0, 1.7085), (.0075, .0052), -.0030, FORWARD)                                   # bridge root
sc.spot((0, 1.752), (.046, .018), .0014)                                                # forehead

# mouth
sc.spot((0, 1.6145), (.031, .0215), .0088, FORWARD)                                     # teeth push the lips forward
UP_LIP = lambda ax: 1.6198 - .0056*(ax/.024)**2 - .0009*np.exp(-(ax/.0032)**2)
LOW_LIP = lambda ax: 1.6028 + .0080*(ax/.024)**2
MOUTH = lambda x: 1.6128 - .0007*(x/.024)**2 + .0007*np.exp(-(x/.005)**2)
xs = np.linspace(-.0235, .0235, 11)
sc.stroke([(x, float(MOUTH(x)+.0034-.0016*(x/.024)**2), .0040*(1-(x/.026)**2)+.0008, .0024) for x in xs], FORWARD)
sc.stroke([(x, float(MOUTH(x)-.0050+.0026*(x/.024)**2), .0065*(1-(x/.026)**2)+.0006, .0031) for x in xs], FORWARD)
sc.stroke([(x, float(MOUTH(x)), -.0048, .0011) for x in np.linspace(-.0238, .0238, 13)], FORWARD)   # the line between the lips
sc.spot((0, 1.6262), (.0032, .0058), -.0016, FORWARD)                                   # philtrum
sc.spot((0, 1.5945), (.0135, .0036), -.0030, FORWARD)                                   # under the lower lip
sc.spot((0, 1.5805), (.0165, .0118), .031, FORWARD)                                    # chin
for s in (1, -1):
    sc.spot((s*.0245, float(MOUTH(.0245))), (.0022, .0022), -.0018, FORWARD)            # mouth corner

# eye openings: an almond cut right through the skin, so the eyeball shows only inside it
EX, EZ, EY, ER = .0325, 1.6965, -.0716, .0135
def lid_top(t): return .0012*t + .0003 + .0048*np.sin(math.pi*np.clip(t, 0, 1)**.9)
def lid_bot(t): return .0012*t - .0002 - .0033*np.sin(math.pi*np.clip(t, 0, 1)**.9)
for s in (1, -1):
    ts = np.linspace(0, 1, 9)
    ux = lambda t: s*(EX-.0135+.027*t)
    sc.stroke([(float(ux(t)), float(EZ+lid_top(t)+.0009), .0015, .0014) for t in ts], FORWARD)                # upper rim
    sc.stroke([(float(ux(t)), float(EZ+lid_bot(t)-.0008), .0009, .0011) for t in ts], FORWARD)                # lower rim
    sc.stroke([(float(ux(t)), float(EZ+lid_top(t)+.0048), -.0013, .0012) for t in ts], FORWARD)               # lid crease
    V0 = sc.V
    t = (s*(V0[:, 0]-s*EX)+.0135)/.027
    dz = V0[:, 2]-EZ
    inside = sstep(t, 0, .03)*(1-sstep(t, .97, 1))*sstep(lid_top(t)-dz, 0, .0009)*sstep(dz-lid_bot(t), 0, .0009)
    side = (np.sign(V0[:, 0]) == s)
    sc.D[:, 1] += .025*inside*side*sc.front

Vc = sc.finish()
_, Nr = surface(head)
Vc = push(head, Vc, Nr, .00007*lump(Vc, 240)+.00016*lump(Vc, 60, (4, 1, 9)))

x, y, z = Vc[:, 0], Vc[:, 1], Vc[:, 2]
ax = np.abs(x)
flush = .22*np.exp(-(((ax-.052)/.022)**2+((z-1.655)/.02)**2)/2)*sstep(-y, .05, .08)
flush = np.maximum(flush, .28*np.exp(-((ax/.016)**2+((z-1.652)/.014)**2)/2)*sstep(-y, .1, .12))
flush = np.maximum(flush, .12*sstep(ax, .08, .09)*sstep(z, 1.66, 1.68)*(1-sstep(z, 1.73, 1.745)))
flush = np.maximum(flush, .08*(1-sstep(np.sqrt(((ax-EX)/.019)**2+((z-EZ)/.0105)**2), .75, 1.25)))
lip_c = (1-sstep(ax, .0205, .0242))*sstep(z-LOW_LIP(ax), 0, .0011)*sstep(UP_LIP(ax)-z, 0, .0011)*sstep(-y, .075, .085)
jaw = sstep(1.645-z, 0, .03)*sstep(-y, .0, .03)*sstep(z, 1.53, 1.56)
lipup = sstep(1.6465-z, 0, .008)*(1-sstep(ax, .03, .045))*sstep(z, 1.60, 1.61)*sstep(-y, .08, .1)
beard = np.clip(np.maximum(jaw, lipup)*(1-lip_c), 0, 1)
tint = head.data.color_attributes.new('tint', 'FLOAT_COLOR', 'POINT')
tint.data.foreach_set('color', np.column_stack([flush, lip_c, beard, np.ones(len(Vc))]).ravel())
head.data.update()

# eyebrows and lashes: real strands on the finished skin
FV = Vc[Vc[:, 1] < -.04]
def face_y(px, pz):
    return float(FV[((FV[:, 0]-px)**2+(FV[:, 2]-pz)**2).argmin(), 1])
strands = Group()
rng = np.random.default_rng(11)
for s in (1, -1):
    for _ in range(135):
        t = rng.uniform(0, 1)
        x0 = .0125+.049*t
        z0 = 1.7255+.0045*math.sin(math.pi*t**.75)-.0035*t + rng.uniform(-1, 1)*.0025*(1-.55*t)
        a = math.radians(72-58*t+rng.uniform(-12, 12))
        L = rng.uniform(.0045, .0072)
        px, pz = s*x0, z0
        ex, ez = px+s*L*math.cos(a), pz+L*math.sin(a)
        y0, y1 = face_y(px, pz), face_y(ex, ez)
        strands.add([(px, y0+.0002, pz, .00023), ((px+ex)/2, (y0+y1)/2-.0007, (pz+ez)/2, .00019), (ex, y1-.0004, ez, .00006)], sides=5, per=3)
    for t in np.linspace(.05, .97, 30):
        px, pz = s*(EX-.0135+.027*t), EZ+float(lid_top(t))+.0006
        y0 = face_y(px, pz)
        ln_ = rng.uniform(.0048, .0066)*(.72+.28*math.sin(math.pi*t))
        fan = s*(t-.42)*.007
        strands.add([(px, y0, pz, .00014), (px+fan*.45, y0-ln_*.62, pz+.0007, .00011), (px+fan, y0-ln_, pz+ln_*.55, .00003)], sides=5, per=3)
brow_mat, _ = pbsdf('Brow', Base_Color=(.028, .02, .016, 1), Roughness=.5)
strands.build('Brows_Lashes', brow_mat)

# eyes: colour is painted per vertex, so the iris follows the ball's curve
eye_mat, en = pbsdf('Eye', Roughness=.07, Specular_IOR_Level=.75)
ean = eye_mat.node_tree.nodes.new('ShaderNodeAttribute'); ean.attribute_name = 'col'
eye_mat.node_tree.links.new(ean.outputs['Color'], en.inputs['Base Color'])
for s in (1, -1):
    me = bpy.data.meshes.new('Eye'); bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=128, v_segments=96, radius=ER)
    bm.to_mesh(me); bm.free()
    me.polygons.foreach_set('use_smooth', [True]*len(me.polygons))
    n = len(me.vertices)
    P = np.empty(n*3); me.vertices.foreach_get('co', P); P = P.reshape(-1, 3)
    P[:, 0] += s*EX; P[:, 1] += EY; P[:, 2] += EZ
    me.vertices.foreach_set('co', P.ravel())
    rx, rz = P[:, 0]-s*EX, P[:, 2]-EZ
    rho = np.sqrt(rx*rx+rz*rz)*(P[:, 1] < EY)+(P[:, 1] >= EY)
    ang = np.arctan2(rz, rx)
    fib = .5+.5*np.sin(ang*41)*np.sin(ang*17+1)
    Ic = np.array([.06, .115, .15])
    col = np.tile(np.array([.72, .68, .64]), (n, 1))
    vein = .06*sstep(rho, .0065, .0125)
    col = col*(1-vein[:, None]) + np.array([.5, .13, .1])*vein[:, None]
    ir = 1-sstep(rho, .0055, .0060)
    iris_col = Ic[None]*(.55+.9*fib[:, None]*sstep(rho, .0026, .005)[:, None])
    col = col*(1-ir[:, None]) + iris_col*ir[:, None]
    limbal = np.exp(-((rho-.0058)/.0005)**2)
    col = col*(1-.7*limbal[:, None])
    pu = 1-sstep(rho, .0025, .0029)
    col = col*(1-pu[:, None]) + .002*pu[:, None]
    ca = me.color_attributes.new('col', 'FLOAT_COLOR', 'POINT')
    ca.data.foreach_set('color', np.column_stack([col, np.ones(n)]).ravel())
    me.update(); me.materials.append(eye_mat)
    ob = bpy.data.objects.new('Eye', me); bpy.context.collection.objects.link(ob)

# hair shows below the hat at the sides and back
hair = Group()
hair.add([(0, .040, 1.645, .056, 1.25, 1.16), (0, .034, 1.690, .074, 1.08, 1.18), (0, .026, 1.735, .0775, 1.04, 1.27), (0, .014, 1.778, .066, 1.03, 1.28), (0, .014, 1.804, .03, 1, 1.2)], sides=48, per=10)
hairob = remesh(hair.build('Hair', hair_mat), .003, 20)
V, Nr = surface(hairob)
ear_gap = sum(gauss(V, (s_*.085, .022, 1.703), (.016, .022, .03)) for s_ in (1, -1))  # hair clears the ears
hairob_V = push(hairob, V, Nr, .0016*lump(V, 60)+.002*lump(V, 22, (3, 8, 1))-.012*ear_gap)

# top hat: oval to fit the head, waisted crown, brim rolled up at the sides
ZB = 1.774
OVAL = 1.2
hat_profile = [(.002, ZB+.004), (.05, ZB+.002), (.094, ZB-.001), (.125, ZB-.0035), (.1455, ZB-.002), (.1525, ZB+.0035), (.1495, ZB+.0075), (.128, ZB+.0045), (.101, ZB+.0065),
               (.0945, ZB+.018), (.0905, ZB+.052), (.0925, ZB+.098), (.0985, ZB+.143), (.1015, ZB+.1585), (.0985, ZB+.1655), (.08, ZB+.167), (.002, ZB+.167)]
hat = Group()
hat.raw(*lathe(hat_profile, OVAL, brim=(.102, .1525, .02, .004)))
hat.build('Top_Hat', hat_mat)
band = Group()
band.raw(*lathe([(.0925, ZB+.0115), (.0948, ZB+.013), (.0955, ZB+.024), (.0945, ZB+.036), (.0912, ZB+.0385)], OVAL, per=4))
band.build('Hat_Band', band_mat)

# ---------- clothes and hands ----------
if not HEAD_ONLY:
    def make_cloth(name, a, b, rough, weave, strength):
        m, p = pbsdf(name, Roughness=rough, Sheen_Weight=.12)
        n = Nodes(m)
        col = n.ramp(n.tex(70, 8), a, b, .36, .64)
        geo = n.N.new('ShaderNodeNewGeometry')
        cav = n.ramp(geo.outputs['Pointiness'], (.68, .65, .62, 1), (1, 1, 1, 1), .44, .52)
        n.L.new(n.mix(1, col, cav, 'MULTIPLY'), p.inputs['Base Color'])
        w1 = n.tex(weave, 2, (1, 1, .12)); w2 = n.tex(weave, 2, (.12, .12, 1))
        n.L.new(n.bump(n.math('ADD', w1, w2), strength, .0007), p.inputs['Normal'])
        return m

    def make_leather(name, a, b, rough):
        m, p = pbsdf(name, Roughness=rough)
        n = Nodes(m)
        n.L.new(n.ramp(n.tex(38, 7), a, b), p.inputs['Base Color'])
        n.L.new(n.bump(n.tex(520, 4), .16, .0007), p.inputs['Normal'])
        return m

    coat_mat = make_cloth('Coat', (.098, .083, .058, 1), (.135, .114, .08, 1), .68, 900, .12)
    trouser_mat = make_cloth('Trousers', (.011, .011, .014, 1), (.024, .024, .029, 1), .66, 1200, .08)
    shirt_mat = make_cloth('Shirt', (.46, .45, .43, 1), (.62, .61, .58, 1), .6, 1400, .05)
    tie_mat = make_cloth('Tie', (.038, .006, .008, 1), (.06, .009, .012, 1), .56, 1500, .05)
    glove_mat = make_leather('Glove', (.012, .009, .008, 1), (.04, .028, .022, 1), .36)
    shoe_mat = make_leather('Shoe', (.012, .007, .006, 1), (.03, .015, .012, 1), .3)
    sole_mat, _ = pbsdf('Sole', Base_Color=(.01, .009, .008, 1), Roughness=.55)
    button_mat, _ = pbsdf('Button', Base_Color=(.03, .018, .012, 1), Roughness=.25)

    def on_surface(ob_V, px, pz, front=True):
        P = ob_V[ob_V[:, 1] < 0] if front else ob_V
        i = ((P[:, 0]-px)**2+(P[:, 2]-pz)**2).argmin()
        return float(P[i, 1])

    # coat: one surface from hem to collar, with the sleeves fused in at the shoulders
    coat = Group()
    coat.add([(0, .01, .50, .215, 1.18, .80), (0, .01, .72, .19, 1.12, .82), (0, .01, .95, .175, 1.06, .80), (0, .005, 1.08, .160, 1.05, .78), (0, 0, 1.22, .165, 1.08, .78),
              (0, -.005, 1.34, .172, 1.10, .76), (0, 0, 1.43, .155, 1.2, .76), (0, .008, 1.49, .095, 1.25, .98), (0, .012, 1.525, .068, 1, 1.02)], sides=64, per=10, cap=(.015, .5))
    coat.both([(.04, .005, 1.482, .05), (.13, .005, 1.462, .054), (.185, .005, 1.432, .059)], sides=24)
    coat.both([(.185, .005, 1.425, .059), (.212, .018, 1.27, .053), (.232, .022, 1.13, .049), (.248, -.005, 1.0, .045), (.258, -.038, .885, .044)], sides=32, cap=(1, .06))
    coat_ob = remesh(coat.build('Coat', coat_mat), .004, 7)
    V, Nr = surface(coat_ob)
    x, y, z = V[:, 0], V[:, 1], V[:, 2]
    ax = np.abs(x)
    sleeve = sstep(ax, .183, .203)*sstep(z, .86, .9)
    th = np.arctan2(y-.01, x)
    ln = lump(V, 7)
    d = .0035*ln + .0022*lump(V, 19, (5, 2, 8))
    skirt = sstep(1.02-z, 0, .3)*(1-sleeve)
    drape = lumpv(V, (17, 17, 1.9))
    ridge = np.clip(1-np.abs(lumpv(V, (26, 26, 2.6)))*3.2, 0, 1)**2
    d += skirt*(.016*drape+.0075*ridge)+.0045*skirt*np.sin(th*7+1.3*np.sin(z*5)+ln*3)
    torso = (1-sleeve)*sstep(z, 1.12, 1.18)*(1-sstep(z, 1.38, 1.44))
    d += torso*(.0075*lumpv(V, (11, 11, 3.5))+.0035*lumpv(V, (6, 20, 22)))
    d += .0048*sleeve*lumpv(V, (9, 9, 34))
    pinch = np.exp(-((z-1.07)/.06)**2)*(1-sleeve)
    d += .0036*pinch*np.sin(th*17+np.sin(z*30)+ln*6)
    d += .0052*sleeve*np.exp(-((z-1.13)/.075)**2)*np.sin(z*125+ln*7)
    d += .0026*sleeve*np.sin(z*60+th*3+ln*5)*sstep(1.4-z, 0, .25)
    d += .0028*(1-sleeve)*np.sin(z*46+th*5+ln*4)*sstep(1.4-z, 0, .1)*(1-sstep(1.1-z, 0, .05))*sstep(ax, .1, .17)  # chest pulls
    front = sstep(-y, .03, .07)
    # V opening at the chest, pushed in behind the shirt
    half = np.clip((z-1.16)/(1.48-1.16), 0, 1)*.088
    d += -.052*sstep(half-ax, 0, .008)*(z > 1.16)*front
    # double-breasted closing edge, and the vent at the back of the skirt
    d += -.0075*np.exp(-((x+.082)/.0032)**2)*front*(1-sstep(z, 1.16, 1.2))*(1-sleeve)
    d += -.0075*np.exp(-(x/.0032)**2)*sstep(-y, .05, .1)*0
    d += -.0085*np.exp(-(x/.0035)**2)*sstep(y, .05, .1)*(1-sstep(z, .84, .88))
    # stitched yoke and cuff strap lines
    d += -.0016*np.exp(-((z-1.335)/.0022)**2)*sstep(y, .03, .08)*(1-sleeve)
    d += -.0022*sleeve*np.exp(-((z-.945)/.0028)**2)
    coat_V = push(coat_ob, V, Nr, d)

    lapel = Group()
    for s in (1, -1):
        lapel.add([(s*.018, -.143, 1.16, .006, 1, .5), (s*.05, -.145, 1.24, .03, 1.25, .2), (s*.082, -.14, 1.33, .04, 1.25, .18), (s*.108, -.128, 1.41, .043, 1.2, .18), (s*.112, -.108, 1.46, .03, 1.1, .22)], sides=20, per=8)
    # collar: one flat band from lapel to lapel round the back of the neck
    half_collar = [(.112, -.105, 1.468), (.105, -.06, 1.497), (.098, -.01, 1.518), (.076, .05, 1.526)]
    path = half_collar + [(0, .094, 1.527)] + [(-q[0], q[1], q[2]) for q in reversed(half_collar)]
    lapel.add([q+(.031, .26, 1) for q in path], sides=14, per=8)
    remesh(lapel.build('Lapels', coat_mat), .0018, 10)

    belt = Group()
    a_ = np.linspace(-math.pi/2+.22, 1.5*math.pi+.22+.2, 30)
    belt.add([(.178*math.cos(t), .005+.137*math.sin(t), 1.07, .024, .28, 1) for t in a_], sides=12, per=3)
    remesh(belt.build('Belt', coat_mat), .0035, 3)

    shirt = Group()
    shirt.add([(0, 0, 1.18, .12, 1.15, .84), (0, -.01, 1.30, .14, 1.1, .85), (0, 0, 1.45, .10, 1.1, .8), (0, .012, 1.53, .056)], sides=40, cap=(.1, .3))
    ca_ = np.linspace(math.pi*-.42, math.pi*1.42, 24)
    shirt.add([(.0635*math.cos(t), .018+.0635*math.sin(t), 1.538-.012*math.sin(t), .021, .22, 1) for t in ca_], sides=12, per=3)
    shirt.both([(.012, -.054, 1.552, .01, 1, .35), (.042, -.066, 1.518, .019, 1.2, .24), (.066, -.074, 1.476, .007, 1, .4)], sides=12, per=6)
    remesh(shirt.build('Shirt', shirt_mat), .002, 10)

    tie = Group()
    tie.add([(0, -.053, 1.528, .0165, 1.25, .7), (0, -.059, 1.502, .012, .95, .7)], sides=14)
    tie.add([(0, -.063, 1.498, .012, 1, .35), (0, -.095, 1.42, .024, 1, .16), (0, -.135, 1.30, .034, 1, .12), (0, -.135, 1.22, .04, 1, .12), (0, -.13, 1.16, .02, 1, .2)], sides=14, per=8)
    remesh(tie.build('Tie', tie_mat), .0014, 8)

    legs = Group()
    legs.both([(.10, 0, .72, .072), (.098, -.005, .5, .06), (.096, .01, .26, .056), (.094, .012, .082, .062)], sides=28, cap=(1, .06))
    leg_ob = remesh(legs.build('Trousers', trouser_mat), .004, 8)
    V, Nr = surface(leg_ob)
    x, y, z = V[:, 0], V[:, 1], V[:, 2]
    ln = lump(V, 9)
    d = .003*ln
    d += .0034*np.exp(-((np.abs(x)-.097)/.0042)**2)*sstep(-y, .02, .05)                    # front crease
    d += .0062*(1-sstep(z, .13, .26))*np.sin(z*72+x*30+ln*5)                               # break over the shoe
    d += .003*(1-sstep(z, .42, .58))*sstep(z, .3, .4)*np.sin(z*52+ln*6)
    push(leg_ob, V, Nr, d)

    # shoes: faces -Y, so the tube axis runs front to back
    shoes, soles, laces = Group(), Group(), Group()
    for s in (1, -1):
        X = s*.094
        shoes.add([(X, .07, .056, .040, .95, 1.05), (X, .02, .066, .049, 1, 1.15), (X, -.04, .061, .05, 1.02, .98), (X, -.105, .043, .047, 1.02, .72), (X, -.15, .036, .037, 1, .6), (X, -.172, .034, .02, .95, .6)], sides=28, per=10)
        shoes.add([(X, .03, .06, .047), (X, .026, .112, .042)], sides=24)
        soles.add([(X, .074, .0135, .043, 1, .28), (X, -.02, .0135, .05, 1.05, .25), (X, -.11, .0135, .05, 1.1, .25), (X, -.156, .0135, .036, 1.1, .3), (X, -.178, .0135, .018, 1.05, .35)], sides=24, per=8)
        soles.add([(X, .083, .022, .033, 1.05, .55), (X, .05, .022, .036, 1.05, .55)], sides=20, per=3, cap=(.3, .1))
        for k, yy in enumerate(np.linspace(-.075, -.015, 5)):
            zz = .1055+(.0+.0)-.0+(-.0)+ (yy+.015)*.35 - .0
            laces.add([(X-.021, yy-.002, zz-.006, .0019), (X, yy, zz+.0015, .0021), (X+.021, yy+.002, zz-.006, .0019)], sides=8, per=4)
    remesh(shoes.build('Shoes', shoe_mat), .003, 8)
    remesh(soles.build('Soles', sole_mat), .003, 3)
    laces.build('Laces', sole_mat)

    # gloves: palms in, thumbs forward, fingers relaxed and curling toward the leg
    glove = Group()
    for s in (1, -1):
        W = np.array([s*.259, -.04, .885])
        ph = -s*math.radians(34)
        def turn(q):
            """Rotates hand points about the vertical through the wrist, keeping radius and flattening values."""
            dx, dy = q[0]-W[0], q[1]-W[1]
            return (W[0]+dx*math.cos(ph)-dy*math.sin(ph), W[1]+dx*math.sin(ph)+dy*math.cos(ph))+tuple(q[2:])
        add = lambda pts, **kw: glove.add([turn(q) for q in pts], **kw)
        glove.add([(W[0], W[1], W[2]+.03, .036, .92, 1.05), (W[0], W[1], W[2]-.005, .0295, .8, 1.1)], sides=20, per=4, cap=(.08, 1))
        add([(W[0], W[1], W[2]-.005, .027, .8, 1.1), (W[0]-s*.002, W[1]-.004, W[2]-.05, .03, .72, 1.5), (W[0]-s*.004, W[1]-.006, W[2]-.10, .031, .62, 1.58)], sides=20, per=6)
        for i, L in enumerate([.083, .093, .087, .07]):
            dy = (-1.5+i)*.0245
            P = W + np.array([-s*.005, -.006+dy, -.122])
            pts = [tuple(P)+(.0108,)]
            th_f = math.radians(3)
            for frac, bend, r in ((.46, 11, .0103), (.30, 18, .0096), (.24, 16, .0086)):
                th_f += math.radians(bend)
                P = P + L*frac*np.array([-s*math.sin(th_f), 0, -math.cos(th_f)])
                pts.append(tuple(P)+(r,))
            add(pts, sides=12, per=6)
        T0 = W+np.array([-s*.002, -.03, -.028])
        T1 = T0+np.array([-s*.006, -.03, -.045])
        T2 = T1+np.array([-s*.013, -.011, -.036])
        T3 = T2+np.array([-s*.015, .001, -.028])
        add([tuple(T0)+(.0165,), tuple(T1)+(.0125,), tuple(T2)+(.011,), tuple(T3)+(.0095,)], sides=14, per=6)
    remesh(glove.build('Gloves', glove_mat), .0024, 8)

    # buttons and buckle
    bits = Group()
    for s in (1, -1):
        for zz in (1.27, 1.18, .97, .86):
            bits.blob((s*.066, on_surface(coat_V, s*.066, zz)-.0035, zz), .0115, 1, .32, .02)
    bits.blob((0, -.1465, 1.07), .021, 1.05, .16, .55)
    bits.build('Buttons', button_mat)


# ---------- stage ----------
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
scene.cycles.device = 'CPU'
scene.render.threads_mode = 'FIXED'; scene.render.threads = 12
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

cam = bpy.data.cameras.new('c'); cam.sensor_fit = 'VERTICAL'; cam.sensor_height = 24
co = bpy.data.objects.new('c', cam); bpy.context.collection.objects.link(co); scene.camera = co
def shoot(name, az, height, dist, target, lens):
    t = Vector(target); a = math.radians(az)
    co.location = t + Vector((math.sin(a)*dist, -math.cos(a)*dist, height-target[2]))
    co.rotation_euler = (t-co.location).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = lens
    scene.render.filepath = f'{OUT}/{name}.png'
    bpy.ops.render.render(write_still=True)
    print('rendered', name, flush=True)

def noir(on, side=False):
    key.hide_render = rim.hide_render = on
    lo.hide_render = fo.hide_render = not on
    old_player.hide_render = visor.hide_render = not side
    bg.inputs[0].default_value = (.012, .012, .014, 1) if on else (.16, .16, .17, 1)
    bg.inputs[1].default_value = .35 if on else 1

fm, fn = pbsdf('Floor', Roughness=.75)
n = Nodes(fm)
n.L.new(n.ramp(n.tex(.7, 10), (.07, .07, .07, 1), (.24, .24, .23, 1)), fn.inputs['Base Color'])
bpy.ops.mesh.primitive_plane_add(size=24); bpy.context.object.data.materials.append(fm)

# the old capsule player for scale, side view only
pm, _ = pbsdf('Old_Player', Base_Color=(.2, .25, .34, 1), Roughness=.6)
me_ = bpy.data.meshes.new('Old_Player'); bm_ = bmesh.new(); bmesh.ops.create_uvsphere(bm_, u_segments=96, v_segments=64, radius=.35)
for v_ in bm_.verts: v_.co += Vector((0, .75, 1.45 if v_.co.z > 0 else .35))
bm_.to_mesh(me_); bm_.free(); me_.polygons.foreach_set('use_smooth', [True]*len(me_.polygons)); me_.update(); me_.materials.append(pm)
old_player = bpy.data.objects.new('Old_Player', me_); bpy.context.collection.objects.link(old_player)
bpy.ops.mesh.primitive_cube_add(size=1, location=(.0, .45, 1.5), scale=(.35, .15, .12)); visor = bpy.context.object
vm, _ = pbsdf('Visor', Base_Color=(.02, .02, .022, 1), Roughness=.4); visor.data.materials.append(vm)

lamp = bpy.data.lights.new('lamp', 'SPOT'); lamp.energy = 900; lamp.spot_size = math.radians(80); lamp.spot_blend = .12; lamp.shadow_soft_size = .05; lamp.color = (1, .86, .62)
lo = bpy.data.objects.new('lamp', lamp); bpy.context.collection.objects.link(lo); lo.location = (-1.0, -.6, 3.6)
lo.rotation_euler = (Vector((0, -.05, 1.0))-lo.location).to_track_quat('-Z', 'Y').to_euler()
flash = bpy.data.lights.new('flash', 'SPOT'); flash.energy = 70; flash.spot_size = math.radians(30); flash.spot_blend = .5; flash.shadow_soft_size = .02
fo = bpy.data.objects.new('flash', flash); bpy.context.collection.objects.link(fo); fo.location = (.5, -4.2, 1.35)
fo.rotation_euler = (Vector((0, 0, 1.45))-fo.location).to_track_quat('-Z', 'Y').to_euler()

BODY = (0, -.03, 1.0)
if 'front' in want: noir(False); shoot('p1_front', 0, 1.18, 8.6, BODY, 85)
if 'three' in want: noir(False); shoot('p2_three', 215, 1.18, 8.6, BODY, 85)
if 'side' in want: noir(False, side=True); shoot('p3_side', 90, 1.18, 10.5, (0, .15, 1.0), 85)
if 'noir' in want: noir(True); shoot('p4_noir', 22, 1.18, 8.6, BODY, 85)
if 'face' in want:
    noir(False)
    shoot('p5_face', 25, 1.74, 2.4, (0, -.04, 1.70), 85)
if 'back' in want:
    noir(False)
    for az_ in (130, 180, 235):
        shoot(f'back_{az_}', az_, 1.62, 1.5, (0, .02, 1.60), 85)
if HEAD_ONLY:
    shoot('p5_face_close', 0, 1.68, .9, (0, -.08, 1.65), 85)
if not HEAD_ONLY and set(want) >= {'front', 'three', 'side', 'noir', 'face'}:
    names = ['p1_front', 'p2_three', 'p3_side', 'p4_noir', 'p5_face']
    imgs = [bpy.data.images.load(f'{OUT}/{n_}.png') for n_ in names]
    w, h = imgs[0].size
    px = [np.array(i.pixels[:]).reshape(h, w, 4) for i in imgs]
    sheet = np.hstack(px)
    out = bpy.data.images.new('sheet', sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels = sheet.ravel().tolist()
    out.filepath_raw = f'{HERE}/human_design_sheet.png'; out.file_format = 'PNG'; out.save()
    bpy.ops.wm.save_as_mainfile(filepath=f'{HERE}/human.blend', compress=True)
print('TRIS', sum(sum(len(p_.vertices)-2 for p_ in o_.data.polygons) for o_ in bpy.data.objects if o_.type == 'MESH' and o_.name not in ('Plane', 'Old_Player', 'Cube')), flush=True)
print('DONE', flush=True)
