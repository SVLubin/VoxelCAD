#!/usr/bin/env python3
"""Writes the CLI script that builds the castle sample, assets/samples/castle.vcad.

The sample is generated rather than edited by hand, so a change to it is a change
here followed by a rebuild:

    python3 -I tools/samples/castle.py > tmp/castle.txt
    dotnet run --project tools/VoxelCAD.Cli -- --script tmp/castle.txt \\
        --out assets/samples/castle.vcad

It is built from the shipped materials plus four of the project's own, each made
from a ready preset, and it is laid out to show what the editor does: a tree of
named elements, four corner towers that are instances of one prefab turned to
face outward, and a camera so that a render works at once.

Y is up. The scene is 40 x 40 x 40; the default view looks from +X +Z, so the
gatehouse sits in the +Z wall and the keep stands toward the back.
"""
import collections

SIZE = 40
BASE = 3  # first cell above the ground

GRASS = "dense_green_grass_sharp"
MUD = "forest_mud_leaves_smooth"
COBBLE = "old_mossy_cobblestone"
DECK = "old_wood_deck_sharp"
PLINTH = "weathered_fortress_wall_dark_1"
WALL = "ancient_fortress_wall_smooth"
KEEP = "sloppy_grey_stone_bricks_bright"
SLATE = "round_slate_roof_tiles_bright"
CLAY = "interlocking_clay_roof_tiles_smooth"
DOOR = "weathered_medieval_planks_smooth"
IRON = "brushed_iron_sharp"
POLE = "dark_old_wood"
CLOTH = "Banner"
WATER = "Water"
LIGHT = "Window light"
GOLD = "Gold"


def disc(cx, cz, radius):
    reach = int(radius) + 1
    return [(cx + dx, cz + dz)
            for dx in range(-reach, reach + 1)
            for dz in range(-reach, reach + 1)
            if dx * dx + dz * dz <= radius * radius]


class Model:
    def __init__(self):
        self.cells = {}

    def put(self, x, y, z, material):
        self.cells[(x, y, z)] = material

    def clear(self, x, y, z):
        self.cells.pop((x, y, z), None)

    def box(self, x0, y0, z0, x1, y1, z1, material):
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.put(x, y, z, material)


def stone(y):
    return PLINTH if y < BASE + 2 else WALL


# Ground: an island in a moat, a bridge, a cobbled yard.
ground = Model()
moat = set()
for x in range(SIZE):
    for z in range(SIZE):
        in_ring = 2 <= x <= 37 and 2 <= z <= 37 and not (5 <= x <= 34 and 5 <= z <= 34)
        if in_ring:
            moat.add((x, z))
            ground.put(x, 0, z, MUD)
            ground.put(x, 1, z, WATER)
        else:
            for y in range(3):
                ground.put(x, y, z, GRASS)
for x in range(10, 30):
    for z in range(10, 30):
        ground.put(x, 2, z, COBBLE)
for x in range(18, 22):
    for z in range(32, 40):
        ground.put(x, 2, z, DECK if (x, z) in moat else COBBLE)

TOWER_BODY = 3.6
TOWER_CORNERS = [(9, 9), (30, 9), (9, 30), (30, 30)]
GATE_X = range(15, 25)
GATE_Z = range(28, 34)


def under_tower(x, z):
    return any((x - cx) ** 2 + (z - cz) ** 2 <= TOWER_BODY ** 2 for cx, cz in TOWER_CORNERS)


def under_gatehouse(x, z):
    return x in GATE_X and z in GATE_Z


# Curtain walls, crenellated, leaving room for the towers and the gatehouse.
walls = Model()
for x in range(8, 32):
    for z in range(8, 32):
        if 10 <= x <= 29 and 10 <= z <= 29:
            continue
        if under_tower(x, z) or under_gatehouse(x, z):
            continue
        for y in range(BASE, 12):
            walls.put(x, y, z, stone(y))
        outer = x in (8, 31) or z in (8, 31)
        if outer and (x + z) % 2 == 0:
            walls.put(x, 12, z, WALL)

# Gatehouse: two square towers and an arched passage with a barred door.
gate = Model()
for x0 in (15, 22):
    for x in range(x0, x0 + 3):
        for z in range(28, 34):
            for y in range(BASE, 16):
                gate.put(x, y, z, stone(y))
            rim = x in (x0, x0 + 2) or z in (28, 33)
            if rim and (x + z) % 2 == 0:
                gate.put(x, 16, z, WALL)
for x in range(18, 22):
    for z in range(29, 33):
        for y in range(BASE, 12):
            gate.put(x, y, z, stone(y))
    if x % 2 == 0:
        gate.put(x, 12, 32, WALL)
for z in range(29, 33):
    for x in range(18, 22):
        for y in range(BASE, 8):
            gate.clear(x, y, z)
    for x in (19, 20):
        gate.clear(x, 8, z)
for x in range(18, 22):
    for y in range(BASE, 8):
        gate.put(x, y, 30, IRON if y in (4, 6) else DOOR)
for x in (19, 20):
    gate.put(x, 8, 30, DOOR)
for x in (19, 20):
    gate.put(x, 10, 32, LIGHT)

# Keep: a tall block with lit windows, a round turret and a flag.
keep = Model()
TURRET = (22, 12)
TURRET_RADIUS = 2.6
turret_cells = set(disc(*TURRET, TURRET_RADIUS))
for x in range(12, 23):
    for z in range(12, 22):
        if (x, z) in turret_cells:
            continue
        for y in range(BASE, 24):
            keep.put(x, y, z, PLINTH if y < BASE + 2 or y == 13 else KEEP)
        rim = x in (12, 22) or z in (12, 21)
        if rim and (x + z) % 2 == 0:
            keep.put(x, 24, z, KEEP)
for x in (14, 17, 20):
    for y in (15, 16, 19, 20):
        keep.put(x, y, 21, LIGHT)
for z in (14, 19):
    for y in (15, 16, 19, 20):
        keep.put(22, y, z, LIGHT)
for x in range(15, 18):
    for y in range(BASE, 7):
        keep.put(x, y, 21, DOOR)
keep.put(16, 7, 21, DOOR)
for x, z in turret_cells:
    for y in range(BASE, 28):
        keep.put(x, y, z, PLINTH if y < BASE + 2 else KEEP)
for y in range(28, 34):
    radius = 3.0 * (33.5 - y) / 6.0
    for x, z in disc(*TURRET, radius):
        keep.put(x, y, z, CLAY)
keep.put(TURRET[0], 34, TURRET[1], GOLD)
for y in (17, 18, 22, 23):
    keep.put(TURRET[0] + 2, y, TURRET[1], LIGHT)
    keep.put(TURRET[0], y, TURRET[1] + 2, LIGHT)
for y in range(24, 32):
    keep.put(17, y, 16, POLE)
keep.put(17, 32, 16, GOLD)
for x in range(18, 23):
    for y in range(28, 31):
        keep.put(x, y, 16, CLOTH)

# One corner tower, built at the south-east corner; the other three are instances.
tower = Model()
CX, CZ = 30, 30
for x, z in disc(CX, CZ, TOWER_BODY):
    for y in range(BASE, 17):
        tower.put(x, y, z, stone(y))
for x, z in disc(CX, CZ, 4.4):
    tower.put(x, 17, z, WALL)
for y in range(18, 28):
    radius = 4.4 * (27.6 - y) / 9.6
    for x, z in disc(CX, CZ, radius):
        tower.put(x, y, z, SLATE)
tower.put(CX, 28, CZ, GOLD)
for y in (7, 8, 9):
    tower.clear(CX + 3, y, CZ)
    tower.clear(CX, y, CZ + 3)
for y in (12, 13):
    tower.put(CX + 3, y, CZ, LIGHT)
    tower.put(CX, y, CZ + 3, LIGHT)


def pivot(model):
    xs, ys, zs = zip(*model.cells)
    return tuple((min(v) + max(v) + 1) / 2 for v in (xs, ys, zs))


def fmt(v):
    return f"{v:g}"


def voxels(name, model):
    by_material = collections.defaultdict(list)
    for (x, y, z), material in sorted(model.cells.items()):
        by_material[material].append(f"{x},{y},{z}")
    for material, cells in by_material.items():
        print(f'set-voxels element="{name}" material="{material}" cells={";".join(cells)}')


print("new size=40,40,40")
print('rename-scene scene=Scene name="Castle"')
for name, preset, color in ((WATER, "Water", None), (LIGHT, "Lamp", None), (GOLD, "Gold", None)):
    print(f'create-material name="{name}" color=1,1,1')
    print(f'set-material-preset material="{name}" preset={preset}')
print(f'create-material name="{CLOTH}" color=1,1,1')
print(f'set-material-preset material="{CLOTH}" preset=Painted')
print(f'set-material-color material="{CLOTH}" color=0.72,0.1,0.08')
print('create-element name="Castle"')
for name in ("Ground", "Walls", "Gatehouse", "Keep"):
    print(f'create-element name="{name}" kind=VoxelModel parent="Castle"')
print('create-element name="Towers" parent="Castle"')
print('create-element name="Tower" kind=VoxelModel parent="Towers"')
voxels("Ground", ground)
voxels("Walls", walls)
voxels("Gatehouse", gate)
voxels("Keep", keep)
voxels("Tower", tower)
for name in ("Ground", "Walls", "Gatehouse", "Keep", "Tower"):
    print(f'set-pivot element="{name}" point=center')
print('create-prefab element="Tower" name="Tower"')
print('rename-element element="Tower" name="Tower SE"')
print('delete-material material="Default"')
px, py, pz = pivot(tower)
for name, (cx, cz), turn in (("Tower NE", (30, 9), 90), ("Tower NW", (9, 9), 180),
                             ("Tower SW", (9, 30), 270)):
    print(f'instantiate-prefab prefab="Tower" parent="Towers" name="{name}"')
    print(f'set-transform element="{name}" channel=Position '
          f'value={fmt(px + cx - CX)},{fmt(py)},{fmt(pz + cz - CZ)}')
    print(f'set-transform element="{name}" channel=Rotation value=0,{turn},0')
print('create-element name="Camera" kind=Camera')
print('set-transform element="Camera" channel=Position value=44,34,72')
print('aim-camera camera="Camera" at=19,11,19')
print("materials")
print("tree")
