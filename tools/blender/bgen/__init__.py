"""Blender generators for Strata's models (need Blender's Python or the bpy module).

Generator registry: a new generator module registers its functions with @generator instead of being added to
build_all.py's dispatch. build_all.py (and preview.py) call load_all(), which imports every module of this package so
the registrations run, and consult GENERATORS alongside the older per-module GENERATORS dicts.

    from . import generator

    @generator                    # registered as "chandelier"
    def chandelier(mb, params, rng): ...

    @generator("gem_lamp")        # registered under an explicit name
    def lamp(mb, params, rng): ...

Object and item generators build into the MeshBuilder; creature generators also return their rig description, just
like the entries of the per-module dicts. A name registered twice, or one the older dicts already use, is an error.
"""
import importlib
import pkgutil

GENERATORS = {}


def generator(name=None):
    """Registers fn(mb, params, rng) in GENERATORS under `name` (default: the function's name)."""
    def register(fn, key=None):
        key = key or fn.__name__
        if key in GENERATORS:
            raise ValueError(f"generator '{key}' is registered twice ({GENERATORS[key].__module__} and {fn.__module__})")
        GENERATORS[key] = fn
        return fn
    if callable(name):  # bare @generator
        return register(name)
    return lambda fn: register(fn, name)


def load_all():
    """Imports every module and subpackage of bgen (sorted by name) so their @generator calls run; returns GENERATORS."""
    for info in pkgutil.walk_packages(__path__, __name__ + "."):
        importlib.import_module(info.name)
    return GENERATORS
