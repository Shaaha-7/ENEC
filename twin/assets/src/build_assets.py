"""Qandeel 3D assets, generated from the design specs (PROMPT_3D_MODELS.md). Metres, 1 BU = 1 m.
Run:  blender -b --factory-startup -P assets/src/build_assets.py -- <out_dir>
Writes <out_dir>/workboat.fbx, blower_container.fbx, plant_unit.fbx and a preview render of each.
Materials are named slots (Paint_White, Paint_Orange, ...) that Unity remaps to HDRP materials.
No logos or trademarks. Undefined dimensions are design assumptions (see ASSETS.md)."""
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

OUT = Path(sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "assets/out")
OUT.mkdir(parents=True, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgb, rough=0.5, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1.0); m.roughness = rough; m.metallic = metal
    return m


def assign(obj, m):
    obj.data.materials.clear(); obj.data.materials.append(m)
    return obj


def box(name, size, loc, m, bevel=0.0, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.object; o.name = name; o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        b = o.modifiers.new("bevel", "BEVEL"); b.width = bevel; b.segments = 2
    return assign(o, m)


def cyl(name, r, depth, loc, m, rot=(0, 0, 0), verts=24):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=depth, location=loc, rotation=rot, vertices=verts)
    o = bpy.context.object; o.name = name
    return assign(o, m)


def tube(name, pts, r, m):
    """Pipe along a polyline (railings, A-frame)."""
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"; cu.bevel_depth = r; cu.bevel_resolution = 3
    sp = cu.splines.new("POLY"); sp.points.add(len(pts) - 1)
    for p, c in zip(sp.points, pts):
        p.co = (*c, 1)
    o = bpy.data.objects.new(name, cu); bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.convert(target="MESH")
    return assign(o, m)


def export(name):
    for o in bpy.context.scene.objects:
        if o.type == "MESH":
            bpy.context.view_layer.objects.active = o
            for md in list(o.modifiers):
                bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=str(OUT / f"{name}.fbx"), use_selection=True, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE")
    preview(name)


def preview(name):
    sc = bpy.context.scene
    objs = [o for o in sc.objects if o.type == "MESH"]
    lo = Vector((min(v[i] for o in objs for v in o.bound_box) for i in range(3)))
    hi = Vector((max(v[i] for o in objs for v in o.bound_box) for i in range(3)))
    c, size = (lo + hi) / 2, (hi - lo).length
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = c + Vector((size * 0.9, -size * 1.1, size * 0.6))
    cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 4
    w = bpy.data.worlds.new("w"); sc.world = w; w.color = (0.45, 0.55, 0.65)
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x, sc.render.resolution_y = 960, 600
    sc.render.filepath = str(OUT / f"{name}_preview.png")
    bpy.ops.render.render(write_still=True)


# ------------------------------------------------------------------ workboat (hero), LOA 9.0 m, beam 3.2 m (assumption)
def workboat():
    reset()
    white, orange = mat("Paint_White", (0.9, 0.92, 0.94), 0.35), mat("Paint_Orange", (1, 0.32, 0.03), 0.4)
    steel, glass = mat("Steel", (0.6, 0.62, 0.65), 0.35, 0.9), mat("Glass", (0.03, 0.05, 0.07), 0.05, 0.3)
    rubber, deckm = mat("Rubber", (0.04, 0.04, 0.04), 0.8), mat("Deck", (0.32, 0.34, 0.35), 0.7, 0.5)
    antifoul = mat("Antifoul", (0.45, 0.08, 0.06), 0.7)

    L, B = 9.0, 3.2
    def half_beam(x):  # x: 0 stern .. L bow
        u = x / L
        return B / 2 * (0.97 if u < 0.6 else math.sqrt(max(0.0, 1 - ((u - 0.6) / 0.4) ** 1.6)))
    def sheer(x): return 1.15 + 0.5 * max(0.0, (x / L - 0.3) / 0.7) ** 2
    def keel(x): return -0.55 + 0.45 * max(0.0, (x / L - 0.75) / 0.25) ** 1.5

    bm = bmesh.new(); rings = []
    n = 40
    for i in range(n + 1):
        x = L * i / n; b = half_beam(x); k = keel(x); d = sheer(x)
        sec = [(0, k), (b * 0.55, k + 0.08), (b * 0.92, -0.12), (b, 0.25), (b, d)]  # keel, deadrise, chine, topsides, gunwale
        full = [(-y, z) for (y, z) in reversed(sec)] + sec[1:]
        rings.append([bm.verts.new((x - L / 2, y, z)) for (y, z) in full])
    for a, b in zip(rings, rings[1:]):
        for j in range(len(a) - 1):
            bm.faces.new((a[j], a[j + 1], b[j + 1], b[j]))
    bm.faces.new(list(reversed(rings[0])))  # transom
    me = bpy.data.meshes.new("Hull"); bm.to_mesh(me); bm.free()
    hull = bpy.data.objects.new("Hull", me); bpy.context.collection.objects.link(hull)
    hull.data.materials.append(white); hull.data.materials.append(antifoul)
    for p in hull.data.polygons:  # below the waterline: antifouling
        if p.center.z < 0.02: p.material_index = 1
    sub = hull.modifiers.new("smooth", "SUBSURF"); sub.levels = 1

    # orange sheer band and rubbing strake
    pts = [(L * i / n - L / 2, half_beam(L * i / n) + 0.02, sheer(L * i / n) - 0.3) for i in range(n + 1)]
    tube("Band_S", pts, 0.12, orange); tube("Band_P", [(x, -y, z) for x, y, z in pts], 0.12, orange)
    fend = [(L * i / n - L / 2, half_beam(L * i / n) + 0.1, sheer(L * i / n) - 0.05) for i in range(2, n - 2)]
    tube("Fender_S", fend, 0.11, rubber); tube("Fender_P", [(x, -y, z) for x, y, z in fend], 0.11, rubber)

    box("Deck", (L * 0.86, B * 0.92, 0.06), (-0.3, 0, 1.12), deckm)
    # wheelhouse forward with raked front windows
    box("Wheelhouse", (2.6, 2.3, 1.8), (1.1, 0, 2.05), white, bevel=0.06)
    box("Win_F", (0.04, 2.0, 0.7), (2.42, 0, 2.45), glass, rot=(0, math.radians(-15), 0))
    box("Win_S", (2.1, 0.04, 0.65), (1.1, 1.16, 2.45), glass); box("Win_P", (2.1, 0.04, 0.65), (1.1, -1.16, 2.45), glass)
    box("Roof", (3.0, 2.6, 0.12), (1.2, 0, 3.0), white, bevel=0.03)
    cyl("Mast", 0.05, 1.4, (0.8, 0, 3.75), steel)
    box("Radar", (0.9, 0.15, 0.08), (0.8, 0, 4.2), steel)
    cyl("Searchlight", 0.12, 0.25, (2.1, 0.7, 3.2), steel, rot=(0, math.radians(90), 0))
    cyl("Exhaust", 0.09, 0.9, (-0.3, 0.9, 2.5), steel)
    cyl("Nav_P", 0.07, 0.12, (1.6, -1.25, 3.1), mat("NavRed", (1, 0, 0), 0.2))
    cyl("Nav_S", 0.07, 0.12, (1.6, 1.25, 3.1), mat("NavGreen", (0, 1, 0), 0.2))
    # aft working deck: A-frame, tow bitt, winch
    for s in (-1, 1):
        tube(f"AFrame_{s}", [(-4.1, s * 1.35, 1.15), (-4.3, s * 1.15, 3.1), (-4.3, s * 0.9, 3.4)], 0.08, orange)
    tube("AFrame_Beam", [(-4.3, -0.9, 3.4), (-4.3, 0.9, 3.4)], 0.08, orange)
    cyl("Tow_Bitt", 0.16, 0.5, (-2.9, 0, 1.4), steel); cyl("Tow_Bitt_Cap", 0.24, 0.06, (-2.9, 0, 1.66), steel)
    cyl("Winch_Drum", 0.25, 0.9, (-1.6, 0, 1.45), steel, rot=(math.radians(90), 0, 0))
    box("Winch_Frame", (0.5, 1.1, 0.35), (-1.6, 0, 1.3), orange)
    # railings and stanchions
    for s in (-1, 1):
        rail = [(x, s * (half_beam(x + L / 2) - 0.08), 2.05) for x in (-4.3, -3, -1.5, -0.2)]
        tube(f"Rail_{s}", rail, 0.025, steel)
        for x, y, _ in rail:
            tube(f"Stanchion_{s}_{x}", [(x, y, 1.15), (x, y, 2.05)], 0.02, steel)
    cyl("Liferaft", 0.27, 0.8, (0.2, 0, 3.2), white, rot=(0, math.radians(90), 0))
    for x in (-3.8, 3.4):
        for s in (-1, 1):
            box(f"Cleat_{x}_{s}", (0.3, 0.06, 0.08), (x, s * 1.4, 1.22), steel)
    export("workboat")


# ------------------------------------------------------------------ 40 ft blower container (ISO 12.19 x 2.44 x 2.59 m)
def blower_container():
    reset()
    paint = mat("Container_Paint", (0.82, 0.84, 0.82), 0.55, 0.3)
    dark, steel = mat("Steel_Dark", (0.15, 0.16, 0.17), 0.5, 0.8), mat("Steel", (0.6, 0.62, 0.65), 0.35, 0.9)
    Lc, Wc, Hc = 12.19, 2.44, 2.59
    box("Body", (Lc, Wc - 0.06, Hc - 0.06), (0, 0, Hc / 2), paint)
    for i in range(int(Lc / 0.28)):  # corrugation
        x = -Lc / 2 + 0.2 + i * 0.28
        for s in (-1, 1):
            box(f"Corr_{i}_{s}", (0.12, 0.05, Hc - 0.3), (x, s * (Wc / 2 - 0.01), Hc / 2), paint)
    for x in (-Lc / 2 + 0.09, Lc / 2 - 0.09):  # corner posts and castings
        for s in (-1, 1):
            box(f"Post_{x}_{s}", (0.18, 0.18, Hc), (x, s * (Wc / 2 - 0.09), Hc / 2), dark)
            for z in (0.09, Hc - 0.09):
                box(f"Casting_{x}_{s}_{z}", (0.2, 0.2, 0.18), (x, s * (Wc / 2 - 0.09), z), steel)
    for z in (0.08, Hc - 0.08):
        for s in (-1, 1):
            box(f"Rail_{z}_{s}", (Lc, 0.16, 0.16), (0, s * (Wc / 2 - 0.08), z), dark)
    for k in range(4):  # weatherproof louvres
        x = -4 + k * 2.6
        box(f"Louvre_{k}", (1.4, 0.06, 0.9), (x, Wc / 2 + 0.03, 1.6), dark)
        for j in range(6):
            box(f"Blade_{k}_{j}", (1.35, 0.1, 0.03), (x, Wc / 2 + 0.07, 1.2 + j * 0.15), steel, rot=(math.radians(35), 0, 0))
    box("Service_Door", (1.0, 0.05, 2.0), (4.6, -Wc / 2 - 0.02, 1.1), dark)
    box("HVAC", (1.2, 0.8, 0.5), (-4.5, 0, Hc + 0.25), steel, bevel=0.03)
    cyl("Outlet_Pipe", 0.25, 1.2, (Lc / 2 + 0.4, 0.6, 1.0), steel, rot=(0, math.radians(90), 0))
    cyl("Silencer", 0.4, 1.6, (Lc / 2 + 0.6, -0.5, 1.4), steel)
    box("Cable_Entry", (0.5, 0.3, 0.4), (-Lc / 2 - 0.15, -0.6, 0.4), dark)
    export("blower_container")


# ------------------------------------------------------------------ APR-1400-class unit (illustrative massing, far field)
def plant_unit():
    reset()
    conc, hall = mat("Concrete_Light", (0.86, 0.84, 0.8), 0.6), mat("Hall_Cladding", (0.78, 0.8, 0.82), 0.45, 0.2)
    roof, dark = mat("Roof_Grey", (0.55, 0.57, 0.6), 0.6), mat("Steel_Dark", (0.15, 0.16, 0.17), 0.5, 0.8)
    cyl("Containment", 23, 48, (0, 0, 24), conc, verts=64)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=23, location=(0, 0, 48), segments=64, ring_count=32)
    dome = bpy.context.object; dome.name = "Dome"; dome.scale.z = 0.55; assign(dome, conc)
    bm = bmesh.new(); bm.from_mesh(dome.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.01], context="VERTS"); bm.to_mesh(dome.data); bm.free()
    box("Auxiliary", (70, 40, 34), (0, -45, 17), conc, bevel=0.3)
    box("Aux_Upper", (40, 30, 10), (8, -45, 39), conc, bevel=0.3)
    box("Turbine_Hall", (62, 105, 44), (0, 62, 22), hall, bevel=0.3)
    box("Turbine_Roof", (63, 106, 1.2), (0, 62, 44.6), roof)
    for i in range(10):
        box(f"Hall_Rib_{i}", (0.6, 106, 44), (-31 + i * 6.8, 62, 22), roof)
    box("Transformer_Yard", (40, 20, 6), (45, 85, 3), dark)
    cyl("Stack", 2.5, 70, (40, -30, 35), conc)
    export("plant_unit")


# ------------------------------------------------------------------ curtain: 12 m segment of one 96.7 m section
def curtain_segment():
    """HDPE PE100 OD200 SDR17 pipe, axis 0.75 m above the seabed (z = 0 is the seabed), 2 mm holes ~8.5 cm apart on top,
    clamped to one precast concrete sleeper. Sleeper size is a DESIGN ASSUMPTION sized from ~4.7 t submerged ballast per
    section; sleeper spacing is NOT specified (Unity tiles segments for display only)."""
    reset()
    hdpe, conc = mat("HDPE_Black", (0.03, 0.03, 0.035), 0.55), mat("Sleeper_Concrete", (0.55, 0.54, 0.5), 0.85)
    steel, hole = mat("Steel", (0.6, 0.62, 0.65), 0.35, 0.9), mat("Hole_Dark", (0, 0, 0), 1.0)
    L = 12.0
    cyl("Pipe", 0.1, L, (0, 0, 0.75), hdpe, rot=(0, math.radians(90), 0), verts=32)
    for i in range(int(L / 0.085)):  # 2 mm holes on the crown (drawn 6 mm so they read in close-ups)
        cyl(f"Hole_{i}", 0.003, 0.004, (-L / 2 + 0.04 + i * 0.085, 0, 0.851), hole, verts=6)
    box("Sleeper", (0.5, 1.6, 0.62), (0, 0, 0.31), conc, bevel=0.03)
    box("Saddle", (0.3, 0.32, 0.08), (0, 0, 0.66), conc)
    tube("Clamp", [(0, -0.13, 0.62)] + [(0, 0.12 * math.cos(a), 0.75 + 0.12 * math.sin(a)) for a in
                    [math.radians(d) for d in range(200, -21, -20)]] + [(0, 0.13, 0.62)], 0.012, steel)
    for s in (-1, 1):
        cyl(f"Bolt_{s}", 0.015, 0.12, (0, s * 0.13, 0.64), steel)
    export("curtain_segment")


# ------------------------------------------------------------------ ocean boom, RO-BOOM 2000 class: one 12.5 m piece
def boom_section():
    """Freeboard 0.6 m, skirt 1.1 m below the waterline (z = 0), ballast chain at the skirt foot. Unity strings
    16 pieces (200 m) along the U between the two boats."""
    reset()
    yellow, skirt = mat("Boom_Yellow", (1.0, 0.78, 0.05), 0.45), mat("Boom_Skirt", (0.95, 0.72, 0.05), 0.6)
    chain, joint = mat("Steel_Dark", (0.15, 0.16, 0.17), 0.5, 0.8), mat("Steel", (0.6, 0.62, 0.65), 0.35, 0.9)
    L = 12.5
    for k in range(5):  # inflated float chambers
        x = -L / 2 + 1.25 + k * 2.5
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.45, location=(x, 0, 0.15), segments=24, ring_count=12)
        f = bpy.context.object; f.name = f"Float_{k}"; f.scale = (2.4, 1, 1); assign(f, yellow)
    box("Skirt", (L, 0.03, 1.1), (0, 0, -0.55), skirt)
    tube("Chain", [(-L / 2, 0, -1.12), (L / 2, 0, -1.12)], 0.035, chain)
    box("Connector", (0.12, 0.25, 1.6), (L / 2 - 0.06, 0, -0.25), joint)
    export("boom_section")


# ------------------------------------------------------------------ closed-bottom catch bag
def catch_bag():
    """Tapered sock: mouth 4 m x 3 m, 3 m deep, 3.3 m long (~20 m3), permeable mesh, reinforced rim, lifting points."""
    reset()
    net, rim = mat("Bag_Net", (0.08, 0.2, 0.22), 0.8), mat("Boom_Yellow", (1.0, 0.78, 0.05), 0.45)
    steel = mat("Steel", (0.6, 0.62, 0.65), 0.35, 0.9)
    bm = bmesh.new(); rings = []
    for i in range(9):
        u = i / 8; x = -3.3 * u
        w, h = 2.0 * (1 - 0.75 * u ** 1.4), 1.5 * (1 - 0.7 * u ** 1.4)
        rings.append([bm.verts.new((x, w * math.cos(a), -1.5 + h * math.sin(a) + 1.5 * (1 - u) * 0))
                      for a in [2 * math.pi * k / 24 for k in range(24)]])
    for a, b in zip(rings, rings[1:]):
        for j in range(24):
            bm.faces.new((a[j], a[(j + 1) % 24], b[(j + 1) % 24], b[j]))
    bm.faces.new(list(reversed(rings[-1])))  # closed bottom
    me = bpy.data.meshes.new("Bag"); bm.to_mesh(me); bm.free()
    bag = bpy.data.objects.new("Bag", me); bpy.context.collection.objects.link(bag); assign(bag, net)
    wf = bag.modifiers.new("mesh", "WIREFRAME"); wf.thickness = 0.03; wf.use_replace = False  # visible netting
    tube("Rim", [(0, 2.0 * math.cos(a), -1.5 + 1.5 * math.sin(a)) for a in [2 * math.pi * k / 24 for k in range(25)]], 0.06, rim)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        cyl(f"Lift_{k}", 0.08, 0.15, (0, 2.0 * math.cos(a), -1.5 + 1.5 * math.sin(a)), steel)
    export("catch_bag")


# ------------------------------------------------------------------ lit marker buoy for the release point (design assumption)
def marker_buoy():
    reset()
    orange, white, steel = mat("Paint_Orange", (1, 0.32, 0.03), 0.4), mat("Paint_White", (0.9, 0.92, 0.94), 0.35), mat("Steel", (0.6, 0.62, 0.65), 0.35, 0.9)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.9, location=(0, 0, 0.1), segments=32, ring_count=16)
    b = bpy.context.object; b.name = "Float"; b.scale.z = 0.7; assign(b, orange)
    box("Band", (1.9, 1.9, 0.15), (0, 0, 0.35), white)
    tube("Mast", [(0, 0, 0.6), (0, 0, 2.6)], 0.05, steel)
    for k in range(3):
        tube(f"Strut_{k}", [(0.7 * math.cos(k * 2.09), 0.7 * math.sin(k * 2.09), 0.55), (0, 0, 2.0)], 0.03, steel)
    bpy.ops.mesh.primitive_cone_add(radius1=0.3, depth=0.5, location=(0, 0, 2.95))
    assign(bpy.context.object, orange); bpy.context.object.name = "Topmark"
    cyl("Lantern", 0.09, 0.2, (0, 0, 2.62), mat("Lantern", (1, 0.85, 0.3), 0.1))
    export("marker_buoy")


workboat()
blower_container()
plant_unit()
curtain_segment()
boom_section()
catch_bag()
marker_buoy()
print("QANDEEL_ASSETS_OK", OUT)
