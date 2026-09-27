#!/usr/bin/env python3
"""Generates metal equipment families (bars, tools, weapons, armor + recipes) from ore_tiers.json.

Keeping tier stats in one table makes balancing the progression curve a single-file edit.
Output: data/items/generated/ore_tiers.json (do not hand-edit; CI checks it is up to date).
"""
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
OUT = REPO / "data" / "items" / "generated" / "ore_tiers.json"


# Hardmode metals map their pieces onto the reference game's family names (the reference has no helmet, hammer or
# shortsword for them, so those pieces cover nothing).
REF_PIECES = {"pickaxe": "pickaxe", "axe": "waraxe", "broadsword": "sword", "bow": "repeater",
              "chainmail": "breastplate", "greaves": "leggings", "bar": "bar", "drill": "drill", "chainsaw": "chainsaw"}


def covers(t: dict, piece: str) -> list:
    """Reference concepts an item of this tier covers: explicit per-tier covers, a reference metal family, or the
    same-named reference item (pre-hardmode metals)."""
    if "covers" in t:
        return [t["covers"][piece]] if piece in t["covers"] else []
    if "refMetal" in t:
        return [f"terraria:{t['refMetal']}_{REF_PIECES[piece]}"] if piece in REF_PIECES else []
    return [f"terraria:{t['metal']}_{piece}"]


def label(t: dict, piece: str) -> str:
    """Display name of a piece: the tier's own name for it (Light's Bane, Molten Fury, Cobalt Repeater) or
    "<Metal> <Piece>"."""
    return t.get("pieceNames", {}).get(piece, f"{t['name']} {piece.capitalize()}")


def build(table: dict) -> dict:
    items, recipes = [], []
    for t in table["tiers"]:
        m, n, r, p, v = t["metal"], t["name"], t["rarity"], t["power"], t["value"]
        bar = f"{m}_bar"
        items.append({"id": bar, "name": f"{n} Bar", "category": "material", "maxStack": 9999,
                      "value": v * 3 // 4, "rarity": r, "covers": covers(t, "bar")})
        recipes.append({"id": bar, "output": bar,
                        "inputs": [{"item": t.get("ore", f"{m}_ore"), "count": t["orePerBar"]}] + t.get("extraInputs", []),
                        "stations": [t.get("barStation", "furnace")]})
        craft = [t.get("craftStation", "anvil")]

        def tool(kind, stat_field, dmg_time, bars, wood, extra):
            iid = f"{m}_{kind}"
            dmg, use = dmg_time
            item = {"id": iid, "name": label(t, kind), "category": "tool", "rarity": r,
                    "value": v * bars // 2, "useStyle": "swing", "useTime": use, "useAnimation": use + 6,
                    "autoReuse": True, "damage": dmg, "damageType": "melee", "knockback": 2.0,
                    stat_field: p, "covers": covers(t, kind)}
            item.update(extra)
            items.append(item)
            inputs = [{"item": bar, "count": bars}] + ([{"group": "any_wood", "count": wood}] if wood else [])
            recipes.append({"id": iid, "output": iid, "inputs": inputs, "stations": craft})

        tool("pickaxe", "pickPower", t["pick"], 12, 4, {"mineBrush": 2})
        tool("axe", "axePower", t["axe"], 9, 3, {})
        tool("hammer", "hammerPower", t["hammer"], 10, 3, {"knockback": 5.5})
        if "refMetal" in t:
            # Hardmode metals also make power tools: faster than the pickaxe and waraxe, a little weaker in a fight.
            dmg, use = t["pick"]
            tool("drill", "pickPower", (max(1, dmg - 3), max(4, use - 5)), 15, 0, {"mineBrush": 2, "knockback": 0.5})
            dmg, use = t["axe"]
            tool("chainsaw", "axePower", (max(1, dmg - 8), max(4, use - 10)), 10, 0, {"knockback": 0.5})

        dmg, use, kb = t["broad"]
        items.append({"id": f"{m}_broadsword", "name": label(t, "broadsword"), "category": "weapon", "rarity": r,
                      "value": v * 4, "useStyle": "swing", "useTime": use, "useAnimation": use, "damage": dmg,
                      "damageType": "melee", "knockback": kb, "crit": 0, "covers": covers(t, "broadsword"),
                      "behaviors": [{"type": "meleeSwing", "arc": 160, "length": 3.2}]})
        recipes.append({"id": f"{m}_broadsword", "output": f"{m}_broadsword", "inputs": [{"item": bar, "count": 8}],
                        "stations": craft})

        dmg, use, kb = t["short"]
        items.append({"id": f"{m}_shortsword", "name": label(t, "shortsword"), "category": "weapon", "rarity": r,
                      "value": v * 3, "useStyle": "thrust", "useTime": use, "useAnimation": use, "damage": dmg,
                      "damageType": "melee", "knockback": kb, "autoReuse": True,
                      "covers": covers(t, "shortsword"), "behaviors": [{"type": "thrust", "length": 2.4}]})
        recipes.append({"id": f"{m}_shortsword", "output": f"{m}_shortsword", "inputs": [{"item": bar, "count": 7}],
                        "stations": craft})

        dmg, use, speed = t["bow"]
        items.append({"id": f"{m}_bow", "name": label(t, "bow"), "category": "weapon", "rarity": r, "value": v * 3,
                      "useStyle": "shoot", "useTime": use, "useAnimation": use, "damage": dmg,
                      "damageType": "ranged", "knockback": 0.5, "useAmmo": "arrow", "shootSpeed": speed * 3.0,
                      "covers": covers(t, "bow")})
        recipes.append({"id": f"{m}_bow", "output": f"{m}_bow", "inputs": [{"item": bar, "count": 7}],
                        "stations": craft})

        for slot, part, bars, idx in (("head", "helmet", 15, 0), ("body", "chainmail", 25, 1), ("legs", "greaves", 20, 2)):
            iid = f"{m}_{part}"
            items.append({"id": iid, "name": label(t, part), "category": "armor", "rarity": r,
                          "value": v * bars // 2, "equipSlot": slot, "defense": t["armor"][idx],
                          "armorSet": f"{m}_armor", "covers": covers(t, part)})
            recipes.append({"id": iid, "output": iid, "inputs": [{"item": bar, "count": bars}],
                            "stations": craft})
    return {"items": items, "recipes": recipes}


def render(doc: dict) -> str:
    header = "// GENERATED by tools/content/gen_ore_tiers.py from tools/content/ore_tiers.json — do not edit.\n"
    return header + json.dumps(doc, indent=2) + "\n"


def main() -> int:
    table = json.loads((HERE / "ore_tiers.json").read_text(encoding="utf-8"))
    text = render(build(table))
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if current != text:
            print(f"{OUT.relative_to(REPO)} is out of date; run tools/content/gen_ore_tiers.py")
            return 1
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding="utf-8")
    print(f"wrote {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
