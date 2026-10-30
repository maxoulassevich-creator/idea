import bpy, bmesh, sys, os, struct, json, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
wand_path, codex_path, out = args
os.makedirs(out, exist_ok=True)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def write_wzm(path, verts, norms, uvs, tris):
    with open(path, 'wb') as f:
        f.write(b'WZM1'); f.write(struct.pack('<ii', len(verts), len(tris)))
        for v in verts: f.write(struct.pack('<fff', *v))
        for n in norms: f.write(struct.pack('<fff', *n))
        for u in uvs: f.write(struct.pack('<ff', *u))
        f.write(struct.pack('<%di' % len(tris), *tris))

def mesh_arrays(obj, xform):
    """Split vertices per loop (uv seams), Unity space: x, y(up), z with handedness flip."""
    me = obj.data
    me.calc_loop_triangles()
    uvl = me.uv_layers.active.data if me.uv_layers.active else None
    verts, norms, uvs, tris, cache = [], [], [], [], {}
    nm = xform.to_3x3().inverted().transposed()
    for lt in me.loop_triangles:
        idx = []
        for li in lt.loops:
            vi = me.loops[li].vertex_index
            co = xform @ me.vertices[vi].co
            no = (nm @ me.loops[li].normal).normalized() if hasattr(me.loops[li], 'normal') else (nm @ me.vertices[vi].normal).normalized()
            uv = tuple(uvl[li].uv) if uvl else (0.0, 0.0)
            key = (vi, round(uv[0], 5), round(uv[1], 5), round(no.x, 2), round(no.y, 2), round(no.z, 2))
            if key not in cache:
                cache[key] = len(verts)
                # blender z-up right-handed -> unity y-up left-handed: (x, z, y)
                verts.append((co.x, co.z, co.y)); norms.append((no.x, no.z, no.y)); uvs.append((uv[0], uv[1]))
            idx.append(cache[key])
        tris += [idx[0], idx[2], idx[1]]  # flip winding for the handedness change
    return verts, norms, uvs, tris

def save_image(img, path, size, fmt='JPEG'):
    im = img.copy(); im.scale(size, size)
    im.filepath_raw = path; im.file_format = fmt; im.save()

# ---------------------------------------------------------------- wand
reset()
bpy.ops.import_scene.gltf(filepath=wand_path)
obj = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = obj; obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
dec = obj.modifiers.new('dec', 'DECIMATE'); dec.ratio = 3200 / max(1, len(obj.data.polygons))
bpy.ops.object.modifier_apply(modifier='dec')
# long axis = x; tip at -x, handle at +x. Map handle end -> y=0, tip -> y=1 (length 1).
xs = [v.co.x for v in obj.data.vertices]; lo, hi = min(xs), max(xs); L = hi - lo
ys = [v.co.y for v in obj.data.vertices]; zs = [v.co.z for v in obj.data.vertices]
cy, cz = (min(ys) + max(ys)) / 2, (min(zs) + max(zs)) / 2
from mathutils import Matrix
# new z (blender up) = (hi - x)/L ; x' = y - cy ; y' = z - cz
M = Matrix(((0, 1 / L, 0, -cy / L), (0, 0, 1 / L, -cz / L), (-1 / L, 0, 0, hi / L), (0, 0, 0, 1)))
v, n, u, t = mesh_arrays(obj, M)
write_wzm(os.path.join(out, 'wand.wzm'), v, n, u, t)
mat = obj.active_material
imgs = {}
for node in mat.node_tree.nodes:
    if node.type == 'TEX_IMAGE' and node.image:
        imgs[node.image.name.lower()] = node.image
for name, img in imgs.items():
    if 'normal' in name: save_image(img, os.path.join(out, 'wand_normal.jpg'), 1024)
    elif 'basecolor' in name or 'base' in name: save_image(img, os.path.join(out, 'wand_albedo.jpg'), 1024)
print('wand', len(v), 'verts', len(t) // 3, 'tris', 'imgs', list(imgs))

# ---------------------------------------------------------------- codex
reset()
bpy.ops.import_scene.fbx(filepath=codex_path)
obj = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = obj; obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
co = [ (obj.matrix_world @ vv.co) for vv in obj.data.vertices ]
minz = min(c.z for c in co); maxz = max(c.z for c in co)
minx = min(c.x for c in co); maxx = max(c.x for c in co); miny = min(c.y for c in co); maxy = max(c.y for c in co)
cx, cyy = (minx + maxx) / 2, (miny + maxy) / 2; D = max(maxx - minx, maxy - miny)
print('codex bounds', minx, maxx, miny, maxy, minz, maxz)
# separate loose parts
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']
H = maxz - minz
crystals, candles, rest = [], [], []
for p in parts:
    pc = [p.matrix_world @ vv.co for vv in p.data.vertices]
    zlo = min(c.z for c in pc); zhi = max(c.z for c in pc)
    w = max(max(c.x for c in pc) - min(c.x for c in pc), max(c.y for c in pc) - min(c.y for c in pc))
    mx = sum(c.x for c in pc) / len(pc); my = sum(c.y for c in pc) / len(pc)
    rr = math.hypot(mx - cx, my - cyy) / (D / 2)
    if (zlo - minz) / H > 0.62:
        crystals.append(p)
    elif (zhi - zlo) / H < 0.35 and w / D < 0.08 and rr > 0.45 and (zlo - minz) / H < 0.35 and (zhi - minz) / H > 0.25:
        candles.append((p, mx, my, zhi))
    else:
        rest.append(p)
print('parts', len(parts), 'crystals', len(crystals), 'candles', len(candles))
S = 1.0 / D
M = Matrix(((S, 0, 0, -cx * S), (0, S, 0, -cyy * S), (0, 0, S, -minz * S), (0, 0, 0, 1)))
def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; return o
base = join(rest + [c[0] for c in candles], 'base')
v, n, u, t = mesh_arrays(base, M @ base.matrix_world)
write_wzm(os.path.join(out, 'codex_base.wzm'), v, n, u, t)
print('codex base', len(v), len(t) // 3)
if crystals:
    cr = join(crystals, 'crystals')
    v2, n2, u2, t2 = mesh_arrays(cr, M @ cr.matrix_world)
    write_wzm(os.path.join(out, 'codex_crystals.wzm'), v2, n2, u2, t2)
    print('crystals', len(v2), len(t2) // 3)
tips = [[(mx - cx) * S, (zhi - minz) * S, (my - cyy) * S] for (_, mx, my, zhi) in candles]
json.dump({'candles': tips}, open(os.path.join(out, 'codex.json'), 'w'))
img = None
for m in bpy.data.materials:
    if m.node_tree:
        for node in m.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image: img = node.image
if img:
    save_image(img, os.path.join(out, 'codex_albedo.png'), 1024, 'PNG')
    # emission: bright green areas
    im = img.copy(); im.scale(1024, 1024)
    px = list(im.pixels)
    for i in range(0, len(px), 4):
        r, g, b = px[i], px[i+1], px[i+2]
        e = max(0.0, min(1.0, (g - max(r, b) - 0.08) * 4.0)) * min(1.0, g * 1.6)
        px[i], px[i+1], px[i+2], px[i+3] = r * e, g * e, b * e, 1.0
    im.pixels = px; im.filepath_raw = os.path.join(out, 'codex_emission.png'); im.file_format = 'PNG'; im.save()
print('candle tips', tips)
