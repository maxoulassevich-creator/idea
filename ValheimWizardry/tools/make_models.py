"""Models and item icons for the mod, made with Blender (the bpy module).

    python make_models.py <assets dir> <work dir> [only,these,keys]

Icons are rendered with Cycles (256 px, later scaled to 128 by make_icons.py) into <work>/icons/icon_<key>.png.
The six Codex extensions are also baked (albedo + emission) and exported as WZM meshes into <assets>.
"""
import math
import os
import struct
import sys

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
from mathutils import Matrix, Vector

ASSETS, WORK = sys.argv[1], sys.argv[2]
ONLY = set(sys.argv[3].split(",")) if len(sys.argv) > 3 and sys.argv[3] else None
ICONS = os.path.join(WORK, "icons")
os.makedirs(ICONS, exist_ok=True)


# ============================================================================================================ scene
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 48
    sc.cycles.use_denoising = True
    try:
        sc.cycles.denoiser = "OPENIMAGEDENOISE"
    except Exception:
        pass
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = 256
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    try:
        sc.view_settings.view_transform = "Standard"
        sc.view_settings.look = "None"
    except Exception:
        pass
    w = bpy.data.worlds.new("w")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.09, 0.09, 0.11, 1)
    bg.inputs[1].default_value = 1.0


def look_at(obj, target):
    d = Vector(target) - obj.location
    obj.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def light(kind, loc, energy, color, size=2.0):
    data = bpy.data.lights.new("L", kind)
    data.energy = energy
    data.color = color
    if kind == "AREA":
        data.size = size
    o = bpy.data.objects.new("L", data)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    look_at(o, (0, 0, 0))
    return o


def turn(deg_z, deg_x=0.0):
    """Rotates everything in the scene as one piece around the origin."""
    rot = Matrix.Rotation(math.radians(deg_z), 4, "Z") @ Matrix.Rotation(math.radians(deg_x), 4, "X")
    for o in meshes():
        o.matrix_world = rot @ o.matrix_world


def meshes():
    return [o for o in bpy.context.scene.objects if o.type in ("MESH", "CURVE", "FONT")]


def frame_and_render(key, azim=-35.0, elev=28.0, pad=1.1):
    sc = bpy.context.scene
    deps = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in meshes():
        ev = o.evaluated_get(deps)
        try:
            me = ev.to_mesh()
        except RuntimeError:
            continue
        pts += [ev.matrix_world @ v.co for v in me.vertices]
        ev.to_mesh_clear()
    c = sum(pts, Vector()) / max(1, len(pts))
    a, e = math.radians(azim), math.radians(elev)
    fwd = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))  # from target to camera
    cam_data = bpy.data.cameras.new("C")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("C", cam_data)
    sc.collection.objects.link(cam)
    cam.location = c + fwd * 20
    look_at(cam, c)
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    loc = [inv @ p for p in pts]
    xs, ys = [p.x for p in loc], [p.y for p in loc]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    cam.location = cam.matrix_world @ Vector((cx, cy, 0))
    cam_data.ortho_scale = max(w, h) * pad
    sc.camera = cam
    r = max(w, h)
    light("AREA", c + Vector((-2.2, -3.0, 3.6)) * r, 260 * r * r, (1.0, 0.93, 0.84), 2.5 * r)
    light("AREA", c + Vector((3.2, -1.5, 1.2)) * r, 90 * r * r, (0.75, 0.85, 1.0), 3.0 * r)
    light("AREA", c + Vector((0.5, 3.5, 2.5)) * r, 240 * r * r, (1.0, 0.96, 0.9), 2.0 * r)
    sc.render.filepath = os.path.join(ICONS, "icon_" + key + ".png")
    bpy.ops.render.render(write_still=True)
    print("icon", key)


# ============================================================================================================ materials
def principled(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    return m, m.node_tree, m.node_tree.nodes["Principled BSDF"]


def mat(name, color, rough=0.5, metal=0.0, emit=None, strength=0.0, trans=0.0, noise=0.0, scale=6.0, bump=0.0, coat=0.0, sheen=0.0):
    m, nt, b = principled(name)
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if trans:
        b.inputs["Transmission Weight"].default_value = trans
        b.inputs["IOR"].default_value = 1.45
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = strength
    if coat:
        b.inputs["Coat Weight"].default_value = coat
    if sheen:
        b.inputs["Sheen Weight"].default_value = sheen
    if noise or bump:
        tc = nt.nodes.new("ShaderNodeTexCoord")
        nz = nt.nodes.new("ShaderNodeTexNoise")
        nz.inputs["Scale"].default_value = scale
        nz.inputs["Detail"].default_value = 10
        nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
        if noise:
            ramp = nt.nodes.new("ShaderNodeValToRGB")
            ramp.color_ramp.elements[0].color = (*[c * (1 - noise) for c in color], 1)
            ramp.color_ramp.elements[1].color = (*[min(1, c * (1 + noise * 0.6)) for c in color], 1)
            nt.links.new(nz.outputs["Fac"], ramp.inputs["Fac"])
            nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
        if bump:
            bp = nt.nodes.new("ShaderNodeBump")
            bp.inputs["Strength"].default_value = bump
            nt.links.new(nz.outputs["Fac"], bp.inputs["Height"])
            nt.links.new(bp.outputs["Normal"], b.inputs["Normal"])
    return m


def wood(name, color, rings=4.0, rough=0.6):
    m, nt, b = principled(name)
    b.inputs["Roughness"].default_value = rough
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type = "RINGS"
    wave.inputs["Scale"].default_value = rings
    wave.inputs["Distortion"].default_value = 6
    wave.inputs["Detail"].default_value = 4
    nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*[c * 0.6 for c in color], 1)
    ramp.color_ramp.elements[1].color = (*color, 1)
    nt.links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    bp = nt.nodes.new("ShaderNodeBump")
    bp.inputs["Strength"].default_value = 0.15
    nt.links.new(wave.outputs["Fac"], bp.inputs["Height"])
    nt.links.new(bp.outputs["Normal"], b.inputs["Normal"])
    return m


def stripes(name, color, dark, scale=60.0):
    """Paper edges: fine stripes along one axis."""
    m, nt, b = principled(name)
    b.inputs["Roughness"].default_value = 0.85
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.inputs["Scale"].default_value = scale
    wave.bands_direction = "Z"
    nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].color = (*color, 1)
    nt.links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    return m


GOLD = (1.0, 0.72, 0.32)
BRONZE = (0.78, 0.47, 0.22)
IRON = (0.2, 0.2, 0.22)
SILVER = (0.86, 0.87, 0.9)
PARCH = (0.88, 0.8, 0.6)


def gold(name="gold"):
    return mat(name, GOLD, 0.28, 1.0, noise=0.08, scale=20)


def glass(name, tint=(0.85, 0.95, 1.0)):
    return mat(name, tint, 0.03, 0.0, trans=1.0)


# ============================================================================================================ meshes
def link(o):
    bpy.context.scene.collection.objects.link(o)
    return o


def from_bm(name, bm, m, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = link(bpy.data.objects.new(name, me))
    if m:
        o.data.materials.append(m)
    if smooth:
        for p in o.data.polygons:
            p.use_smooth = True
    return o


def lathe(name, prof, m, segs=48, loc=(0, 0, 0), smooth=True):
    bm = bmesh.new()
    vs = [bm.verts.new((r, 0, z)) for r, z in prof]
    es = [bm.edges.new((vs[i], vs[i + 1])) for i in range(len(vs) - 1)]
    bmesh.ops.spin(bm, geom=vs + es, cent=(0, 0, 0), axis=(0, 0, 1), angle=math.tau, steps=segs, use_merge=True)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm(name, bm, m, smooth)
    o.location = loc
    return o


def prim(kind, name, m, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), bevel=0.0, **kw):
    getattr(bpy.ops.mesh, "primitive_" + kind + "_add")(location=loc, rotation=rot, **kw)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    if m:
        o.data.materials.append(m)
    if bevel:
        bv = o.modifiers.new("b", "BEVEL")
        bv.width = bevel
        bv.segments = 3
        bv.limit_method = "ANGLE"
    for p in o.data.polygons:
        p.use_smooth = kind in ("uv_sphere", "ico_sphere", "torus", "cylinder", "cone")
    return o


def tube(name, pts, radius, m, taper=None, res=12):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 4
    cu.use_fill_caps = True
    sp = cu.splines.new("NURBS")
    sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        sp.points[i].co = (*p, 1)
        if taper:
            sp.points[i].radius = taper[i]
    sp.use_endpoint_u = True
    sp.order_u = 3
    sp.resolution_u = res
    o = link(bpy.data.objects.new(name, cu))
    o.data.materials.append(m)
    return o


def text(name, body, size, m, loc, rot, extrude=0.02):
    bpy.ops.object.text_add(location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.data.body = body
    o.data.size = size
    o.data.extrude = extrude
    o.data.bevel_depth = extrude * 0.3
    o.data.align_x = "CENTER"
    o.data.align_y = "CENTER"
    o.data.materials.append(m)
    return o


def star2d(cx, cy, r1, r2, n, rot=math.pi / 2):
    return [(cx + (r1 if i % 2 == 0 else r2) * math.cos(rot + math.pi * i / n), cy + (r1 if i % 2 == 0 else r2) * math.sin(rot + math.pi * i / n))
            for i in range(n * 2)]


def plate(name, poly2d, depth, m, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0):
    """An extruded flat shape (x, y) with thickness along z."""
    bm = bmesh.new()
    vs = [bm.verts.new((x, y, 0)) for x, y in poly2d]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]:
        v.co.z += depth
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = from_bm(name, bm, m, smooth=False)
    o.location = loc
    o.rotation_euler = rot
    if bevel:
        bv = o.modifiers.new("b", "BEVEL")
        bv.width = bevel
        bv.segments = 2
    return o


def read_wzm(path):
    d = open(path, "rb").read()
    nv, ni = struct.unpack_from("<ii", d, 4)
    off = 12
    v = struct.unpack_from("<%df" % (nv * 3), d, off); off += nv * 12
    n = struct.unpack_from("<%df" % (nv * 3), d, off); off += nv * 12
    uv = struct.unpack_from("<%df" % (nv * 2), d, off); off += nv * 8
    t = struct.unpack_from("<%di" % ni, d, off)
    return nv, v, uv, t


def wand_mesh(name, m, length=1.0):
    nv, v, uv, t = read_wzm(os.path.join(ASSETS, "wand.wzm"))
    me = bpy.data.meshes.new(name)
    verts = [(v[i * 3] * length, v[i * 3 + 2] * length, v[i * 3 + 1] * length) for i in range(nv)]  # unity (x, y up, z) -> blender
    faces = [(t[i], t[i + 2], t[i + 1]) for i in range(0, len(t), 3)]
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new()
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            uvl.data[li].uv = (uv[vi * 2], uv[vi * 2 + 1])
    for p in me.polygons:
        p.use_smooth = True
    o = link(bpy.data.objects.new(name, me))
    o.data.materials.append(m)
    return o


def wand_material(name, tint):
    m, nt, b = principled(name)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(ASSETS, "wand_albedo.jpg"))
    bw = nt.nodes.new("ShaderNodeRGBToBW")
    nt.links.new(tex.outputs["Color"], bw.inputs["Color"])
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = 2.6
    nt.links.new(bw.outputs["Val"], mul.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    mix.inputs[7].default_value = (*tint, 1)
    comb = nt.nodes.new("ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(mul.outputs[0], comb.inputs[i])
    nt.links.new(comb.outputs[0], mix.inputs[6])
    nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    nrm = nt.nodes.new("ShaderNodeTexImage")
    nrm.image = bpy.data.images.load(os.path.join(ASSETS, "wand_normal.jpg"))
    nrm.image.colorspace_settings.name = "Non-Color"
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Roughness"].default_value = 0.5
    return m


# ============================================================================================================ items
WOODS = {
    "beech": (0.78, 0.55, 0.3), "fir": (0.7, 0.28, 0.12), "birch": (0.95, 0.92, 0.85), "ancient": (0.35, 0.48, 0.2),
    "eldercopy": (0.6, 0.55, 0.5), "yggdrasil": (0.35, 0.45, 0.85), "ash": (0.16, 0.13, 0.12), "north": (0.75, 0.9, 1.0),
    "elder": (0.88, 0.85, 0.78),
}
WAND_GLOW = {"beech": (0.6, 0.85, 1.0), "fir": (1.0, 0.6, 0.25), "birch": (0.85, 0.95, 1.0), "ancient": (0.45, 1.0, 0.4),
             "eldercopy": (0.8, 0.8, 1.0), "yggdrasil": (0.45, 0.6, 1.0), "ash": (1.0, 0.45, 0.15), "north": (0.6, 0.9, 1.0),
             "elder": (0.95, 0.95, 1.0)}


def item_wand(wood):
    tint = WOODS[wood]
    w = wand_mesh("wand", wand_material("wm", tint), 1.0)
    w.rotation_euler = (0, math.radians(-50), 0)
    glow = WAND_GLOW[wood]
    tip = prim("uv_sphere", "tip", mat("tipm", glow, emit=glow, strength=2.0), radius=0.02)
    halo = prim("uv_sphere", "halo", mat("halom", glow, 0.0, emit=glow, strength=0.6, trans=1.0), radius=0.05)
    halo.location = Vector((math.sin(math.radians(-50)), 0, math.cos(math.radians(-50))))
    tip.location = Vector((math.sin(math.radians(-50)), 0, math.cos(math.radians(-50))))
    frame_and_render("wand_" + wood, azim=0, elev=12, pad=1.05)


def book_parts(cover, emblem="star", label=None, metal=GOLD, w=0.62, h=0.86, t=0.16):
    cv = mat("cover", cover, 0.55, noise=0.25, scale=14, bump=0.25)
    met = mat("metal", metal, 0.3, 1.0, noise=0.05, scale=30)
    pages = stripes("pages", (0.93, 0.88, 0.74), (0.75, 0.68, 0.52), 90)
    prim("cube", "front", cv, loc=(0, -t / 2 + 0.012, 0), scale=(w / 2, 0.012, h / 2), bevel=0.008)
    prim("cube", "back", cv, loc=(0, t / 2 - 0.012, 0), scale=(w / 2, 0.012, h / 2), bevel=0.008)
    prim("cylinder", "spine", cv, loc=(-w / 2, 0, 0), rot=(0, 0, 0), scale=(t / 2, t / 2, h / 2), vertices=24)
    prim("cube", "pages", pages, loc=(0.012, 0, 0), scale=(w / 2 - 0.02, t / 2 - 0.022, h / 2 - 0.02))
    for sz in (-1, 1):  # corner protectors
        plate("corner", [(0, 0), (-0.09, 0), (0, -0.09 * sz)], 0.008, met, loc=(w / 2 + 0.002, -t / 2 - 0.004, sz * (h / 2 + 0.002)),
              rot=(math.pi / 2, 0, 0), bevel=0.002)
    if emblem == "star":
        plate("emblem", star2d(0, 0, 0.12, 0.05, 4), 0.012, met, loc=(0.02, -t / 2 - 0.004, 0.1), rot=(math.pi / 2, 0, 0), bevel=0.003)
    elif emblem == "ring":
        prim("torus", "emblem", met, loc=(0.02, -t / 2 - 0.004, 0.08), rot=(math.pi / 2, 0, 0), major_radius=0.13, minor_radius=0.012)
        plate("emblem2", star2d(0, 0, 0.11, 0.04, 5), 0.01, met, loc=(0.02, -t / 2 - 0.004, 0.08), rot=(math.pi / 2, 0, 0))
    elif emblem == "crown":
        plate("emblem", [(-0.13, -0.06), (-0.13, 0.08), (-0.065, 0.02), (0, 0.11), (0.065, 0.02), (0.13, 0.08), (0.13, -0.06)], 0.012, met,
              loc=(0.02, -t / 2 - 0.004, 0.1), rot=(math.pi / 2, 0, 0), bevel=0.003)
    elif emblem == "w":
        text("W", "W", 0.32, mat("wtext", (1.0, 0.55, 0.1), 0.4, emit=(1.0, 0.45, 0.1), strength=1.5), (0.02, -t / 2 - 0.012, 0.08),
             (math.pi / 2, 0, 0), 0.015)
    if label:
        text("label", label, 0.26, met, (0.02, -t / 2 - 0.01, -0.22), (math.pi / 2, 0, 0), 0.014)


def item_book(key, cover, emblem, label=None, metal=GOLD):
    book_parts(cover, emblem, label, metal)
    bpy.context.view_layer.update()
    turn(32)
    frame_and_render(key, azim=-20, elev=20)


def item_scroll():
    p = mat("parch", PARCH, 0.8, noise=0.2, scale=10)
    prim("cylinder", "roll", p, rot=(0, math.pi / 2, 0), scale=(0.14, 0.14, 0.6), vertices=40)
    wd = wood("wd", (0.4, 0.22, 0.12))
    for s in (-1, 1):
        prim("cylinder", "rod", wd, loc=(s * 0.62, 0, 0), rot=(0, math.pi / 2, 0), scale=(0.05, 0.05, 0.06), vertices=24)
        prim("uv_sphere", "knob", wd, loc=(s * 0.7, 0, 0), radius=0.07)
    prim("torus", "ribbon", mat("rib", (0.6, 0.08, 0.1), 0.5, sheen=0.5), rot=(0, math.pi / 2, 0), major_radius=0.145, minor_radius=0.02)
    seal = mat("seal", (0.15, 0.55, 0.25), 0.35, coat=0.6)
    prim("cylinder", "seal", seal, loc=(0, -0.16, -0.02), rot=(math.pi / 2, 0, 0), scale=(0.07, 0.07, 0.02), vertices=24)
    frame_and_render(Items_Scroll, azim=-20, elev=30)


def item_cup():
    s = mat("silver", SILVER, 0.22, 1.0, noise=0.05, scale=25)
    lathe("cup", [(0.0, 0.0), (0.3, 0.0), (0.32, 0.03), (0.1, 0.08), (0.06, 0.2), (0.07, 0.42), (0.18, 0.48), (0.34, 0.66), (0.38, 0.9),
                  (0.36, 0.9), (0.32, 0.68), (0.0, 0.52)], s)
    lathe("liquid", [(0.0, 0.84), (0.355, 0.84)], mat("liq", (0.3, 0.4, 0.6), 0.1, emit=(0.6, 0.75, 1.0), strength=0.4, noise=0.5, scale=5))
    frame_and_render(Items_Cup, azim=-25, elev=24)


def item_card():
    frame_m = mat("frame", GOLD, 0.3, 1.0)
    body = mat("card", (0.55, 0.28, 0.16), 0.5, noise=0.2, scale=8)
    prim("cube", "card", body, scale=(0.32, 0.01, 0.46), bevel=0.01)
    prim("cube", "portrait", mat("portrait", (0.18, 0.3, 0.5), 0.4, emit=(0.25, 0.45, 0.85), strength=1.2, noise=0.4, scale=4),
         loc=(0, -0.012, 0.06), scale=(0.24, 0.004, 0.3))
    prim("torus", "oval", frame_m, loc=(0, -0.016, 0.06), rot=(math.pi / 2, 0, 0), scale=(0.95, 1.25, 1), major_radius=0.25, minor_radius=0.014)
    plate("star", star2d(0, 0, 0.07, 0.03, 5), 0.01, frame_m, loc=(0, -0.016, -0.36), rot=(math.pi / 2, 0, 0))
    sil = mat("sil", (0.06, 0.07, 0.12), 0.6)
    prim("uv_sphere", "head", sil, loc=(0, -0.02, 0.16), radius=0.085).scale = (1, 0.3, 1.15)
    sh = prim("uv_sphere", "shoulders", sil, loc=(0, -0.02, -0.12), radius=0.2)
    sh.scale = (1.0, 0.25, 0.55)
    prim("cone", "hat", sil, loc=(0, -0.02, 0.32), radius1=0.12, depth=0.2, vertices=24).scale = (1, 0.3, 1)
    frame_and_render(Items_Card, azim=-22, elev=12)


def item_memory(key, wisp):
    lathe("vial", [(0.0, 0.0), (0.16, 0.0), (0.18, 0.04), (0.18, 0.5), (0.1, 0.6), (0.07, 0.66), (0.08, 0.7), (0.0, 0.7)], glass("gl"))
    pts = [(0.08 * math.cos(i * 0.9), 0.08 * math.sin(i * 0.9), 0.06 + i * 0.045) for i in range(10)]
    tube("wisp", pts, 0.022, mat("wisp", wisp, 0.2, emit=wisp, strength=0.55), taper=[0.6 + 0.4 * math.sin(i / 3) for i in range(10)])
    prim("cylinder", "cork", wood("cork", (0.6, 0.45, 0.3), 9), loc=(0, 0, 0.74), scale=(0.075, 0.075, 0.06), vertices=24)
    frame_and_render(key, azim=-15, elev=18)


def item_stone():
    st = mat("stone", (0.08, 0.08, 0.09), 0.25, noise=0.4, scale=6, bump=0.3, coat=0.6)
    o = prim("ico_sphere", "stone", st, subdivisions=4, radius=0.4)
    o.scale = (1.0, 0.55, 0.8)
    d = o.modifiers.new("d", "DISPLACE")
    tx = bpy.data.textures.new("n", "CLOUDS")
    tx.noise_scale = 0.6
    d.texture = tx
    d.strength = 0.08
    em = mat("sign", (0.7, 0.9, 1.0), 0.2, emit=(0.6, 0.85, 1.0), strength=2.5)
    y = -0.225
    tri = [(0, 0.22), (-0.19, -0.11), (0.19, -0.11), (0, 0.22)]
    for i in range(3):
        a, b = tri[i], tri[i + 1]
        tube("tri", [(a[0], y, a[1]), ((a[0] + b[0]) / 2, y, (a[1] + b[1]) / 2), (b[0], y, b[1])], 0.008, em)
    prim("torus", "circle", em, loc=(0, y, -0.002), rot=(math.pi / 2, 0, 0), major_radius=0.105, minor_radius=0.008)
    tube("line", [(0, y, 0.22), (0, y, 0.05), (0, y, -0.11)], 0.008, em)
    frame_and_render(Items_Stone, azim=-10, elev=10)


def item_cloak():
    cl = mat("cloak", (0.16, 0.18, 0.26), 0.4, noise=0.45, scale=4, sheen=0.35, emit=(0.35, 0.45, 0.7), strength=0.15)
    trim = mat("trim", SILVER, 0.3, 1.0)
    for i in range(3):
        o = prim("cube", "fold%d" % i, cl, loc=(0.02 * i, -0.01 * i, 0.09 * i), rot=(0, 0, math.radians(4 - 5 * i)), scale=(0.48 - 0.04 * i, 0.34 - 0.03 * i, 0.045),
                 bevel=0.035)
        o.modifiers.new("s", "SUBSURF").levels = 2
        d = o.modifiers.new("d", "DISPLACE")
        tx = bpy.data.textures.new("w%d" % i, "WOOD")
        tx.wood_type = "BANDNOISE"
        tx.noise_scale = 0.18
        tx.turbulence = 4
        d.texture = tx
        d.strength = 0.03
        prim("cube", "edge%d" % i, trim, loc=(0.02 * i, -0.01 * i - (0.34 - 0.03 * i), 0.09 * i), rot=(0, 0, math.radians(4 - 5 * i)),
             scale=(0.47 - 0.04 * i, 0.012, 0.03), bevel=0.008)
    prim("torus", "clasp", gold(), loc=(0.04, -0.3, 0.26), rot=(math.radians(70), 0, 0), major_radius=0.07, minor_radius=0.016)
    frame_and_render(Items_Cloak, azim=-25, elev=34)


def item_potion(key, color):
    lathe("bottle", [(0.0, 0.0), (0.14, 0.0), (0.26, 0.05), (0.32, 0.18), (0.3, 0.32), (0.2, 0.42), (0.09, 0.5), (0.08, 0.66), (0.11, 0.7), (0.0, 0.7)],
          glass("gl"))
    liquid = mat("liq", color, 0.15, emit=color, strength=0.5, trans=0.15)
    lathe("liquid", [(0.0, 0.02), (0.13, 0.02), (0.24, 0.065), (0.295, 0.18), (0.28, 0.3), (0.0, 0.3)], liquid)
    prim("cylinder", "cork", wood("cork", (0.6, 0.45, 0.3), 9), loc=(0, 0, 0.74), scale=(0.09, 0.09, 0.07), vertices=24)
    prim("torus", "string", mat("str", (0.75, 0.6, 0.35), 0.8), loc=(0, 0, 0.6), major_radius=0.1, minor_radius=0.012)
    frame_and_render(key, azim=-15, elev=20)


def feather(key, color, glow=None):
    pts = []
    for i in range(21):
        t = i / 20
        wdt = math.sin(t * math.pi) ** 0.7 * 0.16 * (1 - 0.3 * t)
        pts.append((wdt, t))
    outline = [(x, y) for x, y in pts] + [(-x, y) for x, y in reversed(pts)]
    vane = stripes("vane", color, [c * 0.55 for c in color], 140)
    if glow:
        vane.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (*glow, 1)
        vane.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 0.8
    o = plate("vane", [(x * 1.5, y - 0.05) for x, y in outline], 0.006, vane, rot=(math.pi / 2, 0, 0))
    s = tube("shaft", [(0, -0.008, -0.25), (0, -0.008, 0.4), (0, -0.008, 0.95)], 0.012, mat("shaft", (0.92, 0.9, 0.82), 0.4))
    for ob in (o, s):
        ob.rotation_euler.y += math.radians(-35)
    frame_and_render(key, azim=0, elev=4)


def item_core(key):
    k = "WZ_Core_" + key
    if key == "feather":
        feather(k, (0.15, 0.15, 0.18))
    elif key == "celestial":
        feather(k, (1.0, 0.62, 0.12), glow=(1.0, 0.55, 0.1))
    elif key == "antler":
        bone = mat("bone", (0.78, 0.72, 0.6), 0.6, noise=0.2, scale=12, bump=0.3)
        tube("beam", [(0, 0, 0), (0.1, 0, 0.3), (0.05, 0, 0.6), (0.2, 0, 0.9)], 0.05, bone, taper=[1, 0.9, 0.7, 0.3])
        tube("tine1", [(0.08, 0, 0.3), (-0.12, 0, 0.45), (-0.22, 0, 0.62)], 0.035, bone, taper=[1, 0.6, 0.2])
        tube("tine2", [(0.07, 0, 0.6), (-0.06, 0, 0.78), (-0.1, 0, 0.92)], 0.03, bone, taper=[1, 0.6, 0.2])
        prim("uv_sphere", "spark", mat("sp", (0.55, 0.75, 1.0), emit=(0.55, 0.75, 1.0), strength=2.5), loc=(0.2, 0, 0.9), radius=0.025)
        frame_and_render(k, azim=-5, elev=10)
    elif key == "fenris":
        hair = mat("hair", (0.75, 0.75, 0.8), 0.45, sheen=1.0)
        for i in range(14):
            a = i / 14 * math.tau
            r = 0.04 + 0.02 * math.sin(i * 1.7)
            tube("h", [(r * math.cos(a), r * math.sin(a), 0), (r * 1.5 * math.cos(a), r * 1.5 * math.sin(a), 0.4),
                       (r * 2.5 * math.cos(a + 0.4), r * 2.5 * math.sin(a + 0.4), 0.85)], 0.008, hair, taper=[1, 0.8, 0.2])
        prim("torus", "band", mat("band", (0.35, 0.2, 0.1), 0.6), loc=(0, 0, 0.18), major_radius=0.065, minor_radius=0.02)
        frame_and_render(k, azim=-20, elev=15)
    elif key == "wishbone":
        bone = mat("bone", (0.85, 0.82, 0.72), 0.5, noise=0.15, scale=10)
        tube("l", [(0, 0, 0.5), (-0.15, 0, 0.25), (-0.3, 0, -0.05)], 0.035, bone)
        tube("r", [(0, 0, 0.5), (0.15, 0, 0.25), (0.3, 0, -0.05)], 0.035, bone)
        tube("top", [(0, 0, 0.5), (0, 0, 0.62)], 0.045, bone)
        prim("uv_sphere", "glow", mat("g", (0.3, 1.0, 0.4), emit=(0.3, 1.0, 0.4), strength=1.8), loc=(0, 0, 0.66), radius=0.05)
        frame_and_render(k, azim=0, elev=10)
    elif key == "serpent":
        sc = mat("scale", (0.15, 0.55, 0.55), 0.25, 0.6, noise=0.4, scale=8, coat=1.0)
        plate("scale", [(0, 0.5), (0.28, 0.25), (0.25, -0.15), (0, -0.4), (-0.25, -0.15), (-0.28, 0.25)], 0.05, sc, rot=(math.pi / 2, 0, 0), bevel=0.02)
        frame_and_render(k, azim=-25, elev=25)
    elif key == "dragon":
        col = (1.0, 0.35, 0.1)
        pts = [(0.18 * math.cos(i * 0.7), 0.18 * math.sin(i * 0.7), i * 0.035) for i in range(24)]
        tube("string", pts, 0.022, mat("hs", (0.7, 0.12, 0.04), 0.3, emit=col, strength=1.2))
        frame_and_render(k, azim=-20, elev=30)
    elif key == "troll":
        hide = mat("hide", (0.32, 0.42, 0.6), 0.7, noise=0.35, scale=6, bump=0.5)
        o = prim("grid", "hide", hide, x_subdivisions=24, y_subdivisions=24, size=1.0)
        o.rotation_euler = (math.radians(70), 0, 0)
        d = o.modifiers.new("d", "DISPLACE")
        tx = bpy.data.textures.new("c", "CLOUDS")
        tx.noise_scale = 0.35
        d.texture = tx
        d.strength = 0.15
        o.modifiers.new("t", "SOLIDIFY").thickness = 0.03
        frame_and_render(k, azim=-15, elev=35)
    elif key == "morgen":
        h = mat("heart", (0.45, 0.04, 0.06), 0.25, noise=0.4, scale=10, bump=0.5, coat=0.8, emit=(0.6, 0.0, 0.05), strength=0.6)
        prim("uv_sphere", "a", h, loc=(-0.12, 0, 0.12), radius=0.2)
        prim("uv_sphere", "b", h, loc=(0.12, 0, 0.12), radius=0.2)
        c = prim("cone", "c", h, loc=(0, 0, -0.15), rot=(math.pi, 0, 0), radius1=0.3, depth=0.42, vertices=32)
        c.scale = (1, 0.7, 1)
        tube("vein", [(0.0, 0.0, 0.3), (0.05, 0, 0.45), (0.12, 0, 0.5)], 0.035, h)
        frame_and_render(k, azim=-15, elev=15)
    elif key == "ice":
        ice = mat("ice", (0.35, 0.65, 1.0), 0.12, trans=0.35, emit=(0.4, 0.7, 1.0), strength=0.25)
        for i, (x, z, s, a) in enumerate([(0, 0, 1.0, 0), (0.15, -0.05, 0.7, 20), (-0.14, -0.04, 0.75, -18), (0.05, -0.08, 0.5, 40)]):
            o = prim("cone", "c%d" % i, ice, loc=(x, 0, z + 0.2 * s), rot=(0, math.radians(a), 0), radius1=0.09 * s, depth=0.45 * s, vertices=6)
            prim("cone", "d%d" % i, ice, loc=(x - math.sin(math.radians(a)) * 0.0, 0, z - 0.02 * s), rot=(math.pi, math.radians(a), 0),
                 radius1=0.09 * s, depth=0.08 * s, vertices=6)
        frame_and_render(k, azim=-20, elev=15)


# ============================================================================================================ Codex extensions
def ext_stand():
    wd = wood("wood", (0.5, 0.3, 0.16))
    prim("cube", "base", wd, loc=(0, 0, 0.05), scale=(0.34, 0.16, 0.05), bevel=0.01)
    prim("cube", "back", wd, loc=(0, 0.12, 0.32), scale=(0.34, 0.03, 0.3), bevel=0.01)
    prim("cube", "rail", wd, loc=(0, 0.04, 0.42), scale=(0.34, 0.06, 0.02), bevel=0.005)
    for i, tint in enumerate([(0.95, 0.85, 0.72), (0.55, 0.6, 0.45), (0.82, 0.82, 0.8), (0.45, 0.38, 0.35)]):
        wnd = wand_mesh("w%d" % i, mat("wm%d" % i, tint, 0.5, noise=0.3, scale=20), 0.55)
        wnd.location = (-0.24 + i * 0.16, 0.04, 0.1)
        wnd.rotation_euler = (math.radians(-8), math.radians(-6 + i * 4), 0)
    gm = gold()
    for x in (-0.3, 0.3):
        prim("uv_sphere", "finial", gm, loc=(x, 0.12, 0.64), radius=0.035)


def ext_cauldron():
    iron = mat("iron", IRON, 0.45, 0.85, noise=0.25, scale=10, bump=0.2)
    lathe("pot", [(0.0, 0.08), (0.2, 0.08), (0.36, 0.18), (0.42, 0.36), (0.4, 0.52), (0.34, 0.6), (0.37, 0.64), (0.34, 0.65),
                  (0.32, 0.6), (0.0, 0.6)], iron)
    for i in range(3):
        a = i * math.tau / 3
        tube("leg", [(0.25 * math.cos(a), 0.25 * math.sin(a), 0.15), (0.33 * math.cos(a), 0.33 * math.sin(a), 0.05),
                     (0.36 * math.cos(a), 0.36 * math.sin(a), 0.0)], 0.03, iron)
    brew = (0.3, 1.0, 0.35)
    lathe("brew", [(0.0, 0.58), (0.325, 0.58)], mat("brew", (0.1, 0.5, 0.15), 0.1, emit=brew, strength=1.6))
    for i, (x, y, r) in enumerate([(0.1, 0.05, 0.035), (-0.12, 0.1, 0.025), (0.0, -0.15, 0.03), (0.17, -0.1, 0.02)]):
        prim("uv_sphere", "bub%d" % i, mat("bub", brew, 0.1, emit=brew, strength=2.0), loc=(x, y, 0.59), radius=r)
    for s in (-1, 1):
        prim("torus", "handle", iron, loc=(s * 0.42, 0, 0.55), rot=(0, math.pi / 2, 0), major_radius=0.06, minor_radius=0.012)


def ext_ball():
    br = mat("bronze", BRONZE, 0.32, 1.0, noise=0.1, scale=20)
    lathe("stand", [(0.0, 0.0), (0.26, 0.0), (0.27, 0.04), (0.18, 0.08), (0.08, 0.14), (0.06, 0.3), (0.12, 0.36), (0.0, 0.36)], br)
    for i in range(3):
        a = i * math.tau / 3
        tube("claw", [(0.1 * math.cos(a), 0.1 * math.sin(a), 0.33), (0.2 * math.cos(a), 0.2 * math.sin(a), 0.42),
                      (0.17 * math.cos(a), 0.17 * math.sin(a), 0.56)], 0.02, br, taper=[1, 0.8, 0.3])
    mist = (0.55, 0.75, 1.0)
    prim("uv_sphere", "ball", mat("ball", (0.2, 0.32, 0.65), 0.05, trans=0.3, emit=mist, strength=0.35, noise=0.6, scale=3),
         loc=(0, 0, 0.56), radius=0.2, segments=48, ring_count=24)


def ext_pensieve():
    st = mat("stone", (0.45, 0.45, 0.47), 0.75, noise=0.35, scale=5, bump=0.4)
    lathe("basin", [(0.0, 0.0), (0.3, 0.0), (0.3, 0.06), (0.14, 0.12), (0.12, 0.5), (0.22, 0.56), (0.48, 0.7), (0.5, 0.8), (0.46, 0.8),
                    (0.42, 0.72), (0.0, 0.68)], st)
    silver = (0.75, 0.85, 1.0)
    lathe("memory", [(0.0, 0.74), (0.44, 0.74)], mat("mem", (0.35, 0.45, 0.6), 0.05, 0.6, emit=silver, strength=0.4, noise=0.6, scale=4))
    gm = mat("rune", (0.6, 0.85, 1.0), emit=(0.6, 0.85, 1.0), strength=2)
    for i in range(10):
        a = i * math.tau / 10
        prim("cube", "rune%d" % i, gm, loc=(0.475 * math.cos(a), 0.475 * math.sin(a), 0.76), rot=(0, 0, a), scale=(0.012, 0.03, 0.012))


def ext_astrolabe():
    br = mat("bronze", BRONZE, 0.3, 1.0, noise=0.1, scale=20)
    gm = gold()
    lathe("base", [(0.0, 0.0), (0.24, 0.0), (0.24, 0.05), (0.1, 0.1), (0.05, 0.45), (0.0, 0.45)], br)
    for i, (rx, ry) in enumerate([(0, 0), (60, 0), (-60, 30)]):
        prim("torus", "ring%d" % i, gm if i == 0 else br, loc=(0, 0, 0.75), rot=(math.radians(rx), math.radians(ry), 0),
             major_radius=0.28 - i * 0.03, minor_radius=0.012)
    prim("uv_sphere", "core", mat("core", (0.3, 0.5, 1.0), 0.2, emit=(0.4, 0.6, 1.0), strength=1.8), loc=(0, 0, 0.75), radius=0.07)
    tube("axis", [(0, 0, 0.42), (0, 0, 0.75), (0, 0, 1.06)], 0.01, gm)


def ext_turner():
    gm = gold()
    st = mat("stone", (0.35, 0.33, 0.36), 0.6, noise=0.3, scale=6)
    lathe("pedestal", [(0.0, 0.0), (0.3, 0.0), (0.3, 0.08), (0.2, 0.14), (0.16, 0.4), (0.24, 0.46), (0.0, 0.46)], st)
    lathe("glass", [(0.0, 0.52), (0.12, 0.52), (0.13, 0.6), (0.03, 0.74), (0.13, 0.88), (0.12, 0.96), (0.0, 0.96)],
          mat("glass", (0.6, 0.72, 0.85), 0.05, trans=0.6, emit=(0.6, 0.75, 0.9), strength=0.12))
    lathe("sand", [(0.0, 0.54), (0.1, 0.54), (0.08, 0.6), (0.0, 0.62)], mat("sand", (1.0, 0.8, 0.4), 0.4, emit=(1.0, 0.75, 0.3), strength=1.4))
    for i, rz in enumerate([0, 90]):
        prim("torus", "ring%d" % i, gm, loc=(0, 0, 0.74), rot=(math.pi / 2, 0, math.radians(rz)), major_radius=0.26 - i * 0.04, minor_radius=0.015)
    for s in (-1, 1):
        prim("cylinder", "post", gm, loc=(s * 0.27, 0, 0.6), scale=(0.015, 0.015, 0.14), vertices=12)


EXTENSIONS = {"stand": ext_stand, "cauldron": ext_cauldron, "ball": ext_ball, "pensieve": ext_pensieve, "astrolabe": ext_astrolabe,
              "turner": ext_turner}


# ============================================================================================================ bake + export
def mesh_arrays(obj):
    """Per-loop vertices (uv seams), Unity space: x, y(up), z with the handedness flip."""
    me = obj.data
    me.calc_loop_triangles()
    uvl = me.uv_layers.active.data
    verts, norms, uvs, tris, cache = [], [], [], [], {}
    for lt in me.loop_triangles:
        idx = []
        for li in lt.loops:
            vi = me.loops[li].vertex_index
            co = me.vertices[vi].co
            no = me.loops[li].normal if lt.use_smooth else lt.normal
            uv = tuple(uvl[li].uv)
            key = (vi, round(uv[0], 5), round(uv[1], 5), round(no.x, 2), round(no.y, 2), round(no.z, 2))
            if key not in cache:
                cache[key] = len(verts)
                verts.append((co.x, co.z, co.y))
                norms.append((no.x, no.z, no.y))
                uvs.append(uv)
            idx.append(cache[key])
        tris += [idx[0], idx[2], idx[1]]
    return verts, norms, uvs, tris


def write_wzm(path, verts, norms, uvs, tris):
    with open(path, "wb") as f:
        f.write(b"WZM1")
        f.write(struct.pack("<ii", len(verts), len(tris)))
        for v in verts:
            f.write(struct.pack("<fff", *v))
        for n in norms:
            f.write(struct.pack("<fff", *n))
        for u in uvs:
            f.write(struct.pack("<ff", *u))
        f.write(struct.pack("<%di" % len(tris), *tris))


def bake_export(name, size=1024):
    objs = meshes()
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.convert(target="MESH")
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006)
    bpy.ops.object.mode_set(mode="OBJECT")
    sc = bpy.context.scene
    sc.cycles.samples = 16
    imgs = {}
    for kind in ("albedo", "emission"):
        img = bpy.data.images.new(name + "_" + kind, size, size, alpha=False)
        imgs[kind] = img
        for slot in obj.material_slots:
            nt = slot.material.node_tree
            node = nt.nodes.get("WZ_BAKE") or nt.nodes.new("ShaderNodeTexImage")
            node.name = "WZ_BAKE"
            node.image = img
            nt.nodes.active = node
        if kind == "albedo":
            bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, margin=6)
        else:
            for slot in obj.material_slots:
                b = slot.material.node_tree.nodes.get("Principled BSDF")
                if b is not None and b.inputs["Emission Strength"].default_value > 0:
                    b.inputs["Emission Strength"].default_value = 1.0
            bpy.ops.object.bake(type="EMIT", margin=6)
    a = imgs["albedo"]
    a.filepath_raw = os.path.join(ASSETS, "ext_" + name + "_albedo.jpg")
    a.file_format = "JPEG"
    sc.render.image_settings.quality = 88
    a.save()
    em = imgs["emission"]
    px = list(em.pixels)
    bright = max(px[i] + px[i + 1] + px[i + 2] for i in range(0, len(px), 4 * 37))
    if bright > 0.05:
        em.filepath_raw = os.path.join(ASSETS, "ext_" + name + "_emission.png")
        em.file_format = "PNG"
        em.save()
    v, n, u, t = mesh_arrays(obj)
    write_wzm(os.path.join(ASSETS, "ext_" + name + ".wzm"), v, n, u, t)
    print("ext", name, len(v), "verts", len(t) // 3, "tris", "emission" if bright > 0.05 else "no emission")


# ============================================================================================================ main
Items_Scroll, Items_Cup, Items_Card, Items_Stone, Items_Cloak = "WZ_Scroll_Obliviate", "WZ_Cup_Pensieve", "WZ_Card", "WZ_ResurrectionStone", "WZ_InvisibilityCloak"
BOOKS = [((0.36, 0.2, 0.09), "I"), ((0.42, 0.06, 0.05), "II"), ((0.08, 0.28, 0.12), "III"), ((0.07, 0.14, 0.4), "IV"), ((0.3, 0.08, 0.36), "V"),
         ((0.12, 0.12, 0.14), "VI"), ((0.55, 0.38, 0.08), "VII")]
POTIONS = {"felix": (1.0, 0.8, 0.15), "polyjuice": (0.42, 0.5, 0.2), "pepperup": (1.0, 0.3, 0.12), "death": (0.35, 0.15, 0.55),
           "strength": (0.85, 0.25, 0.2), "elixir": (0.95, 0.12, 0.2)}
CORES = ["feather", "antler", "fenris", "wishbone", "serpent", "dragon", "troll", "celestial", "morgen", "ice"]
BOSSES = ["eikthyr", "elder", "bonemass", "moder", "yagluth", "queen", "fader", "kall"]


def want(key):
    return ONLY is None or key in ONLY


def job(key, fn, *args):
    if not want(key):
        return
    reset()
    fn(*args)


if __name__ == "__main__":
    for w in WOODS:
        job("wand_" + w, item_wand, w)
    for i, (c, n) in enumerate(BOOKS):
        job("WZ_Book_%d" % (i + 1), item_book, "WZ_Book_%d" % (i + 1), c, "star", n)
    job("WZ_Book_Weasley", item_book, "WZ_Book_Weasley", (0.9, 0.42, 0.1), "w", None, (0.95, 0.85, 0.3))
    job("WZ_Book_Rituals", item_book, "WZ_Book_Rituals", (0.32, 0.36, 0.55), "ring", None, SILVER)
    job("WZ_Book_Grimoire", item_book, "WZ_Book_Grimoire", (0.16, 0.1, 0.18), "crown")
    job("WZ_Book_Practice", item_book, "WZ_Book_Practice", (0.7, 0.62, 0.45), "star", None, BRONZE)
    job(Items_Scroll, item_scroll)
    job(Items_Cup, item_cup)
    job(Items_Card, item_card)
    job("WZ_Memory", item_memory, "WZ_Memory", (0.45, 0.7, 1.0))
    for b in BOSSES:
        job("WZ_Memory_" + b, item_memory, "WZ_Memory_" + b, (1.0, 0.62, 0.15))
    job(Items_Stone, item_stone)
    job(Items_Cloak, item_cloak)
    for k, c in POTIONS.items():
        job("potion_" + k, item_potion, "potion_" + k, c)
    for k in CORES:
        job("WZ_Core_" + k, item_core, k)
    for k, fn in EXTENSIONS.items():
        if not want("ext_" + k):
            continue
        reset()
        fn()
        frame_and_render("ext_" + k, azim=-30, elev=24)
        bake_export(k)
