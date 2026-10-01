"""Blender materials for generated assets.

Slot names encode everything the game needs: ``prop:<layer>`` plus flags ``:glow`` (emissive), ``:clear``
(translucent, vertex alpha scales it) and ``:metal`` (tinted metal: the layer is read as neutral grey normalised to
METAL_TARGET, so the vertex colour is the metal's colour), and ``card:<layer>`` for
alpha-tested leaf cards. The game replaces them with
its shared prop shader (texture layer from assets/textures/props/layers.json, tinted by vertex colour, optional
emission). For icon rendering the same look is rebuilt here with box-projected slices of the props atlas.
"""
import json
import pathlib

import bpy
import numpy as np

PREFIX = "prop:"
# Mean sRGB brightness a ``:metal`` slot's layer is normalised to (the game's PropLibrary uses the same target).
METAL_TARGET = 0.85
# Leaf cards: UV-mapped, alpha-tested slices of the same atlas (tree crowns, bushes).
CARD_PREFIX = "card:"


class PropMaterials:
    def __init__(self, repo: pathlib.Path):
        folder = repo / "assets" / "textures" / "props"
        meta = json.loads((folder / "layers.json").read_text(encoding="utf-8"))
        self.columns = meta["columns"]
        self.size = meta["size"]
        self.layers = {layer["name"]: layer["index"] for layer in meta["layers"]}
        self.types = {layer["name"]: layer["type"] for layer in meta["layers"]}
        self.means = {layer["name"]: layer.get("mean", 0.0) for layer in meta["layers"]}
        self.atlas_path = folder / "props_albedo.png"
        self._atlas = None
        self._images: dict[str, bpy.types.Image] = {}
        self._materials: dict[str, bpy.types.Material] = {}

    @staticmethod
    def parse(slot: str) -> tuple[str, bool]:
        prefix = next((p for p in (PREFIX, CARD_PREFIX) if slot.startswith(p)), None)
        if prefix is None:
            raise ValueError(f"material slot '{slot}' must start with '{PREFIX}' or '{CARD_PREFIX}'")
        parts = slot[len(prefix):].split(":")
        return parts[0], "glow" in parts[1:]

    def validate(self, slot: str):
        layer, _ = self.parse(slot)
        if layer not in self.layers:
            raise KeyError(f"unknown prop texture layer '{layer}' (known: {', '.join(sorted(self.layers))})")

    def material(self, slot: str) -> bpy.types.Material:
        """Material for a slot name (``prop:planks``); validates the layer exists."""
        if slot in self._materials:
            return self._materials[slot]
        self.validate(slot)
        layer, glow = self.parse(slot)
        neutral = ":metal" in slot[len(PREFIX):] and self.means.get(layer, 0) > 0.01
        mat = bpy.data.materials.new(slot)
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        nodes.clear()
        out = nodes.new("ShaderNodeOutputMaterial")
        bsdf = nodes.new("ShaderNodeBsdfPrincipled")
        vcol = nodes.new("ShaderNodeVertexColor")
        vcol.layer_name = "Color"
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = self._layer_image(layer, neutral)
        coords = nodes.new("ShaderNodeTexCoord")
        if slot.startswith(CARD_PREFIX):
            links.new(coords.outputs["UV"], tex.inputs["Vector"])
            links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        else:
            tex.projection = "BOX"
            tex.projection_blend = 0.3
            mapping = nodes.new("ShaderNodeMapping")
            mapping.inputs["Scale"].default_value = (1.0, 1.0, 1.0)  # one texture tile per metre, like the terrain
            links.new(coords.outputs["Object"], mapping.inputs["Vector"])
            links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        links.new(tex.outputs["Color"], mix.inputs["A"])
        links.new(vcol.outputs["Color"], mix.inputs["B"])
        links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.7
        if self.types.get(layer) == "metal":  # icons shine like the in-game metal (kept moderate: a flat grey world)
            bsdf.inputs["Metallic"].default_value = 0.25
            bsdf.inputs["Roughness"].default_value = 0.35
        if glow:
            links.new(vcol.outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = 1.1
        links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        self._materials[slot] = mat
        return mat

    def _layer_image(self, layer: str, neutral=False) -> bpy.types.Image:
        key = layer + (":metal" if neutral else "")
        if key in self._images:
            return self._images[key]
        if self._atlas is None:
            atlas = bpy.data.images.load(str(self.atlas_path), check_existing=True)
            w, h = atlas.size
            px = np.empty(w * h * 4, np.float32)
            atlas.pixels.foreach_get(px)
            self._atlas = px.reshape(h, w, 4)  # Blender stores rows bottom-up
        index = self.layers[layer]
        row, col = divmod(index, self.columns)
        s = self.size
        h = self._atlas.shape[0]
        top = h - (row + 1) * s  # convert top-down atlas row to bottom-up pixel rows
        tile = np.ascontiguousarray(self._atlas[top:top + s, col * s:(col + 1) * s, :])
        if neutral:
            lum = np.clip(tile[..., :3] @ np.array([0.2126, 0.7152, 0.0722], np.float32) * (METAL_TARGET / self.means[layer]), 0, 1)
            tile = np.ascontiguousarray(np.dstack([lum, lum, lum, tile[..., 3]]), dtype=np.float32)
        img = bpy.data.images.new(f"layer_{key}", s, s, alpha=True)
        img.pixels.foreach_set(tile.ravel())
        img.alpha_mode = "CHANNEL_PACKED"  # alpha carries other data (cards cut out with it); never darken the colour
        img.pack()
        self._images[key] = img
        return img
