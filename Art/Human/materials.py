"""Cycles materials for the design model. Colours are set for a muted noir grade."""
import bpy


class Nodes:
    def __init__(self, name):
        self.material = bpy.data.materials.new(name)
        self.material.use_nodes = True
        self.tree = self.material.node_tree
        self.bsdf = self.tree.nodes["Principled BSDF"]
        self.coord = self.add("ShaderNodeTexCoord")

    def add(self, kind, **settings):
        node = self.tree.nodes.new(kind)
        for key, value in settings.items():
            setattr(node, key, value)
        return node

    def link(self, out, target):
        self.tree.links.new(out, target)

    def set(self, **values):
        for key, value in values.items():
            self.bsdf.inputs[key.replace("_", " ")].default_value = value

    def noise(self, scale, detail=4.0, roughness=0.55):
        node = self.add("ShaderNodeTexNoise")
        node.inputs["Scale"].default_value = scale
        node.inputs["Detail"].default_value = detail
        node.inputs["Roughness"].default_value = roughness
        self.link(self.coord.outputs["Object"], node.inputs["Vector"])
        return node

    def mix(self, colour_a, colour_b, factor):
        node = self.add("ShaderNodeMix", data_type="RGBA")
        node.inputs["A"].default_value = (*colour_a, 1)
        node.inputs["B"].default_value = (*colour_b, 1)
        if isinstance(factor, (int, float)):
            node.inputs["Factor"].default_value = factor
        else:
            self.link(factor, node.inputs["Factor"])
        return node

    def ramp(self, value, low, high):
        node = self.add("ShaderNodeMapRange")
        node.inputs["From Min"].default_value = low
        node.inputs["From Max"].default_value = high
        self.link(value, node.inputs["Value"])
        return node

    def bump(self, height, strength=1.0, distance=0.001):
        node = self.add("ShaderNodeBump")
        node.inputs["Strength"].default_value = strength
        node.inputs["Distance"].default_value = distance
        self.link(height, node.inputs["Height"])
        self.link(node.outputs["Normal"], self.bsdf.inputs["Normal"])
        return node


def gabardine():
    m = Nodes("M_Coat")
    fibre = m.noise(900.0, detail=2.0)
    weave = m.add("ShaderNodeTexWave", wave_type="BANDS", bands_direction="DIAGONAL")
    weave.inputs["Scale"].default_value = 520.0
    weave.inputs["Distortion"].default_value = 0.4
    m.link(m.coord.outputs["Object"], weave.inputs["Vector"])

    blotch = m.noise(5.0, detail=5.0)
    tone = m.ramp(blotch.outputs["Fac"], 0.32, 0.68)
    base = m.mix((0.385, 0.296, 0.182), (0.415, 0.32, 0.198), tone.outputs[0])
    m.link(base.outputs["Result"], m.bsdf.inputs["Base Color"])
    m.set(Roughness=0.78, Sheen_Weight=0.45, Sheen_Roughness=0.55, Specular_IOR_Level=0.3)

    add = m.add("ShaderNodeMath", operation="ADD")
    m.link(weave.outputs["Fac"], add.inputs[0])
    m.link(fibre.outputs["Fac"], add.inputs[1])
    m.bump(add.outputs[0], strength=0.28, distance=0.0009)
    return m.material


def shirt():
    m = Nodes("M_Shirt")
    weave = m.noise(1400.0, detail=2.0)
    m.set(Base_Color=(0.78, 0.77, 0.74, 1), Roughness=0.62, Sheen_Weight=0.25)
    m.bump(weave.outputs["Fac"], strength=0.12, distance=0.0005)
    return m.material


def tie():
    m = Nodes("M_Tie")
    stripes = m.add("ShaderNodeTexWave", wave_type="BANDS", bands_direction="DIAGONAL")
    stripes.inputs["Scale"].default_value = 24.0
    stripes.inputs["Distortion"].default_value = 0.0
    stripes.inputs["Detail"].default_value = 0.0
    m.link(m.coord.outputs["Object"], stripes.inputs["Vector"])
    thin = m.ramp(stripes.outputs["Fac"], 0.9, 0.93)
    colour = m.mix((0.115, 0.012, 0.025), (0.62, 0.58, 0.55), thin.outputs[0])
    m.link(colour.outputs["Result"], m.bsdf.inputs["Base Color"])
    m.set(Roughness=0.56, Sheen_Weight=0.25)
    return m.material


def wool(name, colour):
    m = Nodes(name)
    fibre = m.noise(700.0, detail=3.0)
    tone = m.noise(12.0)
    blend = m.ramp(tone.outputs["Fac"], 0.3, 0.7)
    base = m.mix(colour, tuple(c * 1.25 for c in colour), blend.outputs[0])
    m.link(base.outputs["Result"], m.bsdf.inputs["Base Color"])
    m.set(Roughness=0.72, Sheen_Weight=0.7, Sheen_Roughness=0.5, Specular_IOR_Level=0.3)
    m.bump(fibre.outputs["Fac"], strength=0.25, distance=0.0008)
    return m.material


def leather(name, colour, roughness, coat=0.0):
    m = Nodes(name)
    grain = m.noise(420.0, detail=4.0)
    m.set(Base_Color=(*colour, 1), Roughness=roughness, Coat_Weight=coat, Coat_Roughness=0.15, Specular_IOR_Level=0.55)
    m.bump(grain.outputs["Fac"], strength=0.14, distance=0.0006)
    return m.material


def metal():
    m = Nodes("M_Metal")
    m.set(Base_Color=(0.30, 0.235, 0.15, 1), Metallic=1.0, Roughness=0.38)
    return m.material


def horn():
    m = Nodes("M_Button")
    grain = m.noise(180.0)
    tone = m.ramp(grain.outputs["Fac"], 0.35, 0.65)
    base = m.mix((0.022, 0.012, 0.007), (0.075, 0.042, 0.022), tone.outputs[0])
    m.link(base.outputs["Result"], m.bsdf.inputs["Base Color"])
    m.set(Roughness=0.22, Coat_Weight=0.5, Coat_Roughness=0.1)
    return m.material


def silk_hat():
    """Polished silk plush: nearly black, with long vertical highlights."""
    m = Nodes("M_Hat")
    tangent = m.add("ShaderNodeTangent", direction_type="RADIAL", axis="Z")
    m.link(tangent.outputs["Tangent"], m.bsdf.inputs["Tangent"])
    streak = m.noise(240.0, detail=2.0)
    roughness = m.ramp(streak.outputs["Fac"], 0.25, 0.75)
    roughness.inputs["To Min"].default_value = 0.235
    roughness.inputs["To Max"].default_value = 0.265
    m.link(roughness.outputs[0], m.bsdf.inputs["Roughness"])
    m.set(Base_Color=(0.008, 0.008, 0.01, 1), Anisotropic=0.8, Specular_IOR_Level=0.5, Sheen_Weight=0.12)
    return m.material


def band():
    m = Nodes("M_HatBand")
    ribs = m.add("ShaderNodeTexWave", wave_type="BANDS", bands_direction="Z")
    ribs.inputs["Scale"].default_value = 700.0
    ribs.inputs["Distortion"].default_value = 0.0
    m.link(m.coord.outputs["Object"], ribs.inputs["Vector"])
    m.set(Base_Color=(0.095, 0.009, 0.016, 1), Roughness=0.6, Sheen_Weight=0.5)
    m.bump(ribs.outputs["Fac"], strength=0.2, distance=0.0005)
    return m.material


def hair():
    m = Nodes("M_Hair")
    mapping = m.add("ShaderNodeMapping")
    # Strands run straight down, which reads correctly on both the sides and the back.
    mapping.inputs["Rotation"].default_value = (0.0, 0.0, 0.0)
    mapping.inputs["Scale"].default_value = (640.0, 640.0, 42.0)
    m.link(m.coord.outputs["Object"], mapping.inputs["Vector"])
    strands = m.add("ShaderNodeTexNoise")
    strands.inputs["Detail"].default_value = 3.0
    strands.inputs["Scale"].default_value = 1.0
    m.link(mapping.outputs[0], strands.inputs["Vector"])

    tone = m.ramp(strands.outputs["Fac"], 0.38, 0.62)
    colour = m.mix((0.0065, 0.0048, 0.004), (0.058, 0.043, 0.033), tone.outputs[0])

    grey_noise = m.ramp(strands.outputs["Fac"], 0.56, 0.6)
    geometry = m.add("ShaderNodeNewGeometry")
    separate = m.add("ShaderNodeSeparateXYZ")
    m.link(geometry.outputs["Position"], separate.inputs[0])
    temple = m.ramp(separate.outputs["Z"], 1.71, 1.66)
    # Grey only at the temples: the back of the head stays dark.
    forward = m.ramp(separate.outputs["Y"], -0.015, -0.055)
    temples_only = m.add("ShaderNodeMath", operation="MULTIPLY")
    m.link(temple.outputs[0], temples_only.inputs[0])
    m.link(forward.outputs[0], temples_only.inputs[1])
    amount = m.add("ShaderNodeMath", operation="MULTIPLY")
    m.link(grey_noise.outputs[0], amount.inputs[0])
    m.link(temples_only.outputs[0], amount.inputs[1])
    salted = m.add("ShaderNodeMix", data_type="RGBA")
    m.link(amount.outputs[0], salted.inputs["Factor"])
    m.link(colour.outputs["Result"], salted.inputs["A"])
    salted.inputs["B"].default_value = (0.16, 0.155, 0.15, 1)
    m.link(salted.outputs["Result"], m.bsdf.inputs["Base Color"])

    m.set(Roughness=0.38, Specular_IOR_Level=0.5, Sheen_Weight=0.3)
    m.bump(strands.outputs["Fac"], strength=0.75, distance=0.0012)
    return m.material


def eye(obj):
    """Sclera, a pale blue iris and a dark limbal ring, placed by angle from straight ahead."""
    radius = obj["eye_radius"]
    centre = obj.location
    m = Nodes("M_Eye_" + obj.name)
    geometry = m.add("ShaderNodeNewGeometry")
    subtract = m.add("ShaderNodeVectorMath", operation="SUBTRACT")
    m.link(geometry.outputs["Position"], subtract.inputs[0])
    subtract.inputs[1].default_value = centre
    normalise = m.add("ShaderNodeVectorMath", operation="NORMALIZE")
    m.link(subtract.outputs[0], normalise.inputs[0])
    dot = m.add("ShaderNodeVectorMath", operation="DOT_PRODUCT")
    m.link(normalise.outputs[0], dot.inputs[0])
    dot.inputs[1].default_value = (0.03 if centre.x > 0 else -0.03, -1.0, -0.05)
    fibre = m.noise(2200.0, detail=2.0)
    mix_cos = m.add("ShaderNodeMath", operation="ADD")
    jitter = m.add("ShaderNodeMath", operation="MULTIPLY")
    jitter.inputs[1].default_value = 0.012
    m.link(fibre.outputs["Fac"], jitter.inputs[0])
    m.link(dot.outputs["Value"], mix_cos.inputs[0])
    m.link(jitter.outputs[0], mix_cos.inputs[1])

    ramp = m.add("ShaderNodeValToRGB")
    elements = ramp.color_ramp.elements
    elements[0].position = 0.925
    elements[0].color = (0.62, 0.56, 0.5, 1)
    elements[1].position = 0.937
    elements[1].color = (0.06, 0.09, 0.14, 1)
    for position, colour in ((0.948, (0.15, 0.28, 0.42, 1)), (0.975, (0.2, 0.34, 0.46, 1)), (0.987, (0.09, 0.11, 0.13, 1)), (0.989, (0.008, 0.008, 0.01, 1))):
        element = elements.new(position)
        element.color = colour
    m.link(mix_cos.outputs[0], ramp.inputs[0])
    m.link(ramp.outputs[0], m.bsdf.inputs["Base Color"])
    m.set(Roughness=0.09, Coat_Weight=1.0, Coat_Roughness=0.03, Specular_IOR_Level=0.6)
    return m.material


def head(albedo, rough, normal):
    m = Nodes("M_Head")

    def image(path, colour):
        node = m.add("ShaderNodeTexImage")
        node.image = bpy.data.images.load(path, check_existing=True)
        node.image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        m.link(m.coord.outputs["UV"], node.inputs["Vector"])
        return node

    m.link(image(albedo, True).outputs["Color"], m.bsdf.inputs["Base Color"])
    m.link(image(rough, False).outputs["Color"], m.bsdf.inputs["Roughness"])

    normal_map = m.add("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.5
    m.link(image(normal, False).outputs["Color"], normal_map.inputs["Color"])
    m.link(normal_map.outputs["Normal"], m.bsdf.inputs["Normal"])

    m.set(Subsurface_Weight=0.65, Subsurface_Radius=(0.7, 0.26, 0.13), Subsurface_Scale=0.006, Specular_IOR_Level=0.45)
    return m.material
