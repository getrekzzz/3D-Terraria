"""Held-item sockets: named attachment points exported as empties next to an item's mesh, so the game knows where the
hand grips an item and where its muzzle, tip, bow string, yoyo string or flail chain starts.

Sockets live in Blender space (Z up, metres) while building; the glTF exporter turns them into Y-up nodes. Each empty
is oriented so that, in Godot, its basis -Z is the socket's ``forward`` and +Y its ``up``.

Precedence: an asset's ``params.sockets`` (assets.json) beats what the generator set with ``mb.socket``, which beats
the per-generator defaults below.
"""
from mathutils import Matrix, Vector

# Every item: the grip at the origin (items are modelled with the grip there, business end along +Z).
# "tip": the +Z end of the mesh, for generators whose business end is the far end along the item's axis.
TIP_GENERATORS = {"sword", "shortsword", "pickaxe", "axe", "hammer", "spear", "wand", "staff", "rod", "drill", "chainsaw",
                  "arrow", "knife", "red_wrench", "wire_cutter", "bug_net", "paintbrush", "paint_scraper", "paint_sprayer"}
# Generators that are not held in a hand (worn armour, materials, consumables) get no sockets at all.
UNHELD = {"helmet", "chest_armor", "greaves", "boots", "bar", "gem", "blob", "chunk", "coin", "essence", "lens", "seed",
          "potion", "heart", "star", "feather", "fish", "crate", "ring", "wings", "balloon", "horseshoe", "crown",
          "eye_lens", "doll", "bottled", "spike", "bone", "sigil", "critter", "small_mushroom", "spool", "wire_coil",
          "clockwork", "minecart", "bullet"}


def resolve(generator: str, mb, params, lo: Vector, hi: Vector, held=True) -> dict:
    """The final sockets of a model: name -> (pos, forward, up), all Blender-space tuples. ``held`` False (projectile
    models, tile objects) skips the per-generator defaults."""
    out = {}
    if held and generator not in UNHELD:
        out["grip"] = ((0.0, 0.0, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0))
        if generator in TIP_GENERATORS:
            out["tip"] = ((0.0, 0.0, hi.z), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0))
    out.update(mb.sockets)
    for name, value in (params.get("sockets") or {}).items():
        out[name] = _parse(name, value)
    return out


def _parse(name, value):
    """assets.json override: [x, y, z] or {"pos": [...], "forward": [...], "up": [...]} (Blender space)."""
    if isinstance(value, list):
        return (tuple(value), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0))
    if isinstance(value, dict) and "pos" in value:
        return (tuple(value["pos"]), tuple(value.get("forward", (0, 0, 1))), tuple(value.get("up", (0, 1, 0))))
    raise ValueError(f"socket '{name}' must be [x, y, z] or {{pos, forward, up}}")


def matrix(pos, forward, up) -> Matrix:
    """Empty matrix: Blender local +Y = forward and +Z = up, which the Y-up export turns into Godot -Z and +Y."""
    f = Vector(forward).normalized()
    u = Vector(up)
    u = (u - f * u.dot(f))
    if u.length < 1e-6:
        u = f.orthogonal()
    u.normalize()
    x = f.cross(u)
    m = Matrix((x, f, u)).transposed().to_4x4()
    m.translation = Vector(pos)
    return m


def empties(obj, sockets: dict) -> list:
    """Creates one empty per socket, parented to ``obj`` (named exactly like the socket)."""
    import bpy
    result = []
    for name, (pos, forward, up) in sorted(sockets.items()):
        empty = bpy.data.objects.new(name, None)
        empty.empty_display_size = 0.02
        bpy.context.scene.collection.objects.link(empty)
        empty.parent = obj
        empty.matrix_world = obj.matrix_world @ matrix(pos, forward, up)
        result.append(empty)
    return result


def y_up(sockets: dict) -> dict:
    """Sockets in the game's frame (Y up, metres) for assets/manifest.json, as "x y z > forward": Blender (x, y, z) ->
    (x, z, -y)."""
    conv = lambda v: " ".join(f"{round(c, 4) + 0.0:g}" for c in (v[0], v[2], -v[1]))
    return {name: f"{conv(p)} > {conv(f)}" for name, (p, f, _u) in sorted(sockets.items())}
