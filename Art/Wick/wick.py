import math, os, sys
import bpy, numpy as np
from mathutils import noise, Vector

OUT = os.path.dirname(os.path.abspath(__file__)) if '__file__' in dir() else '.'
SAMPLES = 64
VOXEL = 0.0075

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
    return [tuple(q) + (1.0, 1.0) if len(q) == 4 else tuple(q) for q in p]

def tube(pts, sides=18, per=7):
    S = catmull(norm(pts), per)
    C, R, W, D = S[:, :3], np.maximum(S[:, 3], 0.0006), S[:, 4], S[:, 5]
    T = np.gradient(C, axis=0)
    T /= np.maximum(np.linalg.norm(T, axis=1, keepdims=True), 1e-9)
    ref = np.array([1.0, 0, 0]) if abs(T[0][0]) < 0.85 else np.array([0, 1.0, 0])
    Sd = []
    s = ref
    for t in T:
        s = s - s.dot(t)*t
        s /= np.linalg.norm(s)
        Sd.append(s)
    Sd = np.array(Sd)
    U = np.cross(T, Sd)
    rings = []
    a = np.linspace(0, 2*math.pi, sides, endpoint=False)
    def ring(c, r, w, d, s, u):
        return c + r*w*np.cos(a)[:, None]*s + r*d*np.sin(a)[:, None]*u
    for k in [4, 3, 2, 1]:
        an = k/5*math.pi/2
        rings.append(ring(C[0]-T[0]*R[0]*math.sin(an), R[0]*math.cos(an), W[0], D[0], Sd[0], U[0]))
    for i in range(len(C)):
        rings.append(ring(C[i], R[i], W[i], D[i], Sd[i], U[i]))
    for k in [1, 2, 3, 4]:
        an = k/5*math.pi/2
        rings.append(ring(C[-1]+T[-1]*R[-1]*math.sin(an), R[-1]*math.cos(an), W[-1], D[-1], Sd[-1], U[-1]))
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

class Group:
    def __init__(self): self.V, self.F, self.n = [], [], 0
    def add(self, pts, **kw):
        V, F = tube(pts, **kw)
        self.V.append(V)
        self.F += [tuple(i+self.n for i in f) for f in F]
        self.n += len(V)
    def both(self, pts, **kw):
        self.add(pts, **kw)
        self.add([(-p[0],)+tuple(p[1:]) for p in pts], **kw)
    def blob(self, c, r, w=1, d=1):
        self.add([(c[0], c[1], c[2]-r*.2, r, w, d), (c[0], c[1], c[2]+r*.2, r, w, d)], sides=14, per=2)
    def build(self, name, mat=None, smooth=True):
        me = bpy.data.meshes.new(name)
        me.from_pydata(np.vstack(self.V).tolist(), [], self.F)
        me.update()
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        if smooth:
            me.polygons.foreach_set('use_smooth', [True]*len(me.polygons))
            me.update()
        if mat: me.materials.append(mat)
        return ob

# ---------- anatomy (faces -Y, feet at z=0, metres) ----------
body, horn, teeth, eyes, organ = Group(), Group(), Group(), Group(), Group()

SPINE = [
    (0, .02, .95, .105, 1.12, .95),
    (0, .05, 1.08, .098, 1.05, .9),
    (0, .06, 1.20, .082, 1.05, .85),   # starved waist
    (0, .045, 1.32, .118, 1.15, .95),
    (0, .01, 1.47, .155, 1.2, 1.0),
    (0, -.04, 1.61, .165, 1.2, 1.0),
    (0, -.10, 1.75, .135, 1.25, .95),  # hump
    (0, -.19, 1.85, .085, 1.0, .9),
]
body.add(SPINE, sides=28, per=10)
# neck thrusts forward and the head hangs, so the eyes end up at the 1.7 m the Enemy script sees from
body.add([(0, -.17, 1.84, .075), (0, -.27, 1.875, .055), (0, -.35, 1.85, .048), (0, -.42, 1.80, .05)])
# neck tendons
body.both([(.045, -.16, 1.83, .016), (.04, -.30, 1.80, .012), (.035, -.40, 1.75, .012)], sides=10)
# skull
body.add([
    (0, -.36, 1.855, .05, .9, 1), (0, -.42, 1.85, .094, .88, 1), (0, -.495, 1.795, .095, .86, 1),
    (0, -.55, 1.725, .072, .92, 1), (0, -.595, 1.665, .047, .95, 1), (0, -.625, 1.61, .026, 1, 1),
], sides=24, per=10)
body.both([(.082, -.565, 1.722, .018), (.04, -.602, 1.738, .021), (.004, -.612, 1.728, .016)], sides=10)  # brow ridge
body.both([(.082, -.48, 1.69, .02), (.075, -.55, 1.655, .014), (.045, -.60, 1.635, .01)], sides=10)        # cheek bone
# hanging jaw
body.both([(.078, -.45, 1.72, .022), (.078, -.51, 1.61, .019), (.05, -.575, 1.525, .017), (.008, -.625, 1.492, .019)], sides=12)
body.add([(0, -.585, 1.497, .014, 2.2, .6), (0, -.54, 1.517, .032, 1.6, .6), (0, -.46, 1.585, .055, 1.2, .9), (0, -.40, 1.68, .055)], sides=16)
body.add([(0, -.61, 1.50, .015), (0, -.52, 1.525, .04, 1.2, .8), (0, -.42, 1.62, .05), (0, -.36, 1.74, .05)], sides=14)  # throat

# shoulder girdle
body.both([(.015, -.235, 1.66, .018), (.13, -.235, 1.70, .02), (.26, -.18, 1.745, .032)], sides=12)
body.both([(.03, -.18, 1.86, .05), (.13, -.14, 1.81, .07, 1.1, .7), (.26, -.12, 1.75, .075)], sides=16)  # trapezius
body.both([(.20, .06, 1.76, .045, 1.3, .7), (.18, .10, 1.62, .05, 1.25, .7), (.11, .12, 1.50, .02)], sides=14)  # shoulder blade
# arms: too long, elbows out
ARM = [(.28, -.13, 1.72, .08), (.325, -.11, 1.54, .056), (.375, -.075, 1.35, .036), (.40, -.05, 1.20, .036),
       (.42, -.13, 1.04, .054), (.43, -.24, .86, .038), (.435, -.32, .68, .023)]
body.both(ARM, sides=16)
body.both([(.40, -.02, 1.235, .025), (.405, .002, 1.20, .022)], sides=10)  # elbow point
# forearm sinews
body.both([(.40, -.09, 1.14, .012), (.455, -.17, .98, .018), (.445, -.30, .72, .009)], sides=8)
body.both([(.415, -.03, 1.15, .010), (.41, -.21, .94, .016), (.425, -.31, .70, .008)], sides=8)

# hand and fingers
def hand(sign):
    W = np.array([sign*.435, -.32, .68])
    body.add([tuple(W)+(.024,), (W[0], W[1]-.035, W[2]-.075, .03, 1.5, .55), (W[0], W[1]-.05, W[2]-.125, .03, 1.75, .45)], sides=12)
    for i, ln in enumerate([.78, .9, .85, .7]):
        dx = (i-1.5)*.027*sign
        R = W + np.array([dx, -.055, -.135])
        pts = [R, R+np.array([dx*.5, -.03, -.13*ln]), R+np.array([dx*.8, -.03, -.27*ln]), R+np.array([dx*.9, .03, -.38*ln])]
        body.add([tuple(pts[0])+(.0145,), tuple(pts[1])+(.0135,), tuple(pts[2])+(.0115,), tuple(pts[3])+(.0088,)], sides=10, per=6)
        for k in (1, 2):
            body.blob(pts[k], .0175-.002*k)
        tip = pts[3]
        horn.add([tuple(tip)+(.0095,), tuple(tip+np.array([0, .018, -.05]))+(.0065,), tuple(tip+np.array([0, .055, -.105]))+(.0004,)], sides=10, per=6)
    T = W + np.array([-sign*.038, -.02, -.05])
    horn_base = T + np.array([-sign*.045, -.075, -.13])
    body.add([tuple(T)+(.017,), tuple(T+np.array([-sign*.03, -.045, -.07]))+(.013,), tuple(horn_base)+(.01,)], sides=10)
    horn.add([tuple(horn_base)+(.01,), tuple(horn_base+np.array([0, .015, -.08]))+(.0004,)], sides=10, per=6)
hand(1); hand(-1)

# legs: permanent crouch
body.both([(.10, .02, .97, .078), (.135, -.045, .76, .066), (.155, -.11, .58, .045), (.165, -.135, .50, .041)], sides=18)
body.both([(.165, -.135, .50, .044), (.165, -.075, .40, .05), (.165, .0, .25, .036), (.165, .045, .10, .027)], sides=16)
body.both([(.165, -.16, .50, .032)], sides=12) if False else None
body.both([(.175, -.02, .92, .02), (.20, -.06, .76, .028), (.18, -.12, .56, .012)], sides=10)
body.both([(.06, -.02, .90, .018), (.085, -.075, .72, .026), (.13, -.13, .55, .012)], sides=10)
body.both([(.12, .07, .92, .018), (.15, .02, .74, .024), (.17, -.06, .54, .012)], sides=10)
body.both([(.19, -.05, .43, .018), (.195, .0, .30, .02), (.17, .03, .16, .007)], sides=10)
body.blob((.165, -.165, .505), .036); body.blob((-.165, -.165, .505), .036)
body.both([(.11, -.055, 1.03, .018), (.07, -.11, .98, .012)], sides=8)  # hip crest
# foot and long toes
def foot(sign):
    A = np.array([sign*.165, .045, .10])
    body.add([tuple(A)+(.03,), (A[0], .065, .04, .036), (A[0], -.05, .036, .03, 1.5, .7), (sign*.17, -.15, .028, .025, 2.2, .6)], sides=14)
    for i, ln in enumerate([.8, 1.0, .9]):
        dx = (i-1)*.042
        R = np.array([sign*.17+dx, -.15, .022])
        a, b = R+np.array([dx*.3, -.09*ln, .013]), R+np.array([dx*.5, -.18*ln, .0])
        body.add([tuple(R)+(.018,), tuple(a)+(.0155,), tuple(b)+(.012,)], sides=10, per=6)
        body.blob(a, .0195)
        horn.add([tuple(b)+(.0125,), tuple(b+np.array([0, -.04, -.003]))+(.008,), tuple(b+np.array([0, -.082, -.012]))+(.0004,)], sides=10, per=6)
foot(1); foot(-1)

# teeth: needles, slightly uneven
rng = np.random.default_rng(7)
def tooth(g, base, down, ln, r):
    tip = base + down*ln
    g.add([tuple(base)+(r,), tuple(base+down*ln*.5+np.array([0, rng.uniform(-.003, .003), 0]))+(r*.55,), tuple(tip)+(.0003,)], sides=7, per=4)
for s in (1, -1):
    for t in np.linspace(0, 1, 10):
        b = np.array([s*(.009+.046*t), -.632+.10*t, 1.602+.038*t])
        tooth(teeth, b, np.array([s*.05, -.15, -1.0]), rng.uniform(.018, .034)*(1-.3*t), .0052)
    for t in np.linspace(0, 1, 7):
        b = np.array([s*(.012+.058*t), -.618+.085*t, 1.50+.075*t])
        tooth(teeth, b, np.array([s*.03, -.1, 1.0]), rng.uniform(.014, .026), .0044)

# deep-set night eyes, set at 1.7 m
for s in (1, -1):
    eyes.blob((s*.05, -.577, 1.697), .0105, 1.15, .75)

# ---------- materials ----------
def pbsdf(name, **inp):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    n = m.node_tree.nodes.get('Principled BSDF')
    for k, v in inp.items():
        n.inputs[k.replace('_', ' ')].default_value = v
    return m, n

skin, sk = pbsdf('Skin', Roughness=.5)
sk.inputs['Subsurface Weight'].default_value = .35
sk.inputs['Subsurface Radius'].default_value = (.7, .16, .1)
sk.inputs['Subsurface Scale'].default_value = .007
nt = skin.node_tree
N, L = nt.nodes, nt.links
tc = N.new('ShaderNodeTexCoord')
mott = N.new('ShaderNodeTexNoise'); mott.inputs['Scale'].default_value = 8; mott.inputs['Detail'].default_value = 9
L.new(tc.outputs['Object'], mott.inputs['Vector'])
ramp = N.new('ShaderNodeValToRGB'); r = ramp.color_ramp
r.elements[0].position = .3; r.elements[0].color = (.15, .125, .12, 1)
r.elements[1].position = .7; r.elements[1].color = (.30, .255, .225, 1)
L.new(mott.outputs['Fac'], ramp.inputs['Fac'])
# char: blackened extremities, like a burnt wick
attr = N.new('ShaderNodeAttribute'); attr.attribute_name = 'char'
edge = N.new('ShaderNodeTexNoise'); edge.inputs['Scale'].default_value = 34; edge.inputs['Detail'].default_value = 8
L.new(tc.outputs['Object'], edge.inputs['Vector'])
add = N.new('ShaderNodeMath'); add.operation = 'ADD'
L.new(attr.outputs['Fac'], add.inputs[0])
sub = N.new('ShaderNodeMath'); sub.operation = 'SUBTRACT'; L.new(edge.outputs['Fac'], sub.inputs[0]); sub.inputs[1].default_value = .5
mul = N.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'; L.new(sub.outputs[0], mul.inputs[0]); mul.inputs[1].default_value = 1.3
L.new(mul.outputs[0], add.inputs[1])
cr = N.new('ShaderNodeValToRGB'); cr.color_ramp.elements[0].position = .32; cr.color_ramp.elements[1].position = .62
L.new(add.outputs[0], cr.inputs['Fac'])
mix = N.new('ShaderNodeMix'); mix.data_type = 'RGBA'
L.new(cr.outputs['Color'], mix.inputs[0])
L.new(ramp.outputs['Color'], mix.inputs['A'])
mix.inputs['B'].default_value = (.013, .011, .012, 1)
# cavity grime
geo = N.new('ShaderNodeNewGeometry')
cav = N.new('ShaderNodeValToRGB'); cav.color_ramp.elements[0].position = .42; cav.color_ramp.elements[0].color = (.25, .22, .22, 1); cav.color_ramp.elements[1].position = .54
L.new(geo.outputs['Pointiness'], cav.inputs['Fac'])
mm = N.new('ShaderNodeMix'); mm.data_type = 'RGBA'; mm.blend_type = 'MULTIPLY'; mm.inputs[0].default_value = 1
L.new(mix.outputs[2], mm.inputs['A']); L.new(cav.outputs['Color'], mm.inputs['B'])
L.new(mm.outputs[2], sk.inputs['Base Color'])
# wet patches
rn = N.new('ShaderNodeTexNoise'); rn.inputs['Scale'].default_value = 9
L.new(tc.outputs['Object'], rn.inputs['Vector'])
rr = N.new('ShaderNodeMapRange'); rr.inputs['From Min'].default_value = .35; rr.inputs['From Max'].default_value = .65
rr.inputs['To Min'].default_value = .24; rr.inputs['To Max'].default_value = .55
L.new(rn.outputs['Fac'], rr.inputs['Value']); L.new(rr.outputs['Result'], sk.inputs['Roughness'])
# pores and creases
vor = N.new('ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 420
vor.feature = 'DISTANCE_TO_EDGE'
L.new(tc.outputs['Object'], vor.inputs['Vector'])
wr = N.new('ShaderNodeTexNoise'); wr.inputs['Scale'].default_value = 150; wr.inputs['Detail'].default_value = 6
L.new(tc.outputs['Object'], wr.inputs['Vector'])
ma = N.new('ShaderNodeMath'); ma.operation = 'ADD'
L.new(wr.outputs['Fac'], ma.inputs[0])
mb = N.new('ShaderNodeMath'); mb.operation = 'MULTIPLY'; L.new(vor.outputs['Distance'], mb.inputs[0]); mb.inputs[1].default_value = 0.0
L.new(mb.outputs[0], ma.inputs[1])
bp = N.new('ShaderNodeBump'); bp.inputs['Strength'].default_value = .32; bp.inputs['Distance'].default_value = .002
L.new(ma.outputs[0], bp.inputs['Height']); L.new(bp.outputs['Normal'], sk.inputs['Normal'])

hornm, _ = pbsdf('Horn', Base_Color=(.012, .01, .009, 1), Roughness=.3)
toothm, _ = pbsdf('Tooth', Base_Color=(.42, .36, .24, 1), Roughness=.32)
eyem, em = pbsdf('Eye', Base_Color=(.002, .002, .002, 1), Roughness=.06, Specular_IOR_Level=1.0)
glow, gn = pbsdf('Alert', Base_Color=(0, 0, 0, 1), Roughness=.4)
gn.inputs['Emission Color'].default_value = (1, .48, .05, 1)
gn.inputs['Emission Strength'].default_value = 7

# ---------- sculpt: union, smooth, then bones and sinew ----------
ob = body.build('Wick', skin)
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
m = ob.modifiers.new('r', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = VOXEL; m.use_smooth_shade = True
bpy.ops.object.modifier_apply(modifier='r')
m = ob.modifiers.new('s', 'LAPLACIANSMOOTH'); m.iterations = 12; m.lambda_factor = .9; m.use_volume_preserve = True
bpy.ops.object.modifier_apply(modifier='s')
me = ob.data
nv = len(me.vertices)
print('verts', nv, flush=True)
V = np.empty(nv*3); me.vertices.foreach_get('co', V); V = V.reshape(-1, 3)
Nr = np.empty(nv*3); me.vertex_normals.foreach_get('vector', Nr); Nr = Nr.reshape(-1, 3)

SP = catmull(norm(SPINE), 24)
C, RR = SP[:, :3], SP[:, 3]
cum = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(C, axis=0), axis=1))])
idx = np.zeros(nv, int); dmin = np.zeros(nv)
for i in range(0, nv, 40000):
    d = ((V[i:i+40000, None, :]-C[None, :, :])**2).sum(2)
    idx[i:i+40000] = d.argmin(1); dmin[i:i+40000] = np.sqrt(d.min(1))
def sstep(x, a, b):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)
disp = np.zeros(nv)
rel = V-C[idx]
wide = SP[idx, 4]*RR[idx]
near = dmin < wide*1.28
s = cum[idx]
front = -rel[:, 1]/np.maximum(dmin, 1e-6)
sc = C[idx, 2]
ribmask = near*sstep(sc, 1.27, 1.36)*(1-sstep(sc, 1.64, 1.72))
wave = (.5+.5*np.cos(2*math.pi*(s+.045*front)/.05))**3
disp += .0115*wave*ribmask*sstep(np.abs(V[:, 0]), .012, .05)*(1-.35*sstep(rel[:, 1], .03, .12))
# belly sucked in under the ribs
disp -= .012*near*sstep(sc, 1.13, 1.2)*(1-sstep(sc, 1.27, 1.33))*sstep(front, .1, .8)

# spine knuckles
T = np.gradient(C, axis=0); T /= np.linalg.norm(T, axis=1, keepdims=True)
back = np.column_stack([np.zeros(len(C)), T[:, 2], -T[:, 1]])
BP = C + back*(RR*SP[:, 5])[:, None]
vs = np.arange(cum[2], cum[-1]-.02, .034)
gl_pts = []
for j, a in enumerate(vs):
    i = int(np.searchsorted(cum, a))
    d2 = ((V-BP[i])**2).sum(1)
    disp += .021*np.exp(-d2/(2*.0125**2))
    if j % 3 == 1:
        gl_pts.append((BP[i], back[i]))

# face: sockets, cheeks, mouth corners
for s_ in (1, -1):
    for p, a, sg in [((s_*.05, -.60, 1.70), -.031, .019), ((s_*.075, -.53, 1.62), -.012, .025)]:
        d2 = ((V-np.array(p))**2).sum(1)
        disp += a*np.exp(-d2/(2*sg**2))

# muscle lumps and raised veins
ln, lv = np.zeros(nv), np.zeros(nv)
for i in range(nv):
    p = Vector(V[i])
    ln[i] = noise.noise(p*14) + .5*noise.noise(p*31)
    lv[i] = abs(noise.noise(p*11+Vector((3, 1, 7))))
disp += .004*ln*(1-.5*ribmask)
disp += .0035*(1-sstep(lv, .0, .05))*(1-ribmask)
V2 = V + Nr*disp[:, None]
me.vertices.foreach_set('co', V2.ravel())

# char map for the shader
def char_at(v):
    armside = np.abs(v[:, 0]) > .27
    arm = armside*sstep(1.24-v[:, 2], 0, .55)
    leg = (~armside)*sstep(.56-v[:, 2], 0, .36)
    q = ((v[:, 0]/.05)**2 + ((v[:, 1]+.525)/.085)**2 + ((v[:, 2]-1.572)/.058)**2)
    mouth = 1-sstep(q, .75, 1.15)
    return np.maximum(np.maximum(arm, leg), mouth)
ca = me.color_attributes.new('char', 'FLOAT_COLOR', 'POINT')
c = char_at(V2)
ca.data.foreach_set('color', np.column_stack([c, c, c, np.ones(nv)]).ravel())
me.update()

# alert organ: throat sac and glands breaking through the spine
seam = []
for z_, r_, x_ in [(1.36, .0015, .002), (1.395, .006, -.004), (1.43, .0045, .003), (1.47, .0105, -.002), (1.505, .0065, .005), (1.54, .0095, -.003), (1.575, .004, .002), (1.61, .0012, -.002)]:
    i_ = int(np.argmin(np.abs(C[:, 2]-z_)))
    p_ = C[i_]-back[i_]*(RR[i_]*SP[i_, 5]-.0015)
    seam.append((p_[0]+x_, p_[1], p_[2], r_, 1.0, .75))
organ.add(seam, sides=10, per=9)
for p, n in gl_pts[1:]:
    pp = p + n*.001
    organ.blob(tuple(pp), .007, 1.3, .8)

hornob = horn.build('Claws', hornm)
teeth.build('Teeth', toothm)
eyes.build('Eyes', eyem)
organob = organ.build('Alert_Organ', glow)

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

fm, fn = pbsdf('Floor', Base_Color=(.17, .17, .17, 1), Roughness=.75)
n = fm.node_tree.nodes; t = n.new('ShaderNodeTexNoise'); t.inputs['Scale'].default_value = 7; t.inputs['Detail'].default_value = 10
cc = n.new('ShaderNodeValToRGB'); cc.color_ramp.elements[0].color = (.07, .07, .07, 1); cc.color_ramp.elements[1].color = (.24, .24, .23, 1)
fm.node_tree.links.new(t.outputs['Fac'], cc.inputs['Fac']); fm.node_tree.links.new(cc.outputs['Color'], fn.inputs['Base Color'])
bpy.ops.mesh.primitive_plane_add(size=24); floor = bpy.context.object; floor.data.materials.append(fm)

# 1.8 m player capsule for scale (side view only)
bpy.ops.mesh.primitive_cylinder_add(radius=.32, depth=1.8, location=(0, .58, .9), vertices=48)
player = bpy.context.object
pm, _ = pbsdf('Player', Base_Color=(.2, .25, .34, 1), Roughness=.6); player.data.materials.append(pm)
bpy.ops.object.shade_smooth()

def area(name, loc, target, size, power):
    li = bpy.data.lights.new(name, 'AREA'); li.size = size; li.energy = power
    o = bpy.data.objects.new(name, li); bpy.context.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector(target)-Vector(loc)).to_track_quat('-Z', 'Y').to_euler(); return o
key = area('key', (-2.4, -3.6, 3.3), (0, -.2, 1.2), 2.6, 420)
rim = area('rim', (2.6, 3.0, 3.0), (0, 0, 1.3), 2.0, 520)
studio = [key, rim]

lamp = bpy.data.lights.new('lamp', 'SPOT'); lamp.energy = 900; lamp.spot_size = math.radians(80); lamp.spot_blend = .12; lamp.shadow_soft_size = .05
lamp.color = (1, .86, .62)
lo = bpy.data.objects.new('lamp', lamp); bpy.context.collection.objects.link(lo); lo.location = (-1.0, -.1, 3.6)
flash = bpy.data.lights.new('flash', 'SPOT'); flash.energy = 70; flash.spot_size = math.radians(30); flash.spot_blend = .5; flash.shadow_soft_size = .02
fo = bpy.data.objects.new('flash', flash); bpy.context.collection.objects.link(fo); fo.location = (.5, -4.2, 1.35)
fo.rotation_euler = (Vector((0, -.4, 1.45))-fo.location).to_track_quat('-Z', 'Y').to_euler()

cam = bpy.data.cameras.new('c'); cam.lens = 85; cam.sensor_fit = 'VERTICAL'; cam.sensor_height = 24
co = bpy.data.objects.new('c', cam); bpy.context.collection.objects.link(co); scene.camera = co

def shoot(name, az, height=1.18, dist=8.6, target=(0, -.12, 1.0), lens=85, res=None):
    t = Vector(target); a = math.radians(az)
    co.location = t + Vector((math.sin(a)*dist, -math.cos(a)*dist, height-target[2]))
    co.rotation_euler = (t-co.location).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = lens
    scene.render.filepath = f'{OUT}/{name}.png'
    bpy.ops.render.render(write_still=True)
    print('rendered', name, flush=True)

def mode(noir, side=False, red=False):
    for o in studio: o.hide_render = noir
    lo.hide_render = not noir; fo.hide_render = not noir
    player.hide_render = not side
    bg.inputs[0].default_value = (.012, .012, .014, 1) if noir else (.16, .16, .17, 1)
    bg.inputs[1].default_value = .35 if noir else 1
    gn.inputs['Emission Color'].default_value = (1, .015, .01, 1) if red else (1, .48, .05, 1)
    gn.inputs['Emission Strength'].default_value = 34 if red else 2.5

want = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['front', 'three', 'side', 'noir', 'face']
if 'front' in want: mode(False); shoot('p1_front', 0)
if 'three' in want: mode(False); shoot('p2_three', 215)
if 'side' in want: mode(False, side=True); shoot('p3_side', 90, dist=10.5)
if 'noir' in want: mode(True, red=True); shoot('p4_noir', 22)
if 'face' in want:
    mode(False)
    shoot('p5_face', 25, height=1.72, dist=3.0, target=(0, -.5, 1.68), lens=85)
bpy.ops.wm.save_as_mainfile(filepath=f'{OUT}/wick.blend')
print('DONE', flush=True)
